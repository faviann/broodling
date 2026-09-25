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

    internal sealed record Direct(string Target, string DirectOrigin) : InvocationConfiguration;

    internal sealed record Local(string Target, string PythonExecutable, string StateDirectory, string WorkspaceRoot,
        string? RealCodex = null, string? ProfileHome = null, string? CodexHome = null, string? Launcher = null) : InvocationConfiguration;

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
            // The installed operator profile is loopback-only. The callable invocation API remains separate.
            case Direct direct when !Uri.TryCreate(direct.DirectOrigin, UriKind.Absolute, out var endpoint)
                || endpoint.Port is < 1 or > 65535 || direct.DirectOrigin != $"http://127.0.0.1:{endpoint.Port}":
                throw new UnsupportedRuntime("The operator DirectTarget must use a canonical loopback HTTP origin with an explicit port.");
            case Direct:
                return config;
            // A Codex profile is all four paths or none.
            case Local local when new[] { local.RealCodex, local.ProfileHome, local.CodexHome, local.Launcher }
                .Select(path => path is null).Distinct().Count() == 1:
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
                local.RealCodex is null ? null : new(local.RealCodex, local.ProfileHome!, local.CodexHome!, local.Launcher!)),
            transport ?? new ZeroshotTransport(local.PythonExecutable)),
        _ => throw new UnsupportedRuntime("The invocation target is unsupported.")
    };
}
