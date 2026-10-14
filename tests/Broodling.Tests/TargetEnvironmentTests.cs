using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The adoption record, <c>deployment/target-environment.json</c>, and the producer manifest bytes it retains:
/// the record's digest and image are the manifest's, the manifest is one this revision supports, every
/// component is fixed by an artifact checksum or the base image digest, and the adopted image actually
/// carries what the manifest states.
/// </summary>
public sealed class TargetEnvironmentTests
{
    private static JsonNode Manifest => JsonNode.Parse(File.ReadAllBytes(Adopted.Manifest))!;

    [Test]
    public async Task AdoptionRecordRetainsTheSupportedProducerManifest()
    {
        await Assert.That((string)Adopted.Record["kind"]!).IsEqualTo("broodling.target-environment-adoption/v1");
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Adopted.Manifest)))).IsEqualTo(Adopted.ManifestSha256);
        var environment = TargetEnvironment.Read(File.ReadAllBytes(Adopted.Manifest), Adopted.ManifestSha256);
        await Assert.That(environment.Image).IsEqualTo(Adopted.Image);
        await Assert.That(environment.Release).IsEqualTo(Adopted.Release);
        // The environment release is its own identity, apart from the native release it carries.
        await Assert.That(Adopted.Release).IsNotEqualTo(DirectTargetBinding.NativeRelease);
        await Assert.That(Adopted.Release).IsEqualTo("dotnet-r2");
    }

    /// <summary>
    /// The manifest identifies its recipe and base, and fixes every component by exact version and either the
    /// checksums of its downloaded artifacts or the base image digest: nothing is installed at run time.
    /// </summary>
    [Test]
    public async Task EveryComponentIsFixedByAnArtifactChecksumOrTheBaseDigest()
    {
        var manifest = Manifest;
        static bool Sha256(JsonNode? value) => value is not null && Regex.IsMatch((string)value!, "^[0-9a-f]{64}$");
        await Assert.That(Regex.IsMatch((string)manifest["recipe"]!["revision"]!, "^[0-9a-f]{40}$")).IsTrue();
        await Assert.That(Regex.IsMatch((string)manifest["base"]!["digest"]!, "^sha256:[0-9a-f]{64}$")).IsTrue();
        await Assert.That((string)manifest["base"]!["reference"]!).EndsWith("@" + (string)manifest["base"]!["digest"]!);
        await Assert.That((string)manifest["os"]!).IsNotEmpty();
        List<(string Name, JsonNode Component)> components =
        [
            ("native", manifest["native"]!), ("dotnet", manifest["dotnet"]!),
            .. manifest["integration"]!.AsObject().Select(entry => ("integration " + entry.Key, entry.Value!)),
            .. manifest["harnesses"]!.AsObject().Select(entry => ("harness " + entry.Key, entry.Value!)),
            .. manifest["tools"]!.AsObject().Select(entry => ("tool " + entry.Key, entry.Value!)),
            .. manifest["osPackages"]!.AsArray().Select(package => ("package " + (string)package!["name"]!, package!))
        ];
        await Assert.That(components.Count).IsGreaterThan(150);
        foreach (var (name, component) in components)
        {
            switch ((string?)component["fixedBy"])
            {
                case "base": break;
                case "artifacts":
                    var artifacts = component["artifacts"]!.AsObject();
                    await Assert.That(artifacts.Count).IsGreaterThan(0).Because(name);
                    foreach (var (artifact, value) in artifacts)
                        await Assert.That(Sha256(value!["sha256"])).IsTrue().Because(name + " " + artifact);
                    break;
                default: throw new InvalidOperationException(name + " names no fixing digest.");
            }
            var exact = component["version"] ?? component["sdks"] ?? component["sha256"];
            await Assert.That(exact is not null && exact.ToJsonString() is not ("\"\"" or "[]")).IsTrue().Because(name);
        }
        await Assert.That(Sha256(manifest["native"]!["zeroshot"]!["sha256"]) && Sha256(manifest["native"]!["restic"]!["sha256"])).IsTrue();
    }

    /// <summary>
    /// The entrypoint is Broodling's file. The adopted image embeds this revision's copy, verified by SHA-256 from an
    /// exact Broodling commit; an entrypoint change is adopted only with an environment release that carries it.
    /// </summary>
    [Test]
    public async Task AdoptedImageEmbedsThisRevisionsEntrypoint()
    {
        var entrypoint = Manifest["integration"]!["broodling-entrypoint"]!;
        var bytes = File.ReadAllBytes(Path.Combine(TestRepository.Root, "deployment", "direct-target-entrypoint.sh"));
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqualTo((string)entrypoint["sha256"]!);
        await Assert.That((string)entrypoint["source"]!["path"]!).IsEqualTo("deployment/direct-target-entrypoint.sh");
        await Assert.That(Regex.IsMatch((string)entrypoint["source"]!["revision"]!, "^[0-9a-f]{40}$")).IsTrue();
        await Assert.That((string)Manifest["startupInterfaces"]!["entrypoint"]!["version"]!).IsEqualTo(TargetEnvironment.StartupInterface);
    }

    /// <summary>
    /// Broodling's own qualification of the image bytes by digest: the pulled image is the manifest's platform and
    /// digest, and its native, restic and entrypoint bytes, harnesses, Node.js, gh, Git, .NET and every Debian
    /// package are those the manifest states.
    /// </summary>
    [Test]
    public async Task AdoptedImageCarriesWhatItsManifestStates()
    {
        var manifest = Manifest;
        var image = await Direct.Value;
        var inspected = await DockerCommand("image", "inspect", "--format", "{{.Os}}/{{.Architecture}} {{json .RepoDigests}}", image);
        RequireSuccess(inspected);
        var platform = inspected.Output.Trim().Split(' ', 2);
        await Assert.That(platform[0]).IsEqualTo((string)manifest["platform"]!);
        await Assert.That(JsonNode.Parse(platform[1])!.AsArray().Select(digest => (string)digest!)).Contains(Adopted.Image);

        string Version(string tool) => (string)manifest["tools"]![tool]!["version"]!;
        string Harness(string harness) => (string)manifest["harnesses"]![harness]!["version"]!;
        string[] expected =
        [
            "zeroshot " + (string)manifest["native"]!["version"]!,
            (string)manifest["native"]!["zeroshot"]!["sha256"]! + "  /usr/local/bin/zeroshot",
            (string)manifest["native"]!["restic"]!["sha256"]! + "  /usr/local/bin/restic",
            (string)manifest["integration"]!["broodling-entrypoint"]!["sha256"]! + "  /usr/local/bin/broodling-target",
            "codex-cli " + Harness("codex"),
            Harness("claude") + " (Claude Code)",
            "GitHub Copilot CLI " + Harness("copilot") + ".",
            Version("node"), Version("harness-node"), Version("gh"), Version("git"), Version("python"),
            .. manifest["dotnet"]!["sdks"]!.AsArray().Select(sdk => (string)sdk!),
            .. manifest["dotnet"]!["runtimes"]!.AsArray().Select(runtime => (string)runtime!),
            "PRETTY_NAME=\"" + (string)manifest["os"]! + "\"",
            .. manifest["osPackages"]!.AsArray().Select(package =>
                $"{(string)package!["name"]!} {(string)package["version"]!} {(string)package["architecture"]!}")
        ];
        var observed = await DockerCommand("run", "--rm", "--network", "none", "--entrypoint", "/bin/sh", image, "-ec", """
            zeroshot --version
            sha256sum /usr/local/bin/zeroshot /usr/local/bin/restic /usr/local/bin/broodling-target
            codex --version 2>/dev/null
            claude --version
            copilot --version | head -n 1
            node --version
            /opt/zeroshot/bin/node --version
            gh --version | head -n 1
            git --version
            python3 --version
            dotnet --list-sdks | cut -d ' ' -f 1
            dotnet --list-runtimes | cut -d ' ' -f 1,2
            grep '^PRETTY_NAME=' /etc/os-release
            dpkg-query --show --showformat '${Package} ${Version} ${Architecture}\n'
            """);
        RequireSuccess(observed);
        var lines = observed.Output.TrimEnd('\n').Split('\n');
        await Assert.That(string.Join('\n', expected.Except(lines))).IsEqualTo("").Because("stated but not observed");
        await Assert.That(string.Join('\n', lines.Except(expected))).IsEqualTo("").Because("observed but not stated");
        await Assert.That(lines.Length).IsEqualTo(expected.Length);
    }
}
