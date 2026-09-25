using System.Net;
using System.Security.Cryptography.X509Certificates;
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
    internal static async Task SubmitAsync(Uri origin, X509ChainPolicy? trust, string requestJson, string intendedRunId,
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
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, DirectTargetDiscovery.RunPath))
        { Content = DirectTargetExchange.JsonContent(JsonSerializer.SerializeToUtf8Bytes(request)) };

        using var budget = DirectTargetBudget.Start(DirectTargetLimits.Submit, clock, caller);
        using var handler = DirectTargetExchange.CreateHandler(trust);
        using var http = DirectTargetExchange.CreateClient(handler);
        await DirectTargetDiscovery.RequireAsync(http, origin, budget);
        var (status, reply) = await DirectTargetExchange.SendJsonAsync(http, message, budget);
        if (status == HttpStatusCode.OK)
        {
            if (!DirectTargetExchange.Shape(reply, ["runId"], [])) throw new NativeTransportError("invalid_response");
            if (DirectTargetExchange.Text(reply, "runId") != intendedRunId) throw new NativeTransportError("foreign_run");
            return;
        }
        var code = DirectTargetExchange.ProblemCode(reply) ?? throw new NativeTransportError("invalid_response");
        if (status == HttpStatusCode.Conflict && code == "request.conflict")
            throw new SubmissionConflict("The target reports a conflicting immutable submission.");
        throw new NativeTransportError("TargetError");
    }
}
