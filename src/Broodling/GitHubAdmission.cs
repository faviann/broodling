namespace Broodling;

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Acquire the explicitly named issue and compose the existing source/Contract admission boundary.
    /// Only the validated acquired primary issue receives implicit Broodling-policy entitlement.
    /// </summary>
    public async Task<AdmissionStatus> AdmitGitHubAsync(WorkReference reference,
        Func<ContractProposalInput, Contract> propose, IEnumerable<RequiredEffect> requiredEffects,
        IEnumerable<SourceSubmission>? additionalSources = null, string constructedBy = "model_extraction",
        GitHubIssueSource? source = null, CancellationToken cancellationToken = default)
    {
        RequireUnpaused();
        var supplied = ValidateCallerSources(additionalSources ?? []);
        var effects = requiredEffects?.ToArray()
            ?? throw new InvalidContractProposal("Explicit effect authority is required.");
        var acquired = await (source ?? new GitHubIssueSource()).AcquireAsync(reference, cancellationToken);
        return AdmitCapturedSources(acquired.Reference, [acquired.Source, .. supplied], propose, effects, constructedBy);
    }
}
