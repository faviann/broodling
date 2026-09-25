using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Broodling;

/// <summary>Finite first-profile acquisition bounds, retained with the bundle manifest.</summary>
internal sealed record RequestBundleLimits(int MaxReferences, long MaxItemBytes, long MaxTotalBytes)
{
    public static RequestBundleLimits Default { get; } = new(50, 1024 * 1024, 8 * 1024 * 1024);
}

public sealed partial class BroodlingStore
{
    private static readonly JsonSerializerOptions CaptureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class CaptureRefused(IReadOnlyList<RequestCaptureFinding> findings) : Exception
    {
        public CaptureRefused(string code, string subject, string detail) : this([new(code, subject, detail)]) { }
        public IReadOnlyList<RequestCaptureFinding> Findings { get; } = findings;
    }

    /// <summary>
    /// Capture one accepted Issue submission's v1 Executable Request, pinned
    /// repository and bounded reference closure. Deterministic refusals are
    /// retained on the refused bundle; operational failures throw and a later
    /// call resumes from committed captures without refetching them. A sealed
    /// bundle is returned without acquisition. A new capture always uses the
    /// first-profile bounds.
    /// </summary>
    public Task<RequestBundle> CaptureRequestBundleAsync(string submissionId, string repositoryRoot,
        GitHubRepositoryCredentials credentials, GitHubIssueSource? issueSource = null,
        GitHubRepositorySource? repositorySource = null, CancellationToken cancellationToken = default) =>
        CaptureRequestBundleAsync(submissionId, repositoryRoot, credentials, issueSource, repositorySource,
            null, cancellationToken);

    /// <summary>Tests begin a capture with small bounds; production callers cannot choose them.</summary>
    internal async Task<RequestBundle> CaptureRequestBundleAsync(string submissionId, string repositoryRoot,
        GitHubRepositoryCredentials credentials, GitHubIssueSource? issueSource,
        GitHubRepositorySource? repositorySource, RequestBundleLimits? limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var issues = issueSource ?? new GitHubIssueSource();
        var work = GetWorkUnit(GetIssueSubmission(submissionId).WorkUnitId);
        var primary = WorkReference.Parse(work.Host + "/" + work.Owner + "/" + work.Repository,
            work.IssueNumber, work.RepositoryIdentity, work.IssueIdentity);
        // Begin replays an existing bundle in any state only under this exact
        // plan, so a bundle frozen with other inputs, policy or limits conflicts.
        var bounds = limits ?? RequestBundleLimits.Default;
        var bundle = BeginRequestBundleCapture(submissionId, new RequestBundlePlan(
            Json(new { issue = primary.IssueLocator }),
            Json(new { convention = ExecutableRequest.Convention, traversal = "declared-then-breadth-first-github-issue-comment-links" }),
            Json(bounds)));
        if (bundle.State is "complete" or "refused")
            return bundle;

        var total = 0L;
        void Measure(string referenceId, long size)
        {
            total += size;
            if (size > bounds.MaxItemBytes)
                throw new CaptureRefused("reference_limit_exceeded", referenceId,
                    $"The captured item exceeds {bounds.MaxItemBytes} bytes.");
            if (total > bounds.MaxTotalBytes)
                throw new CaptureRefused("reference_limit_exceeded", referenceId,
                    $"The captured bundle exceeds {bounds.MaxTotalBytes} bytes in total.");
        }

        // First capture wins; always continue from the retained bytes.
        async Task<byte[]> Source(string referenceId, Func<Task<SourceSubmission>> acquire)
        {
            if (!ReadBundleReference(bundle.BundleId, referenceId, null)!.IsCaptured)
            {
                SourceSubmission acquired;
                try { acquired = await acquire(); }
                catch (GitHubSourceError error) when (!error.Retryable)
                {
                    throw new CaptureRefused("reference_unavailable", referenceId, error.Message);
                }
                CaptureRequestBundleSource(bundle.BundleId, referenceId, acquired);
            }
            var content = ReadCapturedBundleReference(bundle.BundleId, referenceId).Content;
            Measure(referenceId, content.LongLength);
            return content;
        }

        var primaryId = RequestTarget.GitHub(primary.Owner, primary.Repository,
            primary.IssueNumber.ToString(CultureInfo.InvariantCulture), null)!.ReferenceId;
        var queue = new List<RequestTarget>();
        var seen = new HashSet<string> { primaryId };
        void Select(RequestTarget target, object selector)
        {
            if (!seen.Add(target.ReferenceId)) return;
            if (queue.Count == bounds.MaxReferences)
                throw new CaptureRefused("reference_limit_exceeded", target.ReferenceId,
                    $"The reference closure exceeds {bounds.MaxReferences} references.");
            if (target.Path is { } path)
                RegisterRequestBundleRepositoryFile(bundle.BundleId, target.ReferenceId, Json(selector), path);
            else
                RegisterRequestBundleReference(bundle.BundleId, RequestBundleReferenceInput.Source(target.ReferenceId, Json(selector)));
            queue.Add(target);
        }

        try
        {
            RegisterRequestBundleReference(bundle.BundleId, RequestBundleReferenceInput.Source("primary",
                Json(new { role = "primary", url = primary.IssueLocator })));
            var issue = await Source("primary", async () =>
                (await issues.AcquireAsync(primary, credentials, "primary_issue", cancellationToken)).Source);
            var findings = new List<RequestCaptureFinding>();
            var request = ExecutableRequest.Parse(Body(issue), findings);
            foreach (var declaration in request?.Declarations ?? [])
                if (declaration.Selected.ReferenceId == primaryId)
                    findings.Add(new("invalid_reference_declaration", declaration.Label,
                        "The primary issue is not an available reference."));
            if (findings.Count > 0)
                throw new CaptureRefused(findings);

            RegisterRequestBundleReference(bundle.BundleId, RequestBundleReferenceInput.Source("request",
                Json(new { role = "request", convention = ExecutableRequest.Convention })));
            await Source("request", () => Task.FromResult(new SourceSubmission("executable_request",
                primary.IssueLocator, Encoding.UTF8.GetBytes(request!.Text), "text/markdown; charset=utf-8",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), "broodling_policy",
                new("broodling_policy", "primary_issue_request_section"))));

            await PrepareRequestBundleRepositoryAsync(bundle.BundleId, repositoryRoot, credentials,
                repositorySource, cancellationToken);
            foreach (var declaration in request!.Declarations)
                Select(declaration.Selected, new { role = "declared", label = declaration.Label, target = declaration.Target });

            // Breadth-first in registration order. Repository documents and
            // external links never extend the closure.
            for (var index = 0; index < queue.Count; index++)
            {
                var target = queue[index];
                if (target.Path is not null)
                {
                    // Git reports the size first, so an oversized file is refused unread and uncaptured.
                    long size;
                    try
                    {
                        var captured = CaptureRequestBundleGitBlob(bundle.BundleId, target.ReferenceId,
                            Math.Min(bounds.MaxItemBytes, bounds.MaxTotalBytes - total));
                        size = GitCustody.BlobSize(captured.GitRepository!, captured.GitBlobOid!);
                    }
                    catch (UnresolvedRepositoryPath error)
                    {
                        throw new CaptureRefused("reference_unavailable", target.ReferenceId, error.Message);
                    }
                    catch (GitBlobTooLarge large)
                    {
                        size = large.Size;
                    }
                    Measure(target.ReferenceId, size);
                    continue;
                }
                var content = await Source(target.ReferenceId, async () => target.CommentId is { } comment
                    ? await issues.AcquireCommentAsync(target.Issue!, comment, credentials, cancellationToken)
                    : (await issues.AcquireAsync(target.Issue!, credentials, "referenced_document", cancellationToken)).Source);
                foreach (var (linked, url) in ExecutableRequest.Links(Body(content)))
                    Select(linked, new { role = "linked", from = target.ReferenceId, url });
            }
        }
        catch (CaptureRefused refused)
        {
            return RefuseRequestBundleCapture(bundle.BundleId, refused.Findings);
        }
        return CompleteRequestBundleCapture(bundle.BundleId);
    }

    private static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value, CaptureJson);

    // Acquisition already validated the issue or comment document shape.
    private static string Body(byte[] content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("body").GetString() ?? "";
    }
}
