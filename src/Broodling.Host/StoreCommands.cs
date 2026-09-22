using System.Text.Json;

namespace Broodling.Host;

/// <summary>Thin lifecycle commands, independent of installation and HTTP startup.</summary>
public static class StoreCommands
{
    public static int Run(string[] args, BroodlingApplication application, TextWriter output, TextWriter error)
    {
        if (args.Length != 2 || args[0] is not ("initialize-store" or "upgrade-store"))
        {
            error.WriteLine("Usage: initialize-store <new-path> | upgrade-store <existing-path>");
            return 2;
        }
        try
        {
            using var store = args[0] == "initialize-store"
                ? application.InitializeStore(args[1])
                : application.UpgradeStore(args[1]);
            output.WriteLine(JsonSerializer.Serialize(new { operation = args[0], store = store.Path, schema = store.Information }));
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
}
