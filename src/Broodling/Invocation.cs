namespace Broodling;

/// <summary>One explicit work reference. No completion, retirement or replacement policy.</summary>
public sealed class Invocation(BroodlingStore store, string workspaceRoot, NativeProfile profile, INativeTransport transport)
{
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
        var decision = status.Decision ?? store.Admit(revisionId);
        if (!decision.Admitted) return store.Status(revisionId);
        var attempt = status.Attempts.LastOrDefault();
        if (attempt?.Abandonment is not null || attempt is { IsCurrent: false }) return status;
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
