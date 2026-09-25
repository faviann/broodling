using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class DispatchProcessTests
{
    [Test]
    public async Task AbandonedActiveSubmitRemainsUndrainedThroughCallerLossThenDrainsWithoutReplay()
    {
        using var fixture = new NativeFixture();
        AttemptRecord attempt;
        using (var store = fixture.Git.State.Open()) attempt = fixture.Provision(store);
        var bridge = Path.Combine(fixture.Root, "held-submit.py");
        var started = Path.Combine(fixture.Root, "submit-started");
        var release = Path.Combine(fixture.Root, "submit-release");
        File.WriteAllText(bridge, $$"""
            import errno, json, os, sys, time
            request = json.load(sys.stdin)
            if request["op"] == "version":
                print(json.dumps({"ok": True, "sdkVersion": "10.3.0.post1", "nativeVersion": "zeroshot 10.3.0"}))
            else:
                store = os.path.realpath({{JsonSerializer.Serialize(fixture.Git.State.Path)}})
                writes = []
                for fd in os.listdir("/proc/self/fd"):
                    try:
                        if os.readlink(f"/proc/self/fd/{fd}") != store:
                            continue
                    except OSError:
                        continue
                    try:
                        os.write(int(fd), b"")
                        writes.append("writable")
                    except OSError as error:
                        writes.append("denied" if error.errno == errno.EBADF else errno.errorcode[error.errno])
                with open({{JsonSerializer.Serialize(started + ".tmp")}}, "w") as report:
                    report.write(",".join(writes))
                os.replace({{JsonSerializer.Serialize(started + ".tmp")}}, {{JsonSerializer.Serialize(started)}})
                while not os.path.exists({{JsonSerializer.Serialize(release)}}):
                    time.sleep(0.05)
                print(json.dumps({"ok": True, "runId": "held-run"}))
            """);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
            "native-bridge-crash", fixture.Git.State.Path, attempt.AttemptId, fixture.NativeState,
            fixture.Codex.RealCodex, fixture.Home, fixture.CodexHome, NativeFixture.Launcher,
            NativeFixture.Python, bridge })
            start.ArgumentList.Add(argument);
        using var caller = Process.Start(start)!;
        var error = caller.StandardError.ReadToEndAsync();
        try
        {
            var clock = Stopwatch.StartNew();
            while (!File.Exists(started) && !caller.HasExited && clock.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(50);
            if (!File.Exists(started))
            {
                var diagnostic = caller.HasExited ? await error : "caller still running";
                throw new Exception($"Submit bridge did not start; caller exited={caller.HasExited}, diagnostic={diagnostic}");
            }
            // The one inherited store descriptor is the initiation lock and must not carry write access.
            await Assert.That(File.ReadAllText(started)).IsEqualTo("denied");

            using (var observer = fixture.Git.State.Open())
            {
                var paused = observer.PauseInstallation();
                await Assert.That(paused.UnresolvedDispatches).IsEqualTo(1);
                await Assert.That(paused.InFlightInitiationDrained).IsFalse();
                await Assert.That(async () => await observer.StopAsync(attempt.AttemptId, "maintenance", new ControlledTransport()))
                    .Throws<CessationUnconfirmed>();
                var abandoned = observer.GetInstallationStatus();
                await Assert.That(observer.GetAttempt(attempt.AttemptId).Abandonment).IsNotNull();
                await Assert.That(abandoned.UnresolvedDispatches).IsEqualTo(1);
                await Assert.That(abandoned.InFlightInitiationDrained).IsFalse();

                caller.Kill(entireProcessTree: false);
                await caller.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(caller.ExitCode).IsNotEqualTo(0);

                var afterCallerLoss = observer.GetInstallationStatus();
                await Assert.That(afterCallerLoss.InFlightInitiationDrained).IsFalse();
                await Assert.That(observer.FindSubmission(attempt.AttemptId)!.State).IsEqualTo("dispatched");
                await Assert.That(observer.FindSubmission(attempt.AttemptId)!.RunId).IsNull();

                File.WriteAllText(release, "");
                clock = Stopwatch.StartNew();
                var drained = observer.GetInstallationStatus();
                while (!drained.InFlightInitiationDrained && clock.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await Task.Delay(50);
                    drained = observer.GetInstallationStatus();
                }
                await Assert.That(drained.InFlightInitiationDrained).IsTrue();
            }

            using var reopened = fixture.Git.State.Open();
            var status = reopened.GetInstallationStatus();
            await Assert.That(status.IsPaused).IsTrue();
            await Assert.That(status.UnresolvedDispatches).IsEqualTo(1);
            await Assert.That(status.InFlightInitiationDrained).IsTrue();
            var retained = reopened.FindSubmission(attempt.AttemptId)!;
            await Assert.That(retained.State).IsEqualTo("dispatched");
            await Assert.That(retained.RunId).IsNull();
            await Assert.That(reopened.GetAttempt(attempt.AttemptId).Abandonment).IsNotNull();

            reopened.ReleaseInstallation();
            var replay = new ControlledTransport();
            await Assert.That(async () => await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, replay))
                .Throws<StaleAttempt>();
            await Assert.That(replay.Calls).IsEqualTo(0);
        }
        finally
        {
            File.WriteAllText(release, "");
            if (!caller.HasExited)
            {
                caller.Kill(entireProcessTree: false);
                await caller.WaitForExitAsync();
            }
            try { await error; } catch { }
        }
    }

    [Test]
    [Arguments("discovery")]
    [Arguments("mid-body")]
    [Arguments("accepted")]
    public async Task KilledHttpCallerRetainsIntentAndReplayConvergesOnTheIntendedRun(string stage)
    {
        await using var target = new StockTarget { StallMidBody = stage == "mid-body" };
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        fixture.Store.Dispose();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (stage == "discovery") target.Discovery = async () => { reached.TrySetResult(); await hold.Task; };
        if (stage == "mid-body") _ = target.Stalled.Task.ContinueWith(_ => reached.TrySetResult());
        // The target accepts the complete request, then the acknowledgement is never delivered.
        if (stage == "accepted") target.Submit = async body => { target.Accept(body); reached.TrySetResult(); await hold.Task; return (500, ""); };

        var start = new ProcessStartInfo("dotnet") { RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
            "http-dispatch", fixture.Git.State.Path, prepared.AttemptId })
            start.ArgumentList.Add(argument);
        using var caller = Process.Start(start)!;
        var error = caller.StandardError.ReadToEndAsync();
        try
        {
            if (await Task.WhenAny(reached.Task, caller.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(40))) != reached.Task)
                throw new Exception($"HTTP caller did not reach {stage}; exited={caller.HasExited}, diagnostic={(caller.HasExited ? await error : "")}");
        }
        finally
        {
            if (!caller.HasExited) caller.Kill(entireProcessTree: false);
            await caller.WaitForExitAsync();
        }
        await Assert.That(caller.ExitCode).IsNotEqualTo(0);

        using var reopened = fixture.Git.State.Open();
        var retained = reopened.FindSubmission(prepared.AttemptId)!;
        await Assert.That(retained.State).IsEqualTo("dispatched");
        await Assert.That(retained.RunId).IsNull();
        var status = await InstallationPauseTests.SettledStatus(reopened);
        await Assert.That(status.UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(status.InFlightInitiationDrained).IsTrue();
        await Assert.That(target.Runs.Count).IsEqualTo(stage == "accepted" ? 1 : 0);

        target.StallMidBody = false;
        target.Discovery = () => Task.CompletedTask;
        target.Submit = body => Task.FromResult(target.Accept(body));
        var correlated = await reopened.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials());
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(correlated.RequestJson).IsEqualTo(prepared.RequestJson);
        await Assert.That(target.Runs.Values.Single()).IsEqualTo(prepared.IntendedRunId);
        hold.TrySetResult();
    }

    [Test]
    public async Task BufferedHttpRequestCanCreateTheRunAfterCallerDeathDrainageAndUnknownRunStop()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var run = prepared.Frozen.Run(prepared.IntendedRunId!);
        fixture.Store.Dispose();
        var buffered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The target holds the complete request bytes and accepts them only when released.
        target.Submit = async body =>
        {
            buffered.TrySetResult();
            await release.Task;
            accepted.TrySetResult();
            return (200, new JsonObject { ["runId"] = (string)body["runId"]! }.ToJsonString());
        };
        target.Reply = (request, id) => accepted.Task.IsCompleted || (string)request["method"]! != "run/status" ? null
            : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject
                { ["code"] = -32000, ["message"] = "run was not found", ["data"] = new JsonObject { ["code"] = "NOT_FOUND" } } }.ToJsonString();

        var start = new ProcessStartInfo("dotnet") { RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
            "http-dispatch", fixture.Git.State.Path, prepared.AttemptId })
            start.ArgumentList.Add(argument);
        using var caller = Process.Start(start)!;
        var error = caller.StandardError.ReadToEndAsync();
        try
        {
            if (await Task.WhenAny(buffered.Task, caller.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(40))) != buffered.Task)
                throw new Exception($"HTTP caller did not deliver its request; exited={caller.HasExited}, diagnostic={(caller.HasExited ? await error : "")}");
        }
        finally
        {
            if (!caller.HasExited) caller.Kill(entireProcessTree: false);
            await caller.WaitForExitAsync();
        }

        using var store = fixture.Git.State.Open();
        await Assert.That((await InstallationPauseTests.SettledStatus(store)).InFlightInitiationDrained).IsTrue();
        var unknown = await Assert.That(async () => await store.StopAsync(prepared.AttemptId, "maintenance", null))
            .Throws<CessationUnconfirmed>();
        await Assert.That(unknown!.NativeStopRequested).IsFalse();
        await Assert.That(target.Count("run/force")).IsEqualTo(0);

        // Only the bytes already buffered are released; nothing is replayed.
        release.SetResult();
        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        target.Projections.Enqueue(DirectTargetSessionTests.Running(run));
        var progress = await store.ObserveAsync(prepared.AttemptId, null);
        await Assert.That(progress).IsTypeOf<NativeObservation.Available>();
        await Assert.That(progress!.Identity).IsEqualTo(NativeRunIdentity.Intended);
        target.Projections.Enqueue(DirectTargetSessionTests.Running(run));
        target.Projections.Enqueue(DirectTargetSessionTests.Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
        }, run));
        var stopped = await Assert.That(async () => await store.StopAsync(prepared.AttemptId, "maintenance", null))
            .Throws<CessationUnconfirmed>();
        await Assert.That(stopped!.NativeStopRequested).IsTrue();
        await Assert.That((string)target.Messages.Last(message => (string)message["method"]! == "run/force")["params"]!["runId"]!)
            .IsEqualTo(prepared.IntendedRunId);

        // Uncertainty and quarantine remain: no correlation, replay, retirement or replacement.
        var retained = store.FindSubmission(prepared.AttemptId)!;
        await Assert.That(retained.State).IsEqualTo("dispatched");
        await Assert.That(retained.RunId).IsNull();
        await Assert.That(store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
        await Assert.That(store.FindRetirement(prepared.AttemptId)).IsNull();
        await Assert.That(() => store.RetireAttempt(prepared.AttemptId)).Throws<CessationUnconfirmed>();
        await Assert.That(() => store.AdmitRetry(prepared.AttemptId, "replace")).Throws<AttemptAdmissionError>();
        await Assert.That(async () => await store.DispatchHttpAsync(prepared.AttemptId, HttpDispatchTests.Credentials()))
            .Throws<StaleAttempt>();
        await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
    }

    [Test]
    [Arguments("during-prepare", null)]
    [Arguments("prepared", "prepared")]
    [Arguments("before-call", "dispatched")]
    [Arguments("after-accept", "dispatched")]
    [Arguments("during-correlation", "dispatched")]
    [Arguments("after-correlation", "correlated")]
    public async Task KilledCallerRetainsOnlyCommittedFactsAndReplayConvergesOnTheSameNativeRun(string mode, string? expectedState)
    {
        using var fixture = new NativeFixture();
        AttemptRecord attempt;
        using (var store = fixture.Git.State.Open()) attempt = fixture.Provision(store);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "Broodling.ProcessWitness.dll"),
            "native-crash", fixture.Git.State.Path, attempt.AttemptId, fixture.NativeState,
            fixture.Codex.RealCodex, fixture.Home, fixture.CodexHome, NativeFixture.Launcher, mode, NativeFixture.Python })
            start.ArgumentList.Add(argument);
        using var caller = Process.Start(start)!;
        var error = caller.StandardError.ReadToEndAsync();
        string? observed;
        try
        {
            observed = await caller.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
            if (observed is null) throw new Exception("Crash witness failed before gate: " + await error);
        }
        finally
        {
            if (!caller.HasExited) caller.Kill(entireProcessTree: false);
            await caller.WaitForExitAsync();
        }
        await Assert.That(caller.ExitCode).IsNotEqualTo(0);
        using var reopened = fixture.Git.State.Open();
        var retained = reopened.FindSubmission(attempt.AttemptId);
        await Assert.That(retained?.State).IsEqualTo(expectedState);
        await Assert.That(reopened.GetInstallationStatus().InFlightInitiationDrained).IsTrue();
        var correlated = await reopened.DispatchAsync(attempt.AttemptId, fixture.Profile, NativeFixture.Transport());
        if (retained is not null)
        {
            await Assert.That(correlated.RequestJson).IsEqualTo(retained.RequestJson);
            await Assert.That(correlated.SubmissionKey).IsEqualTo(retained.SubmissionKey);
        }
        if (mode is "after-accept" or "after-correlation") await Assert.That(correlated.RunId).IsEqualTo(observed);
        var terminal = await NativeFixture.Transport().WaitAsync(correlated.Run!);
        await Assert.That(terminal.Succeeded).IsTrue();
        using var again = fixture.Git.State.Open();
        await Assert.That(again.FindSubmission(attempt.AttemptId)).IsEqualTo(correlated);
        await Assert.That((await InstallationPauseTests.SettledStatus(again)).InFlightInitiationDrained).IsTrue();
    }
}
