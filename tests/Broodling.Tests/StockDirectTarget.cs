using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Broodling.Tests.TargetImage;

namespace Broodling.Tests;

/// <summary>
/// The selected unmodified native HTTP/OECP target in the DirectTarget image, with a controlled
/// Codex provider and a controlled forge: shimmed <c>git</c>/<c>gh</c> backed by a host bare
/// repository for <c>acme/widget</c>, which a test may script and whose <c>gh</c> requests it can read.
/// State volumes are disposable and credentials are fake.
/// The host-side application needs an origin it can reach without zeroshot-tls, so the witness binds
/// native to a literal-loopback origin, initialized and served through the unchanged entrypoint at the
/// fixed inner port, published on host loopback only. Like an installation, the target mounts its own
/// root-only synthetic bootstrap key, and every start is followed by the application's bootstrap of this
/// target's synthetic control token, which <see cref="TestAccess.Live"/> then names for its origin. No fixture
/// makes an outbound call, and nothing names or serves Broodling to the target.
/// </summary>
internal sealed class StockDirectTarget : IAsyncDisposable
{
    private readonly string id = "broodling-180-" + Guid.NewGuid().ToString("N");
    private string image;
    private readonly string root;
    private readonly int port = FreePort();
    internal string Origin => $"http://127.0.0.1:{port}";
    /// <summary>The control token currently configured, and installed after each start, for this target.</summary>
    internal string Token { get; private set; } = TestAccess.NewSecret();
    private string? tokenFile;
    /// <summary>The bootstrap operation's copy of the key, beside the forge rather than in the target's mounts.</summary>
    private string BootstrapKeyFile => Path.Combine(Path.GetDirectoryName(root)!, id + "-bootstrap-key");
    /// <summary>The forge's bare repository; the target reaches it as <c>https://github.com/acme/widget.git</c>.</summary>
    internal string Forge => Path.Combine(root, "widget.git");

    private StockDirectTarget(string image, string root) { this.image = image; this.root = root; }

    /// <summary>
    /// A freshly initialized target serving over an empty forge under <paramref name="parent"/>. It runs the controlled
    /// layer over this revision's DirectTarget image unless another controlled <paramref name="image"/> is given.
    /// </summary>
    internal static async Task<StockDirectTarget> StartAsync(string parent, string? image = null)
    {
        var target = new StockDirectTarget(image ?? await Controlled.Value, Path.Combine(parent, "forge"));
        try
        {
            Directory.CreateDirectory(target.root);
            AttemptFixture.RunGit(target.root, "init", "--quiet", "--bare", target.Forge);
            // Native writes as root and fetches as its isolated writer identity.
            AttemptFixture.RunGit(target.Forge, "config", "core.sharedRepository", "0666");
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-state"));
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-home"));
            RequireSuccess(await DockerCommand("volume", "create", target.id + "-secrets"));
            var key = TestAccess.NewSecret();
            File.WriteAllText(target.BootstrapKeyFile, key);
            // The operator's rendering of the target's secret: root-owned, mode 0400, exactly the key.
            RequireSuccess(await DockerCommand(["run", "--rm", "--network", "none", "--mount", $"type=volume,src={target.id}-secrets,dst=/secrets",
                "--env", "KEY=" + key, "--entrypoint", "/bin/sh", target.image, "-ec",
                "printf %s \"$KEY\" >/secrets/zeroshot-bootstrap-key; chown 0:0 /secrets/zeroshot-bootstrap-key; chmod 0400 /secrets/zeroshot-bootstrap-key"]));
            target.tokenFile = TestAccess.Register(new Uri(target.Origin), target.Token);
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

    /// <summary>
    /// Stop the target and serve the same native state, mounts and origin through the entrypoint's ordinary
    /// startup again, then bootstrap it: an operator restart, or an image update to <paramref name="replacement"/>
    /// when given. With <paramref name="rotate"/> it is the explicit rotation: the configured token is replaced
    /// while the target is stopped and the new process is bootstrapped with the replacement.
    /// </summary>
    internal async Task RestartAsync(string? replacement = null, bool rotate = false)
    {
        RequireSuccess(await DockerCommand("rm", "--force", id));
        image = replacement ?? image;
        if (rotate)
        {
            Token = TestAccess.NewSecret();
            File.WriteAllText(tokenFile!, Token);
        }
        await ServeAsync();
    }

    /// <summary>The status of one OECP session request with <paramref name="token"/> as bearer: whether this target accepts it for control.</summary>
    internal async Task<System.Net.HttpStatusCode> ControlAsync(string token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Post, Origin + "/native-v2/oecp-session") { Content = new StringContent("{}") };
        request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
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

    /// <summary>Replace the controlled forge's scenario (tests/fixtures/stock-target/gh), which it reads on every call.</summary>
    internal void Script(JsonObject scenario)
    {
        File.WriteAllText(Scenario + ".new", scenario.ToJsonString());
        File.Move(Scenario + ".new", Scenario, overwrite: true);
    }

    /// <summary>
    /// Every <c>gh</c> invocation native has made so far, as the controlled forge recorded it. A last line the
    /// shim is still appending, not yet newline-terminated, is not a request yet.
    /// </summary>
    internal IReadOnlyList<ForgeRequest> Trace()
    {
        var trace = Path.Combine(root, "gh-trace.jsonl");
        if (!File.Exists(trace)) return [];
        var lines = File.ReadAllText(trace).Split('\n');
        return lines[..^1].Select(line => JsonSerializer.Deserialize<ForgeRequest>(line, Json)!).ToList();
    }

    /// <summary>The native actually serving: its reported version and executable SHA-256, read inside the running target.</summary>
    internal async Task<(string Version, string Sha256)> NativeAsync()
    {
        var native = await DockerCommand("exec", id, "/bin/sh", "-ec", "zeroshot --version; sha256sum < /usr/local/bin/zeroshot");
        RequireSuccess(native);
        var lines = native.Output.Split('\n');
        return (lines[0], lines[1].Split(' ')[0]);
    }

    /// <summary>
    /// Native's own ledger record of <paramref name="runId"/>, read-only inside the target: its terminal failure
    /// reason, which the application deliberately reduces to <c>native_failed</c>, and how many times each graph
    /// node executed.
    /// </summary>
    internal async Task<(string? Failure, IReadOnlyDictionary<string, int> Executions)> LedgerAsync(string runId)
    {
        var stored = await DockerCommand("exec", id, "python3", "-c", "import sqlite3, sys; print(sqlite3.connect("
            + "'file:/state/runs.sqlite3?mode=ro', uri=True).execute('SELECT stored_json FROM v2_runs WHERE run_id = ?', "
            + "(sys.argv[1],)).fetchone()[0])", runId);
        RequireSuccess(stored);
        var snapshot = JsonNode.Parse(stored.Output)!["snapshot"]!;
        var executions = snapshot["executions"]!.AsObject().GroupBy(execution => (string)execution.Value!["reference"]!["node"]!)
            .ToDictionary(node => node.Key, node => node.Count());
        return ((string?)snapshot["terminal"]?["reason"], executions);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Scenario => Path.Combine(root, "scenario.json");
    private string[] Volumes => ["--mount", $"type=volume,src={id}-state,dst=/state",
        "--mount", $"type=volume,src={id}-home,dst=/home/node,volume-nocopy"];
    private string[] Secrets => ["--mount", $"type=volume,src={id}-secrets,dst=/run/secrets,readonly"];
    private string[] Arguments => ["--listen", TargetReadiness.NativeListen, "--public-origin", Origin, "--storage", "/state"];
    private string[] ForgeMount => ["--mount", $"type=bind,src={root},dst=/forge"];

    private async Task ServeAsync()
    {
        RequireSuccess(await DockerCommand(["run", "--detach", "--name", id, "--publish", $"127.0.0.1:{port}:18770",
            .. Volumes, .. Secrets, .. ForgeMount, image, .. Arguments]));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var retry = 0; retry < 300; retry++)
        {
            try
            {
                using var response = await http.GetAsync(Origin + "/.well-known/zeroshot-native-v2");
                if (response.IsSuccessStatusCode)
                {
                    // Every target-process start serves nothing to anyone until the configured token is installed.
                    await DirectTargetControl.BootstrapAsync(Origin, TestAccess.Live, BootstrapKeyFile);
                    return;
                }
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
        await DockerCommand("volume", "rm", id + "-state", id + "-home", id + "-secrets");
        if (tokenFile is not null) TestAccess.Unregister(new Uri(Origin), tokenFile);
        // The target writes forge objects as root; open them so the owning test root can be deleted.
        if (Directory.Exists(root)) await Shell("chmod -R a+rwX /forge");
    }
}
