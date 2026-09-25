using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

public static class HostTargetCommands
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TargetReadiness? readiness = null,
        CancellationToken cancellationToken = default)
    {
        if (args.Length != 3 || args[0] != "check-target")
        {
            output.WriteLine("Usage: check-target <target-inventory.json> <config.json>");
            return 2;
        }
        try
        {
            var inventory = JsonSerializer.Deserialize<TargetReadinessInventory>(File.ReadAllBytes(args[1]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new TargetNotReady("Target inventory is invalid.");
            if (InvocationConfiguration.Read(args[2]) is not InvocationConfiguration.Direct configuration)
                throw new UnsupportedRuntime("Target readiness requires a DirectTarget invocation configuration.");
            var facts = await (readiness ?? new TargetReadiness()).CheckAsync(inventory, configuration.DirectOrigin, cancellationToken);
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
}
