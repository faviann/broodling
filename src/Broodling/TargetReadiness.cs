using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broodling;

/// <summary>
/// Operator inventory of the existing ADR 0001 stack: the <c>zeroshot</c> target, <c>zeroshot-tls</c> and
/// <c>broodling</c> containers, their project network and canonical host mount paths. Neither
/// installation nor lifecycle authority.
/// </summary>
public sealed record TargetReadinessInventory(string ContainerName, string ImageId, string DirectOrigin,
    string StateMount, string HomeMount, string Network, string TlsContainerName, string RootKeyMount,
    string RootCertificateMount, string BroodlingContainerName);

public sealed record TargetReadinessFacts(string ContainerName, string ContainerId, string ImageId, string DirectOrigin,
    IReadOnlyDictionary<string, string> Versions, bool ApiPaginateSlurp = true, bool HostedUidTransition = true,
    bool Ready = true, int ProviderTasks = 0);

public sealed class TargetNotReady(string message) : Exception(message);

/// <summary>Inspect the selected stack and discovery through its origin without submitting work or changing its lifecycle.</summary>
public sealed class TargetReadiness
{
    /// <summary>The pinned <c>zeroshot-tls</c> image, exactly as the container must be created from it.</summary>
    internal const string TlsImage = "caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b";
    /// <summary><c>zeroshot-tls</c>'s non-root user, which alone owns the root key (the target entrypoint's <c>tls_user</c>).</summary>
    internal const string TlsUser = "10443:10443";
    /// <summary>Native's fixed inner listener on the project network, where <c>zeroshot-tls</c> forwards; never published.</summary>
    internal const string NativeListen = "0.0.0.0:18770";
    private const string NativeSha256 = NativeProfile.NativeExecutableSha256;
    private const string GhSha256 = "ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff";
    private static readonly string[] CredentialNames = ["GH_TOKEN", "GITHUB_TOKEN", "GATEWAY_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "CODEX_API_KEY"];
    private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(10);
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

    /// <summary>
    /// <paramref name="selectedDirectOrigin"/> and <paramref name="rootCertificate"/> are the invocation
    /// configuration's origin and root. The root must be the stack's public <c>root.crt</c>, which discovery
    /// trusts exactly as invocation does.
    /// </summary>
    public async Task<TargetReadinessFacts> CheckAsync(TargetReadinessInventory inventory, string selectedDirectOrigin,
        string? rootCertificate, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] names = [inventory.ContainerName, inventory.TlsContainerName, inventory.BroodlingContainerName];
            Require(names.All(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('-')) && names.Distinct().Count() == 3
                && !string.IsNullOrWhiteSpace(inventory.ImageId) && !string.IsNullOrWhiteSpace(inventory.Network),
                "Target inventory is invalid.");
            // In-project clients reach zeroshot-tls through the alias on the origin's port, where Caddy listens.
            Require(DirectTargetExchange.CanonicalOrigin(inventory.DirectOrigin) is { Scheme: "https", IsDefaultPort: true,
                HostNameType: UriHostNameType.Dns }, "DirectTarget origin must be the canonical HTTPS origin, on port 443, that zeroshot-tls serves.");
            var origin = new Uri(inventory.DirectOrigin);
            Require(selectedDirectOrigin == inventory.DirectOrigin,
                "Invocation configuration differs from the recorded DirectTarget.");
            string[] paths = [inventory.StateMount, inventory.HomeMount, inventory.RootKeyMount, inventory.RootCertificateMount];
            Require(paths.All(path => Path.IsPathFullyQualified(path) && PhysicalPaths.Resolve(path) == path),
                "Target mount inventory must use canonical absolute paths.");
            Require(!Overlap(inventory.RootKeyMount, inventory.RootCertificateMount),
                "The root key and certificate must be recorded in separate locations.");
            Require(rootCertificate == Path.Combine(inventory.RootCertificateMount, "root.crt"),
                "Invocation configuration must trust the stack's public root, root.crt in the recorded root certificate mount.");

            using var inspection = JsonDocument.Parse(await command(["inspect", "--type", "container", .. names], cancellationToken));
            var records = inspection.RootElement;
            Require(records.GetArrayLength() == 3, "Expected exactly the recorded zeroshot, zeroshot-tls and broodling containers.");
            var (actual, tls, broodling) = (records[0], records[1], records[2]);

            Require(actual.GetProperty("Image").GetString() == inventory.ImageId, "Target image differs from inventory.");
            Require(actual.GetProperty("State").GetProperty("Running").GetBoolean(), "Target is stopped; start its existing container.");
            var config = actual.GetProperty("Config");
            var host = actual.GetProperty("HostConfig");
            Require(!config.TryGetProperty("User", out var user) || user.GetString() is "" or "0" or "0:0" or "root",
                "Native target must run as container root for its isolated process identities.");
            Require(OrdinaryIsolation(host), "Target must use ordinary Docker isolation.");
            Require(Strings(host, "CapDrop").Length == 0, "Target requires Docker's default Linux capabilities.");
            Require(host.GetProperty("RestartPolicy").GetProperty("Name").GetString() == "no",
                "Target restart must remain operator controlled.");
            Require(Strings(config.GetProperty("Entrypoint")).SequenceEqual(["/usr/local/bin/broodling-target"]),
                "Target entrypoint differs from the supported profile.");
            Require(!Strings(config.GetProperty("Env")).Any(value => CredentialNames.Contains(value.Split('=', 2)[0])),
                "Dispatch credentials must not be installed in target configuration.");
            var mounts = Mounts(actual);
            Require(mounts.Length == 3 && mounts.ToHashSet().SetEquals([
                    new Mount(inventory.StateMount, "/state", true, true), new Mount(inventory.HomeMount, "/home/node", true, true),
                    new Mount(inventory.RootCertificateMount, "/tls-root", false, true)]),
                "Target persistent mounts differ from inventory.");
            Require(Published(host).Length == 0, "Target must publish no port; zeroshot-tls serves its origin.");
            Require(Strings(config.GetProperty("Cmd")).SequenceEqual(["--listen", NativeListen,
                "--public-origin", inventory.DirectOrigin, "--storage", "/state"]), "Target launch arguments differ from inventory.");
            // zeroshot-tls forwards to zeroshot:18770, so the inspected target must be the one answering there.
            Require(HasAlias(actual, inventory.Network, "zeroshot"), "Target must carry the zeroshot alias on the recorded project network.");

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
            Require(HasAlias(tls, inventory.Network, origin.Host), "zeroshot-tls must carry the origin's alias on the recorded project network.");
            var tlsMounts = Mounts(tls);
            Require(tlsMounts.Where(m => m.Destination is "/tls-root-key" or "/tls-root").ToHashSet().SetEquals([
                    new Mount(inventory.RootKeyMount, "/tls-root-key", false, true),
                    new Mount(inventory.RootCertificateMount, "/tls-root", false, true)]),
                "zeroshot-tls root key and certificate mounts differ from inventory.");
            // broodling and zeroshot see the public directory, so Caddy's data (its intermediate key) must stay outside it.
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

            var containerId = actual.GetProperty("Id").GetString();
            Require(!string.IsNullOrWhiteSpace(containerId) && !containerId.StartsWith('-'), "Target container identity is invalid.");

            Task<string> Execute(params string[] arguments) => command(["exec", containerId!, .. arguments], cancellationToken);
            var versions = new Dictionary<string, string>();
            foreach (var (label, program, expected) in new[] {
                ("native", "/usr/local/bin/zeroshot", NativeProfile.NativeVersion),
                ("codex", "/usr/local/bin/codex", CodexProfile.Version),
                ("node", "/usr/local/bin/node", "v22.23.2") })
            {
                Require((await Execute(program, "--version")).Trim() == expected, $"Target {label} version differs from supported pin.");
                versions[label] = expected;
            }
            var gh = (await Execute("/usr/bin/gh", "--version")).Trim().Split('\n')[0];
            Require(gh.StartsWith("gh version 2.101.0 ", StringComparison.Ordinal), "Target GitHub CLI version differs from supported pin.");
            versions["gh"] = "gh version 2.101.0"; // Do not return arbitrary trailing process output.
            foreach (var (program, expected) in new[] { ("/usr/local/bin/zeroshot", NativeSha256), ("/usr/bin/gh", GhSha256) })
                Require((await Execute("sha256sum", program)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == expected,
                    "Target executable bytes differ from supported pin.");
            var help = await Execute("/usr/bin/gh", "api", "graphql", "--paginate", "--slurp", "--help");
            Require(Regex.IsMatch(help, @"^\s+--slurp(?:\s|$)", RegexOptions.Multiline), "Actual target GitHub CLI lacks api --paginate --slurp.");
            // The baseline's one short controlled target exec, not a provider task or lifecycle operation.
            await Execute("python3", "-c", "import os; os.setgroups([10002]); os.setgid(10002); "
                + "os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002");

            // Through zeroshot-tls itself, so the chain it serves now (including a stored intermediate) must reach the root.
            using var http = discoveryClient(origin, rootCertificate, published);
            using var discovery = DirectTargetBudget.Start(DiscoveryBudget, clock, cancellationToken);
            await DirectTargetDiscovery.RequireAsync(http, origin, discovery);
            return new(inventory.ContainerName, containerId!, inventory.ImageId, inventory.DirectOrigin, versions);
        }
        catch (TargetNotReady) { throw; }
        catch (UnsupportedRuntime) { throw new TargetNotReady("Target discovery differs from supported DirectTarget."); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException("Target inspection cancelled.", cancellationToken); }
        catch (Exception)
        { throw new TargetNotReady("Target inventory, inspection or discovery is unavailable or invalid."); }
    }

    private sealed record Mount(string? Source, string? Destination, bool ReadWrite, bool Bind);

    private static Mount[] Mounts(JsonElement container) => container.GetProperty("Mounts").EnumerateArray().Select(m => new Mount(
        m.GetProperty("Source").GetString(), m.GetProperty("Destination").GetString(), m.GetProperty("RW").GetBoolean(),
        m.GetProperty("Type").GetString() == "bind")).ToArray();

    private static bool Overlap(string? first, string? second) =>
        first is not null && second is not null && (PhysicalPaths.Contains(first, second) || PhysicalPaths.Contains(second, first));

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
    /// Discovery for the origin's name and trust, connected to zeroshot-tls's published port rather than
    /// whatever the name resolves to on this host (LAN DNS selects Traefik, which terminates TLS itself).
    /// </summary>
    private static HttpClient PublishedPortClient(Uri origin, string? rootCertificate, IPEndPoint published)
    {
        var handler = DirectTargetExchange.CreateHandler(origin, rootCertificate);
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

    internal static async Task<string> RunCommandAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
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
