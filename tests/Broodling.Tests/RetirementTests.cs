using Broodling.Host;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RetirementTests
{
    [Test]
    [Arguments("absent")]
    [Arguments("prepared")]
    public async Task HttpUndispatchedStopAndRetirementNeedNoLocalResourceAndKeepCustody(string submission)
    {
        using var fixture = new HttpFixture();
        var local = fixture.Git.LocalResources();
        var attempt = fixture.Attempt;
        AttemptRetirement proof;
        if (submission == "prepared") fixture.Prepare();
        using (var store = fixture.Git.State.Open())
        {
            await Assert.That(() => store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
            store.AbandonAttempt(attempt.AttemptId, "first reason");
            proof = await store.StopAsync(attempt.AttemptId, "later reason");
            await Assert.That(proof.Basis).IsEqualTo("no_dispatch_intent");
            await Assert.That(proof.RetiredAt).IsNull();
        }
        using var reopened = fixture.Git.State.Open();
        await Assert.That(await reopened.StopAsync(attempt.AttemptId, "second reason")).IsEqualTo(proof);
        var retired = reopened.RetireAttempt(attempt.AttemptId);
        await Assert.That(retired.RetiredAt).IsNotNull();
        await Assert.That(reopened.RetireAttempt(attempt.AttemptId)).IsEqualTo(retired);
        var retained = reopened.GetAttempt(attempt.AttemptId);
        await Assert.That(retained.Retirement).IsEqualTo(retired);
        await Assert.That(retained.Abandonment!.Reason).IsEqualTo("first reason");
        await Assert.That(retained.B1).IsEqualTo(attempt.B1);
        await Assert.That(reopened.FindSubmission(attempt.AttemptId)?.State).IsEqualTo(submission == "prepared" ? "prepared" : null);
        await Assert.That(fixture.Git.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(attempt.B1.CommitOid);
        await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);
    }

    [Test]
    [Arguments("force_stopped")]
    [Arguments("succeeded")]
    [Arguments("unreachable")]
    [Arguments("cancelled")]
    public async Task AbandonmentCommitsBeforeHttpStopAndEveryOutcomeStaysQuarantined(string outcome)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        // Nothing listens at the fixture's own target.
        var submission = fixture.PrepareAt(outcome == "unreachable" ? new Uri(HttpFixture.Target) : target.Origin, "correlated");
        var attempt = fixture.Attempt;
        // A known run is stopped from retained identity alone, with the source checkout unavailable.
        Directory.Move(fixture.Git.Repository, fixture.Git.Repository + "-offline");
        var abandonedFirst = false;
        target.Reply = (request, _) =>
        {
            if ((string)request["method"]! == "run/force")
            {
                using var observer = fixture.Git.State.Open();
                abandonedFirst = observer.GetAttempt(attempt.AttemptId).Abandonment?.Reason == "operator stop"
                    && observer.CurrentAttempt(attempt.WorkUnitId) is null;
            }
            return null;
        };
        target.Projections.Enqueue(outcome switch
        {
            "succeeded" => AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt()),
            "cancelled" => null,
            _ => HttpForceStopped(submission.Run!)
        });
        using var caller = new CancellationTokenSource();
        var stop = fixture.Store.StopAsync(attempt.AttemptId, "operator stop", caller.Token);
        if (outcome == "cancelled")
        {
            await target.Stalled.Task.WaitAsync(DirectTargetRunTests.Patience);
            caller.Cancel();
            await Assert.That(async () => await stop).Throws<OperationCanceledException>();
        }
        else
        {
            var refusal = await Refusal<CessationUnconfirmed>(() => stop);
            await Assert.That(refusal.NativeStopRequested).IsEqualTo(outcome != "unreachable");
        }
        if (outcome != "unreachable")
        {
            await Assert.That(abandonedFirst).IsTrue();
            await Assert.That(target.Count("run/force")).IsEqualTo(1);
        }
        await HttpQuarantined(fixture, "correlated", "operator stop");
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)).IsNull();
        await Assert.That(fixture.Store.Status(attempt.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(attempt.AttemptId);
        await Assert.That(() => fixture.Store.AdmitRetry(attempt.AttemptId, "unsafe")).Throws<AttemptAdmissionError>();
        await Assert.That(fixture.Store.AbandonAttempt(attempt.AttemptId, "late reason").Reason).IsEqualTo("operator stop");
    }

    [Test]
    public async Task SqlRetainsSafeHttpProofAndCannotManufactureDispatchedCleanupAuthority()
    {
        using var fixture = new HttpFixture();
        var store = fixture.Store;
        await store.StopAsync(fixture.Attempt.AttemptId, "safe");
        foreach (var sql in new[] {
            "UPDATE attempt_retirements SET basis = 'stopped_target'",
            "DELETE FROM attempt_retirements",
            "INSERT OR REPLACE INTO attempt_retirements SELECT * FROM attempt_retirements" })
            await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();
        store.RetireAttempt(fixture.Attempt.AttemptId);
        await Assert.That(() => fixture.Git.State.Execute("UPDATE attempt_retirements SET retired_at = NULL")).Throws<SqliteException>();
        var successor = store.AdmitRetry(fixture.Attempt.AttemptId, "explicit");
        fixture.Prepare(attemptId: successor.AttemptId);
        fixture.Git.State.Execute($"UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = '{successor.AttemptId}'");
        store.AbandonAttempt(successor.AttemptId, "dispatched");
        await Assert.That(() => fixture.Git.State.Execute($"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at) VALUES ('{successor.AttemptId}', 'no_dispatch_intent', 'now')"))
            .Throws<SqliteException>();
        // Outside the pause, SQL refuses the stopped-target basis too.
        await Assert.That(() => fixture.Git.State.Execute(StoppedTargetInsert(successor.AttemptId))).Throws<SqliteException>();
        await Assert.That(store.FindRetirement(successor.AttemptId)).IsNull();
    }

    [Test]
    [Arguments("matching")]
    [Arguments("foreign")]
    [Arguments("unknown")]
    public async Task DispatchedHttpStopForcesTheIntendedRunOnlyAfterAMatchingStatus(string precheck)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "dispatched");
        var run = submission.Frozen.Run(submission.IntendedRunId!);
        if (precheck == "unknown") target.Reply = NotFound;
        else target.Projections.Enqueue(DirectTargetRunTests.Running(precheck == "foreign" ? run with { Title = "Another run" } : run));
        target.Projections.Enqueue(HttpForceStopped(run));
        fixture.Store.PauseInstallation(); // Stop remains available while paused.

        var refusal = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator stop"));
        await Assert.That(refusal.NativeStopRequested).IsEqualTo(precheck == "matching");
        await Assert.That(target.Count("run/force")).IsEqualTo(precheck == "matching" ? 1 : 0);
        if (precheck == "matching")
            await Assert.That((string)target.Messages.Last()["params"]!["runId"]!).IsEqualTo(submission.IntendedRunId);
        // Stop never replays the submission to discover or create its run.
        await Assert.That(target.Bodies.Count).IsEqualTo(0);
        await HttpQuarantined(fixture, "dispatched", "operator stop");
    }

    [Test]
    public async Task CorrelatedHttpStopForcesTheConfirmedRunWithoutPrecheck()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        target.Projections.Enqueue(HttpForceStopped(submission.Run!));

        var refusal = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator stop"));
        await Assert.That(refusal.NativeStopRequested).IsTrue();
        await Assert.That(target.Count("run/status")).IsEqualTo(0);
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await HttpQuarantined(fixture, "correlated", "operator stop");
    }

    [Test]
    public async Task UnansweredHttpForceIsAnUncertainTimeoutAfterCommittedAbandonment()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin, "correlated");
        target.Projections.Enqueue(null);
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;

        var stop = fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator stop");
        await target.Stalled.Task.WaitAsync(DirectTargetRunTests.Patience);
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment).IsNotNull();
        clock.Advance(DirectTargetLimits.Stop);
        await DirectTargetRunTests.Fails(() => stop, "TimeoutError");
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await HttpQuarantined(fixture, "correlated", "operator stop");
    }

    [Test]
    public async Task RepeatedHttpStopCanForceARunThatWasUnknownAtFirst()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "dispatched");
        var run = submission.Frozen.Run(submission.IntendedRunId!);
        target.Reply = NotFound;
        var first = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "first stop"));
        await Assert.That(first.NativeStopRequested).IsFalse();

        // The delayed request is accepted later; the same intended ID is now addressable.
        target.Reply = (_, _) => null;
        target.Projections.Enqueue(DirectTargetRunTests.Running(run));
        target.Projections.Enqueue(HttpForceStopped(run));
        var second = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "second stop"));
        await Assert.That(second.NativeStopRequested).IsTrue();
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await HttpQuarantined(fixture, "dispatched", "first stop");
    }

    [Test]
    public async Task IssueCancellationAbandonsTheHttpAttemptBeforeContactingTheTarget()
    {
        await using var target = new StockTarget { StallAt = "discovery" };
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin, "dispatched");
        var submission = fixture.Store.SubmitIssue("https://github.com/acme/widget/issues/12");
        fixture.Store.AssociateIssueSubmission(submission.SubmissionId, fixture.Attempt.ContractRevisionId);
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;

        var cancel = fixture.Store.CancelIssueSubmissionAsync(submission.SubmissionId, "requester withdrew");
        await target.Stalled.Task.WaitAsync(DirectTargetRunTests.Patience);
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!.Reason).IsEqualTo("requester withdrew");
        clock.Advance(DirectTargetLimits.Stop);
        var refusal = await Refusal<CessationUnconfirmed>(() => cancel);
        await Assert.That(refusal.NativeStopRequested).IsFalse();
        var cancelled = fixture.Store.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(cancelled.State).IsEqualTo("cancelled");
        await Assert.That(cancelled.Cancellation!.AttemptId).IsEqualTo(fixture.Attempt.AttemptId);
        await HttpQuarantined(fixture, "dispatched", "requester withdrew");
    }

    [Test]
    [Arguments("dispatched")]
    [Arguments("correlated")]
    public async Task VerifiedStoppedTargetRetiresAbandonedDispatchedHttpWorkWithoutResolvingOrReplacingIt(string state)
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), state);
        var id = fixture.Attempt.AttemptId;
        fixture.Store.AbandonAttempt(id, "operator stop");
        var local = fixture.Git.LocalResources();
        await Assert.That(() => fixture.Git.State.Execute(StoppedTargetInsert(id))).Throws<SqliteException>(); // SQL requires the pause.
        fixture.Store.PauseInstallation();
        var check = Check(submission.Origin);
        check = check with { VerifiedAt = check.VerifiedAt.ToOffset(TimeSpan.FromHours(-4)) };

        var retired = fixture.Store.RetireStoppedTargetAttempt(id, check);
        await Assert.That(retired.Basis).IsEqualTo("stopped_target");
        await Assert.That(retired.StoppedTarget).IsEqualTo(check);
        await Assert.That(retired.CeasedAt).IsEqualTo(check.VerifiedAt.ToUniversalTime().ToString("O")); // The same UTC form as other retained times.
        await Assert.That(retired.RetiredAt).IsNotNull();
        using var reopened = fixture.Git.State.Open();
        // The retained retirement is returned as recorded; a later check is neither needed nor recorded.
        await Assert.That(reopened.RetireStoppedTargetAttempt(id, Check(submission.Origin))).IsEqualTo(retired);
        await Assert.That(await reopened.StopAsync(id, "later stop")).IsEqualTo(retired);
        var status = reopened.Status(fixture.Attempt.ContractRevisionId);
        await Assert.That(status.Attempts.Single().Retirement).IsEqualTo(retired);
        await Assert.That(status.Attempts.Single().Abandonment!.Reason).IsEqualTo("operator stop");
        await Assert.That(status.QuarantinedAttemptIds.Count).IsEqualTo(0);
        // Retirement resolves no acceptance uncertainty and creates no successor.
        await Assert.That(reopened.FindSubmission(id)).IsEqualTo(submission);
        await Assert.That(reopened.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(state == "dispatched" ? 1 : 0);
        await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);
        await Assert.That(fixture.Git.Git("rev-parse", fixture.Attempt.B1.RetentionRef).Trim()).IsEqualTo(fixture.Attempt.B1.CommitOid);
    }

    [Test]
    public async Task VerifiedMaintenanceRetiresASuccessfulHttpAttemptWithoutAbandoningItsResult()
    {
        var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var accepted = fixture.Git.Deliver();
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
        var completion = await fixture.Store.WaitAsync(fixture.Attempt.AttemptId);
        await target.DisposeAsync();
        fixture.Store.PauseInstallation();
        // A successful result whose accepted pin is gone is an unresolved retention condition.
        fixture.Git.Git("update-ref", "-d", "refs/broodling/accepted/" + accepted);
        await Assert.That(() => fixture.Store.RetireStoppedTargetAttempt(fixture.Attempt.AttemptId, Check(submission.Origin)))
            .Throws<ResultRetentionError>();
        fixture.Git.Git("update-ref", "refs/broodling/accepted/" + accepted, accepted);

        var retired = fixture.Store.RetireStoppedTargetAttempt(fixture.Attempt.AttemptId, Check(submission.Origin));
        await Assert.That(retired.Basis).IsEqualTo("stopped_target");
        // A later operator stop hands back the retirement without abandoning the completed Attempt.
        var output = new StringWriter();
        await Assert.That(await InvocationCommands.RunAsync(["stop", fixture.Git.State.Path, fixture.Attempt.AttemptId, "later stop"],
            fixture.Git.State.Application, output, new StringWriter())).IsEqualTo(0);
        var stop = JsonNode.Parse(output.ToString())!;
        await Assert.That(stop["attempt"]!["retirement"].Deserialize<AttemptRetirement>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .IsEqualTo(retired);
        await Assert.That((bool)stop["quarantined"]!).IsFalse();
        await Assert.That((string)stop["message"]!).IsEqualTo("Attempt retired under verified stopped-target maintenance.");
        var status = fixture.Store.Status(fixture.Attempt.ContractRevisionId);
        await Assert.That(status.Attempts.Single().Retirement).IsEqualTo(retired);
        await Assert.That(status.Attempts.Single().Abandonment).IsNull();
        await Assert.That(status.Completions.Single()).IsEqualTo(completion);
        await Assert.That(await fixture.Store.WaitAsync(fixture.Attempt.AttemptId)).IsEqualTo(completion);
        await Assert.That(fixture.Git.Git("rev-parse", "refs/broodling/accepted/" + accepted).Trim()).IsEqualTo(accepted);
        // Replacement of retired dispatched work still never reopens a completed Work Unit.
        await Assert.That(() => fixture.Store.AdmitRetry(fixture.Attempt.AttemptId, "replacement")).Throws<StaleAttempt>();
    }

    [Test]
    [Arguments("unpaused")]
    [Arguments("check-before-pause")]
    [Arguments("future-check")]
    [Arguments("foreign-target")]
    [Arguments("initiating")]
    [Arguments("current")]
    [Arguments("missing-b1")]
    public async Task MaintenanceRetirementRefusesUnlessEveryCurrentConditionHolds(string condition)
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), "correlated");
        var id = fixture.Attempt.AttemptId;
        // A current Attempt stays refused whatever its native label; the others are abandoned.
        if (condition != "current") fixture.Store.AbandonAttempt(id, "operator stop");
        fixture.Store.PauseInstallation();
        var check = Check(submission.Origin);
        // A check made before a release and re-pause belongs to the earlier pause epoch.
        if (condition == "check-before-pause") { fixture.Store.ReleaseInstallation(); fixture.Store.PauseInstallation(); }
        if (condition == "unpaused") fixture.Store.ReleaseInstallation();
        // A future check would otherwise also satisfy any later pause.
        if (condition == "future-check") check = check with { VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        if (condition == "foreign-target") check = check with { DirectOrigin = "http://127.0.0.1:10" };
        if (condition == "missing-b1") fixture.Git.Git("update-ref", "-d", fixture.Attempt.B1.RetentionRef);
        using var initiating = condition == "initiating"
            ? InitiationLock.AcquireExisting(fixture.Git.State.Path, shared: true) : null;

        BroodlingException? refusal = null;
        try { fixture.Store.RetireStoppedTargetAttempt(id, check); }
        catch (BroodlingException error) { refusal = error; }
        await Assert.That(refusal?.Code).IsEqualTo(condition switch
        {
            "current" => "cessation_unconfirmed", "missing-b1" => "submission_not_ready", _ => "maintenance_unverified"
        });
        await Assert.That(fixture.Store.FindRetirement(id)).IsNull();
        await Assert.That(fixture.Store.Status(fixture.Attempt.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(id);
    }

    [Test]
    public async Task RepeatedPauseRefusesAnInterruptedInvocationsCheck()
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), "dispatched");
        var id = fixture.Attempt.AttemptId;
        fixture.Store.AbandonAttempt(id, "operator stop");
        fixture.Store.PauseInstallation();
        var interrupted = Check(submission.Origin);
        // The next invocation starts by pausing again; the installation was never released.
        fixture.Store.PauseInstallation();

        await Assert.That(() => fixture.Store.RetireStoppedTargetAttempt(id, interrupted)).Throws<MaintenanceUnverified>();
        await Assert.That(fixture.Store.FindRetirement(id)).IsNull();
        var fresh = Check(submission.Origin);
        await Assert.That(fixture.Store.RetireStoppedTargetAttempt(id, fresh).StoppedTarget).IsEqualTo(fresh);
    }

    [Test]
    public async Task RetireAttemptCommandRefusesAnIncompleteCheckThenPrintsTheRecordedRetirement()
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), "dispatched");
        var id = fixture.Attempt.AttemptId;
        fixture.Store.AbandonAttempt(id, "operator stop");
        fixture.Store.PauseInstallation();
        var check = Check(submission.Origin);
        var json = JsonSerializer.SerializeToNode(check, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        var incomplete = json.DeepClone().AsObject();
        incomplete["homeMount"] = " ";
        var (path, application) = (fixture.Git.State.Path, fixture.Git.State.Application);

        var output = new StringWriter();
        var error = new StringWriter();
        // A blank member reaches the operation's own completeness guard.
        await Assert.That(StoreCommands.Run(["retire-attempt", path, id, incomplete.ToJsonString()], application, output, error)).IsEqualTo(1);
        await Assert.That((string)JsonNode.Parse(error.ToString())!["error"]!).IsEqualTo("maintenance_unverified");
        await Assert.That(fixture.Store.FindRetirement(id)).IsNull();
        await Assert.That(StoreCommands.Run(["retire-attempt", path, id, json.ToJsonString()], application, output, error)).IsEqualTo(0);
        await Assert.That((string)JsonNode.Parse(output.ToString())!["basis"]!).IsEqualTo("stopped_target");
        await Assert.That(fixture.Store.FindRetirement(id)!.StoppedTarget).IsEqualTo(check);
    }

    private static StoppedTargetCheck Check(string origin) =>
        new(origin, "broodling-target", "/srv/broodling/target-state", "/srv/broodling/target-home", DateTimeOffset.UtcNow);

    private static string StoppedTargetInsert(string attemptId) =>
        $"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at, stopped_target_json) VALUES ('{attemptId}', 'stopped_target', 'now', '{{}}')";

    private static string? NotFound(JsonObject request, string id) => (string)request["method"]! != "run/status" ? null
        : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject
            { ["code"] = -32000, ["message"] = "run was not found", ["data"] = new JsonObject { ["code"] = "NOT_FOUND" } } }.ToJsonString();

    private static JsonObject HttpForceStopped(NativeRunBinding run) => DirectTargetRunTests.Projection(new JsonObject
    {
        ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
    }, run);

    private static async Task<T> Refusal<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    /// <summary>Abandonment stays committed, stop output never correlates, and dispatch intent keeps the Attempt quarantined.</summary>
    private static async Task HttpQuarantined(HttpFixture fixture, string state, string reason)
    {
        var attempt = fixture.Store.GetAttempt(fixture.Attempt.AttemptId);
        await Assert.That(attempt.Abandonment!.Reason).IsEqualTo(reason);
        await Assert.That(fixture.Store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo(state);
        await Assert.That(fixture.Store.FindRetirement(attempt.AttemptId)).IsNull();
        await Assert.That(() => fixture.Store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
    }
}
