using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>
/// Secret-free operator DirectTarget selection. <see cref="DirectControlTokenFile"/> names the absolute path of
/// the file holding the target's private control token, and <see cref="DirectRootCertificate"/> optionally names
/// the absolute path of the PEM root that HTTPS connections trust instead of system trust. Only their shape is
/// checked here; each operation reads the files again, so a missing file never prevents startup or retained
/// reads, and a rotated token or root needs no restart. Any other member is refused.
/// </summary>
internal sealed record InvocationConfiguration(string DirectOrigin, string DirectControlTokenFile, string? DirectRootCertificate = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
    };

    internal static InvocationConfiguration Read(string path)
    {
        InvocationConfiguration? config = null;
        try { config = JsonSerializer.Deserialize<InvocationConfiguration>(File.ReadAllBytes(path), Options); }
        catch (JsonException) { }
        if (config is null) throw new InvalidContractProposal("Invalid invocation configuration.");
        if (config.DirectRootCertificate is { } root && !Path.IsPathFullyQualified(root))
            throw new UnsupportedRuntime("The DirectTarget root certificate must be an absolute path.");
        if (!Path.IsPathFullyQualified(config.DirectControlTokenFile))
            throw new UnsupportedRuntime("The DirectTarget control token file must be an absolute path.");
        _ = config.Target; // The one origin rule; it refuses anything else.
        return config;
    }

    internal InvocationTarget Target => new(DirectOrigin);

    /// <summary>The control token goes only to <see cref="DirectOrigin"/>; an Attempt retained at another origin gets none.</summary>
    internal DirectTargetAccess Access => DirectTargetAccess.For(DirectOrigin, DirectControlTokenFile, DirectRootCertificate);
}
