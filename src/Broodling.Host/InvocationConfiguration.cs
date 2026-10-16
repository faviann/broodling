using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>
/// Secret-free operator configuration: the environment catalog. Each entry of <see cref="Environments"/> names an
/// environment, the canonical origin of the one DirectTarget serving it, the absolute path of the file holding that
/// target's private control token, the adopted image by digest, and the target's container and host state and home
/// mounts. <see cref="DefaultEnvironment"/> optionally names the entry for repositories that declare none, and
/// <see cref="DirectRootCertificate"/> optionally names the absolute path of the PEM root that HTTPS connections
/// trust instead of system trust. Only shape and the catalog's rules are checked here; each operation reads the
/// files again, so a missing file never prevents startup or retained reads, and a rotated token or root needs no
/// restart. Any other member is refused.
/// </summary>
internal sealed record InvocationConfiguration(IReadOnlyList<EnvironmentEntry> Environments, string? DefaultEnvironment = null,
    string? DirectRootCertificate = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false
    };

    /// <summary>The catalog <paramref name="path"/> configures over the adoption set bundled with this release.</summary>
    internal static EnvironmentCatalog Read(string path)
    {
        InvocationConfiguration? config = null;
        try { config = JsonSerializer.Deserialize<InvocationConfiguration>(File.ReadAllBytes(path), Options); }
        catch (JsonException) { }
        if (config is null || config.Environments is null || config.Environments.Any(entry => entry is null))
            throw new InvalidContractProposal("Invalid invocation configuration.");
        if (config.Environments.Count == 0) throw new UnsupportedRuntime("The invocation configuration names no environment.");
        return EnvironmentCatalog.Create(config.Environments, config.DefaultEnvironment, config.DirectRootCertificate,
            AdoptedEnvironments.LoadBundled());
    }
}
