using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The adoption record, <c>src/Broodling/environments/dotnet-r6.adoption.json</c>, and the producer manifest bytes
/// it retains: the release bundles exactly that adoption, the record's digest and image are the manifest's, the
/// manifest is one this revision supports, every component is fixed by an artifact checksum or the base image
/// digest, and the adopted image actually carries what the manifest states. The test-only environment T
/// (<see cref="TestEnvironmentImage"/>) is held to the same record, manifest and image checks.
/// </summary>
public sealed class TargetEnvironmentTests
{
    private static JsonNode Manifest => JsonNode.Parse(File.ReadAllBytes(Adopted.Manifest))!;

    /// <summary>One adoption record under test, in the directory holding it and the manifest it retains.</summary>
    private sealed record Adoption(string Release, string Directory)
    {
        internal JsonNode Record { get; } = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Directory, Release + ".adoption.json")))!;
        internal string Image => (string)Record["image"]!;
        internal string ManifestSha256 => (string)Record["manifest"]!["sha256"]!;
        internal byte[] ManifestBytes => File.ReadAllBytes(Path.Combine(Directory, (string)Record["manifest"]!["path"]!));
        internal JsonNode Manifest => JsonNode.Parse(ManifestBytes)!;
    }

    /// <summary>The committed dotnet-r6 adoption, or T's, generated for this run.</summary>
    private static async Task<Adoption> Subject(string release) => release == TestEnvironmentImage.Release
        ? new(release, await TestEnvironmentImage.DirectoryAsync()) : new(release, Adopted.Directory);

    [Test]
    [Arguments("dotnet-r6")]
    [Arguments(TestEnvironmentImage.Release)]
    public async Task AdoptionRecordRetainsTheSupportedProducerManifest(string release)
    {
        var adoption = await Subject(release);
        await Assert.That((string)adoption.Record["kind"]!).IsEqualTo("broodling.target-environment-adoption/v1");
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(adoption.ManifestBytes))).IsEqualTo(adoption.ManifestSha256);
        var environment = TargetEnvironment.Read(adoption.ManifestBytes, adoption.ManifestSha256);
        await Assert.That(environment.Image).IsEqualTo(adoption.Image);
        await Assert.That(environment.Release).IsEqualTo((string)adoption.Record["environment"]!);
        await Assert.That(environment.Release).IsEqualTo(release);
        // The environment release is its own identity, apart from the native release it carries.
        await Assert.That(environment.Release).IsNotEqualTo(DirectTargetBinding.NativeRelease);
        await Assert.That(environment.Os).IsEqualTo((string)adoption.Manifest["os"]!);
        await Assert.That(environment.Components["dotnet-sdks"]).IsEquivalentTo(["10.0.401"]);
        // The adoption set loader accepts it under the same rules.
        await Assert.That(AdoptedEnvironments.Load(adoption.Directory).Select(adopted => adopted.Release)).IsEquivalentTo([release]);
    }

    /// <summary>
    /// Claude Code runs without its managed settings, and says nothing, when the file is missing. The adopted
    /// manifest states the file the approval reviewed, so adoption and readiness can hold the image to it.
    /// </summary>
    [Test]
    [Arguments("dotnet-r6")]
    [Arguments(TestEnvironmentImage.Release)]
    public async Task AdoptedClaudeCodeCarriesTheApprovedManagedSettings(string release)
    {
        var claude = (await Subject(release)).Manifest["harnesses"]!["claude"]!;
        await Assert.That((string)claude["version"]!).IsEqualTo("2.1.276");
        await Assert.That(claude["managedSettings"]!.ToJsonString()).IsEqualTo(
            """{"path":"/etc/claude-code/managed-settings.json","sha256":"a469a57a1f61686f006df05d41973208f0bd9f81de219db17f4034882b038f2e"}""");
    }

    /// <summary>
    /// T differs from dotnet-r6 only in its identity and its stated OS: the same native, Claude Code, startup interfaces,
    /// components and packages, so the DirectTarget binding it is adopted under is the bundled one.
    /// </summary>
    [Test]
    public async Task TestEnvironmentDiffersFromTheAdoptedOneOnlyInIdentityAndOs()
    {
        var test = (await Subject(TestEnvironmentImage.Release)).Manifest.AsObject();
        var adopted = Manifest.AsObject();
        await Assert.That((string)test["os"]!).IsEqualTo(TestEnvironmentImage.Os);
        await Assert.That((string)test["os"]!).IsNotEqualTo((string)adopted["os"]!);
        await Assert.That(string.Join(",", adopted.Select(member => member.Key)
                .Where(name => !JsonNode.DeepEquals(adopted[name], test[name])).Order()))
            .IsEqualTo("base,environment,image,os,qualification,recipe");
        await Assert.That(string.Join(",", test.Select(member => member.Key).Except(adopted.Select(member => member.Key)))).IsEqualTo("");
        await Assert.That(test.ContainsKey("qualification")).IsFalse();
        await Assert.That((string)test["base"]!["reference"]!).IsEqualTo(Adopted.Image);
    }

    /// <summary>
    /// The release carries exactly the dotnet-r6 adoption, as its committed bytes; nothing else is adopted. A
    /// change here is a new adoption decision, not a refactoring.
    /// </summary>
    [Test]
    public async Task ReleaseBundlesExactlyTheAdoptedEnvironment()
    {
        var bundled = AdoptedEnvironments.LoadBundled();
        await Assert.That(bundled.Select(environment => environment.Release)).IsEquivalentTo(["dotnet-r6"]);
        var adopted = bundled.Single();
        await Assert.That(adopted.RecordSha256).IsEqualTo("48f7d2d8b8f2d7da0e624aa57934d9ffaab43300746204cafc5f09c46acde326");
        await Assert.That(adopted.ManifestSha256).IsEqualTo("304a417ca752e22652b66f3de47a5995247a45339ccd9fad8221f8f0b29fb89b");
        await Assert.That(adopted.Record.SequenceEqual(File.ReadAllBytes(Adopted.RecordFile))).IsTrue();
        await Assert.That(adopted.Manifest.SequenceEqual(File.ReadAllBytes(Adopted.Manifest))).IsTrue();
        await Assert.That(adopted.Image).IsEqualTo(Adopted.Image);
        await Assert.That(Directory.GetFiles(Adopted.Directory).Select(file => Path.GetFileName(file)))
            .IsEquivalentTo(["dotnet-r6.adoption.json", "dotnet-r6.manifest.json"]);
    }

    /// <summary>
    /// An adoption whose manifest names another native, Claude Code or managed settings or states a component readiness
    /// cannot probe, a record that names another image than its manifest, a manifest that is not the retained bytes
    /// and a repeated release are refused when the set is loaded.
    /// </summary>
    [Test]
    [Arguments("native")]
    [Arguments("claude")]
    [Arguments("unmanaged")]
    [Arguments("managed-settings")]
    [Arguments("discovery")]
    [Arguments("unprobed")]
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
                case "claude": manifest["harnesses"]!["claude"]!["version"] = "2.1.237"; break;
                case "unmanaged": manifest["harnesses"]!["claude"]!.AsObject().Remove("managedSettings"); break;
                case "managed-settings": manifest["harnesses"]!["claude"]!["managedSettings"]!["sha256"] = new string('0', 64); break;
                case "discovery": manifest["startupInterfaces"]!["native"]!["discovery"]!["authentication"] = "none"; break;
                case "unprobed": manifest["tools"]!["go"] = new JsonObject { ["version"] = "go1.25.0", ["fixedBy"] = "base" }; break;
                case "image": record["image"] = "ghcr.io/faviann/zeroshot-environments/dotnet@sha256:" + new string('0', 64); break;
            }
            var edited = defect is not ("image" or "digest" or "repeated");
            byte[] bytes = defect == "digest" ? [.. File.ReadAllBytes(Adopted.Manifest), (byte)'\n'] : edited
                ? System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()) : File.ReadAllBytes(Adopted.Manifest);
            if (edited) record["manifest"]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.WriteAllBytes(Path.Combine(directory, "dotnet-r6.manifest.json"), bytes);
            File.WriteAllText(Path.Combine(directory, "dotnet-r6.adoption.json"), record.ToJsonString());
            if (defect == "repeated") File.WriteAllText(Path.Combine(directory, "again.adoption.json"), record.ToJsonString());
            var refused = await Assert.That(() => AdoptedEnvironments.Load(directory)).Throws<UnsupportedRuntime>();
            if (defect == "unprobed") await Assert.That(refused!.Message).Contains("states go, which readiness cannot probe");
            if (defect == "claude") await Assert.That(refused!.Message).Contains("Claude Code differs from the approved harness");
            if (defect is "unmanaged" or "managed-settings")
                await Assert.That(refused!.Message).Contains("Claude Code managed settings differ from the approved ones");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>
    /// The manifest identifies its recipe and base, and fixes every component by exact version and either the
    /// checksums of its downloaded artifacts or the base image digest: nothing is installed at run time.
    /// </summary>
    [Test]
    [Arguments("dotnet-r6")]
    [Arguments(TestEnvironmentImage.Release)]
    public async Task EveryComponentIsFixedByAnArtifactChecksumOrTheBaseDigest(string release)
    {
        var manifest = (await Subject(release)).Manifest;
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
    [Arguments("dotnet-r6")]
    [Arguments(TestEnvironmentImage.Release)]
    public async Task AdoptedImageEmbedsThisRevisionsEntrypoint(string release)
    {
        var manifest = (await Subject(release)).Manifest;
        var entrypoint = manifest["integration"]!["broodling-entrypoint"]!;
        var bytes = File.ReadAllBytes(Path.Combine(TestRepository.Root, "deployment", "direct-target-entrypoint.sh"));
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqualTo((string)entrypoint["sha256"]!);
        await Assert.That((string)entrypoint["source"]!["path"]!).IsEqualTo("deployment/direct-target-entrypoint.sh");
        await Assert.That(Regex.IsMatch((string)entrypoint["source"]!["revision"]!, "^[0-9a-f]{40}$")).IsTrue();
        await Assert.That((string)manifest["startupInterfaces"]!["entrypoint"]!["version"]!).IsEqualTo(TargetEnvironment.StartupInterface);
    }

    /// <summary>
    /// Broodling's own qualification of the image bytes by digest: the pulled image is the manifest's platform and
    /// digest, and its native, restic and entrypoint bytes, the Claude Code managed settings as native's first agent
    /// identity reads them, every component version and every Debian package are those the manifest states. The
    /// managed settings directory holds only that file, because Claude Code also applies drop-ins and a CLAUDE.md there.
    /// Root owns the directory and the regular file, at modes 0755 and 0644, so no agent can rewrite the settings.
    /// </summary>
    [Test]
    [Arguments("dotnet-r6")]
    [Arguments(TestEnvironmentImage.Release)]
    public async Task AdoptedImageCarriesWhatItsManifestStates(string release)
    {
        var adoption = await Subject(release);
        var manifest = adoption.Manifest;
        var image = release == TestEnvironmentImage.Release ? adoption.Image : await Direct.Value;
        var inspected = await DockerCommand("image", "inspect", "--format", "{{.Os}}/{{.Architecture}} {{json .RepoDigests}}", image);
        RequireSuccess(inspected);
        var platform = inspected.Output.Trim().Split(' ', 2);
        await Assert.That(platform[0]).IsEqualTo((string)manifest["platform"]!);
        await Assert.That(JsonNode.Parse(platform[1])!.AsArray().Select(digest => (string)digest!)).Contains(adoption.Image);

        var probes = EnvironmentProbe.For(TargetEnvironment.Read(adoption.ManifestBytes, adoption.ManifestSha256));
        // Every stated version, the OS identity, the native, restic and entrypoint bytes and every Debian package.
        await Assert.That(probes.Select(probe => probe.Component)).IsEquivalentTo(["os", "native", "codex", "claude", "copilot",
            "node", "harness-node", "npm", "yarn", "corepack", "python", "pip", "rust", "rustup", "rust-components", "gcc", "git",
            "gh", "docker-cli", "dotnet-sdks", "dotnet-runtimes"]);
        var probed = await DockerCommand("run", "--rm", "--network", "none", "--entrypoint", "/bin/sh", image, "-c",
            EnvironmentProbe.Sections(probes));
        RequireSuccess(probed);
        await Assert.That(EnvironmentProbe.Mismatches(probes, probed.Output)).IsEmpty();
        string[] expected =
        [
            (string)manifest["native"]!["zeroshot"]!["sha256"]! + "  /usr/local/bin/zeroshot",
            (string)manifest["native"]!["restic"]!["sha256"]! + "  /usr/local/bin/restic",
            (string)manifest["integration"]!["broodling-entrypoint"]!["sha256"]! + "  /usr/local/bin/broodling-target",
            (string)manifest["harnesses"]!["claude"]!["managedSettings"]!["sha256"]! + "  /etc/claude-code/managed-settings.json",
            "d 0:0 755 /etc/claude-code",
            "f 0:0 644 /etc/claude-code/managed-settings.json",
            .. manifest["osPackages"]!.AsArray().Select(package =>
                $"{(string)package!["name"]!} {(string)package["version"]!} {(string)package["architecture"]!}")
        ];
        var observed = await DockerCommand("run", "--rm", "--network", "none", "--entrypoint", "/bin/sh", image, "-ec", """
            sha256sum /usr/local/bin/zeroshot /usr/local/bin/restic /usr/local/bin/broodling-target
            setpriv --reuid 20000 --regid 10002 --clear-groups sha256sum /etc/claude-code/managed-settings.json
            find /etc/claude-code -printf '%y %U:%G %m %p\n'
            dpkg-query --show --showformat '${Package} ${Version} ${Architecture}\n'
            """);
        RequireSuccess(observed);
        var lines = observed.Output.TrimEnd('\n').Split('\n');
        await Assert.That(string.Join('\n', expected.Except(lines))).IsEqualTo("").Because("stated but not observed");
        await Assert.That(string.Join('\n', lines.Except(expected))).IsEqualTo("").Because("observed but not stated");
        await Assert.That(lines.Length).IsEqualTo(expected.Length);
    }

    /// <summary>
    /// The managed settings deny WebFetch and WebSearch, whose side calls would leave the uniform medium effort, turn
    /// off Workflows, which could run past the session's result, and allow only claude-opus-5-5. A session started as
    /// native's first agent identity, with the permission flag native's hosted lane always passes, offers neither web
    /// tool nor Workflow and still offers the tools that edit the workspace. Asked for Haiku, it warns and falls back
    /// to Claude Code's built-in default model.
    /// </summary>
    [Test]
    public async Task AdoptedClaudeCodeOffersNoWebOrWorkflowToolsAndRefusesHaiku()
    {
        var started = await DockerCommand("run", "--rm", "--network", "none", "--user", "20000:10002", "--tmpfs", "/tmp:exec",
            "--env", "HOME=/tmp/agent", "--env", "CLAUDE_CODE_MAX_RETRIES=0", "--env", "ANTHROPIC_API_KEY=placeholder",
            "--entrypoint", "/bin/sh", await Direct.Value, "-c",
            "mkdir -p \"$HOME\" && cd \"$HOME\" && claude --print --output-format stream-json --verbose --dangerously-skip-permissions --model haiku q | head -n 2");
        RequireSuccess(started);
        var events = started.Output.TrimEnd('\n').Split('\n').Select(line => JsonNode.Parse(line)!).ToArray();
        var init = events[0];
        await Assert.That((string)init["permissionMode"]!).IsEqualTo("bypassPermissions");
        await Assert.That(init["tools"]!.AsArray().Select(tool => (string)tool!)
            .Where(tool => tool is "Bash" or "Edit" or "Write" or "WebFetch" or "WebSearch" or "Workflow"))
            .IsEquivalentTo(["Bash", "Edit", "Write"]);
        await Assert.That((string)init["model"]!).IsEqualTo("claude-opus-5[1m]");
        await Assert.That((string)events[1]["content"]!)
            .IsEqualTo("Model \"haiku\" is restricted by your organization's settings. Using claude-opus-5[1m] instead.");
    }
}
