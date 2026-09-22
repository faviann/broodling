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
        store.PrepareSubmission(original.AttemptId, fixture.Profile);
        await ReplacementTests.SafeRetire(store, original);

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
                    enclosure || '-history', worktree_path || '-history', branch || '-history', admitted_at FROM attempts;
                {guard};
                INSERT INTO worktree_provisions VALUES ('historical-completed', 'then');
                INSERT INTO native_submissions SELECT 'historical-completed', 'broodling:dotnet:v1:historical-completed',
                    json_set(request_json, '$.submissionKey', 'broodling:dotnet:v1:historical-completed'), 'prepared', NULL FROM native_submissions;
                UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = 'historical-completed';
                UPDATE native_submissions SET state = 'correlated', run_id = 'historical-run' WHERE attempt_id = 'historical-completed';
                INSERT INTO attempt_completions SELECT attempt_id, work_unit_id, contract_revision_id,
                    'historical-run', $receipt, 'then' FROM attempts WHERE attempt_id = 'historical-completed';
                """;
            command.Parameters.AddWithValue("$receipt", CompletionFixture.Receipt().GetRawText());
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        using var reopened = fixture.Git.State.Open();
        var completion = reopened.FindCompletion("historical-completed")!;
        await Assert.That(() => reopened.AdmitRetry(original.AttemptId, "completed", fixture.Git.Workspaces, fixture.Profile)).Throws<StaleAttempt>();
        await Assert.That(() => fixture.Git.Admit(reopened)).Throws<StaleAttempt>();
        await Assert.That(reopened.FindRetry("completed")).IsNull();

        using (var connection = fixture.Git.State.Connect())
        using (var transaction = connection.BeginTransaction())
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO attempt_retries VALUES ('sql-retry', $predecessor, 'sql-successor', $root, '{}', 'now')";
            command.Parameters.AddWithValue("$predecessor", original.AttemptId);
            command.Parameters.AddWithValue("$root", fixture.Git.Workspaces);
            command.ExecuteNonQuery(); // Valid safe lineage: the abandoned-work exception applies.
            command.CommandText = """
                INSERT INTO attempts SELECT 'sql-successor', work_unit_id, contract_revision_id, 1,
                    b1_repository, b1_commit_oid, b1_material_sha256, b1_requested_revision,
                    $root, $root || '/sql-successor', $root || '/sql-successor/worktree', 'broodling/sql-successor', 'now'
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
        var successor = store.AdmitRetry(original.AttemptId, "completed-key", fixture.Git.Workspaces, fixture.Profile);
        await store.RetryAsync(original.AttemptId, "completed-key", fixture.Git.Workspaces, fixture.Profile,
            fixture.Transport, new("test-token", NativeProfile.GatewayBaseUrl, "test-gateway"));
        var completion = await store.WaitAsync(successor.AttemptId, fixture.Transport);
        using var reopened = fixture.Git.State.Open();
        var historical = reopened.AdmitRetry(original.AttemptId, "completed-key", fixture.Git.Workspaces, fixture.Profile);
        await Assert.That(historical.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(historical.IsCurrent).IsFalse();
        await Assert.That(() => reopened.PrepareRetry(original.AttemptId, "completed-key", fixture.Git.Workspaces, fixture.Profile)).Throws<StaleAttempt>();
        await Assert.That(async () => await reopened.DispatchAsync(successor.AttemptId, fixture.Profile, fixture.Transport)).Throws<StaleAttempt>();
        await Assert.That(async () => await reopened.StopAsync(successor.AttemptId, "too late", fixture.Transport)).Throws<StaleAttempt>();
        await Assert.That(() => reopened.RetireAttempt(successor.AttemptId)).Throws<CessationUnconfirmed>();
        await Assert.That(reopened.GetAttempt(successor.AttemptId).Abandonment).IsNull();
        await Assert.That(reopened.Status(original.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(successor.AttemptId);
        await Assert.That(reopened.FindCompletion(successor.AttemptId)).IsEqualTo(completion);
    }
}
