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
            || baseUrl != NativeProfile.GatewayBaseUrl)
            throw new ContractProposerError(
                "Contract proposal requires a current gateway API key and the exact supported gateway URL.", retryable: false);
        return apiKey;
    }

    public override string ToString() => nameof(GatewayCredentials);
}

/// <summary>
/// The one built-in proposer: the supported model behind the pinned OpenAI-compatible gateway,
/// called through Chat Completions. It starts from the Executable Request, the fixed authority and
/// a compact manifest, and reads frozen references on demand only through the bundle-scoped
/// <see cref="BroodlingStore.ReadRequestBundleReference"/>. Model output that is not a typed proposal
/// is an <see cref="InvalidContractProposal"/>; a gateway failure is a <see cref="ContractProposerError"/>.
/// </summary>
internal sealed class BundledProposer(BroodlingStore store, GatewayCredentials credentials, HttpMessageHandler? gateway = null)
{
    internal const string Model = "gpt-5.6-sol";
    internal const string ReadTool = "read_reference";
    internal const int MaxTurns = 8;
    internal const int ReadChunkBytes = 64 * 1024;
    internal const int ReadBudgetBytes = 512 * 1024;
    private const int ResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(3);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal const string Instructions = """
        You prepare one Contract proposal for Broodling from a frozen Executable Request. Broodling checks
        your proposal deterministically and cannot let it change the authority you are given.

        The user message is a JSON object. executableRequest is the complete request text; it alone defines
        the requested work. authority holds fixed Contract fields. manifest lists the frozen RequestBundle's
        members by referenceId with their capture kind, selector, content digest and any Git commit and
        path, but not their content.

        Available references are supporting material captured before this call. They can supply technical
        details or conformance targets but never add requested work or effects. Read one with the
        read_reference tool by its manifest referenceId. It returns the captured bytes, which never change,
        in chunks; pass offset to continue a truncated read. Read only what you need.

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
          true only when the request or references show that it already holds.
        - Omit workUnitId, sourceAttribution, requiredEffects, constructedBy and requestBundle, or copy them
          exactly from authority. Never add, remove or change an effect, attributed source or binding.
        """;

    private static readonly JsonNode ReadToolDefinition = JsonNode.Parse("""
        {"type": "function", "function": {"name": "read_reference",
          "description": "Read captured content of one frozen RequestBundle member by its manifest referenceId.",
          "parameters": {"type": "object", "additionalProperties": false, "required": ["referenceId"],
            "properties": {"referenceId": {"type": "string"},
              "offset": {"type": "integer", "minimum": 0, "description": "Byte offset to continue a truncated read."}}}}}
        """)!;

    internal async Task<Contract> ProposeAsync(ContractProposalInput input, CancellationToken cancellationToken)
    {
        var bundle = input.RequestBundle
            ?? throw new InvalidOperationException("The bundled proposer requires bundle-bound input.");
        var apiKey = credentials.ApiKey();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = Instructions },
            new JsonObject { ["role"] = "user", ["content"] = InitialContext(input).ToJsonString() }
        };
        var budget = ReadBudgetBytes;
        using var client = new HttpClient(gateway ?? new SocketsHttpHandler(), disposeHandler: gateway is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = ResponseBytes
        };
        for (var turn = 1; ; turn++)
        {
            // The last allowed call forbids tools, so the model must answer.
            var final = turn == MaxTurns;
            var (message, finish) = await CompleteAsync(client, apiKey, messages, final, cancellationToken);
            // A cut-off, filtered or otherwise incomplete reply is not the model's answer.
            if (finish == "length")
                throw new ContractProposerError("The model reply reached its output limit.", retryable: true);
            var calls = message["tool_calls"] as JsonArray;
            var calling = calls is { Count: > 0 };
            if ((message["tool_calls"] is not null && calls is null) || (calling && final)
                || (finish is not (null or "stop") && !(calling && finish == "tool_calls"))
                || (message["content"] is { } content && !Text(content, out _)))
                throw new ContractProposerError("The model gateway returned an unusable response.", retryable: true);
            if (calling)
            {
                var replies = new List<JsonNode>();
                var echoed = new JsonArray();
                foreach (var call in calls!)
                {
                    if (call is not JsonObject { } entry || !Text(entry["id"], out var id)
                        || entry["function"] is not JsonObject function || !Text(function["name"], out var name))
                        throw new ContractProposerError("The model gateway returned an unusable response.", retryable: true);
                    var arguments = Text(function["arguments"], out var given) ? given : "";
                    echoed.Add(new JsonObject
                    {
                        ["id"] = id, ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
                    });
                    var result = name == ReadTool
                        ? Read(bundle.BundleId, arguments, ref budget)
                        : new JsonObject { ["error"] = "Unknown tool; only read_reference is available." };
                    replies.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result.ToJsonString() });
                }
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = echoed });
                foreach (var reply in replies)
                    messages.Add(reply);
                continue;
            }
            return Proposal(input, message["content"]);
        }
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

    /// <summary>
    /// The request text, fixed authority and the compact manifest native agents also receive:
    /// member identities and digests, never member content.
    /// </summary>
    private JsonObject InitialContext(ContractProposalInput input) => new()
    {
        ["executableRequest"] = StrictUtf8.GetString(input.Sources.Single().Content),
        ["authority"] = Authority(input),
        ["manifest"] = store.CompactManifest(input.BundleBinding!)
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

    /// <summary>One bounded chunk of a frozen member, through the store's bundle-scoped read.</summary>
    private JsonObject Read(string bundleId, string arguments, ref int budget)
    {
        string referenceId;
        long offset;
        try
        {
            var request = Parse(arguments) as JsonObject;
            referenceId = request?["referenceId"]?.GetValueKind() == JsonValueKind.String
                ? (string)request["referenceId"]! : throw new JsonException();
            offset = request!["offset"] is { } given ? (long)given : 0;
            if (offset < 0) throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return new JsonObject { ["error"] = "Pass a manifest referenceId and an optional nonnegative offset." };
        }
        if (budget <= 0)
            return new JsonObject { ["error"] = "The read budget for this proposal is exhausted." };
        RequestBundleReferenceContent captured;
        try { captured = store.ReadRequestBundleReference(bundleId, referenceId); }
        catch (UnknownRecord)
        {
            return new JsonObject { ["error"] = "No such member in this RequestBundle." };
        }
        var content = captured.Content;
        var start = (int)Math.Min(offset, content.Length);
        var length = Math.Min(Math.Min(ReadChunkBytes, budget), content.Length - start);
        var isText = true;
        try { StrictUtf8.GetCharCount(content); }
        catch (DecoderFallbackException) { isText = false; }
        if (isText)
        {
            // Keep text chunks on character boundaries.
            while (start < content.Length && (content[start] & 0xC0) == 0x80) start++;
            length = Math.Min(length, content.Length - start);
            while (start + length < content.Length && length > 0 && (content[start + length] & 0xC0) == 0x80) length--;
        }
        budget -= length;
        return new JsonObject
        {
            ["referenceId"] = captured.ReferenceId, ["contentSha256"] = captured.ContentSha256,
            ["totalBytes"] = content.Length, ["offset"] = start, ["bytes"] = length,
            ["truncated"] = start + length < content.Length,
            ["encoding"] = isText ? "utf-8" : "base64",
            ["content"] = isText ? StrictUtf8.GetString(content, start, length) : Convert.ToBase64String(content, start, length)
        };
    }

    private static async Task<(JsonObject Message, string? Finish)> CompleteAsync(HttpClient client, string apiKey, JsonArray messages,
        bool final, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["messages"] = messages.DeepClone(),
            ["tools"] = new JsonArray(ReadToolDefinition.DeepClone()),
            ["tool_choice"] = final ? "none" : "auto",
            ["response_format"] = new JsonObject { ["type"] = "json_object" }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, NativeProfile.GatewayBaseUrl + "/chat/completions")
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
