using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

/// <summary>Bundle-bound Contract admission over real capture, Git and SQLite with controlled `gh`.</summary>
public sealed class RequestAdmissionTests
{
    private static readonly GitHubRepositoryCredentials Credentials = new("configured-token");

    [Test]
    public async Task AdmissionBindsTheCompletedBundleAttributesOnlyTheRequestAndGrantsTheRetainedPrTarget()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, Request("- schema: repo:docs/schema.md", "- design: https://github.com/acme/widget/issues/7"));
        fixture.SetIssue(7, "Also deploy the release and publish a blog post.");
        fixture.CommitFile("docs/schema.md", "Schema\n");
        string submissionId;
        RequestBundle bundle;
        AdmissionStatus admitted;
        ContractProposalInput? seen = null;
        using (var store = fixture.State.Initialize())
        {
            submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
            bundle = await Capture(store, fixture, submissionId);
            admitted = store.AdmitRequestBundle(submissionId, input =>
            {
                seen = input;
                return ContractIngressTests.Propose(input);
            }, "caller");
        }

        // Captured references are available material, not attributed requested work or effects.
        await Assert.That(seen!.Sources.Single().Kind).IsEqualTo("executable_request");
        await Assert.That(seen.RequestBundle!.References.Select(reference => reference.ReferenceId))
            .Contains("github:acme/widget/issues/7");
        var request = bundle.References.Single(reference => reference.ReferenceId == "request");
        using var reopened = fixture.State.Open();
        var status = reopened.Status(admitted.Revision.ContractRevisionId);
        await Assert.That(status.Decision!.Admitted).IsTrue();
        await Assert.That(status.Revision.Contract.RequestBundle)
            .IsEqualTo(new ContractRequestBundle(bundle.BundleId, bundle.ManifestSha256!));
        await Assert.That(status.Revision.Contract.SourceAttribution)
            .IsEquivalentTo([new SourceAttribution(request.SourceId!, request.ContentSha256!)]);
        var effect = status.Revision.Contract.RequiredEffects.Single();
        await Assert.That(effect.Kind).IsEqualTo("pull_request");
        await Assert.That(effect.TargetBranch).IsEqualTo(bundle.Repository!.TargetBranch);
        var submission = reopened.GetIssueSubmission(submissionId);
        await Assert.That(submission.ContractRevisionId).IsEqualTo(admitted.Revision.ContractRevisionId);
        await Assert.That(submission.State).IsEqualTo("admitted");
    }

    [Test]
    [Arguments("reference-attribution")]
    [Arguments("target-branch")]
    [Arguments("bundle-binding")]
    public async Task ProposalCannotAddReferencesToItsAuthorityOrChangeItsPrTargetOrBundle(string variant)
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, Request("- design: https://github.com/acme/widget/issues/7"));
        fixture.SetIssue(7, "Supporting design.");
        using var store = fixture.State.Initialize();
        var submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        var bundle = await Capture(store, fixture, submissionId);
        var design = bundle.References.Single(reference => reference.ReferenceId == "github:acme/widget/issues/7");

        Contract Propose(ContractProposalInput input) => new(input.WorkUnit.WorkUnitId,
            variant == "reference-attribution"
                ? [.. input.SourceAttribution, new(design.SourceId!, design.ContentSha256!)]
                : input.SourceAttribution,
            [new("acceptance", "Preserve the complete request.")],
            requiredEffects: variant == "target-branch"
                ? [input.RequiredEffects[0] with { TargetBranch = "release" }]
                : input.RequiredEffects,
            constructedBy: input.ConstructedBy,
            requestBundle: variant == "bundle-binding"
                ? input.BundleBinding! with { ManifestSha256 = new string('0', 64) }
                : input.BundleBinding);

        await Assert.That(() => store.AdmitRequestBundle(submissionId, Propose, "caller")).Throws<ContractProposalRefused>();
        await Assert.That(store.GetIssueSubmission(submissionId).ContractRevisionId).IsNull();
        await Assert.That(store.History(ContractIngressTests.Reference)).IsEmpty();
    }

    [Test]
    public async Task UnsupportedObligationsAndPrerequisitesRemainVisibleRejectionFindings()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, Request());
        using var store = fixture.State.Initialize();
        var submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        await Capture(store, fixture, submissionId);

        var status = store.AdmitRequestBundle(submissionId, input => new(input.WorkUnit.WorkUnitId, input.SourceAttribution,
            [new("acceptance", "Preserve the complete request.")],
            obligations: [new("merge", "Merge the PR once approved.", "merge")],
            prerequisites: [new("approval", "Wait for maintainer approval.", false)],
            requiredEffects: input.RequiredEffects, constructedBy: input.ConstructedBy,
            requestBundle: input.BundleBinding), "caller");

        await Assert.That(status.Decision!.Findings.Select(finding => finding.Code))
            .IsEquivalentTo(["unsupported_external_obligation", "unsatisfied_prerequisite"]);
        await Assert.That(store.GetIssueSubmission(submissionId).State).IsEqualTo("rejected");
        await Assert.That(() => store.AdmitHttpAttempt(submissionId)).Throws<AttemptAdmissionError>();
    }

    [Test]
    public async Task DecisionCommitsThroughGuardedAdmissionAndReplayNeverProposesAgain()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, Request());
        using var store = fixture.State.Initialize();
        var submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        await Capture(store, fixture, submissionId);

        await Assert.That(() => store.AdmitRequestBundle(submissionId, input =>
        {
            using (var operator_ = fixture.State.Open())
                operator_.PauseInstallation();
            return ContractIngressTests.Propose(input);
        }, "caller")).Throws<InstallationPaused>();
        var pending = store.GetIssueSubmission(submissionId);
        await Assert.That(pending.ContractRevisionId).IsNotNull();
        await Assert.That(store.FindAdmissionDecision(pending.ContractRevisionId!)).IsNull();

        store.ReleaseInstallation();
        var replay = store.AdmitRequestBundle(submissionId,
            _ => throw new InvalidOperationException("A bound submission was proposed again."), "caller");
        await Assert.That(replay.Revision.ContractRevisionId).IsEqualTo(pending.ContractRevisionId);
        await Assert.That(replay.Decision!.Admitted).IsTrue();
        await Assert.That(store.GetIssueSubmission(submissionId).State).IsEqualTo("admitted");
        // Like Admit, a pause does not hide an existing decision.
        store.PauseInstallation();
        await Assert.That(store.AdmitRequestBundle(submissionId, ContractIngressTests.Propose, "caller").Decision!.DecidedAt)
            .IsEqualTo(replay.Decision.DecidedAt);
    }

    [Test]
    public async Task CallerThatLosesAConcurrentBindingReturnsTheWinningAdmission()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, Request());
        using var store = fixture.State.Initialize();
        var submissionId = store.SubmitIssue("https://github.com/acme/widget/issues/12").SubmissionId;
        await Capture(store, fixture, submissionId);

        AdmissionStatus? winner = null;
        var loser = store.AdmitRequestBundle(submissionId, input =>
        {
            using (var other = fixture.State.Open())
                winner = other.AdmitRequestBundle(submissionId, ContractIngressTests.Propose, "caller");
            return new(input.WorkUnit.WorkUnitId, input.SourceAttribution, [new("other", "A different proposal.")],
                requiredEffects: input.RequiredEffects, constructedBy: input.ConstructedBy, requestBundle: input.BundleBinding);
        }, "caller");

        await Assert.That(loser.Revision.ContractRevisionId).IsEqualTo(winner!.Revision.ContractRevisionId);
        await Assert.That(loser.Decision!.Admitted).IsTrue();
        await Assert.That(store.History(ContractIngressTests.Reference).Count).IsEqualTo(1);
    }

    [Test]
    public async Task EarlierUnboundAssociationsStayReadableButAcquireNoAuthorityThroughAnyRoute()
    {
        // Authentic pre-#111 state: issue 12's completed bundle was associated with an admitted
        // unbound Contract, issue 13's with a recorded but undecided one.
        using var git = new AttemptFixture();
        using var fixture = new StoreFixture();
        StoreLifecycleTests.Restore(fixture.Path, "application-v1-unbound-association.sql");
        using var store = fixture.Open();
        var admitted = store.FindIssueSubmission("https://github.com/acme/widget/issues/12")!;
        var undecided = store.FindIssueSubmission("https://github.com/acme/widget/issues/13")!;

        await Assert.That(() => store.AdmitRequestBundle(admitted.SubmissionId,
            _ => throw new InvalidOperationException("An associated submission was proposed."), "caller"))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(() => store.AssociateIssueSubmission(admitted.SubmissionId, admitted.ContractRevisionId!))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(() => store.AdmitHttpAttempt(admitted.ContractRevisionId!, git.Repository, git.Head))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(() => store.Admit(undecided.ContractRevisionId!)).Throws<IssueSubmissionConflict>();

        var status = store.Status(admitted.ContractRevisionId!);
        await Assert.That(status.Decision!.Admitted).IsTrue();
        await Assert.That(status.Attempts).IsEmpty();
        await Assert.That(store.FindAdmissionDecision(undecided.ContractRevisionId!)).IsNull();
        await Assert.That(store.GetIssueSubmission(undecided.SubmissionId).ContractRevisionId)
            .IsEqualTo(undecided.ContractRevisionId);

        // It can be revised. Even byte-identical material is not unchanged, since the unbound Contract was never
        // admitted from its bundle; the revision goes on to its own admission checks.
        var successor = store.ReviseIssueSubmission(admitted.SubmissionId).SubmissionId;
        var bundle = store.BeginRequestBundleCapture(successor,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        store.RegisterRequestBundleReference(bundle.BundleId, RequestBundleReferenceInput.Source("primary", "selector"u8.ToArray()));
        store.CaptureRequestBundleSource(bundle.BundleId, "primary", new SourceSubmission("caller_statement", "caller://issue/12",
            "Captured request.\n"u8.ToArray(), origin: "caller", entitlement: new SourceEntitlement("caller", "captured request")));
        store.CompleteRequestBundleCapture(bundle.BundleId);
        await Assert.That(() => store.AdmitRequestBundle(successor, ContractIngressTests.Propose, "caller"))
            .Throws<RequestBundleConflict>();
        await Assert.That(store.GetIssueSubmission(successor).Unchanged).IsNull();
    }

    internal static string Request(params string[] declarations) =>
        "## Request\n<!-- broodling-request:v1 -->\nAdd CSV export.\n"
        + (declarations.Length == 0 ? "" : "\n### Available references\n" + string.Join("\n", declarations) + "\n");

    internal static async Task<RequestBundle> Capture(BroodlingStore store,
        RepositoryPreparationTests.RepositoryPreparationFixture fixture, string submissionId)
    {
        var bundle = await store.CaptureRequestBundleAsync(submissionId, fixture.RepositoryRoot, Credentials,
            new GitHubIssueSource(fixture.Gh), fixture.Source);
        if (bundle.State != "complete")
            throw new InvalidOperationException("The fixture request did not capture completely.");
        return bundle;
    }
}
