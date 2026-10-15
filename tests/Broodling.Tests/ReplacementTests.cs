using Broodling.Host;
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
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var original = fixture.Admit(store);
        await SafeRetire(store, original);
        var successor = store.AdmitRetry(original.AttemptId, "retry-\ufffd");
        using var reopened = fixture.State.Open();
        if (operation == "find")
            await Assert.That(() => reopened.FindRetry("retry-\ud800")).Throws<BroodlingException>();
        else
            await Assert.That(() => reopened.AdmitRetry(original.AttemptId, "retry-\ud800")).Throws<BroodlingException>();
        await Assert.That(reopened.FindRetry("retry-\ufffd")!.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(reopened.AdmitRetry(original.AttemptId, "retry-\ufffd")).IsEqualTo(successor);
        await Assert.That(reopened.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SameKeyConvergesAndHistoricalReplayReturnsIdentityWithoutRevivingAuthority()
    {
        using var fixture = new HttpFixture();
        var store = fixture.Store;
        var original = fixture.Attempt;
        await SafeRetire(store, original);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var other = fixture.Git.State.Open();
            return other.AdmitRetry(original.AttemptId, "same-key");
        })));
        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
        var successor = results[0];
        foreach (var (id, key) in new[] { (original.AttemptId, "other-key"), (successor.AttemptId, "same-key") })
            await Assert.That(() => store.AdmitRetry(id, key)).Throws<AttemptConflict>();
        await SafeRetire(store, successor);
        var third = store.AdmitRetry(successor.AttemptId, "next-key");
        using var reopened = fixture.Git.State.Open();
        var historical = reopened.AdmitRetry(original.AttemptId, "same-key");
        await Assert.That(historical.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(historical.IsCurrent).IsFalse();
        await Assert.That(reopened.CurrentAttempt(original.WorkUnitId)!.AttemptId).IsEqualTo(third.AttemptId);
        await Assert.That(() => fixture.Prepare(reopened, historical.AttemptId)).Throws<StaleAttempt>();
        await Assert.That(async () => await reopened.DispatchHttpAsync(historical.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<StaleAttempt>();
        await Assert.That(reopened.FindSubmission(historical.AttemptId)).IsNull();
        await Assert.That(() => fixture.Git.Admit(reopened)).Throws<StaleAttempt>();
    }

    [Test]
    public async Task AllocationFailureRollsBackRetryIdentityAndAttemptTogether()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var original = fixture.Admit(store);
        await SafeRetire(store, original);
        var connection = Connection(store);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TEMP TRIGGER fail_retry AFTER INSERT ON attempts BEGIN SELECT RAISE(ABORT, 'allocation failure'); END";
        command.ExecuteNonQuery();
        await Assert.That(() => store.AdmitRetry(original.AttemptId, "rollback")).Throws<SqliteException>();
        await Assert.That(store.FindRetry("rollback")).IsNull();
        await Assert.That(store.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(1);
        command.CommandText = "DROP TRIGGER fail_retry"; command.ExecuteNonQuery();
        await Assert.That(store.AdmitRetry(original.AttemptId, "rollback").IsCurrent).IsTrue();
    }

    [Test]
    [Arguments("original-object")]
    [Arguments("source-bytes")]
    public async Task MissingOriginalMaterialRefusesBeforeAnyAllocation(string corruption)
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var original = fixture.Admit(store);
        await SafeRetire(store, original);
        if (corruption == "original-object") fixture.DeleteObject(original.B1.CommitOid);
        else fixture.State.Execute("DROP TRIGGER sources_no_update; UPDATE entitled_sources SET content = X'1234'");
        if (corruption == "original-object")
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "bad")).Throws<UnsupportedStartingState>();
        else
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "bad")).Throws<SourceAttributionError>();
        await Assert.That(store.FindRetry("bad")).IsNull();
        await Assert.That(store.Status(original.ContractRevisionId).Attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SqlCannotBypassRetirementRewriteLineageOrRebindOriginal()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var original = fixture.Admit(store);
        await Assert.That(() => fixture.State.Execute($"INSERT INTO attempt_retries VALUES ('unsafe', '{original.AttemptId}', 'forged', 'now', 0)")).Throws<SqliteException>();
        await SafeRetire(store, original);
        using (var connection = fixture.State.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"INSERT INTO attempt_retries VALUES ('forged', '{original.AttemptId}', 'forged', 'now', 0)";
            command.ExecuteNonQuery();
            command.CommandText = $"""
                INSERT INTO attempts SELECT 'forged', work_unit_id, contract_revision_id, 1,
                b1_repository, '{new string('f', 40)}', b1_material_sha256, b1_requested_revision,
                'now' FROM attempts
                """;
            await Assert.That(() => command.ExecuteNonQuery()).Throws<SqliteException>();
            // A lineage-only commit cannot leave an orphan reservation.
            await Assert.That(() => transaction.Commit()).Throws<SqliteException>();
        }
        store.AdmitRetry(original.AttemptId, "valid");
        foreach (var sql in new[] { "DELETE FROM attempt_retries", "UPDATE attempt_retries SET retry_key = 'changed'",
            "INSERT OR REPLACE INTO attempt_retries SELECT * FROM attempt_retries" })
            await Assert.That(() => fixture.State.Execute(sql)).Throws<SqliteException>();
    }

    internal static SqliteConnection Connection(BroodlingStore store) => (SqliteConnection)typeof(BroodlingStore)
        .GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;

    [Test]
    public async Task HttpReplacementKeepsOriginalB1WithoutLocalResources()
    {
        using var fixture = new AttemptFixture();
        string local;
        AttemptRecord original, successor;
        AdmissionStatus originalStatus;
        using (var store = fixture.State.Open())
        {
            original = fixture.Admit(store, revision: "main");
            AttemptRecord Retry() => store.AdmitRetry(original.AttemptId, "replace");
            await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
            await store.StopAsync(original.AttemptId, "abandoned");
            await Assert.That(() => Retry()).Throws<AttemptAdmissionError>();
            store.RetireAttempt(original.AttemptId);
            originalStatus = store.Status(original.ContractRevisionId);
            fixture.Commit("today's HEAD must not become B1\n");
            local = fixture.LocalResources();
            store.PauseInstallation(); // Explicit safe replacement allocation remains permitted while paused.
            // Without a retained origin the one-call operation refuses before admitting anything.
            await Assert.That(() => store.PrepareRetry(original.AttemptId, "replace", TestEnvironments.Live)).Throws<SubmissionNotReady>();
            await Assert.That(store.FindRetry("replace")).IsNull();
            successor = Retry();
            await Assert.That(successor.AttemptId == original.AttemptId).IsFalse();
            await Assert.That(successor.B1).IsEqualTo(original.B1);
            await Assert.That(successor.ContractRevisionId).IsEqualTo(original.ContractRevisionId);
            await Assert.That(successor.IsCurrent).IsTrue();
            await Assert.That(successor.Retry).IsEqualTo(new AttemptRetry("replace", original.AttemptId, successor.AttemptId, successor.AdmittedAt, false));
            await Assert.That(() => store.AdmitRetry(original.AttemptId, "second")).Throws<AttemptConflict>();
        }
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.AdmitRetry(original.AttemptId, "replace")).IsEqualTo(successor);
        // The successor shares the original Contract and source snapshots, never today's.
        var status = reopened.Status(original.ContractRevisionId);
        await Assert.That(status.Attempts.Count).IsEqualTo(2);
        await Assert.That(status.Revision.CanonicalBytes.SequenceEqual(originalStatus.Revision.CanonicalBytes)).IsTrue();
        await Assert.That(status.Sources.Select(source => source.SourceId).SequenceEqual(originalStatus.Sources.Select(source => source.SourceId))).IsTrue();
        await Assert.That(fixture.Git("rev-parse", original.B1.RetentionRef).Trim()).IsEqualTo(original.B1.CommitOid);
        await Assert.That(fixture.LocalResources()).IsEqualTo(local);
    }

    [Test]
    public async Task StoppedTargetRetiredDirectTargetWorkIsReplacedWithinThePauseFromItsFrozenAuthorityAndDispatchedOnlyAfterRelease()
    {
        await using var target = new StockTarget();
        using var fixture = await BundleHttpFixture.CreateAsync();
        var store = fixture.Store;
        var original = fixture.Attempt;
        var origin = target.Origin.GetLeftPart(UriPartial.Authority);
        // The predecessor's send is left unresolved: the target may hold its run until maintenance stops it.
        var sent = store.PrepareHttpSubmission(original.AttemptId, TestEnvironments.For(origin));
        target.Submit = _ => Task.FromResult((503, """{"code":"target.unavailable","message":"unavailable"}"""));
        await Assert.That(async () => await store.DispatchHttpAsync(original.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<NativeTransportError>();
        target.Submit = body => Task.FromResult(target.Accept(body));
        var unresolved = store.FindSubmission(original.AttemptId)!;
        store.AbandonAttempt(original.AttemptId, "target lost");
        store.PauseInstallation();
        var retired = store.RetireStoppedTargetAttempt(original.AttemptId, TestEnvironments.StoppedTarget(unresolved));

        // Replacement happens only within a maintenance pause: neither admission nor first preparation outside it.
        var resume = new Invocation(store, TestEnvironments.For(origin));
        store.ReleaseInstallation();
        await Assert.That(() => store.PrepareRetry(original.AttemptId, "after-maintenance", TestEnvironments.Live)).Throws<MaintenanceUnverified>();
        await Assert.That(store.FindRetry("after-maintenance")).IsNull();
        store.PauseInstallation();
        var successor = store.AdmitRetry(original.AttemptId, "after-maintenance");
        store.ReleaseInstallation();
        await Assert.That(() => store.PrepareRetry(original.AttemptId, "after-maintenance", TestEnvironments.Live)).Throws<MaintenanceUnverified>();
        await Assert.That(async () => await resume.ResumeAsync(original.ContractRevisionId, credentials: HttpDispatchTests.Credentials()))
            .Throws<MaintenanceUnverified>();
        await Assert.That(store.FindSubmission(successor.AttemptId)).IsNull();

        store.PauseInstallation();
        var prepared = store.PrepareRetry(original.AttemptId, "after-maintenance", TestEnvironments.Live);
        await Assert.That(prepared.AttemptId).IsEqualTo(successor.AttemptId);
        await Assert.That(successor.ContractRevisionId).IsEqualTo(original.ContractRevisionId);
        await Assert.That(successor.B1).IsEqualTo(original.B1);
        await Assert.That(store.PrepareRetry(original.AttemptId, "after-maintenance", TestEnvironments.Live)).IsEqualTo(prepared);
        await Assert.That(() => store.AdmitRetry(original.AttemptId, "second")).Throws<AttemptConflict>();
        // The predecessor's verified origin, the same bundle-bound task and exact B1 source; only the Attempt's own identities differ.
        await Assert.That(prepared.Origin).IsEqualTo(origin);
        await Assert.That(prepared.IntendedRunId == sent.IntendedRunId).IsFalse();
        JsonNode Submission(NativeSubmission record) => JsonNode.Parse(record.RequestJson)!["submission"]!;
        await Assert.That(JsonNode.DeepEquals(Submission(prepared)["initialInput"], Submission(sent)["initialInput"])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(Submission(prepared)["source"], Submission(sent)["source"])).IsTrue();
        await Assert.That(async () => await store.DispatchHttpAsync(successor.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<InstallationPaused>();
        await Assert.That(target.Bodies.Count).IsEqualTo(1);

        store.ReleaseInstallation();
        var resumed = await resume.ResumeAsync(original.ContractRevisionId, credentials: HttpDispatchTests.Credentials());
        var correlated = resumed.Submissions.Single(record => record.AttemptId == successor.AttemptId);
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That((string)target.Bodies.Last()["runId"]!).IsEqualTo(prepared.IntendedRunId);
        // Repeating the operation hands back the retained submission and prepares nothing new.
        await Assert.That(store.PrepareRetry(original.AttemptId, "after-maintenance", TestEnvironments.Live)).IsEqualTo(correlated);

        // The predecessor's history stays exactly as retained; only the successor is quarantined now.
        await Assert.That(store.FindSubmission(original.AttemptId)).IsEqualTo(unresolved);
        await Assert.That(store.FindRetirement(original.AttemptId)).IsEqualTo(retired);
        var lineage = original.AttemptId + "," + successor.AttemptId;
        await Assert.That(string.Join(",", resumed.Attempts.Select(attempt => attempt.AttemptId))).IsEqualTo(lineage);
        await Assert.That(resumed.Attempts[0].Abandonment!.Reason).IsEqualTo("target lost");
        await Assert.That(resumed.QuarantinedAttemptIds.Single()).IsEqualTo(successor.AttemptId);
        await Assert.That(store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(string.Join(",", store.GetIssueSubmission(fixture.Bundle.SubmissionId).AttemptIds)).IsEqualTo(lineage);
    }

    [Test]
    public async Task ReplaceAttemptCommandRefusesOutsideThePauseThenPrintsThePreparedSuccessor()
    {
        using var fixture = new HttpFixture();
        var sent = fixture.PrepareAt(new Uri(HttpFixture.Target), "dispatched");
        var id = fixture.Attempt.AttemptId;
        fixture.Store.AbandonAttempt(id, "operator stop");
        fixture.Store.PauseInstallation();
        fixture.Store.RetireStoppedTargetAttempt(id, TestEnvironments.StoppedTarget(sent));
        fixture.Store.ReleaseInstallation();
        var (path, application) = (fixture.Git.State.Path, fixture.Git.State.Application);
        // The secret-free catalog naming the predecessor's environment; its control token file is never read.
        var config = Path.Combine(fixture.Git.State.Root, "invocation.json");
        File.WriteAllText(config, TestEnvironments.Configuration(HttpFixture.Target, "/srv/broodling-test/absent-token").ToJsonString());

        var output = new StringWriter();
        var error = new StringWriter();
        await Assert.That(StoreCommands.Run(["replace-attempt", path, id, "after-maintenance", config], application, output, error)).IsEqualTo(1);
        await Assert.That((string)JsonNode.Parse(error.ToString())!["error"]!).IsEqualTo("maintenance_unverified");
        await Assert.That(fixture.Store.FindRetry("after-maintenance")).IsNull();
        fixture.Store.PauseInstallation();
        await Assert.That(StoreCommands.Run(["replace-attempt", path, id, "after-maintenance", config], application, output, error)).IsEqualTo(0);
        var printed = JsonNode.Parse(output.ToString())!;
        var successor = fixture.Store.FindRetry("after-maintenance")!.AttemptId;
        await Assert.That((string)printed["attemptId"]!).IsEqualTo(successor);
        await Assert.That((string)printed["state"]!).IsEqualTo("prepared");
        await Assert.That((string)printed["intendedRunId"]!).IsEqualTo(fixture.Store.FindSubmission(successor)!.IntendedRunId);
        await Assert.That((string)printed["environment"]!["name"]!).IsEqualTo(TestEnvironments.NameFor(HttpFixture.Target));
        await Assert.That(fixture.Store.FindRetry("after-maintenance")!.EnvironmentUpdate).IsFalse();
        // The same key with the other environment choice conflicts; any other trailing argument is a usage error.
        error.GetStringBuilder().Clear();
        await Assert.That(StoreCommands.Run(["replace-attempt", path, id, "after-maintenance", config, "--update-environment"],
            application, output, error)).IsEqualTo(1);
        await Assert.That((string)JsonNode.Parse(error.ToString())!["error"]!).IsEqualTo("attempt_conflict");
        await Assert.That(StoreCommands.Run(["replace-attempt", path, id, "after-maintenance", config, "--update"],
            application, output, error)).IsEqualTo(2);
    }

    /// <summary>
    /// A prepared-only predecessor of the environment <c>dotnet</c> at <see cref="HttpFixture.Target"/>, safely retired,
    /// and the adoption set whose second image is a stable-name image swap of the bundled one.
    /// </summary>
    private static async Task<(HttpFixture Fixture, NativeSubmission Predecessor, IReadOnlyList<AdoptedEnvironment> Adopted)>
        RetiredPredecessor(Action<AttemptFixture>? source = null)
    {
        var fixture = new HttpFixture(source: source);
        var adopted = EnvironmentSelectionTests.WithSwappedImage(Path.Combine(fixture.Git.State.Root, "adopted"));
        var predecessor = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, Catalog(adopted));
        await SafeRetire(fixture.Store, fixture.Attempt);
        return (fixture, predecessor, adopted);
    }

    /// <summary>The catalog naming <c>dotnet</c> as the predecessor was prepared, or with the given changes.</summary>
    private static EnvironmentCatalog Catalog(IReadOnlyList<AdoptedEnvironment> adopted, string? image = null,
        string origin = HttpFixture.Target, string name = "dotnet") =>
        TestEnvironments.Catalog([TestEnvironments.Entry(name, origin, image: image ?? adopted[0].Image)], name, adopted);

    private static string EnvironmentSha256(HttpFixture fixture, string attemptId) =>
        fixture.Scalar($"SELECT environment_sha256 FROM native_submissions WHERE attempt_id = '{attemptId}'");

    private static JsonNode Environment(NativeSubmission record) => JsonNode.Parse(record.BindingJson)!["environment"]!;

    /// <summary>
    /// By default a successor inherits its predecessor's resolution exactly, which the catalog must still serve:
    /// a swapped image, a removed entry or a reassigned origin refuses with nothing admitted.
    /// </summary>
    [Test]
    [Arguments("unchanged")]
    [Arguments("image-swapped")]
    [Arguments("entry-removed")]
    [Arguments("origin-reassigned")]
    public async Task InheritsPredecessorResolution(string change)
    {
        var (fixture, predecessor, adopted) = await RetiredPredecessor();
        using var _ = fixture;
        var id = fixture.Attempt.AttemptId;
        var today = change switch
        {
            "unchanged" => Catalog(adopted),
            "image-swapped" => Catalog(adopted, image: adopted[1].Image),
            "entry-removed" => Catalog(adopted, name: "approved-alt"),
            _ => Catalog(adopted, origin: "http://127.0.0.1:10")
        };
        if (change != "unchanged")
        {
            var refused = await Assert.That(() => fixture.Store.PrepareRetry(id, "inherit", today)).Throws<EnvironmentRefused>();
            await Assert.That(refused!.Code).IsEqualTo("environment_changed");
            await Assert.That(refused.Message).Contains("--update-environment");
            await Assert.That(fixture.Store.FindRetry("inherit")).IsNull();
            await Assert.That(fixture.Store.Status(fixture.Attempt.ContractRevisionId).Attempts.Count).IsEqualTo(1);
            await Assert.That(fixture.Scalar("SELECT count(*) FROM native_submissions")).IsEqualTo("1");
            return;
        }
        var successor = fixture.Store.PrepareRetry(id, "inherit", today);
        await Assert.That(EnvironmentSha256(fixture, successor.AttemptId)).IsEqualTo(EnvironmentSha256(fixture, id));
        await Assert.That(JsonNode.DeepEquals(Environment(successor)["requirement"], Environment(predecessor)["requirement"])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(Environment(successor)["resolution"], Environment(predecessor)["resolution"])).IsTrue();
        await Assert.That(successor.Environment).IsEqualTo(predecessor.Environment);
        await Assert.That(fixture.Store.FindRetry("inherit")!.EnvironmentUpdate).IsFalse();
        // Once prepared, the successor keeps its retained environment whatever the catalog says later.
        await Assert.That(fixture.Store.PrepareRetry(id, "inherit", Catalog(adopted, image: adopted[1].Image))).IsEqualTo(successor);
    }

    /// <summary>
    /// The explicit update is recorded on the successor's retry, and its resolution keeps the predecessor's environment
    /// name and requirement (here a declaration at original B1), resolved to the image the catalog names now.
    /// </summary>
    [Test]
    public async Task ExplicitUpdateIsRecorded()
    {
        var (fixture, predecessor, adopted) = await RetiredPredecessor(git =>
            EnvironmentSelectionTests.Commit(git, EnvironmentSelectionTests.Declaration, EnvironmentSelectionTests.Declare("dotnet")));
        using var _ = fixture;
        var id = fixture.Attempt.AttemptId;
        // A later commit declaring another environment is not the successor's B1; nothing re-reads the repository.
        EnvironmentSelectionTests.Commit(fixture.Git, EnvironmentSelectionTests.Declaration, EnvironmentSelectionTests.Declare("approved-alt"));
        var swapped = TestEnvironments.Catalog([TestEnvironments.Entry("dotnet", HttpFixture.Target, image: adopted[1].Image),
            TestEnvironments.Entry("approved-alt", "http://127.0.0.1:10")], "approved-alt", adopted);

        var successor = fixture.Store.PrepareRetry(id, "update", swapped, updateEnvironment: true);
        await Assert.That(fixture.Store.FindRetry("update")!.EnvironmentUpdate).IsTrue();
        await Assert.That(fixture.Store.GetAttempt(successor.AttemptId).Retry!.EnvironmentUpdate).IsTrue();
        await Assert.That(successor.Environment.Name).IsEqualTo("dotnet");
        await Assert.That(successor.Environment.Image).IsEqualTo(adopted[1].Image);
        await Assert.That(predecessor.Environment.Image).IsEqualTo(adopted[0].Image);
        await Assert.That(EnvironmentSha256(fixture, successor.AttemptId) == EnvironmentSha256(fixture, id)).IsFalse();
        await Assert.That(JsonNode.DeepEquals(Environment(successor)["requirement"], Environment(predecessor)["requirement"])).IsTrue();
        await Assert.That((string)Environment(successor)["resolution"]!["basis"]!).IsEqualTo("declared");
        // The successor's request keeps its predecessor's exact source.
        JsonNode Source(NativeSubmission record) => JsonNode.Parse(record.RequestJson)!["submission"]!["source"]!;
        await Assert.That(JsonNode.DeepEquals(Source(successor), Source(predecessor))).IsTrue();
        await Assert.That(fixture.Store.PrepareRetry(id, "update", swapped, updateEnvironment: true)).IsEqualTo(successor);
    }

    [Test]
    public async Task UpdateRefusedWhenNameGone()
    {
        var (fixture, _, adopted) = await RetiredPredecessor();
        using var __ = fixture;
        var refused = await Assert.That(() => fixture.Store.PrepareRetry(fixture.Attempt.AttemptId, "update",
            Catalog(adopted, image: adopted[1].Image, name: "approved-alt"), updateEnvironment: true)).Throws<EnvironmentRefused>();
        await Assert.That(refused!.Code).IsEqualTo("environment_unapproved");
        await Assert.That(fixture.Store.FindRetry("update")).IsNull();
        await Assert.That(fixture.Store.Status(fixture.Attempt.ContractRevisionId).Attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RetryKeyReplayWithOtherFlagConflicts()
    {
        var (fixture, _, adopted) = await RetiredPredecessor();
        using var __ = fixture;
        var id = fixture.Attempt.AttemptId;
        var successor = fixture.Store.AdmitRetry(id, "key", updateEnvironment: true);
        await Assert.That(() => fixture.Store.AdmitRetry(id, "key")).Throws<AttemptConflict>();
        await Assert.That(() => fixture.Store.PrepareRetry(id, "key", Catalog(adopted))).Throws<AttemptConflict>();
        await Assert.That(fixture.Store.FindSubmission(successor.AttemptId)).IsNull();
        await Assert.That(fixture.Store.AdmitRetry(id, "key", updateEnvironment: true)).IsEqualTo(successor);
        // Preparation follows the recorded choice, through the ordinary preparation that resumption uses as well.
        var prepared = fixture.Store.PrepareHttpSubmission(successor.AttemptId, Catalog(adopted, image: adopted[1].Image));
        await Assert.That(prepared.Environment.Image).IsEqualTo(adopted[1].Image);
    }

    /// <summary>
    /// SQL keeps a successor's retained environment to its predecessor's exact resolution unless its retry records an
    /// update, and to its predecessor's requirement either way.
    /// </summary>
    [Test]
    [Arguments("exact")]
    [Arguments("other-resolution")]
    [Arguments("other-requirement")]
    public async Task SqlRefusesAForgedSuccessorEnvironment(string forgery)
    {
        var (fixture, _, _) = await RetiredPredecessor();
        using var __ = fixture;
        var id = fixture.Attempt.AttemptId;
        var successor = fixture.Store.AdmitRetry(id, "forged").AttemptId;
        var run = Guid.CreateVersion7().ToString();
        var key = "broodling:http:v1:" + successor;
        var binding = forgery == "other-requirement" ? "json_set(binding_json, '$.environment.requirement.name', 'dotnet')" : "binding_json";
        var sha256 = forgery == "other-resolution" ? $"'{new string('f', 64)}'" : "environment_sha256";
        var insert = $"""
            INSERT INTO native_submissions (attempt_id, submission_key, request_json, state, intended_run_id, asset_sha256,
                binding_json, environment_name, environment_sha256, adoption_sha256, manifest_sha256, declaration_sha256)
            SELECT '{successor}', '{key}', json_set(request_json, '$.runId', '{run}', '$.submission.submissionKey', '{key}'),
                'prepared', '{run}', asset_sha256, {binding}, environment_name, {sha256}, adoption_sha256, manifest_sha256,
                declaration_sha256
            FROM native_submissions WHERE attempt_id = '{id}'
            """;
        if (forgery == "exact")
        {
            fixture.Git.State.Execute(insert);
            await Assert.That(fixture.Store.FindSubmission(successor)).IsNotNull();
            return;
        }
        await Assert.That(() => fixture.Git.State.Execute(insert)).Throws<SqliteException>();
        await Assert.That(fixture.Store.FindSubmission(successor)).IsNull();
    }

    internal static async Task SafeRetire(BroodlingStore store, AttemptRecord attempt)
    {
        await store.StopAsync(attempt.AttemptId, "explicit replacement");
        store.RetireAttempt(attempt.AttemptId);
    }
}
