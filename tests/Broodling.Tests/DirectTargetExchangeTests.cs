using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Zeroshot.Native;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>Real loopback sockets for readiness discovery's exchange bounds; clocks are controlled.</summary>
public sealed class DirectTargetExchangeTests
{
    private const string Stock = """
        {"kind":"zeroshot.native-v2-target/v2","authentication":"private_capability","runPath":"/native-v2/run",
         "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller",
         "privateBootstrapPath":"/native-v2/private-bootstrap"}
        """;

    [Test]
    [Arguments("https://example.test", true)]
    [Arguments("https://example.test:8443", true)]
    [Arguments("https://127.0.0.1:8123", true)]
    [Arguments("http://127.0.0.1", true)]
    [Arguments("http://127.0.0.1:9", true)]
    [Arguments("http://[::1]:9", true)]
    [Arguments("http://localhost:9", false)]
    [Arguments("http://127.0.0.2:9", false)]
    [Arguments("http://[::ffff:127.0.0.1]:9", false)]
    [Arguments("http://192.0.2.1:9", false)]
    [Arguments("http://127.0.0.1:9/", false)]
    [Arguments("http://127.0.0.1:80", false)]
    [Arguments("http://127.0.0.1:0", false)]
    [Arguments("https://example.test:0", false)]
    [Arguments("https://example.test/prefix", false)]
    [Arguments("http://127.0.0.1:9?key=secret", false)]
    [Arguments("https://user@example.test", false)]
    [Arguments("http://user:secret@127.0.0.1:9", false)]
    [Arguments("HTTPS://example.test", false)]
    [Arguments("ftp://127.0.0.1:9", false)]
    public async Task OnlyCanonicalContactableHttpsOrLiteralLoopbackHttpOriginsAreSupported(string address, bool supported)
    {
        await Assert.That(DirectTargetExchange.CanonicalOrigin(address) is not null).IsEqualTo(supported);
    }

    [Test]
    public async Task StockDiscoveryIsReadWithoutAmbientCredentials()
    {
        await using var server = new StockTarget
        {
            Raw = (stream, _) => Write(stream, $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Stock.Length}\r\n\r\n{Stock}")
        };
        using var http = Client();
        using var budget = Budget();
        await DirectTargetDiscovery.RequireAsync(http, server.Origin, budget);
        var head = server.Heads.Single();
        await Assert.That(head).StartsWith("GET /.well-known/zeroshot-native-v2 HTTP/1.1\r\n");
        await Assert.That(head.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Cookie", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    [Arguments(DirectTargetLimits.JsonBytes, null)]
    [Arguments(DirectTargetLimits.JsonBytes + 1, "invalid_response")]
    public async Task ChunkedBodyWithoutDeclaredLengthIsCountedTo4MiB(int length, string? kind)
    {
        var body = Encoding.UTF8.GetBytes("\"" + new string('a', length - 2) + "\"");
        await using var server = new StockTarget
        {
            Raw = async (stream, _) =>
            {
                await Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
                foreach (var chunk in body.Chunk(64 * 1024 + 3))
                {
                    await Write(stream, $"{chunk.Length:x}\r\n");
                    await stream.WriteAsync(chunk);
                    await Write(stream, "\r\n");
                }
                await Write(stream, "0\r\n\r\n");
            }
        };
        await Exchanges(server, kind);
    }

    [Test]
    public async Task TruncatedBodyIsTransportLoss()
    {
        await using var server = new StockTarget { Raw = (stream, _) => Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n{\"kind\"") };
        await Exchanges(server, "transport_failed");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StalledReadEndsByDeadlineOrCallerAndNoLaterExchangeStarts(bool callerCancels)
    {
        var responded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new StockTarget
        {
            Raw = async (stream, stop) =>
            {
                await Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\n{");
                responded.TrySetResult();
                await Task.Delay(Timeout.Infinite, stop);
            }
        };
        using var http = Client();
        using var caller = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Progress, clock, caller.Token);
        var exchange = Send(http, server.Origin, budget);
        await responded.Task;
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
    public async Task DuplicatePropertiesAreRefused()
    {
        await Fails(() => Task.FromResult(DirectTargetExchange.ParseJson(Encoding.UTF8.GetBytes(
            """{"status":{"phase":"running","phase":"finished"}}"""))), "invalid_response");
    }

    private static DirectTargetBudget Budget() => DirectTargetBudget.Start(DirectTargetLimits.Progress, new FakeTimeProvider(), default);

    private static Task<(HttpStatusCode Status, System.Text.Json.JsonElement Body)> Send(HttpClient http, Uri origin, DirectTargetBudget budget) =>
        DirectTargetExchange.SendJsonAsync(http, new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/exchange")), budget);

    private static async Task Exchanges(StockTarget server, string? kind)
    {
        using var http = Client();
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

    /// <summary>The SDK's handler, as readiness discovery uses it.</summary>
    private static HttpClient Client() => new(NativeClient.CreateHttpHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    private static Task Write(Stream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();
}
