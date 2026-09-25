using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
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
}

/// <summary>
/// One enclosing deadline shared by every exchange of an operation. Once it ends, no exchange
/// starts; caller cancellation stays cancellation and expiry is a safe timeout.
/// </summary>
internal sealed class DirectTargetBudget : IDisposable
{
    private readonly CancellationToken caller;
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource linked;

    private DirectTargetBudget(TimeSpan total, TimeProvider clock, CancellationToken caller)
    {
        this.caller = caller;
        deadline = new CancellationTokenSource(total, clock);
        linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
    }

    internal static DirectTargetBudget Start(TimeSpan total, TimeProvider clock, CancellationToken caller) =>
        new(total, clock, caller);

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

    /// <summary>No redirects, proxy, cookies or ambient credentials; ordinary TLS verification.</summary>
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        MaxResponseHeadersLength = DirectTargetLimits.ResponseHeaderKiB
    };

    internal static HttpClient CreateClient(SocketsHttpHandler? shared = null) =>
        new(shared ?? CreateHandler(), disposeHandler: shared is null) { Timeout = Timeout.InfiniteTimeSpan };

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

    /// <summary>Complete UTF-8 JSON within the depth limit and without duplicate properties at any level.</summary>
    internal static JsonElement ParseJson(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, Json);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new NativeTransportError("invalid_response"); }
    }
}

/// <summary>The selected stock discovery document; it confirms protocol shape, not native or image identity.</summary>
internal static class DirectTargetDiscovery
{
    private static readonly Dictionary<string, string> Required = new(StringComparer.Ordinal)
    {
        ["kind"] = "zeroshot.native-v2-target/v2",
        ["authentication"] = "none",
        ["audience"] = "controller",
        ["runPath"] = "/native-v2/run",
        ["sessionPath"] = "/native-v2/oecp-session",
        ["oecpPath"] = "/native-v2/oecp"
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
        document.ValueKind == JsonValueKind.Object
        && Required.All(field => document.TryGetProperty(field.Key, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString() == field.Value)
        && document.EnumerateObject().All(property => Required.ContainsKey(property.Name)
            || NullOnly.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.Null
            || property.Name == "extensions" && property.Value.ValueKind == JsonValueKind.Object
                && !property.Value.EnumerateObject().Any());
}
