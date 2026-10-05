using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class DispatchProcessTests
{
    [Test]
    [Arguments("received")]
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
        // The target holds the complete request without creating the run.
        if (stage == "received") target.Submit = async _ => { reached.TrySetResult(); await hold.Task; return (500, ""); };
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
        var unknown = await Assert.That(async () => await store.StopAsync(prepared.AttemptId, "maintenance"))
            .Throws<CessationUnconfirmed>();
        await Assert.That(unknown!.NativeStopRequested).IsFalse();
        await Assert.That(target.Count("run/force")).IsEqualTo(0);

        // Only the bytes already buffered are released; nothing is replayed.
        release.SetResult();
        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        target.Projections.Enqueue(DirectTargetRunTests.Running(run));
        var progress = await store.ObserveAsync(prepared.AttemptId);
        await Assert.That(progress).IsTypeOf<NativeObservation.Available>();
        await Assert.That(progress!.Identity).IsEqualTo(NativeRunIdentity.Intended);
        target.Projections.Enqueue(DirectTargetRunTests.Running(run));
        target.Projections.Enqueue(DirectTargetRunTests.Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
        }, run));
        var stopped = await Assert.That(async () => await store.StopAsync(prepared.AttemptId, "maintenance"))
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

}
