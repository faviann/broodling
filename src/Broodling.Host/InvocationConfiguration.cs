using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

internal sealed record InvocationConfiguration(string PythonExecutable, string StateDirectory, string WorkspaceRoot,
    string? DirectOrigin = null, string? RealCodex = null, string? ProfileHome = null, string? CodexHome = null, string? Launcher = null)
{
    internal static InvocationConfiguration Read(string path)
    {
        var config = JsonSerializer.Deserialize<InvocationConfiguration>(File.ReadAllBytes(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidContractProposal("Invalid invocation configuration.");
        // The installed operator profile is loopback-only. The callable invocation API remains separate.
        if (config.DirectOrigin is { } origin && (!Uri.TryCreate(origin, UriKind.Absolute, out var endpoint)
            || endpoint.Port is < 1 or > 65535 || origin != $"http://127.0.0.1:{endpoint.Port}"))
            throw new UnsupportedRuntime("The operator DirectTarget must use a canonical loopback HTTP origin with an explicit port.");
        return config;
    }
}
