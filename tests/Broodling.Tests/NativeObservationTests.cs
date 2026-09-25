using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
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
        {
            if (await store.ObserveAsync(attempt.AttemptId, transport) is NativeObservation.Available { Progress.ActiveNodes.Count: > 0 } active)
                running = active;
            else await Task.Delay(200);
        }
        await Assert.That(running).IsNotNull();
        await Assert.That(running!.Progress.Phase).IsEqualTo("running");

        // Native completion alone is observed as progress, not consumed as a result.
        await transport.WaitAsync(submission.Run!);
        var readStartedAt = DateTimeOffset.UtcNow;
        var finished = await store.ObserveAsync(attempt.AttemptId, transport) as NativeObservation.Available;
        var readEndedAt = DateTimeOffset.UtcNow;
        await Assert.That(finished!.Progress.Phase).IsEqualTo("finished");
        await Assert.That(finished.ObservedAt).IsGreaterThanOrEqualTo(readStartedAt);
        await Assert.That(finished.ObservedAt).IsLessThanOrEqualTo(readEndedAt);
        await Assert.That(finished.ObservedAt).IsGreaterThan(running.ObservedAt);
        await Assert.That(RetainedJson(store, attempt)).IsEqualTo(retained);
    }

    [Test]
    public async Task VersionPreflightIsBoundedAndCancellationCannotStartStatusLater()
    {
        using var fixture = new NativeFixture();
        var script = Path.Combine(fixture.Root, "held-version.py");
        var started = Path.Combine(fixture.Root, "version-started");
        var release = Path.Combine(fixture.Root, "version-release");
        var statusStarted = Path.Combine(fixture.Root, "status-started");
        File.WriteAllText(script, $$$"""
            import json, os, sys, time
            request = json.load(sys.stdin)
            if request["op"] == "version":
                open({{{JsonSerializer.Serialize(started)}}}, "w").close()
                while not os.path.exists({{{JsonSerializer.Serialize(release)}}}):
                    time.sleep(0.02)
                print(json.dumps({"ok": True, "sdkVersion": "10.3.0.post1", "nativeVersion": "zeroshot 10.3.0"}))
            else:
                open({{{JsonSerializer.Serialize(statusStarted)}}}, "w").close()
                print(json.dumps({"ok": True, "result": {"runId": request["runId"], "phase": "running", "activeNodes": []}}))
            """);
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport());
        var retained = RetainedJson(store, attempt);
        var transport = new ZeroshotTransport(NativeFixture.Python, script);

        var clock = Stopwatch.StartNew();
        var timedRead = store.ObserveAsync(attempt.AttemptId, transport, TimeSpan.FromMilliseconds(500), default);
        try
        {
            var unavailable = await timedRead.WaitAsync(TimeSpan.FromSeconds(3)) as NativeObservation.Unavailable;
            await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
            await Assert.That(unavailable?.Reason).IsEqualTo("TimeoutError");
        }
        finally
        {
            File.WriteAllText(release, "");
            await timedRead.WaitAsync(TimeSpan.FromSeconds(3));
        }
        await Assert.That(File.Exists(statusStarted)).IsFalse();

        File.Delete(started);
        File.Delete(release);
        using var cancellation = new CancellationTokenSource();
        var cancelledRead = store.ObserveAsync(attempt.AttemptId, transport, TimeSpan.FromSeconds(2), cancellation.Token);
        clock.Restart();
        while (!File.Exists(started) && clock.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
        await Assert.That(File.Exists(started)).IsTrue();
        cancellation.Cancel();
        await Assert.That(async () => await cancelledRead.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
        File.WriteAllText(release, "");
        await Task.Delay(300);
        await Assert.That(File.Exists(statusStarted)).IsFalse();
        await Assert.That(RetainedJson(store, attempt)).IsEqualTo(retained);
    }

    [Test]
    [Arguments("hang", "TimeoutError")]
    [Arguments("lost", "TargetError")]
    public async Task UnavailableObservationIsBoundedAndLeavesRetainedFactsUnchanged(string behavior, string reason)
    {
        using var fixture = new NativeFixture();
        // Stub only the SDK's status read behind the production bridge.
        var script = Path.Combine(fixture.Root, "status-sdk.py");
        File.WriteAllText(script, $$"""
            import asyncio, importlib.metadata, importlib.resources, runpy, subprocess, sys, types
            from pathlib import Path

            class TargetError(Exception):
                pass

            class Run:
                async def status(self):
                    if {{JsonSerializer.Serialize(behavior)}} == "hang":
                        await asyncio.sleep(30)
                    raise TargetError("target unavailable")

            class Client:
                def __init__(self, *, target, environment):
                    pass
                async def __aenter__(self):
                    return self
                async def __aexit__(self, *args):
                    pass
                def get_run(self, run_id):
                    return Run()

            sdk = types.ModuleType("zeroshot")
            sdk.Client, sdk.Preset, sdk.UniformRuntime = Client, None, None
            sdk.LocalTarget = sdk.DirectTarget = lambda *args, **kwargs: None
            errors = types.ModuleType("zeroshot.run_errors")
            errors.SubmissionConflictError = type("SubmissionConflictError", (Exception,), {})
            sys.modules["zeroshot"], sys.modules["zeroshot.run_errors"] = sdk, errors
            importlib.metadata.version = lambda _: "10.3.0.post1"
            importlib.resources.files = lambda _: Path("/unused-stub-native")
            subprocess.run = lambda *args, **kwargs: types.SimpleNamespace(stdout="zeroshot 10.3.0")
            runpy.run_path({{JsonSerializer.Serialize(Path.Combine(AppContext.BaseDirectory, "bridge", "zeroshot_bridge.py"))}}, run_name="__main__")
            """);
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var uncorrelated = new ControlledTransport();
        await Assert.That(await store.ObserveAsync(attempt.AttemptId, uncorrelated)).IsNull();
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, uncorrelated);
        var retained = RetainedJson(store, attempt);

        var clock = Stopwatch.StartNew();
        var observation = await store.ObserveAsync(attempt.AttemptId, new ZeroshotTransport(NativeFixture.Python, script), TimeSpan.FromSeconds(2), default);
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        await Assert.That((observation as NativeObservation.Unavailable)?.Reason).IsEqualTo(reason);
        await Assert.That(RetainedJson(store, attempt)).IsEqualTo(retained);
    }

    [Test]
    public async Task PreparedHttpAttemptHasNoProgressAndMakesNoContact()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin);
        await Assert.That(await fixture.Store.ObserveAsync(fixture.Attempt.AttemptId, null)).IsNull();
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    [Test]
    [Arguments("dispatched", NativeRunIdentity.Intended)]
    [Arguments("abandoned-paused", NativeRunIdentity.Intended)]
    [Arguments("correlated", NativeRunIdentity.Confirmed)]
    public async Task HttpProgressNamesTheIdentityItReadAndRetainsNothing(string state, NativeRunIdentity identity)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin, state == "correlated" ? "correlated" : "dispatched");
        if (state == "abandoned-paused")
        {
            fixture.Store.AbandonAttempt(fixture.Attempt.AttemptId, "Operator stopped observing authority.");
            fixture.Store.PauseInstallation();
        }
        // A finished projection is still progress; it consumes no result and correlates nothing.
        var run = prepared.Frozen.Run(prepared.IntendedRunId!);
        target.Projections.Enqueue(DirectTargetSessionTests.Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "succeeded", ["output"] = null }
        }, run));
        var retained = RetainedJson(fixture.Store, fixture.Attempt);

        var observation = await fixture.Store.ObserveAsync(fixture.Attempt.AttemptId, null) as NativeObservation.Available;
        await Assert.That(observation!.Identity).IsEqualTo(identity);
        await Assert.That(observation.Progress.Phase).IsEqualTo("finished");
        await Assert.That((string)target.Messages.Last()["params"]!["runId"]!).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(RetainedJson(fixture.Store, fixture.Attempt)).IsEqualTo(retained);
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!.State).IsEqualTo(state == "correlated" ? "correlated" : "dispatched");
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    [Arguments("unknown", "RunNotFoundError")]
    [Arguments("foreign", "foreign_run")]
    [Arguments("stalled", "TimeoutError")]
    [Arguments("malformed", "invalid_response")]
    public async Task IntendedHttpProgressIsUnavailableUnlessTheExactRunAnswersInTime(string answer, string reason)
    {
        await using var target = new StockTarget { StallAt = answer == "stalled" ? "run/status" : null };
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin, "dispatched");
        var run = prepared.Frozen.Run(prepared.IntendedRunId!);
        if (answer == "unknown")
            target.Reply = (request, id) => (string)request["method"]! != "run/status" ? null
                : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject
                    { ["code"] = -32000, ["message"] = "run was not found", ["data"] = new JsonObject { ["code"] = "NOT_FOUND" } } }.ToJsonString();
        // The exact run, but one status string is an undecodable lone surrogate.
        if (answer == "malformed")
            target.Reply = (request, id) => (string)request["method"]! != "run/status" ? null
                : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = DirectTargetSessionTests.Running(run) }
                    .ToJsonString().Replace("opaque-cursor", "\\uDC00");
        target.Projections.Enqueue(DirectTargetSessionTests.Running(run with { Title = "Another run" }));
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;

        var read = fixture.Store.ObserveAsync(fixture.Attempt.AttemptId, null);
        if (answer == "stalled")
        {
            await target.Stalled.Task.WaitAsync(DirectTargetSessionTests.Patience);
            clock.Advance(DirectTargetLimits.Progress);
        }
        var observation = await read.WaitAsync(DirectTargetSessionTests.Patience) as NativeObservation.Unavailable;
        await Assert.That(observation!.Identity).IsEqualTo(NativeRunIdentity.Intended);
        await Assert.That(observation.Reason).IsEqualTo(reason);
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!.State).IsEqualTo("dispatched");
    }

    private static string RetainedJson(BroodlingStore store, AttemptRecord attempt) =>
        JsonSerializer.Serialize(store.Status(attempt.ContractRevisionId));
}
