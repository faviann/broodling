using System.Text.RegularExpressions;

namespace Broodling;

/// <summary>
/// One catalog entry exactly as operator configuration states it, before validation: the environment name, the
/// origin of the one DirectTarget serving it, the absolute file holding that origin's control token, the adopted
/// image by digest, and the target's container and host state and home mounts.
/// </summary>
internal sealed record EnvironmentEntry(string Name, string Origin, string ControlTokenFile, string Image, string Container,
    string StateMount, string HomeMount);

/// <summary>
/// The exact target serving one environment: its name, canonical origin, container and host state and home mounts.
/// Never carries a credential or token path, so it may be retained and reported.
/// </summary>
public sealed record TargetIdentity(string Environment, string Origin, string Container, string StateMount, string HomeMount);

/// <summary>One validated catalog entry: an operator-named adopted environment and the target serving it.</summary>
internal sealed record ConfiguredEnvironment(string Name, string Origin, string ControlTokenFile, AdoptedEnvironment Adoption,
    TargetIdentity Target);

/// <summary>
/// The operator's environment catalog: each approved environment by name, mapped to an adopted image and the one
/// DirectTarget serving it, plus an optional default for repositories that declare none. Secret-free: control
/// tokens stay in their files, which <see cref="Access"/> reads again at each operation and sends only to the
/// origin they are configured for.
/// </summary>
public sealed class EnvironmentCatalog
{
    /// <summary>A DNS label short enough that <c>zeroshot-&lt;name&gt;</c> is one too.</summary>
    private static readonly Regex Name = new("^[a-z0-9]([a-z0-9-]{0,52}[a-z0-9])?$", RegexOptions.CultureInvariant);
    private static readonly Regex Container = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant);

    private EnvironmentCatalog(IReadOnlyList<ConfiguredEnvironment> entries, string? defaultEnvironment, DirectTargetAccess access,
        Func<string, ConfiguredEnvironment?>? lookup = null)
    {
        Entries = entries;
        Default = defaultEnvironment;
        Access = access;
        this.lookup = lookup;
    }

    /// <summary>Tests only: further environments found by name, see <see cref="WithLookup"/>.</summary>
    private readonly Func<string, ConfiguredEnvironment?>? lookup;

    internal IReadOnlyList<ConfiguredEnvironment> Entries { get; }

    /// <summary>The environment for repositories that declare none; null refuses them.</summary>
    internal string? Default { get; }

    /// <summary>
    /// The current connection material: each entry's control token file for exactly its origin, and the root that
    /// HTTPS connections trust.
    /// </summary>
    public DirectTargetAccess Access { get; }

    internal ConfiguredEnvironment? Find(string name) => Entries.SingleOrDefault(entry => entry.Name == name) ?? lookup?.Invoke(name);

    internal ConfiguredEnvironment? DefaultEntry => Default is null ? null : Find(Default);

    internal static bool IsName(string name) => Name.IsMatch(name);

    /// <summary>
    /// The configured environment <paramref name="requirement"/> selects: its declared name exactly, or the default
    /// when it declares none. Never a nearest or similar entry.
    /// </summary>
    internal EnvironmentResolution Resolve(EnvironmentRequirement requirement)
    {
        if (requirement.Name is { } name)
            return Find(name) is { } declared
                ? new(requirement, declared.Adoption, declared.Target)
                : throw new EnvironmentRefused("environment_unapproved",
                    $"The repository declares the environment {name}, which the operator's environment catalog does not approve.");
        return DefaultEntry is { } fallback
            ? new(requirement, fallback.Adoption, fallback.Target)
            : throw new EnvironmentRefused("environment_default_missing",
                "The repository declares no environment and the operator's environment catalog names no default.");
    }

    /// <summary>
    /// Validates <paramref name="entries"/> against <paramref name="adopted"/>, refusing a name that is not a DNS label
    /// of at most 54 characters; a repeated name, origin or container; an origin that is not canonical; a token file
    /// or mount that is not a normalized absolute path; state, home or token paths that overlap, within or across
    /// entries; an image that is not exactly one adopted image; and a default that names no entry.
    /// </summary>
    internal static EnvironmentCatalog Create(IReadOnlyList<EnvironmentEntry> entries, string? defaultEnvironment,
        string? rootCertificate, IReadOnlyList<AdoptedEnvironment> adopted)
    {
        if (rootCertificate is not null && !Path.IsPathFullyQualified(rootCertificate))
            throw new UnsupportedRuntime("The DirectTarget root certificate must be an absolute path.");
        var configured = new List<ConfiguredEnvironment>();
        foreach (var entry in entries)
        {
            Require(Name.IsMatch(entry.Name),
                "An environment name must be a lowercase DNS label of at most 54 characters.");
            Require(DirectTargetExchange.CanonicalOrigin(entry.Origin) is not null,
                $"Environment {entry.Name}: the origin must be canonical HTTPS or HTTP to exactly 127.0.0.1 or [::1].");
            Require(Container.IsMatch(entry.Container),
                $"Environment {entry.Name}: the container name is invalid.");
            Require(Absolute(entry.ControlTokenFile), $"Environment {entry.Name}: the control token file must be an absolute path.");
            Require(Absolute(entry.StateMount) && Absolute(entry.HomeMount),
                $"Environment {entry.Name}: the state and home mounts must be normalized absolute paths.");
            var adoption = adopted.SingleOrDefault(candidate => candidate.Image == entry.Image)
                ?? throw new UnsupportedRuntime($"Environment {entry.Name}: the image is not an adopted environment image.");
            configured.Add(new(entry.Name, entry.Origin, entry.ControlTokenFile, adoption,
                new(entry.Name, entry.Origin, entry.Container, entry.StateMount, entry.HomeMount)));
        }
        Require(configured.DistinctBy(entry => entry.Name).Count() == configured.Count, "Environment names must be unique.");
        Require(configured.DistinctBy(entry => entry.Origin).Count() == configured.Count, "Environment origins must be unique.");
        Require(configured.DistinctBy(entry => entry.Target.Container).Count() == configured.Count,
            "Environment target containers must be unique.");
        var paths = configured.SelectMany(entry => new[] { entry.Target.StateMount, entry.Target.HomeMount, entry.ControlTokenFile }).ToArray();
        for (var first = 0; first < paths.Length; first++)
            for (var second = first + 1; second < paths.Length; second++)
                Require(!PhysicalPaths.Contains(paths[first], paths[second]) && !PhysicalPaths.Contains(paths[second], paths[first]),
                    "Environment state, home and control token paths must not overlap.");
        Require(defaultEnvironment is null || configured.Any(entry => entry.Name == defaultEnvironment),
            "The default environment is not a configured environment.");
        var byOrigin = configured.ToDictionary(entry => entry.Origin, entry => entry.ControlTokenFile, StringComparer.Ordinal);
        return new(configured, defaultEnvironment,
            new(origin => byOrigin.TryGetValue(origin, out var file) ? file : null, rootCertificate));
    }

    /// <summary>The same entries with other connection material, for tests whose stand-ins register tokens as they start.</summary>
    internal EnvironmentCatalog WithAccess(DirectTargetAccess access) => new(Entries, Default, access, lookup);

    /// <summary>
    /// The same entries, also naming whatever <paramref name="lookup"/> finds by name, for tests whose stand-in
    /// targets are configured as they start.
    /// </summary>
    internal EnvironmentCatalog WithLookup(Func<string, ConfiguredEnvironment?> lookup) => new(Entries, Default, Access, lookup);

    private static bool Absolute(string? path) =>
        !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && Path.GetFullPath(path) == path && (path == "/" || !path.EndsWith('/'));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new UnsupportedRuntime(message);
    }

    public override string ToString() => nameof(EnvironmentCatalog);
}
