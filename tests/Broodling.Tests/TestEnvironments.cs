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

    internal static IReadOnlyList<AdoptedEnvironment> Bundled => bundled.Value;

    /// <summary>The bundled dotnet-r2 adoption, which every test environment names unless it says otherwise.</summary>
    internal static AdoptedEnvironment Adopted => Bundled.Single();

    /// <summary>No environment: connection material for every live test target, and no new Attempt can be prepared.</summary>
    internal static EnvironmentCatalog Live { get; } = Catalog([], null);

    /// <summary>Like <see cref="Live"/>, trusting <paramref name="rootCertificate"/> (null: system trust).</summary>
    internal static EnvironmentCatalog Trusting(string? rootCertificate, params string[] origins) =>
        For(origins).WithAccess(TestAccess.Trusting(rootCertificate));

    /// <summary>
    /// One environment per origin, named <c>env0</c>, <c>env1</c> and so on, the first being the default; with no
    /// origin, the same as <see cref="Live"/>.
    /// </summary>
    internal static EnvironmentCatalog For(params string[] origins) =>
        Catalog([.. origins.Select((origin, index) => Entry("env" + index, origin))], origins.Length == 0 ? null : "env0");

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
    /// <paramref name="controlTokenFile"/>, the default unless <paramref name="isDefault"/> is false.
    /// </summary>
    internal static JsonObject Configuration(string origin, string controlTokenFile, string? rootCertificate = null,
        string name = "dotnet", bool isDefault = true)
    {
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
