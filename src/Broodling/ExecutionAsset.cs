using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The release-bundled, approved DirectTarget graph/runtime. Identity is SHA-256 of the exact file bytes;
/// C# transports the values opaquely and never expands, edits or regenerates them. The asset and the
/// native release its manifest must name are the <see cref="DirectTargetBinding"/>.
/// </summary>
internal sealed class ExecutionAsset
{
    private readonly byte[] content;

    private ExecutionAsset(byte[] content) => this.content = content;

    internal string Sha256 => DirectTargetBinding.AssetSha256;
    internal byte[] Content() => content.ToArray();
    internal JsonNode Graph() => JsonNode.Parse(content)!["graph"]!.DeepClone();
    internal JsonNode Runtime() => JsonNode.Parse(content)!["runtime"]!.DeepClone();

    /// <summary>Retained content, never today's installed file. Only the approved identity is supported.</summary>
    internal static ExecutionAsset? FromRetained(byte[]? bytes) =>
        bytes is not null && Digests.Bytes(bytes) == DirectTargetBinding.AssetSha256 ? new(bytes) : null;

    internal static ExecutionAsset LoadBundled() => Load(Path.Combine(AppContext.BaseDirectory, "execution-assets"));

    internal static ExecutionAsset Load(string directory)
    {
        byte[] bytes;
        JsonNode? manifest;
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(directory, DirectTargetBinding.AssetFile));
            manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "approval.json")));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new UnsupportedRuntime("The approved execution asset and manifest are required.");
        }
        if (Digests.Bytes(bytes) != DirectTargetBinding.AssetSha256)
            throw new UnsupportedRuntime("The execution asset is not the approved identity.");
        if ((string?)manifest?["kind"] != "broodling.execution-asset-approval/v1"
            || !JsonNode.DeepEquals(manifest["asset"], new JsonObject { ["file"] = DirectTargetBinding.AssetFile, ["bytes"] = bytes.Length, ["sha256"] = DirectTargetBinding.AssetSha256 })
            || !JsonNode.DeepEquals(manifest["native"], Approval["native"])
            || !JsonNode.DeepEquals(manifest["policy"], Approval["policy"]))
            throw new UnsupportedRuntime("The execution asset approval is bound to a different release or policy.");
        return new(bytes);
    }

    internal static readonly JsonObject Approval = new()
    {
        ["native"] = DirectTargetBinding.Native(),
        ["policy"] = new JsonObject
        {
            ["workflow"] = "software-change", ["delivery"] = "pull_request", ["harness"] = "codex", ["provider"] = "gateway",
            ["model"] = "gpt-5.6-sol", ["effort"] = "medium", ["size"] = "small", ["sessionScope"] = "execution",
            ["connections"] = new JsonObject
            {
                ["gateway"] = new JsonArray("GATEWAY_API_KEY", "GATEWAY_BASE_URL"), ["github"] = new JsonArray("GH_TOKEN")
            },
            ["pullRequestFeedback"] = "consider", ["gatewayBaseUrl"] = NativeProfile.GatewayBaseUrl
        }
    };
}
