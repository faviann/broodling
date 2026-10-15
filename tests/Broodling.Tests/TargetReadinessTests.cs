using Broodling.Host;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
        var report = await fixture.Check();
        var facts = report.Targets.Single();
        await Assert.That(facts.Environment).IsEqualTo("dotnet");
        await Assert.That(facts.ContainerName).IsEqualTo("installation-dotnet");
        await Assert.That(facts.ContainerId).IsEqualTo(ReadinessFixture.IdFor("installation-dotnet"));
        await Assert.That(facts.ImageId).IsEqualTo("sha256:image-dotnet");
        await Assert.That(facts.DirectOrigin).IsEqualTo("https://zeroshot-dotnet.dev.faviann.com");
        await Assert.That(facts.Release).IsEqualTo("dotnet-r2");
        await Assert.That(facts.Os).IsEqualTo("Debian GNU/Linux 13 (trixie)");
        await Assert.That(facts.Versions["git"]).IsEqualTo(TestEnvironments.Adopted.Environment.Components.Git);
        await Assert.That(fixture.Calls.Count).IsEqualTo(fixture.ExpectedCalls);
        await Assert.That(fixture.Calls[0].SequenceEqual(["inspect", "--type", "container", "installation-dotnet", "installation-tls",
            "installation-broodling"])).IsTrue();
        await Assert.That(fixture.Calls[1].SequenceEqual(["image", "inspect", "sha256:image-dotnet"])).IsTrue();
        await Assert.That(fixture.Calls[2].SequenceEqual(["network", "inspect", "broodling_default"])).IsTrue();
        // One live probe of the OS identity and every manifest tool, inside the selected container.
        await Assert.That(fixture.Calls[4].SequenceEqual(["exec", facts.ContainerId, "/bin/sh", "-c",
            EnvironmentProbe.Sections(EnvironmentProbe.For(TestEnvironments.Adopted.Environment))])).IsTrue();
        await Assert.That(fixture.Calls.Skip(4).All(args => args.Take(2).SequenceEqual(["exec", facts.ContainerId]))).IsTrue();
        await Assert.That(fixture.Calls[^1].SequenceEqual(["exec", facts.ContainerId, "python3", "-c",
            "import os; os.setgroups([10002]); os.setgid(10002); os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002"])).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(1);
        // Discovery reaches zeroshot-tls's actual publication and trusts the configured root.
        await Assert.That(fixture.Published).IsEqualTo(new IPEndPoint(IPAddress.Loopback, 18443));
        await Assert.That(fixture.TrustedRoot).IsEqualTo(fixture.RootCertificate);
        // Then an unauthenticated control request, which must be refused, and one with the configured token.
        await Assert.That(fixture.SessionBearers.SequenceEqual([null, fixture.Token])).IsTrue();
        await Assert.That(JsonSerializer.Serialize(report).Contains(ReadinessFixture.Secret)).IsFalse();
        await Assert.That(JsonSerializer.Serialize(report).Contains(fixture.Token)).IsFalse();
    }

    /// <summary>
    /// The whole installation: each catalog target is checked as its own container, alias, mounts, image and live
    /// environment, and reached at its own origin through the one zeroshot-tls with its own token only.
    /// </summary>
    [Test]
    public async Task EveryCatalogTargetIsCheckedAtItsOwnOriginWithItsOwnToken()
    {
        using var fixture = new ReadinessFixture("dotnet", "other");
        var report = await fixture.Check();
        await Assert.That(report.Targets.Select(facts => facts.Environment).SequenceEqual(["dotnet", "other"])).IsTrue();
        await Assert.That(report.Targets.Select(facts => facts.DirectOrigin).SequenceEqual(
            ["https://zeroshot-dotnet.dev.faviann.com", "https://zeroshot-other.dev.faviann.com"])).IsTrue();
        await Assert.That(report.Targets.Select(facts => facts.ContainerId).Distinct().Count()).IsEqualTo(2);
        await Assert.That(fixture.Calls.Count).IsEqualTo(fixture.ExpectedCalls);
        await Assert.That(fixture.Calls[0].SequenceEqual(["inspect", "--type", "container", "installation-dotnet", "installation-other",
            "installation-tls", "installation-broodling"])).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(2);
        await Assert.That(fixture.SessionBearers.SequenceEqual([null, fixture.Tokens["dotnet"], null, fixture.Tokens["other"]])).IsTrue();
        await Assert.That(fixture.SessionOrigins.SequenceEqual(["https://zeroshot-dotnet.dev.faviann.com", "https://zeroshot-dotnet.dev.faviann.com",
            "https://zeroshot-other.dev.faviann.com", "https://zeroshot-other.dev.faviann.com"])).IsTrue();
    }

    [Test]
    public async Task SourcelessTmpfsMountsShareNoStorage()
    {
        using var fixture = new ReadinessFixture();
        // Docker reports tmpfs with an empty Source, which is no host path at all.
        var tmpfs = new JsonObject { ["Type"] = "tmpfs", ["RW"] = true, ["Source"] = "", ["Destination"] = "/tmp" };
        fixture.Broodling["Mounts"]!.AsArray().Add(tmpfs);
        fixture.Tls["Mounts"]!.AsArray().Add(tmpfs.DeepClone());
        await fixture.Check();
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
    [Arguments("mount-extra-volume", "mounts")]
    [Arguments("mount-declared-bind", "mounts")]
    [Arguments("mount-duplicate", "mounts")]
    [Arguments("root-certificate-rw", "mounts")]
    [Arguments("root-key", "mounts")]
    [Arguments("bootstrap-key-missing", "mounts")]
    [Arguments("bootstrap-key-rw", "mounts")]
    [Arguments("bootstrap-key-source", "mounts")]
    [Arguments("port-published", "publish no port")]
    [Arguments("publish-all", "publish no port")]
    [Arguments("network", "zeroshot-dotnet alias")]
    [Arguments("target-alias", "zeroshot-dotnet alias")]
    [Arguments("former-alias", "zeroshot-dotnet alias")]
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
    [Arguments("tls-alias", "origin's alias")]
    [Arguments("tls-network", "origin's alias")]
    [Arguments("tls-key-source", "root key and certificate")]
    [Arguments("tls-key-rw", "root key and certificate")]
    [Arguments("tls-certificate-missing", "root key and certificate")]
    [Arguments("tls-data-in-public-root", "storage must not be inside the public root")]
    [Arguments("broodling-key", "broodling must not mount")]
    [Arguments("broodling-key-parent", "broodling must not mount")]
    [Arguments("broodling-caddy-data", "broodling must not mount")]
    [Arguments("broodling-root-rw", "public root directory as a read-only bind")]
    [Arguments("broodling-bootstrap-key", "only the target may mount the bootstrap key")]
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
            // A volume is admitted only where the image declares one.
            case "mount-extra-volume": fixture.Target["Mounts"]!.AsArray().Add(ReadinessFixture.Volume("/extra")); break;
            case "mount-declared-bind": fixture.Target["Mounts"]![4] = Bind(fixture.Root, "/var/lib/zeroshot", true); break;
            case "mount-duplicate": fixture.Target["Mounts"]![1] = mount.DeepClone(); break;
            case "root-certificate-rw": fixture.Target["Mounts"]![2]!["RW"] = true; break;
            case "root-key": fixture.Target["Mounts"]![2]!["Source"] = fixture.Inventory.RootKeyMount; break;
            case "bootstrap-key-missing": fixture.Target["Mounts"]!.AsArray().RemoveAt(3); break;
            case "bootstrap-key-rw": fixture.Target["Mounts"]![3]!["RW"] = true; break;
            case "bootstrap-key-source": fixture.Target["Mounts"]![3]!["Source"] = Path.Combine(fixture.Root, "other-key"); break;
            case "port-published": host["PortBindings"] = JsonNode.Parse("""{"18770/tcp":[{"HostIp":"127.0.0.1","HostPort":"18770"}]}"""); break;
            case "publish-all": host["PublishAllPorts"] = true; break;
            case "network": fixture.Target["NetworkSettings"]!["Networks"] = JsonNode.Parse("""{"other":{"Aliases":["zeroshot-dotnet"]}}"""); break;
            case "target-alias": fixture.Target["NetworkSettings"]!["Networks"]!["broodling_default"]!["Aliases"] = new JsonArray("other"); break;
            // ADR 0001's single-target alias no longer names any target.
            case "former-alias": fixture.Target["NetworkSettings"]!["Networks"]!["broodling_default"]!["Aliases"] = new JsonArray("zeroshot"); break;
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
            case "tls-network": tls["NetworkSettings"]!["Networks"] = JsonNode.Parse("""{"other":{"Aliases":["zeroshot-dotnet.dev.faviann.com"]}}"""); break;
            case "tls-key-source": tls["Mounts"]![0]!["Source"] = fixture.Root; break;
            case "tls-key-rw": tls["Mounts"]![0]!["RW"] = true; break;
            case "tls-certificate-missing": tls["Mounts"]!.AsArray().RemoveAt(1); break;
            case "tls-data-in-public-root": tls["Mounts"]![2] = Bind(Path.Combine(fixture.Inventory.RootCertificateMount, "data"), "/data", true); break;
            case "broodling-key": broodlingMounts.Add(Bind(fixture.Inventory.RootKeyMount, "/key", false)); break;
            case "broodling-key-parent": broodlingMounts.Add(Bind(fixture.Root, "/srv", false)); break;
            case "broodling-caddy-data": broodlingMounts.Add(tls["Mounts"]![2]!.DeepClone()); break;
            case "broodling-root-rw": broodlingMounts[0]!["RW"] = true; break;
            case "broodling-bootstrap-key": broodlingMounts.Add(Bind(fixture.BootstrapKeyFile, "/run/secrets/zeroshot-bootstrap-key", false)); break;
            case "two-containers": fixture.Inspect = $"[{fixture.Target},{fixture.Tls}]"; break;
            case "malformed": fixture.Inspect = ReadinessFixture.Secret; break;
        }
        await Refuses(fixture.Check, message);
        // Two containers or malformed inspection refuse before the image is inspected.
        await Assert.That(fixture.Calls.Count).IsEqualTo(change is "two-containers" or "malformed" ? 1 : 2);
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
        await Assert.That(fixture.Calls.All(args => args[0] is "inspect" or "image")).IsTrue();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    /// <summary>
    /// Each origin must be one zeroshot-tls serves: the catalog refuses noncanonical spellings, and readiness refuses
    /// a canonical origin that is not HTTPS on port 443 to a DNS name, before any target access.
    /// </summary>
    [Test]
    [Arguments("http://127.0.0.1:18770")]
    [Arguments("https://zeroshot-dotnet.dev.faviann.com:8443")]
    [Arguments("https://zeroshot-dotnet.dev.faviann.com:443")]
    [Arguments("https://zeroshot-dotnet.dev.faviann.com/")]
    [Arguments("https://ZEROSHOT-DOTNET.dev.faviann.com")]
    [Arguments("https://user:secret@zeroshot-dotnet.dev.faviann.com")]
    [Arguments("https://zeroshot-dotnet.dev.faviann.com?key=secret")]
    [Arguments("https://192.0.2.10")]
    public async Task NoncanonicalRecordedOriginsRefuseBeforeTargetAccess(string origin)
    {
        using var fixture = new ReadinessFixture();
        fixture.Configuration["environments"]![0]!["origin"] = origin;
        fixture.WriteConfiguration();
        EnvironmentCatalog catalog;
        try { catalog = InvocationConfiguration.Read(fixture.Arguments[2]); }
        catch (UnsupportedRuntime) { await Assert.That(fixture.Calls.Count).IsEqualTo(0); return; }
        await Refuses(() => fixture.Readiness.CheckAsync(fixture.Inventory, catalog), "HTTPS origin");
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("same", "separate locations")]
    [Arguments("nested", "separate locations")]
    [Arguments("relative", "canonical absolute")]
    [Arguments("bootstrap-key-relative", "canonical absolute")]
    [Arguments("duplicate-container", "inventory is invalid")]
    [Arguments("tls-is-target", "inventory is invalid")]
    [Arguments("network", "inventory is invalid")]
    [Arguments("no-target", "exactly the environments")]
    [Arguments("other-environment", "exactly the environments")]
    [Arguments("extra-environment", "exactly the environments")]
    [Arguments("repeated-environment", "exactly the environments")]
    [Arguments("bootstrap-key-in-state", "must not overlap")]
    [Arguments("root-in-home", "must not overlap")]
    public async Task InvalidInventoryRefusesBeforeTargetAccess(string change, string message)
    {
        using var fixture = new ReadinessFixture();
        var target = fixture.Inventory.Targets[0];
        var inventory = change switch
        {
            "same" => fixture.Inventory with { RootKeyMount = fixture.Inventory.RootCertificateMount },
            "nested" => fixture.Inventory with { RootKeyMount = Path.Combine(fixture.Inventory.RootCertificateMount, "key") },
            "relative" => fixture.Inventory with { RootKeyMount = "tls-root-key" },
            "bootstrap-key-relative" => fixture.Inventory with { Targets = [target with { BootstrapKeyFile = "zeroshot-bootstrap-key" }] },
            "duplicate-container" => fixture.Inventory with { BroodlingContainerName = fixture.Inventory.TlsContainerName },
            "tls-is-target" => fixture.Inventory with { TlsContainerName = "installation-dotnet" },
            "network" => fixture.Inventory with { Network = "" },
            // The inventory must state exactly the catalog's environments, each once.
            "no-target" => fixture.Inventory with { Targets = [] },
            "other-environment" => fixture.Inventory with { Targets = [target with { Environment = "other" }] },
            "extra-environment" => fixture.Inventory with { Targets = [target, target with { Environment = "other" }] },
            "repeated-environment" => fixture.Inventory with { Targets = [target, target] },
            "bootstrap-key-in-state" => fixture.Inventory with
                { Targets = [target with { BootstrapKeyFile = Path.Combine(fixture.StateMount("dotnet"), "key") }] },
            _ => fixture.Inventory with { RootCertificateMount = Path.Combine(fixture.HomeMount("dotnet"), "tls-root") }
        };
        await Refuses(() => fixture.Readiness.CheckAsync(inventory, fixture.Catalog), message);
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    /// <summary>
    /// Across targets: one target's bootstrap key may not lie in or be another's storage, a host path reaching
    /// another target's state through a link is refused as noncanonical, and no two targets may share a control token.
    /// All refuse before any target access.
    /// </summary>
    [Test]
    [Arguments("bootstrap-key-in-other-state", "must not overlap")]
    [Arguments("shared-bootstrap-key", "must not overlap")]
    [Arguments("state-linked-to-other", "canonical absolute")]
    [Arguments("shared-token", "own control token")]
    public async Task TargetsShareNoStorageOrToken(string change, string message)
    {
        using var fixture = new ReadinessFixture("dotnet", "other");
        var other = fixture.Inventory.Targets[1];
        switch (change)
        {
            case "bootstrap-key-in-other-state":
                fixture.Inventory = fixture.Inventory with
                    { Targets = [fixture.Inventory.Targets[0], other with { BootstrapKeyFile = Path.Combine(fixture.StateMount("dotnet"), "key") }] };
                break;
            case "shared-bootstrap-key":
                fixture.Inventory = fixture.Inventory with
                    { Targets = [fixture.Inventory.Targets[0], other with { BootstrapKeyFile = fixture.Inventory.Targets[0].BootstrapKeyFile }] };
                break;
            case "state-linked-to-other":
                Directory.CreateDirectory(fixture.StateMount("dotnet"));
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.StateMount("other"))!);
                File.CreateSymbolicLink(fixture.StateMount("other"), fixture.StateMount("dotnet"));
                break;
            default: File.WriteAllText(fixture.TokenFile("other"), fixture.Tokens["dotnet"]); break;
        }
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.Calls.Count + fixture.DiscoveryCalls).IsEqualTo(0);
    }

    /// <summary>
    /// Each target alias and each origin alias resolves to exactly one container on the project network: zeroshot-tls
    /// must carry every origin, and no other target, stray container or broodling may answer as either, which would
    /// load-balance or redirect one environment's work. All refuse before any exec or discovery.
    /// </summary>
    [Test]
    [Arguments("origin-alias-missing", "environment other: zeroshot-tls must carry the origin's alias")]
    [Arguments("target-alias-shared", "zeroshot-dotnet")]
    [Arguments("stray-target-alias", "zeroshot-other")]
    [Arguments("stray-origin-alias", "zeroshot-other.dev.faviann.com")]
    [Arguments("stray-named-as-alias", "zeroshot-dotnet")]
    [Arguments("broodling-origin-alias", "zeroshot-dotnet.dev.faviann.com")]
    public async Task EveryAliasNamesExactlyOneContainer(string change, string message)
    {
        using var fixture = new ReadinessFixture("dotnet", "other");
        JsonArray Aliases(JsonObject container) => container["NetworkSettings"]!["Networks"]!["broodling_default"]!["Aliases"]!.AsArray();
        switch (change)
        {
            case "origin-alias-missing": Aliases(fixture.Tls).RemoveAt(1); break;
            case "target-alias-shared": Aliases(fixture.Targets["other"]).Add("zeroshot-dotnet"); break;
            case "stray-target-alias": fixture.Strays.Add(ReadinessFixture.Stray("stray", "zeroshot-other")); break;
            case "stray-origin-alias": fixture.Strays.Add(ReadinessFixture.Stray("stray", "zeroshot-other.dev.faviann.com")); break;
            case "stray-named-as-alias": fixture.Strays.Add(ReadinessFixture.Stray("zeroshot-dotnet")); break;
            default: Aliases(fixture.Broodling).Add("zeroshot-dotnet.dev.faviann.com"); break;
        }
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.Calls.Any(args => args[0] == "exec")).IsFalse();
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    /// <summary>
    /// The adopted manifest is only its producer's statement: each target is ready only when its live OS identity and
    /// every tool the manifest states are present and match it. A missing or other tool, or another OS, refuses
    /// before discovery and names only the component.
    /// </summary>
    [Test]
    [Arguments("os", "live os identity differs")]
    [Arguments("native", "native is missing or differs")]
    [Arguments("codex", "codex is missing or differs")]
    [Arguments("harness-node", "harness-node is missing or differs")]
    [Arguments("node", "node is missing or differs")]
    [Arguments("gh", "gh is missing or differs")]
    [Arguments("git", "git is missing or differs")]
    [Arguments("python", "python is missing or differs")]
    [Arguments("claude", "claude is missing or differs")]
    [Arguments("copilot", "copilot is missing or differs")]
    [Arguments("dotnet-sdks", "dotnet-sdks is missing or differs")]
    [Arguments("dotnet-runtimes", "dotnet-runtimes is missing or differs")]
    [Arguments("missing-os", "live os identity differs")]
    [Arguments("missing-git", "git is missing or differs")]
    [Arguments("missing-dotnet-sdks", "dotnet-sdks is missing or differs")]
    [Arguments("extra-dotnet-sdk", "dotnet-sdks is missing or differs")]
    [Arguments("native-hash", "executable bytes")]
    [Arguments("restic-hash", "executable bytes")]
    [Arguments("entrypoint-hash", "executable bytes")]
    [Arguments("slurp", "lacks api")]
    [Arguments("uid", "invalid")]
    public async Task ActualRuntimeDriftAndHostedIdentityFailureRefuse(string change, string message)
    {
        using var fixture = new ReadinessFixture { RuntimeFailure = change };
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task OneTargetsLiveDriftRefusesTheWholeInstallation()
    {
        using var fixture = new ReadinessFixture("dotnet", "other") { RuntimeFailure = "os", RuntimeFailureTarget = "other" };
        await Refuses(fixture.Check, "environment other: the live os identity differs");
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    /// <summary>The target container must run the adopted image, pulled by its digest, on the supported platform.</summary>
    [Test]
    [Arguments("other-digest", "not the adopted environment image")]
    [Arguments("tag-only", "not the adopted environment image")]
    [Arguments("architecture", "platform is not supported")]
    [Arguments("os", "platform is not supported")]
    public async Task OtherImageOrPlatformRefusesBeforeAnyExec(string change, string message)
    {
        using var fixture = new ReadinessFixture();
        switch (change)
        {
            case "other-digest":
                fixture.Image["RepoDigests"] = new JsonArray("ghcr.io/faviann/zeroshot-environments/dotnet@sha256:" + new string('0', 64));
                break;
            case "tag-only": fixture.Image["RepoDigests"] = new JsonArray(); break;
            case "architecture": fixture.Image["Architecture"] = "arm64"; break;
            case "os": fixture.Image["Os"] = "windows"; break;
        }
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.Calls.Count).IsEqualTo(2);
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(0);
    }

    [Test]
    public async Task StockOptionalDiscoveryFieldsMayBeNullOrEmptyAndExtensionsAreIgnored()
    {
        using var fixture = new ReadinessFixture();
        foreach (var name in new[] { "oauth", "loginSession" }) fixture.Discovery[name] = null;
        fixture.Discovery["extensions"] = new JsonObject();
        await fixture.Check();
        // Native advertises capabilities this controller does not use.
        using var advertising = new ReadinessFixture();
        advertising.Discovery["extensions"] = JsonNode.Parse("""
            {"run_history":{"kind":"zeroshot.run-history/v1","baseUrl":"https://zeroshot-dotnet.dev.faviann.com"},
             "workspace_recovery":{"kind":"openengine.workspace-recovery/v1"}}
            """);
        await advertising.Check();
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
            case "extensions": fixture.Discovery["extensions"] = new JsonArray(ReadinessFixture.Secret); break;
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

    /// <summary>
    /// Public discovery alone never passes: the target must refuse unauthenticated control and accept exactly the
    /// configured token, and a missing or unreadable token refuses before any target access.
    /// </summary>
    [Test]
    [Arguments("unauthenticated-accepted", "accepts unauthenticated control")]
    [Arguments("unauthenticated-unavailable", "accepts unauthenticated control")]
    [Arguments("wrong-token", "refuses the configured control token")]
    [Arguments("missing-token", "no readable control token")]
    public async Task ControlMustBePrivateAndAcceptOnlyTheConfiguredToken(string change, string message)
    {
        using var fixture = new ReadinessFixture();
        switch (change)
        {
            // A target serving unauthenticated, as native's direct mode does, behind a private-looking discovery.
            case "unauthenticated-accepted":
                fixture.Unauthenticated = (HttpStatusCode.OK, """{"endpoint":"wss://zeroshot-dotnet.dev.faviann.com/native-v2/oecp"}""");
                break;
            case "unauthenticated-unavailable": fixture.Unauthenticated = (HttpStatusCode.ServiceUnavailable, """{"code":"target.unavailable","message":"busy"}"""); break;
            // Not bootstrapped with this token: never bootstrapped, restarted or rotated.
            case "wrong-token": fixture.Token = TestAccess.NewSecret(); break;
            case "missing-token": File.Delete(fixture.TokenFile("dotnet")); break;
        }
        await Refuses(fixture.Check, message);
        await Assert.That(fixture.DiscoveryCalls).IsEqualTo(change == "missing-token" ? 0 : 1);
        if (change == "missing-token") await Assert.That(fixture.Calls.Count).IsEqualTo(0);
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
        var cancelled = stalled.Readiness.CheckAsync(stalled.Inventory, stalled.Catalog, cancellation.Token);
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
        var target = result.RootElement.GetProperty("targets").EnumerateArray().Single();
        await Assert.That(target.GetProperty("environment").GetString()).IsEqualTo("dotnet");
        await Assert.That(target.GetProperty("release").GetString()).IsEqualTo("dotnet-r2");
        await Assert.That(target.GetProperty("os").GetString()).IsEqualTo("Debian GNU/Linux 13 (trixie)");
        await Assert.That(fixture.Calls.Count).IsEqualTo(fixture.ExpectedCalls);
        // Discovery trusts the invocation configuration's root, as invocation itself does.
        await Assert.That(fixture.TrustedRoot).IsEqualTo(fixture.RootCertificate);
        await Assert.That(Directory.GetFiles(fixture.Root).Length).IsEqualTo(2);
        await Assert.That(output.ToString().Contains(fixture.Token)).IsFalse();
        await Assert.That(output.ToString().Contains(fixture.TokenFile("dotnet"))).IsFalse();
    }

    [Test]
    [Arguments("other-environment")]
    [Arguments("missing-origin")]
    [Arguments("home-in-state")]
    [Arguments("unknown-config")]
    [Arguments("local-field")]
    [Arguments("credential-config")]
    [Arguments("credential-inventory")]
    [Arguments("manifest-inventory")]
    [Arguments("origin-inventory")]
    [Arguments("missing-targets")]
    [Arguments("missing-inventory")]
    [Arguments("malformed-config")]
    [Arguments("missing-root")]
    [Arguments("other-root")]
    public async Task InvalidOrExtendedInputRefusesBeforeTargetAccessWithoutLeakingBytes(string change)
    {
        using var fixture = new ReadinessFixture();
        var config = fixture.Configuration;
        var inventory = JsonNode.Parse(File.ReadAllText(fixture.Arguments[1]))!;
        switch (change)
        {
            // The inventory and the catalog must name the same environments.
            case "other-environment": config["environments"]![0]!["name"] = "other"; config["defaultEnvironment"] = "other"; break;
            case "missing-origin": config["environments"]![0]!.AsObject().Remove("origin"); break;
            case "home-in-state": config["environments"]![0]!["homeMount"] = Path.Combine(fixture.StateMount("dotnet"), "home"); break;
            case "unknown-config": config["extra"] = ReadinessFixture.Secret; break;
            case "local-field": config["pythonExecutable"] = "/unavailable-python"; break;
            case "credential-config": config["gatewayApiKey"] = ReadinessFixture.Secret; break;
            case "credential-inventory": inventory["GH_TOKEN"] = ReadinessFixture.Secret; break;
            // The manifest is the bundled adoption's and the origin the catalog's; the inventory states neither.
            case "manifest-inventory": inventory["environmentManifest"] = Path.Combine(fixture.Root, "manifest.json"); break;
            case "origin-inventory": inventory["targets"]![0]!["directOrigin"] = "https://zeroshot-dotnet.dev.faviann.com"; break;
            case "missing-targets": inventory.AsObject().Remove("targets"); break;
            case "missing-inventory": break;
            // Readiness must check the root the stack mounts, not system trust or another copy.
            case "missing-root": config.AsObject().Remove("directRootCertificate"); break;
            case "other-root": config["directRootCertificate"] = "/srv/broodling/zeroshot-tls-root.crt"; break;
        }
        if (change == "missing-inventory") File.Delete(fixture.Arguments[1]);
        else File.WriteAllText(fixture.Arguments[1], inventory.ToJsonString());
        if (change == "malformed-config") File.WriteAllText(fixture.Arguments[2], ReadinessFixture.Secret);
        else fixture.WriteConfiguration();
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
        await Refuses<string>(async () => { await TargetReadiness.RunCommandAsync("/bin/sh",
            ["-c", "printf 'secret sentinel'; printf 'secret sentinel' >&2; exit 1"], default); return null!; }, "command failed");
        await Refuses<string>(async () => { await TargetReadiness.RunCommandAsync("/missing-secret-sentinel", [], default); return null!; }, "command failed");
        await Assert.That(await TargetReadiness.RunCommandAsync("/bin/sh", ["-c", "printf '  inspected  \\n'"], default)).IsEqualTo("inspected");
    }

    /// <summary>The probe script attributes each component's output, so one missing tool cannot shift another's.</summary>
    [Test]
    public async Task ProbeSectionsAttributeMissingAndOtherComponents()
    {
        var probes = EnvironmentProbe.For(TestEnvironments.Adopted.Environment);
        string Output(Func<EnvironmentProbe, IEnumerable<string>> lines) =>
            string.Join('\n', probes.SelectMany(probe => lines(probe).Prepend(EnvironmentProbe.Marker + probe.Component)));
        await Assert.That(EnvironmentProbe.Mismatches(probes, Output(probe => probe.Expected))).IsEmpty();
        await Assert.That(EnvironmentProbe.Mismatches(probes,
            Output(probe => probe.Component == "git" ? [] : probe.Expected)).SequenceEqual(["git"])).IsTrue();
        await Assert.That(EnvironmentProbe.Mismatches(probes,
            Output(probe => probe.Component is "os" or "node" ? ["other"] : probe.Expected)).SequenceEqual(["os", "node"])).IsTrue();
        await Assert.That(EnvironmentProbe.Mismatches(probes, "")).Count().IsEqualTo(probes.Count);
        // The script runs every probe even when one fails, marking each one's output.
        var observed = await TargetReadiness.RunCommandAsync("/bin/sh", ["-c", EnvironmentProbe.Sections(
            [new("present", "echo yes", ["yes"]), new("missing", "/missing-tool --version", ["1"]), new("after", "echo ok", ["ok"])])], default);
        await Assert.That(observed).IsEqualTo($"{EnvironmentProbe.Marker}present\nyes\n{EnvironmentProbe.Marker}missing\n{EnvironmentProbe.Marker}after\nok");
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
    /// The actual images and host Docker: the stack is not ready until its running target is bootstrapped with the
    /// configured token, then ready with its live OS identity and tools equal to the adopted manifest's statement; a
    /// rotation that kept Caddy's stored intermediate is caught through the origin (and a control client refuses it
    /// too), and the documented TLS rotation restores readiness while the target keeps running with its installed token.
    /// </summary>
    [Test]
    public async Task ActualStackIsReadyAndAStaleIntermediateAfterIncompleteRotationIsNot()
    {
        await using var stack = await TargetStack.CreateAsync();
        await stack.InitializeAll();
        await stack.StartTarget();
        await stack.StartBroodling();
        await stack.Serving();
        var inventory = await stack.Inventory();
        var readiness = new TargetReadiness();
        Task<TargetReadinessReport> Check() => readiness.CheckAsync(inventory, stack.Catalog());
        // Private discovery and refused unauthenticated control are not enough before the token is installed.
        await Refuses(Check, "refuses the configured control token");
        await stack.Bootstrap();
        var report = await Check();
        await Assert.That(report.Targets.Single().Os).IsEqualTo(TestEnvironments.Adopted.Environment.Os);

        await stack.RotateRoot(complete: false);
        await Refuses(Check, "unavailable or invalid");
        await Assert.That(async () => await stack.Control(stack.Token)).Throws<HttpRequestException>();

        await stack.RotateRoot(complete: true);
        await Check();
        await Assert.That(await stack.Control(stack.Token)).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task Refuses<T>(Func<Task<T>> action, string message)
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

    /// <summary>
    /// A controlled installation of one target per environment behind one zeroshot-tls, as <c>docker</c> would report
    /// it, with its catalog configuration, inventory and token files on disk, and each target's discovery and control.
    /// </summary>
    private sealed class ReadinessFixture : HttpMessageHandler
    {
        internal const string Secret = "secret sentinel";
        internal const string Network = "broodling_default";
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(
            Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"),
            "target-readiness-" + Guid.NewGuid().ToString("N"))).FullName;
        internal IReadOnlyList<string> Names { get; }
        internal TargetReadinessInventory Inventory { get; set; }
        /// <summary>The invocation configuration; <see cref="WriteConfiguration"/> stores changes.</summary>
        internal JsonObject Configuration { get; }
        internal EnvironmentCatalog Catalog => InvocationConfiguration.Read(Arguments[2]);
        internal string RootCertificate => Path.Combine(Inventory.RootCertificateMount, "root.crt");
        internal TargetReadiness Readiness { get; }
        /// <summary>Each target container by environment, as <c>docker inspect</c> reports it.</summary>
        internal Dictionary<string, JsonObject> Targets { get; } = [];
        internal JsonObject Target => Targets[Names[0]];
        internal JsonObject Tls { get; }
        internal JsonObject Broodling { get; }
        /// <summary>Further containers on the project network.</summary>
        internal List<JsonObject> Strays { get; } = [];
        /// <summary>Each target's image as <c>docker image inspect</c> reports it.</summary>
        internal Dictionary<string, JsonObject> Images { get; } = [];
        internal JsonObject Image => Images[Names[0]];
        internal JsonObject Discovery { get; } = JsonNode.Parse("""
            {"kind":"zeroshot.native-v2-target/v2","authentication":"private_capability","runPath":"/native-v2/run",
             "sessionPath":"/native-v2/oecp-session","oecpPath":"/native-v2/oecp","audience":"controller",
             "privateBootstrapPath":"/native-v2/private-bootstrap"}
            """)!.AsObject();
        /// <summary>The token each target accepts, as if bootstrapped; its configured token file holds it too.</summary>
        internal Dictionary<string, string> Tokens { get; } = [];
        internal string Token { get => Tokens[Names[0]]; set => Tokens[Names[0]] = value; }
        internal string TokenFile(string name) => Path.Combine(Root, "tokens", name);
        internal string StateMount(string name) => Path.Combine(Root, "targets", name, "state");
        internal string HomeMount(string name) => Path.Combine(Root, "targets", name, "home");
        internal string BootstrapKeyFile => Inventory.Targets[0].BootstrapKeyFile;
        /// <summary>The target's reply to an unauthenticated session; null for native's own.</summary>
        internal (HttpStatusCode Status, string Body)? Unauthenticated { get; set; }
        internal List<string?> SessionBearers { get; } = [];
        internal List<string> SessionOrigins { get; } = [];
        private readonly HashSet<string> discovered = [];
        internal List<string[]> Calls { get; } = [];
        internal int DiscoveryCalls { get; private set; }
        internal IPEndPoint? Published { get; private set; }
        internal string? TrustedRoot { get; private set; }
        /// <summary>Replaces the first container inspection's output.</summary>
        internal string? Inspect { get; set; }
        /// <summary>
        /// A live drift in <see cref="RuntimeFailureTarget"/> (the first target unless given): a probe component
        /// reporting another value, <c>missing-&lt;component&gt;</c> reporting nothing, <c>extra-dotnet-sdk</c>, or a
        /// failing byte, GitHub CLI or identity check.
        /// </summary>
        internal string? RuntimeFailure { get; set; }
        internal string? RuntimeFailureTarget { get; set; }
        internal string? DiscoveryText { get; set; }
        internal HttpStatusCode DiscoveryStatus { get; set; } = HttpStatusCode.OK;
        internal bool DiscoveryThrows { get; set; }
        internal bool DiscoveryStalls { get; set; }
        internal TaskCompletionSource DiscoveryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal FakeTimeProvider Clock { get; } = new();
        internal string[] Arguments => ["check-target", Path.Combine(Root, "target.json"), Path.Combine(Root, "config.json")];
        /// <summary>Container inspection, each image, the network and its members, then six execs per target.</summary>
        internal int ExpectedCalls => 1 + Names.Count + 2 + 6 * Names.Count;

        internal static string IdFor(string name) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

        private static JsonObject Bind(string source, string destination, bool readWrite) =>
            new() { ["Type"] = "bind", ["RW"] = readWrite, ["Source"] = source, ["Destination"] = destination };

        internal ReadinessFixture(params string[] names)
        {
            Names = names.Length == 0 ? ["dotnet"] : names;
            Directory.CreateDirectory(Path.Combine(Root, "tokens"));
            Inventory = new(Network, "installation-tls", Path.Combine(Root, "tls-root-key"), Path.Combine(Root, "tls-root"),
                "installation-broodling", [.. Names.Select(name => new InventoryTarget(name, "sha256:image-" + name,
                    Path.Combine(Root, "secrets", name + "-bootstrap-key")))]);
            Configuration = new JsonObject
            {
                ["directRootCertificate"] = RootCertificate,
                ["defaultEnvironment"] = Names[0],
                ["environments"] = new JsonArray([.. Names.Select(name => (JsonNode)new JsonObject
                {
                    ["name"] = name, ["origin"] = TargetStack.OriginFor(name), ["controlTokenFile"] = TokenFile(name),
                    ["image"] = Adopted.Image, ["container"] = "installation-" + name, ["stateMount"] = StateMount(name),
                    ["homeMount"] = HomeMount(name)
                })])
            };
            foreach (var (name, index) in Names.Select((name, index) => (name, index)))
            {
                var container = "installation-" + name;
                var target = JsonNode.Parse("""
                    {"Image":"","State":{"Running":true},
                     "Config":{"User":"","Env":["HOME=/home/node","CODEX_HOME=/home/node/.codex"],
                        "Entrypoint":["/usr/local/bin/broodling-target"],"Cmd":[]},
                     "HostConfig":{"Privileged":false,"NetworkMode":"broodling_default","CapDrop":null,"RestartPolicy":{"Name":"no"},
                        "PortBindings":{},"PublishAllPorts":false},
                     "NetworkSettings":{"Ports":{},"Networks":{}},
                     "Mounts":[]}
                    """)!.AsObject();
                target["Id"] = IdFor(container);
                target["Name"] = "/" + container;
                target["Image"] = Inventory.Targets[index].ImageId;
                target["Config"]!["Cmd"] = new JsonArray([.. TargetStack.ArgumentsFor(TargetStack.OriginFor(name)).Select(value => (JsonNode)value)]);
                target["NetworkSettings"]!["Networks"]![Network] = new JsonObject { ["Aliases"] = new JsonArray("zeroshot-" + name) };
                // With the anonymous volume Docker creates for the image's declared, unused /var/lib/zeroshot.
                target["Mounts"] = new JsonArray(Bind(StateMount(name), "/state", true), Bind(HomeMount(name), "/home/node", true),
                    Bind(Inventory.RootCertificateMount, "/tls-root", false),
                    Bind(Inventory.Targets[index].BootstrapKeyFile, TargetReadiness.BootstrapKeySecret, false), Volume("/var/lib/zeroshot"));
                Targets[name] = target;
                Images[name] = new JsonObject
                {
                    ["Id"] = Inventory.Targets[index].ImageId, ["Os"] = "linux", ["Architecture"] = "amd64",
                    ["RepoDigests"] = new JsonArray(Adopted.Image),
                    ["Config"] = new JsonObject { ["Volumes"] = new JsonObject { ["/var/lib/zeroshot"] = new JsonObject() } }
                };
                Tokens[name] = TestAccess.NewSecret();
                File.WriteAllText(TokenFile(name), Tokens[name]);
            }
            Tls = JsonNode.Parse("""
                {"Name":"/installation-tls","Image":"sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b",
                 "State":{"Running":true},"Config":{"User":"10443:10443","Image":"TLS_IMAGE"},
                 "HostConfig":{"Privileged":false,"NetworkMode":"broodling_default","CapDrop":["ALL"],"CapAdd":["CAP_NET_BIND_SERVICE"],
                    "PortBindings":{"443/tcp":[{"HostIp":"127.0.0.1","HostPort":""}]},"PublishAllPorts":false},
                 "NetworkSettings":{"Ports":{"443/tcp":[{"HostIp":"127.0.0.1","HostPort":"18443"}],"80/tcp":null,"2019/tcp":null},
                    "Networks":{}},
                 "Mounts":[]}
                """.Replace("TLS_IMAGE", TargetReadiness.TlsImage))!.AsObject();
            Tls["Id"] = IdFor("installation-tls");
            Tls["NetworkSettings"]!["Networks"]![Network] = new JsonObject
            {
                ["Aliases"] = new JsonArray([.. Names.Select(name => (JsonNode)new Uri(TargetStack.OriginFor(name)).Host), "zeroshot-tls"])
            };
            Tls["Mounts"] = new JsonArray(Bind(Inventory.RootKeyMount, "/tls-root-key", false),
                Bind(Inventory.RootCertificateMount, "/tls-root", false),
                new JsonObject { ["Type"] = "volume", ["RW"] = true, ["Source"] = "/var/lib/docker/volumes/caddy-data/_data", ["Destination"] = "/data" });
            Broodling = new JsonObject
            {
                ["Id"] = IdFor("installation-broodling"), ["Name"] = "/installation-broodling",
                ["NetworkSettings"] = new JsonObject { ["Networks"] = new JsonObject { [Network] = new JsonObject { ["Aliases"] = new JsonArray("broodling") } } },
                ["Mounts"] = new JsonArray(Bind(Inventory.RootCertificateMount, "/tls-root", false))
            };
            Readiness = new TargetReadiness(Command, DiscoveryClient, Clock);
            File.WriteAllText(Arguments[1], JsonSerializer.Serialize(Inventory, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            WriteConfiguration();
        }

        internal void WriteConfiguration() => File.WriteAllText(Arguments[2], Configuration.ToJsonString());

        internal Task<TargetReadinessReport> Check() => Readiness.CheckAsync(Inventory, Catalog);

        /// <summary>An anonymous volume as <c>docker inspect</c> reports it.</summary>
        internal static JsonObject Volume(string destination) => new()
        {
            ["Type"] = "volume", ["Name"] = new string('a', 64), ["RW"] = true, ["Destination"] = destination,
            ["Source"] = "/var/lib/docker/volumes/" + new string('a', 64) + "/_data"
        };

        /// <summary>Another container on the project network, named <paramref name="name"/> with <paramref name="aliases"/>.</summary>
        internal static JsonObject Stray(string name, params string[] aliases) => new()
        {
            ["Id"] = IdFor(name), ["Name"] = "/" + name,
            ["NetworkSettings"] = new JsonObject { ["Networks"] = new JsonObject
                { [Network] = new JsonObject { ["Aliases"] = new JsonArray([.. aliases.Select(alias => (JsonNode)alias)]) } } }
        };

        private IEnumerable<JsonObject> Containers => Targets.Values.Append(Tls).Append(Broodling).Concat(Strays);

        private HttpClient DiscoveryClient(Uri origin, string? root, IPEndPoint published)
        {
            Published = published;
            TrustedRoot = root;
            return new HttpClient(this, disposeHandler: false);
        }

        private Task<string> Command(IReadOnlyList<string> arguments, CancellationToken token)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Take(3).SequenceEqual(["inspect", "--type", "container"]))
            {
                if (Calls.Count == 1 && Inspect is not null) return Task.FromResult(Inspect);
                return Task.FromResult("[" + string.Join(",", arguments.Skip(3).Select(key => Containers.Single(container =>
                    (string)container["Id"]! == key || (string)container["Name"]! == "/" + key).ToJsonString())) + "]");
            }
            if (arguments.Take(2).SequenceEqual(["image", "inspect"]) && arguments.Count == 3)
                return Task.FromResult($"[{Images.Values.Single(image => (string)image["Id"]! == arguments[2])}]");
            if (arguments.SequenceEqual(["network", "inspect", Network]))
            {
                var attached = new JsonObject();
                foreach (var container in Containers.Where(container => container["NetworkSettings"]!["Networks"]!.AsObject().ContainsKey(Network)))
                    attached[(string)container["Id"]!] = new JsonObject { ["Name"] = ((string)container["Name"]!).TrimStart('/') };
                return Task.FromResult(new JsonArray(new JsonObject { ["Name"] = Network, ["Containers"] = attached }).ToJsonString());
            }
            if (arguments.Count < 3 || arguments[0] != "exec") throw new InvalidOperationException("Unexpected target operation");
            var name = Targets.Single(target => (string)target.Value["Id"]! == arguments[1]).Key;
            var failure = name == (RuntimeFailureTarget ?? Names[0]) ? RuntimeFailure : null;
            var args = arguments.Skip(2).ToArray();
            var probes = EnvironmentProbe.For(TestEnvironments.Adopted.Environment);
            if (args.SequenceEqual(["/bin/sh", "-c", EnvironmentProbe.Sections(probes)]))
                return Task.FromResult(string.Join('\n', probes.SelectMany(probe => (failure == probe.Component ? [Secret]
                    : failure == "missing-" + probe.Component ? []
                    : failure == "extra-dotnet-sdk" && probe.Component == "dotnet-sdks" ? probe.Expected.Append("9.0.100")
                    : probe.Expected).Prepend(EnvironmentProbe.Marker + probe.Component))));
            var (label, output) = string.Join(' ', args) switch
            {
                "sha256sum /usr/local/bin/zeroshot" => ("native-hash", "fd5ffef2d825d9035b3956b0026911a22e2c1b56f7fa205dd00310cfe88aeff5  /usr/local/bin/zeroshot"),
                "sha256sum /usr/local/bin/restic" => ("restic-hash", "90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf  /usr/local/bin/restic"),
                "sha256sum /usr/local/bin/broodling-target" => ("entrypoint-hash", "76c526fe8c51be094dbc48527c40181f5b0f23069996f3c4315c456a49107b79  /usr/local/bin/broodling-target"),
                "/usr/bin/gh api graphql --paginate --slurp --help" => ("slurp", "FLAGS\n    --slurp Wrap pages"),
                "python3 -c import os; os.setgroups([10002]); os.setgid(10002); os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002" => ("uid", ""),
                _ => throw new InvalidOperationException("Unexpected target exec")
            };
            if (failure == label && label == "uid") throw new InvalidOperationException(Secret);
            return Task.FromResult(failure == label ? Secret : output);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var origin = request.RequestUri!.GetLeftPart(UriPartial.Authority);
            var name = Names.Single(name => TargetStack.OriginFor(name) == origin);
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/native-v2/oecp-session" && discovered.Contains(origin))
            {
                // Like native's private mode: the session is control-authenticated and returns the installed token.
                var bearer = request.Headers.Authorization is { Scheme: "Bearer" } authorization ? authorization.Parameter : null;
                SessionBearers.Add(bearer);
                SessionOrigins.Add(origin);
                var (status, body) = bearer is null ? Unauthenticated ?? (HttpStatusCode.Unauthorized, """{"code":"request.unauthorized","message":"unauthorized"}""")
                    : bearer == Tokens[name] ? (HttpStatusCode.OK, $$"""{"endpoint":"wss://{{request.RequestUri.Host}}/native-v2/oecp","bearerToken":"{{bearer}}"}""")
                    : (HttpStatusCode.Unauthorized, """{"code":"request.unauthorized","message":"unauthorized"}""");
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            }
            if (request.Method != HttpMethod.Get || request.RequestUri.AbsolutePath != "/.well-known/zeroshot-native-v2"
                || request.Headers.Authorization is not null || Calls.Count != ExpectedCalls)
                throw new InvalidOperationException("Unexpected discovery request");
            DiscoveryCalls++;
            discovered.Add(origin);
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
