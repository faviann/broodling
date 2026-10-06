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
        var target = new StockTarget();
        using var fixture = GitHubRepository();
        using var gh = new IssueFixture(fixture.State.Root);
        using var store = fixture.State.Open();
        var invocation = new Invocation(store, new InvocationTarget(target.Origin.GetLeftPart(UriPartial.Authority)));
        var submitted = await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
            ContractIngressTests.PullRequest, fixture.Repository, credentials: HttpDispatchTests.Credentials(), source: gh.Source);
        var attempt = submitted.Attempts.Single();
        // The stop cannot reach the target, so native stop is never confirmed.
        await target.DisposeAsync();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await InvocationCommands.RunAsync(["stop", fixture.State.Path, attempt.AttemptId, "operator requested stop"],
            fixture.State.Application, output, error);
        await Assert.That(code).IsEqualTo(1);
        using var handback = JsonDocument.Parse(output.ToString());
        await Assert.That(handback.RootElement.GetProperty("quarantined").GetBoolean()).IsTrue();
        await Assert.That(handback.RootElement.GetProperty("attempt").GetProperty("abandonment").GetProperty("reason").GetString()).IsEqualTo("operator requested stop");
        var repeated = await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
            ContractIngressTests.PullRequest, fixture.Repository, source: gh.Source);
        await Assert.That(repeated.Attempts.Single().AttemptId).IsEqualTo(attempt.AttemptId);
        await Assert.That(target.Bodies.Count).IsEqualTo(1);
        output.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.State.Path, attempt.ContractRevisionId],
            fixture.State.Application, output, error)).IsEqualTo(0);
        using var resumed = JsonDocument.Parse(output.ToString());
        await Assert.That(resumed.RootElement.GetProperty("quarantinedAttemptIds")[0].GetString()).IsEqualTo(attempt.AttemptId);
        gh.RemoveExecutable();
        using var reopened = fixture.State.Open();
        var status = reopened.Status(attempt.ContractRevisionId);
        await Assert.That(status.Attempts.Single().Abandonment!.Reason).IsEqualTo("operator requested stop");
        await Assert.That(reopened.History(ContractIngressTests.Reference).Last().Attempts.Single()).IsEqualTo(status.Attempts.Single());
        await Assert.That(reopened.CurrentAttempt(attempt.WorkUnitId)).IsNull();
    }

    /// <summary>A source repository whose origin names the admitted GitHub repository, as HTTP preparation requires.</summary>
    private static AttemptFixture GitHubRepository()
    {
        var fixture = new AttemptFixture();
        fixture.Git("remote", "add", "origin", "https://github.com/acme/widget.git");
        return fixture;
    }

    internal static readonly byte[] Issue = """
        {"number":12,"node_id":"I_12","html_url":"https://github.com/acme/widget/issues/12",
         "repository_url":"https://api.github.com/repos/acme/widget","title":"Frozen issue","body":"Complete request"}
        """u8.ToArray();

    [Test]
    public async Task DirectPullRequestCompletionFlowsThroughWaitRepeatedSubmitResumeAndOfflineOperatorInspection()
    {
        var target = new StockTarget();
        using var fixture = GitHubRepository();
        using var gh = new IssueFixture(fixture.State.Root);
        var effects = new[] { new RequiredEffect("pr", "Open PR", "pull_request", "main") };
        var credentials = new DispatchCredentials("fixture-token", DirectTargetBinding.GatewayBaseUrl, "fixture-key");
        var local = fixture.LocalResources();
        var proposals = 0;
        Contract Propose(ContractProposalInput input) { proposals++; return new ReviewedIssueProposal(Issue).Propose(input); }
        string revision;
        AttemptCompletion completed;
        using (var store = fixture.State.Open())
        {
            var invocation = new Invocation(store, new InvocationTarget(target.Origin.GetLeftPart(UriPartial.Authority)));
            var submitted = await invocation.SubmitAsync(ContractIngressTests.Reference, Propose, effects,
                fixture.Repository, credentials: credentials, source: gh.Source);
            revision = submitted.Revision.ContractRevisionId;
            var attempt = submitted.Attempts.Single();
            var submission = submitted.Submissions.Single();
            await Assert.That(submission.State).IsEqualTo("correlated");
            await Assert.That(submission.RunId).IsEqualTo(submission.IntendedRunId);
            // No workspace, branch or client state: only shared Git custody changed.
            await Assert.That(fixture.LocalResources()).IsEqualTo(local);
            var output = new StringWriter(); var error = new StringWriter();
            // Reaching the live run needs the configuration's connection material: without it, nothing is contacted.
            await Assert.That(await InvocationCommands.RunAsync(["wait", store.Path, attempt.AttemptId],
                fixture.State.Application, output, error)).IsEqualTo(1);
            await Assert.That(error.ToString()).Contains("\"kind\":\"credentials_unavailable\"");
            await Assert.That(target.Count("run/status")).IsEqualTo(0);
            var config = Path.Combine(fixture.State.Root, "direct.json");
            File.WriteAllText(config, new JsonObject
            {
                ["directOrigin"] = target.Origin.GetLeftPart(UriPartial.Authority), ["directControlTokenFile"] = target.TokenFile
            }.ToJsonString());
            using (var detached = new CancellationTokenSource())
            {
                detached.Cancel();
                await Assert.That(await InvocationCommands.RunAsync(["wait", store.Path, attempt.AttemptId, config],
                    fixture.State.Application, output, error, detached.Token)).IsEqualTo(130);
            }
            store.RequireCurrentAttempt(attempt.AttemptId);
            var accepted = fixture.Deliver();
            target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
            await Assert.That(await InvocationCommands.RunAsync(["wait", store.Path, attempt.AttemptId, config],
                fixture.State.Application, output, error)).IsEqualTo(0);
            completed = store.FindCompletion(attempt.AttemptId)!;
            await Assert.That(output.ToString()).Contains(completed.AcceptedRevision);
            await target.DisposeAsync();
            await Assert.That(await invocation.WaitAsync(attempt.AttemptId)).IsEqualTo(completed);
            // Repeated submit still captures/proposes; handback must not resubmit or contact the target.
            var repeated = await invocation.SubmitAsync(ContractIngressTests.Reference, Propose, effects,
                fixture.Repository, source: gh.Source);
            await Assert.That(proposals).IsEqualTo(2);
            await Assert.That(repeated.Completions.Single()).IsEqualTo(completed);
            await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
        }
        gh.RemoveExecutable();
        using var reopened = fixture.State.Open();
        var resumed = await new Invocation(reopened, new InvocationTarget("http://127.0.0.1:9")).ResumeAsync(revision);
        await Assert.That(resumed.Completions.Single()).IsEqualTo(completed);
        await Assert.That(reopened.History(ContractIngressTests.Reference).Last().Completions.Single()).IsEqualTo(completed);
        foreach (var args in new[] {
            new[] { "wait", reopened.Path, completed.AttemptId }, new[] { "resume", reopened.Path, revision },
            new[] { "status", reopened.Path, revision }, new[] { "history", reopened.Path, "acme/widget", "12" } })
        {
            var output = new StringWriter(); var error = new StringWriter();
            var code = args[0] is "wait" or "resume"
                ? await InvocationCommands.RunAsync(args, fixture.State.Application, output, error)
                : StoreCommands.Run(args, fixture.State.Application, output, error);
            await Assert.That(code).IsEqualTo(0);
            await Assert.That(output.ToString()).Contains(completed.AttemptId);
            await Assert.That(output.ToString()).Contains("SUCCEEDED");
        }
    }

    [Test]
    public async Task CallableSubmitResumeAndHandbackComposeExistingAuthorityWithoutReacquisition()
    {
        await using var target = new StockTarget();
        using var fixture = GitHubRepository();
        using var gh = new IssueFixture(fixture.State.Root);
        var origin = target.Origin.GetLeftPart(UriPartial.Authority);
        target.Submit = _ => Task.FromResult((503, """{"code":"target.unavailable","message":"unavailable"}"""));
        string revisionId;
        string attemptId;
        using (var store = fixture.State.Open())
        {
            var invocation = new Invocation(store, new InvocationTarget(origin));
            await Assert.That(async () => await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
                ContractIngressTests.PullRequest, fixture.Repository, credentials: HttpDispatchTests.Credentials(), source: gh.Source))
                .Throws<NativeTransportError>();
            var status = store.History(ContractIngressTests.Reference).Last();
            revisionId = status.Revision.ContractRevisionId;
            attemptId = status.Attempts.Single().AttemptId;
            await Assert.That(status.Submissions.Single().State).IsEqualTo("dispatched");
        }
        gh.RemoveExecutable();
        target.Submit = body => Task.FromResult(target.Accept(body));
        using var reopened = fixture.State.Open();
        var recovered = await new Invocation(reopened, new InvocationTarget(origin))
            .ResumeAsync(revisionId, credentials: HttpDispatchTests.Credentials("rotated"));
        await Assert.That(recovered.Attempts.Single().AttemptId).IsEqualTo(attemptId);
        var correlated = recovered.Submissions.Single();
        await Assert.That(correlated.RunId).IsEqualTo(correlated.IntendedRunId);
        // Correlated handback needs no matching configuration or credentials.
        var rebound = await new Invocation(reopened, new InvocationTarget("http://127.0.0.1:9")).ResumeAsync(revisionId);
        await Assert.That(rebound.Submissions.Single().RunId).IsEqualTo(correlated.RunId);
        reopened.AbandonAttempt(attemptId, "operator handback");
        var abandoned = await new Invocation(reopened, new InvocationTarget(origin)).ResumeAsync(revisionId);
        await Assert.That(abandoned.Attempts.Single().Abandonment!.Reason).IsEqualTo("operator handback");
        await Assert.That(target.Bodies.Count).IsEqualTo(2);
        await Assert.That(target.Runs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ResumeReturnsRejectionAndAChangedContractCannotDisplaceTheCurrentAttempt()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        var invocation = new Invocation(store, new InvocationTarget("http://127.0.0.1:9"));
        var rejected = store.AdmitSources(WorkReference.Parse("acme/widget", 99),
            [new("primary_issue", "https://github.com/acme/widget/issues/99", Issue, entitlement: new("caller", "reviewed"))],
            ContractIngressTests.Propose, [new("merge", "Merge upstream", "merge")]);
        var handback = await invocation.ResumeAsync(rejected.Revision.ContractRevisionId);
        await Assert.That(handback.Decision!.Admitted).IsFalse();
        await Assert.That(handback.Attempts.Count).IsEqualTo(0);
        var changed = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary("A changed request."u8.ToArray())],
            ContractIngressTests.Propose, ContractIngressTests.PullRequest);
        await Assert.That(async () => await invocation.ResumeAsync(changed.Revision.ContractRevisionId, fixture.Repository))
            .Throws<AttemptConflict>();
        await Assert.That(store.Status(changed.Revision.ContractRevisionId).Attempts.Count).IsEqualTo(0);
        await Assert.That(store.FindSubmission(attempt.AttemptId)).IsNull();
    }

    [Test]
    [NotInParallel]
    public async Task ThinOperatorSubmitAndResumeRetainIdentifiersWithSafeErrorsAndInterruptHandback()
    {
        await using var target = new StockTarget();
        using var fixture = GitHubRepository();
        using var gh = new IssueFixture(fixture.State.Root);
        var reviewed = Path.Combine(fixture.State.Root, "reviewed.json");
        File.WriteAllBytes(reviewed, Issue);
        var config = Path.Combine(fixture.State.Root, "config.json");
        File.WriteAllText(config, new JsonObject
        {
            ["directOrigin"] = target.Origin.GetLeftPart(UriPartial.Authority), ["directControlTokenFile"] = target.TokenFile
        }.ToJsonString());
        var args = new[] { "submit", fixture.State.Path, config, "acme/widget", "12", fixture.Repository, fixture.Head, "main", reviewed, "caller" };
        var output = new StringWriter();
        var error = new StringWriter();
        // `-` named no effect before #233; it is not a branch.
        await Assert.That(await InvocationCommands.RunAsync(args.Select(arg => arg == "main" ? "-" : arg).ToArray(),
            fixture.State.Application, output, error, source: gh.Source)).IsEqualTo(2);
        using (var untouched = fixture.State.Open()) await Assert.That(untouched.History(ContractIngressTests.Reference).Count).IsEqualTo(1);
        target.Submit = _ => Task.FromResult((503, """{"code":"target.unavailable","message":"SECRET_CANARY"}"""));
        var names = new[] { "GH_TOKEN", "GATEWAY_BASE_URL", "GATEWAY_API_KEY" };
        var saved = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (var (name, value) in names.Zip(new[] { "github-canary", DirectTargetBinding.GatewayBaseUrl, "gateway-canary" }))
                Environment.SetEnvironmentVariable(name, value);
            await Assert.That(await InvocationCommands.RunAsync(args, fixture.State.Application, output, error, source: gh.Source)).IsEqualTo(1);
            await Assert.That(error.ToString().Contains("SECRET_CANARY")).IsFalse();
            await Assert.That(error.ToString().Contains("history/status")).IsTrue();
            using var store = fixture.State.Open();
            var retained = store.History(ContractIngressTests.Reference).Last();
            target.Submit = body => Task.FromResult(target.Accept(body));
            using (var detached = new CancellationTokenSource())
            {
                detached.Cancel();
                await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.State.Path, retained.Revision.ContractRevisionId, config],
                    fixture.State.Application, output, error, detached.Token)).IsEqualTo(130);
            }
            await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.State.Path, retained.Revision.ContractRevisionId, config],
                fixture.State.Application, output, error)).IsEqualTo(0);
            using var result = JsonDocument.Parse(output.ToString());
            var summary = result.RootElement.GetProperty("submissions")[0];
            await Assert.That(summary.GetProperty("runId").GetString()).IsEqualTo(summary.GetProperty("intendedRunId").GetString());
            output.GetStringBuilder().Clear();
            File.Delete(config);
            gh.RemoveExecutable();
            await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.State.Path, retained.Revision.ContractRevisionId],
                fixture.State.Application, output, error)).IsEqualTo(0);
            await Assert.That(target.Runs.Count).IsEqualTo(1);
        }
        finally
        {
            foreach (var (name, value) in names.Zip(saved)) Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Test]
    [Arguments("secret-field")]
    [Arguments("missing-origin")]
    [Arguments("target-kind")]
    [Arguments("python")]
    [Arguments("workspace-root")]
    [Arguments("http://user:CONFIG_SECRET@127.0.0.1:8123")]
    [Arguments("http://127.0.0.1:8123?token=CONFIG_SECRET")]
    [Arguments("relative-root")]
    [Arguments("missing-token-file")]
    [Arguments("relative-token-file")]
    [Arguments("inline-token")]
    [Arguments("http://remote.example:8123")]
    [Arguments("http://127.0.0.1:8123/")]
    [Arguments("http://127.0.0.1:0")]
    public async Task OperatorRejectsCredentialFieldsUnknownMembersAndUnsupportedOriginsBeforeAllocating(string invalid)
    {
        await using var target = new StockTarget();
        using var fixture = new AttemptFixture();
        // A refusal that were missed would reach this listening target.
        var direct = new JsonObject { ["directOrigin"] = target.Origin.GetLeftPart(UriPartial.Authority), ["directControlTokenFile"] = target.TokenFile };
        var configuration = invalid switch
        {
            "secret-field" => With(direct, "GH_TOKEN", "CONFIG_SECRET"),
            "missing-origin" => new JsonObject { ["directRootCertificate"] = "/root.crt" },
            // The former kind discriminator and the removed target's members are unmapped members like any other.
            "target-kind" => With(direct, "target", "direct"),
            "python" => With(direct, "pythonExecutable", "/usr/bin/python3"),
            "workspace-root" => With(direct, "workspaceRoot", fixture.State.Root),
            "relative-root" => With(direct, "directRootCertificate", "root.crt"),
            "missing-token-file" => Without(direct, "directControlTokenFile"),
            "relative-token-file" => With(direct, "directControlTokenFile", "control-token"),
            // A token is named by its protected file, never carried in the configuration.
            "inline-token" => With(direct, "directControlToken", "CONFIG_SECRET"),
            _ => With(direct, "directOrigin", invalid)
        };
        var path = Path.Combine(fixture.State.Root, "invalid-config.json");
        File.WriteAllText(path, configuration.ToJsonString());
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await InvocationCommands.RunAsync(
            ["resume", fixture.State.Path, fixture.RevisionId, path, fixture.Repository, fixture.Head],
            fixture.State.Application, output, error);
        await Assert.That(code).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEqualTo("");
        await Assert.That(error.ToString().Contains("CONFIG_SECRET")).IsFalse();
        await Assert.That(target.Connections).IsEqualTo(0);
        using var store = fixture.State.Open();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
    }

    private static JsonObject Without(JsonObject configuration, string field)
    {
        var changed = configuration.DeepClone().AsObject();
        changed.Remove(field);
        return changed;
    }

    private static JsonObject With(JsonObject configuration, string field, string value)
    {
        var changed = configuration.DeepClone().AsObject();
        changed[field] = value;
        return changed;
    }

    [Test]
    [NotInParallel]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DirectOperatorErrorRecordReportsAForeignAcknowledgementWithoutAdoptingIt(bool canonical)
    {
        await using var target = new StockTarget();
        // Target-controlled text that is no UUID, echoing the dispatch credential, is never reported.
        var foreign = canonical ? Guid.CreateVersion7().ToString() : "github-canary\\nforged";
        target.Submit = _ => Task.FromResult((200, $$"""{"runId":"{{foreign}}"}"""));
        using var fixture = GitHubRepository();
        var revision = fixture.RevisionId;
        var config = Path.Combine(fixture.State.Root, "direct.json");
        File.WriteAllText(config, new JsonObject
        {
            ["directOrigin"] = target.Origin.GetLeftPart(UriPartial.Authority), ["directControlTokenFile"] = target.TokenFile
        }.ToJsonString());
        var error = new StringWriter();
        var names = new[] { "GH_TOKEN", "GATEWAY_BASE_URL", "GATEWAY_API_KEY" };
        var saved = names.Select(Environment.GetEnvironmentVariable).ToArray();
        int code;
        try
        {
            foreach (var (name, value) in names.Zip(new[] { "github-canary", DirectTargetBinding.GatewayBaseUrl, "gateway-canary" }))
                Environment.SetEnvironmentVariable(name, value);
            code = await InvocationCommands.RunAsync(["resume", fixture.State.Path, revision, config, fixture.Repository, fixture.Head],
                fixture.State.Application, new StringWriter(), error);
        }
        finally
        {
            foreach (var (name, value) in names.Zip(saved)) Environment.SetEnvironmentVariable(name, value);
        }
        await Assert.That(code).IsEqualTo(1);
        using var record = JsonDocument.Parse(error.ToString());
        await Assert.That(record.RootElement.GetProperty("error").GetString()).IsEqualTo("native_transport_error");
        await Assert.That(record.RootElement.TryGetProperty("acknowledgedRunId", out var reported) ? reported.GetString() : null)
            .IsEqualTo(canonical ? foreign : null);
        await Assert.That(error.ToString().Contains("canary")).IsFalse();
        using var reopened = fixture.State.Open();
        var submission = reopened.Status(revision).Submissions.Single();
        await Assert.That(submission.State).IsEqualTo("dispatched");
        await Assert.That(submission.RunId).IsNull();
    }

    /// <summary>
    /// Over HTTPS, the configured private root alone authenticates the target for dispatch (discovery and
    /// the run request) and for stop (session, WSS and OECP); the retained origin decides where stop connects.
    /// </summary>
    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectOperatorPullRequestNeedsOnlyTheConfiguredOriginAndRoot(bool https)
    {
        var authority = https ? PrivateAuthority.Create() : null;
        await using var target = new StockTarget(authority?.Server);
        using var fixture = GitHubRepository();
        var revision = fixture.RevisionId;
        var config = Path.Combine(fixture.State.Root, "direct.json");
        var direct = new JsonObject { ["directOrigin"] = target.Origin.GetLeftPart(UriPartial.Authority), ["directControlTokenFile"] = target.TokenFile };
        if (authority is not null)
        {
            var root = Path.Combine(fixture.State.Root, "zeroshot-root.crt");
            File.WriteAllText(root, authority.RootPem);
            direct["directRootCertificate"] = root;
        }
        File.WriteAllText(config, direct.ToJsonString());
        var local = fixture.LocalResources();
        var output = new StringWriter();
        var error = new StringWriter();
        // Dispatch credentials are the only current secrets and come from the operator environment.
        var names = new[] { "GH_TOKEN", "GATEWAY_BASE_URL", "GATEWAY_API_KEY" };
        var saved = names.Select(Environment.GetEnvironmentVariable).ToArray();
        int code;
        try
        {
            foreach (var (name, value) in names.Zip(new[] { "github-canary", DirectTargetBinding.GatewayBaseUrl, "gateway-canary" }))
                Environment.SetEnvironmentVariable(name, value);
            code = await InvocationCommands.RunAsync(["resume", fixture.State.Path, revision, config, fixture.Repository, fixture.Head],
                fixture.State.Application, output, error);
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
        await Assert.That(fixture.LocalResources()).IsEqualTo(local);

        using var store2 = fixture.State.Open();
        var attempt = store2.Status(revision).Attempts.Single();
        var submission = store2.FindSubmission(attempt.AttemptId)!;
        // A configuration naming another origin refuses before contact or abandonment.
        var elsewhere = Path.Combine(fixture.State.Root, "elsewhere.json");
        File.WriteAllText(elsewhere, new JsonObject { ["directOrigin"] = "http://127.0.0.1:9", ["directControlTokenFile"] = "/nonexistent/control-token" }.ToJsonString());
        error.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["stop", fixture.State.Path, attempt.AttemptId, "operator requested stop", elsewhere],
            fixture.State.Application, output, error)).IsEqualTo(1);
        await Assert.That(error.ToString()).Contains("submission_conflict");
        await Assert.That(await InvocationCommands.RunAsync(["wait", fixture.State.Path, attempt.AttemptId, elsewhere],
            fixture.State.Application, output, error)).IsEqualTo(1);
        await Assert.That(store2.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
        // Only the one submission contacted the target; the refused configuration never did.
        await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
        // Explicit stop forces the confirmed run; the configuration supplies only the root.
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "failed", "force_stopped"));
        output.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["stop", fixture.State.Path, attempt.AttemptId, "operator requested stop", config],
            fixture.State.Application, output, error)).IsEqualTo(1);
        using (var handback = JsonDocument.Parse(output.ToString()))
        {
            await Assert.That(handback.RootElement.GetProperty("quarantined").GetBoolean()).IsTrue();
            await Assert.That(handback.RootElement.GetProperty("attempt").GetProperty("abandonment").GetProperty("reason").GetString())
                .IsEqualTo("operator requested stop");
            // A compact status summary, not the frozen request and its execution asset.
            await Assert.That(handback.RootElement.GetProperty("submission").ToString()).IsEqualTo(JsonSerializer.Serialize(new
            {
                attemptId = attempt.AttemptId, state = "correlated",
                intendedRunId = submission.IntendedRunId, runId = submission.RunId, replayBlockedReason = (string?)null
            }));
        }
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        // Ended authority is handed back from the Attempt's state; nothing is sent again.
        output.GetStringBuilder().Clear();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.State.Path, revision, config],
            fixture.State.Application, output, error)).IsEqualTo(0);
        using (var resumed = JsonDocument.Parse(output.ToString()))
            await Assert.That(resumed.RootElement.GetProperty("attempts")[0].GetProperty("isCurrent").GetBoolean()).IsFalse();
        await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
    }

    [Test]
    public async Task RetainedOriginDecidesTargetAndAMismatchRefusesBeforeContact()
    {
        await using var target = new StockTarget();
        using var http = new HttpFixture();
        // A prepared HTTP Attempt keeps its retained origin; a differently configured one is refused.
        var prepared = http.Prepare(target: target.Origin.GetLeftPart(UriPartial.Authority));
        await Assert.That(async () => await new Invocation(http.Store, new InvocationTarget("http://127.0.0.1:9"))
            .ResumeAsync(http.Attempt.ContractRevisionId)).Throws<SubmissionConflict>();
        await Assert.That(http.Store.FindSubmission(http.Attempt.AttemptId)).IsEqualTo(prepared);
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    /// <summary>A <c>gh</c> stand-in that answers every issue read with <see cref="Issue"/>.</summary>
    internal sealed class IssueFixture : IDisposable
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
