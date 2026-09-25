using Broodling.Host;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class InvocationTests
{
    [Test]
    public async Task ThinStopHandsBackQuarantineAndSubmitResumeStatusHistoryRetainAbandonment()
    {
        using var fixture = new NativeFixture();
        using var gh = new IssueFixture(fixture.Root);
        using var store = fixture.Git.State.Open();
        var submissionTransport = new ControlledTransport();
        var invocation = new Invocation(store, Local(fixture, submissionTransport));
        var submitted = await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
            [], fixture.Git.Repository, source: gh.Source);
        var attempt = submitted.Attempts.Single();
        var output = new StringWriter();
        var error = new StringWriter();
        var stopTransport = new ControlledTransport { Stop = (_, _, _) => throw new NativeTransportError() };
        var code = await InvocationCommands.RunAsync(["stop", fixture.Git.State.Path, attempt.AttemptId, "operator requested stop"],
            fixture.Git.State.Application, output, error, transport: stopTransport);
        await Assert.That(code).IsEqualTo(1);
        using var handback = JsonDocument.Parse(output.ToString());
        await Assert.That(handback.RootElement.GetProperty("quarantined").GetBoolean()).IsTrue();
        await Assert.That(handback.RootElement.GetProperty("attempt").GetProperty("abandonment").GetProperty("reason").GetString()).IsEqualTo("operator requested stop");
        var repeated = await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
            [], fixture.Git.Repository, source: gh.Source);
        await Assert.That(repeated.Attempts.Single().AttemptId).IsEqualTo(attempt.AttemptId);
        await Assert.That(submissionTransport.Calls).IsEqualTo(1);
        output.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, attempt.ContractRevisionId],
            fixture.Git.State.Application, output, error)).IsEqualTo(0);
        using var resumed = JsonDocument.Parse(output.ToString());
        await Assert.That(resumed.RootElement.GetProperty("quarantinedAttemptIds")[0].GetString()).IsEqualTo(attempt.AttemptId);
        gh.RemoveExecutable();
        Directory.Delete(attempt.Allocation.WorktreePath, true);
        using var reopened = fixture.Git.State.Open();
        var status = reopened.Status(attempt.ContractRevisionId);
        await Assert.That(status.Attempts.Single().Abandonment!.Reason).IsEqualTo("operator requested stop");
        await Assert.That(reopened.History(ContractIngressTests.Reference).Last().Attempts.Single()).IsEqualTo(status.Attempts.Single());
        await Assert.That(reopened.CurrentAttempt(attempt.WorkUnitId)).IsNull();
    }

    private static InvocationTarget Local(NativeFixture fixture, INativeTransport transport) =>
        new InvocationTarget.Local(fixture.Git.Workspaces, fixture.Profile, transport);

    private static readonly byte[] Issue = """
        {"number":12,"node_id":"I_12","html_url":"https://github.com/acme/widget/issues/12",
         "repository_url":"https://api.github.com/repos/acme/widget","title":"Frozen issue","body":"Complete request"}
        """u8.ToArray();

    [Test]
    public async Task DirectPullRequestCompletionFlowsThroughWaitRepeatedSubmitResumeAndOfflineOperatorInspection()
    {
        var target = new StockTarget();
        using var fixture = new NativeFixture();
        using var gh = new IssueFixture(fixture.Root);
        var effects = new[] { new RequiredEffect("pr", "Open PR", "pull_request", "main") };
        var credentials = new DispatchCredentials("fixture-token", NativeProfile.GatewayBaseUrl, "fixture-key");
        var local = fixture.Git.LocalResources();
        var proposals = 0;
        Contract Propose(ContractProposalInput input) { proposals++; return new ReviewedIssueProposal(Issue).Propose(input); }
        string revision;
        AttemptCompletion completed;
        using (var store = fixture.Git.State.Open())
        {
            var invocation = new Invocation(store, new InvocationTarget.Direct(target.Origin.GetLeftPart(UriPartial.Authority)));
            var submitted = await invocation.SubmitAsync(ContractIngressTests.Reference, Propose, effects,
                fixture.Git.Repository, credentials: credentials, source: gh.Source);
            revision = submitted.Revision.ContractRevisionId;
            var attempt = submitted.Attempts.Single();
            var submission = submitted.Submissions.Single();
            await Assert.That(attempt.ResourceKind).IsEqualTo(AttemptRecord.Http);
            await Assert.That(submission.State).IsEqualTo("correlated");
            await Assert.That(submission.RunId).IsEqualTo(submission.IntendedRunId);
            // No Python helper, SDK client state, workspace or launcher: only shared Git custody changed.
            await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);
            var output = new StringWriter(); var error = new StringWriter();
            using (var detached = new CancellationTokenSource())
            {
                detached.Cancel();
                await Assert.That(await InvocationCommands.RunAsync(["wait", store.Path, attempt.AttemptId],
                    fixture.Git.State.Application, output, error, detached.Token)).IsEqualTo(130);
            }
            store.RequireCurrentAttempt(attempt.AttemptId);
            var accepted = fixture.Git.Deliver();
            target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
            await Assert.That(await InvocationCommands.RunAsync(["wait", store.Path, attempt.AttemptId],
                fixture.Git.State.Application, output, error)).IsEqualTo(0);
            completed = store.FindCompletion(attempt.AttemptId)!;
            await Assert.That(output.ToString()).Contains(completed.AcceptedRevision);
            await target.DisposeAsync();
            await Assert.That(await invocation.WaitAsync(attempt.AttemptId)).IsEqualTo(completed);
            // Repeated submit still captures/proposes; handback must not resubmit or contact the target.
            var repeated = await invocation.SubmitAsync(ContractIngressTests.Reference, Propose, effects,
                fixture.Git.Repository, source: gh.Source);
            await Assert.That(proposals).IsEqualTo(2);
            await Assert.That(repeated.Completions.Single()).IsEqualTo(completed);
            await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
        }
        gh.RemoveExecutable();
        using var reopened = fixture.Git.State.Open();
        var resumed = await new Invocation(reopened, new InvocationTarget.Direct("http://127.0.0.1:9")).ResumeAsync(revision);
        await Assert.That(resumed.Completions.Single()).IsEqualTo(completed);
        await Assert.That(reopened.History(ContractIngressTests.Reference).Last().Completions.Single()).IsEqualTo(completed);
        foreach (var args in new[] {
            new[] { "wait", reopened.Path, completed.AttemptId }, new[] { "resume", reopened.Path, revision },
            new[] { "status", reopened.Path, revision }, new[] { "history", reopened.Path, "acme/widget", "12" } })
        {
            var output = new StringWriter(); var error = new StringWriter();
            var code = args[0] is "wait" or "resume"
                ? await InvocationCommands.RunAsync(args, fixture.Git.State.Application, output, error)
                : StoreCommands.Run(args, fixture.Git.State.Application, output, error);
            await Assert.That(code).IsEqualTo(0);
            await Assert.That(output.ToString()).Contains(completed.AttemptId);
            await Assert.That(output.ToString()).Contains("SUCCEEDED");
        }
    }

    [Test]
    public async Task PreparedResumeReconcilesFrozenInvocationWithoutRestoringMissingWorkspace()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
        Directory.Delete(attempt.Allocation.WorktreePath, true);
        var transport = new ControlledTransport();
        await Assert.That(async () => await new Invocation(store, Local(fixture, transport))
            .ResumeAsync(attempt.ContractRevisionId)).Throws<WorktreeOwnershipConflict>();
        await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsFalse();
        await Assert.That(transport.Calls).IsEqualTo(0);
        await Assert.That(store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("prepared");
    }

    [Test]
    public async Task CallableSubmitResumeAndHandbackComposeExistingAuthorityWithoutReacquisitionOrReprovision()
    {
        using var fixture = new NativeFixture();
        using var gh = new IssueFixture(fixture.Root);
        var transport = new ControlledTransport { Submit = _ => throw new NativeTransportError() };
        string revisionId;
        string attemptId;
        using (var store = fixture.Git.State.Open())
        {
            var invocation = new Invocation(store, Local(fixture, transport));
            await Assert.That(async () => await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
                [], fixture.Git.Repository, source: gh.Source)).Throws<NativeTransportError>();
            var status = store.History(ContractIngressTests.Reference).Last();
            revisionId = status.Revision.ContractRevisionId;
            attemptId = status.Attempts.Single().AttemptId;
            await Assert.That(status.Submissions.Single().State).IsEqualTo("dispatched");
        }
        gh.RemoveExecutable();
        transport.Submit = _ => Task.FromResult("callable-run");
        using var reopened = fixture.Git.State.Open();
        var recovered = await new Invocation(reopened, new InvocationTarget.Local("/unused", fixture.Profile, transport)).ResumeAsync(revisionId);
        await Assert.That(recovered.Attempts.Single().AttemptId).IsEqualTo(attemptId);
        await Assert.That(recovered.Submissions.Single().RunId).IsEqualTo("callable-run");
        Directory.Delete(recovered.Attempts.Single().Allocation.WorktreePath, true);
        Directory.Delete(fixture.Home);
        // Correlated handback needs no matching configuration, not even the same target kind.
        var rebound = await new Invocation(reopened, new InvocationTarget.Direct("http://127.0.0.1:9")).ResumeAsync(revisionId);
        await Assert.That(rebound.Submissions.Single().RunId).IsEqualTo("callable-run");
        reopened.AbandonAttempt(attemptId, "operator handback");
        var abandoned = await new Invocation(reopened, new InvocationTarget.Local("/unused", fixture.Profile, transport)).ResumeAsync(revisionId);
        await Assert.That(abandoned.Attempts.Single().Abandonment!.Reason).IsEqualTo("operator handback");
        await Assert.That(transport.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task ResumeCompletesInterruptedProvisionAndReturnsRejectionWhileChangedEffectsCannotDisplaceCurrentAttempt()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Git.Admit(store);
        var transport = new ControlledTransport();
        var invocation = new Invocation(store, new InvocationTarget.Local("/unused", fixture.Profile, transport));
        var resumed = await invocation.ResumeAsync(attempt.ContractRevisionId);
        await Assert.That(resumed.Attempts.Single().Provision).IsNotNull();
        var rejected = store.AdmitSources(WorkReference.Parse("acme/widget", 99),
            [new("primary_issue", "https://github.com/acme/widget/issues/99", Issue, entitlement: new("caller", "reviewed"))],
            ContractIngressTests.Propose, [new("merge", "Merge upstream", "merge")]);
        var handback = await invocation.ResumeAsync(rejected.Revision.ContractRevisionId);
        await Assert.That(handback.Decision!.Admitted).IsFalse();
        await Assert.That(handback.Attempts.Count).IsEqualTo(0);
        var changed = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], ContractIngressTests.Propose,
            [new("pr", "Open PR", "pull_request", "main")]);
        await Assert.That(async () => await new Invocation(store, new InvocationTarget.Direct("http://127.0.0.1:9"))
            .ResumeAsync(changed.Revision.ContractRevisionId, fixture.Git.Repository)).Throws<AttemptConflict>();
        await Assert.That(transport.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task ThinOperatorSubmitAndResumeRetainIdentifiersWithSafeErrorsAndInterruptHandback()
    {
        using var fixture = new NativeFixture();
        using var gh = new IssueFixture(fixture.Root);
        var reviewed = Path.Combine(fixture.Root, "reviewed.json");
        File.WriteAllBytes(reviewed, Issue);
        var config = Path.Combine(fixture.Root, "config.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            target = "local", pythonExecutable = NativeFixture.Python, stateDirectory = fixture.NativeState, workspaceRoot = fixture.Git.Workspaces,
            realCodex = fixture.Codex.RealCodex, profileHome = fixture.Home, codexHome = fixture.CodexHome, launcher = NativeFixture.Launcher
        }));
        var args = new[] { "submit", fixture.Git.State.Path, config, "acme/widget", "12", fixture.Git.Repository, fixture.Git.Head, "-", reviewed, "caller" };
        var output = new StringWriter();
        var error = new StringWriter();
        var transport = new ControlledTransport { Submit = _ => throw new System.ComponentModel.Win32Exception("SECRET_CANARY") };
        await Assert.That(await InvocationCommands.RunAsync(args, fixture.Git.State.Application, output, error, source: gh.Source, transport: transport)).IsEqualTo(1);
        await Assert.That(error.ToString().Contains("SECRET_CANARY")).IsFalse();
        await Assert.That(error.ToString().Contains("history/status")).IsTrue();
        using var store = fixture.Git.State.Open();
        var retained = store.History(ContractIngressTests.Reference).Last();
        transport.Submit = _ => throw new OperationCanceledException();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, retained.Revision.ContractRevisionId, config],
            fixture.Git.State.Application, output, error, transport: transport)).IsEqualTo(130);
        transport.Submit = _ => Task.FromResult("operator-run");
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, retained.Revision.ContractRevisionId, config],
            fixture.Git.State.Application, output, error, transport: transport)).IsEqualTo(0);
        using var result = JsonDocument.Parse(output.ToString());
        await Assert.That(result.RootElement.GetProperty("submissions")[0].GetProperty("runId").GetString()).IsEqualTo("operator-run");
        output.GetStringBuilder().Clear();
        File.Delete(config);
        gh.RemoveExecutable();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, retained.Revision.ContractRevisionId],
            fixture.Git.State.Application, output, error)).IsEqualTo(0);
    }

    [Test]
    [Arguments("secret-field")]
    [Arguments("missing-target")]
    [Arguments("unknown-target")]
    [Arguments("direct-with-python")]
    [Arguments("local-with-origin")]
    [Arguments("partial-codex")]
    [Arguments("no-codex")]
    [Arguments("http://user:CONFIG_SECRET@127.0.0.1:8123")]
    [Arguments("http://127.0.0.1:8123?token=CONFIG_SECRET")]
    [Arguments("relative-root")]
    [Arguments("http://remote.example:8123")]
    [Arguments("http://127.0.0.1:8123/")]
    [Arguments("http://127.0.0.1:0")]
    public async Task OperatorRejectsCredentialFieldsMixedKindsAndUnsupportedOriginsBeforeAllocating(string invalid)
    {
        using var fixture = new NativeFixture();
        var local = JsonSerializer.SerializeToNode(new
        {
            target = "local", pythonExecutable = NativeFixture.Python, stateDirectory = fixture.NativeState, workspaceRoot = fixture.Git.Workspaces,
            realCodex = fixture.Codex.RealCodex, profileHome = fixture.Home, codexHome = fixture.CodexHome, launcher = NativeFixture.Launcher
        })!.AsObject();
        var direct = new JsonObject { ["target"] = "direct", ["directOrigin"] = "http://127.0.0.1:8123" };
        var configuration = invalid switch
        {
            "secret-field" => With(local, "GH_TOKEN", "CONFIG_SECRET"),
            "missing-target" => Without(local, "target"),
            "unknown-target" => With(local, "target", "bridge-direct"),
            "direct-with-python" => With(direct, "pythonExecutable", NativeFixture.Python),
            "local-with-origin" => With(local, "directOrigin", "http://127.0.0.1:8123"),
            "partial-codex" => Without(local, "launcher"),
            "no-codex" => Without(Without(Without(Without(local, "realCodex"), "profileHome"), "codexHome"), "launcher"),
            "relative-root" => With(direct, "directRootCertificate", "root.crt"),
            _ => With(direct, "directOrigin", invalid)
        };
        var path = Path.Combine(fixture.Root, "invalid-config.json");
        File.WriteAllText(path, configuration.ToJsonString());
        var output = new StringWriter();
        var error = new StringWriter();
        var transport = new ControlledTransport();
        var code = await InvocationCommands.RunAsync(
            ["resume", fixture.Git.State.Path, fixture.Git.RevisionId, path, fixture.Git.Repository, fixture.Git.Head],
            fixture.Git.State.Application, output, error, transport: transport);
        await Assert.That(code).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEqualTo("");
        await Assert.That(error.ToString().Contains("CONFIG_SECRET")).IsFalse();
        await Assert.That(transport.Calls).IsEqualTo(0);
        using var store = fixture.Git.State.Open();
        await Assert.That(store.Status(fixture.Git.RevisionId).Attempts.Count).IsEqualTo(0);
    }

    private static JsonObject With(JsonObject configuration, string field, string value)
    {
        var changed = configuration.DeepClone().AsObject();
        changed[field] = value;
        return changed;
    }

    private static JsonObject Without(JsonObject configuration, string field)
    {
        var changed = configuration.DeepClone().AsObject();
        changed.Remove(field);
        return changed;
    }

    /// <summary>
    /// Over HTTPS, the configured private root alone authenticates the target for dispatch (discovery and
    /// the run request) and for stop (session, WSS and OECP); the retained origin decides where stop connects.
    /// </summary>
    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectOperatorPullRequestNeedsNoPythonHelperWorkspaceOrLauncher(bool https)
    {
        var authority = https ? PrivateAuthority.Create() : null;
        await using var target = new StockTarget(authority?.Server);
        using var fixture = new NativeFixture();
        string revision;
        using (var store = fixture.Git.State.Open()) revision = AttemptFixture.PullRequestRevision(store);
        var config = Path.Combine(fixture.Root, "direct.json");
        var direct = new JsonObject { ["target"] = "direct", ["directOrigin"] = target.Origin.GetLeftPart(UriPartial.Authority) };
        if (authority is not null)
        {
            var root = Path.Combine(fixture.Root, "zeroshot-root.crt");
            File.WriteAllText(root, authority.RootPem);
            direct["directRootCertificate"] = root;
        }
        File.WriteAllText(config, direct.ToJsonString());
        var local = fixture.Git.LocalResources();
        var output = new StringWriter();
        var error = new StringWriter();
        // Dispatch credentials are the only current secrets and come from the operator environment.
        var names = new[] { "GH_TOKEN", "GATEWAY_BASE_URL", "GATEWAY_API_KEY" };
        var saved = names.Select(Environment.GetEnvironmentVariable).ToArray();
        int code;
        try
        {
            foreach (var (name, value) in names.Zip(new[] { "github-canary", NativeProfile.GatewayBaseUrl, "gateway-canary" }))
                Environment.SetEnvironmentVariable(name, value);
            code = await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, revision, config, fixture.Git.Repository, fixture.Git.Head],
                fixture.Git.State.Application, output, error);
        }
        finally
        {
            foreach (var (name, value) in names.Zip(saved)) Environment.SetEnvironmentVariable(name, value);
        }
        await Assert.That(code).IsEqualTo(0);
        using (var resumed = JsonDocument.Parse(output.ToString()))
        {
            var summary = resumed.RootElement.GetProperty("submissions")[0];
            await Assert.That(summary.GetProperty("state").GetString()).IsEqualTo("correlated");
            // Handback output carries status facts only, not the frozen request and its execution asset.
            await Assert.That(summary.TryGetProperty("requestJson", out _)).IsFalse();
        }
        await Assert.That(output.ToString().Contains("canary")).IsFalse();
        await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);

        using var store2 = fixture.Git.State.Open();
        var attempt = store2.Status(revision).Attempts.Single();
        var submission = store2.FindSubmission(attempt.AttemptId)!;
        // A configuration naming another origin or the other kind refuses before contact or abandonment.
        var elsewhere = Path.Combine(fixture.Root, "elsewhere.json");
        var localConfig = Path.Combine(fixture.Root, "local.json");
        File.WriteAllText(elsewhere, new JsonObject { ["target"] = "direct", ["directOrigin"] = "http://127.0.0.1:9" }.ToJsonString());
        File.WriteAllText(localConfig, JsonSerializer.Serialize(new
        {
            target = "local", pythonExecutable = NativeFixture.Python, stateDirectory = fixture.NativeState, workspaceRoot = fixture.Git.Workspaces,
            realCodex = fixture.Codex.RealCodex, profileHome = fixture.Home, codexHome = fixture.CodexHome, launcher = NativeFixture.Launcher
        }));
        foreach (var mismatched in new[] { elsewhere, localConfig })
        {
            error.GetStringBuilder().Clear();
            await Assert.That(await InvocationCommands.RunAsync(["stop", fixture.Git.State.Path, attempt.AttemptId, "operator requested stop", mismatched],
                fixture.Git.State.Application, output, error)).IsEqualTo(1);
            await Assert.That(error.ToString()).Contains("submission_conflict");
            await Assert.That(await InvocationCommands.RunAsync(["wait", fixture.Git.State.Path, attempt.AttemptId, mismatched],
                fixture.Git.State.Application, output, error)).IsEqualTo(1);
        }
        await Assert.That(store2.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
        await Assert.That(target.Stages.Count(stage => stage == "discovery")).IsEqualTo(1);
        // Explicit stop forces the confirmed run; a Direct configuration supplies no Python, only the root.
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "failed", "force_stopped"));
        output.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["stop", fixture.Git.State.Path, attempt.AttemptId, "operator requested stop", config],
            fixture.Git.State.Application, output, error)).IsEqualTo(1);
        using (var handback = JsonDocument.Parse(output.ToString()))
        {
            await Assert.That(handback.RootElement.GetProperty("quarantined").GetBoolean()).IsTrue();
            await Assert.That(handback.RootElement.GetProperty("attempt").GetProperty("abandonment").GetProperty("reason").GetString())
                .IsEqualTo("operator requested stop");
            // A compact status summary, not the frozen request and its execution asset.
            await Assert.That(handback.RootElement.GetProperty("submission").ToString()).IsEqualTo(JsonSerializer.Serialize(new
            {
                attemptId = attempt.AttemptId, format = NativeSubmission.Http, state = "correlated",
                intendedRunId = submission.IntendedRunId, runId = submission.RunId, replayBlockedReason = (string?)null
            }));
        }
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        // Ended authority is handed back from the Attempt's state; nothing is sent again.
        output.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, revision, config],
            fixture.Git.State.Application, output, error)).IsEqualTo(0);
        using (var resumed = JsonDocument.Parse(output.ToString()))
            await Assert.That(resumed.RootElement.GetProperty("attempts")[0].GetProperty("isCurrent").GetBoolean()).IsFalse();
        await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
    }

    [Test]
    public async Task OperatorStopOfADispatchedLocalRunWithoutPythonRefusesBeforeAbandoning()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport());
        var output = new StringWriter();
        var error = new StringWriter();
        await Assert.That(await InvocationCommands.RunAsync(["stop", fixture.Git.State.Path, attempt.AttemptId, "operator requested stop"],
            fixture.Git.State.Application, output, error)).IsEqualTo(1);
        using var refusal = JsonDocument.Parse(error.ToString());
        await Assert.That(refusal.RootElement.GetProperty("error").GetString()).IsEqualTo("python_required");
        await Assert.That(output.ToString()).IsEqualTo("");
        // Nothing was abandoned, so a later stop with the LocalTarget configuration can still request native stop.
        await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
        store.RequireCurrentAttempt(attempt.AttemptId);

        // The configuration's pinned SDK Python is the bridge the retained LocalTarget record uses.
        var config = Path.Combine(fixture.Root, "local.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            target = "local", pythonExecutable = NativeFixture.Python, stateDirectory = fixture.NativeState, workspaceRoot = fixture.Git.Workspaces,
            realCodex = fixture.Codex.RealCodex, profileHome = fixture.Home, codexHome = fixture.CodexHome, launcher = NativeFixture.Launcher
        }));
        error.GetStringBuilder().Clear();
        await InvocationCommands.RunAsync(["stop", fixture.Git.State.Path, attempt.AttemptId, "operator requested stop", config],
            fixture.Git.State.Application, output, error);
        await Assert.That(error.ToString()).DoesNotContain("python_required");
        await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment!.Reason).IsEqualTo("operator requested stop");
    }

    [Test]
    public async Task RetainedResourceKindDecidesTargetAndAMismatchRefusesBeforeContact()
    {
        await using var target = new StockTarget();
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var direct = new Invocation(store, new InvocationTarget.Direct(target.Origin.GetLeftPart(UriPartial.Authority)));
        var worktree = fixture.Git.Admit(store);
        await Assert.That(async () => await direct.ResumeAsync(worktree.ContractRevisionId)).Throws<UnsupportedRuntime>();
        await Assert.That(store.FindSubmission(worktree.AttemptId)).IsNull();
        // No-effect work never becomes an HTTP Attempt.
        using var other = new NativeFixture();
        using var otherStore = other.Git.State.Open();
        await Assert.That(async () => await new Invocation(otherStore, new InvocationTarget.Direct(target.Origin.GetLeftPart(UriPartial.Authority)))
            .ResumeAsync(other.Git.RevisionId, other.Git.Repository, other.Git.Head)).Throws<AttemptAdmissionError>();
        await Assert.That(otherStore.Status(other.Git.RevisionId).Attempts.Count).IsEqualTo(0);
        // Authorized PR work never falls back to the bridge.
        var pullRequest = AttemptFixture.PullRequestRevision(otherStore);
        var bridge = new ControlledTransport();
        await Assert.That(async () => await new Invocation(otherStore, Local(other, bridge))
            .ResumeAsync(pullRequest, other.Git.Repository, other.Git.Head)).Throws<AttemptAdmissionError>();
        await Assert.That(otherStore.Status(pullRequest).Attempts.Count).IsEqualTo(0);
        await Assert.That(bridge.Calls).IsEqualTo(0);

        using var http = new HttpFixture();
        var transport = new ControlledTransport();
        await Assert.That(async () => await new Invocation(http.Store, new InvocationTarget.Local(http.Git.Workspaces,
            NativeFixture.Unused(Path.Combine(http.Git.State.Root, "native")), transport)).ResumeAsync(http.Attempt.ContractRevisionId))
            .Throws<UnsupportedRuntime>();
        await Assert.That(http.Store.FindSubmission(http.Attempt.AttemptId)).IsNull();
        // A prepared HTTP Attempt keeps its retained origin; a differently configured one is refused.
        var prepared = http.Prepare(target: target.Origin.GetLeftPart(UriPartial.Authority));
        await Assert.That(async () => await new Invocation(http.Store, new InvocationTarget.Direct("http://127.0.0.1:9"))
            .ResumeAsync(http.Attempt.ContractRevisionId)).Throws<SubmissionConflict>();
        await Assert.That(http.Store.FindSubmission(http.Attempt.AttemptId)).IsEqualTo(prepared);
        await Assert.That(transport.Calls).IsEqualTo(0);
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    private sealed class IssueFixture : IDisposable
    {
        private readonly string executable;
        internal GitHubIssueSource Source { get; }
        internal IssueFixture(string root)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            executable = Path.Combine(root, "gh");
            var response = Path.Combine(root, "issue-response");
            File.WriteAllBytes(response, Issue);
            ExecutableFile.Write(executable, "#!/bin/sh\ncat '" + response + "'\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Source = new(executable);
        }
        internal void RemoveExecutable() => File.Delete(executable);
        public void Dispose() => RemoveExecutable();
    }
}
