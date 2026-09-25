namespace Broodling;

/// <summary>
/// The explicitly configured execution target for a new Attempt. An existing Attempt is continued
/// only through the target kind its retained resources name; nothing falls back to the other kind.
/// </summary>
public abstract record InvocationTarget
{
    private InvocationTarget() { }

    /// <summary>No-effect work in an owned local worktree, submitted through the pinned SDK bridge and launcher.</summary>
    public sealed record Local(string WorkspaceRoot, NativeProfile Profile, INativeTransport Transport) : InvocationTarget;

    /// <summary>Authorized PR work over HTTP/OECP; no Python, SDK client state, workspace root or launcher.</summary>
    public sealed record Direct(string Origin) : InvocationTarget;
}

/// <summary>One explicit work reference. Ended authority is handed back without automatic replacement.</summary>
public sealed class Invocation(BroodlingStore store, InvocationTarget target)
{
    /// <summary>The store routes on the retained record; an HTTP result needs no bridge transport.</summary>
    public Task<AttemptCompletion> WaitAsync(string attemptId, CancellationToken cancellationToken = default) =>
        store.WaitAsync(attemptId, (target as InvocationTarget.Local)?.Transport, cancellationToken);

    /// <summary>Retained handback needs neither dispatch configuration nor the old workspace.</summary>
    public static bool CanResumeWithoutDispatch(AdmissionStatus status)
    {
        var attempt = status.Attempts.LastOrDefault();
        return status.Decision is { Admitted: false } || attempt is { IsCurrent: false }
            || status.Submissions.Any(submission => submission.AttemptId == attempt?.AttemptId && submission.State == "correlated");
    }

    public async Task<AdmissionStatus> SubmitAsync(WorkReference reference, Func<ContractProposalInput, Contract> propose,
        IEnumerable<RequiredEffect> requiredEffects, string repository, string revision = "HEAD",
        IEnumerable<SourceSubmission>? additionalSources = null, string constructedBy = "model_extraction",
        DispatchCredentials? credentials = null, GitHubIssueSource? source = null, CancellationToken cancellationToken = default)
    {
        var admitted = await store.AdmitGitHubAsync(reference, propose, requiredEffects, additionalSources, constructedBy, source, cancellationToken);
        return await ResumeAsync(admitted.Revision.ContractRevisionId, repository, revision, credentials, cancellationToken);
    }

    public async Task<AdmissionStatus> ResumeAsync(string revisionId, string? repository = null, string revision = "HEAD",
        DispatchCredentials? credentials = null, CancellationToken cancellationToken = default)
    {
        var status = store.Status(revisionId);
        if (status.Decision is null)
        {
            store.Admit(revisionId);
            status = store.Status(revisionId);
        }
        if (CanResumeWithoutDispatch(status)) return status;
        var attempt = status.Attempts.LastOrDefault();
        if (attempt is null)
        {
            if (repository is null) throw new AttemptAdmissionError("A source repository is required before first Attempt allocation.");
            attempt = target switch
            {
                InvocationTarget.Direct => store.AdmitHttpAttempt(revisionId, repository, revision),
                InvocationTarget.Local local => store.AdmitAttempt(revisionId, repository, local.WorkspaceRoot, revision),
                _ => throw new UnsupportedRuntime("The invocation target is unsupported.")
            };
        }
        switch (target)
        {
            case InvocationTarget.Direct direct when attempt.ResourceKind == AttemptRecord.Http:
                // Returns the retained preparation, refusing a different configured origin.
                store.PrepareHttpSubmission(attempt.AttemptId, direct.Origin);
                await store.DispatchHttpAsync(attempt.AttemptId, credentials, cancellationToken);
                break;
            case InvocationTarget.Local local when attempt.ResourceKind == AttemptRecord.Worktree:
                // No reacquisition, B1 selection or materialization once dispatch may have happened.
                if (store.FindSubmission(attempt.AttemptId) is null) store.ProvisionAttempt(attempt.AttemptId);
                await store.DispatchAsync(attempt.AttemptId, local.Profile, local.Transport, cancellationToken);
                break;
            default:
                throw new UnsupportedRuntime("The configured target kind differs from the retained Attempt's resource kind.");
        }
        return store.Status(revisionId);
    }
}
