using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broodling.Host;

/// <summary>Reviewed-source operator input over the callable application. No provider or lifecycle decisions.</summary>
public static class InvocationCommands
{
    private sealed record Configuration(string PythonExecutable, string StateDirectory, string WorkspaceRoot,
        string? DirectOrigin = null, string? RealCodex = null, string? ProfileHome = null, string? CodexHome = null, string? Launcher = null);

    public static async Task<int> RunAsync(string[] args, BroodlingApplication application, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default, GitHubIssueSource? source = null, INativeTransport? transport = null)
    {
        if (!(args.Length == 10 && args[0] == "submit" || args.Length is >= 3 and <= 6 && args[0] == "resume"))
        {
            error.WriteLine("Usage: submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch|-> <reviewed-issue.json> <producer> | resume <store> <contract-revision-id> [config.json [checkout [revision]]]");
            return 2;
        }
        try
        {
            using var store = application.OpenStore(args[1]);
            AdmissionStatus? status = null;
            if (args[0] == "resume")
            {
                status = store.Status(args[2]);
                // Inspection/reconnection handles do not need configuration or old dispatch credentials.
                if (status.Decision is { Admitted: false } || status.Attempts.LastOrDefault() is { IsCurrent: false }
                    || status.Submissions.LastOrDefault()?.State == "correlated")
                {
                    Write(status, output);
                    return 0;
                }
                if (args.Length < 4) throw new SubmissionNotReady("Dispatch configuration is required for uncorrelated resume.");
            }
            var configPath = args[0] == "submit" ? args[2] : args[3];
            var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(configPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidContractProposal("Invalid invocation configuration.");
            // The installed operator profile is loopback-only. The callable API remains separate.
            if (config.DirectOrigin is { } origin && (!Uri.TryCreate(origin, UriKind.Absolute, out var endpoint)
                || endpoint.Port is < 1 or > 65535 || origin != $"http://127.0.0.1:{endpoint.Port}"))
                throw new UnsupportedRuntime("The operator DirectTarget must use a canonical loopback HTTP origin with an explicit port.");
            CodexProfile? codex = config.RealCodex is null ? null : new(config.RealCodex, config.ProfileHome!, config.CodexHome!, config.Launcher!);
            var profile = new NativeProfile(config.StateDirectory, codex, config.DirectOrigin);
            var invocation = new Invocation(store, config.WorkspaceRoot, profile, transport ?? new ZeroshotTransport(config.PythonExecutable));
            var credentials = new DispatchCredentials(Environment.GetEnvironmentVariable("GH_TOKEN"),
                Environment.GetEnvironmentVariable("GATEWAY_BASE_URL"), Environment.GetEnvironmentVariable("GATEWAY_API_KEY"));
            if (args[0] == "submit")
            {
                var proposer = new ReviewedIssueProposal(File.ReadAllBytes(args[8]));
                RequiredEffect[] effects = args[7] == "-" ? [] : [new("pr", "Deliver a pull request to the authorized target branch.", "pull_request", args[7])];
                status = await invocation.SubmitAsync(WorkReference.Parse(args[3], args[4]), proposer.Propose, effects,
                    args[5], args[6], constructedBy: args[9], credentials: credentials, source: source, cancellationToken: cancellationToken);
            }
            else status = await invocation.ResumeAsync(args[2], args.Length > 4 ? args[4] : null,
                args.Length > 5 ? args[5] : "HEAD", credentials, cancellationToken);
            Write(status, output);
            return 0;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("{\"error\":\"caller_detached\",\"message\":\"Caller detached. Inspect history/status and resume the retained revision; native work was not stopped.\"}");
            return 130;
        }
        catch (Exception exception)
        {
            // Process/provider exceptions can contain private paths or credentials. Never echo their text.
            error.WriteLine(JsonSerializer.Serialize(new
            {
                error = exception is BroodlingException known ? known.Code : "invocation_failed",
                message = "Invocation refused. Inspect history/status for retained identifiers and resume that exact revision."
            }));
            return 1;
        }
    }

    private static void Write(AdmissionStatus status, TextWriter output) =>
        output.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
