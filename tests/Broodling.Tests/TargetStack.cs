using System.Net.Security;
using System.Net.Sockets;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The environment-target topology on its own project network over disposable host directories: one actual
/// DirectTarget per environment as <c>zeroshot-&lt;name&gt;</c>, each with its own state, home, bootstrap key and
/// control token; the pinned Caddy image with the package's Caddyfile and one rendered site file per origin as
/// <c>zeroshot-tls</c>; and a <c>broodling</c> stand-in (the first target's image, idle) that mounts only the public
/// root. Caddy's data is a disposable named volume. No provider workload runs. Only <c>zeroshot-tls</c> publishes, on
/// host loopback. Each target mounts a root-only copy of its key, and the bootstrap operation and clients run on the
/// host through zeroshot-tls's published port. Members without a target argument act on the first target.
/// </summary>
internal sealed class TargetStack : IAsyncDisposable
{
    internal const string DefaultEnvironment = "dotnet";
    internal static readonly string Origin = OriginFor(DefaultEnvironment);
    internal static readonly string[] Arguments = ArgumentsFor(Origin);
    private readonly string id = "broodling-186-" + Guid.NewGuid().ToString("N");

    private TargetStack(IReadOnlyList<(string Environment, string Image)> targets) =>
        Targets = [.. targets.Select(target => new StackTarget(this, target.Environment, target.Image))];

    /// <summary>One environment's target: its catalog entry's names and paths, and its own secrets.</summary>
    internal sealed class StackTarget(TargetStack stack, string environment, string image)
    {
        internal string Environment => environment;
        /// <summary>The adopted image by digest, which the catalog entry names and the container runs.</summary>
        internal string Image => image;
        internal string Origin { get; } = OriginFor(environment);
        internal string Alias => TargetReadiness.Alias(environment);
        internal string Container => stack.id + "-" + Alias;
        internal string[] Arguments => ArgumentsFor(Origin);
        internal string State => Path.Combine(stack.Root, "targets", environment, "state");
        internal string Home => Path.Combine(stack.Root, "targets", environment, "home");
        /// <summary>The target's root-owned, mode 0400 bootstrap key secret.</summary>
        internal string BootstrapKeyFile => Path.Combine(stack.Root, "secrets", environment + "-bootstrap-key");
        /// <summary>The bootstrap operation's copy of the same key.</summary>
        internal string OperatorBootstrapKeyFile => Path.Combine(stack.Root, "bootstrap-keys", environment);
        internal string TokenFile => Path.Combine(stack.Root, "tokens", environment);
        internal string Token => File.ReadAllText(TokenFile);
        internal string[] Mounts => [.. Bind(State, "/state"), .. Bind(Home, "/home/node"), .. Bind(stack.RootCertificate, "/tls-root", true),
            .. Bind(BootstrapKeyFile, TargetReadiness.BootstrapKeySecret, true)];
        internal EnvironmentEntry Entry => new(environment, Origin, TokenFile, image, Container, State, Home);
        /// <summary>zeroshot-tls's site for this origin, as the installation renders it.</summary>
        internal string Site => $"{new Uri(Origin).Host} {{\n\ttls internal\n\treverse_proxy {Alias}:18770\n}}\n";
    }

    internal static string OriginFor(string environment) => $"https://zeroshot-{environment}.dev.faviann.com";
    internal static string[] ArgumentsFor(string origin) => ["--listen", TargetReadiness.NativeListen, "--public-origin", origin, "--storage", "/state"];

    internal IReadOnlyList<StackTarget> Targets { get; }
    internal StackTarget First => Targets[0];
    internal string Root { get; } = Path.Combine(Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"),
        "target-stack-" + Guid.NewGuid().ToString("N"));
    internal string Network => id;
    internal string Zeroshot => First.Container;
    internal string Tls => id + "-tls";
    internal string Broodling => id + "-broodling";
    internal string State => First.State;
    internal string Home => First.Home;
    internal string RootKey => Path.Combine(Root, "tls-root-key");
    internal string RootCertificate => Path.Combine(Root, "tls-root");
    internal string RootCertificateFile => Path.Combine(RootCertificate, "root.crt");
    internal string Sites => Path.Combine(Root, "caddy-sites");
    internal string BootstrapKeyFile => First.BootstrapKeyFile;
    internal string OperatorBootstrapKeyFile => First.OperatorBootstrapKeyFile;
    internal string TokenFile => First.TokenFile;
    internal string Token => First.Token;
    internal DirectTargetAccess Access => new(origin => Targets.FirstOrDefault(target => target.Origin == origin)?.TokenFile, RootCertificateFile);

    /// <summary>
    /// The invocation configuration's catalog for these targets, the first being the default, over
    /// <paramref name="adopted"/> (the bundled adoption set unless given).
    /// </summary>
    internal EnvironmentCatalog Catalog(IReadOnlyList<AdoptedEnvironment>? adopted = null) =>
        EnvironmentCatalog.Create([.. Targets.Select(target => target.Entry)], First.Environment, RootCertificateFile,
            adopted ?? TestEnvironments.Bundled);

    /// <summary>One target serving <see cref="DefaultEnvironment"/> on the adopted image.</summary>
    internal static async Task<TargetStack> CreateAsync() => await CreateAsync((DefaultEnvironment, await Direct.Value));

    /// <summary>One target per environment, each on its image by digest (pulled already).</summary>
    internal static async Task<TargetStack> CreateAsync(params (string Environment, string Image)[] targets)
    {
        var stack = new TargetStack(targets);
        foreach (var directory in new[] { stack.RootKey, stack.RootCertificate, stack.Sites, Path.Combine(stack.Root, "bootstrap-keys"),
            Path.Combine(stack.Root, "tokens") })
            Directory.CreateDirectory(directory);
        RequireSuccess(await DockerCommand("network", "create", stack.id));
        foreach (var target in stack.Targets)
        {
            Directory.CreateDirectory(target.State);
            Directory.CreateDirectory(target.Home);
            File.WriteAllText(Path.Combine(stack.Sites, target.Alias + ".caddy"), target.Site);
            var key = TestAccess.NewSecret();
            File.WriteAllText(target.OperatorBootstrapKeyFile, key);
            File.WriteAllText(target.TokenFile, TestAccess.NewSecret());
            RequireSuccess(await stack.ProvisionKey(key, target: target));
        }
        return stack;
    }

    internal static string[] Bind(string source, string destination, bool readOnly = false) =>
        ["--mount", $"type=bind,src={source},dst={destination}" + (readOnly ? ",readonly" : "")];
    internal string[] TargetMounts => First.Mounts;

    /// <summary>Render a target's bootstrap key secret as the operator does: root-owned, mode 0400, exactly the key.</summary>
    internal Task<Result> ProvisionKey(string key, string mode = "0400", string owner = "0:0", StackTarget? target = null)
    {
        target ??= First;
        var file = "/owned/secrets/" + Path.GetFileName(target.BootstrapKeyFile);
        return DockerCommand(["run", "--rm", "--network", "none", .. Bind(Root, "/owned"), "--env", "KEY=" + key, "--entrypoint", "/bin/sh",
            target.Image, "-ec", $"mkdir -p /owned/secrets; rm -f {file}; printf %s \"$KEY\" >{file}; chown {owner} {file}; chmod {mode} {file}"]);
    }
    internal string[] RootMounts => [.. Bind(RootKey, "/tls-root-key"), .. Bind(RootCertificate, "/tls-root")];

    /// <summary>The root helper, a one-off root container with the given root locations.</summary>
    internal Task<Result> CreateRoot(params string[] mounts) =>
        DockerCommand(["run", "--rm", "--network", "none", .. mounts, First.Image, "initialize-tls"]);

    /// <summary>zeroshot-tls's user, capabilities, root mounts, Caddyfile and site files and pinned image, as the package runs it.</summary>
    internal async Task<string[]> TlsOptions() => ["--user", TargetReadiness.TlsUser, "--cap-drop", "ALL", "--cap-add", "NET_BIND_SERVICE",
        .. Bind(RootKey, "/tls-root-key", true), .. Bind(RootCertificate, "/tls-root", true), "--mount", $"type=volume,src={id}-tls-data,dst=/data",
        .. Bind(Path.Combine(TestRepository.Root, "deployment", "zeroshot-tls.Caddyfile"), "/etc/caddy/Caddyfile", true),
        .. Bind(Sites, "/etc/caddy/sites", true), await TargetImage.Tls.Value];

    internal async Task StartTls()
    {
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", Tls, "--network", id,
            .. Targets.SelectMany(target => new[] { "--network-alias", new Uri(target.Origin).Host }),
            "--publish", "127.0.0.1::443", .. await TlsOptions()]));
        await ServingTls();
    }

    /// <summary>Native initialization, as <c>docker compose run --rm --no-deps zeroshot-&lt;name&gt; initialize</c>: no network is needed.</summary>
    internal Task<Result> Initialize(StackTarget? target = null)
    {
        target ??= First;
        return DockerCommand(["run", "--rm", "--name", target.Container, "--network", "none", .. target.Mounts, target.Image, "initialize",
            .. target.Arguments]);
    }

    /// <summary>A fresh stack: root, zeroshot-tls and every target's initialized native state, with no target serving.</summary>
    internal async Task InitializeAll()
    {
        RequireSuccess(await CreateRoot(RootMounts));
        await StartTls();
        foreach (var target in Targets) RequireSuccess(await Initialize(target));
    }

    internal Task<Result> Run(params string[] arguments) => RunWith([], arguments);
    internal Task<Result> RunWith(string[] options, string[] arguments) =>
        DockerCommand(["run", "--rm", "--name", Zeroshot, "--network", "none", .. TargetMounts, .. options, First.Image, .. arguments]);

    internal async Task<Result> Shell(string script, params string[] mounts)
    {
        var result = await DockerCommand(["run", "--rm", "--network", "none", .. mounts.Length == 0 ? TargetMounts : mounts,
            "--entrypoint", "/bin/sh", First.Image, "-ec", script]);
        RequireSuccess(result);
        return result;
    }

    /// <summary>Start a target's container, with its own mounts unless others are given.</summary>
    internal async Task StartTarget(StackTarget? target = null, string[]? mounts = null)
    {
        target ??= First;
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", target.Container, "--network", id,
            "--network-alias", target.Alias, .. mounts ?? target.Mounts, target.Image, .. target.Arguments]));
    }

    internal async Task StopTarget(StackTarget? target = null) =>
        RequireSuccess(await DockerCommand("rm", "--force", "--volumes", (target ?? First).Container));

    internal async Task StartBroodling() => RequireSuccess(await DockerCommand(["run", "--detach", "--name", Broodling, "--network", id,
        .. Bind(RootCertificate, "/tls-root", true), "--entrypoint", "sleep", First.Image, "infinity"]));

    /// <summary>A host client of a target's origin through zeroshot-tls's published port, trusting only the public root.</summary>
    internal async Task<HttpClient> Client(StackTarget? target = null)
    {
        var port = await DockerCommand("port", Tls, "443/tcp");
        RequireSuccess(port);
        var endpoint = port.Output.Trim().Split('\n')[0];
        return TargetReadiness.PublishedPortClient(new Uri((target ?? First).Origin), RootCertificateFile,
            new(System.Net.IPAddress.Loopback, int.Parse(endpoint[(endpoint.LastIndexOf(':') + 1)..])));
    }

    /// <summary>The operator's bootstrap of a running target through its origin, with its key unless another is given.</summary>
    internal async Task<DirectTargetBootstrapResult> Bootstrap(string? keyFile = null, StackTarget? target = null)
    {
        target ??= First;
        using var http = await Client(target);
        return await DirectTargetControl.BootstrapAsync(target.Origin, Access, keyFile ?? target.OperatorBootstrapKeyFile, http,
            TimeProvider.System, default);
    }

    /// <summary>The status of one OECP session request through a target's origin, with <paramref name="token"/> as bearer when given.</summary>
    internal async Task<System.Net.HttpStatusCode> Control(string? token, StackTarget? target = null)
    {
        target ??= First;
        using var http = await Client(target);
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Origin + "/native-v2/oecp-session") { Content = new StringContent("{}") };
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Waits until a target answers public discovery through zeroshot-tls.</summary>
    internal async Task Serving(StackTarget? target = null)
    {
        target ??= First;
        for (var retry = 0; retry < 100; retry++)
        {
            try
            {
                using var http = await Client(target);
                using var response = await http.GetAsync(target.Origin + "/.well-known/zeroshot-native-v2");
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception error) when (error is HttpRequestException or IOException) { }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Target never served through zeroshot-tls: " + (await DockerCommand("logs", target.Container)).Error);
    }

    /// <summary>The operator's inventory of this stack, with each running target's local image.</summary>
    internal async Task<TargetReadinessInventory> Inventory()
    {
        var targets = new List<InventoryTarget>();
        foreach (var target in Targets)
        {
            var image = await DockerCommand("inspect", "--format", "{{.Image}}", target.Container);
            RequireSuccess(image);
            targets.Add(new(target.Environment, image.Output.Trim(), target.BootstrapKeyFile));
        }
        return new(id, Tls, RootKey, RootCertificate, Broodling, targets);
    }

    /// <summary>
    /// Replace the key and certificate together while zeroshot-tls is stopped; the documented rotation
    /// also removes Caddy's stored intermediate and leaf, which <paramref name="complete"/> omits when false.
    /// </summary>
    internal async Task RotateRoot(bool complete)
    {
        RequireSuccess(await DockerCommand("stop", Tls));
        await Shell("rm /tls-root-key/root.key /tls-root/root.crt", RootMounts);
        RequireSuccess(await CreateRoot(RootMounts));
        if (complete)
            RequireSuccess(await DockerCommand("run", "--rm", "--network", "none", "--volumes-from", Tls, "--user", TargetReadiness.TlsUser,
                "--cap-drop", "ALL", "--entrypoint", "rm",
                await TargetImage.Tls.Value, "-r", "/data/caddy/pki/authorities/local", "/data/caddy/certificates/local"));
        RequireSuccess(await DockerCommand("start", Tls));
        await ServingTls();
    }

    /// <summary>Waits until zeroshot-tls presents a certificate at all; whether it chains to the root is the caller's check.</summary>
    private async Task ServingTls()
    {
        for (var retry = 0; retry < 100; retry++)
        {
            var port = await DockerCommand("port", Tls, "443/tcp");
            if (port.Code == 0)
            {
                var endpoint = port.Output.Trim().Split('\n')[0];
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync("127.0.0.1", int.Parse(endpoint[(endpoint.LastIndexOf(':') + 1)..]));
                    await using var tls = new SslStream(client.GetStream(), false, (_, _, _, _) => true);
                    await tls.AuthenticateAsClientAsync(new Uri(Origin).Host);
                    return;
                }
                catch (Exception error) when (error is SocketException or IOException or System.Security.Authentication.AuthenticationException) { }
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("zeroshot-tls never served: " + (await DockerCommand("logs", Tls)).Error);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var container in Targets.Select(target => target.Container).Append(Tls).Append(Broodling))
            await DockerCommand("rm", "--force", "--volumes", container);
        await DockerCommand("network", "rm", id);
        await DockerCommand("volume", "rm", id + "-tls-data");
        // Root-owned and zeroshot-tls-owned files: remove them as root before deleting the owned root.
        if (Directory.Exists(Root))
        {
            RequireSuccess(await DockerCommand(["run", "--rm", "--network", "none", .. Bind(Root, "/owned"), "--entrypoint", "find", First.Image,
                "/owned", "-mindepth", "1", "-delete"]));
            Directory.Delete(Root, true);
        }
    }
}
