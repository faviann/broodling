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
            || args.Length is 4 or 5 && args[0] == "stop"))
        {
            error.WriteLine("Usage: submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch|-> <reviewed-issue.json> <producer> | resume <store> <contract-revision-id> [config.json [checkout [revision]]] | wait <store> <attempt-id> [config.json] | stop <store> <attempt-id> <reason> [config.json]");
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
            // wait/stop take the same optional configuration; the retained record decides what applies:
            // a LocalTarget record its pinned SDK Python, an HTTP record its DirectTarget root certificate.
            var configPath = args[0] switch
            {
                "submit" => args[2],
                "resume" => args[3],
                "wait" => args.Length == 4 ? args[3] : null,
                _ => args.Length == 5 ? args[4] : null
            };
            var configuration = configPath is null ? null : InvocationConfiguration.Read(configPath);
            using var store = application.OpenStore(args[1], (configuration as InvocationConfiguration.Direct)?.DirectRootCertificate);
            transport ??= configuration is InvocationConfiguration.Local local ? new ZeroshotTransport(local.PythonExecutable) : null;
            if (args[0] is "wait" or "stop" && configuration is not null)
            {
                // A supplied configuration must describe the retained target, refused before contact or
                // abandonment. The retained binding, not the configuration, still decides where it connects.
                var retainedKind = store.GetAttempt(args[2]).ResourceKind;
                var retainedOrigin = store.FindSubmission(args[2])?.Locator.Address;
                if (configuration is InvocationConfiguration.Direct direct
                        ? retainedKind != AttemptRecord.Http || retainedOrigin is not null && retainedOrigin != direct.DirectOrigin
                        : retainedKind != AttemptRecord.Worktree)
                    throw new SubmissionConflict("The configured target differs from the retained Attempt's target.");
            }
            if (args[0] == "wait")
            {
                // The store routes on the retained record; only a LocalTarget bridge record uses the transport.
                var completion = await store.WaitAsync(args[2], transport, cancellationToken);
                output.WriteLine(JsonSerializer.Serialize(completion, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return 0;
            }
            if (args[0] == "stop")
            {
                // Without a bridge transport a LocalTarget run could be abandoned but never asked to stop.
                if (transport is null && store.FindSubmission(args[2]) is { Format: NativeSubmission.Bridge, State: not "prepared" })
                {
                    error.WriteLine(JsonSerializer.Serialize(new
                    {
                        error = "python_required",
                        message = "Stopping a dispatched LocalTarget run requires the LocalTarget config.json that names the pinned SDK Python. The Attempt was not abandoned."
                    }));
                    return 1;
                }
                string? refusal = null;
                var exitCode = 0;
                try { await store.StopAsync(args[2], args[3], transport, cancellationToken); }
                catch (Exception exception)
                {
                    refusal = exception is BroodlingException known ? known.Code : exception is OperationCanceledException ? "caller_detached" : "stop_failed";
                    exitCode = exception is OperationCanceledException ? 130 : 1;
                }
                var attempt = store.GetAttempt(args[2]);
                var submission = store.FindSubmission(args[2]);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    attempt,
                    submission = Summary(submission),
                    quarantined = submission is { State: not "prepared" } && attempt.Retirement is null, error = refusal,
                    message = attempt.Retirement is { Basis: "stopped_target" } ? "Attempt retired under verified stopped-target maintenance."
                        : attempt.Abandonment is null ? "Stop refused; inspect retained authority."
                        : attempt.Retirement is null ? "Attempt abandoned. Cessation unconfirmed; retain its resources and use operator containment. No automatic retry."
                        : "Attempt abandoned with retained safe cessation proof. Retirement and replacement remain explicit operations."
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return exitCode;
            }
            AdmissionStatus status;
            var invocation = new Invocation(store, configuration!.ToTarget(transport));
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

    /// <summary>
    /// Handback output: retained status with each submission reduced to its status facts. The frozen
    /// request (with an HTTP Attempt's whole asset) stays in the store; status/history show it in full.
    /// </summary>
    private static void Write(AdmissionStatus status, TextWriter output)
    {
        var handback = JsonSerializer.SerializeToNode(status, Json)!.AsObject();
        handback["submissions"] = JsonSerializer.SerializeToNode(status.Submissions.Select(Summary), Json);
        output.WriteLine(handback.ToJsonString());
    }

    private static object? Summary(NativeSubmission? submission) => submission is null ? null : new
    {
        submission.AttemptId, submission.Format, submission.State, submission.IntendedRunId, submission.RunId,
        submission.ReplayBlockedReason
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
