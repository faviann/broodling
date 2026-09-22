using Broodling.Host;

if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
    return StoreCommands.Run(args, new Broodling.BroodlingApplication(), Console.Out, Console.Error);

var app = BroodlingHost.Build(args);

app.Run();
return 0;
