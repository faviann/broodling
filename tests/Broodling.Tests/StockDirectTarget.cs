using System.Net;
using System.Net.Sockets;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The selected unmodified native HTTP/OECP target in the DirectTarget image, with a controlled
/// Codex provider and a controlled forge: shimmed <c>git</c>/<c>gh</c> backed by a host bare
/// repository for <c>acme/widget</c>. State volumes are disposable and credentials are fake.
/// Unlike the startup tests' <c>--network none</c>, serving needs a bridge network so the port
/// can be published, on host loopback only; no fixture makes an outbound call.
/// </summary>
internal sealed class StockDirectTarget : IAsyncDisposable
{
    internal static readonly IReadOnlyDictionary<string, string> Credentials = new Dictionary<string, string>
    {
        ["GH_TOKEN"] = "fixture-github-token", ["GATEWAY_BASE_URL"] = "https://cliproxy.local.faviann.com/v1",
        ["GATEWAY_API_KEY"] = "fixture-gateway-key"
    };

    private readonly string id = "broodling-180-" + Guid.NewGuid().ToString("N");
    private readonly string image;
    private readonly string root;
    private readonly int port = FreePort();
    internal string Origin => $"http://127.0.0.1:{port}";
    /// <summary>The forge's bare repository; the target reaches it as <c>https://github.com/acme/widget.git</c>.</summary>
    internal string Forge => Path.Combine(root, "widget.git");

    private StockDirectTarget(string image, string root) { this.image = image; this.root = root; }

    /// <summary>A freshly initialized target serving over an empty forge under <paramref name="parent"/>.</summary>
    internal static async Task<StockDirectTarget> StartAsync(string parent)
    {
        var target = new StockDirectTarget(await Controlled.Value, Path.Combine(parent, "forge"));
        try
        {
            Directory.CreateDirectory(target.root);
            AttemptFixture.RunGit(target.root, "init", "--quiet", "--bare", target.Forge);
            // Native writes as root and fetches as its isolated writer identity.
            AttemptFixture.RunGit(target.Forge, "config", "core.sharedRepository", "0666");
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-state"));
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-home"));
            RequireSuccess(await DockerCommand(["run", "--rm", "--network", "none", .. target.Volumes, target.image, "initialize", .. target.Arguments]));
            await target.ServeAsync();
            return target;
        }
        catch
        {
            await target.DisposeAsync();
            throw;
        }
    }

    /// <summary>Push to the forge, keeping it usable by the target's isolated identities.</summary>
    internal async Task PushAsync(string repository, string refspec)
    {
        AttemptFixture.RunGit(repository, "push", "--quiet", Forge, refspec);
        await Shell("chmod -R a+rwX /forge");
    }

    /// <summary>Stop and start the same native state, as an operator restart does.</summary>
    internal async Task RestartAsync()
    {
        RequireSuccess(await DockerCommand("rm", "--force", id));
        await ServeAsync();
    }

    private string[] Volumes => ["--mount", $"type=volume,src={id}-state,dst=/state",
        "--mount", $"type=volume,src={id}-home,dst=/home/node,volume-nocopy"];
    private string[] Arguments => ["--listen", $"0.0.0.0:{port}", "--public-origin", Origin, "--storage", "/state"];
    private string[] ForgeMount => ["--mount", $"type=bind,src={root},dst=/forge"];

    private async Task ServeAsync()
    {
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", id, "--publish", $"127.0.0.1:{port}:{port}",
            .. Volumes, .. ForgeMount, image, .. Arguments]));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var retry = 0; retry < 300; retry++)
        {
            try
            {
                using var response = await http.GetAsync(Origin + "/.well-known/zeroshot-native-v2");
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Target never served: " + (await DockerCommand("logs", id)).Error);
    }

    private async Task Shell(string script) =>
        RequireSuccess(await DockerCommand(["run", "--rm", "--network", "none", .. ForgeMount, "--entrypoint", "/bin/sh", image, "-ec", script]));

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await DockerCommand("rm", "--force", id);
        await DockerCommand("volume", "rm", id + "-state", id + "-home");
        // The target writes forge objects as root; open them so the owning test root can be deleted.
        if (Directory.Exists(root)) await Shell("chmod -R a+rwX /forge");
    }
}
