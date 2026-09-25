using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class ReplacementTests
{
    [Test]
    [Arguments("find")]
    [Arguments("admit")]
    public async Task MalformedRetryKeyCannotReturnLegitimateReplacementCharacterHistory(string operation)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await SafeRetire(store, original);
        var successor = store.AdmitRetry(original.AttemptId, "retry-\ufffd", fixture.Git.Workspaces, fixture.Profile);
        using var reopened = fixture.Git.State.Open();
        if (operation == "find")
            await Assert.That(() => reopened.FindRetry("retry-\ud800")).Throws<BroodlingException>();
        else
            await Assert.That(() => reopened.AdmitRetry(original.AttemptId, "retry-\ud800",
                fixture.Git.Workspaces, fixture.Profile)).Throws<BroodlingException>();
        await Assert.That(reopened.FindRetry("retry-\ufffd")!.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(reopened.AdmitRetry(original.AttemptId, "retry-\ufffd", fixture.Git.Workspaces, fixture.Profile))
            .IsEqualTo(successor);
        await Assert.That(reopened.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExplicitReplacementRequiresCompletedRetirementThenFreezesOriginalMaterialAndNewAllocation()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store, revision: "main");
        AttemptRecord Retry() => store.AdmitRetry(original.AttemptId, "replace", fixture.Git.Workspaces, fixture.Profile);
        await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
        store.AbandonAttempt(original.AttemptId, "abandoned");
        await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
        await store.StopAsync(original.AttemptId, "later reason", new ControlledTransport());
        await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
        store.RetireAttempt(original.AttemptId);
        await Assert.That(() => store.AdmitRetry(original.AttemptId, "http")).Throws<AttemptAdmissionError>();
        var originalStatus = store.Status(original.ContractRevisionId);
        fixture.Git.Commit("today's HEAD must not become B1\n");
        var successor = Retry();
        await Assert.That(successor.AttemptId == original.AttemptId).IsFalse();
        await Assert.That(successor.B1).IsEqualTo(original.B1);
        await Assert.That(successor.ContractRevisionId).IsEqualTo(original.ContractRevisionId);
        await Assert.That(successor.WorkUnitId).IsEqualTo(original.WorkUnitId);
        await Assert.That(successor.Allocation.Branch == original.Allocation.Branch).IsFalse();
        await Assert.That(Directory.Exists(successor.Allocation.Enclosure)).IsFalse();
        await Assert.That(store.FindSubmission(successor.AttemptId)).IsNull();
        using var reopened = fixture.Git.State.Open();
        await Assert.That(reopened.AdmitRetry(original.AttemptId, "replace", fixture.Git.Workspaces, fixture.Profile)).IsEqualTo(successor);
        var prepared = reopened.PrepareRetry(original.AttemptId, "replace", fixture.Git.Workspaces, fixture.Profile);
        var request = JsonNode.Parse(prepared.RequestJson)!;
        await Assert.That((string)request["startingCommit"]!).IsEqualTo(original.B1.CommitOid);
        await Assert.That(AttemptFixture.RunGit(successor.Allocation.WorktreePath, "rev-parse", "HEAD").Trim()).IsEqualTo(original.B1.CommitOid);
        await Assert.That(File.ReadAllText(Path.Combine(successor.Allocation.WorktreePath, "original.txt"))).IsEqualTo("original selected bytes\n");
        await Assert.That(reopened.Status(original.ContractRevisionId).Revision.CanonicalBytes.SequenceEqual(originalStatus.Revision.CanonicalBytes)).IsTrue();
        await Assert.That(reopened.Status(original.ContractRevisionId).Sources.Select(s => s.SourceId).SequenceEqual(originalStatus.Sources.Select(s => s.SourceId))).IsTrue();
        await Assert.That(reopened.FindRetry("replace")!.TargetJson).IsEqualTo(request["target"]!.ToJsonString());
    }

    [Test]
    public async Task SameKeyConvergesAndHistoricalReplayReturnsIdentityWithoutRevivingAuthority()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await SafeRetire(store, original);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var other = fixture.Git.State.Open();
            return other.AdmitRetry(original.AttemptId, "same-key", fixture.Git.Workspaces, fixture.Profile);
        })));
        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
        var successor = results[0];
        foreach (var request in new (string Id, string Key, string Root, NativeProfile Profile)[] {
            (original.AttemptId, "other-key", fixture.Git.Workspaces, fixture.Profile),
            (successor.AttemptId, "same-key", fixture.Git.Workspaces, fixture.Profile),
            (original.AttemptId, "same-key", fixture.Git.Workspaces + "-changed", fixture.Profile),
            (original.AttemptId, "same-key", fixture.Git.Workspaces, new NativeProfile(fixture.NativeState + "-changed", fixture.Codex, toolPath: "/usr/bin:/bin")) })
            await Assert.That(() => store.AdmitRetry(request.Id, request.Key, request.Root, request.Profile)).Throws<AttemptConflict>();
        await SafeRetire(store, successor);
        var third = store.AdmitRetry(successor.AttemptId, "next-key", fixture.Git.Workspaces, fixture.Profile);
        using var reopened = fixture.Git.State.Open();
        var historical = reopened.AdmitRetry(original.AttemptId, "same-key", fixture.Git.Workspaces, fixture.Profile);
        await Assert.That(historical.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(historical.IsCurrent).IsFalse();
        await Assert.That(reopened.CurrentAttempt(original.WorkUnitId)!.AttemptId).IsEqualTo(third.AttemptId);
        await Assert.That(() => reopened.PrepareRetry(original.AttemptId, "same-key", fixture.Git.Workspaces, fixture.Profile)).Throws<StaleAttempt>();
        await Assert.That(() => reopened.PrepareSubmission(historical.AttemptId, fixture.Profile)).Throws<SubmissionNotReady>();
        await Assert.That(async () => await reopened.DispatchAsync(historical.AttemptId, fixture.Profile, new ControlledTransport())).Throws<StaleAttempt>();
        await Assert.That(() => fixture.Git.Admit(reopened)).Throws<StaleAttempt>();
        await Assert.That(Directory.Exists(historical.Allocation.Enclosure)).IsFalse();
    }

    [Test]
    public async Task EveryPreparationEntryPointEnforcesReplacementTargetAndDispatchedReplayDoesNotReprovision()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await SafeRetire(store, original);
        var successor = store.AdmitRetry(original.AttemptId, "same", fixture.Git.Workspaces, fixture.Profile);
        store.ProvisionAttempt(successor.AttemptId);
        var changed = new NativeProfile(fixture.NativeState + "-other", fixture.Codex, toolPath: "/usr/bin:/bin");
        await Assert.That(() => store.PrepareSubmission(successor.AttemptId, changed)).Throws<AttemptConflict>();
        await Assert.That(async () => await store.DispatchAsync(successor.AttemptId, changed, new ControlledTransport())).Throws<AttemptConflict>();
        await Assert.That(store.FindSubmission(successor.AttemptId)).IsNull();
        var prepared = store.PrepareRetry(original.AttemptId, "same", fixture.Git.Workspaces, fixture.Profile);
        var transport = new ControlledTransport { Submit = _ => throw new NativeTransportError() };
        await Assert.That(async () => await store.RetryAsync(original.AttemptId, "same", fixture.Git.Workspaces, fixture.Profile, transport)).Throws<NativeTransportError>();
        File.WriteAllText(Path.Combine(successor.Allocation.WorktreePath, "candidate-progress"), "retain even after lost acknowledgment");
        transport.Submit = _ => Task.FromResult("retry-run");
        var correlated = await store.RetryAsync(original.AttemptId, "same", fixture.Git.Workspaces, fixture.Profile, transport);
        await Assert.That(correlated.RunId).IsEqualTo("retry-run");
        await Assert.That(correlated.RequestJson).IsEqualTo(prepared.RequestJson);
        await Assert.That(File.ReadAllText(Path.Combine(successor.Allocation.WorktreePath, "candidate-progress"))).IsEqualTo("retain even after lost acknowledgment");
        await Assert.That(() => store.ProvisionAttempt(successor.AttemptId)).Throws<SubmissionNotReady>();
    }

    [Test]
    public async Task AllocationFailureRollsBackRetryIdentityAndAllocationTogether()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await SafeRetire(store, original);
        var connection = Connection(store);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TEMP TRIGGER fail_retry AFTER INSERT ON attempts BEGIN SELECT RAISE(ABORT, 'allocation failure'); END";
        command.ExecuteNonQuery();
        await Assert.That(() => store.AdmitRetry(original.AttemptId, "rollback", fixture.Git.Workspaces, fixture.Profile)).Throws<SqliteException>();
        await Assert.That(store.FindRetry("rollback")).IsNull();
        await Assert.That(store.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(1);
        await Assert.That(Directory.Exists(fixture.Git.Workspaces)).IsFalse();
        command.CommandText = "DROP TRIGGER fail_retry"; command.ExecuteNonQuery();
        await Assert.That(store.AdmitRetry(original.AttemptId, "rollback", fixture.Git.Workspaces, fixture.Profile).IsCurrent).IsTrue();
    }

    [Test]
    [Arguments("original-object")]
    [Arguments("source-bytes")]
    public async Task MissingOriginalMaterialRefusesBeforeAnyAllocation(string corruption)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await SafeRetire(store, original);
        if (corruption == "original-object") fixture.Git.DeleteObject(original.B1.CommitOid);
        else fixture.Git.State.Execute("DROP TRIGGER sources_no_update; UPDATE entitled_sources SET content = X'1234'");
        if (corruption == "original-object")
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "bad", fixture.Git.Workspaces, fixture.Profile)).Throws<UnsupportedStartingState>();
        else
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "bad", fixture.Git.Workspaces, fixture.Profile)).Throws<SourceAttributionError>();
        await Assert.That(store.FindRetry("bad")).IsNull();
        await Assert.That(store.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(1);
        await Assert.That(Directory.Exists(fixture.Git.Workspaces)).IsFalse();
    }

    [Test]
    public async Task SqlCannotBypassRetirementRewriteLineageRebindOriginalOrDropChosenTarget()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await Assert.That(() => fixture.Git.State.Execute($"INSERT INTO attempt_retries VALUES ('unsafe', '{original.AttemptId}', 'forged', '/', '{{}}', 'now')")).Throws<SqliteException>();
        await SafeRetire(store, original);
        using (var connection = fixture.Git.State.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"INSERT INTO attempt_retries VALUES ('forged', '{original.AttemptId}', 'forged', '/durable', '{{}}', 'now')";
            command.ExecuteNonQuery();
            command.CommandText = $"""
                INSERT INTO attempts SELECT 'forged', work_unit_id, contract_revision_id, 1,
                b1_repository, '{new string('f', 40)}', b1_material_sha256, b1_requested_revision,
                '/durable', '/durable/forged', '/durable/forged/worktree', 'broodling/forged', 'now', resource_kind FROM attempts
                """;
            await Assert.That(() => command.ExecuteNonQuery()).Throws<SqliteException>();
            // A lineage-only commit cannot leave an orphan reservation.
            await Assert.That(() => transaction.Commit()).Throws<SqliteException>();
        }
        var successor = store.AdmitRetry(original.AttemptId, "valid", fixture.Git.Workspaces, fixture.Profile);
        foreach (var sql in new[] { "DELETE FROM attempt_retries", "UPDATE attempt_retries SET target_json = '{}'", "INSERT OR REPLACE INTO attempt_retries SELECT * FROM attempt_retries" })
            await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();
        store.ProvisionAttempt(successor.AttemptId);
        await Assert.That(() => fixture.Git.State.Execute($"INSERT INTO native_submissions (attempt_id, format, submission_key, request_json, state) VALUES ('{successor.AttemptId}', 'bridge', 'key', '{{}}', 'prepared')")).Throws<SqliteException>();
    }

    [Test]
    public async Task RetryRootCannotOverlapSourceSiblingOrStoreAndCreatesNoScaffolding()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var original = fixture.Git.Admit(store);
        await SafeRetire(store, original);
        var sibling = Path.Combine(fixture.Root, "sibling");
        fixture.Git.Git("worktree", "add", "-b", "sibling", sibling);
        foreach (var root in new[] { fixture.Git.Repository, fixture.Git.GitDirectory, Path.Combine(sibling, "nested") })
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "overlap", root, fixture.Profile)).Throws<WorktreeOwnershipConflict>();
        await Assert.That(store.FindRetry("overlap")).IsNull();
        await Assert.That(store.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(1);
    }

    internal static SqliteConnection Connection(BroodlingStore store) => (SqliteConnection)typeof(BroodlingStore)
        .GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;

    [Test]
    public async Task HttpReplacementKeepsOriginalB1AndResourceKindWithoutLocalResources()
    {
        using var fixture = new AttemptFixture();
        string local;
        AttemptRecord original, successor;
        using (var store = fixture.State.Open())
        {
            original = fixture.AdmitHttp(store, revision: "main");
            AttemptRecord Retry() => store.AdmitRetry(original.AttemptId, "replace");
            await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
            await store.StopAsync(original.AttemptId, "abandoned", transport: null);
            await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
            store.RetireAttempt(original.AttemptId);
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "worktree", fixture.Workspaces,
                NativeFixture.Unused(Path.Combine(fixture.State.Root, "native")))).Throws<AttemptAdmissionError>();
            using (var connection = fixture.State.Connect())
            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO attempt_retries VALUES ('forged', '{original.AttemptId}', 'forged', NULL, NULL, 'now');
                    INSERT INTO attempts SELECT 'forged', work_unit_id, contract_revision_id, 1, b1_repository, b1_commit_oid,
                        b1_material_sha256, b1_requested_revision, '/durable', '/durable/forged', '/durable/forged/worktree',
                        'broodling/forged', 'now', 'worktree' FROM attempts WHERE attempt_id = '{original.AttemptId}'
                    """;
                await Assert.That(() => command.ExecuteNonQuery()).Throws<SqliteException>();
            }
            fixture.Commit("today's HEAD must not become B1\n");
            local = fixture.LocalResources();
            store.PauseInstallation(); // Explicit safe replacement allocation remains permitted while paused.
            successor = Retry();
            await Assert.That(successor.AttemptId == original.AttemptId).IsFalse();
            await Assert.That(successor.ResourceKind).IsEqualTo(AttemptRecord.Http);
            await Assert.That(successor.WorktreeAllocation).IsNull();
            await Assert.That(successor.B1).IsEqualTo(original.B1);
            await Assert.That(successor.ContractRevisionId).IsEqualTo(original.ContractRevisionId);
            await Assert.That(successor.IsCurrent).IsTrue();
            await Assert.That(successor.Retry).IsEqualTo(new AttemptRetry("replace", original.AttemptId, successor.AttemptId, null, null, successor.AdmittedAt));
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "second")).Throws<AttemptConflict>();
        }
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.AdmitRetry(original.AttemptId, "replace")).IsEqualTo(successor);
        await Assert.That(reopened.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(2);
        await Assert.That(fixture.Git("rev-parse", original.B1.RetentionRef).Trim()).IsEqualTo(original.B1.CommitOid);
        await Assert.That(fixture.LocalResources()).IsEqualTo(local);
    }

    internal static async Task SafeRetire(BroodlingStore store, AttemptRecord attempt)
    {
        await store.StopAsync(attempt.AttemptId, "explicit replacement", new ControlledTransport());
        store.RetireAttempt(attempt.AttemptId);
    }
}
