using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The adoption record, <c>src/Broodling/environments/dotnet-r2.adoption.json</c>, and the producer manifest bytes
/// it retains: the release bundles exactly that adoption, the record's digest and image are the manifest's, the
/// manifest is one this revision supports, every component is fixed by an artifact checksum or the base image
/// digest, and the adopted image actually carries what the manifest states.
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
        await Assert.That(environment.Os).IsEqualTo((string)Manifest["os"]!);
        await Assert.That(environment.Components.DotnetSdks).IsEquivalentTo(["10.0.401"]);
    }

    /// <summary>
    /// The release carries exactly the dotnet-r2 adoption, as its committed bytes; nothing else is adopted. A
    /// change here is a new adoption decision, not a refactoring.
    /// </summary>
    [Test]
    public async Task ReleaseBundlesExactlyTheDotnetR2Adoption()
    {
        var bundled = AdoptedEnvironments.LoadBundled();
        await Assert.That(bundled.Select(environment => environment.Release)).IsEquivalentTo(["dotnet-r2"]);
        var adopted = bundled.Single();
        await Assert.That(adopted.RecordSha256).IsEqualTo("abdd75035c7503d7e53819c64d02e204822dec4e6882dc54d399866d04acdb63");
        await Assert.That(adopted.ManifestSha256).IsEqualTo("8d1168f339f599d1719dcdc4f4cae8c44966d3f642a3a7e6a3cf81f7e3c66f6e");
        await Assert.That(adopted.Record.SequenceEqual(File.ReadAllBytes(Adopted.RecordFile))).IsTrue();
        await Assert.That(adopted.Manifest.SequenceEqual(File.ReadAllBytes(Adopted.Manifest))).IsTrue();
        await Assert.That(adopted.Image).IsEqualTo(Adopted.Image);
        await Assert.That(Directory.GetFiles(Adopted.Directory).Select(file => Path.GetFileName(file)))
            .IsEquivalentTo(["dotnet-r2.adoption.json", "dotnet-r2.manifest.json"]);
    }

    /// <summary>
    /// An adoption whose manifest names another native, a record that names another image than its manifest, a
    /// manifest that is not the retained bytes and a repeated release are refused when the set is loaded.
    /// </summary>
    [Test]
    [Arguments("native")]
    [Arguments("discovery")]
    [Arguments("image")]
    [Arguments("digest")]
    [Arguments("repeated")]
    public async Task AdoptionSetRefusesWhatThisReleaseCannotUse(string defect)
    {
        var directory = Directory.CreateTempSubdirectory("broodling-adoption-").FullName;
        try
        {
            var manifest = Manifest;
            var record = Adopted.Record.DeepClone();
            switch (defect)
            {
                case "native": manifest["native"]!["version"] = "10.9.0"; break;
                case "discovery": manifest["startupInterfaces"]!["native"]!["discovery"]!["authentication"] = "none"; break;
                case "image": record["image"] = "ghcr.io/faviann/zeroshot-environments/dotnet@sha256:" + new string('0', 64); break;
            }
            byte[] bytes = defect == "digest" ? [.. File.ReadAllBytes(Adopted.Manifest), (byte)'\n'] : defect is "native" or "discovery"
                ? System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()) : File.ReadAllBytes(Adopted.Manifest);
            if (defect is "native" or "discovery") record["manifest"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.WriteAllBytes(Path.Combine(directory, "dotnet-r2.manifest.json"), bytes);
            File.WriteAllText(Path.Combine(directory, "dotnet-r2.adoption.json"), record.ToJsonString());
            if (defect == "repeated") File.WriteAllText(Path.Combine(directory, "again.adoption.json"), record.ToJsonString());
            await Assert.That(() => AdoptedEnvironments.Load(directory)).Throws<UnsupportedRuntime>();
        }
        finally { Directory.Delete(directory, recursive: true); }
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

        var probes = EnvironmentProbe.For(TargetEnvironment.Read(File.ReadAllBytes(Adopted.Manifest), Adopted.ManifestSha256));
        // Every stated version, the OS identity, the native, restic and entrypoint bytes and every Debian package.
        await Assert.That(probes.Select(probe => probe.Component)).IsEquivalentTo(["os", "native", "codex", "harness-node",
            "node", "gh", "git", "python", "claude", "copilot", "dotnet-sdks", "dotnet-runtimes"]);
        string[] expected =
        [
            .. probes.SelectMany(probe => probe.Expected),
            (string)manifest["native"]!["zeroshot"]!["sha256"]! + "  /usr/local/bin/zeroshot",
            (string)manifest["native"]!["restic"]!["sha256"]! + "  /usr/local/bin/restic",
            (string)manifest["integration"]!["broodling-entrypoint"]!["sha256"]! + "  /usr/local/bin/broodling-target",
            .. manifest["osPackages"]!.AsArray().Select(package =>
                $"{(string)package!["name"]!} {(string)package["version"]!} {(string)package["architecture"]!}")
        ];
        var observed = await DockerCommand("run", "--rm", "--network", "none", "--entrypoint", "/bin/sh", image, "-ec",
            EnvironmentProbe.Script(probes) + """

            sha256sum /usr/local/bin/zeroshot /usr/local/bin/restic /usr/local/bin/broodling-target
            dpkg-query --show --showformat '${Package} ${Version} ${Architecture}\n'
            """);
        RequireSuccess(observed);
        var lines = observed.Output.TrimEnd('\n').Split('\n');
        await Assert.That(string.Join('\n', expected.Except(lines))).IsEqualTo("").Because("stated but not observed");
        await Assert.That(string.Join('\n', lines.Except(expected))).IsEqualTo("").Because("observed but not stated");
        await Assert.That(lines.Length).IsEqualTo(expected.Length);
    }
}
