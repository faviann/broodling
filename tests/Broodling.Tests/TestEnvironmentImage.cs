using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Core;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The test-only second environment T of #235's two-target verification: the adopted dotnet-r4 image with only its OS
/// identity relabelled (<c>tests/fixtures/test-environment</c>), so native, Claude Code, the entrypoint and the DirectTarget
/// binding are unchanged while its producer-schema manifest states an incompatible OS. The run builds it once, pushes
/// it to a disposable loopback registry and pulls it back by digest, so it is pinned by a real image digest like a
/// published environment. Its manifest and its adoption record are generated here, in a directory this run owns,
/// and loaded with <see cref="AdoptedEnvironments.Load"/>: they are never committed, and production loads only the
/// bundled set. This is not an Execution asset approval; the approval and the binding stay untouched.
/// </summary>
internal static class TestEnvironmentImage
{
    /// <summary>The disposable registry, pinned by its multi-platform index digest.</summary>
    internal const string Registry = "registry:3@sha256:ddf754342cfc8acc51a56d5d0ab6af06826461864460636d8bd5c546dab2a7b8";
    internal const string Prefix = "broodling-235-";
    /// <summary>The name a test catalog gives T.</summary>
    internal const string Name = "t235";
    internal const string Release = Name + "-r1";
    internal const string Os = "Broodling Test OS 235";
    private static readonly string Directory = System.IO.Directory.CreateTempSubdirectory(Prefix + "adoption-").FullName;
    private static readonly Lazy<Task<AdoptedEnvironment>> adopted = new(CreateAsync);

    /// <summary>T, adopted through the same record and manifest rules as the bundled set.</summary>
    internal static Task<AdoptedEnvironment> Adopted => adopted.Value;

    /// <summary>The directory holding T's generated adoption record and manifest, once <see cref="Adopted"/> has completed.</summary>
    internal static async Task<string> DirectoryAsync()
    {
        await Adopted;
        return Directory;
    }

    /// <summary>The bundled adoption set plus T, as a test catalog may name them.</summary>
    internal static async Task<IReadOnlyList<AdoptedEnvironment>> SetAsync() => [.. TestEnvironments.Bundled, await Adopted];

    private static async Task<AdoptedEnvironment> CreateAsync()
    {
        var baseImage = await Direct.Value;
        var image = await PublishAsync(baseImage);
        var at = image.LastIndexOf('@');
        var bundled = TestEnvironments.Adopted;
        var manifest = JsonNode.Parse(bundled.Manifest)!.AsObject();
        manifest["environment"] = new JsonObject { ["release"] = "r1", ["name"] = Name };
        manifest["image"] = new JsonObject { ["digest"] = image[(at + 1)..], ["repository"] = image[..at] };
        // T's base is the adopted image itself, which fixes every component T does not relabel.
        manifest["base"] = new JsonObject
        {
            ["revision"] = manifest["recipe"]!["revision"]!.DeepClone(), ["reference"] = bundled.Image,
            ["digest"] = bundled.Image[(bundled.Image.LastIndexOf('@') + 1)..]
        };
        manifest["recipe"] = new JsonObject
        {
            ["repository"] = "https://github.com/faviann/broodling",
            ["revision"] = AttemptFixture.RunGit(TestRepository.Root, "rev-parse", "HEAD").Trim(),
            ["path"] = "tests/fixtures/test-environment"
        };
        manifest["os"] = Os;
        // Only this suite qualifies T; there is no producer qualification run.
        manifest.Remove("qualification");
        var bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        var record = new JsonObject
        {
            ["kind"] = AdoptedEnvironments.Kind,
            ["environment"] = Release,
            ["producer"] = new JsonObject
            {
                ["repository"] = "https://github.com/faviann/broodling", ["path"] = "tests/fixtures/test-environment"
            },
            ["manifest"] = new JsonObject { ["path"] = Release + ".manifest.json", ["sha256"] = Digests.Bytes(bytes) },
            ["image"] = image,
            ["adoption"] = "test-only (#235 question 3): never published or approved for real work"
        };
        File.WriteAllBytes(Path.Combine(Directory, Release + ".manifest.json"), bytes);
        File.WriteAllText(Path.Combine(Directory, Release + ".adoption.json"),
            record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return AdoptedEnvironments.Load(Directory).Single();
    }

    /// <summary>
    /// Build T over <paramref name="baseImage"/>, push it to a disposable registry on a host-loopback port Docker
    /// chooses, and pull it back by the pushed digest with no tag left, then remove the registry. Returns the digest
    /// reference, which this run removes afterwards with its other images.
    /// </summary>
    private static async Task<string> PublishAsync(string baseImage)
    {
        var registry = Prefix + "registry-" + Guid.NewGuid().ToString("N");
        try
        {
            RequireSuccess(await DockerCommand("run", "--detach", "--name", registry, "--publish", "127.0.0.1::5000",
                await Pinned(Registry)));
            var port = await DockerCommand("port", registry, "5000/tcp");
            RequireSuccess(port);
            var repository = port.Output.Trim().Split('\n')[0] + "/" + Prefix + "env";
            var tag = repository + ":" + Guid.NewGuid().ToString("N");
            RequireSuccess(await DockerCommand("build", "--quiet", "--tag", tag, "--build-arg", "BASE=" + baseImage,
                Path.Combine(TestRepository.Root, "tests", "fixtures", "test-environment")));
            try
            {
                Result pushed = new(1, "", "");
                // The registry may still be starting.
                for (var retry = 0; retry < 50 && (pushed = await DockerCommand("push", "--quiet", tag)).Code != 0; retry++)
                    await Task.Delay(200);
                RequireSuccess(pushed);
                var digests = await DockerCommand("image", "inspect", "--format", "{{json .RepoDigests}}", tag);
                RequireSuccess(digests);
                var pinned = JsonNode.Parse(digests.Output)!.AsArray().Select(digest => (string)digest!)
                    .Single(digest => digest.StartsWith(repository + "@sha256:", StringComparison.Ordinal));
                RequireSuccess(await DockerCommand("image", "rm", tag));
                RequireSuccess(await DockerCommand("pull", "--quiet", pinned));
                Owned(pinned);
                return pinned;
            }
            finally { await DockerCommand("image", "rm", tag); }
        }
        finally { await DockerCommand("rm", "--force", "--volumes", registry); }
    }

    [After(Assembly)]
    public static void RemoveDirectory() => System.IO.Directory.Delete(Directory, recursive: true);
}
