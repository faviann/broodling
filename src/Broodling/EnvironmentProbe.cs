namespace Broodling;

/// <summary>
/// One live check of a manifest-stated component inside an environment image or running target: a POSIX shell
/// command and the exact output lines it prints when the component is the one the manifest states. Versions and
/// the OS identity only; byte identities and the package list are checked by their own callers.
/// </summary>
internal sealed record EnvironmentProbe(string Component, string Command, IReadOnlyList<string> Expected)
{
    /// <summary>
    /// The probe of each component a manifest may state, by its manifest name: the command and the line it prints for
    /// each stated version. The adoption set refuses a manifest stating any other component.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string Command, Func<string, string> Line)> Commands =
        new Dictionary<string, (string, Func<string, string>)>
        {
            ["native"] = (TargetEnvironment.NativeExecutable + " --version", version => "zeroshot " + version),
            ["codex"] = ("codex --version 2>/dev/null", version => "codex-cli " + version),
            ["claude"] = ("claude --version", version => version + " (Claude Code)"),
            ["copilot"] = ("copilot --version | head -n 1", version => "GitHub Copilot CLI " + version + "."),
            ["harness-node"] = ("/opt/zeroshot/bin/node --version", Verbatim),
            ["node"] = ("node --version", Verbatim),
            ["npm"] = ("npm --version", Verbatim),
            ["yarn"] = ("yarn --version", Verbatim),
            ["corepack"] = ("corepack --version", Verbatim),
            ["python"] = ("python3 --version", Verbatim),
            ["pip"] = ("pip --version | cut -d ' ' -f 1,2", Verbatim),
            ["rust"] = ("rustc --version", Verbatim),
            ["rustup"] = ("rustup --version", Verbatim),
            ["rust-components"] = (
                "echo \"$(rustup show active-toolchain | cut -d ' ' -f 1): $(rustup component list --installed | paste -sd ' ' -)\"",
                Verbatim),
            ["gcc"] = ("gcc -dumpfullversion", Verbatim),
            ["git"] = ("git --version", Verbatim),
            ["gh"] = ("gh --version | head -n 1", Verbatim),
            ["docker-cli"] = ("docker --version", Verbatim),
            ["dotnet-sdks"] = ("dotnet --list-sdks | cut -d ' ' -f 1", Verbatim),
            ["dotnet-runtimes"] = ("dotnet --list-runtimes | cut -d ' ' -f 1,2", Verbatim)
        };

    private static string Verbatim(string version) => version;

    /// <summary>The OS identity and every component <paramref name="environment"/>'s manifest states.</summary>
    internal static IReadOnlyList<EnvironmentProbe> For(TargetEnvironment environment) =>
    [
        new("os", "grep '^PRETTY_NAME=' /etc/os-release", ["PRETTY_NAME=\"" + environment.Os + "\""]),
        .. environment.Components.Select(component => new EnvironmentProbe(component.Key, Commands[component.Key].Command,
            [.. component.Value.Select(Commands[component.Key].Line)]))
    ];

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
