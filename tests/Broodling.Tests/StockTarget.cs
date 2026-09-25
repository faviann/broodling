using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace Broodling.Tests;

/// <summary>
/// A loopback stand-in for the stock target on real HTTP and WebSocket connections: discovery,
/// full-run submission, session creation and an OECP WebSocket answering initialize, then
/// run/status and run/force from <see cref="Projections"/>, where a null entry never replies. Like
/// the stock target, a submission key names at most one run and a replay returns that run's ID.
/// It records each stage reached and can stall at one, or halfway through a submission body.
/// With a server certificate it serves HTTPS and WSS at <c>https://localhost:port</c> instead.
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
    /// <summary>The certificate each new TLS connection presents; replaceable, like a regenerated authority.</summary>
    internal SslStreamCertificateContext? Certificate { get; set; }

    internal StockTarget(SslStreamCertificateContext? certificate = null)
    {
        Submit = body => Task.FromResult(Accept(body));
        Certificate = certificate;
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Origin = new Uri(certificate is null ? $"http://127.0.0.1:{port}" : $"https://localhost:{port}");
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
            Stream stream = client.GetStream();
            if (Certificate is { } certificate)
            {
                var tls = new SslStream(stream);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateContext = certificate }, stop.Token);
                stream = tls;
            }
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
                var socket = Origin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
                var (status, reply) = Session ?? (200, $$"""{"endpoint":"{{socket}}://{{Origin.Authority}}/native-v2/oecp"}""");
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

/// <summary>
/// A private authority shaped like Caddy's <c>tls internal</c>: a root, an intermediate and a leaf for
/// <paramref name="Host"/>, served with its intermediate. Only <see cref="RootPem"/> is given to the client.
/// </summary>
internal sealed record PrivateAuthority(string Host, string RootPem, SslStreamCertificateContext Server)
{
    internal static PrivateAuthority Create(string host = "localhost")
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var root = Request("CN=Broodling test root", rootKey, authority: true).CreateSelfSigned(now.AddHours(-1), now.AddDays(1));
        using var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var intermediate = Issue(Request("CN=Broodling test intermediate", intermediateKey, authority: true), root, now, TimeSpan.FromHours(20))
            .CopyWithPrivateKey(intermediateKey);
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = Request("CN=" + host, leafKey, authority: false);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var leaf = Issue(leafRequest, intermediate, now, TimeSpan.FromHours(12)).CopyWithPrivateKey(leafKey);
        // A PKCS#12 round trip gives the server a persisted key, as a loaded certificate would have.
        var server = X509CertificateLoader.LoadPkcs12(leaf.Export(X509ContentType.Pkcs12), null);
        var chain = new X509Certificate2Collection(X509CertificateLoader.LoadCertificate(intermediate.RawData));
        return new(host, root.ExportCertificatePem(), SslStreamCertificateContext.Create(server, chain, offline: true));
    }

    private static CertificateRequest Request(string subject, ECDsa key, bool authority)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(authority, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(authority
            ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign : X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    /// <summary>Each issued certificate's validity nests inside its issuer's.</summary>
    private static X509Certificate2 Issue(CertificateRequest request, X509Certificate2 issuer, DateTimeOffset now, TimeSpan lifetime) =>
        request.Create(issuer, now.AddMinutes(-30), now + lifetime, RandomNumberGenerator.GetBytes(16));
}
