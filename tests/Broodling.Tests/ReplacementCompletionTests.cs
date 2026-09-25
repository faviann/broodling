using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class ReplacementCompletionTests
{
    [Test]
    public async Task SafeRetryCannotReopenCompletedWorkThroughApplicationOrSql()
    {
        using var fixture = new CompletionFixture();
        var store = fixture.Store;
        var original = fixture.Attempt;
        fixture.Prepare();
        await ReplacementTests.SafeRetire(store, original);
        var historicalRun = Guid.CreateVersion7().ToString();

        // Seed retained historical completion beside an unused safe predecessor.
        // Only new ordinary admission is bypassed; restore that guard before exercising retry.
        using (var connection = fixture.Git.State.Connect())
        using (var transaction = connection.BeginTransaction())
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT sql FROM sqlite_schema WHERE name = 'attempts_no_abandoned_work'";
            var guard = (string)command.ExecuteScalar()!;
            command.CommandText = $"""
                DROP TRIGGER attempts_no_abandoned_work;
                INSERT INTO attempts SELECT 'historical-completed', work_unit_id, contract_revision_id, 1,
                    b1_repository, b1_commit_oid, b1_material_sha256, b1_requested_revision, workspace_root,
                    enclosure, worktree_path, branch, admitted_at, resource_kind FROM attempts;
                {guard};
                INSERT INTO native_submissions (attempt_id, format, submission_key, request_json, state, intended_run_id,
                    asset_sha256, binding_json)
                    SELECT 'historical-completed', format, 'broodling:http:v1:historical-completed',
                    json_set(request_json, '$.runId', $run, '$.submission.submissionKey', 'broodling:http:v1:historical-completed'),
                    'prepared', $run, asset_sha256, binding_json FROM native_submissions;
                UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = 'historical-completed';
                UPDATE native_submissions SET state = 'correlated', run_id = $run WHERE attempt_id = 'historical-completed';
                INSERT INTO attempt_completions SELECT attempt_id, work_unit_id, contract_revision_id,
                    $run, $receipt, 'then' FROM attempts WHERE attempt_id = 'historical-completed';
                """;
            command.Parameters.AddWithValue("$receipt", CompletionFixture.Receipt().GetRawText());
            command.Parameters.AddWithValue("$run", historicalRun);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        using var reopened = fixture.Git.State.Open();
        var completion = reopened.FindCompletion("historical-completed")!;
        await Assert.That(() => reopened.AdmitRetry(original.AttemptId, "completed")).Throws<StaleAttempt>();
        await Assert.That(() => fixture.Git.Admit(reopened)).Throws<StaleAttempt>();
        await Assert.That(reopened.FindRetry("completed")).IsNull();

        using (var connection = fixture.Git.State.Connect())
        using (var transaction = connection.BeginTransaction())
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO attempt_retries VALUES ('sql-retry', $predecessor, 'sql-successor', NULL, NULL, 'now')";
            command.Parameters.AddWithValue("$predecessor", original.AttemptId);
            command.ExecuteNonQuery(); // Valid safe lineage: the abandoned-work exception applies.
            command.CommandText = """
                INSERT INTO attempts SELECT 'sql-successor', work_unit_id, contract_revision_id, 1,
                    b1_repository, b1_commit_oid, b1_material_sha256, b1_requested_revision,
                    NULL, NULL, NULL, NULL, 'now', resource_kind
                    FROM attempts WHERE attempt_id = $predecessor
                """;
            SqliteException? refusal = null;
            try { command.ExecuteNonQuery(); }
            catch (SqliteException error) { refusal = error; }
            await Assert.That(refusal).IsNotNull();
            await Assert.That(refusal!.Message.Contains("completed Work Unit cannot acquire new Attempt authority")).IsTrue();
            transaction.Rollback();
        }
        await Assert.That(reopened.FindRetry("sql-retry")).IsNull();
        await Assert.That(reopened.CurrentAttempt(original.WorkUnitId)).IsNull();
        await Assert.That(reopened.FindCompletion(completion.AttemptId)).IsEqualTo(completion);
    }

    [Test]
    public async Task CompletedReplacementKeyReturnsOnlyHistoricalIdentity()
    {
        using var fixture = new CompletionFixture();
        var store = fixture.Store;
        var original = fixture.Attempt;
        await ReplacementTests.SafeRetire(store, original);
        var successor = store.AdmitRetry(original.AttemptId, "completed-key");
        await fixture.Dispatch(successor.AttemptId);
        var completion = await store.WaitAsync(successor.AttemptId, null);
        using var reopened = fixture.Git.State.Open();
        var historical = reopened.AdmitRetry(original.AttemptId, "completed-key");
        await Assert.That(historical.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(historical.IsCurrent).IsFalse();
        await Assert.That(() => reopened.PrepareHttpSubmission(successor.AttemptId, fixture.Origin)).Throws<StaleAttempt>();
        // A correlated record is handed back as a retained fact; nothing is sent.
        await Assert.That((await reopened.DispatchHttpAsync(successor.AttemptId, null)).State).IsEqualTo("correlated");
        await Assert.That(async () => await reopened.StopAsync(successor.AttemptId, "too late", null)).Throws<StaleAttempt>();
        await Assert.That(fixture.Target.Stages.Contains("run")).IsFalse();
        await Assert.That(fixture.Target.Count("run/force")).IsEqualTo(0);
        await Assert.That(() => reopened.RetireAttempt(successor.AttemptId)).Throws<CessationUnconfirmed>();
        await Assert.That(reopened.GetAttempt(successor.AttemptId).Abandonment).IsNull();
        await Assert.That(reopened.Status(original.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(successor.AttemptId);
        await Assert.That(reopened.FindCompletion(successor.AttemptId)).IsEqualTo(completion);
    }
}
