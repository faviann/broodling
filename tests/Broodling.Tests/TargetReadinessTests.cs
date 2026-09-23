using Broodling.Host;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        await Assert.That(fixture.Calls[0].SequenceEqual(["inspect", "installation-target"])).IsTrue();
        await Assert.That(fixture.Calls.Skip(1).All(args => args.Take(2).SequenceEqual(["exec", "selected-container-id"]))).IsTrue();
        await Assert.That(fixture.Calls[^1].SequenceEqual(["exec", "selected-container-id", "python3", "-c",
            "import os; os.setgroups([10002]); os.setgid(10002); os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002"])).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(1);
        await Assert.That(JsonSerializer.Serialize(facts).Contains(ReadinessFixture.Secret)).IsFalse();
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
    [Arguments("mount-source", "mounts")]
    [Arguments("mount-destination", "mounts")]
    [Arguments("mount-ro", "mounts")]
    [Arguments("mount-type", "mounts")]
    [Arguments("mount-extra", "mounts")]
    [Arguments("mount-duplicate", "mounts")]
    [Arguments("port-public", "loopback")]
    [Arguments("port-wrong", "loopback")]
    [Arguments("port-extra", "loopback")]
    [Arguments("binding-extra", "loopback")]
    [Arguments("port-udp", "loopback")]
    [Arguments("zero-containers", "exactly one")]
    [Arguments("two-containers", "exactly one")]
    [Arguments("malformed", "invalid")]
    public async Task ContainerDriftRefusesBeforeAnyExecOrDiscovery(string change, string message)
    {
        using var fixture = new ReadinessFixture();
        var config = fixture.Container["Config"]!;
        var host = fixture.Container["HostConfig"]!;
        var mount = fixture.Container["Mounts"]![0]!;
        var ports = host["PortBindings"]!.AsObject();
        switch (change)
        {
            case "image": fixture.Container["Image"] = "other"; break;
            case "stopped": fixture.Container["State"]!["Running"] = false; break;
            case "user": config["User"] = "1000:1000"; break;
            case "privileged": host["Privileged"] = true; break;
            case "host-network": host["NetworkMode"] = "host"; break;
            case "cap-drop": host["CapDrop"] = new JsonArray("SETUID"); break;
            case "restart": host["RestartPolicy"]!["Name"] = "always"; break;
            case "entrypoint": config["Entrypoint"] = new JsonArray("other"); break;
            case "arguments": config["Cmd"]![3] = "http://127.0.0.1:18771"; break;
            case "mount-source": mount["Source"] = "/other-state"; break;
            case "mount-destination": mount["Destination"] = "/other"; break;
            case "mount-ro": mount["RW"] = false; break;
            case "mount-type": mount["Type"] = "volume"; break;
            case "mount-extra": fixture.Container["Mounts"]!.AsArray().Add(mount.DeepClone()); break;
            case "mount-duplicate": fixture.Container["Mounts"]![1] = mount.DeepClone(); break;
            case "port-public": ports["18767/tcp"]![0]!["HostIp"] = "0.0.0.0"; break;
            case "port-wrong": ports["18767/tcp"]![0]!["HostPort"] = "18771"; break;
            case "port-extra": ports["1234/tcp"] = ports["18767/tcp"]!.DeepClone(); break;
            case "binding-extra": ports["18767/tcp"]!.AsArray().Add(ports["18767/tcp"]![0]!.DeepClone()); break;
            case "port-udp": ports["18767/udp"] = ports["18767/tcp"]!.DeepClone(); ports.Remove("18767/tcp"); break;
            case "zero-containers": fixture.Inspect = "[]"; break;
            case "two-containers": fixture.Inspect = $"[{fixture.Container},{fixture.Container}]"; break;
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
            fixture.Container["Config"]!["Env"] = new JsonArray(name + "=" + value);
            await Refuses(fixture.Check, "credentials");
        }
        await Assert.That(fixture.Calls.All(args => args[0] == "inspect")).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("http://localhost:18770")]
    [Arguments("http://127.0.0.1")]
    [Arguments("http://127.0.0.1:0")]
    [Arguments("http://127.0.0.1:65536")]
    [Arguments("http://127.0.0.1:18770/")]
    [Arguments("http://127.0.0.1:18770?key=secret")]
    [Arguments("http://user:secret@127.0.0.1:18770")]
    [Arguments("http://127.0.0.1:18770#fragment")]
    [Arguments("https://127.0.0.1:18770")]
    [Arguments("http://127.0.0.1:018770")]
    public async Task NoncanonicalRecordedOriginsRefuseBeforeTargetAccess(string origin)
    {
        using var fixture = new ReadinessFixture();
        await Refuses(() => fixture.Readiness.CheckAsync(fixture.Inventory with { DirectOrigin = origin }, origin), "loopback");
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
    [Arguments("kind")]
    [Arguments("authentication")]
    [Arguments("oecpPath")]
    [Arguments("malformed")]
    [Arguments("http-failure")]
    [Arguments("redirect")]
    [Arguments("transport")]
    public async Task DiscoveryMismatchOrFailureNeverReportsReady(string change)
    {
        using var fixture = new ReadinessFixture();
        switch (change)
        {
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
        await Assert.That(Directory.GetFiles(fixture.Root).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments("different-origin")]
    [Arguments("missing-origin")]
    [Arguments("unknown-config")]
    [Arguments("credential-config")]
    [Arguments("credential-inventory")]
    [Arguments("missing-inventory")]
    [Arguments("malformed-config")]
    public async Task InvalidOrExtendedInputRefusesBeforeTargetAccessWithoutLeakingBytes(string change)
    {
        using var fixture = new ReadinessFixture();
        var config = JsonNode.Parse(File.ReadAllText(fixture.Arguments[2]))!;
        switch (change)
        {
            case "different-origin": config["directOrigin"] = "http://127.0.0.1:18771"; break;
            case "missing-origin": config.AsObject().Remove("directOrigin"); break;
            case "unknown-config": config["extra"] = ReadinessFixture.Secret; break;
            case "credential-config": config["gatewayApiKey"] = ReadinessFixture.Secret; break;
            case "credential-inventory":
                var inventory = JsonNode.Parse(File.ReadAllText(fixture.Arguments[1]))!;
                inventory["GH_TOKEN"] = ReadinessFixture.Secret;
                File.WriteAllText(fixture.Arguments[1], inventory.ToJsonString());
                break;
            case "missing-inventory": File.Delete(fixture.Arguments[1]); break;
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
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(
            Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"),
            "target-readiness-" + Guid.NewGuid().ToString("N"))).FullName;
        internal TargetReadinessInventory Inventory { get; }
        internal TargetReadiness Readiness { get; }
        internal JsonObject Container { get; }
        internal JsonObject Discovery { get; } = JsonNode.Parse("""
            {"kind":"zeroshot.native-v2-target/v2","authentication":"none","oecpPath":"/native-v2/oecp"}
            """)!.AsObject();
        internal List<string[]> Calls { get; } = [];
        internal int DiscoveryCalls { get; private set; }
        internal string? Inspect { get; set; }
        internal string? RuntimeFailure { get; set; }
        internal string? DiscoveryText { get; set; }
        internal HttpStatusCode DiscoveryStatus { get; set; } = HttpStatusCode.OK;
        internal bool DiscoveryThrows { get; set; }
        internal string[] Arguments => ["check-target", Path.Combine(Root, "target.json"), Path.Combine(Root, "config.json")];
        private readonly HttpClient client;

        internal ReadinessFixture()
        {
            Inventory = new("installation-target", "sha256:installed", "http://127.0.0.1:18770", Path.Combine(Root, "state"), Path.Combine(Root, "home"));
            Container = JsonNode.Parse("""
                {"Id":"selected-container-id","Image":"sha256:installed","State":{"Running":true},
                 "Config":{"User":"","Env":["HOME=/home/node","CODEX_HOME=/home/node/.codex"],
                    "Entrypoint":["/usr/local/bin/broodling-target"],
                    "Cmd":["--listen","0.0.0.0:18767","--public-origin","http://127.0.0.1:18770","--storage","/state"]},
                 "HostConfig":{"Privileged":false,"NetworkMode":"bridge","CapDrop":null,"RestartPolicy":{"Name":"no"},
                    "PortBindings":{"18767/tcp":[{"HostIp":"127.0.0.1","HostPort":"18770"}]}},
                 "Mounts":[]}
                """)!.AsObject();
            foreach (var (source, destination) in new[] { (Inventory.StateMount, "/state"), (Inventory.HomeMount, "/home/node") })
                Container["Mounts"]!.AsArray().Add(new JsonObject { ["Type"] = "bind", ["RW"] = true, ["Source"] = source, ["Destination"] = destination });
            client = new HttpClient(this, disposeHandler: false);
            Readiness = new TargetReadiness(Command, client);
            File.WriteAllText(Arguments[1], JsonSerializer.Serialize(Inventory, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            File.WriteAllText(Arguments[2], """
                {"pythonExecutable":"/unavailable-python","stateDirectory":"/unavailable-state","workspaceRoot":"/unavailable-workspaces",
                 "directOrigin":"http://127.0.0.1:18770"}
                """);
        }

        internal Task<TargetReadinessFacts> Check() => Readiness.CheckAsync(Inventory, Inventory.DirectOrigin);

        private Task<string> Command(IReadOnlyList<string> arguments, CancellationToken token)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.SequenceEqual(["inspect", "installation-target"])) return Task.FromResult(Inspect ?? $"[{Container}]");
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri!.OriginalString != Inventory.DirectOrigin + "/.well-known/zeroshot-native-v2"
                || request.Headers.Authorization is not null || Calls.Count != 9)
                throw new InvalidOperationException("Unexpected discovery request");
            DiscoveryCalls++;
            if (DiscoveryThrows) throw new HttpRequestException(Secret);
            var response = new HttpResponseMessage(DiscoveryStatus) { Content = new StringContent(DiscoveryText ?? Discovery.ToJsonString()) };
            if (DiscoveryStatus == HttpStatusCode.Redirect) response.Headers.Location = new Uri("https://unexpected.invalid");
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { client.Dispose(); Directory.Delete(Root, recursive: true); }
            base.Dispose(disposing);
        }
    }
}
