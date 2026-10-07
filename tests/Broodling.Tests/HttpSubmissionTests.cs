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
        Attempt = Git.Admit(Store);
    }

    internal NativeSubmission Prepare(BroodlingStore? store = null, string? attemptId = null, string target = Target) =>
        (store ?? Store).PrepareHttpSubmission(attemptId ?? Attempt.AttemptId, target, () => ExecutionAsset.Load(Assets));

    /// <summary>Prepare against a loopback target, then take only the phase steps the SQL guards allow; no submission is sent.</summary>
    internal NativeSubmission PrepareAt(Uri target, string state = "prepared")
    {
        Prepare(target: target.GetLeftPart(UriPartial.Authority));
        if (state != "prepared") Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        if (state == "correlated") Git.State.Execute("UPDATE native_submissions SET state = 'correlated', run_id = intended_run_id");
        return Store.FindSubmission(Attempt.AttemptId)!;
    }

    internal string Scalar(string sql)
    {
        using var connection = Git.State.Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    public void Dispose() { Store.Dispose(); Git.Dispose(); }
}

/// <summary>
/// An HTTP Attempt of a bundle-bound Contract whose completed RequestBundle also holds a repository file
/// and a linked issue, captured through controlled GitHub acquisition.
/// </summary>
internal sealed class BundleHttpFixture : IDisposable
{
    internal RepositoryPreparationTests.RepositoryPreparationFixture Git { get; } = new();
    internal BroodlingStore Store { get; private set; } = null!;
    internal RequestBundle Bundle { get; private set; } = null!;
    internal AttemptRecord Attempt { get; private set; } = null!;

    /// <summary>Captured bytes that are not UTF-8, so the task carries them as base64.</summary>
    private static readonly byte[] Logo = [0x89, 0x50, 0x4e, 0x47, 0x00, 0xff, 0xfe, 0x0a];

    internal static async Task<BundleHttpFixture> CreateAsync(string schema = "Schema canary — café <a&b>\n")
    {
        var fixture = new BundleHttpFixture();
        try
        {
            var git = fixture.Git;
            git.CommitFile("docs/schema.md", schema);
            git.CommitFile("docs/logo.png", Logo);
            git.SetIssue(12, "## Request\n<!-- broodling-request:v1 -->\nAdd CSV export.\n\n### Available references\n"
                + "- schema: repo:docs/schema.md\n- logo: repo:docs/logo.png\n- design: https://github.com/acme/widget/issues/7\n");
            git.SetIssue(7, "Supporting design canary.");
            var store = fixture.Store = git.State.Initialize();
            var submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
            fixture.Bundle = await store.CaptureRequestBundleAsync(submissionId, git.RepositoryRoot,
                new GitHubRepositoryCredentials("configured-token"), new GitHubIssueSource(git.Gh), git.Source);
            store.AdmitRequestBundle(submissionId, ContractIngressTests.Propose, "caller");
            fixture.Attempt = store.AdmitHttpAttempt(submissionId);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    public void Dispose() { Store?.Dispose(); Git.Dispose(); }
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

        await Assert.That(prepared.State).IsEqualTo("prepared");
        await Assert.That(prepared.RunId).IsNull();
        await Assert.That(prepared.ReplayBlockedReason).IsNull();
        await Assert.That(Guid.TryParseExact(prepared.IntendedRunId, "D", out var intended) && intended.Version == 7
            && intended.ToString() == prepared.IntendedRunId).IsTrue();
        await Assert.That(prepared.SubmissionKey).IsEqualTo("broodling:http:v1:" + attempt.AttemptId);

        var request = JsonNode.Parse(prepared.RequestJson)!.AsObject();
        var submission = request["submission"]!.AsObject();
        var asset = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Assets, DirectTargetBinding.AssetFile)))!;
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

        await Assert.That(prepared.AssetSha256).IsEqualTo(DirectTargetBinding.AssetSha256);
        using (var connection = fixture.Git.State.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT content FROM execution_assets";
            await Assert.That(((byte[])command.ExecuteScalar()!).SequenceEqual(
                File.ReadAllBytes(Path.Combine(fixture.Assets, DirectTargetBinding.AssetFile)))).IsTrue();
        }
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(prepared.BindingJson!), new JsonObject
        {
            ["protocol"] = "zeroshot.native-v2-target/v2", ["origin"] = HttpFixture.Target, ["repository"] = attempt.B1.Repository,
            ["resultOrigin"] = "https://github.com/acme/widget.git",
            ["native"] = DirectTargetBinding.Native()
        })).IsTrue();
        await Assert.That(prepared.Origin).IsEqualTo(HttpFixture.Target);

        // Preparation is neither dispatch intent nor acceptance.
        var status = fixture.Store.Status(attempt.ContractRevisionId);
        await Assert.That(status.Submissions.Single()).IsEqualTo(prepared);
        await Assert.That(status.QuarantinedAttemptIds.Count).IsEqualTo(0);
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);
        await Assert.That(await fixture.Store.ObserveAsync(attempt.AttemptId)).IsNull();
        await Assert.That(fixture.Git.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(attempt.B1.CommitOid);
        await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);
    }

    [Test]
    public async Task FrozenTaskCarriesExactTextBinaryContractAndOriginalB1AcrossNewCaptureAndReplay()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var text = "Complete authority — café\r\nNo newline normalization.\r\n";
        var status = fixture.Store.AdmitSources(WorkReference.Parse("acme/widget", 13),
            [new("primary_issue", WorkReference.Parse("acme/widget", 13).IssueLocator, Encoding.UTF8.GetBytes(text),
                entitlement: new("caller", "Reviewed supplied issue bytes")), ContractIngressTests.Supplement],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest);
        var attempt = fixture.Store.AdmitHttpAttempt(status.Revision.ContractRevisionId, fixture.Git.Repository);
        var prepared = fixture.Prepare(attemptId: attempt.AttemptId, target: target.Origin.GetLeftPart(UriPartial.Authority));
        var task = (string)JsonNode.Parse(prepared.RequestJson)!["submission"]!["initialInput"]!["task"]!;
        var authority = JsonNode.Parse(task[(task.IndexOf("\n\n", StringComparison.Ordinal) + 2)..])!;
        await Assert.That(authority["comparisonBase"]!.GetValue<string>()).IsEqualTo(fixture.Git.Head);
        await Assert.That(JsonNode.DeepEquals(authority["contract"], JsonNode.Parse(status.Revision.CanonicalBytes))).IsTrue();
        var sources = authority["admittedInstructions"]!.AsArray();
        await Assert.That((string)sources.Single(value => (string?)value!["kind"] == "primary_issue")!["content"]!).IsEqualTo(text);
        var binary = sources.Single(value => (string?)value!["kind"] == "referenced_document")!;
        await Assert.That((string)binary["encoding"]!).IsEqualTo("base64");
        await Assert.That(Convert.FromBase64String((string)binary["content"]!).SequenceEqual(ContractIngressTests.Supplement.Content)).IsTrue();
        fixture.Store.AdmitSources(WorkReference.Parse("acme/widget", 13),
            [new("primary_issue", WorkReference.Parse("acme/widget", 13).IssueLocator, "newer source"u8.ToArray(),
                entitlement: new("caller", "Reviewed supplied issue bytes"))],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest);

        using var reopened = fixture.Git.State.Open();
        var correlated = await reopened.DispatchHttpAsync(attempt.AttemptId, HttpDispatchTests.Credentials());
        await Assert.That(correlated.RequestJson).IsEqualTo(prepared.RequestJson);
        await Assert.That(correlated.SubmissionKey).IsEqualTo(prepared.SubmissionKey);
        await Assert.That((string)target.Bodies.Single()["submission"]!["initialInput"]!["task"]!).IsEqualTo(task);
    }

    [Test]
    public async Task GitRemoteAuthorityDoesNotTreatCallerShorthandAsAGitHubOrigin()
    {
        foreach (var origin in new[] { "https://github.com/acme/widget.git", "git@github.com:acme/widget.git", "ssh://git@github.com/acme/widget" })
            await Assert.That(BroodlingStore.GitHubOriginRepository(origin)).IsEqualTo("acme/widget");
        foreach (var origin in new[] { "acme/widget", "github.com/acme/widget", "/acme/widget", "https://other.invalid/acme/widget", "" })
            await Assert.That(BroodlingStore.GitHubOriginRepository(origin)).IsNull();
        await Assert.That(BroodlingStore.GitHubOriginRepository("https://github.com/ACME/Widget.git")).IsEqualTo("ACME/Widget");
    }

    [Test]
    public async Task BundleBoundTaskCarriesEveryReferenceAsItsExactCapturedBytes()
    {
        using var fixture = await BundleHttpFixture.CreateAsync();
        var prepared = fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, HttpFixture.Target);

        var task = (string)JsonNode.Parse(prepared.RequestJson)!["submission"]!["initialInput"]!["task"]!;
        var authority = JsonNode.Parse(task[(task.IndexOf("\n\n", StringComparison.Ordinal) + 2)..])!;
        var bundle = fixture.Bundle;
        await Assert.That(string.Join(",", authority.AsObject().Select(field => field.Key)))
            .IsEqualTo("contract,admittedInstructions,comparisonBase,requestBundle");
        await Assert.That((string)authority["admittedInstructions"]!.AsArray().Single()!["kind"]!).IsEqualTo("executable_request");
        var manifest = authority["requestBundle"]!;
        await Assert.That(new ContractRequestBundle((string)manifest["bundleId"]!, (string)manifest["manifestSha256"]!))
            .IsEqualTo(fixture.Store.GetContractRevision(fixture.Attempt.ContractRevisionId).Contract.RequestBundle);
        await Assert.That(string.Join(",", bundle.References.Select(reference => reference.ReferenceId)))
            .IsEqualTo("primary,request,repo:docs/schema.md,repo:docs/logo.png,github:acme/widget/issues/7");
        await Assert.That(bundle.References[2].GitCommitOid).IsEqualTo(fixture.Attempt.B1.CommitOid);
        // Every member in manifest order: identity, readable selector, digest, the pinned file of a Git capture,
        // and its exact captured bytes, as text when they are UTF-8 and as base64 otherwise.
        var entries = manifest["references"]!.AsArray();
        await Assert.That(entries.Count).IsEqualTo(bundle.References.Count);
        foreach (var (entry, reference) in entries.Zip(bundle.References))
        {
            var expected = new JsonObject
            {
                ["referenceId"] = reference.ReferenceId, ["captureKind"] = reference.CaptureKind,
                ["selector"] = JsonNode.Parse(reference.Selector), ["contentSha256"] = reference.ContentSha256
            };
            if (reference.GitPath is not null) { expected["gitCommitOid"] = reference.GitCommitOid; expected["gitPath"] = reference.GitPath; }
            var captured = fixture.Store.ReadRequestBundleReference(bundle.BundleId, reference.ReferenceId).Content;
            var base64 = reference.ReferenceId == "repo:docs/logo.png";
            expected["encoding"] = base64 ? "base64" : "utf-8";
            expected["content"] = base64 ? Convert.ToBase64String(captured) : Encoding.UTF8.GetString(captured);
            await Assert.That(JsonNode.DeepEquals(entry, expected)).IsTrue();
        }
        // Text stays as written in the task, not escaped beyond what JSON requires.
        await Assert.That(task.Contains("Schema canary — café <a&b>")).IsTrue();
    }

    [Test]
    public async Task PreparationRefusesATaskOverTheNativeBudgetWithoutRetainingAnything()
    {
        // 600 KiB of one captured file: within the capture limits, beyond what native execution accepts in its task.
        using var fixture = await BundleHttpFixture.CreateAsync(schema: new string('s', 600 * 1024));

        var refused = await Assert.That(() => fixture.Store.PrepareHttpSubmission(fixture.Attempt.AttemptId, HttpFixture.Target))
            .Throws<NativeTaskTooLarge>();
        await Assert.That(refused!.Code).IsEqualTo("task_too_large");
        await Assert.That(refused.Message).Contains($"the supported limit is {BroodlingStore.NativeTaskBytes} bytes");
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("https://github.com/acme/other.git")]
    [Arguments("https://x-access-token:canary-secret-value@github.com/acme/widget.git")]
    [Arguments("ssh://git:canary-secret-value@github.com/acme/widget.git")]
    public async Task ResultOriginMustNameAdmittedRepositoryWithoutCredentials(string? origin)
    {
        using var fixture = new HttpFixture(origin);
        await Assert.That(() => fixture.Prepare()).Throws<SubmissionNotReady>();
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.Dispose();
        await Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(fixture.Git.State.Path)).Contains("canary-secret-value")).IsFalse();
    }

    [Test]
    public async Task SshUrlResultOriginMayCarryTheGitUserLikeTheScpForm()
    {
        using var fixture = new HttpFixture("ssh://git@github.com/acme/widget.git");
        await Assert.That((string)JsonNode.Parse(fixture.Prepare().BindingJson!)!["resultOrigin"]!)
            .IsEqualTo("ssh://git@github.com/acme/widget.git");
    }

    [Test]
    public async Task TargetOriginMustBeCanonical()
    {
        using var fixture = new HttpFixture();
        await Assert.That(() => fixture.Prepare(target: "http://example.test:9")).Throws<UnsupportedRuntime>();
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

        var installed = Path.Combine(fixture.Assets, DirectTargetBinding.AssetFile);
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
        // The SDK imports exactly the retained bytes after restart, with the frozen identity and source.
        var imported = DirectTargetSubmission.Import(again.FindSubmission(attempt.AttemptId)!.RequestJson);
        await Assert.That(imported.ExportUtf8().SequenceEqual(Encoding.UTF8.GetBytes(prepared.RequestJson))).IsTrue();
        await Assert.That(imported.RunId.Value).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(imported.Submission.SubmissionKey.Value).IsEqualTo(prepared.SubmissionKey);
        await Assert.That((imported.Submission.Source.Repository.Value, imported.Submission.Source.Branch.Value,
            imported.Submission.Source.Revision.Value)).IsEqualTo(("acme/widget", "main", attempt.B1.CommitOid));
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
    public async Task PreparationKeepsAuthorityAndPauseBoundaries()
    {
        using var fixture = new HttpFixture();
        var store = fixture.Store;
        var attempt = fixture.Attempt;
        store.PauseInstallation();
        await Assert.That(() => fixture.Prepare()).Throws<InstallationPaused>();
        store.ReleaseInstallation();
        await Assert.That(store.FindSubmission(attempt.AttemptId)).IsNull();
        var prepared = fixture.Prepare();

        // Prepared-only history still yields the undispatched safety basis; nothing is deleted.
        await Assert.That((await store.StopAsync(attempt.AttemptId, "operator ended")).Basis).IsEqualTo("no_dispatch_intent");
        await Assert.That(() => fixture.Prepare()).Throws<StaleAttempt>();
        store.RetireAttempt(attempt.AttemptId);
        store.PauseInstallation(); // The explicit replacement may be admitted and prepared, never dispatched, while paused.
        var successor = store.AdmitRetry(attempt.AttemptId, "replace");
        var replacement = fixture.Prepare(attemptId: successor.AttemptId);
        await Assert.That(replacement.IntendedRunId == prepared.IntendedRunId).IsFalse();
        await Assert.That(JsonNode.Parse(replacement.RequestJson)!["submission"]!["source"]!.ToJsonString())
            .IsEqualTo(JsonNode.Parse(prepared.RequestJson)!["submission"]!["source"]!.ToJsonString());
        await Assert.That(store.FindSubmission(attempt.AttemptId)).IsEqualTo(prepared);
    }

    [Test]
    public async Task AnyHttpDispatchIntentKeepsTheAttemptQuarantined()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        fixture.Prepare();
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(async () => await fixture.Store.StopAsync(attempt.AttemptId, "ended"))
            .Throws<CessationUnconfirmed>();
        await Assert.That(() => fixture.Git.State.Execute(
            $"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at) VALUES ('{attempt.AttemptId}', 'no_dispatch_intent', 'now')")).Throws<SqliteException>();
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
            "asset_sha256 = NULL", "binding_json = '{}'", "submission_key = 'other'",
            // A prepared record implies no dispatch, correlation or conflict.
            $"state = 'correlated', run_id = '{intended}'", "replay_blocked_reason = 'submission_conflict'"
        })
            await Refused("UPDATE native_submissions SET " + change);
        await Refused("DELETE FROM native_submissions");
        await Refused("INSERT INTO native_submissions SELECT * FROM native_submissions");
        await Assert.That(() => Complete()).Throws<SqliteException>();

        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        await Refused("UPDATE native_submissions SET state = 'prepared'");
        await Refused("UPDATE native_submissions SET state = 'correlated'");
        await Assert.That(() => Complete()).Throws<SqliteException>();
        // A conflict fact may be recorded once, never cleared, and does not resolve dispatch.
        fixture.Git.State.Execute("UPDATE native_submissions SET replay_blocked_reason = 'submission_conflict'");
        await Refused("UPDATE native_submissions SET replay_blocked_reason = NULL");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        await Refused($"UPDATE native_submissions SET state = 'correlated', run_id = '{intended}', replay_blocked_reason = NULL");
        await Refused($"UPDATE native_submissions SET state = 'correlated', run_id = '{other}'");
        fixture.Git.State.Execute($"UPDATE native_submissions SET state = 'correlated', run_id = '{intended}'");
        await Refused("UPDATE native_submissions SET state = 'dispatched', run_id = NULL");
        await Refused($"UPDATE native_submissions SET run_id = '{other}'");
        await Refused("UPDATE native_submissions SET replay_blocked_reason = NULL");

        var correlated = fixture.Store.FindSubmission(attempt.AttemptId)!;
        await Assert.That(correlated.RunId).IsEqualTo(intended);
        await Assert.That(correlated.ReplayBlockedReason).IsEqualTo("submission_conflict");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);
        // The retained binding names the confirmed run for later read/stop; completion accepts the HTTP representation.
        await Assert.That(correlated.Run).IsEqualTo(new NativeRunBinding(HttpFixture.Target, intended,
            "Broodling Attempt " + attempt.AttemptId, "small", new("acme/widget", "main", attempt.B1.CommitOid)));
        Complete();
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)!.RunId).IsEqualTo(intended);
    }

    [Test]
    public async Task IntendedAndConfirmedIdentitiesCannotNameAnotherAttemptsExecution()
    {
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        // A second Work Unit's HTTP Attempt, dispatched but not yet correlated.
        var reference = WorkReference.Parse("acme/widget", 13);
        var revision = fixture.Store.AdmitSources(reference, [new("primary_issue", reference.IssueLocator,
            "Another request.\n"u8.ToArray(), entitlement: new("caller", "Reviewed"))], ContractIngressTests.Propose, ContractIngressTests.PullRequest).Revision;
        var other = fixture.Store.AdmitHttpAttempt(revision.ContractRevisionId, fixture.Git.Repository, fixture.Git.Head);
        var sent = fixture.Prepare(attemptId: other.AttemptId);
        fixture.Git.State.Execute($"UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = '{other.AttemptId}'");

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
            var insert = $"""
                INSERT INTO native_submissions (attempt_id, submission_key, request_json, state, intended_run_id, asset_sha256, binding_json)
                VALUES ('{attempt.AttemptId}', 'broodling:http:v1:{attempt.AttemptId}', $request, 'prepared', $intended, '{sent.AssetSha256}', $binding)
                """;
            // Another Attempt's intended identity, confirmed or not, is never reserved twice.
            await Assert.That(() => Run(insert, sent.IntendedRunId)).Throws<SqliteException>();
            Run(insert, Guid.CreateVersion7().ToString()); // Only the identity collision was refused.
            transaction.Rollback();
        }

        var prepared = fixture.Prepare();
        // Correlation confirms only the row's own intended identity, so no run is ever another Attempt's execution.
        await Assert.That(() => fixture.Git.State.Execute(
            $"UPDATE native_submissions SET state = 'correlated', run_id = '{prepared.IntendedRunId}' WHERE attempt_id = '{other.AttemptId}'"))
            .Throws<SqliteException>();
        fixture.Git.State.Execute($"UPDATE native_submissions SET state = 'correlated', run_id = '{sent.IntendedRunId}' WHERE attempt_id = '{other.AttemptId}'");
        await Assert.That(fixture.Store.FindSubmission(other.AttemptId)!.RunId).IsEqualTo(sent.IntendedRunId);
    }
}
