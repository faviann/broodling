using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>
/// One Attempt's environment, fixed once at first preparation and retained with its Prepared submission: the
/// requirement and its resolution to an adopted environment and the target serving it. Secret-free: no control
/// token or token file. <see cref="Json"/> is the retained form; <see cref="Sha256"/> identifies the resolution
/// alone, so a successor that inherits it keeps it.
/// </summary>
internal sealed record EnvironmentResolution(EnvironmentRequirement Requirement, AdoptedEnvironment Adoption, TargetIdentity Target)
{
    internal string Name => Target.Environment;

    internal string Sha256 => Digests.Bytes(Encoding.UTF8.GetBytes(Resolution().ToJsonString()));

    /// <summary>The documents the resolution names by digest: adoption record, producer manifest and any declaration.</summary>
    internal IEnumerable<byte[]> Documents =>
        Requirement.Declaration is { } declaration ? [Adoption.Record, Adoption.Manifest, declaration.Bytes] : [Adoption.Record, Adoption.Manifest];

    internal JsonObject Json() => new()
    {
        ["requirement"] = RequirementJson(Requirement),
        ["resolution"] = Resolution()
    };

    private static JsonObject RequirementJson(EnvironmentRequirement requirement) => new()
    {
        ["name"] = requirement.Name,
        ["declaration"] = requirement.Declaration is { } declaration
            ? new JsonObject
            {
                ["path"] = declaration.Path, ["commit"] = declaration.Commit, ["blob"] = declaration.BlobOid,
                ["sha256"] = declaration.Sha256
            }
            : null
    };

    private JsonObject Resolution()
    {
        var environment = Adoption.Environment;
        return new()
        {
            ["basis"] = Requirement.Declaration is null ? "default" : "declared",
            ["name"] = Name,
            ["adoption"] = new JsonObject
            {
                ["kind"] = AdoptedEnvironments.Kind, ["release"] = Adoption.Release, ["sha256"] = Adoption.RecordSha256
            },
            ["manifest"] = new JsonObject { ["schema"] = TargetEnvironment.Schema, ["sha256"] = Adoption.ManifestSha256 },
            ["image"] = Adoption.Image, ["platform"] = TargetEnvironment.SupportedPlatform, ["os"] = environment.Os,
            ["components"] = new JsonObject(environment.Components.Select(component => KeyValuePair.Create<string, JsonNode?>(
                component.Key, new JsonArray([.. component.Value.Select(version => JsonValue.Create(version))])))),
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
            var requirement = ReadRequirement(environment!["requirement"], document) ?? throw new InvalidOperationException();
            var resolution = environment["resolution"]!;
            var record = document(Text(resolution["adoption"]!["sha256"])) ?? throw new InvalidOperationException();
            var manifest = document(Text(resolution["manifest"]!["sha256"])) ?? throw new InvalidOperationException();
            var target = resolution["target"]!;
            var result = new EnvironmentResolution(requirement,
                AdoptedEnvironments.Parse(record, _ => manifest, "retained"),
                new(Text(target["environment"]), Text(target["origin"]), Text(target["container"]), Text(target["stateMount"]),
                    Text(target["homeMount"])));
            return JsonNode.DeepEquals(result.Json(), environment) ? result : null;
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException or JsonException
            or EnvironmentRefused) { return null; }
    }

    /// <summary>
    /// The requirement a retained <paramref name="requirement"/> states, with its declaration bytes from
    /// <paramref name="document"/>; null when it is not the form <see cref="Json"/> writes. It needs no adoption, so
    /// an explicit environment update can still satisfy it when this release cannot use the retained resolution.
    /// </summary>
    internal static EnvironmentRequirement? ReadRequirement(JsonNode? requirement, Func<string, byte[]?> document)
    {
        try
        {
            DeclarationSource? source = null;
            if (requirement!["declaration"] is JsonObject declaration)
            {
                var sha256 = Text(declaration["sha256"]);
                source = new(Text(declaration["path"]), Text(declaration["commit"]), Text(declaration["blob"]), sha256,
                    document(sha256) ?? throw new InvalidOperationException());
            }
            var result = new EnvironmentRequirement(source is null ? null : EnvironmentDeclaration.Parse(source.Bytes), source);
            return JsonNode.DeepEquals(RequirementJson(result), requirement) ? result : null;
        }
        catch (Exception error) when (error is InvalidOperationException or NullReferenceException or JsonException
            or EnvironmentRefused) { return null; }
    }

    private static string Text(JsonNode? node) =>
        (node as JsonValue)?.TryGetValue<string>(out var text) == true ? text : throw new InvalidOperationException();
}

/// <summary>
/// The retained environment's identity as an Attempt's binding names it, for checks against the current catalog and
/// for inspection: the environment name, adopted image and documents, and the target serving it.
/// </summary>
public sealed record RetainedEnvironment(string Name, string Image, string AdoptionSha256, string ManifestSha256, TargetIdentity Target)
{
    internal static RetainedEnvironment Read(string bindingJson)
    {
        var resolution = JsonNode.Parse(bindingJson)!["environment"]!["resolution"]!;
        var target = resolution["target"]!;
        return new((string)resolution["name"]!, (string)resolution["image"]!, (string)resolution["adoption"]!["sha256"]!,
            (string)resolution["manifest"]!["sha256"]!,
            new((string)target["environment"]!, (string)target["origin"]!, (string)target["container"]!,
                (string)target["stateMount"]!, (string)target["homeMount"]!));
    }
}
