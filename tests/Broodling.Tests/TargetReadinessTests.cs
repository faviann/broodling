using Broodling.Host;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class TargetReadinessTests
{
    [Test]
    public async Task ChecksActualSelectedContainerPinsCapabilitiesAndDiscoveryWithoutProviderWork()
    {
        using var fixture = new ReadinessFixture();
        var facts = await fixture.Check();
        await Assert.That(facts.Ready && facts.ApiPaginateSlurp && facts.HostedUidTransition).IsTrue();
        await Assert.That(facts.ProviderTasks).IsEqualTo(0);
        await Assert.That(facts.ContainerId).IsEqualTo("selected-container-id");
        await Assert.That(facts.ImageId).IsEqualTo("sha256:installed");
        await Assert.That(fixture.Calls.Count).IsEqualTo(9);
        await Assert.That(fixture.Calls[0].SequenceEqual(["inspect", "--type", "container", "installation-target", "installation-tls",
            "installation-broodling"])).IsTrue();
        await Assert.That(fixture.Calls.Skip(1).All(args => args.Take(2).SequenceEqual(["exec", "selected-container-id"]))).IsTrue();
        await Assert.That(fixture.Calls[^1].SequenceEqual(["exec", "selected-container-id", "python3", "-c",
            "import os; os.setgroups([10002]); os.setgid(10002); os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002"])).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(1);
        // Discovery reaches zeroshot-tls's actual publication and trusts the configured root.
        await Assert.That(fixture.Published).IsEqualTo(new IPEndPoint(IPAddress.Loopback, 18443));
        await Assert.That(fixture.TrustedRoot).IsEqualTo(fixture.RootCertificate);
        await Assert.That(JsonSerializer.Serialize(facts).Contains(ReadinessFixture.Secret)).IsFalse();
    }

    [Test]
    [Arguments("0.0.0.0", "127.0.0.1")]
    [Arguments("192.0.2.10", "192.0.2.10")]
    public async Task DiscoveryConnectsWhereZeroshotTlsIsPublishedOnTheHost(string hostIp, string reached)
    {
        using var fixture = new ReadinessFixture();
        fixture.Tls["NetworkSettings"]!["Ports"]!["443/tcp"]![0]!["HostIp"] = hostIp;
        await fixture.Check();
        await Assert.That(fixture.Published).IsEqualTo(new IPEndPoint(IPAddress.Parse(reached), 18443));
    }

    [Test]
    [Arguments("image", "image")]
    [Arguments("stopped", "stopped")]
    [Arguments("user", "container root")]
    [Arguments("privileged", "isolation")]
    [Arguments("host-network", "isolation")]
    [Arguments("cap-drop", "capabilities")]
    [Arguments("restart", "restart")]
    [Arguments("entrypoint", "entrypoint")]
    [Arguments("arguments", "launch arguments")]
    [Arguments("inner-port", "launch arguments")]
    [Arguments("mount-source", "mounts")]
    [Arguments("mount-destination", "mounts")]
    [Arguments("mount-ro", "mounts")]
    [Arguments("mount-type", "mounts")]
    [Arguments("mount-extra", "mounts")]
    [Arguments("mount-duplicate", "mounts")]
    [Arguments("root-certificate-rw", "mounts")]
    [Arguments("root-key", "mounts")]
    [Arguments("port-published", "publish no port")]
    [Arguments("publish-all", "publish no port")]
    [Arguments("network", "zeroshot alias")]
    [Arguments("target-alias", "zeroshot alias")]
    [Arguments("tls-image", "pinned image")]
    [Arguments("tls-stopped", "zeroshot-tls is stopped")]
    [Arguments("tls-root-user", "non-root user")]
    [Arguments("tls-other-user", "non-root user")]
    [Arguments("tls-privileged", "isolation")]
    [Arguments("tls-host-network", "isolation")]
    [Arguments("tls-cap-add", "NET_BIND_SERVICE")]
    [Arguments("tls-cap-drop", "NET_BIND_SERVICE")]
    [Arguments("tls-port-extra", "443 port")]
    [Arguments("tls-port-missing", "443 port")]
    [Arguments("tls-publish-all", "443 port")]
    [Arguments("tls-alias", "alias")]
    [Arguments("tls-network", "alias")]
    [Arguments("tls-key-source", "root key and certificate")]
    [Arguments("tls-key-rw", "root key and certificate")]
    [Arguments("tls-certificate-missing", "root key and certificate")]
    [Arguments("tls-data-in-public-root", "storage must not be inside the public root")]
    [Arguments("broodling-key", "broodling must not mount")]
    [Arguments("broodling-key-parent", "broodling must not mount")]
    [Arguments("broodling-caddy-data", "broodling must not mount")]
    [Arguments("broodling-root-rw", "public root directory as a read-only bind")]
    [Arguments("two-containers", "exactly the recorded")]
    [Arguments("malformed", "invalid")]
    public async Task ContainerDriftRefusesBeforeAnyExecOrDiscovery(string change, string message)
    {
        using var fixture = new ReadinessFixture();
        var config = fixture.Target["Config"]!;
        var host = fixture.Target["HostConfig"]!;
        var mount = fixture.Target["Mounts"]![0]!;
        var tls = fixture.Tls;
        var broodlingMounts = fixture.Broodling["Mounts"]!.AsArray();
        JsonObject Bind(string source, string destination, bool readWrite) =>
            new() { ["Type"] = "bind", ["RW"] = readWrite, ["Source"] = source, ["Destination"] = destination };
        switch (change)
        {
            case "image": fixture.Target["Image"] = "other"; break;
            case "stopped": fixture.Target["State"]!["Running"] = false; break;
            case "user": config["User"] = "1000:1000"; break;
            case "privileged": host["Privileged"] = true; break;
            case "host-network": host["NetworkMode"] = "host"; break;
            case "cap-drop": host["CapDrop"] = new JsonArray("SETUID"); break;
            case "restart": host["RestartPolicy"]!["Name"] = "always"; break;
            case "entrypoint": config["Entrypoint"] = new JsonArray("other"); break;
            case "arguments": config["Cmd"]![3] = "https://other.dev.faviann.com"; break;
            case "inner-port": config["Cmd"]![1] = "0.0.0.0:18771"; break;
            case "mount-source": mount["Source"] = "/other-state"; break;
            case "mount-destination": mount["Destination"] = "/other"; break;
            case "mount-ro": mount["RW"] = false; break;
            case "mount-type": mount["Type"] = "volume"; break;
            case "mount-extra": fixture.Target["Mounts"]!.AsArray().Add(Bind(fixture.Root, "/extra", false)); break;
            case "mount-duplicate": fixture.Target["Mounts"]![1] = mount.DeepClone(); break;
            case "root-certificate-rw": fixture.Target["Mounts"]![2]!["RW"] = true; break;
            case "root-key": fixture.Target["Mounts"]![2]!["Source"] = fixture.Inventory.RootKeyMount; break;
            case "port-published": host["PortBindings"] = JsonNode.Parse("""{"18770/tcp":[{"HostIp":"127.0.0.1","HostPort":"18770"}]}"""); break;
            case "publish-all": host["PublishAllPorts"] = true; break;
            case "network": fixture.Target["NetworkSettings"]!["Networks"] = JsonNode.Parse("""{"other":{"Aliases":["zeroshot"]}}"""); break;
            case "target-alias": fixture.Target["NetworkSettings"]!["Networks"]!["broodling_default"]!["Aliases"] = new JsonArray("other"); break;
            case "tls-image": tls["Config"]!["Image"] = "caddy:2.11.4-alpine"; break;
            case "tls-stopped": tls["State"]!["Running"] = false; break;
            case "tls-root-user": tls["Config"]!["User"] = ""; break;
            case "tls-other-user": tls["Config"]!["User"] = "1000:1000"; break;
            case "tls-privileged": tls["HostConfig"]!["Privileged"] = true; break;
            case "tls-host-network": tls["HostConfig"]!["NetworkMode"] = "host"; break;
            case "tls-cap-add": tls["HostConfig"]!["CapAdd"]!.AsArray().Add("CAP_NET_RAW"); break;
            case "tls-cap-drop": tls["HostConfig"]!["CapDrop"] = null; break;
            case "tls-port-extra": tls["HostConfig"]!["PortBindings"]!["80/tcp"] = JsonNode.Parse("""[{"HostIp":"","HostPort":"80"}]"""); break;
            case "tls-port-missing": tls["HostConfig"]!["PortBindings"] = new JsonObject(); break;
            case "tls-publish-all": tls["HostConfig"]!["PublishAllPorts"] = true; break;
            case "tls-alias": tls["NetworkSettings"]!["Networks"]!["broodling_default"]!["Aliases"] = new JsonArray("zeroshot-tls"); break;
            case "tls-network": tls["NetworkSettings"]!["Networks"] = JsonNode.Parse("""{"other":{"Aliases":["zeroshot.dev.faviann.com"]}}"""); break;
            case "tls-key-source": tls["Mounts"]![0]!["Source"] = fixture.Root; break;
            case "tls-key-rw": tls["Mounts"]![0]!["RW"] = true; break;
            case "tls-certificate-missing": tls["Mounts"]!.AsArray().RemoveAt(1); break;
            case "tls-data-in-public-root": tls["Mounts"]![2] = Bind(Path.Combine(fixture.Inventory.RootCertificateMount, "data"), "/data", true); break;
            case "broodling-key": broodlingMounts.Add(Bind(fixture.Inventory.RootKeyMount, "/key", false)); break;
            case "broodling-key-parent": broodlingMounts.Add(Bind(fixture.Root, "/srv", false)); break;
            case "broodling-caddy-data": broodlingMounts.Add(tls["Mounts"]![2]!.DeepClone()); break;
            case "broodling-root-rw": broodlingMounts[0]!["RW"] = true; break;
            case "two-containers": fixture.Inspect = $"[{fixture.Target},{fixture.Tls}]"; break;
            case "malformed": fixture.Inspect = ReadinessFixture.Secret; break;
        }
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.Calls.Count).IsEqualTo(1);
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("GH_TOKEN")]
    [Arguments("GITHUB_TOKEN")]
    [Arguments("GATEWAY_API_KEY")]
    [Arguments("OPENAI_API_KEY")]
    [Arguments("ANTHROPIC_API_KEY")]
    [Arguments("CODEX_API_KEY")]
    public async Task InstalledCredentialNamesRefuseEvenWhenEmpty(string name)
    {
        using var fixture = new ReadinessFixture();
        foreach (var value in new[] { "", ReadinessFixture.Secret })
        {
            fixture.Target["Config"]!["Env"] = new JsonArray(name + "=" + value);
            await Refuses(fixture.Check, "credentials");
        }
        await Assert.That(fixture.Calls.All(args => args[0] == "inspect")).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("http://127.0.0.1:18770")]
    [Arguments("https://zeroshot.dev.faviann.com:8443")]
    [Arguments("https://zeroshot.dev.faviann.com:443")]
    [Arguments("https://zeroshot.dev.faviann.com/")]
    [Arguments("https://ZEROSHOT.dev.faviann.com")]
    [Arguments("https://user:secret@zeroshot.dev.faviann.com")]
    [Arguments("https://zeroshot.dev.faviann.com?key=secret")]
    [Arguments("https://192.0.2.10")]
    public async Task NoncanonicalRecordedOriginsRefuseBeforeTargetAccess(string origin)
    {
        using var fixture = new ReadinessFixture();
        await Refuses(() => fixture.Readiness.CheckAsync(fixture.Inventory with { DirectOrigin = origin }, origin,
            fixture.RootCertificate), "HTTPS origin");
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("same", "separate locations")]
    [Arguments("nested", "separate locations")]
    [Arguments("relative", "canonical absolute")]
    [Arguments("duplicate-container", "inventory is invalid")]
    [Arguments("network", "inventory is invalid")]
    public async Task InvalidInventoryRefusesBeforeTargetAccess(string change, string message)
    {
        using var fixture = new ReadinessFixture();
        var inventory = change switch
        {
            "same" => fixture.Inventory with { RootKeyMount = fixture.Inventory.RootCertificateMount },
            "nested" => fixture.Inventory with { RootKeyMount = Path.Combine(fixture.Inventory.RootCertificateMount, "key") },
            "relative" => fixture.Inventory with { RootKeyMount = "tls-root-key" },
            "duplicate-container" => fixture.Inventory with { BroodlingContainerName = fixture.Inventory.TlsContainerName },
            _ => fixture.Inventory with { Network = "" }
        };
        await Refuses(() => fixture.Readiness.CheckAsync(inventory, inventory.DirectOrigin, fixture.RootCertificate), message);
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("native", "version")]
    [Arguments("codex", "version")]
    [Arguments("node", "version")]
    [Arguments("gh", "version")]
    [Arguments("native-hash", "executable bytes")]
    [Arguments("gh-hash", "executable bytes")]
    [Arguments("slurp", "lacks api")]
    [Arguments("uid", "invalid")]
    public async Task ActualRuntimeDriftAndHostedIdentityFailureRefuse(string change, string message)
    {
        using var fixture = new ReadinessFixture { RuntimeFailure = change };
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StockOptionalDiscoveryFieldsMayBeNullOrEmpty()
    {
        using var fixture = new ReadinessFixture();
        foreach (var name in new[] { "privateBootstrapPath", "oauth", "loginSession" }) fixture.Discovery[name] = null;
        fixture.Discovery["extensions"] = new JsonObject();
        await Assert.That((await fixture.Check()).Ready).IsTrue();
    }

    [Test]
    [Arguments("kind")]
    [Arguments("authentication")]
    [Arguments("audience")]
    [Arguments("runPath")]
    [Arguments("sessionPath")]
    [Arguments("oecpPath")]
    [Arguments("privateBootstrapPath")]
    [Arguments("oauth")]
    [Arguments("unknown")]
    [Arguments("extensions")]
    [Arguments("duplicate")]
    [Arguments("malformed")]
    [Arguments("http-failure")]
    [Arguments("redirect")]
    [Arguments("transport")]
    public async Task DiscoveryMismatchOrFailureNeverReportsReady(string change)
    {
        using var fixture = new ReadinessFixture();
        switch (change)
        {
            case "extensions": fixture.Discovery["extensions"] = new JsonObject { ["profile"] = ReadinessFixture.Secret }; break;
            case "duplicate": fixture.DiscoveryText = fixture.Discovery.ToJsonString()[..^1] + ",\"kind\":\"zeroshot.native-v2-target/v2\"}"; break;
            case "malformed": fixture.DiscoveryText = ReadinessFixture.Secret; break;
            case "http-failure": fixture.DiscoveryStatus = HttpStatusCode.InternalServerError; break;
            case "redirect": fixture.DiscoveryStatus = HttpStatusCode.Redirect; break;
            case "transport": fixture.DiscoveryThrows = true; break;
            default: fixture.Discovery[change] = ReadinessFixture.Secret; break;
        }
        await Refuses(fixture.Check, "discovery");
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(1);
    }

    [Test]
    public async Task StalledDiscoveryTimesOutAsNotReadyButCallerCancellationRemainsCancellation()
    {
        using var fixture = new ReadinessFixture { DiscoveryStalls = true };
        var check = fixture.Check();
        await fixture.DiscoveryStarted.Task;
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        await Refuses(() => check, "unavailable or invalid");

        using var stalled = new ReadinessFixture { DiscoveryStalls = true };
        using var cancellation = new CancellationTokenSource();
        var cancelled = stalled.Readiness.CheckAsync(stalled.Inventory, stalled.Inventory.DirectOrigin, stalled.RootCertificate,
            cancellation.Token);
        await stalled.DiscoveryStarted.Task;
        cancellation.Cancel();
        await Assert.That(async () => await cancelled).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ThinCommandUsesInvocationConfigurationAndRemainsIndependentOfStoreAndPython()
    {
        using var fixture = new ReadinessFixture();
        var output = new StringWriter();
        var code = await HostTargetCommands.RunAsync(fixture.Arguments, output, fixture.Readiness);
        await Assert.That(code).IsEqualTo(0);
        using var result = JsonDocument.Parse(output.ToString());
        await Assert.That(result.RootElement.GetProperty("ready").GetBoolean()).IsTrue();
        await Assert.That(result.RootElement.GetProperty("providerTasks").GetInt32()).IsEqualTo(0);
        await Assert.That(fixture.Calls.Count).IsEqualTo(9);
        // Discovery trusts the invocation configuration's root, as invocation itself does.
        await Assert.That(fixture.TrustedRoot).IsEqualTo(fixture.RootCertificate);
        await Assert.That(Directory.GetFiles(fixture.Root).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments("different-origin")]
    [Arguments("missing-origin")]
    [Arguments("unknown-config")]
    [Arguments("local-field")]
    [Arguments("local-config")]
    [Arguments("credential-config")]
    [Arguments("credential-inventory")]
    [Arguments("missing-inventory")]
    [Arguments("malformed-config")]
    [Arguments("missing-root")]
    [Arguments("other-root")]
    public async Task InvalidOrExtendedInputRefusesBeforeTargetAccessWithoutLeakingBytes(string change)
    {
        using var fixture = new ReadinessFixture();
        var config = JsonNode.Parse(File.ReadAllText(fixture.Arguments[2]))!;
        switch (change)
        {
            case "different-origin": config["directOrigin"] = "https://other.dev.faviann.com"; break;
            case "missing-origin": config.AsObject().Remove("directOrigin"); break;
            case "unknown-config": config["extra"] = ReadinessFixture.Secret; break;
            case "local-field": config["pythonExecutable"] = "/unavailable-python"; break;
            case "local-config":
                config = JsonNode.Parse("""
                    {"target":"local","pythonExecutable":"/p","stateDirectory":"/s","workspaceRoot":"/w",
                     "realCodex":"/c","profileHome":"/h","codexHome":"/ch","launcher":"/l/codex"}
                    """)!;
                break;
            case "credential-config": config["gatewayApiKey"] = ReadinessFixture.Secret; break;
            case "credential-inventory":
                var inventory = JsonNode.Parse(File.ReadAllText(fixture.Arguments[1]))!;
                inventory["GH_TOKEN"] = ReadinessFixture.Secret;
                File.WriteAllText(fixture.Arguments[1], inventory.ToJsonString());
                break;
            case "missing-inventory": File.Delete(fixture.Arguments[1]); break;
            // Readiness must check the root the stack mounts, not system trust or another copy.
            case "missing-root": config.AsObject().Remove("directRootCertificate"); break;
            case "other-root": config["directRootCertificate"] = "/srv/broodling/zeroshot-tls-root.crt"; break;
        }
        File.WriteAllText(fixture.Arguments[2], change == "malformed-config" ? ReadinessFixture.Secret : config.ToJsonString());
        var output = new StringWriter();
        await Assert.That(await HostTargetCommands.RunAsync(fixture.Arguments, output, fixture.Readiness)).IsEqualTo(1);
        using var result = JsonDocument.Parse(output.ToString());
        await Assert.That(result.RootElement.GetProperty("ready").GetBoolean()).IsFalse();
        await Assert.That(output.ToString().Contains(ReadinessFixture.Secret)).IsFalse();
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ControlledProcessFailureAndSpawnFailureHideAllRawDiagnostics()
    {
        await Refuses(async () => { await TargetReadiness.RunCommandAsync("/bin/sh",
            ["-c", "printf 'secret sentinel'; printf 'secret sentinel' >&2; exit 1"], default); return null!; }, "command failed");
        await Refuses(async () => { await TargetReadiness.RunCommandAsync("/missing-secret-sentinel", [], default); return null!; }, "command failed");
        await Assert.That(await TargetReadiness.RunCommandAsync("/bin/sh", ["-c", "printf '  inspected  \\n'"], default)).IsEqualTo("inspected");
    }

    [Test]
    public async Task CancellationReturnsSafeNonreadyHandbackWithoutTargetAccess()
    {
        using var fixture = new ReadinessFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var output = new StringWriter();
        await Assert.That(await HostTargetCommands.RunAsync(fixture.Arguments, output, fixture.Readiness, cancellation.Token)).IsEqualTo(130);
        await Assert.That(output.ToString()).Contains("\"ready\":false");
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ProgramRoutesCheckTargetWithoutStartingHttpOrOpeningAStore()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(HostTargetCommands).Assembly.Location);
        start.ArgumentList.Add("check-target"); // Usage path cannot reach Docker or HTTP.
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: false); }
        await Assert.That(process.ExitCode).IsEqualTo(2);
        await Assert.That(await output).Contains("Usage: check-target");
        await Assert.That(await error).IsEqualTo("");
    }

    /// <summary>
    /// The actual images and host Docker: the ADR stack is ready, a rotation that kept Caddy's stored
    /// intermediate is caught through the origin (and native's own client refuses it too), and the
    /// documented rotation restores readiness while the target keeps running.
    /// </summary>
    [Test]
    public async Task ActualStackIsReadyAndAStaleIntermediateAfterIncompleteRotationIsNot()
    {
        await using var stack = await TargetStack.CreateAsync();
        await stack.InitializeAll();
        await stack.StartTarget();
        await stack.StartBroodling();
        await stack.ServingNativeList();
        var inventory = await stack.Inventory();
        var readiness = new TargetReadiness();
        Task<TargetReadinessFacts> Check() => readiness.CheckAsync(inventory, TargetStack.Origin, stack.RootCertificateFile);
        await Assert.That((await Check()).Ready).IsTrue();

        await stack.RotateRoot(complete: false);
        await Refuses(Check, "unavailable or invalid");
        await Assert.That((await stack.NativeList()).Code).IsNotEqualTo(0);

        await stack.RotateRoot(complete: true);
        await Assert.That((await Check()).Ready).IsTrue();
        await Assert.That(JsonDocument.Parse(await stack.ServingNativeList()).RootElement.GetProperty("runs").GetArrayLength()).IsEqualTo(0);
    }

    private static async Task Refuses(Func<Task<TargetReadinessFacts>> action, string message)
    {
        try { await action(); }
        catch (TargetNotReady exception)
        {
            await Assert.That(exception.Message.ToLowerInvariant()).Contains(message.ToLowerInvariant());
            await Assert.That(exception.ToString().Contains(ReadinessFixture.Secret)).IsFalse();
            await Assert.That(exception.InnerException).IsNull();
            return;
        }
        throw new InvalidOperationException("Expected target readiness refusal.");
    }

    private sealed class ReadinessFixture : HttpMessageHandler
    {
        internal const string Secret = "secret sentinel";
        internal const string Origin = "https://zeroshot.dev.faviann.com";
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(
            Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"),
            "target-readiness-" + Guid.NewGuid().ToString("N"))).FullName;
        internal TargetReadinessInventory Inventory { get; }
        internal string RootCertificate => Path.Combine(Inventory.RootCertificateMount, "root.crt");
        internal TargetReadiness Readiness { get; }
        internal JsonObject Target { get; }
        internal JsonObject Tls { get; }
        internal JsonObject Broodling { get; }
        internal JsonObject Discovery { get; } = JsonNode.Parse("""
            {"kind":"zeroshot.native-v2-target/v2","authentication":"none","runPath":"/native-v2/run",
             "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller"}
            """)!.AsObject();
        internal List<string[]> Calls { get; } = [];
        internal int DiscoveryCalls { get; private set; }
        internal IPEndPoint? Published { get; private set; }
        internal string? TrustedRoot { get; private set; }
        internal string? Inspect { get; set; }
        internal string? RuntimeFailure { get; set; }
        internal string? DiscoveryText { get; set; }
        internal HttpStatusCode DiscoveryStatus { get; set; } = HttpStatusCode.OK;
        internal bool DiscoveryThrows { get; set; }
        internal bool DiscoveryStalls { get; set; }
        internal TaskCompletionSource DiscoveryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal FakeTimeProvider Clock { get; } = new();
        internal string[] Arguments => ["check-target", Path.Combine(Root, "target.json"), Path.Combine(Root, "config.json")];

        internal ReadinessFixture()
        {
            Inventory = new("installation-target", "sha256:installed", Origin, Path.Combine(Root, "state"), Path.Combine(Root, "home"),
                "broodling_default", "installation-tls", Path.Combine(Root, "tls-root-key"), Path.Combine(Root, "tls-root"),
                "installation-broodling");
            static JsonObject Bind(string source, string destination, bool readWrite) =>
                new() { ["Type"] = "bind", ["RW"] = readWrite, ["Source"] = source, ["Destination"] = destination };
            Target = JsonNode.Parse("""
                {"Id":"selected-container-id","Image":"sha256:installed","State":{"Running":true},
                 "Config":{"User":"","Env":["HOME=/home/node","CODEX_HOME=/home/node/.codex"],
                    "Entrypoint":["/usr/local/bin/broodling-target"],
                    "Cmd":["--listen","0.0.0.0:18770","--public-origin","https://zeroshot.dev.faviann.com","--storage","/state"]},
                 "HostConfig":{"Privileged":false,"NetworkMode":"broodling_default","CapDrop":null,"RestartPolicy":{"Name":"no"},
                    "PortBindings":{},"PublishAllPorts":false},
                 "NetworkSettings":{"Ports":{},"Networks":{"broodling_default":{"Aliases":["zeroshot"]}}},
                 "Mounts":[]}
                """)!.AsObject();
            Target["Mounts"] = new JsonArray(Bind(Inventory.StateMount, "/state", true), Bind(Inventory.HomeMount, "/home/node", true),
                Bind(Inventory.RootCertificateMount, "/tls-root", false));
            Tls = JsonNode.Parse("""
                {"Id":"tls-container-id","Image":"sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b",
                 "State":{"Running":true},"Config":{"User":"10443:10443","Image":"TLS_IMAGE"},
                 "HostConfig":{"Privileged":false,"NetworkMode":"broodling_default","CapDrop":["ALL"],"CapAdd":["CAP_NET_BIND_SERVICE"],
                    "PortBindings":{"443/tcp":[{"HostIp":"127.0.0.1","HostPort":""}]},"PublishAllPorts":false},
                 "NetworkSettings":{"Ports":{"443/tcp":[{"HostIp":"127.0.0.1","HostPort":"18443"}],"80/tcp":null,"2019/tcp":null},
                    "Networks":{"broodling_default":{"Aliases":["zeroshot.dev.faviann.com","zeroshot-tls"]}}},
                 "Mounts":[]}
                """.Replace("TLS_IMAGE", TargetReadiness.TlsImage))!.AsObject();
            Tls["Mounts"] = new JsonArray(Bind(Inventory.RootKeyMount, "/tls-root-key", false),
                Bind(Inventory.RootCertificateMount, "/tls-root", false),
                new JsonObject { ["Type"] = "volume", ["RW"] = true, ["Source"] = "/var/lib/docker/volumes/caddy-data/_data", ["Destination"] = "/data" });
            Broodling = new JsonObject { ["Mounts"] = new JsonArray(Bind(Inventory.RootCertificateMount, "/tls-root", false)) };
            Readiness = new TargetReadiness(Command, DiscoveryClient, Clock);
            File.WriteAllText(Arguments[1], JsonSerializer.Serialize(Inventory, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            File.WriteAllText(Arguments[2], $$"""
                {"target":"direct","directOrigin":"{{Origin}}","directRootCertificate":"{{RootCertificate}}"}
                """);
        }

        internal Task<TargetReadinessFacts> Check() => Readiness.CheckAsync(Inventory, Inventory.DirectOrigin, RootCertificate);

        private HttpClient DiscoveryClient(Uri origin, string? root, IPEndPoint published)
        {
            Published = published;
            TrustedRoot = root;
            return new HttpClient(this, disposeHandler: false);
        }

        private Task<string> Command(IReadOnlyList<string> arguments, CancellationToken token)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.SequenceEqual(["inspect", "--type", "container", "installation-target", "installation-tls", "installation-broodling"]))
                return Task.FromResult(Inspect ?? $"[{Target},{Tls},{Broodling}]");
            if (!arguments.Take(2).SequenceEqual(["exec", "selected-container-id"])) throw new InvalidOperationException("Unexpected target operation");
            var args = arguments.Skip(2).ToArray();
            var key = string.Join(' ', args);
            var (label, output) = key switch
            {
                "/usr/local/bin/zeroshot --version" => ("native", "zeroshot 10.3.0"),
                "/usr/local/bin/codex --version" => ("codex", "codex-cli 0.153.4"),
                "/usr/local/bin/node --version" => ("node", "v22.23.2"),
                "/usr/bin/gh --version" => ("gh", "gh version 2.101.0 (2026-09-15) " + Secret + "\nignored"),
                "sha256sum /usr/local/bin/zeroshot" => ("native-hash", "afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06  /usr/local/bin/zeroshot"),
                "sha256sum /usr/bin/gh" => ("gh-hash", "ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff  /usr/bin/gh"),
                "/usr/bin/gh api graphql --paginate --slurp --help" => ("slurp", "FLAGS\n    --slurp Wrap pages"),
                "python3 -c import os; os.setgroups([10002]); os.setgid(10002); os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002" => ("uid", ""),
                _ => throw new InvalidOperationException("Unexpected target exec")
            };
            if (RuntimeFailure == label && label == "uid") throw new InvalidOperationException(Secret);
            return Task.FromResult(RuntimeFailure == label ? Secret : output);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri!.AbsoluteUri != Origin + "/.well-known/zeroshot-native-v2"
                || request.Headers.Authorization is not null || Calls.Count != 9)
                throw new InvalidOperationException("Unexpected discovery request");
            DiscoveryCalls++;
            DiscoveryStarted.TrySetResult();
            if (DiscoveryStalls) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (DiscoveryThrows) throw new HttpRequestException(Secret);
            var response = new HttpResponseMessage(DiscoveryStatus) { Content = new StringContent(DiscoveryText ?? Discovery.ToJsonString()) };
            if (DiscoveryStatus == HttpStatusCode.Redirect) response.Headers.Location = new Uri("https://unexpected.invalid");
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Directory.Delete(Root, recursive: true);
            base.Dispose(disposing);
        }
    }
}
