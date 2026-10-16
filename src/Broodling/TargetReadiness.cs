using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zeroshot.Native;

namespace Broodling;

/// <summary>
/// Operator inventory of the existing stack's host facts that the environment catalog does not hold: the project
/// network, the one <c>zeroshot-tls</c> container with the TLS root's key and certificate locations, the
/// <c>broodling</c> container, and per environment the target's local image and bootstrap key file. Neither
/// installation nor lifecycle authority. The catalog states each target's origin, container, adopted image and
/// state and home mounts.
/// </summary>
public sealed record TargetReadinessInventory(string Network, string TlsContainerName, string RootKeyMount,
    string RootCertificateMount, string BroodlingContainerName, IReadOnlyList<InventoryTarget> Targets);

/// <summary>
/// One target's host facts: its catalog environment, the local image identity its container runs, and the host file
/// it reads its bootstrap key from, which only that target and the bootstrap operation receive.
/// </summary>
public sealed record InventoryTarget(string Environment, string ImageId, string BootstrapKeyFile);

/// <summary>
/// One ready target. <c>Release</c> is the adopted environment release its image is, such as <c>dotnet-r5</c>;
/// <c>Os</c> and <c>Versions</c> are what the live target reported, each equal to its adopted manifest's statement.
/// </summary>
public sealed record TargetReadinessFacts(string Environment, string Release, string ContainerName, string ContainerId,
    string ImageId, string DirectOrigin, string Os, IReadOnlyDictionary<string, string> Versions);

/// <summary>Every configured target ready, with no provider work submitted.</summary>
public sealed record TargetReadinessReport(IReadOnlyList<TargetReadinessFacts> Targets);

public sealed class TargetNotReady(string message) : Exception(message);

/// <summary>
/// Inspect the whole installation: every catalog target against its adopted environment, the one
/// <c>zeroshot-tls</c> in front of them, and discovery and private control through each origin, without
/// submitting work or changing any lifecycle.
/// </summary>
public sealed class TargetReadiness
{
    /// <summary>The pinned <c>zeroshot-tls</c> image, exactly as the container must be created from it.</summary>
    internal const string TlsImage = "caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b";
    /// <summary>
    /// The project-network alias of the target serving <paramref name="environment"/>, where <c>zeroshot-tls</c>'s
    /// site for its origin forwards.
    /// </summary>
    internal static string Alias(string environment) => "zeroshot-" + environment;
    /// <summary><c>zeroshot-tls</c>'s non-root user, which alone owns the root key (the target entrypoint's <c>tls_user</c>).</summary>
    internal const string TlsUser = "10443:10443";
    /// <summary>Native's fixed inner listener on the project network, where <c>zeroshot-tls</c> forwards; never published.</summary>
    internal const string NativeListen = "0.0.0.0:18770";
    /// <summary>Where the target entrypoint reads its root-only bootstrap key, which it copies privately for native.</summary>
    internal const string BootstrapKeySecret = "/run/secrets/zeroshot-bootstrap-key";
    private static readonly string[] CredentialNames = ["GH_TOKEN", "GITHUB_TOKEN", "GATEWAY_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "CODEX_API_KEY",
        "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CODE_OAUTH_REFRESH_TOKEN"];
    private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(10);
    private static readonly Regex ContainerId = new("^[0-9a-f]{12,64}$", RegexOptions.CultureInvariant);
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<string>> command;
    private readonly Func<Uri, string?, IPEndPoint, HttpClient> discoveryClient;
    private readonly TimeProvider clock;

    public TargetReadiness() : this((arguments, token) => RunCommandAsync("docker", arguments, token), PublishedPortClient) { }

    internal TargetReadiness(Func<IReadOnlyList<string>, CancellationToken, Task<string>> command,
        Func<Uri, string?, IPEndPoint, HttpClient> discoveryClient, TimeProvider? clock = null)
    {
        this.command = command;
        this.discoveryClient = discoveryClient;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>One catalog environment and the inventory's host facts for its target.</summary>
    private sealed record Selected(ConfiguredEnvironment Entry, InventoryTarget Host)
    {
        internal string Name => Entry.Name;
        internal Uri Origin { get; } = new(Entry.Origin);
        internal TargetEnvironment Environment => Entry.Adoption.Environment;
    }

    /// <summary>
    /// <paramref name="environments"/> is the invocation configuration's catalog, and the inventory must name exactly
    /// its environments. Its root must be the stack's public <c>root.crt</c>, which every request trusts exactly as
    /// invocation does, and every origin's control token must be readable and its own: readiness requires private
    /// discovery, an unauthenticated control request refused and an authenticated one accepted at each origin,
    /// without submitting work. An adopted manifest is only its producer's statement; each target is ready only when
    /// its live OS identity and every component the manifest states match that statement.
    /// </summary>
    public async Task<TargetReadinessReport> CheckAsync(TargetReadinessInventory inventory, EnvironmentCatalog environments,
        CancellationToken cancellationToken = default)
    {
        var access = environments.Access;
        var rootCertificate = access.RootCertificate;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hosts = inventory.Targets ?? [];
            Require(hosts.Count > 0 && hosts.All(host => host is not null)
                    && hosts.Select(host => host.Environment).Distinct(StringComparer.Ordinal).Count() == hosts.Count
                    && hosts.Select(host => host.Environment).ToHashSet(StringComparer.Ordinal).SetEquals(environments.Entries.Select(entry => entry.Name)),
                "Target inventory must name exactly the environments the invocation configuration's catalog configures.");
            var targets = environments.Entries.Select(entry => new Selected(entry, hosts.Single(host => host.Environment == entry.Name))).ToArray();
            string[] names = [.. targets.Select(target => target.Entry.Target.Container), inventory.TlsContainerName, inventory.BroodlingContainerName];
            Require(names.All(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('-')) && names.Distinct().Count() == names.Length
                && targets.All(target => !string.IsNullOrWhiteSpace(target.Host.ImageId) && !target.Host.ImageId.StartsWith('-'))
                && !string.IsNullOrWhiteSpace(inventory.Network),
                "Target inventory is invalid.");
            // In-project clients reach zeroshot-tls through each origin's alias on the origin's port, where Caddy listens.
            foreach (var target in targets)
                Require(DirectTargetExchange.CanonicalOrigin(target.Entry.Origin) is { Scheme: "https", IsDefaultPort: true,
                    HostNameType: UriHostNameType.Dns }, target, "DirectTarget origin must be the canonical HTTPS origin, on port 443, that zeroshot-tls serves.");
            var owned = targets.SelectMany(target => new[] { target.Entry.Target.StateMount, target.Entry.Target.HomeMount, target.Host.BootstrapKeyFile })
                .ToArray();
            string[] roots = [inventory.RootKeyMount, inventory.RootCertificateMount];
            Require(owned.Concat(roots).All(path => !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && PhysicalPaths.Resolve(path) == path),
                "Target mount inventory must use canonical absolute paths.");
            Require(!Overlap(inventory.RootKeyMount, inventory.RootCertificateMount),
                "The root key and certificate must be recorded in separate locations.");
            // Each target's state, home and bootstrap key are its own: no other target, and neither root location, shares them.
            Require(owned.Select((path, index) => owned.Skip(index + 1).Concat(roots).All(other => !Overlap(path, other))).All(separate => separate),
                "Target state, home and bootstrap key paths must not overlap one another or the root locations, within or across targets.");
            Require(rootCertificate == Path.Combine(inventory.RootCertificateMount, "root.crt"),
                "Invocation configuration must trust the stack's public root, root.crt in the recorded root certificate mount.");
            var tokens = new Dictionary<Selected, string>();
            foreach (var target in targets)
            {
                try { tokens[target] = access.Token(target.Origin); }
                catch (NativeTransportError) { throw new TargetNotReady($"Invocation configuration names no readable control token for the {target.Name} origin."); }
            }
            // A token one target accepts must not work at another.
            Require(tokens.Values.Distinct(StringComparer.Ordinal).Count() == tokens.Count, "Each target must have its own control token; two targets share one.");

            using var inspection = JsonDocument.Parse(await command(["inspect", "--type", "container", .. names], cancellationToken));
            var records = inspection.RootElement;
            Require(records.GetArrayLength() == names.Length, "Expected exactly the recorded target, zeroshot-tls and broodling containers.");
            var (tls, broodling) = (records[targets.Length], records[targets.Length + 1]);
            for (var index = 0; index < targets.Length; index++)
                await RequireTargetContainer(targets[index], records[index], inventory, cancellationToken);

            var tlsHost = tls.GetProperty("HostConfig");
            Require(tls.GetProperty("Config").GetProperty("Image").GetString() == TlsImage, "zeroshot-tls must run the pinned image.");
            Require(tls.GetProperty("State").GetProperty("Running").GetBoolean(), "zeroshot-tls is stopped; start its existing container.");
            Require(tls.GetProperty("Config").TryGetProperty("User", out var tlsUser) && tlsUser.GetString() == TlsUser,
                "zeroshot-tls must run as its non-root user.");
            Require(OrdinaryIsolation(tlsHost), "zeroshot-tls must use ordinary Docker isolation.");
            Require(Capabilities(tlsHost, "CapDrop").SequenceEqual(["ALL"]) && Capabilities(tlsHost, "CapAdd").SequenceEqual(["NET_BIND_SERVICE"]),
                "zeroshot-tls must drop every capability except NET_BIND_SERVICE.");
            Require(Published(tlsHost).SequenceEqual(["443/tcp"]) && tls.GetProperty("NetworkSettings").GetProperty("Ports")
                    .GetProperty("443/tcp") is { ValueKind: JsonValueKind.Array } bindings && bindings.GetArrayLength() > 0,
                "zeroshot-tls must publish only its 443 port.");
            var published = PublishedEndpoint(tls.GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("443/tcp")[0]);
            foreach (var target in targets)
                Require(HasAlias(tls, inventory.Network, target.Origin.Host), target,
                    "zeroshot-tls must carry the origin's alias on the recorded project network.");
            var tlsMounts = Mounts(tls);
            Require(tlsMounts.Where(m => m.Destination is "/tls-root-key" or "/tls-root").ToHashSet().SetEquals([
                    new Mount(inventory.RootKeyMount, "/tls-root-key", false, true),
                    new Mount(inventory.RootCertificateMount, "/tls-root", false, true)]),
                "zeroshot-tls root key and certificate mounts differ from inventory.");
            // broodling and the targets see the public directory, so Caddy's data (its intermediate key) must stay outside it.
            Require(tlsMounts.All(mount => mount.Destination == "/tls-root" || !Overlap(mount.Source, inventory.RootCertificateMount)),
                "zeroshot-tls storage must not be inside the public root directory.");
            var broodlingMounts = Mounts(broodling);
            var publicRoot = broodlingMounts.Where(mount => mount.Source == inventory.RootCertificateMount).ToArray();
            Require(publicRoot.Length > 0 && publicRoot.All(mount => mount is { ReadWrite: false, Bind: true }),
                "broodling must mount the public root directory as a read-only bind.");
            // The key directory and Caddy's data (its intermediate key) are zeroshot-tls storage; only the public root is shared.
            Require(broodlingMounts.All(mount => mount.Source == inventory.RootCertificateMount
                    || tlsMounts.All(storage => !Overlap(mount.Source, storage.Source))),
                "broodling must not mount the root key or other zeroshot-tls storage.");
            // Only each target and the one-off bootstrap operation receive its bootstrap key.
            Require(broodlingMounts.Concat(tlsMounts).All(mount => targets.All(target => !Overlap(mount.Source, target.Host.BootstrapKeyFile))),
                "Only the target may mount the bootstrap key; run the bootstrap as a one-off operation.");
            await RequireOneHolderPerName(targets, inventory.Network, cancellationToken);

            var facts = new List<TargetReadinessFacts>();
            for (var index = 0; index < targets.Length; index++)
                facts.Add(await RequireLiveEnvironment(targets[index], records[index], cancellationToken));

            for (var index = 0; index < targets.Length; index++)
            {
                var origin = targets[index].Origin;
                // Through zeroshot-tls itself, so the chain it serves now (including a stored intermediate) must reach the root.
                using var http = discoveryClient(origin, rootCertificate, published);
                using var discovery = DirectTargetBudget.Start(DiscoveryBudget, clock, cancellationToken);
                await DirectTargetDiscovery.RequireAsync(http, origin, discovery);
                // Public discovery only claims private access: an unauthenticated control request must actually be refused.
                Require(await Session(http, origin, null, discovery) == HttpStatusCode.Unauthorized, targets[index],
                    "Target accepts unauthenticated control; it must serve native private access.");
                // Actual authenticated access, without submitting work: the target issues an OECP session for this token.
                var accepted = await Session(http, origin, tokens[targets[index]], discovery);
                Require(accepted != HttpStatusCode.Unauthorized, targets[index],
                    "Target refuses the configured control token; bootstrap the running target (bootstrap-target) or correct the token.");
                Require(accepted == HttpStatusCode.OK, targets[index], "Target authenticated control differs from the supported private DirectTarget.");
            }
            return new(facts);
        }
        catch (TargetNotReady) { throw; }
        catch (UnsupportedRuntime) { throw new TargetNotReady("Target discovery differs from supported DirectTarget."); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException("Target inspection cancelled.", cancellationToken); }
        catch (Exception)
        { throw new TargetNotReady("Target inventory, inspection or discovery is unavailable or invalid."); }
    }

    /// <summary>
    /// The target container of <paramref name="target"/>, before any exec: created from its adopted image pulled by
    /// digest, running with the supported profile, exactly its own mounts, its origin, and its own alias.
    /// </summary>
    private async Task RequireTargetContainer(Selected target, JsonElement actual, TargetReadinessInventory inventory,
        CancellationToken cancellationToken)
    {
        using var imageInspection = JsonDocument.Parse(await command(["image", "inspect", target.Host.ImageId], cancellationToken));
        Require(imageInspection.RootElement.GetArrayLength() == 1, target, "Target image inspection is invalid.");
        var image = imageInspection.RootElement[0];
        Require(image.GetProperty("Os").GetString() == "linux" && image.GetProperty("Architecture").GetString() == "amd64", target,
            "Target image platform is not supported; only linux/amd64 is.");
        Require(Strings(image, "RepoDigests").Contains(target.Entry.Adoption.Image, StringComparer.Ordinal), target,
            "Target image is not the adopted environment image.");
        // Docker gives every container a volume for each one the image declares (the base's unused
        // /var/lib/zeroshot); only those volumes may accompany the inventory's mounts.
        var declared = image.GetProperty("Config").TryGetProperty("Volumes", out var volumes) && volumes.ValueKind == JsonValueKind.Object
            ? volumes.EnumerateObject().Select(volume => volume.Name).ToHashSet(StringComparer.Ordinal) : [];

        Require(actual.GetProperty("Image").GetString() == target.Host.ImageId, target, "Target image differs from inventory.");
        Require(actual.GetProperty("State").GetProperty("Running").GetBoolean(), target, "Target is stopped; start its existing container.");
        var config = actual.GetProperty("Config");
        var host = actual.GetProperty("HostConfig");
        Require(!config.TryGetProperty("User", out var user) || user.GetString() is "" or "0" or "0:0" or "root", target,
            "Native target must run as container root for its isolated process identities.");
        Require(OrdinaryIsolation(host), target, "Target must use ordinary Docker isolation.");
        Require(Strings(host, "CapDrop").Length == 0, target, "Target requires Docker's default Linux capabilities.");
        Require(host.GetProperty("RestartPolicy").GetProperty("Name").GetString() == "no", target,
            "Target restart must remain operator controlled.");
        Require(Strings(config.GetProperty("Entrypoint")).SequenceEqual([TargetEnvironment.Entrypoint]), target,
            "Target entrypoint differs from the supported profile.");
        Require(!Strings(config.GetProperty("Env")).Any(value => CredentialNames.Contains(value.Split('=', 2)[0])), target,
            "Dispatch credentials must not be installed in target configuration.");
        var mounts = Mounts(actual.GetProperty("Mounts").EnumerateArray().Where(mount => mount.GetProperty("Type").GetString() != "volume"
            || !declared.Contains(mount.GetProperty("Destination").GetString() ?? "")));
        Require(mounts.Length == 4 && mounts.ToHashSet().SetEquals([
                new Mount(target.Entry.Target.StateMount, "/state", true, true), new Mount(target.Entry.Target.HomeMount, "/home/node", true, true),
                new Mount(inventory.RootCertificateMount, "/tls-root", false, true),
                new Mount(target.Host.BootstrapKeyFile, BootstrapKeySecret, false, true)]), target,
            "Target persistent mounts differ from the catalog and inventory.");
        Require(Published(host).Length == 0, target, "Target must publish no port; zeroshot-tls serves its origin.");
        Require(Strings(config.GetProperty("Cmd")).SequenceEqual(["--listen", NativeListen,
            "--public-origin", target.Entry.Origin, "--storage", "/state"]), target, "Target launch arguments differ from the catalog.");
        // zeroshot-tls forwards the origin to zeroshot-<name>:18770, so the inspected target must be the one answering there.
        Require(HasAlias(actual, inventory.Network, Alias(target.Name)), target,
            $"Target must carry its {Alias(target.Name)} alias on the recorded project network.");
        var containerId = actual.GetProperty("Id").GetString();
        Require(!string.IsNullOrWhiteSpace(containerId) && !containerId.StartsWith('-'), target, "Target container identity is invalid.");
    }

    /// <summary>
    /// Every container attached to the project network, so that each target alias and each origin alias resolves to
    /// exactly one container: no second target, and no load balancing, behind one name.
    /// </summary>
    private async Task RequireOneHolderPerName(IReadOnlyList<Selected> targets, string network, CancellationToken cancellationToken)
    {
        using var networkInspection = JsonDocument.Parse(await command(["network", "inspect", network], cancellationToken));
        Require(networkInspection.RootElement.GetArrayLength() == 1, "Project network inspection is invalid.");
        var attached = networkInspection.RootElement[0].GetProperty("Containers") is { ValueKind: JsonValueKind.Object } containers
            ? containers.EnumerateObject().Select(container => container.Name).ToArray() : [];
        Require(attached.Length > 0 && attached.All(ContainerId.IsMatch), "Project network inspection is invalid.");
        using var members = JsonDocument.Parse(await command(["inspect", "--type", "container", .. attached], cancellationToken));
        Require(members.RootElement.GetArrayLength() == attached.Length, "Project network inspection is invalid.");
        var held = members.RootElement.EnumerateArray().Select(member => NetworkNames(member, network)).ToArray();
        foreach (var name in targets.SelectMany(target => new[] { Alias(target.Name), target.Origin.Host }))
            Require(held.Count(names => names.Contains(name)) == 1,
                $"Exactly one container on the recorded project network may answer as {name}.");
    }

    /// <summary>
    /// The live checks of one target: its OS identity and every component its adopted manifest states,
    /// the Claude Code managed settings directory holding only its one root-owned file at fixed modes, the native, restic,
    /// entrypoint and managed settings bytes, the GitHub CLI feature Broodling uses,
    /// and the hosted identity transition.
    /// </summary>
    private async Task<TargetReadinessFacts> RequireLiveEnvironment(Selected target, JsonElement actual, CancellationToken cancellationToken)
    {
        var containerId = actual.GetProperty("Id").GetString()!;
        Task<string> Execute(params string[] arguments) => command(["exec", containerId, .. arguments], cancellationToken);
        var environment = target.Environment;
        var probes = EnvironmentProbe.For(environment);
        var mismatched = EnvironmentProbe.Mismatches(probes, await Execute("/bin/sh", "-c", EnvironmentProbe.Sections(probes)));
        Require(!mismatched.Contains("os"), target, "The live OS identity differs from the one the adopted environment manifest states.");
        Require(mismatched.Count == 0, target,
            $"The live {string.Join(", ", mismatched)} {(mismatched.Count == 1 ? "is missing or differs" : "are missing or differ")} from what the adopted environment manifest states.");
        // Claude Code also applies managed-settings.d drop-ins and a CLAUDE.md from this directory, and an agent that can
        // write the file, or the target of a symbolic link, can rewrite the policy before the next node starts.
        Require(await Execute("/bin/sh", "-c", @"find /etc/claude-code -printf '%y %U:%G %m %p\n' || :")
                == "d 0:0 755 /etc/claude-code\nf 0:0 644 " + DirectTargetBinding.ClaudeManagedSettings,
            target, "Target /etc/claude-code must contain exactly managed-settings.json, the approved Claude Code managed settings, "
                + "as a root-owned 0755 directory and a root-owned 0644 regular file.");
        foreach (var (path, expected) in new[] {
            (TargetEnvironment.NativeExecutable, DirectTargetBinding.NativeExecutableSha256),
            (TargetEnvironment.ResticExecutable, DirectTargetBinding.ResticExecutableSha256),
            (TargetEnvironment.Entrypoint, environment.EntrypointSha256),
            (DirectTargetBinding.ClaudeManagedSettings, DirectTargetBinding.ClaudeManagedSettingsSha256) })
            Require((await Execute("sha256sum", path)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == expected,
                target, $"Target {path} bytes differ from the adopted environment.");
        var help = await Execute("/usr/bin/gh", "api", "graphql", "--paginate", "--slurp", "--help");
        Require(Regex.IsMatch(help, @"^\s+--slurp(?:\s|$)", RegexOptions.Multiline), target, "Actual target GitHub CLI lacks api --paginate --slurp.");
        // The baseline's one short controlled target exec, not a provider task or lifecycle operation.
        await Execute("python3", "-c", "import os; os.setgroups([10002]); os.setgid(10002); "
            + "os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002");
        return new(target.Name, environment.Release, target.Entry.Target.Container, containerId, target.Host.ImageId, target.Entry.Origin,
            environment.Os, probes.Where(probe => probe.Component != "os")
                .ToDictionary(probe => probe.Component, probe => string.Join(", ", probe.Expected)));
    }

    /// <summary>The status of one OECP session request, with <paramref name="token"/> as its bearer when given.</summary>
    private static async Task<HttpStatusCode> Session(HttpClient http, Uri origin, string? token, DirectTargetBudget budget)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, DirectTargetDiscovery.SessionPath))
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        return (await DirectTargetExchange.SendJsonAsync(http, request, budget)).Status;
    }

    private sealed record Mount(string? Source, string? Destination, bool ReadWrite, bool Bind);

    private static Mount[] Mounts(JsonElement container) => Mounts(container.GetProperty("Mounts").EnumerateArray());

    private static Mount[] Mounts(IEnumerable<JsonElement> mounts) => mounts.Select(m => new Mount(
        m.GetProperty("Source").GetString(), m.GetProperty("Destination").GetString(), m.GetProperty("RW").GetBoolean(),
        m.GetProperty("Type").GetString() == "bind")).ToArray();

    /// <summary>Host paths that share storage; a sourceless mount (tmpfs) shares none.</summary>
    private static bool Overlap(string? first, string? second) =>
        !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second)
        && (PhysicalPaths.Contains(first, second) || PhysicalPaths.Contains(second, first));

    private static bool OrdinaryIsolation(JsonElement host) =>
        !host.GetProperty("Privileged").GetBoolean() && host.GetProperty("NetworkMode").GetString() != "host";

    /// <summary>Requested publications, including every exposed port when all are published.</summary>
    private static string[] Published(JsonElement host) =>
        host.GetProperty("PublishAllPorts").GetBoolean() ? ["all"]
        : host.GetProperty("PortBindings") is { ValueKind: JsonValueKind.Object } ports ? ports.EnumerateObject().Select(port => port.Name).ToArray() : [];

    private static bool HasAlias(JsonElement container, string network, string alias) =>
        container.GetProperty("NetworkSettings").GetProperty("Networks").TryGetProperty(network, out var attached)
        && attached.TryGetProperty("Aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array
        && Strings(aliases).Contains(alias, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every name Docker's DNS answers for <paramref name="container"/> on <paramref name="network"/>.</summary>
    private static HashSet<string> NetworkNames(JsonElement container, string network)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { (container.GetProperty("Name").GetString() ?? "").TrimStart('/') };
        if (container.GetProperty("NetworkSettings").GetProperty("Networks").TryGetProperty(network, out var attached))
            names.UnionWith(Strings(attached, "Aliases").Concat(Strings(attached, "DNSNames")));
        return names;
    }

    private static string[] Capabilities(JsonElement host, string name) => Strings(host, name)
        .Select(capability => capability.ToUpperInvariant() is var upper && upper.StartsWith("CAP_", StringComparison.Ordinal) ? upper[4..] : upper)
        .ToArray();

    /// <summary>The actual host address of zeroshot-tls's publication; a wildcard is reached on loopback.</summary>
    private static IPEndPoint PublishedEndpoint(JsonElement binding)
    {
        var address = binding.GetProperty("HostIp").GetString() switch
        {
            "" or "0.0.0.0" => IPAddress.Loopback,
            "::" => IPAddress.IPv6Loopback,
            var ip => IPAddress.Parse(ip!)
        };
        var port = int.Parse(binding.GetProperty("HostPort").GetString()!, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture);
        Require(port is >= 1 and <= 65535, "zeroshot-tls must publish only its 443 port.");
        return new(address, port);
    }

    /// <summary>
    /// Discovery for the origin's name and the SDK's trust in the configured root, connected to zeroshot-tls's
    /// published port rather than whatever the name resolves to on this host (LAN DNS selects Traefik, which
    /// terminates TLS itself).
    /// </summary>
    internal static HttpClient PublishedPortClient(Uri origin, string? rootCertificate, IPEndPoint published)
    {
        var handler = NativeClient.CreateHttpHandler(new TransportOptions { TrustedRootCertificatePath = rootCertificate });
        handler.ConnectCallback = async (_, token) =>
        {
            var socket = new Socket(published.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(published, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static string[] Strings(JsonElement host, string name) =>
        host.TryGetProperty(name, out var values) && values.ValueKind != JsonValueKind.Null ? Strings(values) : [];

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()
        ?? throw new TargetNotReady("Target inspection is invalid.")).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new TargetNotReady(message);
    }

    private static void Require(bool condition, Selected target, string message) =>
        Require(condition, $"Environment {target.Name}: {message}");

    internal static async Task<string> RunCommandAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
                Require(process.ExitCode == 0, "Target inspection command failed; check container status.");
                return (await output).Trim();
            }
            finally
            {
                // Detach only this CLI on cancellation. Never stop the target or claim exec cessation.
                if (!process.HasExited) process.Kill(entireProcessTree: false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException("Target inspection cancelled.", cancellationToken); }
        catch (Exception)
        { throw new TargetNotReady("Target inspection command failed; check container status."); }
    }
}
