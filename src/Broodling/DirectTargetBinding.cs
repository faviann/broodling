using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// The DirectTarget's release binding: the native release, source revision and Linux x86-64 executables
/// that serve HTTP Attempts, the Claude Code harness the approved execution asset runs on, and that asset. The
/// approval manifest, each prepared submission's retained binding and target readiness name exactly these
/// values. An adopted target environment must carry them: readiness refuses a manifest that names others.
/// </summary>
internal static class DirectTargetBinding
{
    /// <summary>
    /// The one supported gateway, sub2api, by its base URL without a version path: native's gateway provider
    /// maps it to the agents' Anthropic base URL, and the bundled proposer appends its Chat Completions path.
    /// Dispatch credentials and the approved asset policy require exactly this value.
    /// </summary>
    internal const string GatewayBaseUrl = "https://gateway.ai.faviann.com";
    internal const string NativeRelease = "10.12.0";
    internal const string NativeVersion = "zeroshot " + NativeRelease;
    internal const string NativeSourceRevision = "928de8ff918488749ca9d23e0a7349873401b7f7";
    /// <summary>Upstream's from-source build, as its published target image for this release carries it.</summary>
    internal const string NativeExecutableSha256 = "7b3c74db2c4ce06d2b483813a05c6b5928f81b10c471366d677ec25371b52693";
    /// <summary>The restic that native run allocation requires beside the executable.</summary>
    internal const string ResticExecutableSha256 = "90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf";
    /// <summary>Upstream's published target image for this release, which fixes both executables' bytes.</summary>
    internal const string NativeImage =
        "ghcr.io/the-open-engine/zeroshot-target:10.12.0@sha256:9eca152ead38e6589b76b13cca802ce2e7538579b851566a891ed470b069bd51";
    /// <summary>The harness the approved asset runs, and its key in the approval and environment manifests.</summary>
    internal const string Harness = "claude";
    /// <summary>
    /// The Claude Code release reviewed with the approved asset. It is a native executable, so no Node.js is
    /// part of the harness.
    /// </summary>
    internal const string ClaudeVersion = "2.1.276";
    /// <summary>
    /// The managed settings the adopted environment installs for Claude Code. They stop its nonessential traffic and
    /// title call, so every model call goes to the gateway, and lock out repository hooks and MCP servers. Claude
    /// Code reports nothing and runs without them when the file is missing.
    /// </summary>
    internal const string ClaudeManagedSettings = "/etc/claude-code/managed-settings.json";
    internal const string ClaudeManagedSettingsSha256 = "cbedfec6d9c688288255c084f95269b4198d86435dbb48953ca4e71dac961b0b";
    internal const string AssetFile = "software-change-pr-claude-gateway.json";
    internal const string AssetSha256 = "7e232da54ed2ca138b31fea38a6f002d08d77b584965253b18077205b2c1e5be";
    /// <summary>SHA-256 of the exact reviewed approval manifest, execution-assets/approval.json.</summary>
    internal const string ApprovalSha256 = "07cda6c77dac541e8a724691416c260379fc0988a10a43471c24c2642d6fa0d2";

    /// <summary>The native identity as the approval manifest and a retained HTTP binding record it.</summary>
    internal static JsonObject Native() => new()
    {
        ["version"] = NativeVersion, ["sourceRevision"] = NativeSourceRevision, ["linuxX64ExecutableSha256"] = NativeExecutableSha256,
        ["resticLinuxX64ExecutableSha256"] = ResticExecutableSha256, ["image"] = NativeImage
    };

    /// <summary>The approved harness as the approval manifest records it.</summary>
    internal static JsonObject Claude() => new()
    {
        ["version"] = ClaudeVersion,
        ["managedSettings"] = new JsonObject { ["path"] = ClaudeManagedSettings, ["sha256"] = ClaudeManagedSettingsSha256 }
    };
}
