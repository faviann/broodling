using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.DirectTargetSessionTests;

namespace Broodling.Tests;

/// <summary>
/// Wait and stop sequencing over the loopback stock target: polling cadence, budgets, cancellation
/// and force. Setup and projection validation belong to DirectTargetSessionTests.
/// </summary>
public sealed class DirectTargetRunTests
{
    [Test]
    public async Task WaitReadsImmediatelyThenTwoSecondsAfterEachNonterminalStatusWithoutAnOverallDeadline()
    {
        // Sixteen pauses take the wait past its 30-second setup budget; every cursor repeats.
        await using var target = new StockTarget();
        for (var read = 0; read < 17; read++) target.Projections.Enqueue(Running());
        target.Projections.Enqueue(Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "succeeded", ["output"] = null }
        }));
        var clock = new PollClock();
        var wait = DirectTargetRun.WaitAsync(Binding(target.Origin), clock, default);
        for (var pause = 1; pause <= 17; pause++)
        {
            await clock.NextPause();
            await Assert.That(target.Count("run/status")).IsEqualTo(pause);
            clock.Advance(DirectTargetLimits.PollDelay - TimeSpan.FromMilliseconds(1));
            await Task.Delay(20);
            await Assert.That(target.Count("run/status")).IsEqualTo(pause);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        var result = await wait.WaitAsync(Patience);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(target.Count("run/status")).IsEqualTo(18);
        await Assert.That(string.Join(" ", target.Stages.Distinct())).IsEqualTo("discovery session upgrade initialize run/status");
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("cancel-read")]
    [Arguments("cancel-pause")]
    public async Task EachLaterWaitReadHasItsOwnDeadlineAndDetachmentStartsNoOtherRead(string end)
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Running());
        target.Projections.Enqueue(null);
        var clock = new PollClock();
        using var caller = new CancellationTokenSource();
        var wait = DirectTargetRun.WaitAsync(Binding(target.Origin), clock, caller.Token);
        await clock.NextPause();
        if (end == "cancel-pause")
        {
            caller.Cancel();
            await Assert.That(async () => await wait).Throws<OperationCanceledException>();
            await Assert.That(target.Count("run/status")).IsEqualTo(1);
            return;
        }
        clock.Advance(DirectTargetLimits.PollDelay);
        await target.Stalled.Task.WaitAsync(Patience);
        clock.Advance(DirectTargetLimits.WaitRead - TimeSpan.FromMilliseconds(1));
        await Task.Delay(20);
        await Assert.That(wait.IsCompleted).IsFalse();
        if (end == "cancel-read")
        {
            caller.Cancel();
            await Assert.That(async () => await wait).Throws<OperationCanceledException>();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Fails(() => wait, "TimeoutError");
        }
        await Assert.That(target.Count("run/status")).IsEqualTo(2);
    }

    [Test]
    [Arguments("unknown", "RunNotFoundError")]
    [Arguments("foreign", "foreign_run")]
    [Arguments("malformed", "invalid_response")]
    [Arguments("unavailable", "TargetError")]
    public async Task IntendedStopSendsNoForceWithoutAMatchingStatus(string precheck, string reason)
    {
        await using var target = new StockTarget();
        var projection = Running();
        switch (precheck)
        {
            case "unknown":
                target.Reply = (request, id) => (string)request["method"]! == "run/status"
                    ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id,
                        ["error"] = new JsonObject { ["code"] = -32000, ["message"] = "run was not found", ["data"] = new JsonObject { ["code"] = "NOT_FOUND" } } }.ToJsonString()
                    : null;
                break;
            case "foreign": projection["title"] = "Another run"; break;
            case "malformed": projection["submissionKey"] = "key"; break;
            case "unavailable": target.Session = (503, """{"code":"target.unavailable","message":"busy"}"""); break;
        }
        if (precheck is "foreign" or "malformed") target.Projections.Enqueue(projection);
        target.Projections.Enqueue(Finished("force_stopped")); // What a force would get.
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), NativeRunIdentity.Intended, new FakeTimeProvider(), default);

        await Assert.That(stop).IsEqualTo(new DirectTargetStop(DirectTargetForce.NotSent, null, reason));
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StopForcesTheRunOnceAndReturnsItsTerminalReply(bool intended)
    {
        var identity = intended ? NativeRunIdentity.Intended : NativeRunIdentity.Confirmed;
        await using var target = new StockTarget();
        // Even a finished precheck is still followed by the explicit force.
        if (identity == NativeRunIdentity.Intended) target.Projections.Enqueue(Finished("runtime_lost"));
        target.Projections.Enqueue(Finished("force_stopped"));
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), identity, new FakeTimeProvider(), default);

        await Assert.That(stop.Force).IsEqualTo(DirectTargetForce.Terminal);
        await Assert.That(stop.Result!.Failure).IsEqualTo("force_stopped");
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await Assert.That(target.Count("run/status")).IsEqualTo(identity == NativeRunIdentity.Intended ? 1 : 0);
        await Assert.That(target.Messages.Last()["params"]!.ToJsonString()).IsEqualTo($$"""{"runId":"{{RunId}}"}""");
    }

    [Test]
    public async Task NonterminalForceIsPolledToATerminalResultWithoutAnotherForce()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Stopping());
        target.Projections.Enqueue(Stopping());
        target.Projections.Enqueue(Finished("force_stopped"));
        var clock = new PollClock();
        var stop = DirectTargetRun.StopAsync(Binding(target.Origin), NativeRunIdentity.Confirmed, clock, default);
        for (var pause = 0; pause < 2; pause++)
        {
            await clock.NextPause();
            clock.Advance(DirectTargetLimits.PollDelay);
        }

        await Assert.That((await stop.WaitAsync(Patience)).Result!.Failure).IsEqualTo("force_stopped");
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await Assert.That(target.Count("run/status")).IsEqualTo(2);
    }

    [Test]
    [Arguments("precheck-deadline")]
    [Arguments("polling-deadline")]
    [Arguments("polling-cancel")]
    public async Task OneStopBudgetCoversPrecheckForceAndPolling(string end)
    {
        await using var target = new StockTarget { StallAt = end == "precheck-deadline" ? "run/status" : null };
        target.Projections.Enqueue(Stopping());
        target.Projections.Enqueue(null);
        var clock = new PollClock();
        using var caller = new CancellationTokenSource();
        var identity = end == "precheck-deadline" ? NativeRunIdentity.Intended : NativeRunIdentity.Confirmed;
        var stop = DirectTargetRun.StopAsync(Binding(target.Origin), identity, clock, caller.Token);
        var elapsed = TimeSpan.Zero;
        if (end != "precheck-deadline")
        {
            await clock.NextPause();
            clock.Advance(DirectTargetLimits.PollDelay);
            elapsed = DirectTargetLimits.PollDelay;
        }
        await target.Stalled.Task.WaitAsync(Patience);
        // A stop read has no fresh per-read budget; only the shared total remains.
        clock.Advance(DirectTargetLimits.Stop - elapsed - TimeSpan.FromMilliseconds(1));
        await Task.Delay(20);
        await Assert.That(stop.IsCompleted).IsFalse();
        if (end == "polling-cancel")
        {
            caller.Cancel();
            await Assert.That(async () => await stop).Throws<OperationCanceledException>();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Assert.That(await stop.WaitAsync(Patience)).IsEqualTo(new DirectTargetStop(
                end == "precheck-deadline" ? DirectTargetForce.NotSent : DirectTargetForce.Uncertain, null, "TimeoutError"));
        }
        await Assert.That(target.Count("run/force")).IsEqualTo(end == "precheck-deadline" ? 0 : 1);
        await Assert.That(target.Count("run/status")).IsEqualTo(1);
    }

    private static JsonObject Stopping() => Projection(new JsonObject { ["phase"] = "stopping", ["activeExecutions"] = new JsonArray() });

    private static JsonObject Finished(string reason) => Projection(new JsonObject
    {
        ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = reason }
    });

    /// <summary>A controlled clock that signals each two-second poll pause as it starts.</summary>
    private sealed class PollClock : FakeTimeProvider
    {
        private readonly SemaphoreSlim paused = new(0);

        internal async Task NextPause()
        {
            if (!await paused.WaitAsync(Patience)) throw new TimeoutException("No poll pause started.");
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == DirectTargetLimits.PollDelay) paused.Release();
            return timer;
        }
    }
}
