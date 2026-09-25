using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using static Broodling.DirectTargetExchange;

namespace Broodling;

/// <summary>
/// A validated status projection of the expected run, never retained. A terminal result is
/// information for later consumers; reading it consumes no completion and proves no acceptance.
/// </summary>
internal sealed record DirectTargetRunStatus(NativeProgress Progress, NativeResult? Result);

/// <summary>
/// One operation-owned OECP session for one expected run. Setup and each request run inside the
/// caller's budget, so the caller owns totals and polling; nothing here retries.
/// </summary>
internal sealed class DirectTargetSession : IAsyncDisposable
{
    private const long MaxSafeInteger = 9_007_199_254_740_991;
    private static readonly string[] PassedFailures = ["force_stopped", "runtime_lost", "runtime_failed"];
    private static readonly JsonElement Null = JsonDocument.Parse("null").RootElement.Clone();
    // The pinned stock target always answers this constant (NativeV2CloudController::initialize at
    // 054ad3fd): the full graph profile, logs, agent attach and an empty controller status.
    private static readonly JsonElement StockInitialize = JsonDocument.Parse("""
        {"protocolVersion":"openengine.cluster/v1",
         "capabilities":{"graphProfiles":["openengine.graph.full/v1"],"logs":true,"agentAttach":true},
         "status":{"phase":"empty","observedGeneration":null,"currentRunId":null,"atCursor":null}}
        """).RootElement.Clone();

    private readonly SocketsHttpHandler handler = DirectTargetExchange.CreateHandler();
    private readonly ClientWebSocket socket = new();
    private readonly NativeRunBinding run;
    private readonly NativeSource source;
    private int sequence;

    private DirectTargetSession(NativeRunBinding run, NativeSource source)
    {
        this.run = run;
        this.source = source;
    }

    /// <summary>Discovery, session creation, WebSocket connection and initialization.</summary>
    internal static async Task<DirectTargetSession> OpenAsync(NativeRunBinding run, DirectTargetBudget budget)
    {
        var (origin, source) = Target(run);
        var session = new DirectTargetSession(run, source);
        try
        {
            using (var http = DirectTargetExchange.CreateClient(session.handler))
            {
                await DirectTargetDiscovery.RequireAsync(http, origin, budget);
                var endpoint = await session.CreateAsync(http, origin, budget);
                await budget.RunAsync(async token =>
                {
                    // The shared handler keeps the upgrade free of redirects, proxies and cookies.
                    using var invoker = new HttpMessageInvoker(session.handler, disposeHandler: false);
                    try { await session.socket.ConnectAsync(endpoint, invoker, token); }
                    catch (WebSocketException) { throw new NativeTransportError(); }
                    return true;
                });
            }
            var initialized = await session.CallAsync("initialize", new { protocolVersion = "openengine.cluster/v1" }, budget);
            if (!JsonElement.DeepEquals(initialized, StockInitialize))
                throw new UnsupportedRuntime("Target OECP initialization differs from the supported protocol.");
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>Read the named run's status. Each call may carry its own remaining budget.</summary>
    internal async Task<DirectTargetRunStatus> StatusAsync(DirectTargetBudget budget) =>
        Projection(await CallAsync("run/status", new { runId = run.RunId }, budget));

    /// <summary>Request native force of exactly this run; the reply is the same validated projection.</summary>
    internal async Task<DirectTargetRunStatus> ForceAsync(DirectTargetBudget budget) =>
        Projection(await CallAsync("run/force", new { runId = run.RunId }, budget));

    /// <summary>
    /// The one terminal-polling loop. Return as soon as a status carries a terminal result; otherwise
    /// pause two seconds under <paramref name="pacing"/> and read again, one read at a time. Each later
    /// read gets a fresh <paramref name="eachRead"/> budget, or shares <paramref name="pacing"/> when
    /// null. Cursors are never compared, so a repeated cursor cannot hide a newly reported result.
    /// </summary>
    internal async Task<NativeResult> TerminalAsync(DirectTargetRunStatus current, DirectTargetBudget pacing, TimeSpan? eachRead)
    {
        while (current.Result is null)
        {
            await pacing.DelayAsync(DirectTargetLimits.PollDelay);
            if (eachRead is not { } total) current = await StatusAsync(pacing);
            else
            {
                using var read = pacing.Fresh(total);
                current = await StatusAsync(read);
            }
        }
        return current.Result;
    }

    /// <summary>
    /// The one place a retained binding names its target: the direct locator's canonical origin,
    /// HTTPS or literal-loopback HTTP, plus the frozen PR source every projection must match.
    /// </summary>
    private static (Uri Origin, NativeSource Source) Target(NativeRunBinding run)
    {
        if (run.Locator.Kind == "direct" && run.Source is { } source
            && DirectTargetExchange.CanonicalOrigin(run.Locator.Address) is { } origin)
            return (origin, source);
        throw new UnsupportedRuntime("The retained DirectTarget binding is unsupported.");
    }

    /// <summary>Request a session for exactly this run; the endpoint must be the origin's own OECP route.</summary>
    private async Task<Uri> CreateAsync(HttpClient http, Uri origin, DirectTargetBudget budget)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, DirectTargetDiscovery.SessionPath))
        { Content = DirectTargetExchange.JsonContent(JsonSerializer.SerializeToUtf8Bytes(new { runId = run.RunId })) };
        var (status, body) = await DirectTargetExchange.SendJsonAsync(http, request, budget);
        if (status != HttpStatusCode.OK)
            throw new NativeTransportError(DirectTargetExchange.ProblemCode(body) is null ? "invalid_response" : "TargetError");
        var expected = (origin.Scheme == Uri.UriSchemeHttps ? "wss://" : "ws://") + origin.Authority + DirectTargetDiscovery.OecpPath;
        if (!Shape(body, ["endpoint"], ["bearerToken"]) || Text(body, "endpoint") != expected
            || body.TryGetProperty("bearerToken", out var bearer) && bearer.ValueKind != JsonValueKind.Null)
            throw Invalid();
        return new Uri(expected);
    }

    /// <summary>One outstanding JSON-RPC request; the next message must be exactly its reply.</summary>
    private async Task<JsonElement> CallAsync(string method, object parameters, DirectTargetBudget budget)
    {
        var id = (++sequence).ToString(CultureInfo.InvariantCulture);
        await DirectTargetExchange.SendMessageAsync(socket,
            JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, method, @params = parameters }), budget);
        var reply = await DirectTargetExchange.ReceiveMessageAsync(socket, budget);
        if (reply.ValueKind != JsonValueKind.Object || Text(reply, "jsonrpc") != "2.0" || Text(reply, "id") != id) throw Invalid();
        if (Shape(reply, ["jsonrpc", "id", "result"], [])) return reply.GetProperty("result");
        if (!Shape(reply, ["jsonrpc", "id", "error"], [])) throw Invalid();
        throw Failure(reply.GetProperty("error"));
    }

    /// <summary>Only fixed classifications leave; remote messages and details never do.</summary>
    private static Exception Failure(JsonElement error)
    {
        if (!Shape(error, ["code", "message"], ["data"]) || error.GetProperty("code").ValueKind != JsonValueKind.Number
            || !error.GetProperty("code").TryGetInt64(out _)
            || error.GetProperty("message").ValueKind != JsonValueKind.String) return Invalid();
        if (!error.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return new NativeTransportError("TargetError");
        if (!Shape(data, ["code"], ["details"])) return Invalid();
        return Text(data, "code") switch
        {
            "NOT_FOUND" => new NativeTransportError("RunNotFoundError"),
            "UNSUPPORTED_PROTOCOL_VERSION" => new UnsupportedRuntime("Target OECP does not support the required protocol."),
            _ => new NativeTransportError("TargetError")
        };
    }

    /// <summary>The pinned status union for exactly the expected run, title, source and size.</summary>
    private DirectTargetRunStatus Projection(JsonElement result)
    {
        if (!Shape(result, ["runId", "title", "source", "size", "atCursor", "status"], [])
            || !Shape(result.GetProperty("source"), ["repository", "branch", "revision"], [])) throw Invalid();
        var actual = result.GetProperty("source");
        _ = Text(result, "atCursor"); // Opaque: never compared, incremented or interpreted.
        if (Text(result, "runId") != run.RunId || Text(result, "title") != run.Title || Text(result, "size") != run.Size
            || Text(actual, "repository") != source.Repository || Text(actual, "branch") != source.Branch
            || Text(actual, "revision") != source.Revision)
            throw new NativeTransportError("foreign_run");
        var status = result.GetProperty("status");
        var phase = status.ValueKind == JsonValueKind.Object ? Text(status, "phase") : throw Invalid();
        switch (phase)
        {
            case "admitted" when Shape(status, ["phase"], []):
                return new(new(phase, []), null);
            case "running" or "stopping" when Shape(status, ["phase", "activeExecutions"], [])
                && status.GetProperty("activeExecutions").ValueKind == JsonValueKind.Array:
                var nodes = new List<string>();
                foreach (var execution in status.GetProperty("activeExecutions").EnumerateArray())
                {
                    if (!Shape(execution, ["execution", "node"], []) || !ExecutionRef(Text(execution, "execution"))
                        || !Identifier(Text(execution, "node"))) throw Invalid();
                    nodes.Add(Text(execution, "node"));
                }
                return new(new(phase, nodes), null);
            case "finished" when Shape(status, ["phase", "terminalResult"], ["metadata"]):
                if (status.TryGetProperty("metadata", out var metadata)) Metadata(metadata);
                return new(new(phase, []), Terminal(status.GetProperty("terminalResult")));
            default:
                throw Invalid();
        }
    }

    /// <summary>Native failure labels outside the fixed allowlist become <c>native_failed</c>.</summary>
    private NativeResult Terminal(JsonElement terminal)
    {
        if (terminal.ValueKind != JsonValueKind.Object) throw Invalid();
        switch (Text(terminal, "status"))
        {
            case "succeeded" when Shape(terminal, ["status", "output"], []):
                return new(run.RunId, true, terminal.GetProperty("output").Clone(), null);
            case "failed" when Shape(terminal, ["status", "reason"], []) && Identifier(Text(terminal, "reason")):
                var reason = Text(terminal, "reason");
                return new(run.RunId, false, Null, PassedFailures.Contains(reason) ? reason : "native_failed");
            default:
                throw Invalid();
        }
    }

    /// <summary>Validated and dropped: metadata changes no Broodling authority. Absent means empty; null refuses.</summary>
    private static void Metadata(JsonElement metadata)
    {
        if (!Shape(metadata, [], ["tokenUsage"])) throw Invalid();
        if (!metadata.TryGetProperty("tokenUsage", out var usage) || usage.ValueKind == JsonValueKind.Null) return;
        if (!Shape(usage, ["inputTokens", "outputTokens", "complete"], ["cacheReadInputTokens", "cacheCreationInputTokens"])
            || !Count(usage.GetProperty("inputTokens")) || !Count(usage.GetProperty("outputTokens"))
            || usage.GetProperty("complete").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid();
        foreach (var name in new[] { "cacheReadInputTokens", "cacheCreationInputTokens" })
            if (usage.TryGetProperty(name, out var cache) && cache.ValueKind != JsonValueKind.Null && !Count(cache)) throw Invalid();
    }

    private static bool Count(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var count) && count <= MaxSafeInteger;

    /// <summary>The native identifier rule shared by node names and enum labels.</summary>
    private static bool Identifier(string value) =>
        value.Length is > 0 and <= 128 && (char.IsAsciiLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static bool ExecutionRef(string value) =>
        value.Length > 0 && Encoding.UTF8.GetByteCount(value) <= 128 && !value.Any(char.IsControl);

    private static NativeTransportError Invalid() => new("invalid_response");

    public ValueTask DisposeAsync()
    {
        socket.Abort();
        socket.Dispose();
        handler.Dispose();
        return ValueTask.CompletedTask;
    }
}
