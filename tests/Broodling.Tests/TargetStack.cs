using System.Net.Security;
using System.Net.Sockets;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// ADR 0001's services on their own project network over disposable host directories: the actual
/// DirectTarget image as <c>zeroshot</c>, the pinned Caddy image with the package's Caddyfile as
/// <c>zeroshot-tls</c>, and a <c>broodling</c> stand-in (the target image, idle) that mounts only
/// the public root. Caddy's data is a disposable named volume. No provider workload runs. Only
/// <c>zeroshot-tls</c> publishes, on host loopback.
/// </summary>
internal sealed class TargetStack : IAsyncDisposable
{
    internal const string Origin = "https://zeroshot.dev.faviann.com";
    internal static readonly string[] Arguments = ["--listen", TargetReadiness.NativeListen, "--public-origin", Origin, "--storage", "/state"];
    private readonly string id = "broodling-186-" + Guid.NewGuid().ToString("N");
    private readonly string image;

    private TargetStack(string image) => this.image = image;

    internal string Root { get; } = Path.Combine(Environment.GetEnvironmentVariable("BROODLING_TEST_WORKSPACE_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "broodling-tests"),
        "target-stack-" + Guid.NewGuid().ToString("N"));
    internal string Zeroshot => id + "-zeroshot";
    internal string Tls => id + "-tls";
    internal string Broodling => id + "-broodling";
    internal string State => Path.Combine(Root, "state");
    internal string Home => Path.Combine(Root, "home");
    internal string RootKey => Path.Combine(Root, "tls-root-key");
    internal string RootCertificate => Path.Combine(Root, "tls-root");
    internal string RootCertificateFile => Path.Combine(RootCertificate, "root.crt");

    internal static async Task<TargetStack> CreateAsync()
    {
        var stack = new TargetStack(await Direct.Value);
        foreach (var directory in new[] { stack.State, stack.Home, stack.RootKey, stack.RootCertificate })
            Directory.CreateDirectory(directory);
        RequireSuccess(await DockerCommand("network", "create", stack.id));
        return stack;
    }

    internal static string[] Bind(string source, string destination, bool readOnly = false) =>
        ["--mount", $"type=bind,src={source},dst={destination}" + (readOnly ? ",readonly" : "")];
    internal string[] TargetMounts => [.. Bind(State, "/state"), .. Bind(Home, "/home/node"), .. Bind(RootCertificate, "/tls-root", true)];
    internal string[] RootMounts => [.. Bind(RootKey, "/tls-root-key"), .. Bind(RootCertificate, "/tls-root")];

    /// <summary>The root helper, a one-off root container with the given root locations.</summary>
    internal Task<Result> CreateRoot(params string[] mounts) =>
        DockerCommand(["run", "--rm", "--network", "none", .. mounts, image, "initialize-tls"]);

    /// <summary>zeroshot-tls's user, capabilities, root mounts, Caddyfile and pinned image, as the package runs it.</summary>
    internal async Task<string[]> TlsOptions() => ["--user", TargetReadiness.TlsUser, "--cap-drop", "ALL", "--cap-add", "NET_BIND_SERVICE",
        .. Bind(RootKey, "/tls-root-key", true), .. Bind(RootCertificate, "/tls-root", true), "--mount", $"type=volume,src={id}-tls-data,dst=/data",
        .. Bind(Path.Combine(NativeFixture.RepositoryRoot, "deployment", "zeroshot-tls.Caddyfile"), "/etc/caddy/Caddyfile", true),
        await TargetImage.Tls.Value];

    internal async Task StartTls()
    {
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", Tls, "--network", id, "--network-alias", new Uri(Origin).Host,
            "--publish", "127.0.0.1::443", .. await TlsOptions()]));
        await ServingTls();
    }

    /// <summary>Native initialization through zeroshot-tls, as <c>docker compose run --use-aliases zeroshot initialize</c>.</summary>
    internal Task<Result> Initialize() =>
        DockerCommand(["run", "--rm", "--name", Zeroshot, "--network", id, "--network-alias", "zeroshot", .. TargetMounts, image, "initialize", .. Arguments]);

    /// <summary>A fresh stack: root, zeroshot-tls and initialized native state, with no target serving.</summary>
    internal async Task InitializeAll()
    {
        RequireSuccess(await CreateRoot(RootMounts));
        await StartTls();
        RequireSuccess(await Initialize());
    }

    internal Task<Result> Run(params string[] arguments) => RunWith([], arguments);
    internal Task<Result> RunWith(string[] options, string[] arguments) =>
        DockerCommand(["run", "--rm", "--name", Zeroshot, "--network", "none", .. TargetMounts, .. options, image, .. arguments]);

    internal async Task<Result> Shell(string script, params string[] mounts)
    {
        var result = await DockerCommand(["run", "--rm", "--network", "none", .. mounts.Length == 0 ? TargetMounts : mounts,
            "--entrypoint", "/bin/sh", image, "-ec", script]);
        RequireSuccess(result);
        return result;
    }

    internal async Task StartTarget() => RequireSuccess(await DockerCommand(["run", "--detach", "--name", Zeroshot, "--network", id,
        "--network-alias", "zeroshot", .. TargetMounts, image, .. Arguments]));
    internal async Task StopTarget() => RequireSuccess(await DockerCommand("rm", "--force", Zeroshot));

    internal async Task StartBroodling() => RequireSuccess(await DockerCommand(["run", "--detach", "--name", Broodling, "--network", id,
        .. Bind(RootCertificate, "/tls-root", true), "--entrypoint", "sleep", image, "infinity"]));

    /// <summary>Native's own client in the zeroshot container, trusting the public root: operator diagnosis.</summary>
    internal Task<Result> NativeList() =>
        DockerCommand("exec", Zeroshot, "env", "SSL_CERT_FILE=/tls-root/root.crt", "zeroshot", "list", "--target", "broodling");

    internal async Task<string> ServingNativeList()
    {
        for (var retry = 0; retry < 100; retry++)
        {
            var result = await NativeList();
            if (result.Code == 0) return result.Output;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Target never served through zeroshot-tls: " + (await DockerCommand("logs", Zeroshot)).Error);
    }

    internal async Task<TargetReadinessInventory> Inventory()
    {
        var target = await DockerCommand("inspect", "--format", "{{.Image}}", Zeroshot);
        RequireSuccess(target);
        return new(Zeroshot, target.Output.Trim(), Origin, State, Home, id, Tls, RootKey, RootCertificate, Broodling);
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
        foreach (var container in new[] { Zeroshot, Tls, Broodling })
            await DockerCommand("rm", "--force", "--volumes", container);
        await DockerCommand("network", "rm", id);
        await DockerCommand("volume", "rm", id + "-tls-data");
        // Root-owned and zeroshot-tls-owned files: remove them as root before deleting the owned root.
        if (Directory.Exists(Root))
        {
            RequireSuccess(await DockerCommand(["run", "--rm", "--network", "none", .. Bind(Root, "/owned"), "--entrypoint", "find", image,
                "/owned", "-mindepth", "1", "-delete"]));
            Directory.Delete(Root, true);
        }
    }
}
