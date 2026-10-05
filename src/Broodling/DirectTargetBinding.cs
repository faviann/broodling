using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The DirectTarget's release binding: the native release, source revision and Linux x86-64 executables
/// that serve HTTP Attempts, and the approved execution asset they run. The approval manifest, each
/// prepared submission's retained binding and target readiness name exactly these values, and the
/// target image and asset recipe carry them.
/// </summary>
internal static class DirectTargetBinding
{
    /// <summary>
    /// The one supported gateway endpoint: the bundled proposer calls it, and dispatch credentials and the
    /// approved asset policy require it.
    /// </summary>
    internal const string GatewayBaseUrl = "https://cliproxy.local.faviann.com/v1";
    internal const string NativeRelease = "10.10.0";
    internal const string NativeVersion = "zeroshot " + NativeRelease;
    internal const string NativeSourceRevision = "3ee1192cec359a0b997f464e703a936e8b67d63c";
    internal const string NativeExecutableSha256 = "d0c84ffbafa731ef7fa6b61f87af9c000cc4e5b4d2e0d3b7df461fd239bb923e";
    /// <summary>The release archive's restic, which native run allocation requires beside the executable.</summary>
    internal const string ResticExecutableSha256 = "90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf";
    internal const string AssetFile = "software-change-pr-codex-gateway.json";
    internal const string AssetSha256 = "258dc0ab46f30f05d6c95f7be493ede2ad0963160b9247f5ccdb699e4dcc20fc";
    /// <summary>SHA-256 of the exact reviewed approval manifest, execution-assets/approval.json.</summary>
    internal const string ApprovalSha256 = "22416ed8020481a7c99398522c0425a5ad1fa5d87f174f17946b704bfb220d05";

    /// <summary>The native identity as the approval manifest and a retained HTTP binding record it.</summary>
    internal static JsonObject Native() => new()
    {
        ["version"] = NativeVersion, ["sourceRevision"] = NativeSourceRevision, ["linuxX64ExecutableSha256"] = NativeExecutableSha256,
        ["release"] = new JsonObject
        {
            ["repository"] = "the-open-engine/zeroshot", ["tag"] = "v10.10.0",
            ["archive"] = "zeroshot-v10.10.0-x86_64-unknown-linux-musl.tar.gz",
            ["archiveSha256"] = "fbc13b2385a088ff0f8fa03fdf72d4aa7ae6202d4289204e57ba1617628d6f16",
            ["resticLinuxX64ExecutableSha256"] = ResticExecutableSha256
        }
    };
}
