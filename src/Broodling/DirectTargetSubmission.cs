using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The stock full-run submission. It owns framing and acknowledgement classification only; durable
/// intent, correlation and conflict retention belong to the application.
/// </summary>
internal static class DirectTargetSubmission
{
    /// <summary>
    /// Discovery and one <c>POST /native-v2/run</c> within the 60-second submit budget. Returns only
    /// on HTTP 200 carrying exactly the intended run ID; no other identity is ever returned or adopted.
    /// A valid stock <c>request.conflict</c> is <see cref="SubmissionConflict"/> without a run ID.
    /// </summary>
    internal static async Task SubmitAsync(Uri origin, string requestJson, string intendedRunId,
        IReadOnlyDictionary<string, string> credentials, TimeProvider clock, CancellationToken caller)
    {
        // Current credentials enter only this in-memory body, never the frozen request or a diagnostic.
        var request = JsonNode.Parse(requestJson)!.AsObject();
        request["connections"] = new JsonObject
        {
            ["gateway"] = new JsonObject
            {
                ["GATEWAY_BASE_URL"] = credentials["GATEWAY_BASE_URL"], ["GATEWAY_API_KEY"] = credentials["GATEWAY_API_KEY"]
            },
            ["github"] = new JsonObject { ["GH_TOKEN"] = credentials["GH_TOKEN"] }
        };
        request["githubToken"] = credentials["GH_TOKEN"];
        // The final credential-bearing size is checked before any exchange.
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "/native-v2/run"))
        { Content = DirectTargetExchange.JsonContent(JsonSerializer.SerializeToUtf8Bytes(request)) };

        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Submit, clock, caller);
        using var http = DirectTargetExchange.CreateClient();
        await DirectTargetDiscovery.RequireAsync(http, origin, budget);
        var (status, reply) = await DirectTargetExchange.SendJsonAsync(http, message, budget);
        if (status == HttpStatusCode.OK)
        {
            if (reply.ValueKind != JsonValueKind.Object || reply.EnumerateObject().Count() != 1
                || !reply.TryGetProperty("runId", out var runId) || runId.ValueKind != JsonValueKind.String)
                throw new NativeTransportError("invalid_response");
            if (runId.GetString() != intendedRunId) throw new NativeTransportError("foreign_run");
            return;
        }
        var code = DirectTargetExchange.ProblemCode(reply) ?? throw new NativeTransportError("invalid_response");
        if (status == HttpStatusCode.Conflict && code == "request.conflict")
            throw new SubmissionConflict("The target reports a conflicting immutable submission.");
        throw new NativeTransportError("TargetError");
    }
}
