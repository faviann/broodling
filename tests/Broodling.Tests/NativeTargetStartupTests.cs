using System.Diagnostics;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

// The primary boundary is the actual target image, without a provider or published port.
public sealed class NativeTargetStartupTests
{
    private static readonly Lazy<Task<string>> Image = new(BuildImage);

    [Test]
    public async Task ExplicitInitializationThenStartupPreservesNativeBindingAndMixedUidFiles()
    {
        await using var target = await Target.Create(await Image.Value);
        await target.Initialize();
        await target.Shell("touch /state/runs/owned /home/node/owned; chown 10002:10002 /state/runs/owned; chown 20000:20000 /home/node/owned; chmod 700 /state/runs/owned; chmod 600 /home/node/owned");
        var before = await target.Snapshot();
        await target.Start();
        await Assert.That(await target.Discover()).Contains("zeroshot.native-v2-target/v2");
        // A public native list opens the ledger without dispatching any workload.
        var list = await Docker("exec", target.Container, "zeroshot", "list", "--target", "broodling");
        await Assert.That(JsonDocument.Parse(list.Output).RootElement.GetProperty("runs").GetArrayLength()).IsEqualTo(0);
        await target.Stop();
        await Assert.That(await target.Snapshot()).IsEqualTo(before);
        var reinitialize = await target.Run(["initialize", .. Target.Arguments]);
        await Assert.That(reinitialize.Code).IsEqualTo(1);
        await Assert.That(reinitialize.Error).Contains("requires empty state and home");
        await Assert.That(await target.Snapshot()).IsEqualTo(before);
    }

    [Test]
    public async Task MissingOrUnrecognizedStateRefusesBeforeServingWithoutRepairingIt()
    {
        await using var target = await Target.Create(await Image.Value);
        var empty = await target.Run(Target.Arguments);
        await Assert.That(empty.Code).IsEqualTo(1);
        await Assert.That((await target.Shell("ls -A /state /home/node")).Output).IsEqualTo("/home/node:\n\n/state:\n");
        await target.Initialize();
        // Each case models a realistic missing, redirected or foreign mount.
        foreach (var (change, restore) in new[] {
            ("mv /state/runs.sqlite3 /state/saved", "mv /state/saved /state/runs.sqlite3"),
            ("mv /home/node/.config /home/node/saved", "mv /home/node/saved /home/node/.config"),
            ("mv /state/runs.sqlite3 /state/saved; ln -s /state/saved /state/runs.sqlite3", "rm /state/runs.sqlite3; mv /state/saved /state/runs.sqlite3") })
        {
            await target.Shell(change);
            var before = await target.Tree();
            var result = await target.Run(Target.Arguments);
            await Assert.That(result.Code).IsEqualTo(1);
            await Assert.That(result.Error).Contains("Native target state refused:");
            await Assert.That(await target.Tree()).IsEqualTo(before);
            await target.Shell(restore);
        }
        var binding = await target.Run("--listen", "0.0.0.0:18770", "--public-origin", "http://127.0.0.1:18771", "--storage", "/state");
        await Assert.That(binding.Code).IsEqualTo(1);
        await Assert.That(binding.Error).Contains("not bound to this public origin");
    }

    [Test]
    public async Task ConfiguredLocationsAndNonemptyInitializationAreRefused()
    {
        await using var target = await Target.Create(await Image.Value);
        // Pinned native, not the entrypoint, decides origin validity, before any state exists.
        var origin = await target.Run("initialize", "--listen", "0.0.0.0:18770", "--public-origin", "http://broodling-target:18770", "--storage", "/state");
        await Assert.That(origin.Code).IsEqualTo(1);
        await Assert.That((await target.Shell("ls -A /state /home/node")).Output).IsEqualTo("/home/node:\n\n/state:\n");
        await target.Shell("echo retain > /home/node/existing");
        var before = await target.Tree();
        var initialize = await target.Run(["initialize", .. Target.Arguments]);
        await Assert.That(initialize.Code).IsEqualTo(1);
        await Assert.That(await target.Tree()).IsEqualTo(before);
        await target.Shell("rm /home/node/existing");
        await target.Initialize();
        foreach (var configuration in new[] { "HOME=/missing", "CODEX_HOME=/other", "ZEROSHOT_CONFIG_DIR=/other", "XDG_CONFIG_HOME=/other" })
        {
            var refusal = await target.RunWith(["--env", configuration], Target.Arguments);
            await Assert.That(refusal.Code).IsEqualTo(1);
            await Assert.That(refusal.Error).Contains("Native target state refused:");
        }
        var storage = await target.Run("--listen", "0.0.0.0:18770", "--public-origin", Target.Origin, "--storage", "/other");
        await Assert.That(storage.Code).IsEqualTo(1);
        // With the configured mount absent, image-layer defaults cannot create replacement state.
        var missing = await Docker(["run", "--rm", "--network", "none", await Image.Value, .. Target.Arguments]);
        await Assert.That(missing.Code).IsEqualTo(1);
    }

    private static async Task<string> BuildImage()
    {
        var context = Path.Combine(Path.GetTempPath(), "broodling-104-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(context);
        try
        {
            var binary = await Run(NativeFixture.Python, "-c", "import pathlib,zeroshot; print(pathlib.Path(zeroshot.__file__).parent / '_bin' / 'zeroshot')");
            RequireSuccess(binary);
            File.Copy(binary.Output.Trim(), Path.Combine(context, "zeroshot"));
            foreach (var name in new[] { "DirectTarget.Dockerfile", "direct-target-entrypoint.sh" })
                File.Copy(Path.Combine(NativeFixture.RepositoryRoot, "deployment", name), Path.Combine(context, name));
            var tag = "broodling-startup-test:" + Guid.NewGuid().ToString("N");
            RequireSuccess(await Docker("build", "--tag", tag, "--file", Path.Combine(context, "DirectTarget.Dockerfile"), context));
            return tag;
        }
        finally { Directory.Delete(context, true); }
    }

    [After(Class)]
    public static async Task RemoveImage()
    {
        if (Image.IsValueCreated && Image.Value.IsCompletedSuccessfully)
            RequireSuccess(await Docker("image", "rm", await Image.Value));
    }

    private sealed class Target(string image) : IAsyncDisposable
    {
        internal const string Origin = "http://127.0.0.1:18770";
        internal static readonly string[] Arguments = ["--listen", "0.0.0.0:18770", "--public-origin", Origin, "--storage", "/state"];
        private readonly string id = "broodling-104-" + Guid.NewGuid().ToString("N");
        internal string Container => id;
        private string[] Mounts => ["--mount", $"type=volume,src={id}-state,dst=/state", "--mount", $"type=volume,src={id}-home,dst=/home/node,volume-nocopy"];
        internal static async Task<Target> Create(string image)
        {
            var target = new Target(image);
            RequireSuccess(await Docker("volume", "create", target.id + "-state"));
            RequireSuccess(await Docker("volume", "create", target.id + "-home"));
            return target;
        }
        internal Task<Result> Run(params string[] arguments) => RunWith([], arguments);
        internal async Task<Result> RunWith(string[] options, string[] arguments) =>
            await Docker(["run", "--rm", "--name", Container, "--network", "none", .. Mounts, .. options, image, .. arguments]);
        internal async Task<Result> Shell(string script)
        {
            var result = await RunWith(["--entrypoint", "/bin/sh"], ["-ec", script]);
            RequireSuccess(result);
            return result;
        }
        internal async Task Initialize() => RequireSuccess(await Run(["initialize", .. Arguments]));
        internal async Task Start() => RequireSuccess(await Docker(["run", "--detach", "--name", Container, "--network", "none", .. Mounts, image, .. Arguments]));
        internal async Task Stop() => RequireSuccess(await Docker("rm", "--force", Container));
        internal async Task<string> Discover()
        {
            for (var retry = 0; retry < 60; retry++)
            {
                var result = await Docker("exec", Container, "node", "-e", "fetch('http://127.0.0.1:18770/.well-known/zeroshot-native-v2').then(r=>r.text()).then(console.log).catch(()=>process.exit(1))");
                if (result.Code == 0) return result.Output;
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Target never served: " + (await Docker("logs", Container)).Error);
        }
        internal async Task<string> Snapshot() => (await Shell("sha256sum /state/runs.sqlite3 /home/node/.config/zeroshot/targets.json; stat -c '%u:%g:%a' /state/runs/owned /home/node/owned")).Output;
        internal async Task<string> Tree() => (await Shell("find /state /home/node -printf '%p %y %u:%g %m %l\\n' | sort; find /state /home/node -type f -exec sha256sum {} + | sort")).Output;
        public async ValueTask DisposeAsync()
        {
            await Docker("rm", "--force", Container);
            RequireSuccess(await Docker("volume", "rm", id + "-state", id + "-home"));
        }
    }

    private sealed record Result(int Code, string Output, string Error);
    private static Task<Result> Docker(params string[] arguments) => Run("docker", arguments);
    private static void RequireSuccess(Result result)
    {
        if (result.Code != 0) throw new InvalidOperationException($"Command exited {result.Code}: {result.Output}{result.Error}");
    }
    private static async Task<Result> Run(string executable, params string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
