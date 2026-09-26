using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using ControlledGateway = Broodling.Tests.BundledProposerTests.ControlledGateway;
using Gateway = Broodling.Tests.IssueSubmissionPreparationTests.Gateway;
using PreparationFixture = Broodling.Tests.RepositoryPreparationTests.RepositoryPreparationFixture;

namespace Broodling.Tests;

/// <summary>
/// Predecessor-linked revisions over real capture, Git and SQLite, with controlled GitHub and gateway peers and the
/// loopback stock-target stand-in, driven through the operations the progressor composes. Capture, proposal,
/// dispatch, completion, stop and maintenance retirement belong to their owners' tests; these cover the revision
/// transition, its eligibility, the unchanged-input end and history across revisions.
/// </summary>
public sealed class RevisionTests
{
    private const string Issue = "https://github.com/acme/widget/issues/12";
    private const string Proposal = """{"criteria": [{"criterionId": "export", "statement": "Export the data."}]}""";
    private static readonly string Revised = "## Request\n<!-- broodling-request:v1 -->\nAdd CSV and JSON export.\n";

    [Test]
    public async Task OneSuccessorIsCreatedFromTheLatestEndedSubmissionAndReplayFindsItAfterLaterHistory()
    {
        await using var revisions = new Revisions();
        var store = revisions.Store;
        var first = store.SubmitIssue(Issue).SubmissionId;
        // Work still being prepared is active.
        await Refused(store, first, "being prepared");
        revisions.Proposal = "[]";
        await Assert.That(await revisions.Prepare(first)).IsTypeOf<IssueSubmissionPreparation.ProposalRefused>();

        // Concurrent requests from separate sessions converge on one successor.
        var requested = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var session = revisions.Fixture.State.Open();
            return session.ReviseIssueSubmission(first).SubmissionId;
        })));
        await Assert.That(requested.Distinct().Count()).IsEqualTo(1);
        var second = store.GetIssueSubmission(requested[0]);
        await Assert.That(second.PredecessorSubmissionId).IsEqualTo(first);
        await Assert.That(second.Sequence).IsEqualTo(2);
        await Assert.That(second.State).IsEqualTo("accepted");
        await Assert.That(store.GetIssueSubmission(first).SuccessorSubmissionId).IsEqualTo(second.SubmissionId);
        await Assert.That(store.GetIssueSubmission(first).State).IsEqualTo("rejected");

        // The same material never had a committed Contract, so it seeks admission again.
        revisions.Proposal = Proposal;
        var decided = (IssueSubmissionPreparation.Decided)await revisions.Prepare(second.SubmissionId);
        await Assert.That(decided.Admission.Decision!.Admitted).IsTrue();
        await Assert.That(revisions.Gateway.Contexts.Count).IsEqualTo(2);
        // Admitted work that has not started executing is still active.
        await Refused(store, second.SubmissionId, "being prepared");
        await store.CancelIssueSubmissionAsync(second.SubmissionId, "Revise the request instead.");
        var third = store.ReviseIssueSubmission(second.SubmissionId);

        // Replaying the first request returns its exact successor although later history exists; ordinary
        // submission returns the latest and creates nothing.
        await Assert.That(store.ReviseIssueSubmission(first).SubmissionId).IsEqualTo(second.SubmissionId);
        await Assert.That(store.SubmitIssue(Issue).SubmissionId).IsEqualTo(third.SubmissionId);
        await Assert.That(store.IssueHistory(Issue).Select(submission => submission.SubmissionId).ToArray())
            .IsEquivalentTo([first, second.SubmissionId, third.SubmissionId]);
    }

    [Test]
    public async Task DispatchedWorkNeedsMaintenanceRetirementAndItsStopReplayCannotReachTheSuccessor()
    {
        await using var revisions = new Revisions();
        var store = revisions.Store;
        var first = store.SubmitIssue(Issue).SubmissionId;
        await revisions.Prepare(first);
        await revisions.Continue(first);
        var dispatched = store.GetIssueSubmission(first).AttemptIds.Single();
        // A current Attempt is unresolved execution.
        await Refused(store, first, "current Attempt");
        revisions.Finish(dispatched, "failed", "force_stopped");
        await Assert.That(async () => await store.CancelIssueSubmissionAsync(first, "Revise the request."))
            .Throws<CessationUnconfirmed>();
        // Abandoned, but dispatched: execution may continue until verified maintenance retires it.
        await Refused(store, first, "verified maintenance");
        revisions.Retire(dispatched);

        revisions.Fixture.SetIssue(12, Revised);
        var second = store.ReviseIssueSubmission(first).SubmissionId;
        // The revision supersedes the predecessor's Contract, whose retired Attempt is then never replaced.
        store.PauseInstallation();
        await Assert.That(() => store.PrepareRetry(dispatched, "late")).Throws<AttemptConflict>();
        store.ReleaseInstallation();
        await Assert.That(store.UnfinishedSubmissions()).IsEquivalentTo([second]);
        await revisions.Prepare(second);
        // Progression discovers the admitted successor and gives it an ordinary first Attempt from its own B1.
        await Assert.That(store.UnfinishedSubmissions()).IsEquivalentTo([second]);
        await revisions.Continue(second);
        var successor = store.RequireCurrentAttempt(store.GetIssueSubmission(second).AttemptIds.Single());
        await Assert.That(successor.Retry).IsNull();
        await Assert.That(successor.ContractRevisionId).IsNotEqualTo(store.GetAttempt(dispatched).ContractRevisionId);
        await Assert.That(store.FindSubmission(successor.AttemptId)!.State).IsEqualTo("correlated");

        // Replaying the predecessor's stops reaches only its own retired Attempt, without native contact.
        var messages = revisions.Target.Messages.Count;
        var replayed = await store.CancelIssueSubmissionAsync(first, "Revise the request.");
        await Assert.That(replayed.Cancellation!.AttemptId).IsEqualTo(dispatched);
        await Assert.That((await store.StopAsync(dispatched, "again", null)).Basis).IsEqualTo("stopped_target");
        await Assert.That(store.RequireCurrentAttempt(successor.AttemptId).Abandonment).IsNull();
        await Assert.That(revisions.Target.Messages.Count).IsEqualTo(messages);
    }

    [Test]
    public async Task UnchangedInputsEndWithARetainedLinkToTheAdmittedAuthorityWithoutProposing()
    {
        await using var revisions = new Revisions();
        var store = revisions.Store;
        var first = store.SubmitIssue(Issue).SubmissionId;
        await revisions.Prepare(first);
        var admitted = store.GetIssueSubmission(first);
        store.AbandonAttempt(store.AdmitHttpAttempt(first).AttemptId, "Never dispatched.");
        // An intermediate revision is refused before any Contract; the next one restores the admitted inputs.
        revisions.Fixture.SetIssue(12, "## Request\nNo marked request.\n");
        var refused = store.ReviseIssueSubmission(first).SubmissionId;
        await Assert.That(await revisions.Prepare(refused)).IsTypeOf<IssueSubmissionPreparation.CaptureRefused>();
        revisions.Fixture.SetIssue(12, RequestAdmissionTests.Request());
        var second = store.ReviseIssueSubmission(refused).SubmissionId;

        var ended = (IssueSubmissionPreparation.Unchanged)await revisions.Prepare(second);
        var unchanged = store.GetIssueSubmission(second);
        await Assert.That(ended.Explanation).IsEqualTo(unchanged.Unchanged);
        await Assert.That(unchanged.State).IsEqualTo("unchanged");
        await Assert.That(unchanged.ContractRevisionId).IsNull();
        await Assert.That(unchanged.Unchanged!.AdmittedSubmissionId).IsEqualTo(first);
        await Assert.That(unchanged.Unchanged.ContractRevisionId).IsEqualTo(admitted.ContractRevisionId);
        await Assert.That(unchanged.Unchanged.Explanation).Contains("explicit replacement operation");
        await Assert.That(store.GetRequestBundle(second).BundleId).IsNotEqualTo(store.GetRequestBundle(first).BundleId);
        // Neither proposed nor executed, then or later.
        var reads = revisions.Fixture.ReadGhPaths().Length;
        await Assert.That(await revisions.Prepare(second)).IsTypeOf<IssueSubmissionPreparation.Unchanged>();
        await Assert.That(() => store.AdmitRequestBundle(second, ContractIngressTests.Propose, "caller"))
            .Throws<SubmissionInputsUnchanged>();
        await Assert.That(revisions.Gateway.Contexts.Count).IsEqualTo(1);
        await Assert.That(revisions.Fixture.ReadGhPaths().Length).IsEqualTo(reads);
        await Assert.That(store.UnfinishedSubmissions()).IsEmpty();
        // As its explanation says, the linked authority can still execute again through explicit replacement:
        // only the latest submission decides supersession, not the refused one before it.
        var original = store.GetAttempt(store.GetIssueSubmission(first).AttemptIds.Single());
        await ReplacementTests.SafeRetire(store, original);
        store.AbandonAttempt(store.AdmitRetry(original.AttemptId, "rerun").AttemptId, "Revise instead.");

        // A moved starting commit is changed authority, even with the same request text.
        revisions.Fixture.AdvanceMainAndAddDevelop();
        var third = store.ReviseIssueSubmission(second).SubmissionId;
        await Assert.That(await revisions.Prepare(third)).IsTypeOf<IssueSubmissionPreparation.Decided>();
        await Assert.That(revisions.Gateway.Contexts.Count).IsEqualTo(2);
        await Assert.That(store.GetRequestBundle(third).Repository!.StartingCommit).IsEqualTo(revisions.Fixture.AdvancedCommit);
    }

    [Test]
    public async Task TwoRevisionsRetainEachResultAndTheLaterRevisionCanStillBeReplaced()
    {
        await using var revisions = new Revisions();
        var store = revisions.Store;
        var first = store.SubmitIssue(Issue).SubmissionId;
        await revisions.Prepare(first);
        await revisions.Continue(first);
        var earlier = store.GetIssueSubmission(first).AttemptIds.Single();
        var accepted = await revisions.Succeed(earlier, "first result");
        // Success still leaves dispatched execution to verified maintenance.
        await Refused(store, first, "verified maintenance");
        revisions.Retire(earlier);

        revisions.Fixture.SetIssue(12, Revised);
        var second = store.ReviseIssueSubmission(first).SubmissionId;
        await revisions.Prepare(second);
        await revisions.Continue(second);
        var failed = store.GetIssueSubmission(second).AttemptIds.Single();
        revisions.Finish(failed, "failed", "runtime_failed");
        await Assert.That(async () => await store.WaitAsync(failed, null)).Throws<SubmissionNotReady>();
        // The earlier revision's success does not hold back replacing the later revision's own Attempt.
        revisions.Retire(failed, release: false);
        var replacement = store.PrepareRetry(failed, "after-failure").AttemptId;
        store.ReleaseInstallation();
        await revisions.Continue(second);
        var later = await revisions.Succeed(replacement, "second result");

        // Each Attempt keeps its own result and each submission its own exact lineage.
        await Assert.That(store.FindCompletion(earlier)).IsEqualTo(accepted);
        await Assert.That(later.AcceptedRevision).IsNotEqualTo(accepted.AcceptedRevision);
        await Assert.That(store.GetIssueSubmission(first).AttemptIds).IsEquivalentTo([earlier]);
        await Assert.That(store.GetIssueSubmission(second).AttemptIds).IsEquivalentTo([failed, replacement]);
        await Assert.That(store.Status(store.GetIssueSubmission(first).ContractRevisionId!).Completions.Single())
            .IsEqualTo(accepted);
        await Assert.That(store.GetAttempt(earlier).Abandonment).IsNull();
    }

    /// <summary>The revision is refused for the stated reason and creates nothing.</summary>
    private static async Task Refused(BroodlingStore store, string predecessorId, string reason)
    {
        var history = store.IssueHistory(Issue).Count;
        var refusal = Assert.Throws<IssueSubmissionConflict>(() => store.ReviseIssueSubmission(predecessorId));
        await Assert.That(refusal.Message).Contains(reason);
        await Assert.That(store.IssueHistory(Issue).Count).IsEqualTo(history);
    }

    /// <summary>One store, preparer and target for a Work Unit's revisions, driven step by step.</summary>
    private sealed class Revisions : IAsyncDisposable
    {
        private readonly CancellationTokenSource lifetime = new();
        private readonly IssueSubmissionPreparer preparer;
        internal PreparationFixture Fixture { get; } = new();
        internal StockTarget Target { get; } = new();
        internal BroodlingStore Store { get; }
        internal Gateway Gateway { get; }
        internal string Proposal { get; set; } = RevisionTests.Proposal;

        internal Revisions()
        {
            using (Fixture.State.Initialize()) { }
            Fixture.SetIssue(12, RequestAdmissionTests.Request());
            Store = Fixture.State.Open();
            Gateway = new Gateway((_, _) => Task.FromResult(ControlledGateway.Final(Proposal)));
            preparer = new(Fixture.State.Application, Fixture.State.Path, Fixture.RepositoryRoot, lifetime.Token)
            {
                IssueSource = new GitHubIssueSource(Fixture.Gh), RepositorySource = Fixture.Source, Gateway = Gateway
            };
        }

        internal Task<IssueSubmissionPreparation> Prepare(string submissionId) => preparer.PrepareAsync(submissionId,
            new GitHubRepositoryCredentials("configured-token"), new GatewayCredentials(NativeProfile.GatewayBaseUrl, "gateway-key"));

        /// <summary>What progression does next for an admitted submission: allocate, prepare and dispatch or replay.</summary>
        internal Task<AdmissionStatus> Continue(string submissionId) =>
            new Invocation(Store, new InvocationTarget.Direct(Target.Origin.GetLeftPart(UriPartial.Authority)))
                .ResumeSubmissionAsync(submissionId, HttpDispatchTests.Credentials());

        internal void Finish(string attemptId, string status, object detail)
        {
            var native = Store.FindSubmission(attemptId)!;
            Target.Reply = (request, id) => (string)request["method"]! is "run/status" or "run/force"
                ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = AttemptCompletionTests.HttpFinished(native, status, detail) }.ToJsonString()
                : null;
        }

        internal Task<AttemptCompletion> Succeed(string attemptId, string message)
        {
            var source = Store.FindSubmission(attemptId)!.Frozen.Source!;
            var head = AttemptFixture.RunGit(Fixture.ServiceRepository, "commit-tree", source.Revision + "^{tree}",
                "-p", source.Revision, "-m", message).Trim();
            Finish(attemptId, "succeeded", CompletionFixture.Receipt(head: head));
            return Store.WaitAsync(attemptId, null);
        }

        /// <summary>The host maintenance procedure's retirement: pause, verify the stopped target, retire.</summary>
        internal void Retire(string attemptId, bool release = true)
        {
            Store.PauseInstallation();
            Store.RetireStoppedTargetAttempt(attemptId, new StoppedTargetCheck(Store.FindSubmission(attemptId)!.Locator.Address,
                "broodling-target", "/srv/broodling/target-state", "/srv/broodling/target-home", DateTimeOffset.UtcNow));
            if (release) Store.ReleaseInstallation();
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            Store.Dispose();
            await Target.DisposeAsync();
            Fixture.Dispose();
        }
    }
}
