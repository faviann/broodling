using System.Net;
using System.Net.Sockets;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The selected unmodified native HTTP/OECP target in the DirectTarget image, with a controlled
/// Codex provider and a controlled forge: shimmed <c>git</c>/<c>gh</c> backed by a host bare
/// repository for <c>acme/widget</c>. State volumes are disposable and credentials are fake.
/// The host-side application needs an origin it can reach without zeroshot-tls, so the witness binds
/// native to a literal-loopback origin: it records that binding with native's own commands, since the
/// entrypoint initializes only through zeroshot-tls, and then serves through the unchanged entrypoint
/// at the fixed inner port, published on host loopback only. The only outbound call a fixture makes is to an optional
/// Broodling reader, bound to the bridge gateway on the host and named <c>broodling</c> inside the
/// target; a mounted file replaces only the helper's image-level reader origin.
/// </summary>
internal sealed class StockDirectTarget : IAsyncDisposable
{
    private readonly string id = "broodling-180-" + Guid.NewGuid().ToString("N");
    private string image;
    private readonly string root;
    private readonly Uri? reader;
    private readonly int port = FreePort();
    internal string Origin => $"http://127.0.0.1:{port}";
    /// <summary>The forge's bare repository; the target reaches it as <c>https://github.com/acme/widget.git</c>.</summary>
    internal string Forge => Path.Combine(root, "widget.git");

    private StockDirectTarget(string image, string root, Uri? reader) { this.image = image; this.root = root; this.reader = reader; }

    /// <summary>
    /// A freshly initialized target serving over an empty forge under <paramref name="parent"/>, whose
    /// agents reach <paramref name="reader"/>, if given, as <c>broodling</c>. It runs the controlled layer
    /// over this revision's DirectTarget image unless another controlled <paramref name="image"/> is given.
    /// </summary>
    internal static async Task<StockDirectTarget> StartAsync(string parent, Uri? reader = null, string? image = null)
    {
        var target = new StockDirectTarget(image ?? await Controlled.Value, Path.Combine(parent, "forge"), reader);
        try
        {
            Directory.CreateDirectory(target.root);
            AttemptFixture.RunGit(target.root, "init", "--quiet", "--bare", target.Forge);
            // Native writes as root and fetches as its isolated writer identity.
            AttemptFixture.RunGit(target.Forge, "config", "core.sharedRepository", "0666");
            if (reader is not null) File.WriteAllText(target.ReaderOrigin, $"http://broodling:{reader.Port}\n");
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-state"));
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-home"));
            RequireSuccess(await DockerCommand(["run", "--rm", "--network", "none", .. target.Volumes, "--entrypoint", "/bin/sh",
                target.image, "-ec", target.LoopbackInitialization]));
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

    /// <summary>
    /// Stop the target and serve the same native state, mounts and origin through the entrypoint's ordinary
    /// startup again: an operator restart, or an image update to <paramref name="replacement"/> when given.
    /// </summary>
    internal async Task RestartAsync(string? replacement = null)
    {
        RequireSuccess(await DockerCommand("rm", "--force", id));
        image = replacement ?? image;
        await ServeAsync();
    }

    /// <summary>
    /// The native ledger's run count, read-only inside the target without a provider or credentials. Native's
    /// own client cannot run there: the loopback origin's port is published only on the host.
    /// </summary>
    internal async Task<int> RunCountAsync()
    {
        var count = await DockerCommand("exec", id, "python3", "-c", "import sqlite3; print(sqlite3.connect("
            + "'file:/state/runs.sqlite3?mode=ro', uri=True).execute('SELECT count(*) FROM v2_runs').fetchone()[0])");
        RequireSuccess(count);
        return int.Parse(count.Output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string[] Volumes => ["--mount", $"type=volume,src={id}-state,dst=/state",
        "--mount", $"type=volume,src={id}-home,dst=/home/node,volume-nocopy"];
    private string[] Arguments => ["--listen", TargetReadiness.NativeListen, "--public-origin", Origin, "--storage", "/state"];
    /// <summary>The entrypoint's former loopback initialization: bind and open the ledger without submitting work.</summary>
    private string LoopbackInitialization => $"""
        zeroshot target serve --listen 127.0.0.1:{port} --public-origin {Origin} --storage /state >/dev/null 2>&1 & native=$!
        for attempt in $(seq 1 100); do timeout 2 zeroshot target add broodling --url {Origin} --direct 2>/dev/null && break; sleep 0.1; done
        timeout 10 zeroshot list --target broodling >/dev/null
        kill $native; wait $native || :
        """;
    private string[] ForgeMount => ["--mount", $"type=bind,src={root},dst=/forge"];
    private string ReaderOrigin => Path.Combine(root, "reader-origin");
    private string[] Reader => reader is null ? [] : ["--add-host", $"broodling:{reader.Host}",
        "--mount", $"type=bind,src={ReaderOrigin},dst=/etc/broodling/reader-origin,readonly"];

    /// <summary>The host's address on the default bridge network, where a reader for the target can bind narrowly.</summary>
    internal static async Task<string> BridgeGatewayAsync()
    {
        var inspected = await DockerCommand("network", "inspect", "bridge", "--format", "{{(index .IPAM.Config 0).Gateway}}");
        RequireSuccess(inspected);
        return inspected.Output.Trim();
    }

    private async Task ServeAsync()
    {
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", id, "--publish", $"127.0.0.1:{port}:18770",
            .. Volumes, .. ForgeMount, .. Reader, image, .. Arguments]));
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
