using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class ContractPolicyTests
{
    [Test]
    public async Task OptionalGuidanceIsPreservedWithoutRequiringOrInterpretingAProofPlan()
    {
        using var fixture = new StoreFixture();
        AdmissionStatus status;
        var members = new[] { "second", "first" };
        var argv = new[] { "python3", "check.py", "a b" };
        using (var store = fixture.Initialize())
        {
            status = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], input => new(
                input.WorkUnit.WorkUnitId, input.SourceAttribution,
                [new("c", "  Preserve the outcome verbatim.\n", new("unbounded", members, "  surface\n"),
                    "  seam  ", "arbitrary command\n", "  counterexample\n", mechanicalEvidence: new(argv, "nested", ["file"]))],
                obligations: [new("change", "Change the candidate.", "candidate_change"), new("check", "Check locally.", "local_validation")],
                prerequisites: [new("ready", "Already available.", true)],
                hostAssumptions: ["single_host", "one_attempt_one_dedicated_worktree", "disposable_worktree", "local_filesystem_only", "no_authoritative_effects"],
                constructedBy: input.ConstructedBy, notes: "caller notes ✓"), []);
            members[0] = "mutated";
            argv[0] = "mutated";
            await Assert.That(status.Decision!.Admitted).IsTrue();
        }
        using var reopened = fixture.Open();
        var restored = reopened.GetContractRevision(status.Revision.ContractRevisionId);
        var criterion = restored.Contract.Criteria.Single();
        await Assert.That(criterion.Statement).IsEqualTo("  Preserve the outcome verbatim.\n");
        await Assert.That(criterion.EvidencePopulation!.Members.SequenceEqual(new[] { "second", "first" })).IsTrue();
        await Assert.That(criterion.EvidencePopulation.Surface).IsEqualTo("  surface\n");
        await Assert.That(criterion.ValidationSeam).IsEqualTo("  seam  ");
        await Assert.That(criterion.ValidationAction).IsEqualTo("arbitrary command\n");
        await Assert.That(criterion.FalsifyingObservation).IsEqualTo("  counterexample\n");
        await Assert.That(criterion.MechanicalEvidence!.Argv.SequenceEqual(new[] { "python3", "check.py", "a b" })).IsTrue();
        await Assert.That(criterion.MechanicalEvidence.Cwd).IsEqualTo("nested");
        await Assert.That(criterion.MechanicalEvidence.Materials.Single()).IsEqualTo("file");
        await Assert.That(restored.CanonicalBytes.SequenceEqual(status.Revision.CanonicalBytes)).IsTrue();
        await Assert.That(restored.Contract.Notes).IsEqualTo("caller notes ✓");
    }

    [Test]
    public async Task UnboundContractRepresentationKeepsItsExactRetainedBytesAndIdentity()
    {
        // Canonical bytes of a Contract recorded without a RequestBundle binding. Re-serializing
        // them must not add a field, or every retained revision would fail its canonical check.
        var legacy = """
            {"workUnitId":"wu-legacy","sourceAttribution":[{"sourceId":"src-1","contentSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"criteria":[{"criterionId":"acceptance","statement":"Preserve the complete request.","evidencePopulation":null,"validationSeam":"","validationAction":"","falsifyingObservation":"","evidenceEffectDependencies":[],"mechanicalEvidence":null}],"obligations":[],"prerequisites":[],"requiredEffects":[{"effectId":"pr","statement":"Open PR","kind":"pull_request","targetBranch":"main"}],"hostAssumptions":[],"constructedBy":"caller","notes":"","finalAssuranceMaterials":null}
            """u8.ToArray();

        var contract = Contract.FromCanonicalBytes(legacy);

        await Assert.That(contract.CanonicalBytes().SequenceEqual(legacy)).IsTrue();
        await Assert.That(contract.ContractRevisionId)
            .IsEqualTo("cr-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(legacy)));
    }

    [Test]
    public async Task RejectionsPreserveEveryUnsupportedObligationDependencyPrerequisiteAndAssumption()
    {
        using var fixture = new StoreFixture();
        AdmissionStatus status;
        using (var store = fixture.Initialize())
        {
            status = store.AdmitSources(ContractIngressTests.Reference, [ContractIngressTests.Primary()], input => new(
                input.WorkUnit.WorkUnitId, input.SourceAttribution,
                [new("evidence", "Verify the live service after publication.", evidenceEffectDependencies: ["deployment", "unknown-effect"])],
                obligations: [new("publish", "Publish the signed release.\n", "publication"), new("unknown", "Keep this unclassified requirement.", "novel")],
                prerequisites: [new("waiting", "Wait for external approval.", false)],
                requiredEffects: input.RequiredEffects, hostAssumptions: ["fleet_of_hosts"], constructedBy: input.ConstructedBy,
                finalAssuranceMaterials: []), [new("deploy", "Deploy this exact release.", "deployment")]);
        }
        using var reopened = fixture.Open();
        var restored = reopened.Status(status.Revision.ContractRevisionId);
        await Assert.That(restored.Decision!.Admitted).IsFalse();
        await Assert.That(restored.Decision.Findings.Select(finding => finding.Code).SequenceEqual(new[]
        {
            "unsupported_required_effect", "unsupported_external_obligation", "unrecognized_obligation_kind",
            "unsupported_final_material_selection", "effect_dependent_evidence", "effect_dependent_evidence",
            "unsatisfied_prerequisite", "unsupported_host_assumption"
        })).IsTrue();
        await Assert.That(restored.Decision.Findings.Single(finding => finding.Code == "unsupported_external_obligation").PreservedObligation)
            .IsEqualTo("Publish the signed release.\n");
        await Assert.That(restored.Decision.Findings.Single(finding => finding.Code == "unrecognized_obligation_kind").PreservedObligation)
            .IsEqualTo("Keep this unclassified requirement.");
        await Assert.That(restored.Decision.Findings.Where(finding => finding.Code == "effect_dependent_evidence")
            .All(finding => finding.PreservedObligation == "Verify the live service after publication.")).IsTrue();
        await Assert.That(restored.Decision.Findings.Single(finding => finding.Code == "unsatisfied_prerequisite").PreservedObligation)
            .IsEqualTo("Wait for external approval.");
        await Assert.That(restored.Decision.Findings.Single(finding => finding.Code == "unsupported_host_assumption").PreservedObligation)
            .IsEqualTo("fleet_of_hosts");
        await Assert.That(restored.Revision.Contract.FinalAssuranceMaterials!.Count).IsEqualTo(0);
        await Assert.That(reopened.Admit(restored.Revision.ContractRevisionId).DecidedAt).IsEqualTo(restored.Decision.DecidedAt);
        await Assert.That(reopened.IsAdmitted(restored.Revision.ContractRevisionId)).IsFalse();
    }

    [Test]
    [Arguments("none", true)]
    [Arguments("pr", true)]
    [Arguments("missing-target", false)]
    [Arguments("mixed", false)]
    [Arguments("multiple-pr", false)]
    [Arguments("foreign-host", false)]
    [Arguments("contradiction", false)]
    [Arguments("pr-evidence", false)]
    [Arguments("pr-obligation", false)]
    [Arguments("selected-material", false)]
    public async Task DeliveryAdmissionKeepsTheCurrentSupportedProfile(string profile, bool admitted)
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var reference = profile == "foreign-host" ? WorkReference.Parse("gitlab.com/acme/widget", 12) : ContractIngressTests.Reference;
        var effect = new RequiredEffect("pr", "Open a PR targeting main.", "pull_request", profile == "missing-target" ? " " : "main");
        RequiredEffect[] effects = profile switch
        {
            "none" => [],
            "mixed" => [effect, new("deploy", "Deploy.", "deployment")],
            "multiple-pr" => [effect, effect with { EffectId = "another" }],
            _ => [effect]
        };
        var status = store.AdmitSources(reference,
            [new("primary_issue", reference.IssueLocator, [], entitlement: new("caller", "explicit"))],
            input => new(input.WorkUnit.WorkUnitId, input.SourceAttribution,
                [new("c", "Outcome.", evidenceEffectDependencies: profile == "pr-evidence" ? ["pr"] : [])],
                obligations: profile == "pr-obligation" ? [new("o", "Open the PR.", "pull_request")] : [],
                requiredEffects: input.RequiredEffects,
                hostAssumptions: profile == "contradiction" ? ["local_filesystem_only", "no_authoritative_effects"] : [],
                finalAssuranceMaterials: profile == "selected-material" ? [new("result.txt")] : null,
                constructedBy: input.ConstructedBy), effects);
        await Assert.That(status.Decision!.Admitted).IsEqualTo(admitted);
        if (profile == "contradiction")
            await Assert.That(status.Decision.Findings.Count).IsEqualTo(2);
        if (profile is "mixed" or "multiple-pr")
            await Assert.That(status.Decision.Findings.Count(finding => finding.Code == "unsupported_required_effect")).IsEqualTo(2);
    }

    [Test]
    public async Task MissingCriteriaAndSourcesAreDurableRefusalsAndMeaningChangesAppendOnly()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var work = store.ResolveWorkUnit(ContractIngressTests.Reference);
        var empty = new Contract(work.WorkUnitId, [], []);
        var first = store.RecordContractRevision(empty);
        var decision = store.Admit(first.ContractRevisionId);
        await Assert.That(decision.Findings.Select(finding => finding.Code).SequenceEqual(new[] { "no_entitled_source_attribution", "no_criteria" })).IsTrue();
        var source = store.EntitleSource(work.WorkUnitId, ContractIngressTests.Primary());
        SourceAttribution[] pins = [new(source.SourceId, source.ContentSha256)];
        var second = store.RecordContractRevision(new(work.WorkUnitId, pins, [new("a", "First."), new("b", "Second.")]));
        var third = store.RecordContractRevision(new(work.WorkUnitId, pins, [new("b", "Second."), new("a", "First.")]));
        var fourth = store.RecordContractRevision(new(work.WorkUnitId, pins, [new("b", "Second."), new("a", "First.")], notes: "New meaning."));
        await Assert.That(second.SupersedesRevisionId).IsEqualTo(first.ContractRevisionId);
        await Assert.That(third.SupersedesRevisionId).IsEqualTo(second.ContractRevisionId);
        await Assert.That(fourth.SupersedesRevisionId).IsEqualTo(third.ContractRevisionId);
        await Assert.That(store.RecordContractRevision(empty).ContractRevisionId).IsEqualTo(first.ContractRevisionId);
        await Assert.That(store.History(ContractIngressTests.Reference).Count).IsEqualTo(4);
    }
}
