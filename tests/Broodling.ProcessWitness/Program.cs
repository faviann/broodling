using Broodling;

// A local test caller, never a product command or runtime helper.
try
{
    if (args[0] == "http-dispatch")
    {
        // The parent's controlled target decides where this caller is killed.
        // The parent's target origin and its control token file, as the operator configuration names them.
        using var httpStore = new BroodlingApplication().OpenStore(args[1], DirectTargetAccess.For(args[3], args[4]));
        await httpStore.DispatchHttpAsync(args[2], new DispatchCredentials("witness-github-token", DirectTargetBinding.GatewayBaseUrl, "witness-gateway-key"));
        return 99;
    }
    if (args[0] == "replacement-crash")
    {
        // The parent kills this caller at the printed boundary of one HTTP replacement.
        using var replacementStore = new BroodlingApplication().OpenStore(args[1]);
        var mode = args[3];
        if (mode is "allocation-write" or "prepare-write")
        {
            var connection = (Microsoft.Data.Sqlite.SqliteConnection)typeof(BroodlingStore)
                .GetField("connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(replacementStore)!;
            connection.CreateFunction("crash_gate", () => { CrashGate.Hold(mode); return 1; });
            using var command = connection.CreateCommand();
            command.CommandText = mode == "allocation-write"
                ? "CREATE TEMP TRIGGER crash_gate AFTER INSERT ON attempts BEGIN SELECT crash_gate(); END;"
                : "CREATE TEMP TRIGGER crash_gate AFTER INSERT ON native_submissions BEGIN SELECT crash_gate(); END;";
            command.ExecuteNonQuery();
        }
        if (mode is "allocation-write" or "allocated") replacementStore.AdmitRetry(args[2], "process-retry");
        else replacementStore.PrepareRetry(args[2], "process-retry");
        CrashGate.Hold(mode);
        return 99;
    }
    if (args[0] == "repository-preparation-crash")
    {
        using var preparationStore = new BroodlingApplication().OpenStore(args[1]);
        var source = new GitHubRepositorySource(args[4], args[5]);
        await preparationStore.PrepareRequestBundleRepositoryAsync(args[2], args[3],
            new GitHubRepositoryCredentials("configured-token"), source);
        return 0;
    }
    Console.Error.WriteLine("Unknown witness mode: " + args[0]);
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
    return 1;
}

internal static class CrashGate
{
    internal static void Hold(string value)
    {
        Console.WriteLine(value);
        Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite); // Parent SIGKILL, not managed unwinding, exercises each durable boundary.
    }
}
