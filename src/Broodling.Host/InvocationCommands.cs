using System.Text.Json;

namespace Broodling.Host;

/// <summary>Reviewed-source operator input over the callable application. No provider or lifecycle decisions.</summary>
public static class InvocationCommands
{
    public static async Task<int> RunAsync(string[] args, BroodlingApplication application, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default, GitHubIssueSource? source = null, INativeTransport? transport = null)
    {
        if (!(args.Length == 10 && args[0] == "submit" || args.Length is >= 3 and <= 6 && args[0] == "resume"
            || args.Length is 3 or 4 && args[0] == "wait"
            || args.Length == 5 && args[0] == "stop"))
        {
            error.WriteLine("Usage: submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch|-> <reviewed-issue.json> <producer> | resume <store> <contract-revision-id> [config.json [checkout [revision]]] | wait <store> <attempt-id> [python-executable] | stop <store> <attempt-id> <reason> <python-executable>");
            return 2;
        }
        try
        {
            using var store = application.OpenStore(args[1]);
            if (args[0] == "wait")
            {
                var completion = store.FindCompletion(args[2]);
                if (completion is null)
                {
                    if (transport is null && args.Length < 4)
                        throw new SubmissionNotReady("Waiting on an unretained result requires the pinned SDK Python executable.");
                    completion = await store.WaitAsync(args[2], transport ?? new ZeroshotTransport(args[3]), cancellationToken);
                }
                output.WriteLine(JsonSerializer.Serialize(completion, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return 0;
            }
            if (args[0] == "stop")
            {
                string? refusal = null;
                var exitCode = 0;
                try { await store.StopAsync(args[2], args[3], transport ?? new ZeroshotTransport(args[4]), cancellationToken); }
                catch (Exception exception)
                {
                    refusal = exception is BroodlingException known ? known.Code : exception is OperationCanceledException ? "caller_detached" : "stop_failed";
                    exitCode = exception is OperationCanceledException ? 130 : 1;
                }
                var attempt = store.GetAttempt(args[2]);
                var submission = store.FindSubmission(args[2]);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    attempt, submission, quarantined = submission is { State: not "prepared" }, error = refusal,
                    message = attempt.Abandonment is null ? "Stop refused; inspect retained authority."
                        : attempt.Retirement is null ? "Attempt abandoned. Cessation unconfirmed; retain the checkout and use operator containment. No automatic retry."
                        : "Attempt abandoned with retained safe cessation proof. Retirement and replacement remain explicit operations."
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return exitCode;
            }
            AdmissionStatus? status = null;
            if (args[0] == "resume")
            {
                status = store.Status(args[2]);
                // Inspection/reconnection handles do not need configuration or old dispatch credentials.
                if (Invocation.CanResumeWithoutDispatch(status))
                {
                    Write(status, output);
                    return 0;
                }
                if (args.Length < 4) throw new SubmissionNotReady("Dispatch configuration is required for uncorrelated resume.");
            }
            var configPath = args[0] == "submit" ? args[2] : args[3];
            var config = InvocationConfiguration.Read(configPath);
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
