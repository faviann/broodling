namespace Broodling;

public sealed record ClosabilityFinding(string Code, string Subject, string PreservedObligation, string Detail);
public sealed record DeliveryAuthorization(string Mode, string? TargetBranch = null);

public sealed class ClosabilityAssessment(IReadOnlyList<ClosabilityFinding> findings)
{
    public IReadOnlyList<ClosabilityFinding> Findings { get; } = ContractData.Copy(findings, nameof(findings));
    public bool Admissible => Findings.Count == 0;
    public string Outcome => Admissible ? "admitted" : "rejected";
}

/// <summary>Pure supported-profile admission. Findings preserve rather than waive requirements.</summary>
public static class Closability
{
    public static DeliveryAuthorization AuthorizeDelivery(Contract contract)
    {
        if (contract.RequiredEffects.Count == 0)
            return new("none");
        if (contract.RequiredEffects.Count == 1
            && contract.RequiredEffects[0] is { Kind: "pull_request" } effect
            && !string.IsNullOrWhiteSpace(effect.TargetBranch))
            return new("pull_request", effect.TargetBranch);
        throw new InvalidContractProposal("Contract does not authorize one supported result delivery.");
    }

    public static ClosabilityAssessment Assess(Contract contract, string workUnitHost)
    {
        contract.Validate();
        var findings = new List<ClosabilityFinding>();
        if (contract.SourceAttribution.Count == 0)
            findings.Add(new("no_entitled_source_attribution", "contract", "", "A Contract must attribute entitled source material."));

        DeliveryAuthorization? delivery = null;
        try { delivery = AuthorizeDelivery(contract); }
        catch (InvalidContractProposal)
        {
            foreach (var effect in contract.RequiredEffects)
                findings.Add(new("unsupported_required_effect", "requiredEffect:" + effect.EffectId, effect.Statement,
                    "Only no effect or one pull_request effect naming a target branch is supported; the required effect is preserved."));
        }
        if (delivery?.Mode == "pull_request" && workUnitHost != "github.com")
            findings.Add(new("unsupported_delivery_host", "workUnitHost:" + workUnitHost, contract.RequiredEffects[0].Statement,
                "The pinned native pull-request delivery supports GitHub Work Units only."));

        foreach (var obligation in contract.Obligations)
        {
            if (obligation.Kind is "candidate_change" or "local_validation") continue;
            var external = obligation.Kind is "commit_as_delivery" or "push" or "pull_request" or "merge"
                or "issue_mutation" or "publication" or "deployment" or "external_effect";
            findings.Add(new(external ? "unsupported_external_obligation" : "unrecognized_obligation_kind",
                "obligation:" + obligation.ObligationId, obligation.Statement,
                external ? "This obligation requires external authority the supported profile does not execute."
                    : "An unrecognized obligation cannot be assumed to be local."));
        }
        if (contract.Criteria.Count == 0)
            findings.Add(new("no_criteria", "contract", "", "A Contract needs an acceptance criterion."));
        if (contract.FinalAssuranceMaterials is not null)
            findings.Add(new("unsupported_final_material_selection", "finalAssuranceMaterials", "retain selected final candidate material",
                "The supported result is the native delivery receipt; selected worktree material is not a supported stable result."));
        foreach (var criterion in contract.Criteria)
            foreach (var dependency in criterion.EvidenceEffectDependencies)
                findings.Add(new("effect_dependent_evidence", "criterion:" + criterion.CriterionId, criterion.Statement,
                    $"Evidence depends on effect '{dependency}', which this profile cannot execute or observe as criterion evidence."));
        foreach (var prerequisite in contract.Prerequisites)
            if (!prerequisite.SatisfiedWithinProfile)
                findings.Add(new("unsatisfied_prerequisite", "prerequisite:" + prerequisite.PrerequisiteId, prerequisite.Statement,
                    "The prerequisite is not satisfied within the supported profile; admission hands back without waiting."));
        foreach (var assumption in contract.HostAssumptions)
        {
            if (delivery?.Mode == "pull_request" && assumption is "no_authoritative_effects" or "local_filesystem_only")
                findings.Add(new("unsupported_host_assumption", "hostAssumption:" + assumption, assumption,
                    "The host assumption contradicts the authorized pull-request delivery."));
            else if (assumption is not ("single_host" or "one_attempt_one_dedicated_worktree" or "disposable_worktree"
                or "local_filesystem_only" or "no_authoritative_effects"))
                findings.Add(new("unsupported_host_assumption", "hostAssumption:" + assumption, assumption,
                    "The host assumption is outside the supported single-host, dedicated-worktree profile."));
        }
        return new(findings);
    }
}
