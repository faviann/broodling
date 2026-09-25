using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>
/// Secret-free operator target selection. <c>"target"</c> names exactly one kind, and each kind
/// accepts only its own fields: a DirectTarget has no Python, native state, workspace or launcher.
/// </summary>
internal abstract record InvocationConfiguration
{
    private InvocationConfiguration() { }

    /// <summary>
    /// <see cref="DirectRootCertificate"/> optionally names the absolute path of the PEM root that HTTPS
    /// connections trust instead of system trust. Only its shape is checked here; the file is read by each
    /// DirectTarget operation, so a missing file never prevents startup or retained reads.
    /// </summary>
    internal sealed record Direct(string Target, string DirectOrigin, string? DirectRootCertificate = null) : InvocationConfiguration;

    internal sealed record Local(string Target, string PythonExecutable, string StateDirectory, string WorkspaceRoot,
        string RealCodex, string ProfileHome, string CodexHome, string Launcher) : InvocationConfiguration;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
    };

    internal static InvocationConfiguration Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        InvocationConfiguration? config = null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var kind = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("target", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            config = kind switch
            {
                "direct" => JsonSerializer.Deserialize<Direct>(bytes, Options),
                "local" => JsonSerializer.Deserialize<Local>(bytes, Options),
                _ => null
            };
        }
        catch (JsonException) { }
        switch (config)
        {
            case Direct { DirectRootCertificate: { } root } when !Path.IsPathFullyQualified(root):
                throw new UnsupportedRuntime("The DirectTarget root certificate must be an absolute path.");
            case Direct direct:
                _ = new InvocationTarget.Direct(direct.DirectOrigin); // The one origin rule; it refuses anything else.
                return config;
            case Local:
                return config;
            default:
                throw new InvalidContractProposal("Invalid invocation configuration.");
        }
    }

    /// <summary>The configured target; a LocalTarget uses the pinned SDK bridge unless a transport is supplied.</summary>
    internal InvocationTarget ToTarget(INativeTransport? transport) => this switch
    {
        Direct direct => new InvocationTarget.Direct(direct.DirectOrigin),
        Local local => new InvocationTarget.Local(local.WorkspaceRoot, new NativeProfile(local.StateDirectory,
                new(local.RealCodex, local.ProfileHome, local.CodexHome, local.Launcher)),
            transport ?? new ZeroshotTransport(local.PythonExecutable)),
        _ => throw new UnsupportedRuntime("The invocation target is unsupported.")
    };
}
