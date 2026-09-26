using Broodling.Host;

if (args.FirstOrDefault() == "check-target")
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    return await HostTargetCommands.RunAsync(args, Console.Out, cancellationToken: cancellation.Token);
}

if (args.FirstOrDefault() is "submit" or "resume" or "wait" or "stop")
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    return await InvocationCommands.RunAsync(args, new Broodling.BroodlingApplication(), Console.Out, Console.Error, cancellation.Token);
}

if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
    return StoreCommands.Run(args, new Broodling.BroodlingApplication(), Console.Out, Console.Error);

WebApplication app;
try { app = BroodlingHost.Build(args); }
catch (Exception exception) when (exception is Broodling.BroodlingException or IOException or UnauthorizedAccessException
    or Microsoft.Data.Sqlite.SqliteException)
{
    // Never echo raw exception text or paths.
    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        error = exception is Broodling.BroodlingException known ? known.Code : "startup_failed",
        message = "Server startup refused. Configure Broodling:Store as existing initialized state and, to process submissions, "
            + "Broodling:Invocation, an existing Broodling:RepositoryRoot and current credentials; nothing was created or replaced."
    }));
    return 1;
}

// Run disposes the host's services when it returns.
var processing = app.Services.GetService<Broodling.Host.Processing>();
app.Run();
// A processing service that failed unexpectedly stopped the server; exit unsuccessfully for the supervisor.
return processing?.Failed == true ? 1 : 0;
