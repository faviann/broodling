using System.Text;
using System.Text.Json;
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
        new("fixture-github-token", DirectTargetBinding.GatewayBaseUrl, "fixture-gateway-key");

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

        var status = await new Invocation(store, new InvocationTarget(target.Origin)).ResumeAsync(revision, git.Repository, b1, Credentials);
        var attempt = status.Attempts.Single();
        var submission = status.Submissions.Single();
        await Assert.That(submission.State).IsEqualTo("correlated");
        await Assert.That(submission.RunId).IsEqualTo(submission.IntendedRunId);
        // No client execution checkout, branch or runtime directory was created.
        await Assert.That(git.LocalResources()).IsEqualTo(local);
        await Finished(store, attempt.AttemptId);
        var reference = await TerminalAsync(submission.Run!);

        await target.RestartAsync();
        store.Dispose();
        store = git.State.Open();
        var completion = await store.WaitAsync(attempt.AttemptId);
        await Assert.That(completion.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(completion.DeliveryReceipt.GetRawText()).IsEqualTo(reference.GetRawText());
        await Assert.That(string.Join(",", reference.EnumerateObject().Select(field => field.Name == "headRevision"
                ? field.Name : $"{field.Name}={field.Value}").Order()))
            .IsEqualTo("headRevision,mode=pr,outcome=ready,pullRequestId=1,repository=acme/widget,targetBranch=main,version=v2");
        var accepted = completion.AcceptedRevision;
        await Assert.That(git.Git("rev-parse", "refs/broodling/accepted/" + accepted).Trim()).IsEqualTo(accepted);
        // The candidate commit starts from exact B1. Before publishing, native integrates the moved target branch
        // and routes the integration to delivery repair, whose commit follows it.
        var integration = git.Git("rev-list", "--merges", accepted).Trim();
        await Assert.That(git.Git("rev-parse", integration + "^1^").Trim()).IsEqualTo(b1);
        await Assert.That(git.Git("rev-parse", integration + "^2").Trim()).IsEqualTo(moved);
        await Assert.That(git.Git("rev-parse", accepted + "^").Trim()).IsEqualTo(integration);
        await Assert.That(git.Git("show", accepted + ":delivery-repairs.jsonl")).Contains("\"outcome\": \"repair_required\"")
            .And.Contains("integrated captured target revision " + moved);
        await Assert.That(AttemptFixture.RunGit(target.Forge, "rev-parse", "main").Trim()).IsEqualTo(moved);

        await target.DisposeAsync();
        using (var offline = git.State.Open())
            await Assert.That(await offline.WaitAsync(attempt.AttemptId)).IsEqualTo(completion);
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
        var invocation = new Invocation(store, new InvocationTarget(target.Origin));
        var unresolved = await LoseAcknowledgementAsync(store, git, invocation, revision, b1, target);

        // Exact replay with rotated credentials converges on the same key and run.
        var replayed = await invocation.ResumeAsync(revision, credentials: new("rotated-github-token", DirectTargetBinding.GatewayBaseUrl, "rotated-key"));
        await Assert.That(replayed.Submissions.Single().RunId).IsEqualTo(unresolved.IntendedRunId);
        await Assert.That(await target.RunCountAsync()).IsEqualTo(1);
        var attempt = unresolved.AttemptId;
        await Finished(store, attempt);
        await Assert.That(async () => await store.WaitAsync(attempt)).Throws<SubmissionNotReady>();
        await Assert.That(store.GetAttempt(attempt).Abandonment!.Reason).IsEqualTo("Zeroshot run failed: native_failed");
        await Assert.That(store.FindCompletion(attempt)).IsNull();
        // No delivery branch: the target never substituted the branch tip.
        await Assert.That(AttemptFixture.RunGit(target.Forge, "for-each-ref", "--format=%(refname)").Trim()).IsEqualTo("refs/heads/main");
    }

    /// <summary>
    /// An Unresolved dispatch whose send reached the target: Resume without dispatch credentials prepares the
    /// Attempt at <paramref name="b1"/> and sends nothing; the dispatch intent is then committed and the exact
    /// retained request sent, and its acknowledgement discarded, as when a reply is lost.
    /// </summary>
    internal static async Task<NativeSubmission> LoseAcknowledgementAsync(BroodlingStore store, AttemptFixture git,
        Invocation invocation, string revision, string b1, StockDirectTarget target)
    {
        await Assert.That(async () => await invocation.ResumeAsync(revision, git.Repository, b1, null)).Throws<UnsupportedRuntime>();
        var attempt = store.Status(revision).Attempts.Single().AttemptId;
        git.State.Execute($"UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = '{attempt}'");
        var record = store.FindSubmission(attempt)!;
        await using (var client = DirectTargetClient.Open(new Uri(target.Origin), TestAccess.Live))
            await DirectTargetSubmission.SubmitAsync(client, DirectTargetSubmission.Import(record.RequestJson),
                Credentials.TargetRun(), TimeProvider.System, default);
        var unresolved = store.FindSubmission(attempt)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        await Assert.That(unresolved.RunId).IsNull();
        return unresolved;
    }

    [Test]
    public async Task NativeAgentReadsBundleReferencesFromItsSubmissionWithNoBroodlingToReach()
    {
        using var fixture = await BundleHttpFixture.CreateAsync();
        var (store, bundle, attempt) = (fixture.Store, fixture.Bundle, fixture.Attempt);
        // No Broodling reader runs anywhere, and the target is given no name or route for one.
        await using var target = await StockDirectTarget.StartAsync(fixture.Git.State.Root);
        var repository = attempt.B1.Repository;
        AttemptFixture.RunGit(repository, "config", "url." + target.Forge + ".insteadOf", "https://github.com/acme/widget.git");
        await target.PushAsync(repository, attempt.B1.CommitOid + ":refs/heads/main");

        await new Invocation(store, new InvocationTarget(target.Origin)).ResumeAsync(attempt.ContractRevisionId, credentials: Credentials);
        var completion = await store.WaitAsync(attempt.AttemptId);

        // The controlled agent committed each listed reference as its task carried it: the exact captured bytes,
        // compared through their Git blob identities so binary members are compared exactly too.
        var accepted = completion.AcceptedRevision;
        await Assert.That(AttemptFixture.RunGit(repository, "ls-tree", "--name-only", accepted + ":references").Split('\n',
            StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(bundle.References.Count);
        foreach (var (reference, index) in bundle.References.Select((reference, index) => (reference, index)))
            await Assert.That(AttemptFixture.RunGit(repository, "rev-parse", $"{accepted}:references/{index}").Trim()).IsEqualTo(
                BlobOid(store.ReadRequestBundleReference(bundle.BundleId, reference.ReferenceId).Content));
    }

    private static string BlobOid(byte[] content) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {content.Length}\0"), .. content]));

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
            if (await store.ObserveAsync(attemptId) is NativeObservation.Available { Progress.Phase: "finished" }) return;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException("The controlled run did not finish.");
    }

    /// <summary>The finished run's own terminal output, read as the reference before anything is consumed.</summary>
    internal static async Task<JsonElement> TerminalAsync(NativeRunBinding run) =>
        (await DirectTargetRun.WaitAsync(run, TestAccess.Live, TimeProvider.System, default)).Output;
}
