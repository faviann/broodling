using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// Broodling's run operations over the SDK against the loopback stock target: reconnection by the
/// retained binding, identity and failure classification, wait through the SDK's watch, and stop
/// sequencing and budgets. The SDK's own wire validation is not retested here.
/// </summary>
public sealed class DirectTargetRunTests
{
    /// <summary>Real-time guard for loopback progress; generous because the whole suite shares the thread pool.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    internal const string RunId = "01996f2e-7a4b-7c3d-8e5f-0123456789ab";
    private static readonly NativeSource Source = new("owner/widget", "broodling/fix-widget", new string('b', 40));

    [Test]
    public async Task ProgressIsOneStatusReadOfTheRetainedRun()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Running());
        var progress = await Progress(target);

        await Assert.That(progress.Phase).IsEqualTo("running");
        await Assert.That(progress.ActiveNodes).IsEquivalentTo(["worker", "verifier"]);
        await Assert.That(target.Count("run/status")).IsEqualTo(1);
        await Assert.That((string)target.Messages.Last()["params"]!["runId"]!).IsEqualTo(RunId);
        await Assert.That(target.Heads.All(head => !head.Contains("Authorization", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    [Arguments("title")]
    [Arguments("size")]
    [Arguments("revision")]
    public async Task AStatusOfAnotherRunUnderTheSameIdIsForeign(string change)
    {
        var projection = Running();
        switch (change)
        {
            case "title": projection["title"] = "Another run"; break;
            case "size": projection["size"] = "large"; break;
            case "revision": projection["source"]!["revision"] = new string('c', 40); break;
        }
        await using var target = new StockTarget();
        target.Projections.Enqueue(projection);
        await Fails(() => Progress(target), "foreign_run");
    }

    [Test]
    [Arguments("""{"code":-32000,"message":"canary secret","data":{"code":"NOT_FOUND"}}""", "RunNotFoundError")]
    [Arguments("""{"code":-32603,"message":"canary secret","data":{"code":"INTERNAL_ERROR","details":null}}""", "TargetError")]
    [Arguments("""{"code":-32602,"message":"canary secret"}""", "TargetError")]
    public async Task OecpErrorsBecomeFixedClassificationsWithoutRemoteText(string error, string kind)
    {
        await using var target = new StockTarget();
        target.Reply = (request, id) => (string)request["method"]! == "run/status"
            ? $$"""{"jsonrpc":"2.0","id":"{{id}}","error":{{error}}}""" : null;
        var failure = await Fails(() => Progress(target), kind);
        await Assert.That(failure.ToString().Contains("canary", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    [Arguments("local", "http://127.0.0.1:{port}")]
    [Arguments("direct", "http://localhost:{port}")]
    [Arguments("direct", "no-source")]
    public async Task UnsupportedBindingsAreRefusedBeforeContact(string kind, string address)
    {
        await using var target = new StockTarget();
        var binding = address == "no-source"
            ? Binding(target.Origin) with { Source = null }
            : new NativeRunBinding(new NativeLocator(kind, address.Replace("{port}", target.Origin.Port.ToString())), RunId,
                "Fix the widget", "small", Source);
        await Assert.That(async () => await DirectTargetRun.ProgressAsync(binding, null, DirectTargetLimits.Progress, new FakeTimeProvider(), default))
            .Throws<UnsupportedRuntime>();
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    [Test]
    [MatrixDataSource]
    public async Task AStalledReadEndsByItsDeadlineOrTheCallerWithoutLaterRequests(
        [Matrix("discovery", "session", "run/status")] string stage, [Matrix(false, true)] bool callerCancels)
    {
        await using var target = new StockTarget { StallAt = stage };
        target.Projections.Enqueue(Running());
        using var caller = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var read = DirectTargetRun.ProgressAsync(Binding(target.Origin), null, DirectTargetLimits.Progress, clock, caller.Token);
        await target.Stalled.Task.WaitAsync(Patience);
        clock.Advance(DirectTargetLimits.Progress - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        await Assert.That(read.IsCompleted).IsFalse();
        if (callerCancels)
        {
            caller.Cancel();
            await Assert.That(async () => await read).Throws<OperationCanceledException>();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Fails(() => read, "TimeoutError");
        }
        await Assert.That(target.Stages.Last()).IsEqualTo(stage);
    }

    [Test]
    [Arguments("""{"status":"succeeded","output":{"receipt":{"pr":7}}}""", true, null)]
    [Arguments("""{"status":"failed","reason":"runtime_failed"}""", false, "runtime_failed")]
    [Arguments("""{"status":"failed","reason":"canary_secret_label"}""", false, "native_failed")]
    public async Task AFinishedFirstStatusIsTheResultWithoutWatching(string terminal, bool succeeded, string? failure)
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Projection(new JsonObject { ["phase"] = "finished", ["terminalResult"] = JsonNode.Parse(terminal) }));
        var result = await DirectTargetRun.WaitAsync(Binding(target.Origin), null, new FakeTimeProvider(), default);

        await Assert.That((result.RunId, result.Succeeded, result.Failure)).IsEqualTo((RunId, succeeded, failure));
        if (succeeded) await Assert.That(result.Output.GetRawText()).IsEqualTo(JsonNode.Parse(terminal)!["output"]!.ToJsonString());
        await Assert.That(target.Count("run/watch")).IsEqualTo(0);
    }

    [Test]
    public async Task WaitWatchesANonterminalRunToItsTerminalEventWithoutPolling()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Running()); // Broodling's identity check.
        target.Projections.Enqueue(Running()); // The SDK wait's first status.
        target.Projections.Enqueue(Running());
        target.Projections.Enqueue(Finished("runtime_lost"));
        var result = await DirectTargetRun.WaitAsync(Binding(target.Origin), null, new FakeTimeProvider(), default).WaitAsync(Patience);

        await Assert.That((result.Succeeded, result.Failure)).IsEqualTo((false, "runtime_lost"));
        await Assert.That(target.Count("run/status")).IsEqualTo(2);
        await Assert.That(target.Count("run/watch")).IsEqualTo(1);
    }

    [Test]
    public async Task AFailedWatchDetachesTheWaitWithAFixedKind()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Running());
        target.Projections.Enqueue(Running());
        target.Reply = (request, id) => (string)request["method"]! == "run/watch"
            ? $$$"""{"jsonrpc":"2.0","id":"{{{id}}}","error":{"code":-32603,"message":"canary secret"}}""" : null;
        var failure = await Fails(() => DirectTargetRun.WaitAsync(Binding(target.Origin), null, new FakeTimeProvider(), default), "TargetError");

        await Assert.That(failure.ToString().Contains("canary", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    [Test]
    public async Task CancellingAWaitDetachesWithoutStopping()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Running());
        target.Projections.Enqueue(Running());
        using var caller = new CancellationTokenSource();
        var wait = DirectTargetRun.WaitAsync(Binding(target.Origin), null, new FakeTimeProvider(), caller.Token);
        while (target.Count("run/watch") == 0) await Task.Delay(10).WaitAsync(Patience);
        await Task.Delay(50);
        await Assert.That(wait.IsCompleted).IsFalse();
        caller.Cancel();

        await Assert.That(async () => await wait).Throws<OperationCanceledException>();
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    [Test]
    [Arguments("unknown", "RunNotFoundError")]
    [Arguments("foreign", "foreign_run")]
    [Arguments("unavailable", "TargetError")]
    public async Task IntendedStopSendsNoForceWithoutAMatchingStatus(string precheck, string reason)
    {
        await using var target = new StockTarget();
        switch (precheck)
        {
            case "unknown":
                target.Reply = (request, id) => (string)request["method"]! == "run/status"
                    ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id,
                        ["error"] = new JsonObject { ["code"] = -32000, ["message"] = "run was not found", ["data"] = new JsonObject { ["code"] = "NOT_FOUND" } } }.ToJsonString()
                    : null;
                break;
            case "foreign": target.Projections.Enqueue(Running(Binding(target.Origin) with { Title = "Another run" })); break;
            case "unavailable": target.Session = (503, """{"code":"target.unavailable","message":"busy"}"""); break;
        }
        target.Projections.Enqueue(Finished("force_stopped")); // What a force would get.
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), NativeRunIdentity.Intended, null, new FakeTimeProvider(), default);

        await Assert.That(stop).IsEqualTo(new DirectTargetStop(DirectTargetForce.NotSent, reason));
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
    }

    [Test]
    public async Task AForceThatCannotReachTheTargetIsNotSent()
    {
        await using var target = new StockTarget { Session = (503, """{"code":"target.unavailable","message":"busy"}""") };
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), NativeRunIdentity.Confirmed, null, new FakeTimeProvider(), default);

        await Assert.That(stop).IsEqualTo(new DirectTargetStop(DirectTargetForce.NotSent, "TargetError"));
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
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), identity, null, new FakeTimeProvider(), default);

        await Assert.That(stop).IsEqualTo(new DirectTargetStop(DirectTargetForce.Terminal, null));
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await Assert.That(target.Count("run/status")).IsEqualTo(identity == NativeRunIdentity.Intended ? 1 : 0);
        await Assert.That((string)target.Messages.Last(message => (string)message["method"]! == "run/force")["params"]!["runId"]!).IsEqualTo(RunId);
    }

    [Test]
    public async Task AForeignForceReplyIsUncertain()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
        }, Binding(target.Origin) with { Title = "Another run" }));
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), NativeRunIdentity.Confirmed, null, new FakeTimeProvider(), default);

        await Assert.That(stop).IsEqualTo(new DirectTargetStop(DirectTargetForce.Uncertain, "foreign_run"));
    }

    [Test]
    public async Task ANonterminalForceIsWaitedToATerminalResultWithoutAnotherForce()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Stopping());
        target.Projections.Enqueue(Stopping()); // The SDK wait's first status.
        target.Projections.Enqueue(Finished("force_stopped"));
        var stop = await DirectTargetRun.StopAsync(Binding(target.Origin), NativeRunIdentity.Confirmed, null, new FakeTimeProvider(), default);

        await Assert.That(stop).IsEqualTo(new DirectTargetStop(DirectTargetForce.Terminal, null));
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await Assert.That(target.Count("run/watch")).IsEqualTo(1);
    }

    [Test]
    [Arguments("precheck-deadline")]
    [Arguments("waiting-deadline")]
    [Arguments("waiting-cancel")]
    public async Task OneStopBudgetCoversPrecheckForceAndWaiting(string end)
    {
        await using var target = new StockTarget { StallAt = end == "precheck-deadline" ? "run/status" : null };
        target.Projections.Enqueue(Stopping());
        target.Projections.Enqueue(null); // The wait after force never answers.
        var clock = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();
        var identity = end == "precheck-deadline" ? NativeRunIdentity.Intended : NativeRunIdentity.Confirmed;
        var stop = DirectTargetRun.StopAsync(Binding(target.Origin), identity, null, clock, caller.Token);
        await target.Stalled.Task.WaitAsync(Patience);
        clock.Advance(DirectTargetLimits.Stop - TimeSpan.FromMilliseconds(1));
        await Task.Delay(20);
        await Assert.That(stop.IsCompleted).IsFalse();
        if (end == "waiting-cancel")
        {
            caller.Cancel();
            await Assert.That(async () => await stop).Throws<OperationCanceledException>();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Assert.That(await stop.WaitAsync(Patience)).IsEqualTo(new DirectTargetStop(
                end == "precheck-deadline" ? DirectTargetForce.NotSent : DirectTargetForce.Uncertain, "TimeoutError"));
        }
        await Assert.That(target.Count("run/force")).IsEqualTo(end == "precheck-deadline" ? 0 : 1);
    }

    internal static NativeRunBinding Binding(Uri origin) =>
        new(new NativeLocator("direct", origin.GetLeftPart(UriPartial.Authority)), RunId, "Fix the widget", "small", Source);

    private static Task<NativeProgress> Progress(StockTarget target) =>
        DirectTargetRun.ProgressAsync(Binding(target.Origin), null, DirectTargetLimits.Progress, new FakeTimeProvider(), default);

    /// <summary>A projection of <paramref name="run"/>, by default this class's fixed binding.</summary>
    internal static JsonObject Projection(JsonObject status, NativeRunBinding? run = null)
    {
        run ??= Binding(new Uri("http://127.0.0.1:1"));
        return new()
        {
            ["runId"] = run.RunId, ["title"] = run.Title, ["size"] = run.Size, ["atCursor"] = "opaque-cursor",
            ["source"] = new JsonObject { ["repository"] = run.Source!.Repository, ["branch"] = run.Source.Branch, ["revision"] = run.Source.Revision },
            ["status"] = status
        };
    }

    internal static JsonObject Running(NativeRunBinding? run = null) => Projection(new JsonObject
    {
        ["phase"] = "running",
        ["activeExecutions"] = new JsonArray(
            new JsonObject { ["execution"] = "execution-1", ["node"] = "worker" },
            new JsonObject { ["execution"] = "execution-2", ["node"] = "verifier" })
    }, run);

    private static JsonObject Stopping() => Projection(new JsonObject { ["phase"] = "stopping", ["activeExecutions"] = new JsonArray() });

    private static JsonObject Finished(string reason) => Projection(new JsonObject
    {
        ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = reason }
    });

    internal static async Task<Exception> Fails<T>(Func<Task<T>> action, string kind)
    {
        try { await action(); }
        catch (NativeTransportError error)
        {
            await Assert.That(error.Kind).IsEqualTo(kind);
            return error;
        }
        throw new InvalidOperationException("Expected native transport error " + kind);
    }
}
