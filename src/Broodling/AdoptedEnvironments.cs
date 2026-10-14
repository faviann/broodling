using System.Text.Json;

namespace Broodling;

/// <summary>
/// One adopted environment release as this Broodling release bundles it: the exact bytes of its adoption record
/// (<c>broodling.target-environment-adoption/v1</c>) and of the producer manifest the record retains by SHA-256,
/// and the environment that manifest describes. Adoption makes an image eligible for an operator's environment
/// catalog; it is not an Execution asset approval.
/// </summary>
internal sealed record AdoptedEnvironment(string Release, string Image, byte[] Record, string RecordSha256,
    byte[] Manifest, string ManifestSha256, TargetEnvironment Environment);

/// <summary>
/// The adoption set: every <c>*.adoption.json</c> record bundled with the release under <c>environments/</c>.
/// Configuration cannot add to it; an operator's catalog may only name an image it contains.
/// </summary>
internal static class AdoptedEnvironments
{
    internal const string Kind = "broodling.target-environment-adoption/v1";
    private const string RecordSuffix = ".adoption.json";

    internal static IReadOnlyList<AdoptedEnvironment> LoadBundled() => Load(Path.Combine(AppContext.BaseDirectory, "environments"));

    /// <summary>
    /// Every record in <paramref name="directory"/>, refusing a record of another kind, a manifest that is not its
    /// retained bytes or that this release's native binding does not support, a record whose release or image
    /// differs from its manifest's, a repeated release or image, and an empty set.
    /// </summary>
    internal static IReadOnlyList<AdoptedEnvironment> Load(string directory)
    {
        string[] records;
        try { records = [.. Directory.GetFiles(directory, "*" + RecordSuffix).Order(StringComparer.Ordinal)]; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new UnsupportedRuntime("The adopted environment set is unavailable."); }
        if (records.Length == 0) throw new UnsupportedRuntime("No adopted environment is bundled.");
        var adopted = records.Select(path => Read(directory, path)).ToArray();
        if (adopted.DistinctBy(environment => environment.Release).Count() != adopted.Length
            || adopted.DistinctBy(environment => environment.Image).Count() != adopted.Length)
            throw new UnsupportedRuntime("The adopted environment set repeats a release or an image.");
        return adopted;
    }

    private static AdoptedEnvironment Read(string directory, string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            var record = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(record);
            var root = document.RootElement;
            var manifestPath = Text(root.GetProperty("manifest"), "path");
            var manifestSha256 = Text(root.GetProperty("manifest"), "sha256");
            if (Text(root, "kind") != Kind || manifestPath != Path.GetFileName(manifestPath) || manifestPath is "" or "." or "..")
                throw new UnsupportedRuntime($"The adoption record {name} is not a supported adoption record.");
            var manifest = File.ReadAllBytes(Path.Combine(directory, manifestPath));
            TargetEnvironment environment;
            try { environment = TargetEnvironment.Read(manifest, manifestSha256); }
            catch (TargetNotReady error) { throw new UnsupportedRuntime($"The adopted environment {name} is not supported: {error.Message}"); }
            if (Text(root, "environment") != environment.Release || Text(root, "image") != environment.Image)
                throw new UnsupportedRuntime($"The adoption record {name} names another release or image than its manifest.");
            return new(environment.Release, environment.Image, record, Digests.Bytes(record), manifest, manifestSha256, environment);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new UnsupportedRuntime($"The adoption record {name} is malformed."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new UnsupportedRuntime($"The adopted environment {name} is unavailable."); }
    }

    private static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidOperationException();
}
