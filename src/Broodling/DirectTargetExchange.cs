using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Broodling;

/// <summary>
/// Fixed DirectTarget client limits. Budgets bound client operations and their responses,
/// never native execution; a healthy wait has no job-duration deadline.
/// </summary>
internal static class DirectTargetLimits
{
    /// <summary>Each HTTP JSON body, in either direction, and each assembled incoming WebSocket message.</summary>
    internal const int JsonBytes = 4 * 1024 * 1024;
    /// <summary>Each outgoing WebSocket message, within the native receive limit.</summary>
    internal const int OutgoingMessageBytes = 1024 * 1024;
    internal const int ResponseHeaderKiB = 32;
    internal const int JsonDepth = 64;

    internal static readonly TimeSpan Progress = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan Submit = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan Stop = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan WaitSetup = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan WaitRead = TimeSpan.FromSeconds(10);
    /// <summary>The pause after each nonterminal status before the next read; waiting, not retry.</summary>
    internal static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
}

/// <summary>
/// One enclosing deadline shared by every exchange of an operation. Once it ends, no exchange
/// starts; caller cancellation stays cancellation and expiry is a safe timeout.
/// </summary>
internal sealed class DirectTargetBudget : IDisposable
{
    private readonly TimeProvider clock;
    private readonly CancellationToken caller;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource linked;

    private DirectTargetBudget(TimeSpan total, TimeProvider clock, CancellationToken caller)
    {
        this.clock = clock;
        this.caller = caller;
        deadline = new CancellationTokenSource(total, clock);
        linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
    }

    internal static DirectTargetBudget Start(TimeSpan total, TimeProvider clock, CancellationToken caller) =>
        new(total, clock, caller);

    /// <summary>A new budget with the same clock and caller, for an exchange with its own total.</summary>
    internal DirectTargetBudget Fresh(TimeSpan total) => new(total, clock, caller);

    /// <summary>A pause that the caller can cancel and that never outlasts this budget.</summary>
    internal Task DelayAsync(TimeSpan delay) => RunAsync(async token =>
    {
        await Task.Delay(delay, clock, token);
        return true;
    });

    internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> exchange)
    {
        caller.ThrowIfCancellationRequested();
        if (deadline.IsCancellationRequested) throw new NativeTransportError("TimeoutError");
        try { return await exchange(linked.Token); }
        catch (Exception) when (caller.IsCancellationRequested) { throw new OperationCanceledException(caller); }
        catch (Exception) when (deadline.IsCancellationRequested) { throw new NativeTransportError("TimeoutError"); }
    }

    public void Dispose()
    {
        linked.Dispose();
        deadline.Dispose();
    }
}

/// <summary>Bounded HTTP/WebSocket reads and writes with fixed, secret-free failure kinds.</summary>
internal static class DirectTargetExchange
{
    private static readonly JsonDocumentOptions Json = new() { MaxDepth = DirectTargetLimits.JsonDepth, AllowDuplicateProperties = false };

    /// <summary>No redirects, proxy, cookies or ambient credentials. TLS is always verified, by system trust unless <paramref name="trust"/> replaces it.</summary>
    internal static SocketsHttpHandler CreateHandler(X509ChainPolicy? trust = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            MaxResponseHeadersLength = DirectTargetLimits.ResponseHeaderKiB
        };
        if (trust is not null) handler.SslOptions.CertificateChainPolicy = trust;
        return handler;
    }

    /// <summary>
    /// For an HTTPS <paramref name="origin"/> with a <paramref name="rootCertificate"/>, custom root trust in
    /// exactly that PEM root: the system store plays no part, and revocation is unchecked as for ordinary
    /// TLS. Otherwise null, meaning system trust. Each operation reads the root once, before it contacts
    /// the target; a missing or unreadable root fails that operation as a transport failure.
    /// </summary>
    internal static X509ChainPolicy? Trust(Uri origin, string? rootCertificate)
    {
        if (rootCertificate is null || origin.Scheme != Uri.UriSchemeHttps) return null;
        X509Certificate2 root;
        try { root = X509Certificate2.CreateFromPem(File.ReadAllText(rootCertificate)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        { throw new NativeTransportError(); }
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
        policy.CustomTrustStore.Add(root);
        return policy;
    }

    internal static HttpClient CreateClient(SocketsHttpHandler? shared = null) =>
        new(shared ?? CreateHandler(), disposeHandler: shared is null) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// A canonical DirectTarget origin, mirroring the native rule: HTTPS, or literal-loopback HTTP,
    /// spelled exactly as its scheme and authority (a default port omitted), with no userinfo, path,
    /// query or fragment, and a contactable port. Otherwise null.
    /// </summary>
    internal static Uri? CanonicalOrigin(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var origin) && origin.UserInfo == "" && origin.Port != 0
        && address == origin.GetLeftPart(UriPartial.Authority)
        && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp
            && IPAddress.TryParse(origin.DnsSafeHost, out var host) && IPAddress.IsLoopback(host))
            ? origin : null;

    /// <summary>A JSON request body with a known Content-Length.</summary>
    internal static HttpContent JsonContent(byte[] body)
    {
        if (body.Length > DirectTargetLimits.JsonBytes) throw new NativeTransportError("request_too_large");
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    internal static Task<(HttpStatusCode Status, JsonElement Body)> SendJsonAsync(HttpClient http, HttpRequestMessage request,
        DirectTargetBudget budget) => budget.RunAsync(async token =>
    {
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var body = new MemoryStream();
            var block = new byte[16 * 1024];
            int count;
            // Count the bytes actually received, whatever the framing or declared length.
            while ((count = await stream.ReadAsync(block, token)) != 0)
            {
                if (body.Length + count > DirectTargetLimits.JsonBytes) throw new NativeTransportError("invalid_response");
                body.Write(block, 0, count);
            }
            return (response.StatusCode, ParseJson(body.GetBuffer().AsMemory(0, (int)body.Length)));
        }
        catch (HttpRequestException error) when (error.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        { throw new NativeTransportError("invalid_response"); }
        catch (Exception error) when (error is HttpRequestException or IOException)
        { throw new NativeTransportError(); }
    });

    internal static Task<JsonElement> ReceiveMessageAsync(WebSocket socket, DirectTargetBudget budget) => budget.RunAsync(async token =>
    {
        try
        {
            using var message = new MemoryStream();
            var block = new byte[16 * 1024];
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(block.AsMemory(), token);
                if (received.MessageType == WebSocketMessageType.Close) throw new NativeTransportError();
                if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > DirectTargetLimits.JsonBytes)
                    throw new NativeTransportError("invalid_response");
                message.Write(block, 0, received.Count);
            } while (!received.EndOfMessage);
            return ParseJson(message.GetBuffer().AsMemory(0, (int)message.Length));
        }
        catch (WebSocketException) { throw new NativeTransportError(); }
    });

    internal static Task SendMessageAsync(WebSocket socket, ReadOnlyMemory<byte> message, DirectTargetBudget budget)
    {
        if (message.Length > DirectTargetLimits.OutgoingMessageBytes) throw new NativeTransportError("request_too_large");
        return budget.RunAsync(async token =>
        {
            try { await socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, token); }
            catch (WebSocketException) { throw new NativeTransportError(); }
            return true;
        });
    }

    /// <summary>
    /// The stock HTTP problem <c>{code, message, details?}</c>: its code when the shape is valid, otherwise null.
    /// The message and details are never read.
    /// </summary>
    internal static string? ProblemCode(JsonElement body) =>
        Shape(body, ["code", "message"], ["details"]) && body.GetProperty("message").ValueKind == JsonValueKind.String
        && body.GetProperty("code") is { ValueKind: JsonValueKind.String } code ? code.GetString() : null;

    /// <summary>
    /// Complete JSON within the depth limit and without duplicate properties at any level. Every string
    /// and property name must decode: the parser leaves their contents unchecked, so malformed UTF-8 or
    /// a lone escaped surrogate would otherwise surface later, as a non-transport failure, when read.
    /// </summary>
    internal static JsonElement ParseJson(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, Json);
            var reader = new Utf8JsonReader(bytes.Span);
            while (reader.Read())
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName) reader.GetString();
            return document.RootElement.Clone();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { throw new NativeTransportError("invalid_response"); }
    }

    /// <summary>An object with every required property and nothing unknown.</summary>
    internal static bool Shape(JsonElement value, string[] required, string[] optional) =>
        value.ValueKind == JsonValueKind.Object && required.All(name => value.TryGetProperty(name, out _))
        && value.EnumerateObject().All(property => required.Contains(property.Name) || optional.Contains(property.Name));

    /// <summary>A string property's value; anything else is an invalid response.</summary>
    internal static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()! : throw new NativeTransportError("invalid_response");
}

/// <summary>The selected stock discovery document; it confirms protocol shape, not native or image identity.</summary>
internal static class DirectTargetDiscovery
{
    internal const string Kind = "zeroshot.native-v2-target/v2";
    internal const string RunPath = "/native-v2/run";
    internal const string SessionPath = "/native-v2/oecp-session";
    internal const string OecpPath = "/native-v2/oecp";

    private static readonly Dictionary<string, string> Required = new(StringComparer.Ordinal)
    {
        ["kind"] = Kind, ["authentication"] = "none", ["audience"] = "controller",
        ["runPath"] = RunPath, ["sessionPath"] = SessionPath, ["oecpPath"] = OecpPath
    };
    private static readonly string[] NullOnly = ["privateBootstrapPath", "oauth", "loginSession"];

    internal static async Task RequireAsync(HttpClient http, Uri origin, DirectTargetBudget budget)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/.well-known/zeroshot-native-v2"));
        var (status, document) = await DirectTargetExchange.SendJsonAsync(http, request, budget);
        if (status != HttpStatusCode.OK || !Stock(document))
            throw new UnsupportedRuntime("Target discovery differs from the supported DirectTarget protocol.");
    }

    private static bool Stock(JsonElement document) =>
        DirectTargetExchange.Shape(document, [.. Required.Keys], [.. NullOnly, "extensions"])
        && Required.All(field => document.GetProperty(field.Key) is { ValueKind: JsonValueKind.String } value && value.GetString() == field.Value)
        && NullOnly.All(name => !document.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        && (!document.TryGetProperty("extensions", out var extensions) || DirectTargetExchange.Shape(extensions, [], []));
}
