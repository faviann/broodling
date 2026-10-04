using System.Net;
using System.Text.Json;

namespace Broodling;

/// <summary>
/// Fixed DirectTarget client limits. Budgets bound client operations and their responses,
/// never native execution; a healthy wait has no job-duration deadline.
/// </summary>
internal static class DirectTargetLimits
{
    /// <summary>Each discovery response body.</summary>
    internal const int JsonBytes = 4 * 1024 * 1024;
    internal const int JsonDepth = 64;

    internal static readonly TimeSpan Progress = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan Submit = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan Stop = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan WaitSetup = TimeSpan.FromSeconds(30);
}

/// <summary>
/// One enclosing deadline shared by every exchange or SDK call of an operation. Once it ends, none
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

/// <summary>The canonical origin rule, and readiness discovery's bounded HTTP read with fixed, secret-free failure kinds.</summary>
internal static class DirectTargetExchange
{
    private static readonly JsonDocumentOptions Json = new() { MaxDepth = DirectTargetLimits.JsonDepth, AllowDuplicateProperties = false };

    /// <summary>
    /// A canonical DirectTarget origin, mirroring the native and SDK rules: HTTPS, or HTTP to exactly
    /// 127.0.0.1 or [::1], spelled exactly as its scheme and authority (a default port omitted), with no
    /// userinfo, path, query or fragment, and a contactable port. Otherwise null.
    /// </summary>
    internal static Uri? CanonicalOrigin(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var origin) && origin.UserInfo == "" && origin.Port != 0
        && address == origin.GetLeftPart(UriPartial.Authority)
        && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp && origin.Host is "127.0.0.1" or "[::1]")
            ? origin : null;

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
        // Native advertises optional capabilities (run history, workspace recovery/checkpoints) that this
        // controller does not use; they change nothing about the run protocol it relies on.
        && (!document.TryGetProperty("extensions", out var extensions) || extensions.ValueKind == JsonValueKind.Object);
}
