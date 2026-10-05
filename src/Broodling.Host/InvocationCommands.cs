using System.Text.Json;

namespace Broodling.Host;

/// <summary>Reviewed-source operator input over the callable application. No provider or lifecycle decisions.</summary>
public static class InvocationCommands
{
    public static async Task<int> RunAsync(string[] args, BroodlingApplication application, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default, GitHubIssueSource? source = null)
    {
        // `-` named the removed no-effect request; it is never a target branch.
        if (!(args.Length == 10 && args[0] == "submit" && args[7] != "-" || args.Length is >= 3 and <= 6 && args[0] == "resume"
            || args.Length is 3 or 4 && args[0] == "wait"
            || args.Length is 4 or 5 && args[0] == "stop"))
        {
            error.WriteLine("Usage: submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch> <reviewed-issue.json> <producer> | resume <store> <contract-revision-id> [config.json [checkout [revision]]] | wait <store> <attempt-id> [config.json] | stop <store> <attempt-id> <reason> [config.json]");
            return 2;
        }
        try
        {
            if (args[0] == "resume")
            {
                // Inspection/reconnection handles do not need configuration or old dispatch credentials.
                using (var inspection = application.OpenStore(args[1]))
                {
                    var retained = inspection.Status(args[2]);
                    if (Invocation.CanResumeWithoutDispatch(retained))
                    {
                        Write(retained, output);
                        return 0;
                    }
                }
                if (args.Length < 4) throw new SubmissionNotReady("Dispatch configuration is required for uncorrelated resume.");
            }
            if (args[0] == "wait")
            {
                // A retained completion needs no configuration, even a stale one.
                using var inspection = application.OpenStore(args[1]);
                if (inspection.FindCompletion(args[2]) is { } retained)
                {
                    output.WriteLine(JsonSerializer.Serialize(retained, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                    return 0;
                }
            }
            // wait/stop take the same optional configuration, which supplies only the DirectTarget root certificate.
            var configPath = args[0] switch
            {
                "submit" => args[2],
                "resume" => args[3],
                "wait" => args.Length == 4 ? args[3] : null,
                _ => args.Length == 5 ? args[4] : null
            };
            var configuration = configPath is null ? null : InvocationConfiguration.Read(configPath);
            using var store = application.OpenStore(args[1], configuration?.DirectRootCertificate);
            if (args[0] is "wait" or "stop" && configuration is not null)
            {
                // A supplied configuration must describe the retained target, refused before contact or
                // abandonment. The retained binding, not the configuration, still decides where it connects.
                var retainedOrigin = store.FindSubmission(args[2])?.Origin;
                if (retainedOrigin is not null && retainedOrigin != configuration.DirectOrigin)
                    throw new SubmissionConflict("The configured target differs from the retained Attempt's target.");
            }
            if (args[0] == "wait")
            {
                var completion = await store.WaitAsync(args[2], cancellationToken);
                output.WriteLine(JsonSerializer.Serialize(completion, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return 0;
            }
            if (args[0] == "stop")
            {
                string? refusal = null;
                var exitCode = 0;
                try { await store.StopAsync(args[2], args[3], cancellationToken); }
                catch (Exception exception)
                {
                    refusal = exception is BroodlingException known ? known.Code : exception is OperationCanceledException ? "caller_detached" : "stop_failed";
                    exitCode = exception is OperationCanceledException ? 130 : 1;
                }
                output.WriteLine(JsonSerializer.Serialize(StopReport(store, args[2], refusal), Json));
                return exitCode;
            }
            AdmissionStatus status;
            var invocation = new Invocation(store, configuration!.Target);
            var credentials = new DispatchCredentials(Environment.GetEnvironmentVariable("GH_TOKEN"),
                Environment.GetEnvironmentVariable("GATEWAY_BASE_URL"), Environment.GetEnvironmentVariable("GATEWAY_API_KEY"));
            if (args[0] == "submit")
            {
                var proposer = new ReviewedIssueProposal(File.ReadAllBytes(args[8]));
                RequiredEffect[] effects = [new("pr", "Deliver a pull request to the authorized target branch.", "pull_request", args[7])];
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
            error.WriteLine(JsonSerializer.Serialize(ErrorRecord(exception, "invocation_failed",
                "Invocation refused. Inspect history/status for retained identifiers and resume that exact revision.")));
            return 1;
        }
    }

    /// <summary>
    /// Handback output: retained status with each submission reduced to its status facts. The frozen
    /// request (with its whole asset) stays in the store; status/history show it in full.
    /// </summary>
    private static void Write(AdmissionStatus status, TextWriter output)
    {
        var handback = JsonSerializer.SerializeToNode(status, Json)!.AsObject();
        handback["submissions"] = JsonSerializer.SerializeToNode(status.Submissions.Select(Summary), Json);
        output.WriteLine(handback.ToJsonString());
    }

    /// <summary>
    /// What an exact stop committed, told apart from what it observed: abandonment or retirement, the native
    /// submission's status facts, quarantine, and the refusal that ended the stop, if any.
    /// </summary>
    internal static object StopReport(BroodlingStore store, string attemptId, string? refusal)
    {
        var attempt = store.GetAttempt(attemptId);
        var submission = store.FindSubmission(attemptId);
        return new
        {
            attempt,
            submission = Summary(submission),
            quarantined = submission is { State: not "prepared" } && attempt.Retirement is null, error = refusal,
            message = attempt.Retirement is { Basis: "stopped_target" } ? "Attempt retired under verified stopped-target maintenance."
                : attempt.Abandonment is null ? "Stop refused; inspect retained authority."
                : attempt.Retirement is null ? "Attempt abandoned. Cessation unconfirmed; retain its resources and use operator containment. No automatic retry."
                : "Attempt abandoned with retained safe cessation proof. Retirement and replacement remain explicit operations."
        };
    }

    private static object? Summary(NativeSubmission? submission) => submission is null ? null : new
    {
        submission.AttemptId, submission.State, submission.IntendedRunId, submission.RunId, submission.ReplayBlockedReason
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A safe error record: the refusal code and a fixed message, never exception text. A foreign
    /// acknowledgement adds the run ID the target named, which Broodling did not adopt.
    /// </summary>
    internal static Dictionary<string, string> ErrorRecord(Exception exception, string fallback, string message)
    {
        var record = new Dictionary<string, string>
        {
            ["error"] = exception is BroodlingException known ? known.Code : fallback, ["message"] = message
        };
        if (exception is NativeTransportError { AcknowledgedRunId: { } foreign }) record["acknowledgedRunId"] = foreign;
        return record;
    }
}
