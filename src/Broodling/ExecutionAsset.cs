using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The release-bundled, approved DirectTarget graph/runtime. Identity is SHA-256 of the exact file bytes;
/// C# transports the values opaquely and never expands, edits or regenerates them.
/// </summary>
internal sealed class ExecutionAsset
{
    internal const string ApprovedSha256 = "10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8";
    private const string FileName = "software-change-pr-codex-gateway.json";
    private readonly byte[] content;

    private ExecutionAsset(byte[] content) => this.content = content;

    internal string Sha256 => ApprovedSha256;
    internal byte[] Content() => content.ToArray();
    internal JsonNode Graph() => JsonNode.Parse(content)!["graph"]!.DeepClone();
    internal JsonNode Runtime() => JsonNode.Parse(content)!["runtime"]!.DeepClone();

    /// <summary>Retained content, never today's installed file. Only the approved identity is supported.</summary>
    internal static ExecutionAsset? FromRetained(byte[]? bytes) =>
        bytes is not null && Digests.Bytes(bytes) == ApprovedSha256 ? new(bytes) : null;

    internal static ExecutionAsset LoadBundled() => Load(Path.Combine(AppContext.BaseDirectory, "execution-assets"));

    internal static ExecutionAsset Load(string directory)
    {
        byte[] bytes;
        JsonNode? manifest;
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(directory, FileName));
            manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "approval.json")));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new UnsupportedRuntime("The approved execution asset and manifest are required.");
        }
        if (Digests.Bytes(bytes) != ApprovedSha256)
            throw new UnsupportedRuntime("The execution asset is not the approved identity.");
        if ((string?)manifest?["kind"] != "broodling.execution-asset-approval/v1"
            || !JsonNode.DeepEquals(manifest["asset"], new JsonObject { ["file"] = FileName, ["bytes"] = bytes.Length, ["sha256"] = ApprovedSha256 })
            || !JsonNode.DeepEquals(manifest["native"], Approval["native"])
            || !JsonNode.DeepEquals(manifest["policy"], Approval["policy"]))
            throw new UnsupportedRuntime("The execution asset approval is bound to a different release or policy.");
        return new(bytes);
    }

    private static readonly JsonObject Approval = new()
    {
        ["native"] = new JsonObject
        {
            ["version"] = NativeProfile.NativeVersion, ["sourceRevision"] = NativeProfile.NativeSourceRevision,
            ["linuxX64ExecutableSha256"] = NativeProfile.NativeExecutableSha256
        },
        ["policy"] = new JsonObject
        {
            ["workflow"] = "software-change", ["delivery"] = "pull_request", ["harness"] = "codex", ["provider"] = "gateway",
            ["model"] = "gpt-5.6-sol", ["effort"] = "medium", ["size"] = "small", ["sessionScope"] = "execution",
            ["connections"] = new JsonObject
            {
                ["gateway"] = new JsonArray("GATEWAY_API_KEY", "GATEWAY_BASE_URL"), ["github"] = new JsonArray("GH_TOKEN")
            },
            ["gatewayBaseUrl"] = NativeProfile.GatewayBaseUrl
        }
    };
}
