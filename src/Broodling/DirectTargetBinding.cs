using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The DirectTarget's release binding: the native release, source revision and Linux x86-64 executables
/// that serve HTTP Attempts, and the approved execution asset they run. The approval manifest, each
/// prepared submission's retained binding and target readiness name exactly these values, and the
/// target image and asset recipe carry them. It is independent of the LocalTarget bridge's SDK and
/// bundled native (<see cref="NativeProfile"/>); changing one never changes the other.
/// </summary>
internal static class DirectTargetBinding
{
    internal const string NativeVersion = "zeroshot 10.9.0";
    internal const string NativeSourceRevision = "75ae54b6693b6ae4cedeedd37a79ce3919d9a8fa";
    internal const string NativeExecutableSha256 = "f39952b98652301db58a89c4132a0476ae4ec570749b5945cc5200c2d22fad94";
    /// <summary>The release archive's restic, which native run allocation requires beside the executable.</summary>
    internal const string ResticExecutableSha256 = "90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf";
    internal const string AssetFile = "software-change-pr-codex-gateway-10.9.0.json";
    internal const string AssetSha256 = "258dc0ab46f30f05d6c95f7be493ede2ad0963160b9247f5ccdb699e4dcc20fc";
    /// <summary>SHA-256 of the exact reviewed approval manifest, execution-assets/approval.json.</summary>
    internal const string ApprovalSha256 = "307c3aecb48743b5dc5058c9314b57680801f19b00014b7fee536fc7d0938b81";

    /// <summary>The native identity as the approval manifest and a retained HTTP binding record it.</summary>
    internal static JsonObject Native() => new()
    {
        ["version"] = NativeVersion, ["sourceRevision"] = NativeSourceRevision, ["linuxX64ExecutableSha256"] = NativeExecutableSha256,
        ["release"] = new JsonObject
        {
            ["repository"] = "the-open-engine/zeroshot", ["tag"] = "v10.9.0",
            ["archive"] = "zeroshot-v10.9.0-x86_64-unknown-linux-musl.tar.gz",
            ["archiveSha256"] = "ca7305a0a165f3909481ccfcccce367d3bc2c40a9ab65760f6d6cad2a38d002d",
            ["resticLinuxX64ExecutableSha256"] = ResticExecutableSha256
        }
    };
}
