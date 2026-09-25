using System.Text.Json;
using System.Text.Json.Nodes;
using Broodling.Host;
using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// A correlated HTTP Attempt whose retained binding reads a loopback stock-target stand-in. Each
/// status read reports <see cref="Transport"/>'s native result as a finished projection, and a force
/// reports <c>force_stopped</c>, so completion witnesses choose the result without re-testing the wire.
/// </summary>
internal sealed class CompletionFixture : IDisposable
{
    private readonly HttpFixture http = new();
    private NativeRunBinding? run;
    internal AttemptFixture Git => http.Git;
    internal BroodlingStore Store => http.Store;
    internal AttemptRecord Attempt => http.Attempt;
    internal StockTarget Target { get; } = new();
    internal ControlledTransport Transport { get; } = new();
    internal string Accepted { get; }
    internal CompletionFixture()
    {
        Accepted = Git.Deliver();
        Transport.Wait = (_, id, _) => Task.FromResult(new NativeResult(id, true, Receipt(head: Accepted), null));
        Target.Reply = (request, id) => (string)request["method"]! switch
        {
            "run/status" => Reply(id, Transport.WaitAsync(run!).GetAwaiter().GetResult()),
            "run/force" => Reply(id, new(run!.RunId, false, default, "force_stopped")),
            _ => null
        };
    }
    internal string Origin => Target.Origin.GetLeftPart(UriPartial.Authority);
    /// <summary>Prepare at the stand-in without sending anything.</summary>
    internal NativeSubmission Prepare(string? attemptId = null) => http.Prepare(attemptId: attemptId, target: Origin);
    /// <summary>Prepare and correlate through the guarded phase steps; nothing is sent.</summary>
    internal Task<NativeSubmission> Dispatch(string? attemptId = null)
    {
        var id = Prepare(attemptId).AttemptId;
        Git.State.Execute($"UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = '{id}'");
        Git.State.Execute($"UPDATE native_submissions SET state = 'correlated', run_id = intended_run_id WHERE attempt_id = '{id}'");
        var submission = Store.FindSubmission(id)!;
        run = submission.Run;
        return Task.FromResult(submission);
    }
    private string Reply(string id, NativeResult result)
    {
        var terminal = result.Succeeded
            ? new JsonObject { ["status"] = "succeeded", ["output"] = JsonNode.Parse(result.Output.GetRawText()) }
            : new JsonObject { ["status"] = "failed", ["reason"] = result.Failure };
        var projection = DirectTargetSessionTests.Projection(new JsonObject { ["phase"] = "finished", ["terminalResult"] = terminal },
            run! with { RunId = result.RunId });
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = projection }.ToJsonString();
    }
    internal static JsonElement Receipt(string id = "50", string? head = null) => JsonSerializer.SerializeToElement(new
    {
        version = "v1", mode = "pr", outcome = "opened", repository = "acme/widget", targetBranch = "main",
        headRevision = head ?? new string('b', 40), pullRequestId = id
    });
    internal Task<AttemptCompletion> Wait() => Store.WaitAsync(Attempt.AttemptId, null);
    public void Dispose() { Target.DisposeAsync().AsTask().GetAwaiter().GetResult(); http.Dispose(); }
}

public sealed class AttemptCompletionTests
{
    [Test]
    [Arguments("0")]
    [Arguments("000050")]
    [Arguments("9999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999")]
    public async Task DigitStringsAreNotNumbersAndRetainedCompletionNeedsNoTarget(string pr)
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        fixture.Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, CompletionFixture.Receipt(pr, fixture.Accepted), null));
        var completed = await fixture.Wait();
        await Assert.That(completed.Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(completed.AcceptedRevision).IsEqualTo(fixture.Accepted);
        await Assert.That(completed.DeliveryReceipt.GetProperty("pullRequestId").GetString()).IsEqualTo(pr);
        await Assert.That(fixture.Store.CurrentAttempt(completed.WorkUnitId)).IsNull();
        fixture.Transport.Wait = (_, _, _) => throw new Exception("Native unavailable");
        using var reopened = fixture.Git.State.Open();
        // Terminal replay is observation: an unrelated SQLite writer must not block it.
        using var writer = fixture.Git.State.Connect();
        using var held = writer.BeginTransaction(deferred: false);
        await Assert.That(await reopened.WaitAsync(completed.AttemptId, null)).IsEqualTo(completed);
        await Assert.That(reopened.FindCompletion(completed.AttemptId)).IsEqualTo(completed);
        await Assert.That(reopened.FindCompletion("unknown-attempt")).IsNull();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(1);
    }

    [Test]
    public async Task IncompleteForeignAndWrongTypeReceiptsRefuseWithoutLosingAuthority()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var invalid = new List<string> { "null", "[]", "true", "42", "\"receipt\"", "{}" };
        var valid = JsonNode.Parse(CompletionFixture.Receipt().GetRawText())!.AsObject();
        foreach (var field in valid.Select(pair => pair.Key).ToArray())
        {
            var missing = valid.DeepClone().AsObject(); missing.Remove(field); invalid.Add(missing.ToJsonString());
            foreach (var value in new[] { "null", "1", "false", "[]", "{}" })
            {
                var changed = valid.DeepClone().AsObject(); changed[field] = JsonNode.Parse(value); invalid.Add(changed.ToJsonString());
            }
        }
        foreach (var (field, value) in new[] {
            ("version", "v2"), ("mode", "merge"), ("outcome", "closed"), ("repository", "other/project"),
            ("targetBranch", "release"), ("headRevision", fixture.Attempt.B1.CommitOid),
            ("headRevision", new string('A', 40)), ("headRevision", new string('g', 40)), ("headRevision", new string('b', 39)),
            ("pullRequestId", ""), ("pullRequestId", "-1"), ("pullRequestId", "+1"), ("pullRequestId", " 1"),
            ("pullRequestId", "1 "), ("pullRequestId", "١"), ("pullRequestId", "1.0"), ("pullRequestId", "1\0") })
        {
            var changed = valid.DeepClone().AsObject(); changed[field] = value; invalid.Add(changed.ToJsonString());
        }
        var extra = valid.DeepClone().AsObject(); extra["extra"] = "value"; invalid.Add(extra.ToJsonString());
        // Duplicate properties never reach the receipt: the status reader refuses them (DirectTargetSessionTests).
        foreach (var json in invalid)
        {
            fixture.Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, JsonSerializer.Deserialize<JsonElement>(json), null));
            await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
            await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
            fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        }
    }

    [Test]
    public async Task UncorrelatedOrAbandonedAttemptsRefuseBeforeWaitAndForeignRunCannotAbandonOrComplete()
    {
        using var fixture = new CompletionFixture();
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionNotReady>();
        fixture.Prepare();
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionNotReady>();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(0);
        await fixture.Dispatch();
        foreach (var succeeded in new[] { true, false })
        {
            fixture.Transport.Wait = (_, _, _) => Task.FromResult(new NativeResult("01a00000-0000-7000-8000-000000000000", succeeded,
                CompletionFixture.Receipt(), "runtime_lost"));
            await DirectTargetSessionTests.Fails(() => fixture.Wait(), "foreign_run");
            fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        }
        fixture.Store.AbandonAttempt(fixture.Attempt.AttemptId, "operator decision");
        var calls = fixture.Transport.WaitCalls;
        await Assert.That(async () => await fixture.Wait()).Throws<StaleAttempt>();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(calls);
    }

    [Test]
    public async Task DetachingWaitIsRetryableButNativeFailureAbandonsWithoutCleanup()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.That(async () => await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null, cancellation.Token)).Throws<OperationCanceledException>();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        fixture.Transport.Wait = (_, _, _) => throw new NativeTransportError();
        await Assert.That(async () => await fixture.Wait()).Throws<NativeTransportError>();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        fixture.Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, false, default, "runtime_lost"));
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionNotReady>();
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!.Reason).Contains("runtime_lost");
        await Assert.That(fixture.Target.Count("run/force")).IsEqualTo(0);
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    public async Task AbandonmentWhileWaitingRejectsLateSuccessAndCompletionPreventsAbandonment()
    {
        using var fixture = new CompletionFixture();
        var submission = fixture.Store.SubmitIssue("https://github.com/acme/widget/issues/12");
        fixture.Store.AssociateIssueSubmission(submission.SubmissionId, fixture.Attempt.ContractRevisionId);
        await fixture.Dispatch();
        fixture.Transport.Wait = (_, run, _) =>
        {
            using var other = fixture.Git.State.Open();
            CessationUnconfirmed? cancellationStop = null;
            try
            {
                // The HTTP record forces exactly its retained run; no bridge transport is involved.
                other.CancelIssueSubmissionAsync(submission.SubmissionId, "stop won").GetAwaiter().GetResult();
            }
            catch (CessationUnconfirmed error)
            {
                cancellationStop = error;
            }
            if (cancellationStop is null || !cancellationStop.NativeStopRequested)
                throw new Exception("Cancellation did not commit exact abandonment before native stop");
            if (other.GetIssueSubmission(submission.SubmissionId).Cancellation!.AttemptId != fixture.Attempt.AttemptId)
                throw new Exception("Cancellation did not retain the exact Attempt");
            return Task.FromResult(new NativeResult(run, true, CompletionFixture.Receipt(head: fixture.Accepted), null));
        };
        await Assert.That(async () => await fixture.Wait()).Throws<StaleAttempt>();
        await Assert.That(fixture.Target.Count("run/force")).IsEqualTo(1);
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        await Assert.That(fixture.Store.GetIssueSubmission(submission.SubmissionId).State).IsEqualTo("cancelled");
        using var winner = new CompletionFixture();
        var winnerSubmission = winner.Store.SubmitIssue("https://github.com/acme/widget/issues/12");
        winner.Store.AssociateIssueSubmission(winnerSubmission.SubmissionId, winner.Attempt.ContractRevisionId);
        await winner.Dispatch();
        await winner.Wait();
        await Assert.That(async () => await winner.Store.CancelIssueSubmissionAsync(winnerSubmission.SubmissionId, "too late"))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(() => winner.Store.AbandonAttempt(winner.Attempt.AttemptId, "too late")).Throws<StaleAttempt>();
        await Assert.That(() => winner.Git.State.Execute($"INSERT INTO attempt_abandonments VALUES ('{winner.Attempt.AttemptId}', 'too late', 'now')")).Throws<SqliteException>();
    }

    [Test]
    public async Task FailedAuthorityUpdateRollsBackReceiptDispositionAndCurrentnessThenRetrySucceeds()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        // Fail after insertion, inside the completion trigger's authority update.
        fixture.Git.State.Execute("CREATE TRIGGER fixture_failure BEFORE UPDATE ON attempts BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END;");
        await Assert.That(async () => await fixture.Wait()).Throws<SqliteException>();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        var pin = "refs/broodling/accepted/" + fixture.Accepted;
        await Assert.That(fixture.Git.Git("rev-parse", pin).Trim()).IsEqualTo(fixture.Accepted);
        fixture.Git.State.Execute("DROP TRIGGER fixture_failure");
        await Assert.That((await fixture.Wait()).Outcome).IsEqualTo("SUCCEEDED");
        await Assert.That(fixture.Git.Git("rev-parse", pin).Trim()).IsEqualTo(fixture.Accepted);
    }

    [Test]
    public async Task AcceptedCommitSurvivesOriginLossAndGarbageCollection()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var completed = await fixture.Wait();
        Directory.Delete(fixture.Git.Origin, true);
        fixture.Git.Git("gc", "--prune=now");
        await Assert.That(fixture.Git.Git("show", fixture.Accepted + ":delivered.txt")).IsEqualTo("delivered\n");
        fixture.Transport.Wait = (_, _, _) => throw new Exception("Native unavailable");
        await Assert.That(await fixture.Wait()).IsEqualTo(completed);
    }

    [Test]
    public async Task UnfetchableAcceptedCommitLeavesResultConsumableUntilPublished()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var unpublished = fixture.Git.Deliver("unpublished\n", push: false);
        fixture.Transport.Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, CompletionFixture.Receipt(head: unpublished), null));
        await Assert.That(async () => await fixture.Wait()).Throws<ResultRetentionError>();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        await Assert.That(() => fixture.Git.Git("show-ref", "--verify", "refs/broodling/accepted/" + unpublished)).Throws<InvalidOperationException>();
        fixture.Git.Push();
        await Assert.That((await fixture.Wait()).AcceptedRevision).IsEqualTo(unpublished);
    }

    [Test]
    public async Task CancellingDuringAcceptedFetchDetachesAndStopsGit()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var entered = Path.Combine(fixture.Git.State.Root, "fetch-entered");
        var redirect = "url." + fixture.Git.Origin + ".insteadOf";
        var stalled = $"url.ext::sh -c echo% $$% >% {entered};% exec% sleep% 30.insteadOf";
        fixture.Git.Git("config", "--unset", redirect);
        fixture.Git.Git("config", "protocol.ext.allow", "always");
        fixture.Git.Git("config", stalled, "https://github.com/acme/widget.git");
        using var cancellation = new CancellationTokenSource();
        var waiting = fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null, cancellation.Token);
        // The loopback status read precedes the fetch; allow for a busy test host.
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(entered) || File.ReadAllText(entered).Length == 0)
        {
            if (waiting.IsCompleted) await waiting; // A failed wait surfaces as itself, not as a timeout.
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The fetch transport never started.");
            await Task.Delay(10);
        }
        var transport = int.Parse(File.ReadAllText(entered));
        cancellation.Cancel();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
        await Assert.That(Running(transport)).IsFalse();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        fixture.Git.Git("config", "--unset", stalled);
        fixture.Git.Git("config", redirect, "https://github.com/acme/widget.git");
        await Assert.That((await fixture.Wait()).AcceptedRevision).IsEqualTo(fixture.Accepted);
    }

    // A killed helper can linger briefly as a zombie until its reparented reaper collects it.
    private static bool Running(int pid)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            string stat;
            try { stat = File.ReadAllText($"/proc/{pid}/stat"); }
            catch (IOException) { return false; }
            if (stat[(stat.LastIndexOf(')') + 2)..].StartsWith('Z')) return false;
            Thread.Sleep(10);
        }
        return true;
    }

    [Test]
    public async Task ConflictingAcceptedPinRefusesWithoutRepointingOrCompleting()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var pin = "refs/broodling/accepted/" + fixture.Accepted;
        fixture.Git.Git("update-ref", pin, fixture.Git.Head);
        await Assert.That(async () => await fixture.Wait()).Throws<ResultRetentionError>();
        await Assert.That(fixture.Git.Git("rev-parse", pin).Trim()).IsEqualTo(fixture.Git.Head);
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    public async Task IndependentFinalizersConvergeAndNeverHoldWriterWhileAwaitingNative()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        using var other = fixture.Git.State.Open();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        fixture.Transport.Wait = async (_, run, _) =>
        {
            if (Interlocked.Increment(ref entered) == 2) ready.SetResult();
            await ready.Task;
            return new(run, true, CompletionFixture.Receipt(head: fixture.Accepted), null);
        };
        var both = await Task.WhenAll(fixture.Wait(), other.WaitAsync(fixture.Attempt.AttemptId, null));
        await Assert.That(both[0]).IsEqualTo(both[1]);
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(2);
        await Assert.That(fixture.Store.Status(fixture.Attempt.ContractRevisionId).Completions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FrozenInvocationIsValidatedBeforeWaitAndAgainBeforeFinalWrite()
    {
        using var fixture = new CompletionFixture();
        await fixture.Dispatch();
        var submitted = fixture.Store.FindSubmission(fixture.Attempt.AttemptId)!;
        fixture.Git.State.Execute("DROP TRIGGER submission_binding_stable");
        void Change(string request)
        {
            using var connection = fixture.Git.State.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE native_submissions SET request_json = $request";
            command.Parameters.AddWithValue("$request", request); command.ExecuteNonQuery();
        }
        var changed = JsonNode.Parse(submitted.RequestJson)!; changed["task"] = "Foreign Contract";
        Change(changed.ToJsonString());
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
        await Assert.That(fixture.Transport.WaitCalls).IsEqualTo(0);
        Change(submitted.RequestJson);
        fixture.Transport.Wait = (_, run, _) =>
        {
            Change(changed.ToJsonString());
            return Task.FromResult(new NativeResult(run, true, CompletionFixture.Receipt(head: fixture.Accepted), null));
        };
        await Assert.That(async () => await fixture.Wait()).Throws<SubmissionConflict>();
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
    }

    [Test]
    public async Task NoEffectSuccessCannotManufactureStableLocalResult()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport { Wait = (_, run, _) => Task.FromResult(new NativeResult(run, true, JsonSerializer.Deserialize<JsonElement>("null"), null)) };
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
        await Assert.That(async () => await store.WaitAsync(attempt.AttemptId, transport)).Throws<SubmissionNotReady>();
        store.RequireCurrentAttempt(attempt.AttemptId);
        await Assert.That(store.FindCompletion(attempt.AttemptId)).IsNull();
    }

    [Test]
    [Arguments("prepared")]
    [Arguments("dispatched")]
    public async Task HttpWaitRefusesUnacknowledgedWorkBeforeContact(string state)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, state);
        // Even a finished run under the intended ID is never consumed without acknowledgement.
        target.Projections.Enqueue(HttpFinished(submission, "succeeded", CompletionFixture.Receipt()));
        await Assert.That(async () => await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null)).Throws<SubmissionNotReady>();
        await Assert.That(target.Connections).IsEqualTo(0);
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
    }

    [Test]
    public async Task CorrelatedHttpWaitPinsTheAuthorizedDeliveryAndReplaysItOffline()
    {
        var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var accepted = fixture.Git.Deliver();
        target.Projections.Enqueue(HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));

        var completion = await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null);
        await Assert.That(completion.RunId).IsEqualTo(submission.IntendedRunId);
        await Assert.That(completion.AcceptedRevision).IsEqualTo(accepted);
        await Assert.That(fixture.Git.Git("rev-parse", "refs/broodling/accepted/" + accepted).Trim()).IsEqualTo(accepted);
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
        await target.DisposeAsync();
        await Assert.That(await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null)).IsEqualTo(completion);
    }

    [Test]
    public async Task CorrelatedHttpNativeFailureAbandonsWithOnlyASafeLabel()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        target.Projections.Enqueue(HttpFinished(submission, "failed", "canary_secret_reason"));

        await Assert.That(async () => await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null)).Throws<SubmissionNotReady>();
        var abandonment = fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!;
        await Assert.That(abandonment.Reason).IsEqualTo("Zeroshot run failed: native_failed");
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
    }

    [Test]
    [Arguments("foreign", "foreign_run")]
    [Arguments("unavailable", "TargetError")]
    public async Task DetachedHttpWaitLeavesAuthorityUntouched(string answer, string kind)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        if (answer == "unavailable") target.Session = (503, """{"code":"target.unavailable","message":"busy"}""");
        target.Projections.Enqueue(HttpFinished(submission, "failed", "runtime_failed", title: "Another run"));

        await DirectTargetSessionTests.Fails(() => fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null), kind);
        fixture.Store.RequireCurrentAttempt(fixture.Attempt.AttemptId);
        await Assert.That(target.Count("run/force")).IsEqualTo(0);
        await Assert.That(fixture.Store.FindCompletion(fixture.Attempt.AttemptId)).IsNull();
    }

    internal static JsonObject HttpFinished(NativeSubmission submission, string status, object detail, string? title = null)
    {
        var terminal = new JsonObject { ["status"] = status };
        if (status == "succeeded") terminal["output"] = JsonNode.Parse(((JsonElement)detail).GetRawText());
        else terminal["reason"] = (string)detail;
        var run = submission.Frozen.Run(submission.IntendedRunId!);
        return DirectTargetSessionTests.Projection(new JsonObject { ["phase"] = "finished", ["terminalResult"] = terminal },
            title is null ? run : run with { Title = title });
    }
}
