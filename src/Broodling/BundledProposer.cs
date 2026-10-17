using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// Current gateway credentials for the bundled proposer. They are read when a proposal is made
/// and are never part of a proposal input, retained record or diagnostic.
/// </summary>
public sealed class GatewayCredentials(string? baseUrl, string? apiKey)
{
    public static GatewayCredentials FromEnvironment() => new(Environment.GetEnvironmentVariable("GATEWAY_BASE_URL"),
        Environment.GetEnvironmentVariable("GATEWAY_API_KEY"));

    internal string ApiKey()
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(char.IsControl)
            || baseUrl != DirectTargetBinding.GatewayBaseUrl)
            throw new ContractProposerError(
                "Contract proposal requires a current gateway API key and the exact supported gateway URL.", retryable: false);
        return apiKey;
    }

    public override string ToString() => nameof(GatewayCredentials);
}

/// <summary>
/// The one built-in proposer: the supported model behind the pinned gateway, called once through its
/// OpenAI-compatible Chat Completions endpoint with the Executable Request and the fixed authority.
/// Model output that is not a typed proposal is an <see cref="InvalidContractProposal"/>; a gateway
/// failure is a <see cref="ContractProposerError"/>.
/// </summary>
internal sealed class BundledProposer(GatewayCredentials credentials, HttpMessageHandler? gateway = null)
{
    internal const string Model = "claude-opus-5-5";
    // The gateway otherwise caps a turn at 8192 tokens, adaptive thinking included. A larger cap than the model can
    // generate within TurnTimeout only turns a length failure into a timeout the gateway still bills in full.
    internal const int MaxOutputTokens = 12000;
    private const int ResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(3);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal const string Instructions = """
        You prepare one Contract proposal for Broodling from a frozen Executable Request. Broodling checks
        your proposal deterministically and cannot let it change the authority you are given.

        The user message is a JSON object. executableRequest is the complete request text; it alone defines
        the requested work. authority holds fixed Contract fields.

        Reply with exactly one JSON object and nothing else, in this shape:
        {"criteria": [{"criterionId": "...", "statement": "..."}],
         "obligations": [{"obligationId": "...", "statement": "...", "kind": "..."}],
         "prerequisites": [{"prerequisiteId": "...", "statement": "...", "satisfiedWithinProfile": false}],
         "notes": ""}
        A criterion may also carry validationSeam, validationAction and falsifyingObservation text.
        Identities are unique short slugs.

        Rules:
        - Preserve every requirement of the Executable Request. Never drop, soften or reinterpret one because
          it cannot be done here; Broodling refuses unsupported requirements and reports them.
        - Record each requested action beyond the one authorized pull request as an obligation of kind merge,
          deployment, publication, issue_mutation, push or external_effect. Use kind candidate_change or
          local_validation only for the code change and local checks.
        - Record anything that must happen or hold first as a prerequisite. Set satisfiedWithinProfile to
          true only when the request shows that it already holds.
        - Omit workUnitId, sourceAttribution, requiredEffects, constructedBy and requestBundle, or copy them
          exactly from authority. Never add, remove or change an effect, attributed source or binding.
        """;

    internal async Task<Contract> ProposeAsync(ContractProposalInput input, CancellationToken cancellationToken)
    {
        var apiKey = credentials.ApiKey();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = Instructions },
            new JsonObject { ["role"] = "user", ["content"] = InitialContext(input).ToJsonString() }
        };
        using var client = new HttpClient(gateway ?? new SocketsHttpHandler(), disposeHandler: gateway is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = ResponseBytes
        };
        var (message, finish) = await CompleteAsync(client, apiKey, messages, cancellationToken);
        // A cut-off, filtered or otherwise incomplete reply is not the model's answer.
        if (finish == "length")
            throw new ContractProposerError("The model reply reached its output limit.", retryable: true);
        if (finish is not (null or "stop") || message["tool_calls"] is not null
            || (message["content"] is { } content && !Text(content, out _)))
            throw new ContractProposerError("The model gateway returned an unusable response.", retryable: true);
        return Proposal(input, message["content"]);
    }

    private static bool Text(JsonNode? node, out string value)
    {
        value = "";
        return node is JsonValue text && text.TryGetValue(out value!);
    }

    /// <summary>Model and gateway JSON is parsed eagerly; a repeated property is malformed, not resolved.</summary>
    private static JsonNode? Parse(string text) => JsonNode.Parse(text, documentOptions: Strict);

    private static JsonNode? Parse(byte[] bytes) => JsonNode.Parse(bytes, documentOptions: Strict);

    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };

    private static JsonObject InitialContext(ContractProposalInput input) => new()
    {
        ["executableRequest"] = StrictUtf8.GetString(input.Sources.Single().Content),
        ["authority"] = Authority(input)
    };

    private static JsonObject Authority(ContractProposalInput input) => new()
    {
        ["workUnitId"] = input.WorkUnit.WorkUnitId,
        ["sourceAttribution"] = Contract.ToProposalNode(input.SourceAttribution),
        ["requiredEffects"] = Contract.ToProposalNode(input.RequiredEffects),
        ["constructedBy"] = input.ConstructedBy,
        ["requestBundle"] = Contract.ToProposalNode(input.BundleBinding)
    };

    /// <summary>
    /// Read the model's final JSON as a typed Contract. An omitted authority field takes its fixed
    /// value; a supplied one is kept as proposed, so admission refuses any change instead of correcting it.
    /// </summary>
    private static Contract Proposal(ContractProposalInput input, JsonNode? content)
    {
        if (!Text(content, out var text))
            throw new InvalidContractProposal("The model replied without a proposal.");
        JsonObject proposal;
        try
        {
            proposal = Parse(text) as JsonObject
                ?? throw new InvalidContractProposal("The model's proposal is not a JSON object.");
        }
        catch (JsonException)
        {
            throw new InvalidContractProposal("The model's proposal is not well-formed JSON without repeated properties.");
        }
        foreach (var (name, value) in Authority(input))
            if (!proposal.ContainsKey(name))
                proposal[name] = value?.DeepClone();
        return Contract.FromProposal(proposal);
    }

    private static async Task<(JsonObject Message, string? Finish)> CompleteAsync(HttpClient client, string apiKey, JsonArray messages,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = MaxOutputTokens,
            ["reasoning_effort"] = "medium",
            ["messages"] = messages,
            ["response_format"] = new JsonObject { ["type"] = "json_object" }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, DirectTargetBinding.GatewayBaseUrl + "/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turn.CancelAfter(TurnTimeout);
        HttpResponseMessage response;
        byte[] bytes;
        try
        {
            response = await client.SendAsync(request, turn.Token);
            bytes = await response.Content.ReadAsByteArrayAsync(turn.Token);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // Never echo transport text: it can carry the request's endpoint details.
            throw new ContractProposerError("The model gateway did not return a response.", retryable: true);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new ContractProposerError($"The model gateway refused the proposal request with HTTP {status}.",
                    retryable: response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || status >= 500);
            }
        }
        JsonNode? root;
        try { root = Parse(bytes); }
        catch (JsonException) { root = null; }
        if (root is not JsonObject envelope || envelope["choices"] is not JsonArray { Count: > 0 } choices
            || choices[0] is not JsonObject choice || choice["message"] is not JsonObject message
            || (choice["finish_reason"] is { } reason && !Text(reason, out _)))
            throw new ContractProposerError("The model gateway returned an unusable response.", retryable: true);
        // An absent finish reason is read as "stop".
        return (message, Text(choice["finish_reason"], out var finish) ? finish : null);
    }
}
