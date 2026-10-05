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
    public async Task PreparedHttpAttemptHasNoProgressAndMakesNoContact()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin);
        await Assert.That(await fixture.Store.ObserveAsync(fixture.Attempt.AttemptId)).IsNull();
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
        target.Projections.Enqueue(DirectTargetRunTests.Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "succeeded", ["output"] = null }
        }, run));
        var retained = RetainedJson(fixture.Store, fixture.Attempt);

        var observation = await fixture.Store.ObserveAsync(fixture.Attempt.AttemptId) as NativeObservation.Available;
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
        // The exact run, but in a phase outside the status union.
        if (answer == "malformed")
            target.Projections.Enqueue(DirectTargetRunTests.Projection(new JsonObject { ["phase"] = "paused" }, run));
        target.Projections.Enqueue(DirectTargetRunTests.Running(run with { Title = "Another run" }));
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;

        var read = fixture.Store.ObserveAsync(fixture.Attempt.AttemptId);
        if (answer == "stalled")
        {
            await target.Stalled.Task.WaitAsync(DirectTargetRunTests.Patience);
            clock.Advance(DirectTargetLimits.Progress);
        }
        var observation = await read.WaitAsync(DirectTargetRunTests.Patience) as NativeObservation.Unavailable;
        await Assert.That(observation!.Identity).IsEqualTo(NativeRunIdentity.Intended);
        await Assert.That(observation.Reason).IsEqualTo(reason);
        await Assert.That(fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!.State).IsEqualTo("dispatched");
    }

    private static string RetainedJson(BroodlingStore store, AttemptRecord attempt) =>
        JsonSerializer.Serialize(store.Status(attempt.ContractRevisionId));
}
