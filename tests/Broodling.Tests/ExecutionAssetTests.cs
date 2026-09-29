using System.Diagnostics;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class ExecutionAssetTests
{
    private static string Source => Path.Combine(NativeFixture.RepositoryRoot, "src", "Broodling", "execution-assets");

    [Test]
    public async Task BuildOutputCarriesTheApprovedAssetAndManifest()
    {
        var asset = ExecutionAsset.LoadBundled();
        await Assert.That(asset.Content().SequenceEqual(File.ReadAllBytes(Path.Combine(Source, DirectTargetBinding.AssetFile)))).IsTrue();
        await Assert.That(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "execution-assets", "approval.json"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(Source, "approval.json")))).IsTrue();
    }

    [Test]
    [Arguments("missing-asset")]
    [Arguments("missing-manifest")]
    [Arguments("changed-asset")]
    [Arguments("wrong-hash")]
    [Arguments("rebound-native")]
    [Arguments("rebound-policy")]
    [Arguments("10.3-approval")]
    public async Task LoadingRefusesMissingChangedOrDifferentlyBoundMaterial(string defect)
    {
        var directory = Directory.CreateTempSubdirectory("broodling-asset-");
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "execution-assets")))
                File.Copy(file, Path.Combine(directory.FullName, Path.GetFileName(file)));
            var asset = Path.Combine(directory.FullName, DirectTargetBinding.AssetFile);
            var manifest = Path.Combine(directory.FullName, "approval.json");
            await Assert.That(ExecutionAsset.Load(directory.FullName).Sha256).IsEqualTo(DirectTargetBinding.AssetSha256);
            var approval = JsonNode.Parse(File.ReadAllText(manifest))!;
            switch (defect)
            {
                case "missing-asset": File.Delete(asset); break;
                case "missing-manifest": File.Delete(manifest); break;
                case "changed-asset":
                    var bytes = File.ReadAllBytes(asset);
                    bytes[^2] = (byte)' ';
                    File.WriteAllBytes(asset, bytes);
                    break;
                case "wrong-hash": approval["asset"]!["sha256"] = new string('0', 64); break;
                case "rebound-native": approval["native"]!["release"]!["resticLinuxX64ExecutableSha256"] = new string('0', 64); break;
                case "rebound-policy": approval["policy"]!["pullRequestFeedback"] = "ignore"; break;
                case "10.3-approval":
                    // The superseded binding approves nothing, even beside the bytes it named.
                    File.Move(asset, Path.Combine(directory.FullName, "software-change-pr-codex-gateway.json"));
                    approval["asset"] = new JsonObject
                    {
                        ["file"] = "software-change-pr-codex-gateway.json", ["bytes"] = 77069,
                        ["sha256"] = "10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8"
                    };
                    approval["native"] = new JsonObject
                    {
                        ["version"] = "zeroshot 10.3.0", ["sourceRevision"] = "054ad3fd6c763b98d12f5b2e90830b97116561ad",
                        ["linuxX64ExecutableSha256"] = "afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06"
                    };
                    break;
            }
            if (File.Exists(manifest) && defect is not "changed-asset") File.WriteAllText(manifest, approval.ToJsonString());
            await Assert.That(() => ExecutionAsset.Load(directory.FullName)).Throws<UnsupportedRuntime>();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public async Task ApprovedPolicyIsTheAssetsOwnRuntime()
    {
        // The manifest's policy must describe the approved bytes, not only match the loader's expectation.
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Source, "approval.json")))!;
        var policy = manifest["policy"]!;
        await Assert.That(JsonNode.DeepEquals(policy, ExecutionAsset.Approval["policy"])).IsTrue();
        var runtime = ExecutionAsset.LoadBundled().Runtime().AsObject();
        foreach (var field in new[] { "harness", "provider", "size" })
            await Assert.That((string)runtime[field]!).IsEqualTo((string)policy[field]!);
        var connections = new JsonObject();
        foreach (var (name, node) in runtime["nodes"]!.AsObject())
        {
            if ((string)node!["kind"]! == "agent")
            {
                await Assert.That((string)node["model"]!).IsEqualTo((string)policy["model"]!).Because(name);
                await Assert.That((string)node["effort"]!).IsEqualTo((string)policy["effort"]!).Because(name);
            }
            else
            {
                await Assert.That((string)node["kind"]!).IsEqualTo("git_delivery").Because(name);
                // Native serializes its default `consider` pull-request feedback by omitting it.
                await Assert.That((string?)node["pullRequestFeedback"] ?? "consider").IsEqualTo((string)policy["pullRequestFeedback"]!);
            }
            foreach (var (connection, variables) in node["connections"]!.AsObject())
                connections[connection] = variables!.DeepClone();
        }
        await Assert.That(JsonNode.DeepEquals(connections, policy["connections"])).IsTrue();
        var uniform = manifest["generation"]!["uniformRuntime"]!.AsObject();
        foreach (var (field, value) in uniform)
            await Assert.That(JsonNode.DeepEquals(value, field == "connections" ? new JsonObject { ["gateway"] = policy["connections"]!["gateway"]!.DeepClone() } : policy[field]))
                .IsTrue().Because(field);
    }

    [Test]
    public async Task PinnedReleaseReproducesAndAdmitsTheApprovedAsset()
    {
        // The binding's own pinned native release, never the bridge's; generate.sh downloads it once into the
        // test workspace cache and verifies archive, executable, recipe and structure against approval.json.
        var cache = Path.Combine(Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"), "native-releases");
        var output = Path.Combine(Directory.CreateTempSubdirectory("broodling-asset-").FullName, "asset.json");
        try
        {
            await Run(Path.Combine(Source, "generate.sh"), "--fetch", cache, output);
            await Assert.That(File.ReadAllBytes(output).SequenceEqual(ExecutionAsset.LoadBundled().Content())).IsTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(output)!, true);
        }
    }

    private static async Task<string> Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180));
        if (process.ExitCode != 0) throw new Exception(await error);
        return await output;
    }
}
