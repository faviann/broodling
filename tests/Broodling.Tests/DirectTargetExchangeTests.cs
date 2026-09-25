using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>Real loopback sockets for the shared DirectTarget exchange bounds; clocks are controlled.</summary>
public sealed class DirectTargetExchangeTests
{
    private const string Stock = """
        {"kind":"zeroshot.native-v2-target/v2","authentication":"none","runPath":"/native-v2/run",
         "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller"}
        """;

    [Test]
    public async Task StockDiscoveryIsReadAcrossSmallChunksWithoutAmbientCredentials()
    {
        await using var server = new RawServer(async stream =>
        {
            await Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n");
            foreach (var chunk in Stock.Chunk(7)) await Write(stream, $"{chunk.Length:x}\r\n{new string(chunk)}\r\n");
            await Write(stream, "0\r\n\r\n");
        });
        using var http = DirectTargetExchange.CreateClient();
        using var budget = Budget();
        await DirectTargetDiscovery.RequireAsync(http, server.Origin, budget);
        var head = server.Requests.Single();
        await Assert.That(head).StartsWith("GET /.well-known/zeroshot-native-v2 HTTP/1.1\r\n");
        await Assert.That(head.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Cookie", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task RedirectIsNotFollowed()
    {
        await using var server = new RawServer(stream =>
            Write(stream, "HTTP/1.1 302 Found\r\nConnection: close\r\nLocation: /.well-known/zeroshot-native-v2\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}"));
        using var http = DirectTargetExchange.CreateClient();
        using var budget = Budget();
        await Assert.That(() => DirectTargetDiscovery.RequireAsync(http, server.Origin, budget)).Throws<UnsupportedRuntime>();
        await Assert.That(server.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(30, true)]
    [Arguments(33, false)]
    public async Task ResponseHeadersAreLimitedTo32KiB(int kib, bool accepted)
    {
        await using var server = new RawServer(stream =>
            Write(stream, $"HTTP/1.1 200 OK\r\nX-Padding: {new string('a', kib * 1024)}\r\nContent-Length: 2\r\n\r\n{{}}"));
        await Exchanges(server, accepted ? null : "invalid_response");
    }

    [Test]
    [Arguments(DirectTargetLimits.JsonBytes, null)]
    [Arguments(DirectTargetLimits.JsonBytes + 1, "invalid_response")]
    public async Task ChunkedBodyWithoutDeclaredLengthIsCountedTo4MiB(int length, string? kind)
    {
        var body = Encoding.UTF8.GetBytes("\"" + new string('a', length - 2) + "\"");
        await using var server = new RawServer(async stream =>
        {
            await Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
            foreach (var chunk in body.Chunk(64 * 1024 + 3))
            {
                await Write(stream, $"{chunk.Length:x}\r\n");
                await stream.WriteAsync(chunk);
                await Write(stream, "\r\n");
            }
            await Write(stream, "0\r\n\r\n");
        });
        await Exchanges(server, kind);
    }

    [Test]
    public async Task TruncatedBodyIsTransportLoss()
    {
        await using var server = new RawServer(stream => Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n{\"kind\""));
        await Exchanges(server, "transport_failed");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StalledReadEndsByDeadlineOrCallerAndNoLaterExchangeStarts(bool callerCancels)
    {
        await using var server = new RawServer(async (stream, stop) =>
        {
            await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n{");
            await Task.Delay(Timeout.Infinite, stop);
        });
        using var http = DirectTargetExchange.CreateClient();
        using var caller = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, clock, caller.Token);
        var exchange = Send(http, server.Origin, budget);
        await server.Responded.Task;
        clock.Advance(DirectTargetLimits.Progress - TimeSpan.FromMilliseconds(1));
        await Assert.That(exchange.IsCompleted).IsFalse();
        if (callerCancels)
        {
            caller.Cancel();
            await Assert.That(async () => await exchange).Throws<OperationCanceledException>();
        }
        else
        {
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Fails(() => exchange, "TimeoutError");
            await Fails(() => Send(http, server.Origin, budget), "TimeoutError");
        }
        await Assert.That(server.Connections).IsEqualTo(1);
    }

    [Test]
    public async Task JsonDepthAndDuplicatePropertiesAreRefused()
    {
        await Fails(() => Task.FromResult(DirectTargetExchange.ParseJson(Encoding.UTF8.GetBytes(
            new string('[', 65) + new string(']', 65)))), "invalid_response");
        await Fails(() => Task.FromResult(DirectTargetExchange.ParseJson(Encoding.UTF8.GetBytes(
            """{"status":{"phase":"running","phase":"finished"}}"""))), "invalid_response");
    }

    [Test]
    public async Task OutgoingBodiesAreRefusedBeforeSendingWhenOverLimit()
    {
        await Fails(() => Task.FromResult(DirectTargetExchange.JsonContent(new byte[DirectTargetLimits.JsonBytes + 1])), "request_too_large");
        using var content = DirectTargetExchange.JsonContent(new byte[DirectTargetLimits.JsonBytes]);
        await Assert.That(content.Headers.ContentLength).IsEqualTo(DirectTargetLimits.JsonBytes);

        await using var pair = await SocketPair.Open();
        using var budget = Budget();
        await Fails(() => DirectTargetExchange.SendMessageAsync(pair.Client, new byte[DirectTargetLimits.OutgoingMessageBytes + 1], budget),
            "request_too_large");
        var largest = Encoding.UTF8.GetBytes("\"" + new string('a', DirectTargetLimits.OutgoingMessageBytes - 2) + "\"");
        await DirectTargetExchange.SendMessageAsync(pair.Client, largest, budget);
        await Assert.That((await DirectTargetExchange.ReceiveMessageAsync(pair.Server, budget)).GetString()!.Length)
            .IsEqualTo(DirectTargetLimits.OutgoingMessageBytes - 2);
    }

    [Test]
    [Arguments(DirectTargetLimits.JsonBytes, null)]
    [Arguments(DirectTargetLimits.JsonBytes + 1, "invalid_response")]
    public async Task FragmentedIncomingMessageIsAssembledTo4MiB(int length, string? kind)
    {
        await using var pair = await SocketPair.Open();
        var message = Encoding.UTF8.GetBytes("\"" + new string('a', length - 2) + "\"");
        var third = message.Length / 3;
        _ = Task.Run(async () =>
        {
            await pair.Server.SendAsync(message.AsMemory(0, third), WebSocketMessageType.Text, false, default);
            await pair.Server.SendAsync(message.AsMemory(third, third), WebSocketMessageType.Text, false, default);
            await pair.Server.SendAsync(message.AsMemory(2 * third), WebSocketMessageType.Text, true, default);
        });
        using var budget = Budget();
        if (kind is null)
            await Assert.That((await DirectTargetExchange.ReceiveMessageAsync(pair.Client, budget)).GetString()!.Length).IsEqualTo(length - 2);
        else
            await Fails(() => DirectTargetExchange.ReceiveMessageAsync(pair.Client, budget), kind);
    }

    [Test]
    [Arguments("binary", "invalid_response")]
    [Arguments("close", "transport_failed")]
    public async Task BinaryMessagesAndClosureAreNotResponses(string change, string kind)
    {
        await using var pair = await SocketPair.Open();
        if (change == "binary") await pair.Server.SendAsync("{}"u8.ToArray(), WebSocketMessageType.Binary, true, default);
        else await pair.Server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default);
        using var budget = Budget();
        await Fails(() => DirectTargetExchange.ReceiveMessageAsync(pair.Client, budget), kind);
    }

    private static DirectTargetBudget Budget() => DirectTargetBudget.Start(DirectTargetLimits.Progress, new FakeTimeProvider(), default);

    private static Task<(HttpStatusCode Status, System.Text.Json.JsonElement Body)> Send(HttpClient http, Uri origin, DirectTargetBudget budget) =>
        DirectTargetExchange.SendJsonAsync(http, new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/exchange")), budget);

    private static async Task Exchanges(RawServer server, string? kind)
    {
        using var http = DirectTargetExchange.CreateClient();
        using var budget = Budget();
        if (kind is null) await Assert.That((await Send(http, server.Origin, budget)).Status).IsEqualTo(HttpStatusCode.OK);
        else await Fails(() => Send(http, server.Origin, budget), kind);
    }

    private static async Task Fails<T>(Func<Task<T>> action, string kind) => await Fails(async () => { await action(); }, kind);

    private static async Task Fails(Func<Task> action, string kind)
    {
        try { await action(); }
        catch (NativeTransportError error)
        {
            await Assert.That(error.Kind).IsEqualTo(kind);
            return;
        }
        throw new InvalidOperationException("Expected native transport error " + kind);
    }

    private static Task Write(Stream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    /// <summary>A loopback HTTP/1.1 peer that writes scripted raw bytes as the response on each connection.</summary>
    private sealed class RawServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task accepting;
        private int connections;
        internal Uri Origin { get; }
        internal int Connections => Volatile.Read(ref connections);
        internal List<string> Requests { get; } = [];
        internal TaskCompletionSource Responded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal RawServer(Func<Stream, CancellationToken, Task> respond)
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
                        handlers.Add(Handle(client, respond));
                    }
                }
                catch (OperationCanceledException) { }
                await Task.WhenAll(handlers);
            });
        }

        internal RawServer(Func<Stream, Task> respond) : this((stream, _) => respond(stream)) { }

        private async Task Handle(TcpClient client, Func<Stream, CancellationToken, Task> respond)
        {
            using var _ = client;
            try
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var octet = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(octet, stop.Token) == 1)
                    head.Append((char)octet[0]);
                lock (Requests) Requests.Add(head.ToString());
                var response = respond(stream, stop.Token);
                Responded.TrySetResult();
                await response;
            }
            catch (Exception) { } // The client may abandon a response; the test asserts what the client observed.
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            await accepting;
            stop.Dispose();
        }
    }

    /// <summary>Two WebSocket endpoints over a real loopback TCP connection; no HTTP upgrade is under test.</summary>
    private sealed class SocketPair : IAsyncDisposable
    {
        private readonly TcpClient clientTcp;
        private readonly TcpClient serverTcp;
        internal WebSocket Client { get; }
        internal WebSocket Server { get; }

        private SocketPair(TcpClient clientTcp, TcpClient serverTcp)
        {
            this.clientTcp = clientTcp;
            this.serverTcp = serverTcp;
            Client = WebSocket.CreateFromStream(clientTcp.GetStream(), new WebSocketCreationOptions { IsServer = false });
            Server = WebSocket.CreateFromStream(serverTcp.GetStream(), new WebSocketCreationOptions { IsServer = true });
        }

        internal static async Task<SocketPair> Open()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var client = new TcpClient();
                var accepted = listener.AcceptTcpClientAsync();
                await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                return new SocketPair(client, await accepted);
            }
            finally { listener.Stop(); }
        }

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            Server.Dispose();
            clientTcp.Dispose();
            serverTcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
