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
        {
            if (await store.ObserveAsync(attempt.AttemptId, transport) is NativeObservation.Available { Progress.ActiveNodes.Count: > 0 } active)
                running = active;
            else await Task.Delay(200);
        }
        await Assert.That(running).IsNotNull();
        await Assert.That(running!.Progress.Phase).IsEqualTo("running");

        // Native completion alone is observed as progress, not consumed as a result.
        await transport.WaitAsync(submission.Locator, submission.RunId!);
        var finished = await store.ObserveAsync(attempt.AttemptId, transport) as NativeObservation.Available;
        await Assert.That(finished!.Progress.Phase).IsEqualTo("finished");
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

    private static string RetainedJson(BroodlingStore store, AttemptRecord attempt) =>
        JsonSerializer.Serialize(store.Status(attempt.ContractRevisionId));
}
