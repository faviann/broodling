using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>How a Replacement Attempt came by its environment: from <paramref name="Predecessor"/>'s resolution.</summary>
internal sealed record EnvironmentReplacement(string Predecessor, string Mode, string PreviousSha256);

/// <summary>
/// One Attempt's environment, fixed once at first preparation and retained with its Prepared submission: the
/// requirement, its resolution to an adopted environment and the target serving it, and for a Replacement where
/// the resolution came from. Secret-free: no control token or token file. <see cref="Json"/> is the retained
/// form; <see cref="Sha256"/> identifies the resolution alone, so a successor that inherits it keeps it.
/// </summary>
internal sealed record EnvironmentResolution(EnvironmentRequirement Requirement, AdoptedEnvironment Adoption, TargetIdentity Target,
    EnvironmentReplacement? Replacement = null)
{
    internal string Name => Target.Environment;

    internal string Sha256 => Digests.Bytes(Encoding.UTF8.GetBytes(Resolution().ToJsonString()));

    /// <summary>The documents the resolution names by digest: adoption record, producer manifest and any declaration.</summary>
    internal IEnumerable<byte[]> Documents =>
        Requirement.Declaration is { } declaration ? [Adoption.Record, Adoption.Manifest, declaration.Bytes] : [Adoption.Record, Adoption.Manifest];

    internal JsonObject Json() => new()
    {
        ["requirement"] = new JsonObject
        {
            ["name"] = Requirement.Name,
            ["declaration"] = Requirement.Declaration is { } declaration
                ? new JsonObject
                {
                    ["path"] = declaration.Path, ["commit"] = declaration.Commit, ["blob"] = declaration.BlobOid,
                    ["sha256"] = declaration.Sha256
                }
                : null
        },
        ["resolution"] = Resolution(),
        ["replacement"] = Replacement is { } replacement
            ? new JsonObject
            {
                ["predecessor"] = replacement.Predecessor, ["mode"] = replacement.Mode, ["previousSha256"] = replacement.PreviousSha256
            }
            : null
    };

    private JsonObject Resolution()
    {
        var environment = Adoption.Environment;
        var components = environment.Components;
        var stated = new JsonObject { ["native"] = components.Native, ["codex"] = components.Codex };
        if (components.Claude is not null) stated["claude"] = components.Claude;
        if (components.Copilot is not null) stated["copilot"] = components.Copilot;
        stated["harness-node"] = components.HarnessNode;
        stated["node"] = components.Node;
        stated["gh"] = components.Gh;
        stated["git"] = components.Git;
        stated["python"] = components.Python;
        stated["dotnetSdks"] = new JsonArray([.. components.DotnetSdks.Select(sdk => JsonValue.Create(sdk))]);
        stated["dotnetRuntimes"] = new JsonArray([.. components.DotnetRuntimes.Select(runtime => JsonValue.Create(runtime))]);
        return new()
        {
            ["basis"] = Requirement.Declaration is null ? "default" : "declared",
            ["name"] = Name,
            ["adoption"] = new JsonObject
            {
                ["kind"] = AdoptedEnvironments.Kind, ["release"] = Adoption.Release, ["sha256"] = Adoption.RecordSha256
            },
            ["manifest"] = new JsonObject { ["schema"] = TargetEnvironment.Schema, ["sha256"] = Adoption.ManifestSha256 },
            ["image"] = Adoption.Image, ["platform"] = environment.Platform, ["os"] = environment.Os,
            ["components"] = stated,
            ["target"] = new JsonObject
            {
                ["environment"] = Target.Environment, ["origin"] = Target.Origin, ["container"] = Target.Container,
                ["stateMount"] = Target.StateMount, ["homeMount"] = Target.HomeMount
            }
        };
    }

    /// <summary>
    /// The resolution a retained <paramref name="environment"/> states, rebuilt from it and the retained documents
    /// <paramref name="document"/> returns by digest, never from a repository, catalog or registry. Null when it is
    /// not the form <see cref="Json"/> writes; <see cref="UnsupportedRuntime"/> when this release cannot use the
    /// retained adoption.
    /// </summary>
    internal static EnvironmentResolution? Read(JsonNode? environment, Func<string, byte[]?> document)
    {
        try
        {
            var requirement = environment!["requirement"]!;
            DeclarationSource? source = null;
            if (requirement["declaration"] is JsonObject declaration)
            {
                var sha256 = Text(declaration["sha256"]);
                source = new(Text(declaration["path"]), Text(declaration["commit"]), Text(declaration["blob"]), sha256,
                    document(sha256) ?? throw new InvalidOperationException());
            }
            var resolution = environment["resolution"]!;
            var record = document(Text(resolution["adoption"]!["sha256"])) ?? throw new InvalidOperationException();
            var manifest = document(Text(resolution["manifest"]!["sha256"])) ?? throw new InvalidOperationException();
            var target = resolution["target"]!;
            var replacement = environment["replacement"] is JsonObject lineage
                ? new EnvironmentReplacement(Text(lineage["predecessor"]), Text(lineage["mode"]), Text(lineage["previousSha256"]))
                : null;
            var result = new EnvironmentResolution(
                new(source is null ? null : EnvironmentDeclaration.Parse(source.Bytes), source),
                AdoptedEnvironments.Parse(record, _ => manifest, "retained"),
                new(Text(target["environment"]), Text(target["origin"]), Text(target["container"]), Text(target["stateMount"]),
                    Text(target["homeMount"])),
                replacement);
            return JsonNode.DeepEquals(result.Json(), environment) ? result : null;
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException or JsonException
            or EnvironmentRefused) { return null; }
    }

    private static string Text(JsonNode? node) =>
        (node as JsonValue)?.TryGetValue<string>(out var text) == true ? text : throw new InvalidOperationException();
}
