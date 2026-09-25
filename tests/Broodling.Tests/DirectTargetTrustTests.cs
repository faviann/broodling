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
    public async Task EachOperationReadsTheRootFileSoARegeneratedRootNeedsNoRestart()
    {
        var first = PrivateAuthority.Create();
        await using var target = new StockTarget(first.Server);
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin);
        var root = Path.Combine(fixture.Git.State.Root, "zeroshot-root.crt");
        // Opening names the root without reading it. A missing root fails dispatch before any intent or contact.
        using var store = fixture.Git.State.Application.OpenStore(fixture.Git.State.Path, root);
        await Assert.That(async () => await store.DispatchHttpAsync(fixture.Attempt.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<NativeTransportError>();
        await Assert.That(store.FindSubmission(fixture.Attempt.AttemptId)!.State).IsEqualTo("prepared");
        await Assert.That(target.Connections).IsEqualTo(0);

        File.WriteAllText(root, first.RootPem);
        var run = (await store.DispatchHttpAsync(fixture.Attempt.AttemptId, HttpDispatchTests.Credentials())).Run!;
        target.Projections.Enqueue(DirectTargetSessionTests.Running(run));
        await Assert.That(await store.ObserveAsync(fixture.Attempt.AttemptId, null)).IsTypeOf<NativeObservation.Available>();

        var second = PrivateAuthority.Create();
        target.Certificate = second.Server;
        await Assert.That((await store.ObserveAsync(fixture.Attempt.AttemptId, null) as NativeObservation.Unavailable)!.Reason)
            .IsEqualTo("transport_failed");
        File.WriteAllText(root, second.RootPem);
        target.Projections.Enqueue(DirectTargetSessionTests.Running(run));
        await Assert.That(await store.ObserveAsync(fixture.Attempt.AttemptId, null)).IsTypeOf<NativeObservation.Available>();

        // A missing root fails only the operation, before any connection; retained reads are unaffected.
        File.Delete(root);
        var connections = target.Connections;
        await Assert.That((await store.ObserveAsync(fixture.Attempt.AttemptId, null) as NativeObservation.Unavailable)!.Reason)
            .IsEqualTo("transport_failed");
        await Assert.That(target.Connections).IsEqualTo(connections);
        await Assert.That(store.Status(fixture.Attempt.ContractRevisionId).Submissions.Single().State).IsEqualTo("correlated");
    }
}
