using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// Broodling's configured root reaching every HTTPS and WSS DirectTarget connection it makes through
/// the SDK, against a private authority on real loopback TLS. How the SDK validates a chain is the
/// SDK's. The operator path through configuration belongs to InvocationTests.
/// </summary>
public sealed class DirectTargetTrustTests
{
    [Test]
    [Arguments("configured-root", true)]
    [Arguments("other-root", false)]
    [Arguments("system-trust", false)]
    public async Task AReadTrustsExactlyTheConfiguredRoot(string trust, bool opens)
    {
        var authority = PrivateAuthority.Create();
        await using var target = new StockTarget(authority.Server);
        target.Projections.Enqueue(DirectTargetRunTests.Running());
        var directory = Directory.CreateTempSubdirectory("broodling-root-");
        try
        {
            var root = Path.Combine(directory.FullName, "root.crt");
            File.WriteAllText(root, trust == "other-root" ? PrivateAuthority.Create().RootPem : authority.RootPem);
            Task<NativeProgress> Read() => DirectTargetRun.ProgressAsync(DirectTargetRunTests.Binding(target.Origin),
                trust == "system-trust" ? null : root, DirectTargetLimits.Progress, new FakeTimeProvider(), default);
            if (opens)
            {
                await Assert.That((await Read()).Phase).IsEqualTo("running");
                // The session endpoint is the origin's wss route, so the OECP read crossed WSS.
                await Assert.That(string.Join(" ", target.Stages)).IsEqualTo("discovery session upgrade initialize run/status");
            }
            else
            {
                await DirectTargetRunTests.Fails(Read, "transport_failed");
                // The client refuses during the handshake, before sending any request.
                await Assert.That(target.Heads.All(head => head == "")).IsTrue();
            }
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public async Task AMissingRootFailsOnlyItsOperationAndDispatchRecordsNoIntent()
    {
        var authority = PrivateAuthority.Create();
        await using var target = new StockTarget(authority.Server);
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin);
        var root = Path.Combine(fixture.Git.State.Root, "zeroshot-root.crt");
        // Opening names the root without reading it.
        using var store = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, root);
        await Assert.That(async () => await store.DispatchHttpAsync(fixture.Attempt.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<NativeTransportError>();
        await Assert.That(store.FindSubmission(fixture.Attempt.AttemptId)!.State).IsEqualTo("prepared");
        await Assert.That(target.Connections).IsEqualTo(0);

        File.WriteAllText(root, authority.RootPem);
        await store.DispatchHttpAsync(fixture.Attempt.AttemptId, HttpDispatchTests.Credentials());
        File.Delete(root);
        var stages = target.Stages.Count;
        await Assert.That((await store.ObserveAsync(fixture.Attempt.AttemptId) as NativeObservation.Unavailable)!.Reason)
            .IsEqualTo("transport_failed");
        await Assert.That(target.Stages.Count).IsEqualTo(stages);
        await Assert.That(store.Status(fixture.Attempt.ContractRevisionId).Submissions.Single().State).IsEqualTo("correlated");
    }

    [Test]
    public async Task EachDispatchReadsTheRootAgain()
    {
        var authority = PrivateAuthority.Create();
        await using var target = new StockTarget(authority.Server);
        using var fixture = new HttpFixture();
        var id = fixture.PrepareAt(target.Origin).AttemptId;
        var root = Path.Combine(fixture.Git.State.Root, "zeroshot-root.crt");
        File.WriteAllText(root, PrivateAuthority.Create().RootPem);
        using var store = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, root);
        // A readable root of another authority: intent commits, then the handshake refuses before any request.
        var error = await Assert.That(async () => await store.DispatchHttpAsync(id, HttpDispatchTests.Credentials()))
            .Throws<NativeTransportError>();
        await Assert.That(error!.Kind).IsEqualTo("transport_failed");
        await Assert.That(target.Heads.All(head => head == "")).IsTrue();
        await Assert.That(store.FindSubmission(id)!.State).IsEqualTo("dispatched");

        // The regenerated root is read by the replay's new client.
        File.WriteAllText(root, authority.RootPem);
        await Assert.That((await store.DispatchHttpAsync(id, HttpDispatchTests.Credentials())).State).IsEqualTo("correlated");
    }

    /// <summary>Completion's own path: its status reads and the SDK's watch each connect through the configured root.</summary>
    [Test]
    public async Task CorrelatedHttpsWaitWatchesThroughTheConfiguredRoot()
    {
        var authority = PrivateAuthority.Create();
        await using var target = new StockTarget(authority.Server);
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var root = Path.Combine(fixture.Git.State.Root, "zeroshot-root.crt");
        File.WriteAllText(root, authority.RootPem);
        var accepted = fixture.Git.Deliver();
        var run = submission.Frozen.Run(submission.RunId!);
        target.Projections.Enqueue(DirectTargetRunTests.Running(run));
        target.Projections.Enqueue(DirectTargetRunTests.Running(run));
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
        using var store = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, root);
        await Assert.That((await store.WaitAsync(fixture.Attempt.AttemptId)).AcceptedRevision).IsEqualTo(accepted);
        await Assert.That(target.Count("run/watch")).IsEqualTo(1);
    }
}
