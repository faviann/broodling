using System.Text;
using System.Text.Json;
using Broodling.Host;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The selected unmodified native HTTP/OECP boundary with the approved asset, driven only through the
/// application's Invocation, Observe and Wait operations. Provider, forge and PR receipt are
/// controlled: this is not a real GitHub PR or semantic-quality result.
/// </summary>
public sealed class StockDirectTargetTests
{
    internal static readonly DispatchCredentials Credentials =
        new("fixture-github-token", NativeProfile.GatewayBaseUrl, "fixture-gateway-key");

    [Test]
    public async Task ControlledPullRequestFromExactB1CompletesAndSurvivesTargetRestartAndGoingOffline()
    {
        using var git = new AttemptFixture();
        await using var target = await StockDirectTarget.StartAsync(git.State.Root);
        var (store, revision) = Admit(git, target);
        var b1 = git.Head;
        await target.PushAsync(git.Repository, "main");
        // The PR branch has moved past B1; the target must still start from exact B1.
        var moved = git.Commit("moved after admission\n");
        await target.PushAsync(git.Repository, "main");
        var local = git.LocalResources();

        var status = await new Invocation(store, new InvocationTarget.Direct(target.Origin)).ResumeAsync(revision, git.Repository, b1, Credentials);
        var attempt = status.Attempts.Single();
        var submission = status.Submissions.Single();
        await Assert.That(attempt.ResourceKind).IsEqualTo(AttemptRecord.Http);
        await Assert.That(submission.State).IsEqualTo("correlated");
        await Assert.That(submission.RunId).IsEqualTo(submission.IntendedRunId);
        // No client execution checkout, branch or runtime directory was created.
        await Assert.That(git.LocalResources()).IsEqualTo(local);
        await Finished(store, attempt.AttemptId);
        var reference = await TerminalAsync(submission.Run!);

        await target.RestartAsync();
        store.Dispose();
        store = git.State.Open();
        var completion = await store.WaitAsync(attempt.AttemptId, null);
        await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(completion.DeliveryReceipt.GetRawText()).IsEqualTo(reference.GetRawText());
        await Assert.That(string.Join(",", reference.EnumerateObject().Select(field => field.Name == "headRevision"
                ? field.Name : $"{field.Name}={field.Value}").Order()))
            .IsEqualTo("headRevision,mode=pr,outcome=opened,pullRequestId=1,repository=acme/widget,targetBranch=main,version=v1");
        var accepted = completion.AcceptedRevision;
        await Assert.That(git.Git("rev-parse", "refs/broodling/accepted/" + accepted).Trim()).IsEqualTo(accepted);
        await Assert.That(git.Git("rev-parse", accepted + "^").Trim()).IsEqualTo(b1);
        await Assert.That(AttemptFixture.RunGit(target.Forge, "rev-parse", "main").Trim()).IsEqualTo(moved);

        await target.DisposeAsync();
        using (var offline = git.State.Open())
            await Assert.That(await offline.WaitAsync(attempt.AttemptId, null)).IsEqualTo(completion);
        store.Dispose();
    }

    [Test]
    public async Task UnavailableExactB1FailsWithoutFallbackAndReplayConvergesOnTheSameRun()
    {
        using var git = new AttemptFixture();
        await using var target = await StockDirectTarget.StartAsync(git.State.Root);
        var (store, revision) = Admit(git, target);
        using var _ = store;
        await target.PushAsync(git.Repository, "main");
        // B1 exists in local custody but was never published to the forge.
        var b1 = git.Commit("never published\n");
        var invocation = new Invocation(store, new InvocationTarget.Direct(target.Origin));

        // The stock target records the failed checkout but does not acknowledge the send.
        await Assert.That(async () => await invocation.ResumeAsync(revision, git.Repository, b1, Credentials)).Throws<NativeTransportError>();
        var attempt = store.Status(revision).Attempts.Single();
        var unresolved = store.FindSubmission(attempt.AttemptId)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        await Assert.That(unresolved.RunId).IsNull();

        // Exact replay with rotated credentials converges on the same key and run.
        var replayed = await invocation.ResumeAsync(revision, credentials: new("rotated-github-token", NativeProfile.GatewayBaseUrl, "rotated-key"));
        await Assert.That(replayed.Submissions.Single().RunId).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(await target.RunCountAsync()).IsEqualTo(1);
        await Finished(store, attempt.AttemptId);
        await Assert.That(async () => await store.WaitAsync(attempt.AttemptId, null)).Throws<SubmissionNotReady>();
        await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment!.Reason).IsEqualTo("Zeroshot run failed: native_failed");
        await Assert.That(store.FindCompletion(attempt.AttemptId)).IsNull();
        // No delivery branch: the target never substituted the branch tip.
        await Assert.That(AttemptFixture.RunGit(target.Forge, "for-each-ref", "--format=%(refname)").Trim()).IsEqualTo("refs/heads/main");
    }

    [Test]
    public async Task NativeAgentReadsBundleReferencesOnDemandThroughTheInstalledHelper()
    {
        using var fixture = await BundleHttpFixture.CreateAsync();
        var (store, bundle, attempt) = (fixture.Store, fixture.Bundle, fixture.Attempt);
        // The real read-only HTTP reader over the same retained state, bound only where the target reaches the host.
        await using var reader = BroodlingHost.Build([$"--urls=http://{await StockDirectTarget.BridgeGatewayAsync()}:0",
            "--Broodling:Store=" + fixture.Git.State.Path]);
        await reader.StartAsync();
        await using var target = await StockDirectTarget.StartAsync(fixture.Git.State.Root, new Uri(reader.Urls.Single()));
        var repository = attempt.B1.Repository;
        AttemptFixture.RunGit(repository, "config", "url." + target.Forge + ".insteadOf", "https://github.com/acme/widget.git");
        await target.PushAsync(repository, attempt.B1.CommitOid + ":refs/heads/main");

        await new Invocation(store, new InvocationTarget.Direct(target.Origin)).ResumeAsync(attempt.ContractRevisionId, credentials: Credentials);
        var completion = await store.WaitAsync(attempt.AttemptId, null);

        // The controlled agent committed what the helper printed for each listed reference: the exact retained bytes.
        var accepted = completion.AcceptedRevision;
        await Assert.That(AttemptFixture.RunGit(repository, "ls-tree", "--name-only", accepted + ":references").Split('\n',
            StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(bundle.References.Count);
        foreach (var (reference, index) in bundle.References.Select((reference, index) => (reference, index)))
            await Assert.That(AttemptFixture.RunGit(repository, "show", $"{accepted}:references/{index}")).IsEqualTo(
                Encoding.UTF8.GetString(store.ReadRequestBundleReference(bundle.BundleId, reference.ReferenceId).Content));
    }

    /// <summary>An authorized-PR revision whose result fetch resolves only to the controlled forge.</summary>
    internal static (BroodlingStore Store, string Revision) Admit(AttemptFixture git, StockDirectTarget target)
    {
        git.Git("remote", "add", "origin", "https://github.com/acme/widget.git");
        git.Git("config", "url." + target.Forge + ".insteadOf", "https://github.com/acme/widget.git");
        var store = git.State.Open();
        return (store, AttemptFixture.PullRequestRevision(store));
    }

    /// <summary>Observation only: progress never correlates or consumes the result.</summary>
    internal static async Task Finished(BroodlingStore store, string attemptId)
    {
        for (var poll = 0; poll < 90; poll++)
        {
            if (await store.ObserveAsync(attemptId, null) is NativeObservation.Available { Progress.Phase: "finished" }) return;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException("The controlled run did not finish.");
    }

    /// <summary>The target's own terminal output, read once as the reference before anything is consumed.</summary>
    internal static async Task<JsonElement> TerminalAsync(NativeRunBinding run)
    {
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, TimeProvider.System, default);
        await using var session = await DirectTargetSession.OpenAsync(run, null, budget);
        return (await session.StatusAsync(budget)).Result!.Output;
    }
}
