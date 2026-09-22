using Broodling.Host;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class InvocationTests
{
    private static readonly byte[] Issue = """
        {"number":12,"node_id":"I_12","html_url":"https://github.com/acme/widget/issues/12",
         "repository_url":"https://api.github.com/repos/acme/widget","title":"Frozen issue","body":"Complete request"}
        """u8.ToArray();

    [Test]
    public async Task PreparedResumeReconcilesFrozenInvocationWithoutRestoringMissingWorkspace()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
        Directory.Delete(attempt.Allocation.WorktreePath, true);
        var transport = new ControlledTransport();
        await Assert.That(async () => await new Invocation(store, fixture.Git.Workspaces, fixture.Profile, transport)
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
        var transport = new ControlledTransport { Submit = (_, _) => throw new NativeTransportError() };
        string revisionId;
        string attemptId;
        using (var store = fixture.Git.State.Open())
        {
            var invocation = new Invocation(store, fixture.Git.Workspaces, fixture.Profile, transport);
            await Assert.That(async () => await invocation.SubmitAsync(ContractIngressTests.Reference, new ReviewedIssueProposal(Issue).Propose,
                [], fixture.Git.Repository, source: gh.Source)).Throws<NativeTransportError>();
            var status = store.History(ContractIngressTests.Reference).Last();
            revisionId = status.Revision.ContractRevisionId;
            attemptId = status.Attempts.Single().AttemptId;
            await Assert.That(status.Submissions.Single().State).IsEqualTo("dispatched");
        }
        gh.RemoveExecutable();
        transport.Submit = (_, _) => Task.FromResult("callable-run");
        using var reopened = fixture.Git.State.Open();
        var recovered = await new Invocation(reopened, "/unused", fixture.Profile, transport).ResumeAsync(revisionId);
        await Assert.That(recovered.Attempts.Single().AttemptId).IsEqualTo(attemptId);
        await Assert.That(recovered.Submissions.Single().RunId).IsEqualTo("callable-run");
        Directory.Delete(recovered.Attempts.Single().Allocation.WorktreePath, true);
        Directory.Delete(fixture.Home);
        var rebound = await new Invocation(reopened, "/unused", new NativeProfile("/unusable", directOrigin: "http://changed.invalid"),
            new ControlledTransport { Submit = (_, _) => throw new Exception("Must not dispatch") }).ResumeAsync(revisionId);
        await Assert.That(rebound.Submissions.Single().RunId).IsEqualTo("callable-run");
        reopened.AbandonAttempt(attemptId, "operator handback");
        var abandoned = await new Invocation(reopened, "/unused", fixture.Profile, transport).ResumeAsync(revisionId);
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
        var invocation = new Invocation(store, "/unused", fixture.Profile, transport);
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
        await Assert.That(async () => await invocation.ResumeAsync(changed.Revision.ContractRevisionId, fixture.Git.Repository)).Throws<AttemptConflict>();
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
            pythonExecutable = NativeFixture.Python, stateDirectory = fixture.NativeState, workspaceRoot = fixture.Git.Workspaces,
            directOrigin = "http://127.0.0.1:8123",
            realCodex = fixture.Codex.RealCodex, profileHome = fixture.Home, codexHome = fixture.CodexHome, launcher = NativeFixture.Launcher
        }));
        var args = new[] { "submit", fixture.Git.State.Path, config, "acme/widget", "12", fixture.Git.Repository, fixture.Git.Head, "-", reviewed, "caller" };
        var output = new StringWriter();
        var error = new StringWriter();
        var transport = new ControlledTransport { Submit = (_, _) => throw new System.ComponentModel.Win32Exception("SECRET_CANARY") };
        await Assert.That(await InvocationCommands.RunAsync(args, fixture.Git.State.Application, output, error, source: gh.Source, transport: transport)).IsEqualTo(1);
        await Assert.That(error.ToString().Contains("SECRET_CANARY")).IsFalse();
        await Assert.That(error.ToString().Contains("history/status")).IsTrue();
        using var store = fixture.Git.State.Open();
        var retained = store.History(ContractIngressTests.Reference).Last();
        transport.Submit = (_, _) => throw new OperationCanceledException();
        await Assert.That(await InvocationCommands.RunAsync(["resume", fixture.Git.State.Path, retained.Revision.ContractRevisionId, config],
            fixture.Git.State.Application, output, error, transport: transport)).IsEqualTo(130);
        transport.Submit = (_, _) => Task.FromResult("operator-run");
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
    [Arguments("http://user:CONFIG_SECRET@127.0.0.1:8123")]
    [Arguments("http://127.0.0.1:8123?token=CONFIG_SECRET")]
    [Arguments("http://remote.example:8123")]
    [Arguments("https://127.0.0.1:8123")]
    [Arguments("http://127.0.0.1")]
    [Arguments("http://127.0.0.1:8123/")]
    [Arguments("http://127.0.0.1:0")]
    public async Task OperatorRejectsCredentialFieldsAndUnsupportedOriginsBeforeAllocating(string invalid)
    {
        using var fixture = new NativeFixture();
        var configuration = JsonSerializer.SerializeToNode(new
        {
            pythonExecutable = NativeFixture.Python, stateDirectory = fixture.NativeState, workspaceRoot = fixture.Git.Workspaces,
            realCodex = fixture.Codex.RealCodex, profileHome = fixture.Home, codexHome = fixture.CodexHome, launcher = NativeFixture.Launcher
        })!.AsObject();
        if (invalid == "secret-field") configuration["GH_TOKEN"] = "CONFIG_SECRET";
        else configuration["directOrigin"] = invalid;
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
            File.WriteAllText(executable, "#!/bin/sh\ncat '" + response + "'\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Source = new(executable);
        }
        internal void RemoveExecutable() => File.Delete(executable);
        public void Dispose() => RemoveExecutable();
    }
}
