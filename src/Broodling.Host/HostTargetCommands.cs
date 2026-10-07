using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>
/// Operator commands for the configured DirectTarget's readiness and private-control bootstrap. Their output is
/// fixed JSON that never carries secret material.
/// </summary>
public static class HostTargetCommands
{
    private const string Usage = "Usage: check-target <target-inventory.json> <config.json> | bootstrap-target <config.json> <bootstrap-key-file>";

    public static async Task<int> RunAsync(string[] args, TextWriter output, TargetReadiness? readiness = null,
        CancellationToken cancellationToken = default)
    {
        if (args.Length != 3 || args[0] is not ("check-target" or "bootstrap-target"))
        {
            output.WriteLine(Usage);
            return 2;
        }
        return args[0] == "check-target"
            ? await CheckAsync(args, output, readiness ?? new TargetReadiness(), cancellationToken)
            : await BootstrapAsync(args, output, cancellationToken);
    }

    private static async Task<int> CheckAsync(string[] args, TextWriter output, TargetReadiness readiness, CancellationToken cancellationToken)
    {
        try
        {
            var inventory = JsonSerializer.Deserialize<TargetReadinessInventory>(File.ReadAllBytes(args[1]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new TargetNotReady("Target inventory is invalid.");
            var configuration = InvocationConfiguration.Read(args[2]);
            var facts = await readiness.CheckAsync(inventory, configuration.DirectOrigin, configuration.Access, cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(facts, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return 0;
        }
        catch (OperationCanceledException)
        {
            output.WriteLine("{\"ready\":false,\"error\":\"Target inspection cancelled.\"}");
            return 130;
        }
        catch (Exception exception)
        {
            output.WriteLine(JsonSerializer.Serialize(new { ready = false, error = exception is TargetNotReady known
                ? known.Message : "Target inventory or invocation configuration is unavailable or invalid." }));
            return 1;
        }
    }

    /// <summary>
    /// Installs the configuration's control token in the target now serving its origin, which every target-process
    /// start needs. The bootstrap key file is the same key the target was started with, supplied only to this command.
    /// </summary>
    private static async Task<int> BootstrapAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            var configuration = InvocationConfiguration.Read(args[1]);
            var result = await DirectTargetControl.BootstrapAsync(configuration.DirectOrigin, configuration.Access, args[2], cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                bootstrapped = true, origin = configuration.DirectOrigin,
                result = result == DirectTargetBootstrapResult.Installed ? "installed" : "already_installed"
            }));
            return 0;
        }
        catch (OperationCanceledException)
        {
            output.WriteLine("{\"bootstrapped\":false,\"error\":\"Bootstrap cancelled; run it again.\"}");
            return 130;
        }
        catch (Exception exception)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                bootstrapped = false,
                code = exception is BroodlingException known ? known.Code : "bootstrap_failed",
                error = exception is DirectTargetBootstrapFailed or UnsupportedRuntime or NativeTransportError
                    ? exception.Message : "Invocation configuration, the bootstrap key or the target is unavailable or invalid."
            }));
            return 1;
        }
    }
}
