using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>
/// The shared DirectTarget run reader against a controlled stock target on real loopback HTTP and
/// WebSocket connections. Byte, fragment, header and depth bounds belong to DirectTargetExchangeTests.
/// </summary>
public sealed class DirectTargetSessionTests
{
    /// <summary>Real-time guard for loopback progress; generous because the whole suite shares the thread pool.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    internal const string RunId = "01996f2e-7a4b-7c3d-8e5f-0123456789ab";
    private static readonly NativeSource Source = new("owner/widget", "broodling/fix-widget", new string('b', 40));

    [Test]
    public async Task StockSetupReadsTheNamedRunAndTheSessionServesLaterBudgets()
    {
        await using var target = new StockTarget();
        target.Projections.Enqueue(Running());
        target.Projections.Enqueue(Projection(new JsonObject { ["phase"] = "admitted" }));
        using var first = Budget();
        await using var session = await DirectTargetSession.OpenAsync(Binding(target.Origin), first);
        var running = await session.StatusAsync(first);
        using var later = Budget();
        var admitted = await session.StatusAsync(later);

        await Assert.That(running.Progress.Phase).IsEqualTo("running");
        await Assert.That(running.Progress.ActiveNodes).IsEquivalentTo(["worker", "verifier"]);
        await Assert.That(running.Result).IsNull();
        await Assert.That(admitted.Progress.Phase).IsEqualTo("admitted");
        await Assert.That(string.Join(" ", target.Stages)).IsEqualTo("discovery session upgrade initialize run/status run/status");
        await Assert.That(target.SessionBody).IsEqualTo($$"""{"runId":"{{RunId}}"}""");
        await Assert.That(target.Heads.All(head => !head.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            && !head.Contains("Cookie", StringComparison.OrdinalIgnoreCase)
            && !head.Contains("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(target.Messages.Select(message => message.ToJsonString()).SequenceEqual([
            """{"jsonrpc":"2.0","id":"1","method":"initialize","params":{"protocolVersion":"openengine.cluster/v1"}}""",
            $$$"""{"jsonrpc":"2.0","id":"2","method":"run/status","params":{"runId":"{{{RunId}}}"}}""",
            $$$"""{"jsonrpc":"2.0","id":"3","method":"run/status","params":{"runId":"{{{RunId}}}"}}"""])).IsTrue();
    }

    [Test]
    [Arguments("""{"status":"succeeded","output":null}""", null, true, null)]
    [Arguments("""{"status":"succeeded","output":{"receipt":{"pr":7}}}""",
        """{"tokenUsage":{"inputTokens":9007199254740991,"outputTokens":0,"complete":false,"cacheReadInputTokens":null,"cacheCreationInputTokens":3}}""", true, null)]
    [Arguments("""{"status":"failed","reason":"runtime_failed"}""", """{}""", false, "runtime_failed")]
    [Arguments("""{"status":"failed","reason":"canary_secret_label"}""", """{"tokenUsage":null}""", false, "native_failed")]
    public async Task FinishedProjectionCarriesOnlyASafeTerminalResult(string terminal, string? metadata, bool succeeded, string? failure)
    {
        var status = new JsonObject { ["phase"] = "finished", ["terminalResult"] = JsonNode.Parse(terminal) };
        if (metadata is not null) status["metadata"] = JsonNode.Parse(metadata);
        await using var target = new StockTarget();
        target.Projections.Enqueue(Projection(status));
        var read = await Read(target);

        await Assert.That(read.Progress.Phase).IsEqualTo("finished");
        await Assert.That(read.Progress.ActiveNodes).IsEmpty();
        await Assert.That(read.Result!.RunId).IsEqualTo(RunId);
        await Assert.That(read.Result.Succeeded).IsEqualTo(succeeded);
        await Assert.That(read.Result.Failure).IsEqualTo(failure);
        if (succeeded) await Assert.That(read.Result.Output.GetRawText()).IsEqualTo(JsonNode.Parse(terminal)!["output"]?.ToJsonString() ?? "null");
    }

    [Test]
    [Arguments("run", "foreign_run")]
    [Arguments("title", "foreign_run")]
    [Arguments("size", "foreign_run")]
    [Arguments("repository", "foreign_run")]
    [Arguments("branch", "foreign_run")]
    [Arguments("revision", "foreign_run")]
    [Arguments("projection-field", "invalid_response")]
    [Arguments("status-field", "invalid_response")]
    [Arguments("execution-field", "invalid_response")]
    [Arguments("terminal-field", "invalid_response")]
    [Arguments("metadata-field", "invalid_response")]
    [Arguments("title-number", "invalid_response")]
    [Arguments("cursor-number", "invalid_response")]
    [Arguments("unknown-phase", "invalid_response")]
    [Arguments("running-without-executions", "invalid_response")]
    [Arguments("finished-with-executions", "invalid_response")]
    [Arguments("succeeded-without-output", "invalid_response")]
    [Arguments("failed-with-output", "invalid_response")]
    [Arguments("failure-label", "invalid_response")]
    [Arguments("node-name", "invalid_response")]
    [Arguments("empty-execution", "invalid_response")]
    [Arguments("null-metadata", "invalid_response")]
    [Arguments("unsafe-count", "invalid_response")]
    [Arguments("negative-count", "invalid_response")]
    public async Task ProjectionMustBeTheExpectedRunInThePinnedUnion(string change, string kind)
    {
        var projection = Running();
        var status = projection["status"]!.AsObject();
        var finished = new JsonObject
        {
            ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "succeeded", ["output"] = null },
            ["metadata"] = new JsonObject { ["tokenUsage"] = new JsonObject { ["inputTokens"] = 1, ["outputTokens"] = 1, ["complete"] = true } }
        };
        switch (change)
        {
            case "run": projection["runId"] = "01996f2e-7a4b-7c3d-8e5f-0123456789ac"; break;
            case "title": projection["title"] = "Fix the other widget"; break;
            case "size": projection["size"] = "medium"; break;
            case "repository": projection["source"]!["repository"] = "owner/other"; break;
            case "branch": projection["source"]!["branch"] = "main"; break;
            case "revision": projection["source"]!["revision"] = new string('c', 40); break;
            case "projection-field": projection["submissionKey"] = "key"; break;
            case "status-field": status["worker"] = "worker"; break;
            case "execution-field": status["activeExecutions"]![0]!["attempt"] = 1; break;
            case "terminal-field": finished["terminalResult"]!["reason"] = "force_stopped"; projection["status"] = finished; break;
            case "metadata-field": finished["metadata"]!["cost"] = 1; projection["status"] = finished; break;
            case "title-number": projection["title"] = 7; break;
            case "cursor-number": projection["atCursor"] = 7; break;
            case "unknown-phase": status["phase"] = "paused"; break;
            case "running-without-executions": status.Remove("activeExecutions"); break;
            case "finished-with-executions": finished["activeExecutions"] = new JsonArray(); projection["status"] = finished; break;
            case "succeeded-without-output": finished["terminalResult"]!.AsObject().Remove("output"); projection["status"] = finished; break;
            case "failed-with-output": finished["terminalResult"] = JsonNode.Parse("""{"status":"failed","reason":"runtime_lost","output":null}"""); projection["status"] = finished; break;
            case "failure-label": finished["terminalResult"] = JsonNode.Parse("""{"status":"failed","reason":"runtime failed"}"""); projection["status"] = finished; break;
            case "node-name": status["activeExecutions"]![0]!["node"] = "1worker"; break;
            case "empty-execution": status["activeExecutions"]![0]!["execution"] = ""; break;
            case "null-metadata": finished["metadata"] = null; projection["status"] = finished; break;
            case "unsafe-count": finished["metadata"]!["tokenUsage"]!["inputTokens"] = 9007199254740992; projection["status"] = finished; break;
            case "negative-count": finished["metadata"]!["tokenUsage"]!["outputTokens"] = -1; projection["status"] = finished; break;
        }
        await using var target = new StockTarget();
        target.Projections.Enqueue(projection);
        await Fails(() => Read(target), kind);
    }

    [Test]
    [Arguments(200, """{"endpoint":"ws://{authority}/native-v2/oecp","bearerToken":null}""", null)]
    [Arguments(200, """{"endpoint":"ws://127.0.0.2:{port}/native-v2/oecp"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"ws://127.0.0.1:1/native-v2/oecp"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"wss://{authority}/native-v2/oecp"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"ws://{authority}/native-v2/oecp?token=secret"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"ws://user:secret@{authority}/native-v2/oecp"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"ws://{authority}/oecp"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"ws://{authority}/native-v2/oecp","bearerToken":"secret"}""", "invalid_response")]
    [Arguments(200, """{"endpoint":"ws://{authority}/native-v2/oecp","runId":"x"}""", "invalid_response")]
    [Arguments(404, """{"code":"request.not_found","message":"canary secret","details":{"canary":"secret"}}""", "TargetError")]
    [Arguments(500, """{"error":"canary secret"}""", "invalid_response")]
    public async Task SessionMustNameTheOriginsOwnOecpRouteBeforeConnecting(int status, string body, string? kind)
    {
        await using var target = new StockTarget();
        target.Session = (status, body.Replace("{authority}", target.Origin.Authority).Replace("{port}", target.Origin.Port.ToString()));
        target.Projections.Enqueue(Running());
        if (kind is null)
        {
            await Assert.That((await Read(target)).Progress.Phase).IsEqualTo("running");
            return;
        }
        await Fails(() => Read(target), kind);
        await Assert.That(string.Join(" ", target.Stages)).IsEqualTo("discovery session");
    }

    [Test]
    [Arguments("protocol")]
    [Arguments("profiles")]
    [Arguments("logs")]
    [Arguments("status")]
    [Arguments("field")]
    [Arguments("unsupported-error")]
    public async Task InitializationMustBeTheStockResponse(string change)
    {
        var result = StockTarget.Initialize();
        switch (change)
        {
            case "protocol": result["protocolVersion"] = "openengine.cluster/v2"; break;
            case "profiles": result["capabilities"]!["graphProfiles"]!.AsArray().Add("openengine.graph.single-worker/v1"); break;
            case "logs": result["capabilities"]!["logs"] = false; break;
            case "status": result["status"]!["currentRunId"] = RunId; break;
            case "field": result["extensions"] = new JsonObject(); break;
        }
        await using var target = new StockTarget();
        target.Reply = (request, id) => (string)request["method"]! != "initialize" ? null
            : change == "unsupported-error"
                ? """{"jsonrpc":"2.0","id":"ID","error":{"code":-32000,"message":"no","data":{"code":"UNSUPPORTED_PROTOCOL_VERSION","details":null}}}""".Replace("ID", id)
                : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
        await Assert.That(async () => { await Read(target); }).Throws<UnsupportedRuntime>();
        await Assert.That(target.Stages.Contains("run/status")).IsFalse();
    }

    [Test]
    [Arguments("""{"jsonrpc":"2.0","id":"99","result":{status}}""")]
    [Arguments("""{"jsonrpc":"2.0","id":2,"result":{status}}""")]
    [Arguments("""{"jsonrpc":"2.0","id":"1","result":{status}}""")]
    [Arguments("""{"id":"2","result":{status}}""")]
    [Arguments("""{"jsonrpc":"1.0","id":"2","result":{status}}""")]
    [Arguments("""{"jsonrpc":"2.0","id":"2","result":{status},"error":{"code":1,"message":"x"}}""")]
    [Arguments("""{"jsonrpc":"2.0","id":"2"}""")]
    [Arguments("""{"jsonrpc":"2.0","id":"2","result":{status},"meta":1}""")]
    [Arguments("""{"jsonrpc":"2.0","method":"event","params":{status}}""")]
    [Arguments("""[{"jsonrpc":"2.0","id":"2","result":{status}}]""")]
    [Arguments("""{"jsonrpc":"2.0","id":"2","error":{"code":"-32000","message":"x"}}""")]
    [Arguments("""{"jsonrpc":"2.0","id":"2","error":{"code":-32000,"message":"x","data":{"code":"NOT_FOUND","trace":"x"}}}""")]
    public async Task RpcReplyMustAnswerTheOutstandingRequest(string reply)
    {
        await using var target = new StockTarget();
        target.Reply = (request, _) => (string)request["method"]! == "run/status"
            ? reply.Replace("{status}", Running().ToJsonString()) : null;
        await Fails(() => Read(target), "invalid_response");
    }

    [Test]
    [Arguments("""{"code":-32000,"message":"canary secret","data":{"code":"NOT_FOUND"}}""", "RunNotFoundError")]
    [Arguments("""{"code":-32603,"message":"canary secret","data":{"code":"INTERNAL_ERROR","details":null}}""", "TargetError")]
    [Arguments("""{"code":-32000,"message":"canary secret","data":{"code":"CANARY_SECRET","details":{"canary":"secret"}}}""", "TargetError")]
    [Arguments("""{"code":-32602,"message":"canary secret"}""", "TargetError")]
    public async Task OecpErrorsBecomeFixedClassificationsWithoutRemoteText(string error, string kind)
    {
        await using var target = new StockTarget();
        target.Reply = (request, id) => (string)request["method"]! == "run/status"
            ? $$"""{"jsonrpc":"2.0","id":"{{id}}","error":{{error}}}""" : null;
        var failure = await Fails(() => Read(target), kind);
        await Assert.That(failure.ToString().Contains("canary", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    [Arguments("local", "http://127.0.0.1:{port}")]
    [Arguments("direct", "http://localhost:{port}")]
    [Arguments("direct", "http://192.0.2.1:{port}")]
    [Arguments("direct", "http://127.0.0.1:{port}/")]
    [Arguments("direct", "http://127.0.0.1:{port}/prefix")]
    [Arguments("direct", "http://127.0.0.1:{port}?key=secret")]
    [Arguments("direct", "http://user:secret@127.0.0.1:{port}")]
    [Arguments("direct", "ftp://127.0.0.1:{port}")]
    [Arguments("direct", "no-source")]
    public async Task UnsupportedBindingsAreRefusedBeforeContact(string kind, string address)
    {
        await using var target = new StockTarget();
        var binding = address == "no-source"
            ? Binding(target.Origin) with { Source = null }
            : new NativeRunBinding(new NativeLocator(kind, address.Replace("{port}", target.Origin.Port.ToString())), RunId,
                "Fix the widget", "small", Source);
        using var budget = Budget();
        await Assert.That(async () => { await DirectTargetSession.OpenAsync(binding, budget); }).Throws<UnsupportedRuntime>();
        await Assert.That(target.Connections).IsEqualTo(0);
    }

    [Test]
    [MatrixDataSource]
    public async Task EachSetupStageEndsByDeadlineOrCallerWithoutLaterRequests(
        [Matrix("discovery", "session", "upgrade", "initialize", "run/status")] string stage, [Matrix(false, true)] bool callerCancels)
    {
        await using var target = new StockTarget { StallAt = stage };
        target.Projections.Enqueue(Running());
        using var caller = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, clock, caller.Token);
        var read = Task.Run(async () =>
        {
            await using var session = await DirectTargetSession.OpenAsync(Binding(target.Origin), budget);
            return await session.StatusAsync(budget);
        });
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
        string[] stages = ["discovery", "session", "upgrade", "initialize", "run/status"];
        await Assert.That(string.Join(" ", target.Stages)).IsEqualTo(string.Join(" ", stages[..(Array.IndexOf(stages, stage) + 1)]));
    }

    internal static NativeRunBinding Binding(Uri origin) =>
        new(new NativeLocator("direct", origin.GetLeftPart(UriPartial.Authority)), RunId, "Fix the widget", "small", Source);

    private static DirectTargetBudget Budget() => DirectTargetBudget.Start(DirectTargetLimits.Progress, new FakeTimeProvider(), default);

    private static async Task<DirectTargetRunStatus> Read(StockTarget target)
    {
        using var budget = Budget();
        await using var session = await DirectTargetSession.OpenAsync(Binding(target.Origin), budget);
        return await session.StatusAsync(budget);
    }

    internal static JsonObject Projection(JsonObject status) => new()
    {
        ["runId"] = RunId, ["title"] = "Fix the widget", ["size"] = "small", ["atCursor"] = "opaque-cursor",
        ["source"] = new JsonObject { ["repository"] = Source.Repository, ["branch"] = Source.Branch, ["revision"] = Source.Revision },
        ["status"] = status
    };

    internal static JsonObject Running() => Projection(new JsonObject
    {
        ["phase"] = "running",
        ["activeExecutions"] = new JsonArray(
            new JsonObject { ["execution"] = "execution-1", ["node"] = "worker" },
            new JsonObject { ["execution"] = "execution-2", ["node"] = "verifier" })
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

    /// <summary>
    /// A loopback stand-in for the stock target: discovery, session creation and an OECP WebSocket
    /// answering initialize, then run/status and run/force from <see cref="Projections"/>, where a
    /// null entry never replies. It records each stage reached and can stall at one.
    /// </summary>
    internal sealed class StockTarget : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task accepting;
        private int connections;
        internal Uri Origin { get; }
        internal int Connections => Volatile.Read(ref connections);
        internal string? StallAt { get; init; }
        internal TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal (int Status, string Body)? Session { get; set; }
        internal Queue<JsonObject?> Projections { get; } = new();
        /// <summary>A raw reply for a request, or null for the stock reply.</summary>
        internal Func<JsonObject, string, string?> Reply { get; set; } = (_, _) => null;
        internal List<string> Stages { get; } = [];
        internal List<string> Heads { get; } = [];
        internal List<JsonObject> Messages { get; } = [];
        internal string? SessionBody { get; private set; }

        internal StockTarget()
        {
            listener.Start();
            Origin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
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
                catch (Exception) when (stop.IsCancellationRequested) { } // Disposed, possibly before the first accept.
                await Task.WhenAll(handlers);
            });
        }

        internal static JsonObject Initialize() => new()
        {
            ["protocolVersion"] = "openengine.cluster/v1",
            ["capabilities"] = new JsonObject { ["graphProfiles"] = new JsonArray("openengine.graph.full/v1"), ["logs"] = true, ["agentAttach"] = true },
            ["status"] = new JsonObject { ["phase"] = "empty", ["observedGeneration"] = null, ["currentRunId"] = null, ["atCursor"] = null }
        };

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
                lock (Heads) Heads.Add(text);
                var line = text[..text.IndexOf('\r')];
                if (line.StartsWith("GET /.well-known/zeroshot-native-v2 ", StringComparison.Ordinal))
                {
                    await Reached("discovery");
                    await Respond(stream, 200, """
                        {"kind":"zeroshot.native-v2-target/v2","authentication":"none","runPath":"/native-v2/run",
                         "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller"}
                        """);
                }
                else if (line.StartsWith("POST /native-v2/oecp-session ", StringComparison.Ordinal))
                {
                    var length = int.Parse(Header(text, "Content-Length")!);
                    var body = new byte[length];
                    await stream.ReadExactlyAsync(body, stop.Token);
                    SessionBody = Encoding.UTF8.GetString(body);
                    await Reached("session");
                    var (status, reply) = Session ?? (200, $$"""{"endpoint":"ws://{{Origin.Authority}}/native-v2/oecp"}""");
                    await Respond(stream, status, reply);
                }
                else if (line.StartsWith("GET /native-v2/oecp ", StringComparison.Ordinal))
                {
                    await Reached("upgrade");
                    var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                        Header(text, "Sec-WebSocket-Key") + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), stop.Token);
                    using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true });
                    await Serve(socket);
                }
                else await Respond(stream, 404, """{"code":"request.not_found","message":"target route was not found"}""");
            }
            catch (Exception) { } // The client may abandon a connection; tests assert what the client observed.
        }

        private async Task Serve(WebSocket socket)
        {
            var buffer = new byte[64 * 1024];
            while (true)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer.AsMemory(), stop.Token);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                var request = JsonNode.Parse(message.ToArray())!.AsObject();
                lock (Messages) Messages.Add(request);
                var method = (string)request["method"]!;
                var id = (string)request["id"]!;
                await Reached(method);
                var reply = Reply(request, id);
                if (reply is null)
                {
                    var result = method == "initialize" ? Initialize() : Projections.Dequeue();
                    if (result is null)
                    {
                        Stalled.TrySetResult();
                        await Task.Delay(Timeout.Infinite, stop.Token);
                    }
                    reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
                }
                await socket.SendAsync(Encoding.UTF8.GetBytes(reply), WebSocketMessageType.Text, true, stop.Token);
            }
        }

        internal int Count(string method)
        {
            lock (Messages) return Messages.Count(message => (string)message["method"]! == method);
        }

        private async Task Reached(string stage)
        {
            lock (Stages) Stages.Add(stage);
            if (stage != StallAt) return;
            Stalled.TrySetResult();
            await Task.Delay(Timeout.Infinite, stop.Token);
        }

        private static string? Header(string head, string name) => head.Split("\r\n")
            .FirstOrDefault(line => line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim();

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
}
