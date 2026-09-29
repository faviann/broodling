using System.Diagnostics;
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
        await Assert.That(asset.Content().SequenceEqual(File.ReadAllBytes(Path.Combine(Source, "software-change-pr-codex-gateway.json")))).IsTrue();
        await Assert.That(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "execution-assets", "approval.json"))
            .SequenceEqual(File.ReadAllBytes(Path.Combine(Source, "approval.json")))).IsTrue();
    }

    [Test]
    [Arguments("missing-asset")]
    [Arguments("missing-manifest")]
    [Arguments("changed-asset")]
    [Arguments("rebound-manifest")]
    public async Task LoadingRefusesMissingChangedOrDifferentlyBoundMaterial(string defect)
    {
        var directory = Directory.CreateTempSubdirectory("broodling-asset-");
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "execution-assets")))
                File.Copy(file, Path.Combine(directory.FullName, Path.GetFileName(file)));
            var asset = Path.Combine(directory.FullName, "software-change-pr-codex-gateway.json");
            var manifest = Path.Combine(directory.FullName, "approval.json");
            await Assert.That(ExecutionAsset.Load(directory.FullName).Sha256).IsEqualTo(DirectTargetBinding.AssetSha256);
            switch (defect)
            {
                case "missing-asset": File.Delete(asset); break;
                case "missing-manifest": File.Delete(manifest); break;
                case "changed-asset":
                    var bytes = File.ReadAllBytes(asset);
                    bytes[^2] = (byte)' ';
                    File.WriteAllBytes(asset, bytes);
                    break;
                case "rebound-manifest":
                    File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(DirectTargetBinding.NativeSourceRevision, new string('0', 40)));
                    break;
            }
            await Assert.That(() => ExecutionAsset.Load(directory.FullName)).Throws<UnsupportedRuntime>();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public async Task PinnedNativeToolingReproducesAndAdmitsTheExactAsset()
    {
        var output = Path.Combine(Directory.CreateTempSubdirectory("broodling-asset-").FullName, "asset.json");
        try
        {
            // The bridge wheel currently carries the DirectTarget binding's executable; generate.sh refuses any other bytes.
            var native = (await Run(NativeFixture.Python, "-I", "-c",
                "import importlib.resources; print(importlib.resources.files('zeroshot').joinpath('_bin', 'zeroshot'))")).Trim();
            await Run(Path.Combine(Source, "generate.sh"), native, output);
            await Assert.That(File.ReadAllBytes(output).SequenceEqual(ExecutionAsset.LoadBundled().Content())).IsTrue();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(output)!, true);
        }
    }

    [Test]
    public async Task PinnedReleaseReproducesAndAdmitsTheCandidateAsset()
    {
        // The candidate's own pinned native release, never the bridge's; generate.sh downloads it once into
        // the test workspace cache and verifies archive, executable and recipe against the candidate manifest.
        var cache = Path.Combine(Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"), "native-releases");
        var output = Path.Combine(Directory.CreateTempSubdirectory("broodling-asset-").FullName, "asset.json");
        try
        {
            await Run(Path.Combine(Source, "generate.sh"), "--manifest", Path.Combine(Source, "candidate-10.9.0.json"), "--fetch", cache, output);
            await Assert.That(File.ReadAllBytes(output).SequenceEqual(File.ReadAllBytes(Path.Combine(Source, "software-change-pr-codex-gateway-10.9.0.json")))).IsTrue();
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
