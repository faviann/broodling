using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Where one preparation of an Issue submission ended. Every case except <see cref="Failed"/> is a retained
/// fact that a later preparation returns again without acquisition or a model call.
/// </summary>
public abstract record SubmissionPreparation
{
    private SubmissionPreparation(string submissionId) => SubmissionId = submissionId;

    public string SubmissionId { get; }

    /// <summary>The submission's Contract has its admission decision: admitted, or rejected with its findings.</summary>
    public sealed record Decided(IssueSubmission Submission, AdmissionStatus Admission)
        : SubmissionPreparation(Submission.SubmissionId);

    /// <summary>Capture was refused; the RequestBundle retains its findings and the submission is rejected.</summary>
    public sealed record CaptureRefused(IssueSubmission Submission, RequestBundle Bundle)
        : SubmissionPreparation(Submission.SubmissionId);

    /// <summary>The Contract proposal was refused; the submission retains its findings and is rejected.</summary>
    public sealed record ProposalRefused(IssueSubmission Submission, ContractProposalRefusal Refusal)
        : SubmissionPreparation(Submission.SubmissionId);

    /// <summary>The submission is cancelled, so it cannot progress.</summary>
    public sealed record Cancelled(IssueSubmission Submission) : SubmissionPreparation(Submission.SubmissionId);

    /// <summary>
    /// Preparation stopped with nothing retained beyond its committed checkpoints. A retryable failure is
    /// temporary, or waits for the installation pause to be released; any other needs attention. The code
    /// and message are safe for operator output.
    /// </summary>
    public sealed record Failed(string SubmissionId, string Code, string Message, bool Retryable)
        : SubmissionPreparation(SubmissionId);
}

/// <summary>
/// Prepares one exact accepted Issue submission through repository selection, bounded capture and the
/// bundled proposer to its admission decision or retained finding, continuing from committed checkpoints.
/// The process holds one instance: concurrent callers for a submission share its one in-flight preparation,
/// while different submissions prepare independently. Each preparation runs off the caller's thread in its
/// own store session. It neither discovers nor schedules work and has no retry cadence.
/// </summary>
public sealed class SubmissionPreparer(BroodlingApplication application, string storePath, string repositoryRoot)
{
    private readonly Dictionary<string, Task<SubmissionPreparation>> preparing = [];

    /// <summary>Tests control acquisition; production uses the authenticated GitHub CLI and Git.</summary>
    internal GitHubIssueSource? IssueSource { get; init; }
    internal GitHubRepositorySource? RepositorySource { get; init; }
    /// <summary>Tests control the model gateway; production uses the pinned endpoint.</summary>
    internal HttpMessageHandler? Gateway { get; init; }

    /// <summary>
    /// Prepare the submission, or join its preparation already in progress. The credentials and the
    /// cancellation token of the caller that starts a preparation are the ones it uses (gateway credentials
    /// default to the process environment). Another caller's token only stops its own wait; if the starting
    /// caller cancels, a caller still waiting takes over.
    /// </summary>
    public async Task<SubmissionPreparation> PrepareAsync(string submissionId, GitHubRepositoryCredentials github,
        GatewayCredentials? gateway = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submissionId);
        ArgumentNullException.ThrowIfNull(github);
        while (true)
        {
            TaskCompletionSource<SubmissionPreparation>? owner = null;
            Task<SubmissionPreparation>? preparation;
            lock (preparing)
            {
                if (!preparing.TryGetValue(submissionId, out preparation))
                {
                    owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    preparing[submissionId] = preparation = owner.Task;
                }
            }
            if (owner is not null)
            {
                // SQLite and Git work in a session is synchronous; keep it off the caller's thread.
                var run = Task.Run(() => PrepareOnceAsync(submissionId, github, gateway, cancellationToken),
                    CancellationToken.None);
                try { await run; }
                catch (Exception) { } // Observed through the shared task below.
                lock (preparing) preparing.Remove(submissionId);
                owner.SetFromTask(run);
            }
            try { return await preparation.WaitAsync(cancellationToken); }
            // The starting caller cancelled and nothing is in flight: continue as the owner.
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
    }

    private async Task<SubmissionPreparation> PrepareOnceAsync(string submissionId, GitHubRepositoryCredentials github,
        GatewayCredentials? gateway, CancellationToken cancellationToken)
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
                    IssueSource, RepositorySource, null, cancellationToken);
                if (bundle.State != "complete")
                    return Ended(store, store.GetIssueSubmission(submissionId))
                        ?? throw new InvalidOperationException("A refused capture left its submission unrejected.");
            }
            var admission = await store.AdmitRequestBundleAsync(submissionId, gateway, Gateway, cancellationToken);
            var decided = store.GetIssueSubmission(submissionId);
            return Ended(store, decided) ?? new SubmissionPreparation.Decided(decided, admission);
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

    /// <summary>The retained cancellation or refusal that ends preparation before any decision, if any.</summary>
    private static SubmissionPreparation? Ended(BroodlingStore store, IssueSubmission submission)
    {
        if (submission.State == "cancelled")
            return new SubmissionPreparation.Cancelled(submission);
        if (submission.ProposalRefusal is { } refusal)
            return new SubmissionPreparation.ProposalRefused(submission, refusal);
        if (submission.ContractRevisionId is null && submission.State == "rejected")
            return new SubmissionPreparation.CaptureRefused(submission, store.GetRequestBundle(submission.SubmissionId));
        return null;
    }

    private static SubmissionPreparation.Failed Failure(string submissionId, Exception failure) => failure is BroodlingException error
        ? new(submissionId, error.Code, error.Message, error switch
        {
            GitHubSourceError source => source.Retryable,
            GitHubRepositoryError repository => repository.Retryable,
            ContractProposerError proposer => proposer.Retryable,
            // Release of the pause, or a store that is busy or not yet readable.
            InstallationPaused or StoreStateException => true,
            _ => false
        })
        : new(submissionId, "store_unavailable", "The store is unavailable or busy.", true);
}
