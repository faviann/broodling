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
}
