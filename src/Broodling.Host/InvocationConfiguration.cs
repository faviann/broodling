using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>
/// Secret-free operator DirectTarget selection. <see cref="DirectRootCertificate"/> optionally names the
/// absolute path of the PEM root that HTTPS connections trust instead of system trust. Only its shape is
/// checked here; each new DirectTarget TLS connection reads the file, so a missing file never prevents
/// startup or retained reads. Any other member is refused.
/// </summary>
internal sealed record InvocationConfiguration(string DirectOrigin, string? DirectRootCertificate = null)
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
        _ = config.Target; // The one origin rule; it refuses anything else.
        return config;
    }

    internal InvocationTarget Target => new(DirectOrigin);
}
