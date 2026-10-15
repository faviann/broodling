namespace Broodling;

/// <summary>
/// One live check of a manifest-stated component inside an environment image or running target: a POSIX shell
/// command and the exact output lines it prints when the component is the one the manifest states. Versions and
/// the OS identity only; byte identities and the package list are checked by their own callers.
/// </summary>
internal sealed record EnvironmentProbe(string Component, string Command, IReadOnlyList<string> Expected)
{
    /// <summary>Every component of <paramref name="environment"/> that has a fixed probe.</summary>
    internal static IReadOnlyList<EnvironmentProbe> For(TargetEnvironment environment)
    {
        var components = environment.Components;
        List<EnvironmentProbe> probes =
        [
            new("os", "grep '^PRETTY_NAME=' /etc/os-release", ["PRETTY_NAME=\"" + environment.Os + "\""]),
            new("native", TargetEnvironment.NativeExecutable + " --version", ["zeroshot " + components.Native]),
            new("codex", "codex --version 2>/dev/null", ["codex-cli " + components.Codex]),
            new("harness-node", "/opt/zeroshot/bin/node --version", [components.HarnessNode]),
            new("node", "node --version", [components.Node]),
            new("gh", "gh --version | head -n 1", [components.Gh]),
            new("git", "git --version", [components.Git]),
            new("python", "python3 --version", [components.Python])
        ];
        if (components.Claude is { } claude) probes.Add(new("claude", "claude --version", [claude + " (Claude Code)"]));
        if (components.Copilot is { } copilot)
            probes.Add(new("copilot", "copilot --version | head -n 1", ["GitHub Copilot CLI " + copilot + "."]));
        if (components.DotnetSdks.Count > 0)
            probes.Add(new("dotnet-sdks", "dotnet --list-sdks | cut -d ' ' -f 1", components.DotnetSdks));
        if (components.DotnetRuntimes.Count > 0)
            probes.Add(new("dotnet-runtimes", "dotnet --list-runtimes | cut -d ' ' -f 1,2", components.DotnetRuntimes));
        return probes;
    }

    /// <summary>One script running every probe in order, for a single <c>/bin/sh -ec</c> invocation.</summary>
    internal static string Script(IEnumerable<EnvironmentProbe> probes) => string.Join('\n', probes.Select(probe => probe.Command));

    internal const string Marker = "@@broodling-environment-probe ";

    /// <summary>
    /// One script, for a single <c>/bin/sh -c</c> invocation, that runs every probe even when one fails and marks
    /// where each probe's output starts, so <see cref="Mismatches"/> can attribute a missing or other component.
    /// </summary>
    internal static string Sections(IEnumerable<EnvironmentProbe> probes) =>
        string.Join('\n', probes.Select(probe => $"echo '{Marker}{probe.Component}'; ({probe.Command}) 2>/dev/null || :"));

    /// <summary>
    /// The components of <paramref name="probes"/> whose section of <paramref name="output"/>, from
    /// <see cref="Sections"/>, is not exactly the expected lines: missing, failing or another version.
    /// </summary>
    internal static IReadOnlyList<string> Mismatches(IReadOnlyList<EnvironmentProbe> probes, string output)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith(Marker, StringComparison.Ordinal))
                sections[line[Marker.Length..]] = current = [];
            else if (current is null) return [.. probes.Select(probe => probe.Component)];
            else if (line.Length > 0) current.Add(line);
        }
        return [.. probes.Where(probe => !sections.TryGetValue(probe.Component, out var lines) || !lines.SequenceEqual(probe.Expected))
            .Select(probe => probe.Component)];
    }
}
