using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The DirectTarget's release binding: the native release, source revision and Linux x86-64 executables
/// that serve HTTP Attempts, the Codex harness the approved execution asset runs on, and that asset. The
/// approval manifest, each prepared submission's retained binding and target readiness name exactly these
/// values. An adopted target environment must carry them: readiness refuses a manifest that names others.
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
    /// <summary>Upstream's from-source build, as its published target image for this release carries it.</summary>
    internal const string NativeExecutableSha256 = "fd5ffef2d825d9035b3956b0026911a22e2c1b56f7fa205dd00310cfe88aeff5";
    /// <summary>The restic that native run allocation requires beside the executable.</summary>
    internal const string ResticExecutableSha256 = "90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf";
    /// <summary>Upstream's published target image for this release, which fixes both executables' bytes.</summary>
    internal const string NativeImage =
        "ghcr.io/the-open-engine/zeroshot-target:10.10.0@sha256:9769f74865d7e1137b82c9ed2ced4cbedb56cfd5fef6c9e8c0647955ba0c438b";
    /// <summary>The Codex release, and the Node.js that runs it, reviewed with the approved asset.</summary>
    internal const string CodexVersion = "0.155.0";
    internal const string CodexNodeVersion = "v24.21.0";
    internal const string AssetFile = "software-change-pr-codex-gateway.json";
    internal const string AssetSha256 = "258dc0ab46f30f05d6c95f7be493ede2ad0963160b9247f5ccdb699e4dcc20fc";
    /// <summary>SHA-256 of the exact reviewed approval manifest, execution-assets/approval.json.</summary>
    internal const string ApprovalSha256 = "3430be71fd0e073c6757fd17d6dec1dc7938712ea22768eca807a23d3fd9c179";

    /// <summary>The native identity as the approval manifest and a retained HTTP binding record it.</summary>
    internal static JsonObject Native() => new()
    {
        ["version"] = NativeVersion, ["sourceRevision"] = NativeSourceRevision, ["linuxX64ExecutableSha256"] = NativeExecutableSha256,
        ["resticLinuxX64ExecutableSha256"] = ResticExecutableSha256, ["image"] = NativeImage
    };

    /// <summary>The approved harness as the approval manifest records it.</summary>
    internal static JsonObject Codex() => new() { ["version"] = CodexVersion, ["node"] = CodexNodeVersion };
}
