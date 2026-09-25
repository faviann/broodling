using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class NativeTransportTests
{
    [Test]
    [Arguments("wrong-sdk", "zeroshot 10.3.0")]
    [Arguments("10.3.0.post1", "zeroshot wrong-native")]
    public async Task CSharpRejectsWrongSdkOrBundledNativeBeforeSubmit(string sdk, string native)
    {
        using var fixture = new NativeFixture();
        var script = Path.Combine(fixture.Root, "corrupt-submit.py");
        File.Copy(NativeFixture.Fixture("corrupt-submit.py"), script);
        File.WriteAllText(Path.ChangeExtension(script, ".version"), JsonSerializer.Serialize(new { ok = true, sdkVersion = sdk, nativeVersion = native }));
        // No .response exists: passing the version check would cross submit and fail with a different error.
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile,
            new ZeroshotTransport(NativeFixture.Python, script))).Throws<UnsupportedRuntime>();
        await Assert.That(store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("dispatched");
    }

    [Test]
    [Arguments("")]
    [Arguments("{")]
    [Arguments("null")]
    [Arguments("[]")]
    [Arguments("{\"ok\":true}")]
    [Arguments("{\"ok\":\"true\",\"runId\":\"fake\"}")]
    [Arguments("{\"ok\":true,\"runId\":\" \"}")]
    [Arguments("{\"ok\":true,\"runId\":42}")]
    [Arguments("{\"ok\":false,\"error\":\"submission_conflict\"}")]
    [Arguments("{\"ok\":false,\"error\":\"submission_conflict\",\"existingRunId\":null}")]
    [Arguments("{\"ok\":false,\"error\":\"submission_conflict\",\"existingRunId\":42}")]
    [Arguments("{\"ok\":false,\"error\":\"submission_conflict\",\"existingRunId\":\"\"}")]
    [Arguments("{\"ok\":false,\"error\":\"submission_conflict\",\"existingRunId\":\" \"}")]
    public async Task CorruptSubmitResponseLeavesDurableUnresolvedIntentAndOnlyIdenticalReplay(string response)
    {
        using var fixture = new NativeFixture();
        var script = Path.Combine(fixture.Root, "corrupt-submit.py");
        File.Copy(NativeFixture.Fixture("corrupt-submit.py"), script);
        File.WriteAllText(Path.ChangeExtension(script, ".response"), response);
        NativeSubmission retained;
        using (var store = fixture.Git.State.Open())
        {
            var attempt = fixture.Provision(store);
            await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile,
                new ZeroshotTransport(NativeFixture.Python, script))).Throws<NativeTransportError>();
            retained = store.FindSubmission(attempt.AttemptId)!;
            await Assert.That(retained.State).IsEqualTo("dispatched");
            await Assert.That(retained.RunId).IsNull();
        }
        using var reopened = fixture.Git.State.Open();
        File.WriteAllText(Path.ChangeExtension(script, ".response"), "{\"ok\":true,\"runId\":\"recovered-native-run\"}");
        var result = await reopened.DispatchAsync(retained.AttemptId, fixture.Profile, new ZeroshotTransport(NativeFixture.Python, script));
        await Assert.That(result.RequestJson).IsEqualTo(retained.RequestJson);
        await Assert.That(result.SubmissionKey).IsEqualTo(retained.SubmissionKey);
        await Assert.That(result.RunId).IsEqualTo("recovered-native-run");
    }

    [Test]
    public async Task SubmittingBridgeKeepsInitiationHeldAfterCallerCancellation()
    {
        using var fixture = new NativeFixture();
        var script = Path.Combine(fixture.Root, "held-submit.py");
        var started = Path.Combine(fixture.Root, "submit-started");
        var release = Path.Combine(fixture.Root, "submit-release");
        File.WriteAllText(script, $$"""
            import json, os, sys, time
            request = json.load(sys.stdin)
            if request["op"] == "version":
                print(json.dumps({"ok": True, "sdkVersion": "10.3.0.post1", "nativeVersion": "zeroshot 10.3.0"}))
            else:
                open({{JsonSerializer.Serialize(started)}}, "w").close()
                while not os.path.exists({{JsonSerializer.Serialize(release)}}):
                    time.sleep(0.05)
                print(json.dumps({"ok": True, "runId": "held-run"}))
            """);
        var lockPath = Path.Combine(fixture.Root, "direct-initiation.lock");
        Task<string> submit;
        Stopwatch clock;
        using var cancellation = new CancellationTokenSource();
        using (var initiation = fixture.DirectInitiation())
        {
            var transport = (IInitiationAwareNativeTransport)new ZeroshotTransport(NativeFixture.Python, script);
            submit = transport.SubmitAsync("{}", initiation, cancellation.Token);
            clock = Stopwatch.StartNew();
            while (!File.Exists(started) && clock.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(50);
            await Assert.That(File.Exists(started)).IsTrue();
            cancellation.Cancel();
            await Assert.That(async () => await submit).Throws<OperationCanceledException>();
        }
        await Assert.That(AdministrativeGitProcess.EnclosureLock.IsFree(lockPath)).IsFalse();
        File.WriteAllText(release, "");
        await Assert.That(submit.IsCanceled).IsTrue();
        clock = Stopwatch.StartNew(); // A concurrent fork in this host may share the description until its exec.
        bool free;
        while (!(free = AdministrativeGitProcess.EnclosureLock.IsFree(lockPath)) && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        await Assert.That(free).IsTrue();
    }

    [Test]
    public async Task ReleasedSdkReplayConflictsOwnedHeadRecoveryAndNullOutput()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = NativeFixture.Transport();
        var prepared = store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
        // Accept through the real released SDK, then lose only the caller's acknowledgment.
        string? acceptedId = null;
        var lostAck = new ControlledTransport { Submit = async request =>
        {
            acceptedId = await transport.SubmitAsync(request);
            throw new NativeTransportError();
        } };
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, lostAck)).Throws<NativeTransportError>();
        var result = await transport.WaitAsync(prepared.Frozen.Run(acceptedId!));
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Output.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(result.Failure).IsNull();
        await Assert.That(await transport.SubmitAsync(prepared.RequestJson)).IsEqualTo(acceptedId);
        var different = JsonNode.Parse(prepared.RequestJson)!;
        different["task"] = "Actually different task";
        var realConflict = await Assert.ThrowsAsync<SubmissionConflict>(async () => await transport.SubmitAsync(different.ToJsonString()));
        await Assert.That(realConflict!.ExistingRunId).IsEqualTo(acceptedId);
        File.WriteAllText(Path.Combine(attempt.Allocation.WorktreePath, "original.txt"), "owned native progress\n");
        AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "add", ".");
        AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "commit", "-m", "owned native progress");
        var driftConflict = await Assert.ThrowsAsync<SubmissionConflict>(async () => await transport.SubmitAsync(prepared.RequestJson));
        await Assert.That(driftConflict!.ExistingRunId).IsEqualTo(realConflict.ExistingRunId);
        await Assert.That(driftConflict.Message).IsEqualTo(realConflict.Message);
        var correlated = await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        await Assert.That(correlated.RunId).IsEqualTo(acceptedId);
        // Locator-only reconnect survives loss of every dispatch-time path/profile.
        Directory.Delete(attempt.Allocation.WorktreePath, true);
        Directory.Delete(fixture.Home, true);
        Directory.Delete(fixture.CodexHome, true);
        File.Delete(fixture.Codex.RealCodex);
        var repeated = await transport.WaitAsync(correlated.Run!);
        await Assert.That(repeated.RunId).IsEqualTo(result.RunId);
        await Assert.That(repeated.Output.GetRawText()).IsEqualTo(result.Output.GetRawText());
        await Assert.That((await transport.StopAsync(correlated.Run!)).RunId).IsEqualTo(acceptedId);
        await Assert.That((await store.DispatchAsync(attempt.AttemptId, NativeFixture.Unused("/missing"), transport)).RunId).IsEqualTo(acceptedId);
        var unknown = await Assert.ThrowsAsync<NativeTransportError>(async () => await transport.WaitAsync(correlated.Frozen.Run("01a00000-0000-7000-8000-000000000000")));
        await Assert.That(unknown!.Kind).IsEqualTo("RunNotFoundError");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelOrKillWaiterDetachesWhileTheNativeRunContinues(bool kill)
    {
        using var fixture = new NativeFixture(NativeFixture.Fixture("slow-codex"));
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = NativeFixture.Transport();
        var submission = await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        if (kill)
        {
            using var waiter = transport.Start(new { op = "wait", locator = submission.Locator.Json(), runId = submission.RunId, timeout = (double?)null });
            await Task.Delay(1000);
            await Assert.That(waiter.HasExited).IsFalse();
            waiter.Kill(entireProcessTree: false);
            await waiter.WaitForExitAsync();
        }
        else
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.That(async () => await transport.WaitAsync(submission.Run!, cancellation.Token)).Throws<OperationCanceledException>();
        }
        var clock = Stopwatch.StartNew();
        var result = await transport.WaitAsync(submission.Run!);
        await Assert.That(clock.Elapsed).IsGreaterThan(TimeSpan.FromSeconds(3));
        await Assert.That(result.RunId).IsEqualTo(submission.RunId);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
    }

    [Test]
    public async Task ExplicitNativeStopIsSeparateAndReturnsStableFailureWithoutApplicationDisposition()
    {
        using var fixture = new NativeFixture(NativeFixture.Fixture("slow-codex"));
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = NativeFixture.Transport();
        var submission = await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        await Task.Delay(1000);
        var stopped = await transport.StopAsync(submission.Run!);
        var replay = await transport.WaitAsync(submission.Run!);
        await Assert.That(stopped.Succeeded).IsFalse();
        await Assert.That(stopped.Failure).IsEqualTo("force_stopped");
        await Assert.That(replay.Failure).IsEqualTo(stopped.Failure);
        await Assert.That(replay.RunId).IsEqualTo(stopped.RunId);
        await Assert.That(store.GetAttempt(attempt.AttemptId).IsCurrent).IsTrue(); // H owns abandonment, not this transport.
    }

    [Test]
    public async Task ReconnectRejectsReboundLocalStateAndNeverReachesADirectTarget()
    {
        using var fixture = new NativeFixture();
        Directory.CreateDirectory(fixture.NativeState);
        var locator = new NativeLocator("local", fixture.NativeState);
        Directory.Delete(fixture.NativeState);
        Directory.CreateSymbolicLink(fixture.NativeState, fixture.Root);
        try
        {
            await Assert.That(async () => await NativeFixture.Transport().WaitAsync(NativeFixture.Run(locator, "unknown"))).Throws<UnsupportedRuntime>();
        }
        finally { Directory.Delete(fixture.NativeState); }
        // DirectTarget is HTTP-only: the bridge refuses a direct locator before starting Python.
        await using var target = new StockTarget();
        var direct = NativeFixture.Run(new("direct", target.Origin.GetLeftPart(UriPartial.Authority)), "01a00000-0000-7000-8000-000000000000");
        await Assert.That(async () => await NativeFixture.Transport().WaitAsync(direct)).Throws<UnsupportedRuntime>();
        await Assert.That(async () => await NativeFixture.Transport().StopAsync(direct)).Throws<UnsupportedRuntime>();
        await Assert.That(target.Connections).IsEqualTo(0);
    }
}
