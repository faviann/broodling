using System.Net;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

// The primary boundary is the actual target and pinned zeroshot-tls images behind the HTTPS origin, in
// native's private mode with synthetic control material,
// with disposable state and no provider workload.
public sealed class NativeTargetStartupTests
{
    [Test]
    public async Task RootHelperCreatesOneRootWithAPrivateKeyAndAPublicCertificate()
    {
        await using var stack = await TargetStack.CreateAsync();
        // The same host directory as both locations would put the key beside the public certificate.
        var shared = await stack.CreateRoot([.. TargetStack.Bind(stack.RootKey, "/tls-root-key"), .. TargetStack.Bind(stack.RootKey, "/tls-root")]);
        await Assert.That(shared.Code).IsEqualTo(1);
        await Assert.That(shared.Error).Contains("separate locations");
        var unmounted = await stack.CreateRoot(TargetStack.Bind(stack.RootKey, "/tls-root-key"));
        await Assert.That(unmounted.Code).IsEqualTo(1);
        await Assert.That(Directory.EnumerateFileSystemEntries(stack.RootKey).Any()).IsFalse();

        RequireSuccess(await stack.CreateRoot(stack.RootMounts));
        const string Listing = "find /tls-root-key /tls-root -printf '%p %U:%G %m\\n' | sort; sha256sum /tls-root-key/root.key /tls-root/root.crt";
        var created = (await stack.Shell(Listing, stack.RootMounts)).Output;
        await Assert.That(string.Join("\n", created.Split('\n').Take(4))).IsEqualTo(
            "/tls-root 0:0 755\n/tls-root-key 10443:10443 700\n/tls-root-key/root.key 10443:10443 400\n/tls-root/root.crt 0:0 644");
        await Assert.That(Directory.GetFileSystemEntries(stack.RootCertificate).Select(Path.GetFileName).SequenceEqual(["root.crt"])).IsTrue();
        await Assert.That(File.ReadAllText(stack.RootCertificateFile)).Contains("BEGIN CERTIFICATE");

        // An existing root, whole or partial, is never replaced.
        var again = await stack.CreateRoot(stack.RootMounts);
        await Assert.That(again.Code).IsEqualTo(1);
        await Assert.That(again.Error).Contains("never replaced");
        await Assert.That((await stack.Shell(Listing, stack.RootMounts)).Output).IsEqualTo(created);
        const string Key = "/owned/tls-root-key/root.key", Certificate = "/owned/tls-root/root.crt";
        var owned = TargetStack.Bind(stack.Root, "/owned");
        foreach (var (remove, remaining) in new[] { (Key, Certificate), (Certificate, Key) })
        {
            var kept = (await stack.Shell($"mv {remove} /owned/saved; sha256sum {remaining}", owned)).Output;
            var partial = await stack.CreateRoot(stack.RootMounts);
            await Assert.That(partial.Code).IsEqualTo(1);
            await Assert.That((await stack.Shell($"test ! -e {remove}; sha256sum {remaining}", owned)).Output).IsEqualTo(kept);
            await stack.Shell($"mv /owned/saved {remove}", owned);
        }
        await Assert.That((await stack.Shell(Listing, stack.RootMounts)).Output).IsEqualTo(created);
    }

    [Test]
    [Arguments("missing-key")]
    [Arguments("missing-certificate")]
    [Arguments("mismatched-key")]
    public async Task ZeroshotTlsNeverStartsWithoutItsProvidedRoot(string change)
    {
        await using var stack = await TargetStack.CreateAsync();
        RequireSuccess(await stack.CreateRoot(stack.RootMounts));
        switch (change)
        {
            case "missing-key": await stack.Shell("rm /tls-root-key/root.key", stack.RootMounts); break;
            case "missing-certificate": await stack.Shell("rm /tls-root/root.crt", stack.RootMounts); break;
            case "mismatched-key":
                // Another root helper's key under the same certificate.
                var other = Path.Combine(stack.Root, "other");
                Directory.CreateDirectory(Path.Combine(other, "key"));
                Directory.CreateDirectory(Path.Combine(other, "crt"));
                RequireSuccess(await stack.CreateRoot([.. TargetStack.Bind(Path.Combine(other, "key"), "/tls-root-key"),
                    .. TargetStack.Bind(Path.Combine(other, "crt"), "/tls-root")]));
                await stack.Shell("cp -p /other/key/root.key /tls-root-key/root.key", [.. stack.RootMounts, .. TargetStack.Bind(other, "/other")]);
                break;
        }
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", stack.Tls, "--network", "none", .. await stack.TlsOptions()]));
        string state = "";
        for (var retry = 0; retry < 100 && !state.StartsWith("exited", StringComparison.Ordinal); retry++)
        {
            await Task.Delay(100);
            state = (await DockerCommand("inspect", "--format", "{{.State.Status}} {{.State.ExitCode}}", stack.Tls)).Output.Trim();
        }
        await Assert.That(state).IsEqualTo("exited 2");
        // Caddy did not fall back to generating a root of its own.
        var stored = await DockerCommand("run", "--rm", "--network", "none", "--volumes-from", stack.Tls, "--entrypoint", "find",
            await TargetImage.Tls.Value, "/data", "-name", "root.*");
        RequireSuccess(stored);
        await Assert.That(stored.Output).IsEqualTo("");
    }

    /// <summary>
    /// Initialization needs no network and records the binding; the target then serves only native's private
    /// mode behind zeroshot-tls. Control is refused before bootstrap and to any other token; a bootstrap with
    /// another key consumes nothing; a closed bootstrap never replaces the installed token. Every restart over
    /// the same state needs a new bootstrap, and an explicit rotation retires the former token, while the state
    /// stays unchanged throughout.
    /// </summary>
    [Test]
    public async Task InitializationRecordsTheBindingAndStartupServesOnlyBootstrappedPrivateControl()
    {
        await using var stack = await TargetStack.CreateAsync();
        RequireSuccess(await stack.CreateRoot(stack.RootMounts));
        await stack.StartTls();
        var initialized = await stack.Initialize();
        RequireSuccess(initialized);
        await Assert.That(initialized.Output).Contains("no provider work submitted");
        await Assert.That((await stack.Shell("cat /home/node/.config/broodling/origin")).Output).IsEqualTo(TargetStack.Origin + "\n");
        await Assert.That((await DockerCommand("ps", "--all", "--quiet", "--filter", "name=" + stack.Zeroshot)).Output).IsEqualTo("");

        await stack.Shell("touch /state/runs/owned /home/node/owned; chown 10002:10002 /state/runs/owned; chown 20000:20000 /home/node/owned; chmod 700 /state/runs/owned; chmod 600 /home/node/owned");
        var before = await Snapshot(stack);
        await stack.StartTarget();
        await stack.Serving();
        using (var http = await stack.Client())
        using (var discovery = JsonDocument.Parse(await http.GetStringAsync(TargetStack.Origin + "/.well-known/zeroshot-native-v2")))
            await Assert.That(discovery.RootElement.GetProperty("authentication").GetString()).IsEqualTo("private_capability");
        // Native read and unlinked its private copy of the key before serving.
        await Assert.That((await DockerCommand("exec", stack.Zeroshot, "ls", "-A", "/run/broodling-target")).Output).IsEqualTo("");
        var token = stack.Token;
        var other = TestAccess.NewSecret();
        foreach (var bearer in new[] { null, token, other })
            await Assert.That(await stack.Control(bearer)).IsEqualTo(HttpStatusCode.Unauthorized);

        // An envelope under another key is refused without consuming the target's key.
        var otherKey = Path.Combine(stack.Root, "other-key");
        File.WriteAllText(otherKey, TestAccess.NewSecret());
        await Fails(() => stack.Bootstrap(otherKey), "bootstrap key differs");
        await Assert.That(await stack.Bootstrap()).IsEqualTo(DirectTargetBootstrapResult.Installed);
        // A repeat, as after a lost acknowledgement, sends nothing.
        await Assert.That(await stack.Bootstrap()).IsEqualTo(DirectTargetBootstrapResult.AlreadyInstalled);
        await Assert.That(await stack.Control(token)).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await stack.Control(null)).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await stack.Control(other)).IsEqualTo(HttpStatusCode.Unauthorized);
        // Another token cannot take over the running target.
        File.WriteAllText(stack.TokenFile, other);
        await Fails(() => stack.Bootstrap(), "holds another one");
        File.WriteAllText(stack.TokenFile, token);
        await Assert.That(await stack.Control(token)).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await stack.Control(other)).IsEqualTo(HttpStatusCode.Unauthorized);
        var published = await DockerCommand("inspect", "--format", "{{json .HostConfig.PortBindings}}", stack.Zeroshot);
        await Assert.That(published.Output.Trim()).IsEqualTo("{}");

        // An ordinary restart over the same state: the new process accepts nothing until bootstrapped again.
        await stack.StopTarget();
        await Assert.That(await Snapshot(stack)).IsEqualTo(before);
        await stack.StartTarget();
        await stack.Serving();
        await Assert.That(await stack.Control(token)).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await stack.Bootstrap()).IsEqualTo(DirectTargetBootstrapResult.Installed);
        await Assert.That(await stack.Control(token)).IsEqualTo(HttpStatusCode.OK);

        // Explicit rotation: a restart bootstrapped with the replacement token, which alone is then accepted.
        await stack.StopTarget();
        File.WriteAllText(stack.TokenFile, other);
        await stack.StartTarget();
        await stack.Serving();
        await Assert.That(await stack.Bootstrap()).IsEqualTo(DirectTargetBootstrapResult.Installed);
        await Assert.That(await stack.Control(other)).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await stack.Control(token)).IsEqualTo(HttpStatusCode.Unauthorized);
        await stack.StopTarget();
        await Assert.That(await Snapshot(stack)).IsEqualTo(before);
        var reinitialize = await stack.Initialize();
        await Assert.That(reinitialize.Code).IsEqualTo(1);
        await Assert.That(reinitialize.Error).Contains("requires empty state and home");
        await Assert.That(await Snapshot(stack)).IsEqualTo(before);
    }

    /// <summary>
    /// Serving requires the root-only bootstrap key: without it, or with one execution agents could read, the
    /// entrypoint stops before native serves anything, and never serves unauthenticated.
    /// </summary>
    [Test]
    [Arguments("missing")]
    [Arguments("other-readable")]
    [Arguments("agent-owned")]
    public async Task ServingRequiresTheRootOnlyBootstrapKeyAndNeverFallsBackToUnauthenticated(string change)
    {
        await using var stack = await TargetStack.CreateAsync();
        RequireSuccess(await stack.Initialize());
        var key = File.ReadAllText(stack.OperatorBootstrapKeyFile);
        string[] mounts = stack.TargetMounts;
        switch (change)
        {
            case "missing": mounts = [.. TargetStack.Bind(stack.State, "/state"), .. TargetStack.Bind(stack.Home, "/home/node")]; break;
            case "other-readable": RequireSuccess(await stack.ProvisionKey(key, mode: "0404")); break;
            case "agent-owned": RequireSuccess(await stack.ProvisionKey(key, owner: "10002:10002")); break;
        }
        var before = await Tree(stack);
        var refused = await DockerCommand(["run", "--rm", "--network", "none", .. mounts, await TargetImage.Direct.Value, .. TargetStack.Arguments]);
        await Assert.That(refused.Code).IsEqualTo(1);
        await Assert.That(refused.Error).Contains("bootstrap key");
        await Assert.That(refused.Error).DoesNotContain("listening");
        await Assert.That(await Tree(stack)).IsEqualTo(before);
    }

    [Test]
    public async Task MissingOrUnrecognizedStateRefusesBeforeServingWithoutRepairingIt()
    {
        await using var stack = await TargetStack.CreateAsync();
        var empty = await stack.Run(TargetStack.Arguments);
        await Assert.That(empty.Code).IsEqualTo(1);
        await Assert.That((await stack.Shell("ls -A /state /home/node")).Output).IsEqualTo("/home/node:\n\n/state:\n");
        await stack.InitializeAll();
        // Each case models a realistic missing, redirected or foreign mount.
        foreach (var (change, restore) in new[] {
            ("mv /state/runs.sqlite3 /state/saved", "mv /state/saved /state/runs.sqlite3"),
            ("mv /home/node/.config /home/node/saved", "mv /home/node/saved /home/node/.config"),
            ("mv /state/runs.sqlite3 /state/saved; ln -s /state/saved /state/runs.sqlite3", "rm /state/runs.sqlite3; mv /state/saved /state/runs.sqlite3"),
            // Native would create its tables inside this unrelated database and serve a fresh ledger.
            ("mv /state/runs.sqlite3 /state/saved; python3 -c \"import sqlite3; sqlite3.connect('/state/runs.sqlite3').execute('CREATE TABLE foreign_store(id)')\"", "mv /state/saved /state/runs.sqlite3") })
        {
            await stack.Shell(change);
            var before = await Tree(stack);
            var result = await stack.Run(TargetStack.Arguments);
            await Assert.That(result.Code).IsEqualTo(1);
            await Assert.That(result.Error).Contains("Native target state refused:");
            await Assert.That(await Tree(stack)).IsEqualTo(before);
            await stack.Shell(restore);
        }
        var binding = await stack.Run("--listen", TargetReadiness.NativeListen, "--public-origin", "https://other.dev.faviann.com", "--storage", "/state");
        await Assert.That(binding.Code).IsEqualTo(1);
        await Assert.That(binding.Error).Contains("not bound to this public origin");
    }

    [Test]
    public async Task ConfiguredLocationsAndNonemptyInitializationAreRefused()
    {
        await using var stack = await TargetStack.CreateAsync();
        // Pinned native, not the entrypoint, decides origin validity and spelling, before any state exists.
        foreach (var invalid in new[] { "http://broodling-target:18770", "https://ZEROSHOT.dev.faviann.com" })
        {
            var origin = await stack.Run("initialize", "--listen", TargetReadiness.NativeListen, "--public-origin", invalid, "--storage", "/state");
            await Assert.That(origin.Code).IsEqualTo(1);
            await Assert.That((await stack.Shell("ls -A /state /home/node")).Output).IsEqualTo("/home/node:\n\n/state:\n");
        }
        await stack.Shell("echo retain > /home/node/existing");
        var before = await Tree(stack);
        var initialize = await stack.Run(["initialize", .. TargetStack.Arguments]);
        await Assert.That(initialize.Code).IsEqualTo(1);
        await Assert.That(await Tree(stack)).IsEqualTo(before);
        await stack.Shell("rm /home/node/existing");
        RequireSuccess(await stack.Initialize());
        foreach (var configuration in new[] { "HOME=/missing", "CODEX_HOME=/other", "ZEROSHOT_CONFIG_DIR=/other", "XDG_CONFIG_HOME=/other" })
        {
            var refusal = await stack.RunWith(["--env", configuration], TargetStack.Arguments);
            await Assert.That(refusal.Code).IsEqualTo(1);
            await Assert.That(refusal.Error).Contains("Native target state refused:");
        }
        var storage = await stack.Run("--listen", TargetReadiness.NativeListen, "--public-origin", TargetStack.Origin, "--storage", "/other");
        await Assert.That(storage.Code).IsEqualTo(1);
        // The inner port is fixed: zeroshot-tls forwards only there.
        var listen = await stack.Run("--listen", "0.0.0.0:18771", "--public-origin", TargetStack.Origin, "--storage", "/state");
        await Assert.That(listen.Code).IsEqualTo(1);
        await Assert.That(listen.Error).Contains("--listen 0.0.0.0:18770");
        // With the configured mount absent, image-layer defaults cannot create replacement state.
        var missing = await DockerCommand(["run", "--rm", "--network", "none", await TargetImage.Direct.Value, .. TargetStack.Arguments]);
        await Assert.That(missing.Code).IsEqualTo(1);
    }

    private static async Task Fails(Func<Task<DirectTargetBootstrapResult>> bootstrap, string message)
    {
        try { await bootstrap(); }
        catch (DirectTargetBootstrapFailed failure)
        {
            await Assert.That(failure.Message).Contains(message);
            return;
        }
        throw new InvalidOperationException("Expected the bootstrap to fail.");
    }

    private static async Task<string> Snapshot(TargetStack stack) => (await stack.Shell(
        "sha256sum /state/runs.sqlite3 /home/node/.config/broodling/origin; stat -c '%u:%g:%a' /state/runs/owned /home/node/owned")).Output;

    private static async Task<string> Tree(TargetStack stack) => (await stack.Shell(
        "find /state /home/node -printf '%p %y %u:%g %m %l\\n' | sort; find /state /home/node -type f -exec sha256sum {} + | sort")).Output;
}
