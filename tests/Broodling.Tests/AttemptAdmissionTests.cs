using Broodling.Host;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class AttemptAdmissionTests
{
    [Test]
    public async Task MalformedAbandonmentReasonCannotWithdrawAuthorityOrRewriteRetainedText()
    {
        using var fixture = new AttemptFixture();
        AttemptRecord attempt;
        using (var store = fixture.State.Open())
        {
            attempt = fixture.Admit(store);
            await Assert.That(() => store.AbandonAttempt(attempt.AttemptId, "reason-\ud800"))
                .Throws<BroodlingException>();
            await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
            await Assert.That(store.GetAttempt(attempt.AttemptId).IsCurrent).IsTrue();
            store.AbandonAttempt(attempt.AttemptId, "reason-\ufffd\U0001f680");
        }
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).Abandonment!.Reason).IsEqualTo("reason-\ufffd\U0001f680");
    }

    [Test]
    public async Task PublicAdmissionRetainsOriginalBindingsAndExactRevisionObservationAcrossReopen()
    {
        using var fixture = new AttemptFixture();
        AttemptRecord attempt;
        string newerRevision;
        using (var store = fixture.State.Open())
        {
            attempt = fixture.Admit(store, "HEAD");
            await Assert.That(attempt.B1.CommitOid).IsEqualTo(fixture.Head);
            await Assert.That(attempt.B1.RequestedRevision).IsEqualTo("HEAD");
            await Assert.That(attempt.B1.Repository).IsEqualTo(fixture.GitDirectory);
            await Assert.That(attempt.B1.MaterialSha256.Length).IsEqualTo(64);
            await Assert.That(fixture.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(fixture.Head);
            var contract = store.GetContractRevision(fixture.RevisionId).Contract;
            newerRevision = store.RecordContractRevision(new(contract.WorkUnitId, contract.SourceAttribution,
                [new("other", "A different task.")], requiredEffects: contract.RequiredEffects)).ContractRevisionId;
            store.Admit(newerRevision);
            await Assert.That(() => store.AdmitHttpAttempt(newerRevision, fixture.Repository, fixture.Head)).Throws<AttemptConflict>();
            await Assert.That(store.Status(newerRevision).Attempts.Count).IsEqualTo(0);
        }
        fixture.Commit("moving HEAD must not change recorded B1\n");
        using var reopened = fixture.State.Open();
        var replay = fixture.Admit(reopened);
        await Assert.That(replay).IsEqualTo(attempt);
        await Assert.That(reopened.GetAttempt(attempt.AttemptId)).IsEqualTo(attempt);
        await Assert.That(reopened.RequireCurrentAttempt(attempt.AttemptId)).IsEqualTo(attempt);
        await Assert.That(reopened.CurrentAttempt(attempt.WorkUnitId)).IsEqualTo(attempt);
        await Assert.That(reopened.History(ContractIngressTests.Reference).Select(s => s.Attempts.Count).ToArray()).IsEquivalentTo(new[] { 1, 0 });
        await Assert.That(() => fixture.Admit(reopened, "HEAD")).Throws<AttemptConflict>();
        var output = new StringWriter();
        await Assert.That(StoreCommands.Run(["status", fixture.State.Path, fixture.RevisionId], fixture.State.Application, output, new StringWriter())).IsEqualTo(0);
        using var json = JsonDocument.Parse(output.ToString());
        await Assert.That(json.RootElement.GetProperty("attempts")[0].GetProperty("b1").GetProperty("commitOid").GetString()).IsEqualTo(fixture.Head);
    }

    [Test]
    public async Task ConcurrentIdenticalRequestsConvergeAndDifferentB1CannotRebind()
    {
        using var fixture = new AttemptFixture();
        using var gate = new Barrier(2);
        Task<AttemptRecord> Submit() => Task.Run(() =>
        {
            using var store = fixture.State.Open();
            gate.SignalAndWait();
            return fixture.Admit(store);
        });
        var attempts = await Task.WhenAll(Submit(), Submit());
        await Assert.That(attempts[0]).IsEqualTo(attempts[1]);
        var newer = fixture.Commit("different commit\n");
        using var reopened = fixture.State.Open();
        await Assert.That(() => fixture.Admit(reopened, newer)).Throws<AttemptConflict>();
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Single()).IsEqualTo(attempts[0]);
    }

    [Test]
    public async Task RacingDifferentAdmissionsCommitExactlyOneBinding()
    {
        using var fixture = new AttemptFixture();
        var newer = fixture.Commit("new competing commit\n");
        using var gate = new Barrier(2);
        Task<AttemptRecord?> Submit(string revision) => Task.Run(() =>
        {
            using var store = fixture.State.Open();
            gate.SignalAndWait();
            try { return fixture.Admit(store, revision); }
            catch (AttemptConflict) { return null; }
        });
        var results = await Task.WhenAll(Submit(fixture.Head), Submit(newer));
        await Assert.That(results.Count(r => r is not null)).IsEqualTo(1);
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Single()).IsEqualTo(results.Single(r => r is not null));
    }

    [Test]
    public async Task InterruptedAllocationLeavesOnlyGitRetentionAndRetryCommitsTheWholeBinding()
    {
        using var fixture = new AttemptFixture();
        using (var store = fixture.State.Open())
        {
            fixture.State.Execute("CREATE TRIGGER interrupt_allocation AFTER INSERT ON attempts BEGIN SELECT RAISE(ABORT, 'allocation interrupted'); END;");
            await Assert.That(() => fixture.Admit(store)).Throws<SqliteException>();
            fixture.State.Execute("DROP TRIGGER interrupt_allocation");
        }
        await Assert.That(fixture.Git("rev-parse", "refs/broodling/starting/" + fixture.Head).Trim()).IsEqualTo(fixture.Head);
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
        var attempt = fixture.Admit(reopened);
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Single()).IsEqualTo(attempt);
    }

    [Test]
    public async Task AbandonmentRollsBackTogetherThenIrreversiblyEndsAuthorityAndKeepsFirstReason()
    {
        using var fixture = new AttemptFixture();
        AttemptRecord attempt;
        AttemptAbandonment abandonment;
        using (var store = fixture.State.Open())
        {
            attempt = fixture.Admit(store);
            fixture.State.Execute("CREATE TRIGGER interrupt_abandonment AFTER UPDATE ON attempts BEGIN SELECT RAISE(ABORT, 'abandonment interrupted'); END;");
            await Assert.That(() => store.AbandonAttempt(attempt.AttemptId, "original reason")).Throws<SqliteException>();
            fixture.State.Execute("DROP TRIGGER interrupt_abandonment");
            await Assert.That(store.RequireCurrentAttempt(attempt.AttemptId)).IsEqualTo(attempt);
            abandonment = store.AbandonAttempt(attempt.AttemptId, "original reason");
        }
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.AbandonAttempt(attempt.AttemptId, "later reason")).IsEqualTo(abandonment);
        await Assert.That(reopened.CurrentAttempt(attempt.WorkUnitId)).IsNull();
        await Assert.That(() => reopened.RequireCurrentAttempt(attempt.AttemptId)).Throws<StaleAttempt>();
        await Assert.That(() => fixture.Admit(reopened)).Throws<StaleAttempt>();
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Single().Abandonment).IsEqualTo(abandonment);
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).B1).IsEqualTo(attempt.B1);
        foreach (var sql in new[] { "UPDATE attempts SET is_current = 1", "DELETE FROM attempt_abandonments", "UPDATE attempt_abandonments SET reason = 'changed'" })
            await Assert.That(() => fixture.State.Execute(sql)).Throws<SqliteException>();
    }

    [Test]
    public async Task StaleCallerRacingAbandonmentCannotResurrectAuthority()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using var gate = new Barrier(2);
        var admission = Task.Run(() =>
        {
            using var caller = fixture.State.Open();
            gate.SignalAndWait();
            try { fixture.Admit(caller); } catch (StaleAttempt) { }
        });
        var abandonment = Task.Run(() =>
        {
            using var caller = fixture.State.Open();
            gate.SignalAndWait();
            caller.AbandonAttempt(attempt.AttemptId, "stop authority");
        });
        await Task.WhenAll(admission, abandonment);
        await Assert.That(store.GetAttempt(attempt.AttemptId).IsCurrent).IsFalse();
        await Assert.That(() => fixture.Admit(store)).Throws<StaleAttempt>();
    }

    [Test]
    public async Task ObservationRemainsCoherentDuringUncommittedAbandonmentAndBindingsAreImmutable()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        using (var writer = fixture.State.Connect())
        using (var transaction = writer.BeginTransaction(deferred: false))
        {
            using var command = writer.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO attempt_abandonments VALUES ($id, 'uncommitted', 'now')";
            command.Parameters.AddWithValue("$id", attempt.AttemptId);
            command.ExecuteNonQuery();
            await Assert.That(store.Status(fixture.RevisionId).Attempts.Single()).IsEqualTo(attempt);
            await Assert.That(store.History(ContractIngressTests.Reference).Single().Attempts.Single()).IsEqualTo(attempt);
        }
        foreach (var column in new[] { "attempt_id", "work_unit_id", "contract_revision_id", "b1_repository", "b1_commit_oid",
            "b1_material_sha256", "b1_requested_revision", "admitted_at" })
            await Assert.That(() => fixture.State.Execute($"UPDATE attempts SET {column} = 'changed'")).Throws<SqliteException>();
        await Assert.That(() => fixture.State.Execute("DELETE FROM attempts")).Throws<SqliteException>();
        await Assert.That(store.RequireCurrentAttempt(attempt.AttemptId)).IsEqualTo(attempt);
    }

    [Test]
    public async Task UndecidedAndRejectedRevisionsNeverAcquireAttempts()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var original = store.GetContractRevision(fixture.RevisionId).Contract;
        var undecided = store.RecordContractRevision(new(original.WorkUnitId, original.SourceAttribution, [new("new", "Undecided")],
            requiredEffects: original.RequiredEffects)).ContractRevisionId;
        var rejected = store.RecordContractRevision(new(original.WorkUnitId, original.SourceAttribution, [new("reject", "Unsupported")],
            requiredEffects: [new("deploy", "Deploy", "deployment")])).ContractRevisionId;
        store.Admit(rejected);
        foreach (var revision in new[] { undecided, rejected })
            await Assert.That(() => store.AdmitHttpAttempt(revision, fixture.Repository)).Throws<AttemptAdmissionError>();
        await Assert.That(store.History(ContractIngressTests.Reference).Sum(s => s.Attempts.Count)).IsEqualTo(0);
    }

    [Test]
    public async Task ChangedSourceCannotRebindOriginalB1Material()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var originalSources = store.Status(fixture.RevisionId).Sources;
        var attempt = fixture.Admit(store);
        var changed = store.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary("new request bytes"u8.ToArray())], ContractIngressTests.Propose, ContractIngressTests.PullRequest);
        await Assert.That(() => store.AdmitHttpAttempt(changed.Revision.ContractRevisionId, fixture.Repository)).Throws<AttemptConflict>();
        await Assert.That(store.Status(fixture.RevisionId).Sources.Single().Content.SequenceEqual(originalSources.Single().Content)).IsTrue();
        await Assert.That(store.GetAttempt(attempt.AttemptId)).IsEqualTo(attempt);
        await Assert.That(changed.Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DatabaseChecksContractOwnershipAndKeepsOneCurrentAttempt()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        var reference = WorkReference.Parse("acme/widget", 13);
        var other = store.AdmitSources(reference,
            [new SourceSubmission("primary_issue", reference.IssueLocator, "other work"u8.ToArray(), entitlement: new("caller", "reviewed"))],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest);
        using var connection = fixture.State.Connect();
        foreach (var insert in new[] { "INSERT", "INSERT OR REPLACE" })
        foreach (var conflict in new[] { "contract", "current" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                {insert} INTO attempts SELECT 'other-attempt', $work, $revision, 1,
                    b1_repository, b1_commit_oid, b1_material_sha256, b1_requested_revision, admitted_at FROM attempts
                """;
            command.Parameters.AddWithValue("$work", conflict == "current" ? attempt.WorkUnitId : other.WorkUnit.WorkUnitId);
            command.Parameters.AddWithValue("$revision", fixture.RevisionId);
            await Assert.That(() => command.ExecuteNonQuery()).Throws<SqliteException>();
        }
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Single()).IsEqualTo(attempt);
        await Assert.That(store.Status(other.Revision.ContractRevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SqlReplacementCannotRebindCurrentAttemptOrRestoreAbandonedIdentityUnderValidParents()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        await Assert.That(() => fixture.State.Execute("""
            INSERT OR REPLACE INTO attempts SELECT attempt_id, work_unit_id, contract_revision_id, is_current,
                b1_repository, 'cccccccccccccccccccccccccccccccccccccccc', b1_material_sha256,
                b1_requested_revision, admitted_at FROM attempts
            """)).Throws<SqliteException>();
        var reference = WorkReference.Parse("acme/widget", 13);
        var other = store.AdmitSources(reference,
            [new SourceSubmission("primary_issue", reference.IssueLocator, "other work"u8.ToArray(), entitlement: new("caller", "reviewed"))],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest);
        store.AbandonAttempt(attempt.AttemptId, "original reason");
        using var connection = fixture.State.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO attempts SELECT attempt_id, $work, $revision, 1,
                b1_repository, b1_commit_oid, b1_material_sha256, b1_requested_revision, admitted_at FROM attempts
            """;
        command.Parameters.AddWithValue("$work", other.WorkUnit.WorkUnitId);
        command.Parameters.AddWithValue("$revision", other.Revision.ContractRevisionId);
        await Assert.That(() => command.ExecuteNonQuery()).Throws<SqliteException>();
        var retained = store.GetAttempt(attempt.AttemptId);
        await Assert.That(retained.B1).IsEqualTo(attempt.B1);
        await Assert.That(retained.ContractRevisionId).IsEqualTo(attempt.ContractRevisionId);
        await Assert.That(retained.IsCurrent).IsFalse();
        await Assert.That(retained.Abandonment!.Reason).IsEqualTo("original reason");
        await Assert.That(store.Status(other.Revision.ContractRevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task HttpAllocationRetainsB1WithoutAnyLocalResourceAcrossReopen()
    {
        using var fixture = new AttemptFixture();
        var local = fixture.LocalResources();
        string revision;
        AttemptRecord attempt;
        using (var store = fixture.State.Open())
        {
            revision = fixture.RevisionId;
            fixture.State.Execute("CREATE TRIGGER interrupt_allocation AFTER INSERT ON attempts BEGIN SELECT RAISE(ABORT, 'allocation interrupted'); END;");
            await Assert.That(() => store.AdmitHttpAttempt(revision, fixture.Repository, "main")).Throws<SqliteException>();
            fixture.State.Execute("DROP TRIGGER interrupt_allocation");
            await Assert.That(fixture.Git("rev-parse", "refs/broodling/starting/" + fixture.Head).Trim()).IsEqualTo(fixture.Head);
            await Assert.That(store.Status(revision).Attempts.Count).IsEqualTo(0);

            attempt = store.AdmitHttpAttempt(revision, fixture.Repository, "main");
            await Assert.That(attempt.B1).IsEqualTo(new OriginalB1(PhysicalPaths.Resolve(fixture.GitDirectory), fixture.Head,
                Digests.AdmittedMaterial(store.GetContractRevision(revision).Contract.SourceAttribution), "main"));
            await Assert.That(store.AdmitHttpAttempt(revision, fixture.Repository, fixture.Head)).IsEqualTo(attempt);
        }
        await Assert.That(fixture.LocalResources()).IsEqualTo(local);
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.Status(revision).Attempts.Single()).IsEqualTo(attempt);
        await Assert.That(reopened.History(ContractIngressTests.Reference).Single(s => s.Revision.ContractRevisionId == revision).Attempts.Single())
            .IsEqualTo(attempt);
        // The retained record names no local resource kind or allocation at all.
        var json = JsonSerializer.SerializeToElement(reopened.GetAttempt(attempt.AttemptId), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var removed in new[] { "resourceKind", "allocation", "worktreeAllocation", "provision" })
            await Assert.That(json.TryGetProperty(removed, out _)).IsFalse();
    }

}
