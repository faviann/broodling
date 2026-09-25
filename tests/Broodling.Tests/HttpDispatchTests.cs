using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// A controlled stock target for discovery and full-run submission on real loopback HTTP. Like the
/// stock target, a submission key names at most one run and a replay returns that run's ID.
/// </summary>
internal sealed class RunTarget : IAsyncDisposable
{
    internal const string Conflict = """{"code":"request.conflict","message":"Conflicting immutable submission"}""";
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task accepting;
    private int connections;
    internal string Origin { get; }
    internal int Connections => Volatile.Read(ref connections);
    /// <summary>Accepted runs by submission key.</summary>
    internal ConcurrentDictionary<string, string> Runs { get; } = new();
    internal List<string> Heads { get; } = [];
    internal List<JsonObject> Bodies { get; } = [];
    /// <summary>Awaited when discovery arrives, before its reply.</summary>
    internal Func<Task> Discovery { get; set; } = () => Task.CompletedTask;
    /// <summary>The reply to one complete submission; stock acceptance by default.</summary>
    internal Func<JsonObject, Task<(int Status, string Body)>> Submit { get; set; }
    /// <summary>Read half of each submission body, then wait without accepting it.</summary>
    internal bool StallMidBody { get; set; }
    internal TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal RunTarget()
    {
        Submit = body => Task.FromResult(Accept(body));
        listener.Start();
        Origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        accepting = Task.Run(async () =>
        {
            var handlers = new List<Task>();
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    Interlocked.Increment(ref connections);
                    handlers.Add(Handle(client));
                }
            }
            catch (Exception) when (stop.IsCancellationRequested) { }
            await Task.WhenAll(handlers);
        });
    }

    internal (int Status, string Body) Accept(JsonObject body) =>
        (200, new JsonObject { ["runId"] = Runs.GetOrAdd((string)body["submission"]!["submissionKey"]!, (string)body["runId"]!) }.ToJsonString());

    private async Task Handle(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var head = new StringBuilder();
            var octet = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(octet, stop.Token) == 1)
                head.Append((char)octet[0]);
            var text = head.ToString();
            if (text.StartsWith("GET /.well-known/zeroshot-native-v2 ", StringComparison.Ordinal))
            {
                await Discovery().WaitAsync(stop.Token);
                await Respond(stream, 200, """
                    {"kind":"zeroshot.native-v2-target/v2","authentication":"none","runPath":"/native-v2/run",
                     "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller"}
                    """);
                return;
            }
            if (!text.StartsWith("POST /native-v2/run ", StringComparison.Ordinal))
            {
                await Respond(stream, 404, """{"code":"request.not_found","message":"target route was not found"}""");
                return;
            }
            lock (Heads) Heads.Add(text);
            var length = int.Parse(text.Split("\r\n").Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))[15..]);
            var body = new byte[length];
            if (StallMidBody)
            {
                await stream.ReadExactlyAsync(body.AsMemory(0, length / 2), stop.Token);
                Stalled.TrySetResult();
                await Task.Delay(Timeout.Infinite, stop.Token);
            }
            await stream.ReadExactlyAsync(body, stop.Token);
            var request = JsonNode.Parse(body)!.AsObject();
            lock (Bodies) Bodies.Add(request);
            var (status, reply) = await Submit(request).WaitAsync(stop.Token);
            await Respond(stream, status, reply);
        }
        catch (Exception) { } // The client may abandon a connection; tests assert what the client observed.
    }

    private Task Respond(Stream stream, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Status\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n")
            .Concat(bytes).ToArray(), stop.Token).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        await accepting;
        stop.Dispose();
    }
}

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
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare(target: target.Origin);
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
    [Arguments("pin-missing", "prepared")]
    [Arguments("pin-missing", "dispatched")]
    [Arguments("pin-symbolic", "dispatched")]
    [Arguments("pin-conflicting", "prepared")]
    public async Task SendAndReplayGatesRefuseBeforeTargetContact(string defect, string phase)
    {
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        fixture.Prepare(target: target.Origin);
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
            "no-credentials" or "foreign-gateway" => typeof(UnsupportedRuntime),
            _ => typeof(SubmissionNotReady)
        });
        await Assert.That(target.Connections).IsEqualTo(0);
        await Assert.That(fixture.Store.FindSubmission(attempt.AttemptId)).IsEqualTo(before);
    }

    [Test]
    public async Task ReplaySendsTheFrozenRequestWithSeparatelyInjectedRotatedCredentials()
    {
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare(target: target.Origin);
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
        foreach (var head in target.Heads)
            await Assert.That(head.Contains("Content-Length:") && !head.Contains("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)).IsTrue();
        fixture.Store.Dispose();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(fixture.Git.State.Path)!))
            await Assert.That(Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("canary")).IsFalse();
    }

    [Test]
    [Arguments(200, "foreign", "foreign_run")]
    [Arguments(200, "uppercase", "foreign_run")]
    [Arguments(200, "extra", "invalid_response")]
    [Arguments(409, "conflict-run-id", "invalid_response")]
    [Arguments(409, "conflict-missing-message", "invalid_response")]
    [Arguments(409, "unknown-code", "TargetError")]
    [Arguments(500, "conflict", "TargetError")]
    [Arguments(500, "canary", "TargetError")]
    public async Task UnacknowledgedRepliesLeaveIntentUnresolvedAndAdoptNothing(int status, string variant, string kind)
    {
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare(target: target.Origin);
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
            "unknown-code" => """{"code":"request.other","message":"other"}""",
            "conflict" => RunTarget.Conflict,
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
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare(target: target.Origin);
        target.Submit = _ => Task.FromResult((409, RunTarget.Conflict));
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
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare(target: target.Origin);
        var arrived = Signal();
        var never = Signal();
        target.Submit = async _ => { arrived.TrySetResult(); await never.Task; return (500, ""); };
        var clock = new FakeTimeProvider();
        var expiring = fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials(), clock, CancellationToken.None);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(DirectTargetLimits.Submit);
        var timeout = await Assert.That(async () => await expiring).Throws<NativeTransportError>();
        await Assert.That(timeout!.Kind).IsEqualTo("TimeoutError");

        arrived = Signal();
        using var caller = new CancellationTokenSource();
        var cancelled = fixture.Store.DispatchHttpAsync(prepared.AttemptId, Credentials(), TimeProvider.System, caller.Token);
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
        await using var target = new RunTarget();
        var runId = Guid.CreateVersion7().ToString();
        var request = new JsonObject { ["runId"] = runId, ["padding"] = "" }.ToJsonString();
        request = request.Replace("\"padding\":\"\"", "\"padding\":\"" + new string('x', DirectTargetLimits.JsonBytes - request.Length - 16) + "\"");
        var credentials = new Dictionary<string, string>
        {
            ["GH_TOKEN"] = "github-token", ["GATEWAY_BASE_URL"] = NativeProfile.GatewayBaseUrl, ["GATEWAY_API_KEY"] = "gateway-key"
        };
        var error = await Assert.That(async () => await DirectTargetSubmission.SubmitAsync(new Uri(target.Origin), request, runId,
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
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var prepared = fixture.Prepare(target: target.Origin);
        var id = prepared.AttemptId;
        var arrived = new[] { Signal(), Signal() };
        var replies = new[] { Signal(), Signal() };
        var count = -1;
        target.Submit = async body =>
        {
            var turn = Interlocked.Increment(ref count);
            arrived[turn].TrySetResult();
            await replies[turn].Task;
            return turn == 0 && order != "both-acknowledge" ? (409, RunTarget.Conflict) : target.Accept(body);
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
    public async Task LateAcknowledgementAfterPauseAbandonmentAndCustodyLossRetainsCorrelationWithoutRestoringAuthority()
    {
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        var prepared = fixture.Prepare(target: target.Origin);
        var arrived = Signal();
        var release = Signal();
        target.Submit = async body => { arrived.TrySetResult(); await release.Task; return target.Accept(body); };
        var pending = fixture.Store.DispatchHttpAsync(attempt.AttemptId, Credentials());
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var observer = fixture.Git.State.Open();
            observer.PauseInstallation();
            observer.AbandonAttempt(attempt.AttemptId, "operator ended");
            fixture.Git.Git("update-ref", "-d", attempt.B1.RetentionRef);
        }
        finally { release.TrySetResult(); }

        await Assert.That(async () => await pending).Throws<StaleAttempt>();
        var correlated = fixture.Store.FindSubmission(attempt.AttemptId)!;
        await Assert.That(correlated.State).IsEqualTo("correlated");
        await Assert.That(correlated.RunId).IsEqualTo(prepared.IntendedRunId);
        var ended = fixture.Store.GetAttempt(attempt.AttemptId);
        await Assert.That(ended.IsCurrent).IsFalse();
        await Assert.That(ended.Abandonment).IsNotNull();
        await Assert.That(fixture.Store.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(0);

        // Correlated handback needs no authority, pause release, custody, credentials or target.
        var contacted = target.Connections;
        await Assert.That(await fixture.Store.DispatchHttpAsync(attempt.AttemptId, null)).IsEqualTo(correlated);
        await Assert.That(target.Connections).IsEqualTo(contacted);
    }

    [Test]
    public async Task DuplicateAcknowledgementAfterCompletionReturnsRetainedFacts()
    {
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var attempt = fixture.Attempt;
        var prepared = fixture.Prepare(target: target.Origin);
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
        await using var target = new RunTarget();
        using var fixture = new HttpFixture();
        var id = fixture.Attempt.AttemptId;
        fixture.Git.State.Execute("CREATE TRIGGER preparation_failure BEFORE INSERT ON native_submissions BEGIN SELECT RAISE(ABORT, 'preparation failure'); END;");
        await Assert.That(() => fixture.Prepare(target: target.Origin)).Throws<SqliteException>();
        await Assert.That(fixture.Store.FindSubmission(id)).IsNull();
        fixture.Git.State.Execute("DROP TRIGGER preparation_failure;");
        var prepared = fixture.Prepare(target: target.Origin);

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
