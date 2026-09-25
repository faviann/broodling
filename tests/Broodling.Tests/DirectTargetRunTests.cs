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
            await clock.Paused.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(target.Count("run/status")).IsEqualTo(pause);
            clock.Advance(DirectTargetLimits.PollDelay - TimeSpan.FromMilliseconds(1));
            await Task.Delay(20);
            await Assert.That(target.Count("run/status")).IsEqualTo(pause);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        var result = await wait.WaitAsync(TimeSpan.FromSeconds(10));

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
        await clock.Paused.WaitAsync(TimeSpan.FromSeconds(10));
        if (end == "cancel-pause")
        {
            caller.Cancel();
            await Assert.That(async () => await wait).Throws<OperationCanceledException>();
            await Assert.That(target.Count("run/status")).IsEqualTo(1);
            return;
        }
        clock.Advance(DirectTargetLimits.PollDelay);
        await target.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
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

    /// <summary>A controlled clock that signals each two-second poll pause as it starts.</summary>
    private sealed class PollClock : FakeTimeProvider
    {
        internal SemaphoreSlim Paused { get; } = new(0);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == DirectTargetLimits.PollDelay) Paused.Release();
            return timer;
        }
    }
}
