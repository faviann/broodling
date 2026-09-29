using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The DirectTarget's release binding: the native release, source revision and Linux x86-64 executable
/// that serve HTTP Attempts, and the approved execution asset they run. The approval manifest, each
/// prepared submission's retained binding and target readiness name exactly these values, and the
/// target image and asset recipe carry them. It is independent of the LocalTarget bridge's SDK and
/// bundled native (<see cref="NativeProfile"/>); changing one never changes the other.
/// </summary>
internal static class DirectTargetBinding
{
    internal const string NativeVersion = "zeroshot 10.3.0";
    internal const string NativeSourceRevision = "054ad3fd6c763b98d12f5b2e90830b97116561ad";
    internal const string NativeExecutableSha256 = "afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06";
    internal const string AssetFile = "software-change-pr-codex-gateway.json";
    internal const string AssetSha256 = "10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8";

    /// <summary>The native identity as the approval manifest and a retained HTTP binding record it.</summary>
    internal static JsonObject Native() => new()
    {
        ["version"] = NativeVersion, ["sourceRevision"] = NativeSourceRevision, ["linuxX64ExecutableSha256"] = NativeExecutableSha256
    };
}
