using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broodling;

/// <summary>
/// An adopted Zeroshot environment release, read from the exact bytes of its producer manifest. The producer
/// states what its image carries; Broodling adopts it only when the manifest names the DirectTarget binding's
/// native, the approved Codex, the startup interface Broodling's entrypoint implements and the native discovery
/// the SDK binding speaks, on the supported platform. Readiness then requires the running target to be that image
/// and checks the identities it relies on. <c>Os</c> and <c>Components</c> are the producer's statement, which
/// only a live probe (<see cref="EnvironmentProbe"/>) verifies.
/// </summary>
internal sealed record TargetEnvironment(string Release, string Image, string Os, string Platform,
    EnvironmentComponents Components, string EntrypointSha256, string DiscoveryKind)
{
    internal const string Schema = "zeroshot-environment.manifest/v2";
    internal const string SupportedPlatform = "linux/amd64";
    /// <summary>
    /// The version of the container interface that <c>deployment/direct-target-entrypoint.sh</c> implements.
    /// Broodling owns the entrypoint and this value; an environment manifest records the version its embedded
    /// copy implements.
    /// </summary>
    internal const string StartupInterface = "zeroshot-environment.direct-target-startup/v1";
    internal const string Entrypoint = "/usr/local/bin/broodling-target";
    internal const string NativeExecutable = "/usr/local/bin/zeroshot";
    internal const string ResticExecutable = "/usr/local/bin/restic";

    /// <summary>
    /// Refuses bytes other than the adopted <paramref name="sha256"/>, another schema or platform, and a manifest
    /// whose native, Codex, startup interface or native discovery differs from what this revision supports.
    /// </summary>
    internal static TargetEnvironment Read(byte[] manifest, string sha256)
    {
        Require(Digests.Bytes(manifest) == sha256, "The environment manifest is not the adopted bytes.");
        try
        {
            using var document = JsonDocument.Parse(manifest);
            var root = document.RootElement;
            Require(Text(root, "schema") == Schema, "The environment manifest schema is not supported.");
            Require(Text(root, "platform") == SupportedPlatform, "The environment platform is not supported; only linux/amd64 is.");
            var image = root.GetProperty("image");
            var (repository, digest) = (Text(image, "repository"), Text(image, "digest"));
            Require(repository.Length > 0 && Regex.IsMatch(digest, "^sha256:[0-9a-f]{64}$"), "The environment manifest is malformed.");

            var native = root.GetProperty("native");
            Require(Text(native, "version") == DirectTargetBinding.NativeRelease
                && Text(native, "sourceRevision") == DirectTargetBinding.NativeSourceRevision
                && Text(native.GetProperty("zeroshot"), "path") == NativeExecutable
                && Text(native.GetProperty("zeroshot"), "sha256") == DirectTargetBinding.NativeExecutableSha256
                && Text(native.GetProperty("restic"), "path") == ResticExecutable
                && Text(native.GetProperty("restic"), "sha256") == DirectTargetBinding.ResticExecutableSha256,
                "The environment's native identity differs from the DirectTarget binding.");
            var tools = root.GetProperty("tools");
            Require(Text(root.GetProperty("harnesses").GetProperty("codex"), "version") == DirectTargetBinding.CodexVersion
                && Text(tools.GetProperty("harness-node"), "version") == DirectTargetBinding.CodexNodeVersion,
                "The environment's Codex differs from the approved harness.");
            var startup = root.GetProperty("startupInterfaces").GetProperty("entrypoint");
            Require(Text(startup, "version") == StartupInterface
                && startup.GetProperty("command").EnumerateArray().Select(part => part.GetString()).SequenceEqual([Entrypoint]),
                "The environment's startup interface differs from the supported one.");
            var entrypoint = root.GetProperty("integration").GetProperty("broodling-entrypoint");
            Require(Text(entrypoint, "path") == Entrypoint && Regex.IsMatch(Text(entrypoint, "sha256"), "^[0-9a-f]{64}$"),
                "The environment manifest is malformed.");
            var discovery = root.GetProperty("startupInterfaces").GetProperty("native").GetProperty("discovery");
            Require(DirectTargetDiscovery.Required.All(field => Text(discovery, field.Key) == field.Value),
                "The environment's native discovery differs from the SDK binding's DirectTarget protocol.");
            var os = Text(root, "os");
            Require(os.Length > 0, "The environment manifest is malformed.");
            var harnesses = root.GetProperty("harnesses");
            string? Optional(JsonElement parent, string name) =>
                parent.TryGetProperty(name, out var component) ? Text(component, "version") : null;
            IReadOnlyList<string> Dotnet(string list) => root.TryGetProperty("dotnet", out var dotnet)
                ? [.. dotnet.GetProperty(list).EnumerateArray().Select(item => item.GetString() ?? throw new InvalidOperationException())]
                : [];
            var components = new EnvironmentComponents(Text(native, "version"), Text(harnesses.GetProperty("codex"), "version"),
                Optional(harnesses, "claude"), Optional(harnesses, "copilot"), Text(tools.GetProperty("harness-node"), "version"),
                Text(tools.GetProperty("node"), "version"), Text(tools.GetProperty("gh"), "version"),
                Text(tools.GetProperty("git"), "version"), Text(tools.GetProperty("python"), "version"),
                Dotnet("sdks"), Dotnet("runtimes"));
            var environment = root.GetProperty("environment");
            return new(Text(environment, "name") + "-" + Text(environment, "release"), repository + "@" + digest, os,
                SupportedPlatform, components, Text(entrypoint, "sha256"), Text(discovery, "kind"));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new TargetNotReady("The environment manifest is malformed.");
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidOperationException();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new TargetNotReady(message);
    }
}

/// <summary>
/// The component versions an environment manifest states, exactly as it states them: the native release, the
/// harnesses (Codex required; Claude and Copilot when listed), the Node.js Codex runs on and the image's own
/// Node.js, gh, Git, Python and, when the manifest lists .NET, its SDKs and runtimes.
/// </summary>
internal sealed record EnvironmentComponents(string Native, string Codex, string? Claude, string? Copilot, string HarnessNode,
    string Node, string Gh, string Git, string Python, IReadOnlyList<string> DotnetSdks, IReadOnlyList<string> DotnetRuntimes);
