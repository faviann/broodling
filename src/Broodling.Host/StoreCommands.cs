using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>Thin state and observation commands, independent of installation and HTTP startup.</summary>
public static class StoreCommands
{
    public static int Run(string[] args, BroodlingApplication application, TextWriter output, TextWriter error)
    {
        var valid = args.Length switch
        {
            2 => args[0] is "initialize-store" or "upgrade-store"
                or "pause-installation" or "installation-status" or "release-installation",
            3 => args[0] == "status",
            4 => args[0] is "history" or "retire-attempt" or "replace-attempt",
            _ => false
        };
        if (!valid)
        {
            error.WriteLine("Usage: initialize-store <new-path> | upgrade-store <existing-path> | pause-installation <path> | installation-status <path> | release-installation <path> | status <path> <revision-id> | history <path> <repository> <issue> | retire-attempt <path> <attempt-id> <stopped-target-check-json> | replace-attempt <path> <predecessor-attempt-id> <retry-key>");
            return 2;
        }
        try
        {
            using var store = args[0] switch
            {
                "initialize-store" => application.InitializeStore(args[1]),
                "upgrade-store" => application.UpgradeStore(args[1]),
                _ => application.OpenStore(args[1])
            };
            object result = args[0] switch
            {
                "status" => store.Status(args[2]),
                "history" => store.History(WorkReference.Parse(args[2], args[3])),
                "pause-installation" => store.PauseInstallation(),
                "installation-status" => store.GetInstallationStatus(),
                "release-installation" => store.ReleaseInstallation(),
                "retire-attempt" => store.RetireStoppedTargetAttempt(args[2], ParseStoppedTargetCheck(args[3])),
                "replace-attempt" => store.PrepareRetry(args[2], args[3]),
                _ => new { operation = args[0], store = store.Path, schema = store.Information }
            };
            // System.Text.Json writes byte arrays as base64, preserving binary source and canonical Contract bytes.
            output.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return 0;
        }
        catch (Exception exception) when (exception is BroodlingException or IOException or UnauthorizedAccessException
            or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Never echo raw exception text, source bytes, paths, or stack traces.
            var code = exception is BroodlingException known ? known.Code : "store_operation_failed";
            error.WriteLine(JsonSerializer.Serialize(new
            {
                error = code,
                message = "Store operation refused. Existing state was not replaced. Inspect the path and retained state before retrying."
            }));
            return 1;
        }
    }

    /// <summary>Every member is required and non-null, and nothing else is accepted, as for <c>check-target</c>.</summary>
    private static StoppedTargetCheck ParseStoppedTargetCheck(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StoppedTargetCheck>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true
            }) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new MaintenanceUnverified("The stopped-target check is incomplete or malformed.");
        }
    }
}
