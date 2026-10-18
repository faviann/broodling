using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Broodling;

public sealed partial class BroodlingStore
{
    private static readonly JsonSerializerOptions CaptureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Capture one accepted Issue submission's primary issue, its whole body as the Executable Request, and
    /// the pinned repository. An unavailable issue or an empty body is a refusal retained on the refused
    /// bundle; operational failures throw and a later call resumes without refetching the captured issue.
    /// A sealed bundle is returned without acquisition.
    /// </summary>
    public async Task<RequestBundle> CaptureRequestBundleAsync(string submissionId, string repositoryRoot,
        GitHubRepositoryCredentials credentials, GitHubIssueSource? issueSource = null,
        GitHubRepositorySource? repositorySource = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var work = GetWorkUnit(GetIssueSubmission(submissionId).WorkUnitId);
        var primary = WorkReference.Parse(work.Host + "/" + work.Owner + "/" + work.Repository,
            work.IssueNumber, work.RepositoryIdentity, work.IssueIdentity);
        // Begin replays an existing bundle in any state only under this exact plan.
        var bundle = BeginRequestBundleCapture(submissionId, new RequestBundlePlan(
            Json(new { issue = primary.IssueLocator }), Json(new { request = "primary_issue_body" })));
        if (bundle.State is "complete" or "refused")
            return bundle;

        var captured = RegisterRequestBundleReference(bundle.BundleId, "primary",
            Json(new { role = "primary", url = primary.IssueLocator }));
        if (!captured.IsCaptured)
        {
            AcquiredIssue issue;
            try { issue = await (issueSource ?? new GitHubIssueSource()).AcquireAsync(primary, credentials, cancellationToken); }
            catch (GitHubSourceError error) when (error.Unavailable)
            {
                return RefuseRequestBundleCapture(bundle.BundleId, [new("issue_unavailable", "primary", error.Message)]);
            }
            captured = CaptureRequestBundleSource(bundle.BundleId, "primary", issue.Source);
        }

        var body = Body(GetEntitledSource(captured.SourceId!).Content);
        if (string.IsNullOrWhiteSpace(body))
            return RefuseRequestBundleCapture(bundle.BundleId,
                [new("request_empty", "primary", "The primary issue body is empty, so there is no Executable Request.")]);
        RegisterRequestBundleReference(bundle.BundleId, "request", Json(new { role = "request" }));
        CaptureRequestBundleSource(bundle.BundleId, "request", new SourceSubmission("executable_request",
            primary.IssueLocator, Encoding.UTF8.GetBytes(body), "text/markdown; charset=utf-8",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), "broodling_policy",
            new("broodling_policy", "primary_issue_body")));

        await PrepareRequestBundleRepositoryAsync(bundle.BundleId, repositoryRoot, credentials,
            repositorySource, cancellationToken);
        return CompleteRequestBundleCapture(bundle.BundleId);
    }

    private static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value, CaptureJson);

    // Acquisition already validated the issue document shape.
    private static string Body(byte[] content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("body").GetString() ?? "";
    }
}
