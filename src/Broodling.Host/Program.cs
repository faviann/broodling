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

var app = BroodlingHost.Build(args);

app.Run();
return 0;
