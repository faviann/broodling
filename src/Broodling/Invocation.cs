namespace Broodling;

/// <summary>One explicit work reference. No retirement or replacement policy.</summary>
public sealed class Invocation(BroodlingStore store, string workspaceRoot, NativeProfile profile, INativeTransport transport)
{
    public Task<AttemptCompletion> WaitAsync(string attemptId, CancellationToken cancellationToken = default) =>
        store.WaitAsync(attemptId, transport, cancellationToken);

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
            attempt = store.AdmitAttempt(revisionId, repository, workspaceRoot, revision);
        }
        var submission = store.FindSubmission(attempt.AttemptId);
        // No reacquisition, B1 selection or materialization once dispatch may have happened.
        if (submission is null) store.ProvisionAttempt(attempt.AttemptId);
        if (submission?.State != "correlated")
            await store.DispatchAsync(attempt.AttemptId, profile, transport, credentials, cancellationToken);
        return store.Status(revisionId);
    }
}
