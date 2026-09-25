using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Core;
using ControlledGateway = Broodling.Tests.BundledProposerTests.ControlledGateway;

namespace Broodling.Tests;

/// <summary>
/// The exact-submission preparation seam over real capture, Git and SQLite, with controlled GitHub and
/// model gateway peers. Capture and admission policy and their crash matrices belong to their owners'
/// tests; these cover ownership and the capture-to-proposal and Contract-commit handoffs.
/// </summary>
public sealed class IssueSubmissionPreparationTests
{
    private static readonly GitHubRepositoryCredentials GitHub = new("configured-token");
    private static readonly GatewayCredentials Credentials = new(NativeProfile.GatewayBaseUrl, "gateway-key");
    private const string Proposal = """{"criteria": [{"criterionId": "export", "statement": "Export the data."}]}""";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Test]
    public async Task ConcurrentCallersShareOneOwnerWhileAnotherSubmissionPreparesIndependently()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        fixture.SetIssue(13, "## Request\n<!-- broodling-request:v1 -->\nAdd JSON export.\n");
        var (blocked, independent) = Submit(fixture, 12, 13);
        using var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new Gateway(async (context, cancellationToken) =>
        {
            if (!context.Contains("CSV"))
                return ControlledGateway.Final(Proposal);
            entered.Release();
            await release.Task.WaitAsync(cancellationToken);
            return ControlledGateway.Final(Proposal);
        });
        var preparer = Preparer(fixture, gateway, CancellationToken.None);
        using var disconnect = new CancellationTokenSource();

        var starting = preparer.PrepareAsync(blocked, GitHub, Credentials, disconnect.Token);
        await Assert.That(await entered.WaitAsync(Bound)).IsTrue();
        var joined = preparer.PrepareAsync(blocked, GitHub, Credentials);

        // Another submission reaches its decision while this one waits on its model call.
        var other = await preparer.PrepareAsync(independent, GitHub, Credentials).WaitAsync(Bound);
        await Assert.That(((IssueSubmissionPreparation.Decided)other).Admission.Decision!.Admitted).IsTrue();

        // The starting caller's token ends only its own wait; the shared preparation continues.
        disconnect.Cancel();
        await Assert.That(async () => await starting).Throws<OperationCanceledException>();
        release.SetResult();
        var result = await joined.WaitAsync(Bound);

        await Assert.That(((IssueSubmissionPreparation.Decided)result).Admission.Decision!.Admitted).IsTrue();
        await Assert.That(gateway.Contexts.Count(context => context.Contains("CSV"))).IsEqualTo(1);
    }

    [Test]
    public async Task InterruptedProposalRepeatsAgainstTheFrozenCaptureAfterRestart()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        var (submissionId, _) = Submit(fixture, 12);
        using var entered = new SemaphoreSlim(0);
        var interrupted = new Gateway(async (_, cancellationToken) =>
        {
            entered.Release();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        using var shutdown = new CancellationTokenSource();

        var first = Preparer(fixture, interrupted, shutdown.Token).PrepareAsync(submissionId, GitHub, Credentials);
        await Assert.That(await entered.WaitAsync(Bound)).IsTrue();
        shutdown.Cancel();
        await Assert.That(async () => await first).Throws<OperationCanceledException>();

        using (var store = fixture.State.Open())
        {
            var pending = store.GetIssueSubmission(submissionId);
            await Assert.That(pending.State).IsEqualTo("capturing");
            await Assert.That(pending.ContractRevisionId).IsNull();
            await Assert.That(pending.ProposalRefusal).IsNull();
            await Assert.That(store.GetRequestBundle(submissionId).State).IsEqualTo("complete");
        }
        var reads = fixture.ReadGhPaths().Length;
        // The upstream edit after capture cannot reach the repeated proposal.
        fixture.SetIssue(12, "## Request\n<!-- broodling-request:v1 -->\nAdd XML export instead.\n");
        var restarted = new Gateway((_, _) => Task.FromResult(ControlledGateway.Final(Proposal)));

        var result = await Preparer(fixture, restarted, CancellationToken.None).PrepareAsync(submissionId, GitHub, Credentials);

        await Assert.That(((IssueSubmissionPreparation.Decided)result).Admission.Decision!.Admitted).IsTrue();
        await Assert.That(restarted.Contexts.Single()).IsEqualTo(interrupted.Contexts.Single());
        await Assert.That(restarted.Contexts.Single()).Contains("Add CSV export.");
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(reads);
    }

    [Test]
    public async Task ContractCommittedBeforeAnInterruptedDecisionIsDecidedWithoutProposingAgain()
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, RequestAdmissionTests.Request());
        var (submissionId, _) = Submit(fixture, 12);
        var pausing = new Gateway((_, _) =>
        {
            // Another session's write commits during the model call: preparation holds no writer.
            using (var operator_ = fixture.State.Open())
                operator_.PauseInstallation();
            return Task.FromResult(ControlledGateway.Final(Proposal));
        });

        var paused = await Preparer(fixture, pausing, CancellationToken.None).PrepareAsync(submissionId, GitHub, Credentials);

        await Assert.That(paused).IsEqualTo(new IssueSubmissionPreparation.Failed(submissionId, "installation_paused",
            new InstallationPaused().Message, true));
        string revisionId;
        using (var store = fixture.State.Open())
        {
            revisionId = store.GetIssueSubmission(submissionId).ContractRevisionId!;
            await Assert.That(revisionId).IsNotNull();
            await Assert.That(store.FindAdmissionDecision(revisionId)).IsNull();
            store.ReleaseInstallation();
        }
        var reads = fixture.ReadGhPaths().Length;
        var never = new Gateway((_, _) => throw new InvalidOperationException("A committed Contract was proposed again."));

        var result = await Preparer(fixture, never, CancellationToken.None).PrepareAsync(submissionId, GitHub, Credentials);

        var decided = (IssueSubmissionPreparation.Decided)result;
        await Assert.That(decided.Admission.Revision.ContractRevisionId).IsEqualTo(revisionId);
        await Assert.That(decided.Admission.Decision!.Admitted).IsTrue();
        await Assert.That(decided.Submission.State).IsEqualTo("admitted");
        await Assert.That(never.Contexts).IsEmpty();
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(reads);
    }

    [Test]
    [Arguments("capture-refused")]
    [Arguments("proposal-refused")]
    [Arguments("cancelled")]
    [Arguments("gateway-unavailable")]
    public async Task PreparationReturnsRetainedFindingsOrAVisibleRetryableFailure(string variant)
    {
        using var fixture = new RepositoryPreparationTests.RepositoryPreparationFixture();
        fixture.SetIssue(12, variant == "capture-refused" ? "Please add CSV export." : RequestAdmissionTests.Request());
        var (submissionId, _) = Submit(fixture, 12);
        var gateway = new Gateway(async (_, _) =>
        {
            switch (variant)
            {
                case "proposal-refused":
                    return ControlledGateway.Final("[]");
                case "cancelled":
                    using (var caller = fixture.State.Open())
                        await caller.CancelIssueSubmissionAsync(submissionId, "No longer wanted.");
                    return ControlledGateway.Final(Proposal);
                default:
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("overloaded") };
            }
        });
        var preparer = Preparer(fixture, gateway, CancellationToken.None);

        var first = await preparer.PrepareAsync(submissionId, GitHub, Credentials);
        var calls = gateway.Contexts.Count;
        var reads = fixture.ReadGhPaths().Length;

        switch (variant)
        {
            case "capture-refused":
                var capture = (IssueSubmissionPreparation.CaptureRefused)first;
                await Assert.That(capture.Bundle.Findings.Select(finding => finding.Code))
                    .IsEquivalentTo(["request_section_missing"]);
                await Assert.That(capture.Submission.State).IsEqualTo("rejected");
                await Assert.That(calls).IsEqualTo(0);
                break;
            case "proposal-refused":
                var proposal = (IssueSubmissionPreparation.ProposalRefused)first;
                await Assert.That(proposal.Refusal.Findings.Single().Code).IsEqualTo("invalid_contract_proposal");
                await Assert.That(proposal.Submission.State).IsEqualTo("rejected");
                break;
            case "cancelled":
                var cancelled = (IssueSubmissionPreparation.Cancelled)first;
                await Assert.That(cancelled.Submission.ContractRevisionId).IsNull();
                break;
            default:
                var failed = (IssueSubmissionPreparation.Failed)first;
                await Assert.That(failed.Code).IsEqualTo("contract_proposer_error");
                await Assert.That(failed.Retryable).IsTrue();
                using (var store = fixture.State.Open())
                {
                    var pending = store.GetIssueSubmission(submissionId);
                    await Assert.That(pending.State).IsEqualTo("capturing");
                    await Assert.That(pending.ContractRevisionId).IsNull();
                    await Assert.That(pending.ProposalRefusal).IsNull();
                }
                return;
        }
        // A retained end is returned again without acquisition or a model call.
        var again = await preparer.PrepareAsync(submissionId, GitHub, Credentials);
        await Assert.That(again.GetType()).IsEqualTo(first.GetType());
        await Assert.That(gateway.Contexts.Count).IsEqualTo(calls);
        await Assert.That(fixture.ReadGhPaths().Length).IsEqualTo(reads);
    }

    [Test]
    public async Task OnlyABusyOrLockedStoreIsARetryableStoreFailure()
    {
        // A guard trigger's abort is an integrity refusal that retrying cannot resolve.
        var guard = IssueSubmissionPreparer.Failure("sub", new Microsoft.Data.Sqlite.SqliteException(
            "SQLite Error 19: 'Issue submission identity and Contract binding are immutable'.", 19));
        var busy = IssueSubmissionPreparer.Failure("sub", new Microsoft.Data.Sqlite.SqliteException(
            "SQLite Error 5: 'database is locked'.", 5));

        await Assert.That(guard).IsEqualTo(new IssueSubmissionPreparation.Failed("sub", "store_error",
            "SQLite Error 19: 'Issue submission identity and Contract binding are immutable'.", false));
        await Assert.That(busy.Retryable).IsTrue();
    }

    private static (string, string) Submit(RepositoryPreparationTests.RepositoryPreparationFixture fixture,
        long issue, long? other = null)
    {
        using var store = fixture.State.Initialize();
        return (store.SubmitIssue("https://github.com/acme/widget/issues/" + issue).SubmissionId,
            other is { } second ? store.SubmitIssue("https://github.com/acme/widget/issues/" + second).SubmissionId : "");
    }

    private static IssueSubmissionPreparer Preparer(RepositoryPreparationTests.RepositoryPreparationFixture fixture,
        HttpMessageHandler gateway, CancellationToken lifetime) =>
        new(fixture.State.Application, fixture.State.Path, fixture.RepositoryRoot, lifetime)
    {
        IssueSource = new GitHubIssueSource(fixture.Gh), RepositorySource = fixture.Source, Gateway = gateway
    };

    /// <summary>A controlled model gateway that records each proposal's initial context.</summary>
    private sealed class Gateway(Func<string, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> contexts = new();
        internal IReadOnlyList<string> Contexts => contexts.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var context = (string)body["messages"]![1]!["content"]!;
            contexts.Enqueue(context);
            return await reply(context, cancellationToken);
        }
    }
}
