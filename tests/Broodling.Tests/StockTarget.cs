using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Broodling.Tests;

/// <summary>
/// A loopback stand-in for the stock target on real HTTP and WebSocket connections: discovery,
/// full-run submission, session creation and an OECP WebSocket answering initialize, then
/// run/status and run/force from <see cref="Projections"/>, where a null entry never replies. Like
/// the stock target, a submission key names at most one run and a replay returns that run's ID.
/// It records each stage reached and can stall at one, or halfway through a submission body.
/// </summary>
internal sealed class StockTarget : IAsyncDisposable
{
    internal const string Conflict = """{"code":"request.conflict","message":"Conflicting immutable submission"}""";
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task accepting;
    private int connections;
    internal Uri Origin { get; }
    internal int Connections => Volatile.Read(ref connections);
    internal string? StallAt { get; init; }
    /// <summary>Read half of each submission body, then wait without accepting it.</summary>
    internal bool StallMidBody { get; set; }
    internal TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>When set, writes the whole response to every request instead of any stock route.</summary>
    internal Func<Stream, CancellationToken, Task>? Raw { get; init; }
    /// <summary>Awaited when discovery arrives, before its reply.</summary>
    internal Func<Task> Discovery { get; set; } = () => Task.CompletedTask;
    /// <summary>The reply to one complete submission; stock acceptance by default.</summary>
    internal Func<JsonObject, Task<(int Status, string Body)>> Submit { get; set; }
    /// <summary>Accepted runs by submission key.</summary>
    internal ConcurrentDictionary<string, string> Runs { get; } = new();
    internal List<JsonObject> Bodies { get; } = [];
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
        Submit = body => Task.FromResult(Accept(body));
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

    internal (int Status, string Body) Accept(JsonObject body) =>
        (200, new JsonObject { ["runId"] = Runs.GetOrAdd((string)body["submission"]!["submissionKey"]!, (string)body["runId"]!) }.ToJsonString());

    internal int Count(string method)
    {
        lock (Messages) return Messages.Count(message => (string)message["method"]! == method);
    }

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
            if (Raw is { } raw)
            {
                await raw(stream, stop.Token);
                return;
            }
            var line = text[..text.IndexOf('\r')];
            if (line.StartsWith("GET /.well-known/zeroshot-native-v2 ", StringComparison.Ordinal))
            {
                await Reached("discovery");
                await Discovery().WaitAsync(stop.Token);
                await Respond(stream, 200, """
                    {"kind":"zeroshot.native-v2-target/v2","authentication":"none","runPath":"/native-v2/run",
                     "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller"}
                    """);
            }
            else if (line.StartsWith("POST /native-v2/run ", StringComparison.Ordinal))
            {
                var body = new byte[int.Parse(Header(text, "Content-Length")!)];
                if (StallMidBody)
                {
                    await stream.ReadExactlyAsync(body.AsMemory(0, body.Length / 2), stop.Token);
                    Stalled.TrySetResult();
                    await Task.Delay(Timeout.Infinite, stop.Token);
                }
                await stream.ReadExactlyAsync(body, stop.Token);
                var request = JsonNode.Parse(body)!.AsObject();
                lock (Bodies) Bodies.Add(request);
                await Reached("run");
                var (status, reply) = await Submit(request).WaitAsync(stop.Token);
                await Respond(stream, status, reply);
            }
            else if (line.StartsWith("POST /native-v2/oecp-session ", StringComparison.Ordinal))
            {
                var body = new byte[int.Parse(Header(text, "Content-Length")!)];
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

    private async Task Reached(string stage)
    {
        lock (Stages) Stages.Add(stage);
        if (stage != StallAt) return;
        Stalled.TrySetResult();
        await Task.Delay(Timeout.Infinite, stop.Token);
    }

    private static string? Header(string head, string name) => head.Split("\r\n")
        .FirstOrDefault(line => line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim();

    /// <summary>Latin-1, so a test body can carry any raw byte; stock bodies are ASCII.</summary>
    private Task Respond(Stream stream, int status, string body)
    {
        var bytes = Encoding.Latin1.GetBytes(body);
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
