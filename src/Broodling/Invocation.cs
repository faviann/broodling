namespace Broodling;

/// <summary>
/// The explicitly configured DirectTarget for a new Attempt. The origin must be canonical HTTPS or HTTP to
/// exactly 127.0.0.1 or [::1], the rule the native target and SDK apply. An existing Attempt continues only
/// through its retained origin.
/// </summary>
public sealed record InvocationTarget
{
    public InvocationTarget(string origin)
    {
        if (DirectTargetExchange.CanonicalOrigin(origin) is null)
            throw new UnsupportedRuntime("The DirectTarget origin must be canonical HTTPS or HTTP to exactly 127.0.0.1 or [::1].");
        Origin = origin;
    }

    public string Origin { get; }
}

/// <summary>One explicit work reference. Ended authority is handed back without automatic replacement.</summary>
public sealed class Invocation(BroodlingStore store, InvocationTarget target)
{
    public Task<AttemptCompletion> WaitAsync(string attemptId, CancellationToken cancellationToken = default) =>
        store.WaitAsync(attemptId, cancellationToken);

    /// <summary>Retained handback needs neither dispatch configuration nor credentials.</summary>
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

    public Task<AdmissionStatus> ResumeAsync(string revisionId, string? repository = null, string revision = "HEAD",
        DispatchCredentials? credentials = null, CancellationToken cancellationToken = default) =>
        ResumeAsync(revisionId, () =>
        {
            if (repository is null) throw new AttemptAdmissionError("A source repository is required before first Attempt allocation.");
            return store.AdmitHttpAttempt(revisionId, repository, revision);
        }, credentials, cancellationToken);

    /// <summary>
    /// Resume one Issue submission's bundle-bound Contract. Its first Attempt starts from the RequestBundle's
    /// retained repository preparation (original B1), which a caller cannot replace; afterwards the retained
    /// Attempt governs, as for <see cref="ResumeAsync(string, string?, string, DispatchCredentials?, CancellationToken)"/>.
    /// </summary>
    public async Task<AdmissionStatus> ResumeSubmissionAsync(string submissionId, DispatchCredentials? credentials = null,
        CancellationToken cancellationToken = default)
    {
        var revisionId = store.GetIssueSubmission(submissionId).ContractRevisionId
            ?? throw new AttemptAdmissionError("The Issue submission has no admitted Contract revision.");
        return await ResumeAsync(revisionId, () => store.AdmitHttpAttempt(submissionId), credentials, cancellationToken);
    }

    private async Task<AdmissionStatus> ResumeAsync(string revisionId, Func<AttemptRecord> allocate,
        DispatchCredentials? credentials, CancellationToken cancellationToken)
    {
        var status = store.Status(revisionId);
        if (status.Decision is null)
        {
            store.Admit(revisionId);
            status = store.Status(revisionId);
        }
        if (CanResumeWithoutDispatch(status)) return status;
        var attempt = status.Attempts.LastOrDefault() ?? allocate();
        // Returns the retained preparation, refusing a different configured origin.
        store.PrepareHttpSubmission(attempt.AttemptId, target.Origin);
        await store.DispatchHttpAsync(attempt.AttemptId, credentials, cancellationToken);
        return store.Status(revisionId);
    }
}
