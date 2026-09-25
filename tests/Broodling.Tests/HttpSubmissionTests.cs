using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>An HTTP Attempt whose shared Git custody names the admitted GitHub repository, plus a copy of the installed asset.</summary>
internal sealed class HttpFixture : IDisposable
{
    internal const string Target = "http://127.0.0.1:9";
    internal AttemptFixture Git { get; } = new();
    internal string Assets => Path.Combine(Git.State.Root, "installed-assets");
    internal BroodlingStore Store { get; }
    internal AttemptRecord Attempt { get; }

    internal HttpFixture(string? origin = "https://github.com/acme/widget.git")
    {
        if (origin is not null) Git.Git("remote", "add", "origin", origin);
        Directory.CreateDirectory(Assets);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "execution-assets")))
            File.Copy(file, Path.Combine(Assets, Path.GetFileName(file)));
        Store = Git.State.Open();
        Attempt = Git.AdmitHttp(Store);
    }

    internal NativeSubmission Prepare(BroodlingStore? store = null, string? attemptId = null, string target = Target) =>
        (store ?? Store).PrepareHttpSubmission(attemptId ?? Attempt.AttemptId, target, () => ExecutionAsset.Load(Assets));

    internal string Scalar(string sql)
    {
        using var connection = Git.State.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    public void Dispose() { Store.Dispose(); Git.Dispose(); }
}

public sealed class HttpSubmissionTests
{
    [Test]
    public async Task OfflinePreparationFreezesCompleteSecretFreeStockRequestAndExactAsset()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        var local = fixture.Git.LocalResources();
        // No target listens and no credentials exist: preparation needs neither.
        var prepared = fixture.Prepare();

        await Assert.That(prepared.Format).IsEqualTo(NativeSubmission.Http);
        await Assert.That(prepared.State).IsEqualTo("prepared");
        await Assert.That(prepared.RunId).IsNull();
        await Assert.That(prepared.ReplayBlockedReason).IsNull();
        await Assert.That(Guid.TryParseExact(prepared.IntendedRunId, "D", out var intended) && intended.Version == 7
            && intended.ToString() == prepared.IntendedRunId).IsTrue();
        await Assert.That(prepared.SubmissionKey).IsEqualTo("broodling:http:v1:" + attempt.AttemptId);

        var request = JsonNode.Parse(prepared.RequestJson)!.AsObject();
        var submission = request["submission"]!.AsObject();
        var asset = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Assets, "software-change-pr-codex-gateway.json")))!;
        await Assert.That(string.Join(",", request.Select(field => field.Key))).IsEqualTo("runId,submission");
        await Assert.That(string.Join(",", submission.Select(field => field.Key)))
            .IsEqualTo("title,graph,runtime,initialInput,source,submissionKey");
        await Assert.That((string)request["runId"]!).IsEqualTo(prepared.IntendedRunId);
        await Assert.That((string)submission["submissionKey"]!).IsEqualTo(prepared.SubmissionKey);
        await Assert.That(JsonNode.DeepEquals(submission["graph"], asset["graph"])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(submission["runtime"], asset["runtime"])).IsTrue();
        // The authorized PR branch is separate from exact original B1.
        await Assert.That(submission["source"]!.ToJsonString())
            .IsEqualTo($$"""{"repository":"acme/widget","branch":"main","revision":"{{attempt.B1.CommitOid}}"}""");
        var task = (string)submission["initialInput"]!["task"]!;
        await Assert.That(task.Contains("The complete reviewed request.") && task.Contains("\"comparisonBase\":\"" + attempt.B1.CommitOid))
            .IsTrue();

        await Assert.That(prepared.AssetSha256).IsEqualTo(ExecutionAsset.ApprovedSha256);
        using (var connection = fixture.Git.State.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT content FROM execution_assets";
            await Assert.That(((byte[])command.ExecuteScalar()!).SequenceEqual(
                File.ReadAllBytes(Path.Combine(fixture.Assets, "software-change-pr-codex-gateway.json")))).IsTrue();
        }
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(prepared.BindingJson!), new JsonObject
        {
            ["protocol"] = "zeroshot.native-v2-target/v2", ["origin"] = HttpFixture.Target, ["repository"] = attempt.B1.Repository,
            ["resultOrigin"] = "https://github.com/acme/widget.git",
            ["native"] = new JsonObject
            {
                ["version"] = NativeProfile.NativeVersion, ["sourceRevision"] = NativeProfile.NativeSourceRevision,
                ["linuxX64ExecutableSha256"] = NativeProfile.NativeExecutableSha256
            }
        })).IsTrue();
        await Assert.That(prepared.Locator).IsEqualTo(new NativeLocator("direct", HttpFixture.Target, null));

        // Preparation is neither dispatch intent nor acceptance.
        var status = fixture.Store.Status(attempt.ContractRevisionId);
        await Assert.That(status.Submissions.Single()).IsEqualTo(prepared);
        await Assert.That(status.QuarantinedAttemptIds.Count).IsEqualTo(0);
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);
        await Assert.That(await fixture.Store.ObserveAsync(attempt.AttemptId, new ControlledTransport())).IsNull();
        await Assert.That(fixture.Git.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(attempt.B1.CommitOid);
        await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);
    }

    [Test]
    [Arguments(null)]
    [Arguments("https://github.com/acme/other.git")]
    [Arguments("https://x-access-token:canary-secret-value@github.com/acme/widget.git")]
    public async Task ResultOriginMustNameAdmittedRepositoryWithoutCredentials(string? origin)
    {
        using var fixture = new HttpFixture(origin);
        await Assert.That(() => fixture.Prepare()).Throws<SubmissionNotReady>();
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.Dispose();
        await Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(fixture.Git.State.Path)).Contains("canary-secret-value")).IsFalse();
    }

    [Test]
    [Arguments("http://127.0.0.1:9/")]
    [Arguments("http://example.test:9")]
    [Arguments("https://user@example.test")]
    [Arguments("HTTPS://example.test")]
    [Arguments("https://example.test/path")]
    public async Task TargetOriginMustBeCanonical(string target)
    {
        using var fixture = new HttpFixture();
        await Assert.That(() => fixture.Prepare(target: target)).Throws<UnsupportedRuntime>();
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    public async Task ConcurrentPreparersConvergeOnFirstCommittedIdentity()
    {
        using var fixture = new HttpFixture();
        using var first = fixture.Git.State.Open();
        using var second = fixture.Git.State.Open();
        using var start = new Barrier(2);
        var results = await Task.WhenAll(new[] { first, second }.Select(store => Task.Run(() =>
        {
            start.SignalAndWait();
            return fixture.Prepare(store);
        })));
        await Assert.That(results[1]).IsEqualTo(results[0]);
        await Assert.That(fixture.Scalar("SELECT count(*) FROM native_submissions")).IsEqualTo("1");
        await Assert.That(fixture.Scalar("SELECT count(*) FROM execution_assets")).IsEqualTo("1");
    }

    [Test]
    public async Task ReopenUsesRetainedContentAfterInstalledAssetChangesOrDisappears()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        // Admission already created the exact B1 pin: a pin without a prepared record grants nothing.
        await Assert.That(fixture.Git.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(attempt.B1.CommitOid);
        await Assert.That(fixture.Store.FindSubmission(attempt.AttemptId)).IsNull();
        var prepared = fixture.Prepare();
        fixture.Store.Dispose();

        var installed = Path.Combine(fixture.Assets, "software-change-pr-codex-gateway.json");
        File.WriteAllText(installed, File.ReadAllText(installed).Replace("gpt-5.6-sol", "gpt-5.7-sol"));
        using (var reopened = fixture.Git.State.Open())
        {
            await Assert.That(reopened.FindSubmission(attempt.AttemptId)).IsEqualTo(prepared);
            await Assert.That(fixture.Prepare(reopened)).IsEqualTo(prepared);
            Directory.Delete(fixture.Assets, recursive: true);
            await Assert.That(fixture.Prepare(reopened)).IsEqualTo(prepared);
            // The retained target binding is never replaced.
            await Assert.That(() => fixture.Prepare(reopened, target: "http://127.0.0.1:10")).Throws<SubmissionConflict>();
        }
        using var again = fixture.Git.State.Open();
        await Assert.That(again.FindSubmission(attempt.AttemptId)).IsEqualTo(prepared);
    }

    [Test]
    [Arguments("asset")]
    [Arguments("missing-asset")]
    [Arguments("graph")]
    [Arguments("task")]
    [Arguments("native-binding")]
    public async Task CorruptRetainedMaterialRefusesInsteadOfRegenerating(string defect)
    {
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare();
        fixture.Git.State.Execute(defect switch
        {
            "asset" => "DROP TRIGGER execution_assets_no_update; UPDATE execution_assets SET content = CAST(CAST(content AS TEXT) || char(10) AS BLOB)",
            "missing-asset" => "PRAGMA foreign_keys = OFF; DROP TRIGGER execution_assets_no_delete; DELETE FROM execution_assets",
            "graph" => "DROP TRIGGER submission_binding_stable; UPDATE native_submissions SET request_json = json_set(request_json, '$.submission.graph.root', 'other')",
            "task" => "DROP TRIGGER submission_binding_stable; UPDATE native_submissions SET request_json = json_set(request_json, '$.submission.initialInput.task', 'Foreign work')",
            _ => "DROP TRIGGER submission_binding_stable; UPDATE native_submissions SET binding_json = json_set(binding_json, '$.native.version', 'zeroshot 10.4.0')"
        });
        Directory.Delete(fixture.Assets, recursive: true); // Refusal must not depend on, or fall back to, installed files.
        await Assert.That(() => fixture.Prepare()).Throws<SubmissionConflict>();
        await Assert.That(fixture.Store.FindSubmission(prepared.AttemptId)!.IntendedRunId).IsEqualTo(prepared.IntendedRunId);
    }

    [Test]
    public async Task PreparationKeepsAuthorityPauseAndBridgeBoundaries()
    {
        using var fixture = new HttpFixture();
        var store = fixture.Store;
        var attempt = fixture.Attempt;
        var profile = new NativeProfile(Path.Combine(fixture.Git.State.Root, "native"), directOrigin: HttpFixture.Target);
        // The bridge never prepares or dispatches an HTTP Attempt, and HTTP preparation never serves a worktree one.
        await Assert.That(() => store.PrepareSubmission(attempt.AttemptId, profile)).Throws<SubmissionNotReady>();
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, profile, new ControlledTransport()))
            .Throws<SubmissionNotReady>();
        store.PauseInstallation();
        await Assert.That(() => fixture.Prepare()).Throws<InstallationPaused>();
        store.ReleaseInstallation();
        await Assert.That(store.FindSubmission(attempt.AttemptId)).IsNull();
        var prepared = fixture.Prepare();

        // Prepared-only history still yields the undispatched safety basis; nothing is deleted.
        await Assert.That((await store.StopAsync(attempt.AttemptId, "operator ended", transport: null)).Basis).IsEqualTo("no_dispatch_intent");
        await Assert.That(() => fixture.Prepare()).Throws<StaleAttempt>();
        store.RetireAttempt(attempt.AttemptId);
        store.PauseInstallation(); // The explicit replacement may be admitted and prepared, never dispatched, while paused.
        var successor = store.AdmitRetry(attempt.AttemptId, "replace");
        var replacement = fixture.Prepare(attemptId: successor.AttemptId);
        await Assert.That(replacement.IntendedRunId == prepared.IntendedRunId).IsFalse();
        await Assert.That(JsonNode.Parse(replacement.RequestJson)!["submission"]!["source"]!.ToJsonString())
            .IsEqualTo(JsonNode.Parse(prepared.RequestJson)!["submission"]!["source"]!.ToJsonString());
        await Assert.That(store.FindSubmission(attempt.AttemptId)).IsEqualTo(prepared);

        using var worktree = new AttemptFixture();
        using var local = worktree.State.Open();
        var owned = worktree.Admit(local);
        await Assert.That(() => local.PrepareHttpSubmission(owned.AttemptId, HttpFixture.Target, ExecutionAsset.LoadBundled))
            .Throws<SubmissionNotReady>();
    }

    [Test]
    public async Task AnyHttpDispatchIntentKeepsTheAttemptQuarantined()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        fixture.Prepare();
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(async () => await fixture.Store.StopAsync(attempt.AttemptId, "ended", transport: null))
            .Throws<CessationUnconfirmed>();
        await Assert.That(() => fixture.Git.State.Execute(
            $"INSERT INTO attempt_retirements VALUES ('{attempt.AttemptId}', 'no_dispatch_intent', 'now', NULL)")).Throws<SqliteException>();
        await Assert.That(fixture.Store.Status(attempt.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(attempt.AttemptId);
        await Assert.That(() => fixture.Store.AdmitRetry(attempt.AttemptId, "replace")).Throws<AttemptAdmissionError>();
    }

    [Test]
    public async Task SqlGuardsKeepHttpPreparationImmutableAndPhaseIdentityAndConflictMonotonic()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        var prepared = fixture.Prepare();
        var intended = prepared.IntendedRunId!;
        var other = Guid.CreateVersion7().ToString();
        async Task Refused(string sql) => await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();
        void Complete() => fixture.Git.State.Execute($"""
            INSERT INTO attempt_completions SELECT attempt_id, work_unit_id, contract_revision_id, '{intended}',
                '{CompletionFixture.Receipt().GetRawText()}', 'now' FROM attempts WHERE attempt_id = '{attempt.AttemptId}'
            """);

        foreach (var change in new[]
        {
            "request_json = json_set(request_json, '$.submission.title', 'other')", $"intended_run_id = '{other}'",
            "asset_sha256 = NULL", "binding_json = '{}'", "format = 'bridge'", "submission_key = 'other'",
            // A prepared record implies no dispatch, correlation, conflict or bridge block.
            $"state = 'correlated', run_id = '{intended}'", "replay_blocked_reason = 'submission_conflict'", "state = 'blocked'"
        })
            await Refused("UPDATE native_submissions SET " + change);
        await Refused("DELETE FROM native_submissions");
        await Refused("INSERT INTO native_submissions SELECT * FROM native_submissions");
        await Assert.That(() => Complete()).Throws<SqliteException>();

        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        await Refused("UPDATE native_submissions SET state = 'prepared'");
        await Assert.That(() => Complete()).Throws<SqliteException>();
        // A conflict fact may be recorded once, never cleared, and does not resolve dispatch.
        fixture.Git.State.Execute("UPDATE native_submissions SET replay_blocked_reason = 'submission_conflict'");
        await Refused("UPDATE native_submissions SET replay_blocked_reason = NULL");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        await Refused($"UPDATE native_submissions SET state = 'correlated', run_id = '{intended}', replay_blocked_reason = NULL");
        await Refused($"UPDATE native_submissions SET state = 'correlated', run_id = '{other}'");
        await Refused("UPDATE native_submissions SET state = 'blocked'");
        fixture.Git.State.Execute($"UPDATE native_submissions SET state = 'correlated', run_id = '{intended}'");
        await Refused("UPDATE native_submissions SET state = 'dispatched', run_id = NULL");
        await Refused($"UPDATE native_submissions SET run_id = '{other}'");
        await Refused("UPDATE native_submissions SET replay_blocked_reason = NULL");

        var correlated = fixture.Store.FindSubmission(attempt.AttemptId)!;
        await Assert.That(correlated.RunId).IsEqualTo(intended);
        await Assert.That(correlated.ReplayBlockedReason).IsEqualTo("submission_conflict");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);
        // The retained binding names the confirmed run for later read/stop; completion accepts the HTTP representation.
        await Assert.That(correlated.Run).IsEqualTo(new NativeRunBinding(new("direct", HttpFixture.Target, null), intended,
            "Broodling Attempt " + attempt.AttemptId, "small", new("acme/widget", "main", attempt.B1.CommitOid)));
        Complete();
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)!.RunId).IsEqualTo(intended);
    }

    [Test]
    public async Task IntendedAndConfirmedIdentitiesCannotNameAnotherAttemptsExecution()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        // A bridge worktree Attempt of a second Work Unit, dispatched but not yet correlated.
        var reference = WorkReference.Parse("acme/widget", 13);
        var revision = fixture.Store.AdmitSources(reference, [new("primary_issue", reference.IssueLocator,
            "Another request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed"))], ContractIngressTests.Propose, []).Revision;
        var bridge = fixture.Store.AdmitAttempt(revision.ContractRevisionId, fixture.Git.Repository, fixture.Git.Workspaces, fixture.Git.Head);
        fixture.Git.State.Execute($"""
            INSERT INTO worktree_provisions VALUES ('{bridge.AttemptId}', 'now');
            INSERT INTO native_submissions (attempt_id, format, submission_key, request_json, state)
                VALUES ('{bridge.AttemptId}', 'bridge', 'bridge-key', json_object(), 'prepared');
            UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = '{bridge.AttemptId}';
            """);
        var confirmed = Guid.CreateVersion7().ToString();

        using (var connection = fixture.Git.State.Connect())
        using (var transaction = connection.BeginTransaction())
        {
            void Run(string sql, string intended)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$intended", intended);
                command.Parameters.AddWithValue("$request", new JsonObject
                {
                    ["runId"] = intended, ["submission"] = new JsonObject
                    {
                        ["submissionKey"] = "broodling:http:v1:" + attempt.AttemptId,
                        ["source"] = new JsonObject { ["revision"] = attempt.B1.CommitOid }
                    }
                }.ToJsonString());
                command.Parameters.AddWithValue("$binding", new JsonObject { ["repository"] = attempt.B1.Repository }.ToJsonString());
                command.ExecuteNonQuery();
            }
            Run($"UPDATE native_submissions SET state = 'correlated', run_id = $intended WHERE attempt_id = '{bridge.AttemptId}'", confirmed);
            Run($"INSERT INTO execution_assets VALUES ('{new string('0', 64)}', X'00')", confirmed);
            const string insert = """
                INSERT INTO native_submissions (attempt_id, format, submission_key, request_json, state, intended_run_id, asset_sha256, binding_json)
                SELECT attempt_id, 'http.v1', 'broodling:http:v1:' || attempt_id, $request, 'prepared', $intended, '0000000000000000000000000000000000000000000000000000000000000000', $binding
                FROM attempts WHERE resource_kind = 'http'
                """;
            await Assert.That(() => Run(insert, confirmed)).Throws<SqliteException>();
            Run(insert, Guid.CreateVersion7().ToString()); // Only the identity collision was refused.
            transaction.Rollback();
        }

        var prepared = fixture.Prepare();
        await Assert.That(() => fixture.Git.State.Execute(
            $"UPDATE native_submissions SET state = 'correlated', run_id = '{prepared.IntendedRunId}' WHERE attempt_id = '{bridge.AttemptId}'"))
            .Throws<SqliteException>();
        fixture.Git.State.Execute($"UPDATE native_submissions SET state = 'correlated', run_id = '{confirmed}' WHERE attempt_id = '{bridge.AttemptId}'");
    }
}
