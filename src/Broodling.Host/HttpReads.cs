using System.Text.Json;

namespace Broodling.Host;

/// <summary>
/// Read-only HTTP over retained application reads. Each request opens and disposes its own
/// session, reserves no SQLite writer and contacts no GitHub, provider or native target.
/// </summary>
internal static class HttpReads
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void Map(WebApplication app, BroodlingApplication application, string storePath)
    {
        IResult Read(Func<BroodlingStore, object> read)
        {
            try
            {
                using var store = application.OpenStore(storePath);
                return Results.Json(read(store), Json);
            }
            catch (Exception exception)
            {
                // Responses never echo raw exception text, source bytes or paths; only the server log keeps a fault.
                var status = exception switch
                {
                    UnknownRecord => 404,
                    InvalidWorkReference or BroodlingException { Code: "invalid_text" } => 400,
                    RequestBundleConflict or WorkUnitIdentityConflict => 409,
                    StoreStateException => 503,
                    _ => 500
                };
                if (status == 500)
                    app.Logger.LogError(exception, "Retained read failed.");
                return Results.Json(new
                {
                    error = exception is BroodlingException known ? known.Code : "read_failed",
                    message = "Read refused. Retained state was not changed."
                }, Json, statusCode: status);
            }
        }

        app.MapGet("/health", () => Read(_ => new { status = "ok" }));
        app.MapGet("/issues", (string url) => Read(store => new
        {
            submissions = store.IssueHistory(url),
            revisions = store.History(WorkReference.ParseIssueUrl(url))
        }));
        app.MapGet("/submissions/{id}", (string id) => Read(store => store.GetIssueSubmission(id)));
        app.MapGet("/submissions/{id}/bundle", (string id) => Read(store => store.GetRequestBundle(id)));
        // Reference identities can contain '/', which a route segment cannot carry decoded.
        app.MapGet("/bundles/{bundleId}/reference", (string bundleId, string id) =>
            Read(store => store.ReadRequestBundleReference(bundleId, id)));
        app.MapGet("/revisions/{id}", (string id) => Read(store => store.Status(id)));
        app.MapGet("/attempts/{id}", (string id) => Read(store =>
        {
            // An Attempt's Contract revision never changes; its Status is one coherent snapshot.
            var status = store.Status(store.GetAttempt(id).ContractRevisionId);
            return new
            {
                attempt = status.Attempts.Single(attempt => attempt.AttemptId == id),
                submission = status.Submissions.SingleOrDefault(submission => submission.AttemptId == id),
                completion = status.Completions.SingleOrDefault(completion => completion.AttemptId == id)
            };
        }));
    }
}
