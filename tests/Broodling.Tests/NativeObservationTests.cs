using System.Diagnostics;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class NativeObservationTests
{
    [Test]
    public async Task ReleasedSdkProgressIsReadThroughRetainedIdentityWithoutChangingRetainedFacts()
    {
        using var fixture = new NativeFixture(NativeFixture.Fixture("slow-codex"));
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = NativeFixture.Transport();
        var submission = await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        var retained = RetainedJson(store, attempt);

        NativeObservation.Available? running = null;
        var clock = Stopwatch.StartNew();
        while (running is null && clock.Elapsed < TimeSpan.FromSeconds(10))
            if (await store.ObserveAsync(attempt.AttemptId, transport) is NativeObservation.Available { Progress.ActiveNodes.Count: > 0 } active)
                running = active;
        await Assert.That(running).IsNotNull();
        await Assert.That(running!.Progress.Phase).IsEqualTo("running");

        // Native completion alone is observed as progress, not consumed as a result.
        await transport.WaitAsync(submission.Locator, submission.RunId!);
        var finished = await store.ObserveAsync(attempt.AttemptId, transport) as NativeObservation.Available;
        await Assert.That(finished!.Progress.Phase).IsEqualTo("finished");
        await Assert.That(finished.Progress.ActiveNodes).IsEmpty();
        await Assert.That(RetainedJson(store, attempt)).IsEqualTo(retained);
    }

    [Test]
    [Arguments("hang", "timeout")]
    [Arguments("lost", "transport_failed")]
    public async Task UnavailableObservationIsBoundedAndLeavesRetainedFactsUnchanged(string behavior, string reason)
    {
        using var fixture = new NativeFixture();
        var script = Path.Combine(fixture.Root, "status-sdk.py");
        File.WriteAllText(script, """
            import json, sys, time
            request = json.load(sys.stdin)
            if request["op"] == "version":
                print(json.dumps({"ok": True, "sdkVersion": "10.3.0.post1", "nativeVersion": "zeroshot 10.3.0"}))
            elif "BEHAVIOR" == "hang":
                time.sleep(3600)
            else:
                sys.exit(1)
            """.Replace("BEHAVIOR", behavior));
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var uncorrelated = new ControlledTransport();
        await Assert.That(await store.ObserveAsync(attempt.AttemptId, uncorrelated)).IsNull();
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, uncorrelated);
        var retained = RetainedJson(store, attempt);

        var clock = Stopwatch.StartNew();
        var observation = await store.ObserveAsync(attempt.AttemptId, new ZeroshotTransport(NativeFixture.Python, script), TimeSpan.FromSeconds(2));
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        await Assert.That((observation as NativeObservation.Unavailable)?.Reason).IsEqualTo(reason);
        await Assert.That(RetainedJson(store, attempt)).IsEqualTo(retained);
    }

    private static string RetainedJson(BroodlingStore store, AttemptRecord attempt) =>
        JsonSerializer.Serialize(store.Status(attempt.ContractRevisionId));
}
