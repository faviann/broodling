using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class CompletionPersistenceTests
{
    private static void Insert(CompletionFixture fixture, string? receipt = null, string? attempt = null,
        string? work = null, string? contract = null, string? run = null)
    {
        using var connection = fixture.Git.State.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO attempt_completions VALUES ($attempt, $work, $contract, $run, $receipt, 'now')";
        command.Parameters.AddWithValue("$attempt", attempt ?? fixture.Attempt.AttemptId);
        command.Parameters.AddWithValue("$work", work ?? fixture.Attempt.WorkUnitId);
        command.Parameters.AddWithValue("$contract", contract ?? fixture.Attempt.ContractRevisionId);
        command.Parameters.AddWithValue("$run", run ?? fixture.Store.FindSubmission(fixture.Attempt.AttemptId)?.RunId ?? "native-run");
        command.Parameters.AddWithValue("$receipt", receipt ?? CompletionFixture.Receipt().GetRawText());
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task SqlRefusesUnjustifiedCurrentnessLossAndForeignBindingsButAcceptsMatchingReceipt()
    {
        using var fixture = new CompletionFixture();
        await Assert.That(() => Insert(fixture)).Throws<SqliteException>();
        await fixture.Dispatch();
        await Assert.That(() => fixture.Git.State.Execute("UPDATE attempts SET is_current = 0")).Throws<SqliteException>();
        var otherWork = fixture.Store.ResolveWorkUnit(WorkReference.Parse("acme/widget", 99));
        // These foreign identities exist: foreign keys alone cannot explain the refusal.
        var otherRevision = fixture.Store.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary("A different request"u8.ToArray())], ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision.ContractRevisionId;
        await Assert.That(() => Insert(fixture, work: otherWork.WorkUnitId)).Throws<SqliteException>();
        await Assert.That(() => Insert(fixture, contract: otherRevision)).Throws<SqliteException>();
        await Assert.That(() => Insert(fixture, attempt: "foreign-attempt")).Throws<SqliteException>();
        await Assert.That(() => Insert(fixture, run: "foreign-run")).Throws<SqliteException>();
        // The same table the application refuses (AttemptCompletionTests), with the accepted revision any valid head.
        foreach (var (name, receipt) in CompletionFixture.RefusedReceipts(fixture.Attempt.B1.CommitOid, new string('b', 40)))
            await Assert.That(() => Insert(fixture, receipt)).Throws<SqliteException>().Because(name);
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        Insert(fixture);
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)!.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(fixture.Store.CurrentAttempt(fixture.Attempt.WorkUnitId)).IsNull();
    }

    [Test]
    public async Task CompletedWorkCannotReadmitThroughApplicationOrSqlAndCompletionCannotBeRewritten()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var completed = await fixture.Wait();
        var newer = fixture.Store.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary("Later request"u8.ToArray())], ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision.ContractRevisionId;
        await Assert.That(() => fixture.Store.AdmitHttpAttempt(newer, fixture.Git.Repository)).Throws<StaleAttempt>();
        var clone = $"""
            INSERT INTO attempts SELECT 'another-attempt', work_unit_id, '{newer}', 1, b1_repository, b1_commit_oid,
                b1_material_sha256, b1_requested_revision, admitted_at FROM attempts
            """;
        await Assert.That(() => fixture.Git.State.Execute(clone)).Throws<SqliteException>();
        foreach (var sql in new[] {
            "UPDATE attempt_completions SET receipt_json = '{}'", "DELETE FROM attempt_completions",
            "INSERT OR REPLACE INTO attempt_completions SELECT * FROM attempt_completions",
            "UPDATE attempts SET is_current = 1" })
            await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();
        await Assert.That(fixture.Store.FindCompletion(completed.AttemptId)).IsEqualTo(completed);
        await Assert.That(fixture.Store.Status(newer).Completions.Count).IsEqualTo(0);
        await Assert.That(fixture.Store.Status(completed.ContractRevisionId).Completions.Single()).IsEqualTo(completed);
    }

    [Test]
    public async Task MultipleHistoricalResultsShareWorkUnitButReadsStayExactAndRunIdentityIsUnique()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var first = await fixture.Wait();
        var second = Guid.CreateVersion7().ToString();
        // Seed a second historical Attempt, bypassing only today's completed/ended admission guards.
        // This is retained-data cardinality evidence, not authorization to reopen completed work.
        using var connection = fixture.Git.State.Connect();
        string guard;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT group_concat(sql, ';') FROM sqlite_schema WHERE name IN ('attempts_no_completed_work', 'attempts_no_abandoned_work')";
            guard = (string)read.ExecuteScalar()!;
        }
        fixture.Git.State.Execute($"""
            DROP TRIGGER attempts_no_completed_work;
            DROP TRIGGER attempts_no_abandoned_work;
            INSERT INTO attempts SELECT 'historical-second', work_unit_id, contract_revision_id, 1,
                b1_repository, b1_commit_oid, b1_material_sha256, b1_requested_revision, admitted_at FROM attempts;
            {guard};
            INSERT INTO native_submissions (attempt_id, submission_key, request_json, state, intended_run_id,
                asset_sha256, binding_json)
                SELECT 'historical-second', 'broodling:http:v1:historical-second',
                json_set(request_json, '$.runId', '{second}', '$.submission.submissionKey', 'broodling:http:v1:historical-second'),
                'prepared', '{second}', asset_sha256, binding_json FROM native_submissions;
            UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = 'historical-second';
            UPDATE native_submissions SET state = 'correlated', run_id = intended_run_id WHERE attempt_id = 'historical-second';
            """);
        await Assert.That(() => Insert(fixture, attempt: "historical-second", run: first.RunId)).Throws<SqliteException>();
        Insert(fixture, CompletionFixture.Receipt("0000").GetRawText(), attempt: "historical-second", run: second);
        using var reopened = fixture.Git.State.Open();
        await Assert.That(reopened.FindCompletion(first.AttemptId)).IsEqualTo(first);
        await Assert.That(await reopened.WaitAsync(first.AttemptId)).IsEqualTo(first);
        await Assert.That(reopened.FindCompletion("historical-second")!.RunId).IsEqualTo(second);
        await Assert.That(reopened.Status(first.ContractRevisionId).Completions.Count).IsEqualTo(2);
        await Assert.That(reopened.FindCompletion("not-retained")).IsNull();
    }
}
