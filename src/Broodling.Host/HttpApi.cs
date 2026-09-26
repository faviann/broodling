using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Broodling.Host;

/// <summary>
/// HTTP over the application operations. Each request opens and disposes its own session and never holds the
/// SQLite writer during an external call. Retained reads contact no GitHub or provider; submission and Attempt
/// reads add one bounded, unretained native observation. Submit, resume and stop need a processing server, and a
/// request's response or disconnect never decides what happens to accepted work.
/// </summary>
internal static class HttpApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal sealed record SubmitRequest(string? IssueUrl);
    internal sealed record StopRequest(string? Reason);

    internal static void Map(WebApplication app, BroodlingApplication application, string storePath,
        string? directTargetRootCertificate, Processing? processing)
    {
        // Reads observe with the configured root; the retained binding still decides where they connect.
        async Task<IResult> Respond(Func<BroodlingStore, Task<IResult>> operation)
        {
            try
            {
                using var store = application.OpenStore(storePath, directTargetRootCertificate);
                return await operation(store);
            }
            catch (Exception exception)
            {
                // Responses never echo raw exception text, source bytes or paths; only the server log keeps a fault.
                var status = exception switch
                {
                    UnknownRecord => 404,
                    InvalidWorkReference or BroodlingException { Code: "invalid_text" } => 400,
                    RequestBundleConflict or WorkUnitIdentityConflict or IssueSubmissionConflict => 409,
                    StoreStateException or SqliteException { SqliteErrorCode: 5 or 6 } => 503,
                    _ => 500
                };
                if (status == 500)
                    app.Logger.LogError(exception, "Request failed.");
                return Results.Json(new
                {
                    error = exception is BroodlingException known ? known.Code : "request_failed",
                    message = "Request refused. Inspect retained state before retrying."
                }, Json, statusCode: status);
            }
        }
        Task<IResult> Read(Func<BroodlingStore, object> read) => Respond(store => Task.FromResult(Results.Json(read(store), Json)));

        // Accepting, resuming or stopping work promises what only a processing server does.
        Task<IResult> Operate(Func<BroodlingStore, Processing, Task<IResult>> operation) => processing is null
            ? Task.FromResult(Results.Json(new
            {
                error = "processing_not_configured",
                message = "This server only reads. Configure Broodling:Invocation and Broodling:RepositoryRoot to submit, resume or stop work."
            }, Json, statusCode: 503))
            : Respond(store => operation(store, processing));

        async Task<object> Submission(BroodlingStore store, string submissionId)
        {
            var submission = store.GetIssueSubmission(submissionId);
            return new
            {
                submission,
                admission = submission.ContractRevisionId is { } revision ? store.Status(revision).Decision : null,
                // Only what this process holds: a running operation, or the reason it waits or stopped.
                progression = processing?.Progressor.Progress().SingleOrDefault(entry => entry.SubmissionId == submissionId),
                observation = submission.AttemptIds.Count == 0 ? null : await Observe(store, submission.AttemptIds[^1])
            };
        }

        app.MapGet("/health", () => Read(_ => new { status = "ok" }));
        app.MapGet("/issues", (string url) => Read(store => new
        {
            submissions = store.IssueHistory(url),
            revisions = store.History(WorkReference.ParseIssueUrl(url))
        }));
        app.MapGet("/submissions/{id}", (string id) => Respond(async store => Results.Json(await Submission(store, id), Json)));
        app.MapGet("/submissions/{id}/bundle", (string id) => Read(store => store.GetRequestBundle(id)));
        // Reference identities can contain '/', which a route segment cannot carry decoded.
        app.MapGet("/bundles/{bundleId}/reference", (string bundleId, string id) =>
            Read(store => store.ReadRequestBundleReference(bundleId, id)));
        app.MapGet("/revisions/{id}", (string id) => Read(store => store.Status(id)));
        app.MapGet("/attempts/{id}", (string id) => Respond(async store =>
        {
            // An Attempt's Contract revision never changes; its Status is one coherent snapshot.
            var status = store.Status(store.GetAttempt(id).ContractRevisionId);
            return Results.Json(new
            {
                attempt = status.Attempts.Single(attempt => attempt.AttemptId == id),
                submission = status.Submissions.SingleOrDefault(submission => submission.AttemptId == id),
                completion = status.Completions.SingleOrDefault(completion => completion.AttemptId == id),
                observation = await Observe(store, id)
            }, Json);
        }));

        // Durable acceptance only, answered from the committed record with no GitHub, model or target contact:
        // preparation, admission and dispatch belong to the progressor, which discovers the submission at its
        // next scan. Ordinary repetition returns the latest existing submission.
        app.MapPost("/submissions", (SubmitRequest request) => Operate((store, _) =>
        {
            var submission = store.SubmitIssue(request.IssueUrl ?? "");
            return Task.FromResult(Results.Json(new { submission }, Json, statusCode: 202)
                .WithLocation($"/submissions/{submission.SubmissionId}"));
        }));
        // The one progression owner continues the exact submission; ended or correlated work is handed back.
        app.MapPost("/submissions/{id}/resume", (string id) => Operate((store, service) =>
        {
            store.GetIssueSubmission(id);
            var resumed = service.Progressor.Resume(id);
            var result = Results.Json(new { submission = store.GetIssueSubmission(id), resumed }, Json, statusCode: resumed ? 202 : 200);
            return Task.FromResult(resumed ? result.WithLocation($"/submissions/{id}") : result);
        }));
        // Stops run to their own bounds whatever the caller does; only shutdown detaches them.
        var stopping = app.Lifetime.ApplicationStopping;
        app.MapPost("/submissions/{id}/stop", (string id, StopRequest request) => Operate(async (store, _) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired();
            string? refusal = null;
            try { await store.CancelIssueSubmissionAsync(id, request.Reason, null, stopping); }
            catch (BroodlingException stop) when (stop is not (UnknownRecord or StoreStateException or IssueSubmissionConflict))
            {
                refusal = stop.Code; // A native stop that followed the committed cancellation.
            }
            var submission = store.GetIssueSubmission(id);
            // Committed cancellation answers 200 even when the native stop that followed was refused or timed out.
            return Results.Json(new
            {
                submission,
                attempt = submission.Cancellation?.AttemptId is { } attemptId ? InvocationCommands.StopReport(store, attemptId, refusal) : null,
                error = refusal
            }, Json, statusCode: submission.Cancellation is null ? 409 : 200);
        }));
        app.MapPost("/attempts/{id}/stop", (string id, StopRequest request) => Operate(async (store, _) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired();
            // Without a bridge transport a LocalTarget run could be abandoned but never asked to stop.
            if (store.FindSubmission(id) is { Format: NativeSubmission.Bridge, State: not "prepared" })
                return Results.Json(new
                {
                    error = "python_required",
                    message = "Stopping a dispatched LocalTarget run requires the release artifact's stop command with its LocalTarget config.json. The Attempt was not abandoned."
                }, Json, statusCode: 409);
            string? refusal = null;
            try { await store.StopAsync(id, request.Reason, null, stopping); }
            catch (BroodlingException stop) when (stop is not (UnknownRecord or StoreStateException)) { refusal = stop.Code; }
            var attempt = store.GetAttempt(id);
            // Committed abandonment answers 200 even when the native stop was refused or timed out.
            return Results.Json(InvocationCommands.StopReport(store, id, refusal), Json,
                statusCode: refusal is null || attempt.Abandonment is not null ? 200 : 409);
        }));
    }

    /// <summary>
    /// One bounded native observation, never retained. Null means nothing was observed: the Attempt has no
    /// addressable run, or its completion is already retained.
    /// </summary>
    private static async Task<object?> Observe(BroodlingStore store, string attemptId)
    {
        if (store.FindCompletion(attemptId) is not null) return null;
        NativeObservation? observation;
        // The read's own bound applies; a disconnected reader only wastes it.
        try { observation = await store.ObserveAsync(attemptId, null, CancellationToken.None); }
        // A dispatched LocalTarget run is observed only through its pinned SDK bridge, which the server lacks.
        catch (SubmissionNotReady) { observation = new NativeObservation.Unavailable(DateTimeOffset.UtcNow, NativeRunIdentity.Confirmed, "python_required"); }
        var identity = observation?.Identity == NativeRunIdentity.Intended ? "intended" : "confirmed";
        return observation switch
        {
            NativeObservation.Available available => new
            {
                attemptId, availability = "available", available.ObservedAt, runIdentity = identity,
                available.Progress.Phase, available.Progress.ActiveNodes
            },
            NativeObservation.Unavailable unavailable => new
            {
                attemptId, availability = "unavailable", unavailable.ObservedAt, runIdentity = identity, unavailable.Reason
            },
            _ => null
        };
    }

    private static IResult ReasonRequired() => Results.Json(new
    {
        error = "reason_required",
        message = "A stop names its reason. Nothing was changed."
    }, Json, statusCode: 400);

    private static IResult WithLocation(this IResult result, string location) => new Located(result, location);

    private sealed class Located(IResult result, string location) : IResult
    {
        public Task ExecuteAsync(HttpContext context)
        {
            context.Response.Headers.Location = location;
            return result.ExecuteAsync(context);
        }
    }
}
