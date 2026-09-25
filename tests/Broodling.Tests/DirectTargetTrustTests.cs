using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// HTTPS and WSS DirectTarget trust against a private authority on real loopback TLS. The
/// operator path through configuration belongs to InvocationTests.
/// </summary>
public sealed class DirectTargetTrustTests
{
    [Test]
    [Arguments("configured-root", true)]
    [Arguments("other-root", false)]
    [Arguments("system-trust", false)]
    [Arguments("other-host", false)]
    public async Task SessionTrustsExactlyTheConfiguredRootForTheOriginHost(string trust, bool opens)
    {
        var authority = PrivateAuthority.Create(trust == "other-host" ? "other.test" : "localhost");
        await using var target = new StockTarget(authority.Server);
        target.Projections.Enqueue(DirectTargetSessionTests.Running());
        var directory = Directory.CreateTempSubdirectory("broodling-root-");
        try
        {
            var root = Path.Combine(directory.FullName, "root.crt");
            File.WriteAllText(root, trust == "other-root" ? PrivateAuthority.Create().RootPem : authority.RootPem);
            using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, new FakeTimeProvider(), default);
            async Task<DirectTargetRunStatus> Read()
            {
                await using var session = await DirectTargetSession.OpenAsync(DirectTargetSessionTests.Binding(target.Origin),
                    trust == "system-trust" ? null : root, budget);
                return await session.StatusAsync(budget);
            }
            if (opens)
            {
                await Assert.That((await Read()).Progress.Phase).IsEqualTo("running");
                // The session endpoint must be the origin's wss route, so the OECP reads crossed WSS.
                await Assert.That(string.Join(" ", target.Stages)).IsEqualTo("discovery session upgrade initialize run/status");
            }
            else
            {
                await DirectTargetSessionTests.Fails(Read, "transport_failed");
                // The client refuses during the handshake, before sending any request.
                await Assert.That(target.Heads.All(head => head == "")).IsTrue();
            }
        }
        finally { directory.Delete(true); }
    }

    [Test]
    public async Task EachTlsConnectionOfOneOperationRereadsTheRoot()
    {
        var first = PrivateAuthority.Create();
        var second = PrivateAuthority.Create();
        await using var target = new StockTarget(first.Server);
        target.Projections.Enqueue(DirectTargetSessionTests.Running());
        var directory = Directory.CreateTempSubdirectory("broodling-root-");
        try
        {
            var root = Path.Combine(directory.FullName, "root.crt");
            File.WriteAllText(root, first.RootPem);
            // Regenerated after the discovery connection, before the session and WSS connections of the same open.
            target.Discovery = () =>
            {
                File.WriteAllText(root, second.RootPem);
                target.Certificate = second.Server;
                return Task.CompletedTask;
            };
            using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, new FakeTimeProvider(), default);
            await using var session = await DirectTargetSession.OpenAsync(DirectTargetSessionTests.Binding(target.Origin), root, budget);
            await Assert.That((await session.StatusAsync(budget)).Progress.Phase).IsEqualTo("running");
            await Assert.That(target.Connections).IsEqualTo(3);
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
        await Assert.That((await store.ObserveAsync(fixture.Attempt.AttemptId, null) as NativeObservation.Unavailable)!.Reason)
            .IsEqualTo("transport_failed");
        await Assert.That(target.Stages.Count).IsEqualTo(stages);
        await Assert.That(store.Status(fixture.Attempt.ContractRevisionId).Submissions.Single().State).IsEqualTo("correlated");
    }

    /// <summary>Completion's own path to the reader: trust itself is covered above.</summary>
    [Test]
    public async Task CorrelatedHttpsWaitCompletesThroughTheConfiguredRoot()
    {
        var authority = PrivateAuthority.Create();
        await using var target = new StockTarget(authority.Server);
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var root = Path.Combine(fixture.Git.State.Root, "zeroshot-root.crt");
        File.WriteAllText(root, authority.RootPem);
        var accepted = fixture.Git.Deliver();
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
        using var store = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, root);
        await Assert.That((await store.WaitAsync(fixture.Attempt.AttemptId, null)).AcceptedRevision).IsEqualTo(accepted);
    }
}
