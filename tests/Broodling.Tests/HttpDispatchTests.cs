using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// HTTP submission and acknowledgement recovery through real SQLite/Git and a controlled loopback target.
/// Wire framing and discovery bounds belong to DirectTargetExchangeTests.
/// </summary>
public sealed class HttpDispatchTests
{
    internal static DispatchCredentials Credentials(string suffix = "current") =>
        new("github-canary-" + suffix, NativeProfile.GatewayBaseUrl, "gateway-canary-" + suffix);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Test]
    public async Task IntentCommitsBeforeDiscoveryAndOnlyTheExactAcknowledgementCorrelates()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var reached = Signal();
        var release = Signal();
        target.Discovery = async () => { reached.TrySetResult(); await release.Task; };
        // The frozen result-fetch origin survives later mutable remote configuration.
        fixture.Git.Git("remote", "set-url", "origin", "https://github.com/acme/other.git");

        var pending = fixture.Store.DispatchHttpAsync(fixture.Attempt.AttemptId, Credentials());
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = fixture.Git.State.Open();
            var intent = observer.FindSubmission(fixture.Attempt.AttemptId)!;
            await Assert.That(intent.State).IsEqualTo("dispatched");
            await Assert.That(intent.RunId).IsNull();
            // No SQLite writer spans the network operation; the local initiation lock does.
            var paused = observer.PauseInstallation();
            await Assert.That(paused.UnresolvedDispatches).IsEqualTo(1);
            await Assert.That(paused.InFlightInitiationDrained).IsFalse();
        }
        finally { release.TrySetResult(); }

        var correlated = await pending;
        await Assert.That(correlated.State).IsEqualTo("correlated");
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(correlated.BindingJson).IsEqualTo(prepared.BindingJson);
        await Assert.That(target.Runs.Values.Single()).IsEqualTo(prepared.IntendedRunId);
        var status = await InstallationPauseTests.SettledStatus(fixture.Store);
        await Assert.That(status.IsPaused).IsTrue(); // A pause after intent does not recall the authorized send.
        await Assert.That(status.UnresolvedDispatches).IsEqualTo(0);
        await Assert.That(status.InFlightInitiationDrained).IsTrue();
    }

    [Test]
    [Arguments("stale", "prepared")]
    [Arguments("stale", "dispatched")]
    [Arguments("paused", "prepared")]
    [Arguments("paused", "dispatched")]
    [Arguments("blocked", "dispatched")]
    [Arguments("asset", "prepared")]
    [Arguments("no-credentials", "prepared")]
    [Arguments("foreign-gateway", "dispatched")]
    [Arguments("gateway-suffix", "prepared")]
    [Arguments("blank-token", "prepared")]
    [Arguments("blank-key", "dispatched")]
    [Arguments("oversized-token", "prepared")]
    [Arguments("pin-missing", "prepared")]
    [Arguments("pin-symbolic", "dispatched")]
    [Arguments("pin-conflicting", "prepared")]
    public async Task SendAndReplayGatesRefuseBeforeTargetContact(string defect, string phase)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        fixture.PrepareAt(target.Origin);
        if (phase == "dispatched") fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        var credentials = Credentials();
        switch (defect)
        {
            case "stale": fixture.Store.AbandonAttempt(attempt.AttemptId, "ended"); break;
            case "paused": fixture.Store.PauseInstallation(); break;
            case "blocked": fixture.Git.State.Execute("UPDATE native_submissions SET replay_blocked_reason = 'submission_conflict'"); break;
            case "asset":
                fixture.Git.State.Execute("DROP TRIGGER execution_assets_no_update; UPDATE execution_assets SET content = CAST(CAST(content AS TEXT) || char(10) AS BLOB)");
                break;
            case "no-credentials": credentials = null; break;
            case "foreign-gateway": credentials = new("github-token", "https://gateway.example.test/v1", "gateway-key"); break;
            case "gateway-suffix": credentials = new("github-token", NativeProfile.GatewayBaseUrl + "/", "gateway-key"); break;
            case "blank-token": credentials = new("", NativeProfile.GatewayBaseUrl, "gateway-key"); break;
            case "blank-key": credentials = new("github-token", NativeProfile.GatewayBaseUrl, " "); break;
            case "oversized-token": credentials = new(new string('s', 4097), NativeProfile.GatewayBaseUrl, "gateway-key"); break;
            case "pin-missing": fixture.Git.Git("update-ref", "-d", attempt.B1.RetentionRef); break;
            case "pin-symbolic":
                fixture.Git.Git("update-ref", "-d", attempt.B1.RetentionRef);
                fixture.Git.Git("symbolic-ref", attempt.B1.RetentionRef, "refs/heads/main");
                break;
            default: fixture.Git.Git("update-ref", attempt.B1.RetentionRef, fixture.Git.Commit("moved")); break;
        }
        var before = fixture.Store.FindSubmission(attempt.AttemptId);

        var error = await Assert.That(async () => await fixture.Store.DispatchHttpAsync(attempt.AttemptId, credentials))
            .Throws<BroodlingException>();
        await Assert.That(error!.GetType()).IsEqualTo(defect switch
        {
            "stale" => typeof(StaleAttempt),
            "paused" => typeof(InstallationPaused),
            "blocked" or "asset" => typeof(SubmissionConflict),
            "no-credentials" or "foreign-gateway" or "gateway-suffix" or "blank-token" or "blank-key" or "oversized-token" => typeof(UnsupportedRuntime),
            _ => typeof(SubmissionNotReady)
        });
        await Assert.That(target.Connections).IsEqualTo(0);
        await Assert.That(fixture.Store.FindSubmission(attempt.AttemptId)).IsEqualTo(before);
    }

    [Test]
    public async Task ReplaySendsTheFrozenRequestWithSeparatelyInjectedRotatedCredentials()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        // The stock target can create the run and still answer 503, e.g. when exact B1 is missing from the forge.
        target.Submit = body => { target.Accept(body); return Task.FromResult((503, """{"code":"target.unavailable","message":"github-canary-first"}""")); };
        var error = await Assert.That(async () => await fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials("first")))
            .Throws<NativeTransportError>();
        await Assert.That(error!.Kind).IsEqualTo("TargetError");
        await Assert.That(error.Message.Contains("canary")).IsFalse();
        await Assert.That(fixture.Store.FindSubmission(prepared.AttemptId)!.State).IsEqualTo("dispatched");
        target.Submit = body => Task.FromResult(target.Accept(body));
        var correlated = await fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials("rotated"));

        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(correlated.RequestJson).IsEqualTo(prepared.RequestJson);
        await Assert.That(target.Runs.Count).IsEqualTo(1);
        foreach (var (body, suffix) in target.Bodies.Zip(["first", "rotated"]))
        {
            await Assert.That(body["connections"]!.ToJsonString()).IsEqualTo(
                $$$"""{"gateway":{"GATEWAY_BASE_URL":"{{{NativeProfile.GatewayBaseUrl}}}","GATEWAY_API_KEY":"gateway-canary-{{{suffix}}}"},"github":{"GH_TOKEN":"github-canary-{{{suffix}}}"}}""");
            await Assert.That((string)body["githubToken"]!).IsEqualTo("github-canary-" + suffix);
            body.Remove("connections");
            body.Remove("githubToken");
            await Assert.That(JsonNode.DeepEquals(body, JsonNode.Parse(prepared.RequestJson))).IsTrue();
        }
        foreach (var head in target.Heads.Where(head => head.StartsWith("POST /native-v2/run ", StringComparison.Ordinal)))
            await Assert.That(head.Contains("Content-Length:") && !head.Contains("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)).IsTrue();
        fixture.Store.Dispose();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(fixture.Git.State.Path)!))
            await Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("canary")).IsFalse();
    }

    [Test]
    public async Task BundleBoundRequestFrozenBeforeReferenceAccessReplaysWithItsExactBytes()
    {
        await using var target = new StockTarget();
        using var fixture = await BundleHttpFixture.CreateAsync();
        var attemptId = fixture.Attempt.AttemptId;
        // The earlier release prepared and sent it, and the acknowledgement was lost.
        var earlier = fixture.PrepareAsEarlierRelease(target.Origin);
        fixture.Git.State.Execute("UPDATE native_submissions SET state = 'dispatched'");
        fixture.Store.Dispose();

        using var store = fixture.Git.State.Open();
        var retained = store.FindSubmission(attemptId)!;
        await Assert.That(store.PrepareHttpSubmission(attemptId, target.Origin.GetLeftPart(UriPartial.Authority))).IsEqualTo(retained);
        var correlated = await store.DispatchHttpAsync(attemptId, Credentials());

        await Assert.That(correlated.State).IsEqualTo("correlated");
        await Assert.That(correlated.RequestJson).IsEqualTo(earlier);
        var sent = target.Bodies.Single();
        sent.Remove("connections");
        sent.Remove("githubToken");
        await Assert.That(JsonNode.DeepEquals(sent, JsonNode.Parse(earlier))).IsTrue();
    }

    [Test]
    [Arguments(200, "foreign", "foreign_run")]
    [Arguments(200, "uppercase", "foreign_run")]
    [Arguments(200, "extra", "invalid_response")]
    [Arguments(409, "conflict-run-id", "invalid_response")]
    [Arguments(409, "conflict-missing-message", "invalid_response")]
    [Arguments(409, "conflict-invalid-utf8", "invalid_response")]
    [Arguments(409, "unknown-code", "TargetError")]
    [Arguments(500, "conflict", "TargetError")]
    [Arguments(500, "canary", "TargetError")]
    public async Task UnacknowledgedRepliesLeaveIntentUnresolvedAndAdoptNothing(int status, string variant, string kind)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var intended = prepared.IntendedRunId!;
        var foreign = Guid.CreateVersion7().ToString();
        target.Submit = _ => Task.FromResult((status, variant switch
        {
            // The stock target answers a same-key submission with its original ID, never this Attempt's.
            "foreign" => $$"""{"runId":"{{foreign}}"}""",
            "uppercase" => $$"""{"runId":"{{intended.ToUpperInvariant()}}"}""",
            "extra" => $$"""{"runId":"{{intended}}","accepted":true}""",
            "conflict-run-id" => $$"""{"code":"request.conflict","message":"conflict","runId":"{{intended}}"}""",
            "conflict-missing-message" => """{"code":"request.conflict"}""",
            // Raw 0xFF: malformed text is no valid conflict, so it sets no replay block.
            "conflict-invalid-utf8" => "{\"code\":\"request.conflict\",\"message\":\"\u00FF\"}",
            "unknown-code" => """{"code":"request.other","message":"other"}""",
            "conflict" => StockTarget.Conflict,
            _ => """{"code":"SECRET_CANARY","message":"SECRET_CANARY","details":{"runId":"SECRET_CANARY"}}"""
        }));

        var error = await Assert.That(async () => await fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials()))
            .Throws<NativeTransportError>();
        await Assert.That(error!.Kind).IsEqualTo(kind);
        await Assert.That(error.Message.Contains("CANARY")).IsFalse();
        var unresolved = fixture.Store.FindSubmission(prepared.AttemptId)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        await Assert.That(unresolved.RunId).IsNull();
        await Assert.That(unresolved.ReplayBlockedReason).IsNull();
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);

        // Exact replay of the same key and identity remains available and converges.
        target.Submit = body => Task.FromResult(target.Accept(body));
        await Assert.That((await fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials())).RunId).IsEqualTo(intended);
    }

    [Test]
    public async Task ValidConflictBlocksFurtherSendsWithoutResolvingDispatch()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        target.Submit = _ => Task.FromResult((409, StockTarget.Conflict));
        await Assert.That(async () => await fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials()))
            .Throws<SubmissionConflict>();
        var blocked = fixture.Store.FindSubmission(prepared.AttemptId)!;
        await Assert.That(blocked.State).IsEqualTo("dispatched");
        await Assert.That(blocked.RunId).IsNull();
        await Assert.That(blocked.ReplayBlockedReason).IsEqualTo("submission_conflict");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);

        var contacted = target.Connections;
        using var reopened = fixture.Git.State.Open();
        await Assert.That(async () => await reopened.DispatchHttpAsync(prepared.AttemptId, Credentials())).Throws<SubmissionConflict>();
        await Assert.That(target.Connections).IsEqualTo(contacted);
        await Assert.That(reopened.FindSubmission(prepared.AttemptId)).IsEqualTo(blocked);
    }

    [Test]
    public async Task TimeoutAndCancellationPreserveUnresolvedIntent()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var arrived = Signal();
        var never = Signal();
        target.Submit = async _ => { arrived.TrySetResult(); await never.Task; return (500, ""); };
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;
        var expiring = fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials());
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(DirectTargetLimits.Submit);
        var timeout = await Assert.That(async () => await expiring).Throws<NativeTransportError>();
        await Assert.That(timeout!.Kind).IsEqualTo("TimeoutError");

        arrived = Signal();
        using var caller = new CancellationTokenSource();
        var cancelled = fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials(), caller.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();
        await Assert.That(async () => await cancelled).Throws<OperationCanceledException>();

        var unresolved = fixture.Store.FindSubmission(prepared.AttemptId)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        await Assert.That(unresolved.RunId).IsNull();
        await Assert.That((await InstallationPauseTests.SettledStatus(fixture.Store)).InFlightInitiationDrained).IsTrue();
    }

    [Test]
    public async Task OversizedCredentialBearingBodyIsRefusedBeforeAnyExchange()
    {
        await using var target = new StockTarget();
        var runId = Guid.CreateVersion7().ToString();
        var request = new JsonObject { ["runId"] = runId, ["padding"] = "" }.ToJsonString();
        request = request.Replace("\"padding\":\"\"", "\"padding\":\"" + new string('x', DirectTargetLimits.JsonBytes - request.Length - 16) + "\"");
        var credentials = new Dictionary<string, string>
        {
            ["GH_TOKEN"] = "github-token", ["GATEWAY_BASE_URL"] = NativeProfile.GatewayBaseUrl, ["GATEWAY_API_KEY"] = "gateway-key"
        };
        var error = await Assert.That(async () => await DirectTargetSubmission.SubmitAsync(target.Origin, null, request, runId,
            credentials, TimeProvider.System, CancellationToken.None)).Throws<NativeTransportError>();
        await Assert.That(error!.Kind).IsEqualTo("request_too_large");
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    [Test]
    [Arguments("conflict-first")]
    [Arguments("acknowledgement-first")]
    [Arguments("both-acknowledge")]
    public async Task ConcurrentRepliesConvergeOnTheIntendedCorrelationAndKeepAnyConflict(string order)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.PrepareAt(target.Origin);
        var id = prepared.AttemptId;
        var arrived = new[] { Signal(), Signal() };
        var replies = new[] { Signal(), Signal() };
        var count = -1;
        target.Submit = async body =>
        {
            var turn = Interlocked.Increment(ref count);
            arrived[turn].TrySetResult();
            await replies[turn].Task;
            return turn == 0 && order != "both-acknowledge" ? (409, StockTarget.Conflict) : target.Accept(body);
        };
        using var other = fixture.Git.State.Open();
        var first = fixture.Store.DispatchHttpAsync(id, Credentials("first"));
        await arrived[0].Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = other.DispatchHttpAsync(id, Credentials("second"));
        await arrived[1].Task.WaitAsync(TimeSpan.FromSeconds(10));

        if (order == "conflict-first")
        {
            replies[0].SetResult();
            await Assert.That(async () => await first).Throws<SubmissionConflict>();
            await Assert.That(fixture.Store.FindSubmission(id)!.ReplayBlockedReason).IsEqualTo("submission_conflict");
            replies[1].SetResult(); // Already in flight: the block stops new sends, not this acknowledgement.
            await Assert.That((await second).RunId).IsEqualTo(prepared.IntendedRunId);
        }
        else
        {
            replies[1].SetResult();
            await Assert.That((await second).RunId).IsEqualTo(prepared.IntendedRunId);
            replies[0].SetResult();
            await Assert.That((await first).RunId).IsEqualTo(prepared.IntendedRunId);
        }

        using var reopened = fixture.Git.State.Open();
        var settled = reopened.FindSubmission(id)!;
        await Assert.That(settled.State).IsEqualTo("correlated");
        await Assert.That(settled.RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(settled.ReplayBlockedReason).IsEqualTo(order == "both-acknowledge" ? null : "submission_conflict");
        await Assert.That(reopened.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);
        await Assert.That(target.Runs.Count).IsEqualTo(1);
        var contacted = target.Connections;
        await Assert.That(await reopened.DispatchHttpAsync(id, null)).IsEqualTo(settled);
        await Assert.That(target.Connections).IsEqualTo(contacted);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LateAcknowledgementAfterAbandonmentRetainsCorrelationThenStopsThatExactRun(bool stopReachable)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        var prepared = fixture.PrepareAt(target.Origin);
        var arrived = Signal();
        var release = Signal();
        target.Submit = async body =>
        {
            arrived.TrySetResult();
            await release.Task;
            return (200, new JsonObject { ["runId"] = (string)body["runId"]! }.ToJsonString());
        };
        target.Projections.Enqueue(DirectTargetSessionTests.Projection(new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
        }, prepared.Frozen.Run(prepared.IntendedRunId!)));
        if (!stopReachable) target.Session = (503, """{"code":"target.unavailable","message":"unavailable"}""");
        var pending = fixture.Store.DispatchHttpAsync(attempt.AttemptId, Credentials());
        try
        {
            await arrived.Task.WaitAsync(DirectTargetSessionTests.Patience);
            // Pause, abandonment and custody loss race the acknowledgement already in flight.
            using var observer = fixture.Git.State.Open();
            observer.PauseInstallation();
            observer.AbandonAttempt(attempt.AttemptId, "operator ended");
            fixture.Git.Git("update-ref", "-d", attempt.B1.RetentionRef);
        }
        finally { release.TrySetResult(); }

        var stale = await Assert.That(async () => await pending).Throws<StaleAttempt>();
        await Assert.That(stale!.NativeStopRequested).IsEqualTo(stopReachable);
        var correlated = fixture.Store.FindSubmission(attempt.AttemptId)!;
        await Assert.That(correlated.State).IsEqualTo("correlated");
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        // The confirmed run is forced once, without precheck or another submission.
        await Assert.That(target.Count("run/status")).IsEqualTo(0);
        await Assert.That(target.Count("run/force")).IsEqualTo(stopReachable ? 1 : 0);
        if (stopReachable) await Assert.That((string)target.Messages.Last()["params"]!["runId"]!).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(target.Stages.Count(stage => stage == "run")).IsEqualTo(1);
        var ended = fixture.Store.GetAttempt(attempt.AttemptId);
        await Assert.That(ended.IsCurrent).IsFalse();
        await Assert.That(ended.Abandonment!.Reason).IsEqualTo("operator ended");
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);
        await Assert.That(() => fixture.Store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();

        // Correlated handback needs no authority, pause release, custody, credentials or target.
        var contacted = target.Connections;
        await Assert.That(await fixture.Store.DispatchHttpAsync(attempt.AttemptId, null)).IsEqualTo(correlated);
        await Assert.That(target.Connections).IsEqualTo(contacted);
    }

    [Test]
    public async Task DuplicateAcknowledgementAfterCompletionReturnsRetainedFacts()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        var prepared = fixture.PrepareAt(target.Origin);
        var arrived = new[] { Signal(), Signal() };
        var replies = new[] { Signal(), Signal() };
        var count = -1;
        target.Submit = async body =>
        {
            var turn = Interlocked.Increment(ref count);
            arrived[turn].TrySetResult();
            await replies[turn].Task;
            return target.Accept(body);
        };
        using var other = fixture.Git.State.Open();
        var first = fixture.Store.DispatchHttpAsync(attempt.AttemptId, Credentials());
        await arrived[0].Task.WaitAsync(TimeSpan.FromSeconds(10));
        var duplicate = other.DispatchHttpAsync(attempt.AttemptId, Credentials());
        await arrived[1].Task.WaitAsync(TimeSpan.FromSeconds(10));
        replies[0].SetResult();
        await first;
        fixture.Git.State.Execute($"""
            INSERT INTO attempt_completions SELECT attempt_id, work_unit_id, contract_revision_id, '{prepared.IntendedRunId}',
                '{CompletionFixture.Receipt().GetRawText()}', 'now' FROM attempts WHERE attempt_id = '{attempt.AttemptId}'
            """);
        replies[1].SetResult();

        await Assert.That((await duplicate).RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(fixture.Store.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
        await Assert.That(fixture.Store.FindCompletion(attempt.AttemptId)).IsNotNull();
    }

    [Test]
    public async Task FailedPreparationOrCorrelationCommitLeavesOnlyCommittedFacts()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var id = fixture.Attempt.AttemptId;
        fixture.Git.State.Execute("CREATE TRIGGER preparation_failure BEFORE INSERT ON native_submissions BEGIN SELECT RAISE(ABORT, 'preparation failure'); END;");
        await Assert.That(() => fixture.PrepareAt(target.Origin)).Throws<SqliteException>();
        await Assert.That(fixture.Store.FindSubmission(id)).IsNull();
        fixture.Git.State.Execute("DROP TRIGGER preparation_failure;");
        var prepared = fixture.PrepareAt(target.Origin);

        using var reopened = fixture.Git.State.Open();
        fixture.Git.State.Execute("CREATE TRIGGER correlation_failure BEFORE UPDATE ON native_submissions WHEN NEW.state = 'correlated' BEGIN SELECT RAISE(ABORT, 'correlation failure'); END;");
        await Assert.That(async () => await reopened.DispatchHttpAsync(id, Credentials())).Throws<SqliteException>();
        var unresolved = reopened.FindSubmission(id)!;
        await Assert.That(unresolved.State).IsEqualTo("dispatched");
        await Assert.That(unresolved.RunId).IsNull();
        await Assert.That(reopened.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(1);
        fixture.Git.State.Execute("DROP TRIGGER correlation_failure;");

        await Assert.That((await reopened.DispatchHttpAsync(id, Credentials())).RunId).IsEqualTo(prepared.IntendedRunId);
        await Assert.That(target.Bodies.Count).IsEqualTo(2);
        await Assert.That(target.Runs.Count).IsEqualTo(1);
    }
}
