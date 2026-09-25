using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broodling;

/// <summary>Operator inventory of an existing target; neither installation nor lifecycle authority.</summary>
public sealed record TargetReadinessInventory(string ContainerName, string ImageId, string DirectOrigin,
    string StateMount, string HomeMount);

public sealed record TargetReadinessFacts(string ContainerName, string ContainerId, string ImageId, string DirectOrigin,
    IReadOnlyDictionary<string, string> Versions, bool ApiPaginateSlurp = true, bool HostedUidTransition = true,
    bool Ready = true, int ProviderTasks = 0);

public sealed class TargetNotReady(string message) : Exception(message);

/// <summary>Inspect the selected container and discovery endpoint without submitting work or changing its lifecycle.</summary>
public sealed class TargetReadiness
{
    private const string NativeSha256 = NativeProfile.NativeExecutableSha256;
    private const string GhSha256 = "ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff";
    private static readonly string[] CredentialNames = ["GH_TOKEN", "GITHUB_TOKEN", "GATEWAY_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "CODEX_API_KEY"];
    private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(10);
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<string>> command;
    private readonly HttpClient? http;
    private readonly TimeProvider clock;

    public TargetReadiness() : this((arguments, token) => RunCommandAsync("docker", arguments, token), null) { }

    internal TargetReadiness(Func<IReadOnlyList<string>, CancellationToken, Task<string>> command, HttpClient? http,
        TimeProvider? clock = null)
    {
        this.command = command;
        this.http = http;
        this.clock = clock ?? TimeProvider.System;
    }

    public async Task<TargetReadinessFacts> CheckAsync(TargetReadinessInventory inventory, string selectedDirectOrigin,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(!string.IsNullOrWhiteSpace(inventory.ContainerName) && !inventory.ContainerName.StartsWith('-')
                && !string.IsNullOrWhiteSpace(inventory.ImageId), "Target inventory is invalid.");
            Require(Uri.TryCreate(inventory.DirectOrigin, UriKind.Absolute, out var endpoint)
                && endpoint.Port is >= 1 and <= 65535 && inventory.DirectOrigin == $"http://127.0.0.1:{endpoint.Port}",
                "DirectTarget origin must be the recorded canonical loopback HTTP origin.");
            Require(selectedDirectOrigin == inventory.DirectOrigin,
                "Invocation configuration differs from the recorded DirectTarget.");
            Require(Path.IsPathFullyQualified(inventory.StateMount) && Path.IsPathFullyQualified(inventory.HomeMount)
                && PhysicalPaths.Resolve(inventory.StateMount) == inventory.StateMount
                && PhysicalPaths.Resolve(inventory.HomeMount) == inventory.HomeMount,
                "Target mount inventory must use canonical absolute paths.");

            using var inspection = JsonDocument.Parse(await command(["inspect", inventory.ContainerName], cancellationToken));
            var records = inspection.RootElement;
            Require(records.GetArrayLength() == 1, "Expected exactly one recorded target.");
            var actual = records[0];
            Require(actual.GetProperty("Image").GetString() == inventory.ImageId, "Target image differs from inventory.");
            Require(actual.GetProperty("State").GetProperty("Running").GetBoolean(), "Target is stopped; start its existing container.");
            var config = actual.GetProperty("Config");
            var host = actual.GetProperty("HostConfig");
            Require(!config.TryGetProperty("User", out var user) || user.GetString() is "" or "0" or "0:0" or "root",
                "Native target must run as container root for its isolated process identities.");
            Require(!host.GetProperty("Privileged").GetBoolean() && host.GetProperty("NetworkMode").GetString() != "host",
                "Target must use ordinary Docker isolation.");
            Require(!host.TryGetProperty("CapDrop", out var caps) || caps.ValueKind == JsonValueKind.Null || caps.GetArrayLength() == 0,
                "Target requires Docker's default Linux capabilities.");
            Require(host.GetProperty("RestartPolicy").GetProperty("Name").GetString() == "no",
                "Target restart must remain operator controlled.");
            Require(Strings(config.GetProperty("Entrypoint")).SequenceEqual(["/usr/local/bin/broodling-target"]),
                "Target entrypoint differs from the supported profile.");
            Require(!Strings(config.GetProperty("Env")).Any(value => CredentialNames.Contains(value.Split('=', 2)[0])),
                "Dispatch credentials must not be installed in target configuration.");
            var mounts = actual.GetProperty("Mounts").EnumerateArray().ToArray();
            Require(mounts.Length == 2 && mounts.All(m => m.GetProperty("Type").GetString() == "bind" && m.GetProperty("RW").GetBoolean())
                && mounts.Select(m => (m.GetProperty("Source").GetString(), m.GetProperty("Destination").GetString())).ToHashSet()
                    .SetEquals([(inventory.StateMount, "/state"), (inventory.HomeMount, "/home/node")]),
                "Target persistent mounts differ from inventory.");
            var ports = host.GetProperty("PortBindings").EnumerateObject().ToArray();
            Require(ports.Length == 1, "Target must publish only its loopback endpoint.");
            var port = ports[0];
            var bindings = port.Value;
            Require(port.Name.EndsWith("/tcp", StringComparison.Ordinal) && bindings.GetArrayLength() == 1
                && bindings[0].EnumerateObject().Count() == 2
                && bindings[0].GetProperty("HostIp").GetString() == "127.0.0.1"
                && bindings[0].GetProperty("HostPort").GetString() == endpoint!.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Target endpoint is not exclusively bound to the recorded loopback port.");
            Require(Strings(config.GetProperty("Cmd")).SequenceEqual(["--listen", "0.0.0.0:" + port.Name[..^4],
                "--public-origin", inventory.DirectOrigin, "--storage", "/state"]), "Target launch arguments differ from inventory.");
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

            using var ownedHttp = http is null ? DirectTargetExchange.CreateClient() : null;
            using var discovery = DirectTargetBudget.Start(DiscoveryBudget, clock, cancellationToken);
            await DirectTargetDiscovery.RequireAsync(http ?? ownedHttp!, endpoint!, discovery);
            return new(inventory.ContainerName, containerId!, inventory.ImageId, inventory.DirectOrigin, versions);
        }
        catch (TargetNotReady) { throw; }
        catch (UnsupportedRuntime) { throw new TargetNotReady("Target discovery differs from supported DirectTarget."); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException("Target inspection cancelled.", cancellationToken); }
        catch (Exception)
        { throw new TargetNotReady("Target inventory, inspection or discovery is unavailable or invalid."); }
    }

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
