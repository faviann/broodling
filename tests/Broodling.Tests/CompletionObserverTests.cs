using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// Automatic completion observation with no caller waiting, over real SQLite/Git and the loopback
/// stock-target stand-in. Receipt, pin and finalization semantics belong to <see cref="AttemptCompletionTests"/>;
/// these witnesses cover discovery, retry cadence, refusal retention and restart.
/// </summary>
public sealed class CompletionObserverTests
{
    [Test]
    public async Task CorrelationIsObservedWhileRunningWithoutDispatchingUnacknowledgedWork()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "dispatched");
        var accepted = fixture.Git.Deliver();
        target.Reply = Status(() => Finished(submission, accepted));

        await using var observer = new Observer(fixture);
        for (var scan = 0; scan < 3; scan++) await observer.Scan();
        await Assert.That(target.Connections).IsEqualTo(0);
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!.State).IsEqualTo("dispatched");

        // Only the exact acknowledgement correlates; the observer then retains the result unasked.
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'correlated', run_id = intended_run_id");
        await observer.ScanUntil(() => fixture.Store.FindCompletion(fixture.Attempt.AttemptId) is not null);
        var completion = fixture.Store.FindCompletion(fixture.Attempt.AttemptId)!;
        await Assert.That(completion.RunId).IsEqualTo(submission.IntendedRunId);
        await Assert.That(completion.AcceptedRevision).IsEqualTo(accepted);
        await Assert.That(Reached(target, "run")).IsEqualTo(0);
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    [Test]
    public async Task ShutdownDetachesAndARestartedObserverRetainsTheSameRun()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var accepted = fixture.Git.Deliver();
        var finished = false;
        target.Reply = Status(() => finished ? Finished(submission, accepted)
            : DirectTargetSessionTests.Running(submission.Frozen.Run(submission.IntendedRunId!)));

        var first = new Observer(fixture);
        await Until(() => target.Count("run/status") > 0);
        await first.DisposeAsync();
        var attempt = fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        await Assert.That(attempt.CompletionRefusal).IsNull();
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)).IsNull();
        await Assert.That(target.Count("run/force")).IsEqualTo(0);

        finished = true;
        await using var restarted = new Observer(fixture);
        await Until(() => fixture.Store.FindCompletion(attempt.AttemptId) is not null);
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)!.RunId).IsEqualTo(submission.IntendedRunId);
    }

    [Test]
    public async Task TemporaryFailuresAreRetriedOncePerScanUntilTheResultIsRetained()
    {
        await using var target = new StockTarget { Session = (503, """{"code":"target.unavailable","message":"busy"}""") };
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var unpublished = fixture.Git.Deliver(push: false);
        await using var observer = new Observer(fixture);
        await Until(() => Reached(target, "session") == 1);
        await Task.Delay(200);
        await Assert.That(Reached(target, "session")).IsEqualTo(1);

        // Target configuration is retried rather than refused, so an operator can repair it in place.
        target.Session = null;
        target.Reply = (request, id) => (string)request["method"]! == "initialize"
            ? """{"jsonrpc":"2.0","id":"ID","error":{"code":-32000,"message":"no","data":{"code":"UNSUPPORTED_PROTOCOL_VERSION","details":null}}}""".Replace("ID", id)
            : null;
        await observer.ScanUntil(() => target.Count("initialize") > 0);
        // The accepted commit is not yet fetchable from the origin, so the pin fails.
        target.Reply = Status(() => Finished(submission, unpublished));
        await observer.ScanUntil(() => target.Count("run/status") > 0);
        await Task.Delay(200);
        var attempt = fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        await Assert.That(attempt.CompletionRefusal).IsNull();
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)).IsNull();

        fixture.Git.Push();
        await observer.ScanUntil(() => fixture.Store.FindCompletion(attempt.AttemptId) is not null);
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)!.AcceptedRevision).IsEqualTo(unpublished);
        await Assert.That(Reached(target, "session")).IsLessThanOrEqualTo(observer.Scans + 1);
    }

    [Test]
    public async Task NativeFailureIsRetainedAsAbandonmentAndObservationEnds()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        target.Reply = Status(() => AttemptCompletionTests.HttpFinished(submission, "failed", "runtime_failed"));

        await using var observer = new Observer(fixture);
        await Until(() => fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment is not null);
        for (var scan = 0; scan < 3; scan++) await observer.Scan();
        var attempt = fixture.Store.GetAttempt(fixture.Attempt.AttemptId);
        await Assert.That(attempt.Abandonment!.Reason).IsEqualTo("Zeroshot run failed: runtime_failed");
        await Assert.That(attempt.CompletionRefusal).IsNull();
        await Assert.That(target.Count("run/status")).IsEqualTo(1);
    }

    [Test]
    public async Task RefusedReceiptIsRetainedWithoutChangingAuthorityAndNeverRetried()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        target.Reply = Status(() => AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(id: "")));
        var id = fixture.Attempt.AttemptId;
        CompletionRefusal? Refusal() => fixture.Store.Status(fixture.Attempt.ContractRevisionId)
            .Attempts.Single(attempt => attempt.AttemptId == id).CompletionRefusal;

        await using (var observer = new Observer(fixture))
        {
            await Until(() => Refusal() is not null);
            for (var scan = 0; scan < 3; scan++) await observer.Scan();
        }
        await using (var restarted = new Observer(fixture))
            for (var scan = 0; scan < 3; scan++) await restarted.Scan();

        await Assert.That(Refusal()!.Reason).IsEqualTo("The delivery receipt does not match frozen PR authority.");
        var attempt = fixture.Store.RequireCurrentAttempt(id);
        await Assert.That(attempt.Abandonment).IsNull();
        await Assert.That(fixture.Store.FindCompletion(id)).IsNull();
        await Assert.That(target.Count("run/status")).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentObserversRetainOneResultAndLeaveRetainedWorkAlone()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var accepted = fixture.Git.Deliver();
        var answer = Status(() => Finished(submission, accepted));
        // Hold each read until both observers have asked, so both finalize the same result.
        target.Reply = (request, id) =>
        {
            SpinWait.SpinUntil(() => target.Count("run/status") >= 2, TimeSpan.FromSeconds(10));
            return answer(request, id);
        };

        await using (var one = new Observer(fixture))
        await using (var two = new Observer(fixture))
            await Until(() => fixture.Store.FindCompletion(fixture.Attempt.AttemptId) is not null);
        await Assert.That(target.Count("run/status")).IsEqualTo(2);
        await Assert.That(fixture.Scalar("SELECT count(*) FROM attempt_completions")).IsEqualTo("1");
        var attempt = fixture.Store.GetAttempt(fixture.Attempt.AttemptId);
        await Assert.That(attempt.CompletionRefusal).IsNull();
        await Assert.That(attempt.Abandonment).IsNull();

        await using (var later = new Observer(fixture))
            for (var scan = 0; scan < 3; scan++) await later.Scan();
        await Assert.That(target.Count("run/status")).IsEqualTo(2);
    }

    private static JsonObject Finished(NativeSubmission submission, string accepted) =>
        AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted));

    private static Func<JsonObject, string, string?> Status(Func<JsonObject> projection) => (request, id) =>
        (string)request["method"]! == "run/status"
            ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = projection() }.ToJsonString()
            : null;

    private static int Reached(StockTarget target, string stage)
    {
        lock (target.Stages) return target.Stages.Count(reached => reached == stage);
    }

    private static Task Until(Func<bool> condition) => ProvisioningProcessTests.WaitUntil(condition);

    /// <summary>An observer over the fixture's store whose scan interval advances only when a test says so.</summary>
    private sealed class Observer : IAsyncDisposable
    {
        private readonly FakeTimeProvider clock = new();
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task running;
        internal int Scans { get; private set; }

        internal Observer(HttpFixture fixture) =>
            running = new CompletionObserver(new BroodlingApplication(), fixture.Git.State.Path) { Clock = clock }
                .RunAsync(cancellation.Token);

        internal async Task Scan()
        {
            clock.Advance(CompletionObserver.Cadence);
            Scans++;
            await Task.Delay(50);
        }

        internal async Task ScanUntil(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("The observer did not settle.");
                await Scan();
            }
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            await running;
            cancellation.Dispose();
        }
    }
}
