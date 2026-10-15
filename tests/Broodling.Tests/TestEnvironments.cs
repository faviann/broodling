using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Broodling.Tests;

/// <summary>
/// Environment catalogs for the suite, over the adoption set this release bundles. Each environment is one
/// test target's origin with synthetic, non-overlapping container and mount names. Catalogs built here resolve
/// control tokens through <see cref="TestAccess.Live"/>, so a stand-in that restarts with a rotated token file
/// still works, exactly as a configuration naming every test target would.
/// </summary>
internal static class TestEnvironments
{
    private static readonly Lazy<IReadOnlyList<AdoptedEnvironment>> bundled = new(AdoptedEnvironments.LoadBundled);
    /// <summary>Every environment <see cref="For(string[])"/> has configured, by name; <see cref="Live"/> names them all.</summary>
    private static readonly ConcurrentDictionary<string, ConfiguredEnvironment> configured = new();

    internal static IReadOnlyList<AdoptedEnvironment> Bundled => bundled.Value;

    /// <summary>The bundled dotnet-r2 adoption, which every test environment names unless it says otherwise.</summary>
    internal static AdoptedEnvironment Adopted => Bundled.Single();

    /// <summary>
    /// Every environment <see cref="For(string[])"/> has configured, unchanged, with connection material for every
    /// live test target, and no default.
    /// </summary>
    internal static EnvironmentCatalog Live { get; } =
        Catalog([], null).WithLookup(name => configured.TryGetValue(name, out var entry) ? entry : null);

    /// <summary>The environment name <see cref="For(string[])"/> gives <paramref name="origin"/>'s target.</summary>
    internal static string NameFor(string origin) =>
        "target-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(origin)))[..12];

    internal static string NameFor(Uri origin) => NameFor(origin.GetLeftPart(UriPartial.Authority));

    /// <summary>The entry <see cref="For(string[])"/> configures for <paramref name="origin"/>, with another token file if given.</summary>
    internal static EnvironmentEntry EntryFor(string origin, string? controlTokenFile = null) =>
        Entry(NameFor(origin), origin, controlTokenFile);

    internal static EnvironmentEntry EntryFor(Uri origin, string? controlTokenFile = null) =>
        EntryFor(origin.GetLeftPart(UriPartial.Authority), controlTokenFile);

    /// <summary>Like <see cref="Live"/>, trusting <paramref name="rootCertificate"/> (null: system trust).</summary>
    internal static EnvironmentCatalog Trusting(string? rootCertificate, params string[] origins) =>
        For(origins).WithAccess(TestAccess.Trusting(rootCertificate));

    /// <summary>
    /// One environment per origin, named for it (<see cref="NameFor(string)"/>), the first being the default; with
    /// no origin, the same as <see cref="Live"/>. <see cref="Live"/> names each of them from then on.
    /// </summary>
    internal static EnvironmentCatalog For(params string[] origins)
    {
        if (origins.Length == 0) return Live;
        var catalog = Catalog([.. origins.Select(origin => EntryFor(origin))], NameFor(origins[0]));
        foreach (var entry in catalog.Entries) configured[entry.Name] = entry;
        return catalog;
    }

    internal static EnvironmentCatalog For(params Uri[] origins) =>
        For([.. origins.Select(origin => origin.GetLeftPart(UriPartial.Authority))]);

    internal static EnvironmentCatalog Catalog(IReadOnlyList<EnvironmentEntry> entries, string? defaultEnvironment,
        IReadOnlyList<AdoptedEnvironment>? adopted = null) =>
        EnvironmentCatalog.Create(entries, defaultEnvironment, null, adopted ?? Bundled).WithAccess(TestAccess.Live);

    /// <summary>
    /// Exactly <paramref name="entries"/>, the first being the default, with the connection material the catalog
    /// itself derives from them: each entry's token file for exactly its origin.
    /// </summary>
    internal static EnvironmentCatalog Configured(params EnvironmentEntry[] entries) =>
        EnvironmentCatalog.Create(entries, entries[0].Name, null, Bundled);

    /// <summary>
    /// An entry at <paramref name="origin"/> with synthetic container, mounts and, unless given, token file under
    /// <c>/srv/broodling-test/&lt;name&gt;</c>.
    /// </summary>
    internal static EnvironmentEntry Entry(string name, string origin, string? controlTokenFile = null, string? image = null) =>
        new(name, origin, controlTokenFile ?? $"/srv/broodling-test/{name}/control-token", image ?? Adopted.Image,
            "broodling-test-" + name, $"/srv/broodling-test/{name}/state", $"/srv/broodling-test/{name}/home");

    /// <summary>
    /// The invocation configuration file contents for one environment at <paramref name="origin"/> with its real
    /// <paramref name="controlTokenFile"/>, the default unless <paramref name="isDefault"/> is false. Unless
    /// <paramref name="name"/> is given, it is the environment <see cref="For(string[])"/> configures there.
    /// </summary>
    internal static JsonObject Configuration(string origin, string controlTokenFile, string? rootCertificate = null,
        string? name = null, bool isDefault = true)
    {
        name ??= NameFor(origin);
        var entry = Entry(name, origin, controlTokenFile);
        var configuration = new JsonObject
        {
            ["environments"] = new JsonArray(new JsonObject
            {
                ["name"] = entry.Name, ["origin"] = entry.Origin, ["controlTokenFile"] = entry.ControlTokenFile,
                ["image"] = entry.Image, ["container"] = entry.Container, ["stateMount"] = entry.StateMount,
                ["homeMount"] = entry.HomeMount
            })
        };
        if (isDefault) configuration["defaultEnvironment"] = name;
        if (rootCertificate is not null) configuration["directRootCertificate"] = rootCertificate;
        return configuration;
    }

    internal static JsonObject Configuration(Uri origin, string controlTokenFile, string? rootCertificate = null) =>
        Configuration(origin.GetLeftPart(UriPartial.Authority), controlTokenFile, rootCertificate);
}
