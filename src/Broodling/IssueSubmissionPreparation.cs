using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Where one preparation of an Issue submission ended. Every case except <see cref="Failed"/> is a retained
/// fact that a later preparation returns again without acquisition or a model call.
/// </summary>
public abstract record IssueSubmissionPreparation
{
    private IssueSubmissionPreparation(string submissionId) => SubmissionId = submissionId;

    public string SubmissionId { get; }

    /// <summary>The submission's Contract has its admission decision: admitted, or rejected with its findings.</summary>
    public sealed record Decided(IssueSubmission Submission, AdmissionStatus Admission)
        : IssueSubmissionPreparation(Submission.SubmissionId);

    /// <summary>Capture was refused; the RequestBundle retains its findings and the submission is rejected.</summary>
    public sealed record CaptureRefused(IssueSubmission Submission, RequestBundle Bundle)
        : IssueSubmissionPreparation(Submission.SubmissionId);

    /// <summary>The Contract proposal was refused; the submission retains its findings and is rejected.</summary>
    public sealed record ProposalRefused(IssueSubmission Submission, ContractProposalRefusal Refusal)
        : IssueSubmissionPreparation(Submission.SubmissionId);

    /// <summary>
    /// The submission's work-defining inputs repeat an earlier admitted submission's; it retains that explanation and
    /// link and is neither proposed nor executed.
    /// </summary>
    public sealed record Unchanged(IssueSubmission Submission, IssueSubmissionUnchanged Explanation)
        : IssueSubmissionPreparation(Submission.SubmissionId);

    /// <summary>The submission is cancelled, so it cannot progress.</summary>
    public sealed record Cancelled(IssueSubmission Submission) : IssueSubmissionPreparation(Submission.SubmissionId);

    /// <summary>
    /// Preparation stopped with nothing retained beyond its committed checkpoints. A retryable failure is
    /// temporary, or waits for the installation pause to be released; any other needs attention. The code
    /// and message are safe for operator output.
    /// </summary>
    public sealed record Failed(string SubmissionId, string Code, string Message, bool Retryable)
        : IssueSubmissionPreparation(SubmissionId);
}

/// <summary>
/// Prepares one exact accepted Issue submission through repository selection, bounded capture and the
/// bundled proposer to its admission decision or retained finding, continuing from committed checkpoints.
/// The process holds one instance: concurrent callers for a submission share its one in-flight preparation,
/// while different submissions prepare independently. Each preparation runs off the caller's thread in its
/// own store session. It neither discovers nor schedules work and has no retry cadence.
/// </summary>
/// <param name="lifetime">
/// Cancelled at shutdown. It is the only token that stops a preparation; progress is kept in its committed
/// checkpoints and a later preparer continues from them.
/// </param>
public sealed class IssueSubmissionPreparer(BroodlingApplication application, string storePath, string repositoryRoot,
    CancellationToken lifetime)
{
    private readonly Dictionary<string, Task<IssueSubmissionPreparation>> preparing = [];

    /// <summary>Tests control acquisition; production uses the authenticated GitHub CLI and Git.</summary>
    internal GitHubIssueSource? IssueSource { get; init; }
    internal GitHubRepositorySource? RepositorySource { get; init; }
    /// <summary>Tests control the model gateway; production uses the pinned endpoint.</summary>
    internal HttpMessageHandler? Gateway { get; init; }

    /// <summary>
    /// Prepare the submission, or join its preparation already in progress. The caller that starts a
    /// preparation supplies the credentials it uses (gateway credentials default to the process environment).
    /// A caller's token only ends that caller's wait; the shared preparation continues. At shutdown every
    /// waiting caller observes cancellation.
    /// </summary>
    public Task<IssueSubmissionPreparation> PrepareAsync(string submissionId, GitHubRepositoryCredentials github,
        GatewayCredentials? gateway = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submissionId);
        ArgumentNullException.ThrowIfNull(github);
        Task<IssueSubmissionPreparation>? preparation;
        lock (preparing)
        {
            if (!preparing.TryGetValue(submissionId, out preparation))
            {
                // SQLite and Git work in a session is synchronous; keep it off the caller's thread.
                preparation = Task.Run(() => PrepareOnceAsync(submissionId, github, gateway), CancellationToken.None);
                preparing[submissionId] = preparation;
                preparation.ContinueWith(ended =>
                {
                    lock (preparing)
                        if (preparing.TryGetValue(submissionId, out var current) && current == ended)
                            preparing.Remove(submissionId);
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
        return preparation.WaitAsync(cancellationToken);
    }

    private async Task<IssueSubmissionPreparation> PrepareOnceAsync(string submissionId, GitHubRepositoryCredentials github,
        GatewayCredentials? gateway)
    {
        BroodlingStore? store = null;
        try
        {
            store = application.OpenStore(storePath);
            var submission = store.GetIssueSubmission(submissionId);
            if (Ended(store, submission) is { } ended)
                return ended;
            // A committed Contract association is reused as it is: never captured or proposed again.
            if (submission.ContractRevisionId is null)
            {
                var bundle = await store.CaptureRequestBundleAsync(submissionId, repositoryRoot, github,
                    IssueSource, RepositorySource, null, lifetime);
                if (bundle.State != "complete")
                    return Ended(store, store.GetIssueSubmission(submissionId))
                        ?? throw new InvalidOperationException("A refused capture left its submission unrejected.");
            }
            var admission = await store.AdmitRequestBundleAsync(submissionId, gateway, Gateway, lifetime);
            var decided = store.GetIssueSubmission(submissionId);
            return Ended(store, decided) ?? new IssueSubmissionPreparation.Decided(decided, admission);
        }
        catch (Exception failure) when (failure is BroodlingException or SqliteException)
        {
            // A refusal reports its retained findings; a cancellation that committed meanwhile stands over
            // the failure it caused.
            try
            {
                if (store is not null && Ended(store, store.GetIssueSubmission(submissionId)) is { } ended)
                    return ended;
            }
            catch (Exception unreadable) when (unreadable is BroodlingException or SqliteException) { }
            return Failure(submissionId, failure);
        }
        finally
        {
            store?.Dispose();
        }
    }

    /// <summary>The retained cancellation, unchanged inputs or refusal that ends preparation before any decision, if any.</summary>
    private static IssueSubmissionPreparation? Ended(BroodlingStore store, IssueSubmission submission)
    {
        if (submission.State == "cancelled")
            return new IssueSubmissionPreparation.Cancelled(submission);
        if (submission.Unchanged is { } unchanged)
            return new IssueSubmissionPreparation.Unchanged(submission, unchanged);
        if (submission.ProposalRefusal is { } refusal)
            return new IssueSubmissionPreparation.ProposalRefused(submission, refusal);
        if (submission.ContractRevisionId is null && submission.State == "rejected")
            return new IssueSubmissionPreparation.CaptureRefused(submission, store.GetRequestBundle(submission.SubmissionId));
        return null;
    }

    /// <summary>
    /// Only temporary acquisition and gateway failures, the pause and a busy or locked store are retryable.
    /// Store state, identity and integrity failures, including guard aborts, need attention.
    /// </summary>
    internal static IssueSubmissionPreparation.Failed Failure(string submissionId, Exception failure) => failure switch
    {
        BroodlingException error => new(submissionId, error.Code, error.Message, error switch
        {
            GitHubSourceError source => source.Retryable,
            GitHubRepositoryError repository => repository.Retryable,
            ContractProposerError proposer => proposer.Retryable,
            InstallationPaused => true,
            _ => false
        }),
        // SQLite reports the error code and any guard's abort text; neither carries data or SQL.
        SqliteException sqlite => sqlite.SqliteErrorCode is SQLiteBusy or SQLiteLocked
            ? new(submissionId, "store_busy", sqlite.Message, true)
            : new(submissionId, "store_error", sqlite.Message, false),
        _ => throw new ArgumentException("Unclassified preparation failure.", nameof(failure))
    };

    private const int SQLiteBusy = 5;
    private const int SQLiteLocked = 6;
}
