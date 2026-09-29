using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The release-bundled, approved DirectTarget graph/runtime. Identity is SHA-256 of the exact file bytes;
/// C# transports the values opaquely and never expands, edits or regenerates them. The asset and the
/// exact reviewed approval manifest are pinned by the <see cref="DirectTargetBinding"/>.
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
        byte[] bytes, manifest;
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(directory, DirectTargetBinding.AssetFile));
            manifest = File.ReadAllBytes(Path.Combine(directory, "approval.json"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UnsupportedRuntime("The approved execution asset and manifest are required.");
        }
        if (Digests.Bytes(bytes) != DirectTargetBinding.AssetSha256)
            throw new UnsupportedRuntime("The execution asset is not the approved identity.");
        // The whole reviewed manifest: asset, native release, policy, recipe, structure and review record.
        if (Digests.Bytes(manifest) != DirectTargetBinding.ApprovalSha256)
            throw new UnsupportedRuntime("The execution asset approval is not the reviewed manifest.");
        return new(bytes);
    }
}
