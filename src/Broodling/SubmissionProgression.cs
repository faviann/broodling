using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>The current credentials for one progression operation. They are read per operation and never retained.</summary>
public sealed record ProgressionCredentials(GitHubRepositoryCredentials Repository, GatewayCredentials Gateway,
    DispatchCredentials Dispatch);

/// <summary>
/// Where automatic progression of one unfinished Issue submission stands in this process. Only what the store
/// does not retain is here: decisions, findings, cancellation, abandonment, replay blocks and correlation are
/// read from their retained records. Code and message are safe for operator output. <see cref="Failures"/>
/// counts consecutive failures within the last failure's <see cref="Stage"/>.
/// </summary>
public sealed record SubmissionProgress(string SubmissionId, string State, string? Stage = null, string? Code = null,
    string? Message = null, bool Retryable = false, int Failures = 0, DateTimeOffset? RetryAt = null)
{
    /// <summary>An operation is running; any stage and code are the previous failure's.</summary>
    public const string Progressing = "progressing";
    /// <summary>The last operation failed temporarily or met the installation pause; it runs again at <see cref="RetryAt"/>.</summary>
    public const string Waiting = "waiting";
    /// <summary>Needs attention: a refusal or conflict retrying cannot resolve, the retry limit or an unexpected failure. Not retried in this process.</summary>
    public const string Stopped = "stopped";

    /// <summary>Capture, proposal or the admission decision, through <see cref="IssueSubmissionPreparer"/>.</summary>
    public const string Preparation = "preparation";
    /// <summary>Attempt admission, HTTP preparation and dispatch, through <see cref="Invocation.ResumeSubmissionAsync"/>.</summary>
    public const string Continuation = "continuation";
}

/// <summary>
/// Progresses unfinished Issue submissions with no caller connected. Each scan, at startup and then every
/// <see cref="Cadence"/>, reads the unfinished submissions from the store and starts at most one operation per
/// submission, in its own store session. An operation prepares the submission through the shared
/// <see cref="IssueSubmissionPreparer"/>, then continues an admitted Contract through
/// <see cref="Invocation.ResumeSubmissionAsync"/> to native correlation, where <see cref="CompletionObserver"/>
/// takes over. It never waits for an Attempt and never initiates a stop, abandonment or replacement; only the
/// dispatch it calls stops the exact run whose acknowledgement arrives after abandonment.
/// </summary>
/// <param name="directTargetRootCertificate">The PEM root for HTTPS DirectTarget connections; null deliberately selects system trust.</param>
/// <param name="target">The configured DirectTarget. A retained binding to another origin refuses and stops.</param>
/// <param name="preparer">
/// The process's one preparer, shared with callers that prepare the same submissions. Its lifetime ends only at
/// shutdown, so a preparation it cancels detaches like this service's own cancellation.
/// </param>
/// <param name="credentials">Read at the start of each operation and again before continuation, so rotation applies to a replay.</param>
/// <param name="stopped">
/// Called once when a submission stops and needs attention, with the exception only for an unexpected failure.
/// A callback that throws is ignored. It may be invoked concurrently from thread-pool threads.
/// </param>
public sealed class SubmissionProgressor(BroodlingApplication application, string storePath, string? directTargetRootCertificate,
    InvocationTarget.Direct target, IssueSubmissionPreparer preparer, Func<ProgressionCredentials> credentials,
    Action<SubmissionProgress, Exception?> stopped)
{
    /// <summary>
    /// The scan interval and the first retry delay. A retry, including a recheck of the pause, runs at the first
    /// scan at least its delay after the failure.
    /// </summary>
    internal static readonly TimeSpan Cadence = TimeSpan.FromSeconds(15);
    /// <summary>The retry delay doubles from <see cref="Cadence"/> up to this.</summary>
    internal static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromHours(1);
    /// <summary>
    /// Consecutive temporary failures in one stage after which a submission stops, about two hours after the
    /// first: long enough for a target or gateway maintenance outage, while a retryable failure that repeats on
    /// the same frozen input (#112) makes at most this many model calls.
    /// </summary>
    internal const int RetryLimit = 10;

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    private int scans;
    /// <summary>Completed discovery passes, so tests can tell a scan has happened.</summary>
    internal int Scans => Volatile.Read(ref scans);

    private readonly Dictionary<string, SubmissionProgress> progress = [];

    /// <summary>Every unfinished submission this process is progressing, waiting on or has stopped.</summary>
    public IReadOnlyList<SubmissionProgress> Progress()
    {
        lock (progress) return progress.Values.OrderBy(entry => entry.SubmissionId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Progress until cancelled. Cancellation detaches every operation at its next await; committed checkpoints
    /// remain and an unresolved dispatch stays unresolved for exact replay by the next process.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var running = new Dictionary<string, Task>();
        using var detach = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            while (true)
            {
                foreach (var ended in running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
                    running.Remove(ended);
                // A failed read says nothing about which work is unfinished, so it starts and forgets nothing.
                if (Discover() is { } unfinished)
                {
                    lock (progress)
                        // Work that has ended, including a stop whose end is retained (a replay block) or work
                        // cancelled elsewhere, leaves; its reason stays readable in the store.
                        foreach (var submissionId in progress.Keys.Where(id => !running.ContainsKey(id) && !unfinished.Contains(id)).ToArray())
                            progress.Remove(submissionId);
                    foreach (var submissionId in unfinished)
                        if (!running.ContainsKey(submissionId) && Due(submissionId))
                            running[submissionId] = Task.Run(() => ProgressAsync(submissionId, detach.Token), CancellationToken.None);
                }
                Interlocked.Increment(ref scans);
                await Task.Delay(Cadence, Clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            // An unexpected discovery failure detaches here too, so no operation outlives this call.
            detach.Cancel();
            await Task.WhenAll(running.Values);
        }
    }

    /// <summary>Unfinished submission IDs, or null when the store is temporarily unreadable.</summary>
    private IReadOnlyList<string>? Discover()
    {
        try
        {
            using var store = application.OpenStore(storePath, directTargetRootCertificate);
            return store.UnfinishedSubmissions();
        }
        catch (Exception failure) when (failure is StoreStateException or SqliteException) { return null; }
    }

    private bool Due(string submissionId)
    {
        lock (progress)
            return !progress.TryGetValue(submissionId, out var entry)
                || entry.State != SubmissionProgress.Stopped && (entry.RetryAt is not { } at || at <= Clock.GetUtcNow());
    }

    private async Task ProgressAsync(string submissionId, CancellationToken cancellationToken)
    {
        SubmissionProgress previous;
        lock (progress)
        {
            previous = progress.GetValueOrDefault(submissionId) ?? new(submissionId, SubmissionProgress.Progressing);
            progress[submissionId] = previous with { State = SubmissionProgress.Progressing, RetryAt = null };
        }
        var stage = SubmissionProgress.Preparation;
        IssueSubmissionPreparation.Failed? failure = null;
        Exception? unexpected = null;
        try
        {
            var current = credentials();
            IssueSubmissionPreparation prepared;
            try { prepared = await preparer.PrepareAsync(submissionId, current.Repository, current.Gateway, cancellationToken); }
            // The preparer's lifetime ends only at shutdown, possibly before this service's token: detach.
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return; }
            if (prepared is IssueSubmissionPreparation.Failed failed) failure = failed;
            // A rejection, refused capture or proposal, or cancellation is retained and ends progression.
            else if (prepared is IssueSubmissionPreparation.Decided { Admission.Decision.Admitted: true })
            {
                stage = SubmissionProgress.Continuation;
                using var store = application.OpenStore(storePath, directTargetRootCertificate);
                // Allocation from retained B1, the retained prepared submission and exact dispatch or replay; a
                // correlated or ended Attempt is handed back without target contact or credentials.
                await new Invocation(store, target).ResumeSubmissionAsync(submissionId, credentials().Dispatch, cancellationToken);
            }
        }
        // Shutdown detaches and reports nothing. Any other cancellation, such as from the credential
        // provider, is unexpected.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (NativeTransportError transport)
        {
            // The frozen request's size never changes; every other transport failure may pass.
            failure = new(submissionId, transport.Code, $"{transport.Message} ({transport.Kind})",
                transport.Kind != "request_too_large");
        }
        catch (Exception error) when (error is BroodlingException or SqliteException)
        {
            failure = IssueSubmissionPreparer.Failure(submissionId, error);
        }
        catch (Exception error)
        {
            failure = new(submissionId, "unexpected_failure", "Progression failed unexpectedly.", false);
            unexpected = error;
        }

        // An end committed meanwhile elsewhere, such as a cancellation or abandonment, stands over the failure it
        // caused. A conflict's retained replay block is this operation's own end and still needs attention.
        if (failure is null || !failure.Retryable && unexpected is null && failure.Code != "submission_conflict"
            && Discover() is { } unfinished && !unfinished.Contains(submissionId))
        {
            lock (progress) progress.Remove(submissionId);
            return;
        }
        // The pause is a wait for release, not a failure, so it never uses up the retry limit. Reaching a new
        // stage starts a new count: a proposal that failed before says nothing about the target.
        var paused = failure.Code == "installation_paused";
        var failures = (previous.Stage == stage ? previous.Failures : 0) + (paused ? 0 : 1);
        var next = !failure.Retryable || failures >= RetryLimit
            ? new SubmissionProgress(submissionId, SubmissionProgress.Stopped, stage, failure.Code, failure.Message,
                failure.Retryable, failures)
            : new SubmissionProgress(submissionId, SubmissionProgress.Waiting, stage, failure.Code, failure.Message, true,
                failures, Clock.GetUtcNow() + (paused ? Cadence : RetryDelay(failures)));
        lock (progress) progress[submissionId] = next;
        if (next.State != SubmissionProgress.Stopped) return;
        try { stopped(next, unexpected); }
        catch (Exception) { } // A failing report must not stop progression of other submissions.
    }

    private static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromTicks(Math.Min(Cadence.Ticks << Math.Min(failures - 1, 30), MaximumRetryDelay.Ticks));
}

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Accepted Issue submissions that ordinary progression can still move: undecided ones with no Contract
    /// or with one bound to their RequestBundle, and admitted ones whose Work Unit has no Attempt or only its
    /// current HTTP Attempt for that Contract, not yet correlated or replay-blocked. Rejected, cancelled,
    /// completed, abandoned and non-current work, Replacement Attempts, earlier unbound associations, and worktree
    /// or bridge records are never selected.
    /// </summary>
    internal IReadOnlyList<string> UnfinishedSubmissions()
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var candidates = new List<(string SubmissionId, string? RevisionId)>();
        // Abandonment and completion both end currentness.
        using (var command = Command("""
            SELECT s.submission_id, s.contract_revision_id FROM issue_submissions AS s
            WHERE s.state IN ('accepted', 'capturing')
                OR (s.state = 'admitted' AND NOT EXISTS (
                    SELECT 1 FROM attempts AS a LEFT JOIN native_submissions AS n USING (attempt_id)
                    WHERE a.work_unit_id = s.work_unit_id
                      AND (a.is_current = 0 OR a.resource_kind <> 'http' OR a.contract_revision_id <> s.contract_revision_id
                          OR n.format <> 'http.v1' OR n.state = 'correlated' OR n.replay_blocked_reason IS NOT NULL)))
            ORDER BY s.rowid
            """, transaction))
        using (var row = command.ExecuteReader())
            while (row.Read()) candidates.Add((row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1)));
        var result = candidates.Where(candidate => candidate.RevisionId is null || BundleBound(candidate.RevisionId, transaction))
            .Select(candidate => candidate.SubmissionId).ToList();
        transaction.Commit();
        return result;
    }

    /// <summary>Whether the revision carries its associated submission's completed RequestBundle, under the one authority rule.</summary>
    private bool BundleBound(string revisionId, SqliteTransaction transaction)
    {
        try { return RequireBundleAuthority(ReadRevision(revisionId, transaction)!, transaction) is not null; }
        catch (IssueSubmissionConflict) { return false; }
    }
}
