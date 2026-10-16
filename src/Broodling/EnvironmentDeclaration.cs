using System.Text;
using System.Text.Json;

namespace Broodling;

/// <summary>The exact declaration an Attempt's requirement was read from: its path and commit (original B1), blob, digest and bytes.</summary>
internal sealed record DeclarationSource(string Path, string Commit, string BlobOid, string Sha256, byte[] Bytes);

/// <summary>
/// What a repository requires of its environment, kept apart from its resolution: the environment name its
/// declaration selects, or none, which the operator's default then serves.
/// </summary>
internal sealed record EnvironmentRequirement(string? Name, DeclarationSource? Declaration);

/// <summary>
/// The repository's environment declaration: <c>.broodling/environment.json</c> at the repository root, exactly
/// <c>{"version": 1, "environment": "&lt;name&gt;"}</c>, a regular file of strict UTF-8 without a byte order mark
/// and at most <see cref="MaxBytes"/> bytes. It is read only at an Attempt's original B1, never at a later revision
/// or the working tree. It selects one operator-approved environment by exact name: no version ranges, families,
/// toolchain matching or fallback. Other files such as <c>global.json</c> are never interpreted. It supplies
/// execution context only, never Contract authority or effects.
/// </summary>
internal static class EnvironmentDeclaration
{
    internal const string Path = ".broodling/environment.json";
    internal const int MaxBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The requirement <paramref name="commit"/> declares, through its retained pin; none when the file is absent.</summary>
    internal static EnvironmentRequirement Read(string repository, string commit)
    {
        GitCustody.TreeEntry entry;
        try { entry = GitCustody.ReadTreeEntry(repository, commit, Path, MaxBytes); }
        catch (GitBlobTooLarge) { throw Invalid($"The environment declaration {Path} is larger than {MaxBytes} bytes."); }
        return entry switch
        {
            GitCustody.TreeEntry.Absent => new(null, null),
            GitCustody.TreeEntry.File file => new(Parse(file.Content),
                new(Path, commit, file.BlobOid, Digests.Bytes(file.Content), file.Content)),
            _ => throw Invalid($"The environment declaration {Path} is not a regular file in a directory.")
        };
    }

    /// <summary>The environment name <paramref name="bytes"/> declare, refusing anything but the one supported form.</summary>
    internal static string Parse(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw Invalid($"The environment declaration {Path} is larger than {MaxBytes} bytes.");
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) throw Invalid($"The environment declaration {Path} starts with a byte order mark.");
        try { StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw Invalid($"The environment declaration {Path} is not UTF-8."); }
        try { JsonDocument.Parse(bytes).Dispose(); }
        catch (JsonException) { throw Invalid($"The environment declaration {Path} is not JSON."); }
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false }); }
        catch (JsonException)
        {
            throw new EnvironmentRefused("environment_declaration_conflict", $"The environment declaration {Path} repeats a member.");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid($"The environment declaration {Path} is not a JSON object.");
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number)
                throw Invalid($"The environment declaration {Path} has no numeric version.");
            if (!version.TryGetInt32(out var number) || number != 1)
                throw Unsupported($"The environment declaration {Path} has version {version.GetRawText()}; only version 1 is supported.");
            foreach (var member in root.EnumerateObject())
                if (member.Name is not ("version" or "environment"))
                    throw Unsupported($"The environment declaration {Path} has the unsupported member \"{member.Name}\"; "
                        + "an environment is selected by its exact name only.");
            if (!root.TryGetProperty("environment", out var name) || name.ValueKind != JsonValueKind.String
                || !EnvironmentCatalog.IsName(name.GetString()!))
                throw Invalid($"The environment declaration {Path} must name an environment as a lowercase DNS label of at most 54 characters.");
            return name.GetString()!;
        }
    }

    private static EnvironmentRefused Invalid(string message) => new("environment_declaration_invalid", message);
    private static EnvironmentRefused Unsupported(string message) => new("environment_requirement_unsupported", message);
}
