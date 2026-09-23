using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class IssueSubmissionTests
{
    [Test]
    public async Task IssueUrlSubmissionIsValidatedBeforeDurableCreationAndConvergesOnOneHandle()
    {
        using var fixture = new StoreFixture();
        using (var store = fixture.Initialize())
        {
            foreach (var invalid in new[]
            {
                "https://gitlab.com/acme/widget/-/issues/12",
                "https://github.com/acme/widget/pull/12",
                "https://github.com/acme/widget/-/issues/12",
                "https://github.com/acme/widget/issues/12?edited=true",
                "https://github.com/acme/widget/issues/not-a-number"
            })
                await Assert.That(() => store.SubmitIssue(invalid)).Throws<InvalidWorkReference>();

            await Assert.That(store.IssueHistory(ContractIngressTests.Reference).Count).IsEqualTo(0);
            await Assert.That(store.FindWorkUnit(ContractIngressTests.Reference)).IsNull();
        }

        using var barrier = new Barrier(2);
        Task<IssueSubmission> SubmitConcurrently() => Task.Run(() =>
        {
            using var store = fixture.Open();
            barrier.SignalAndWait();
            return store.SubmitIssue("https://github.com/acme/widget/issues/12");
        });
        var concurrent = await Task.WhenAll(SubmitConcurrently(), SubmitConcurrently());
        await Assert.That(concurrent[0].SubmissionId).IsEqualTo(concurrent[1].SubmissionId);

        using (var reopened = fixture.Open())
        {
            var recovered = reopened.SubmitIssue("https://github.com/acme/widget/issues/12");
            await Assert.That(recovered.SubmissionId).IsEqualTo(concurrent[0].SubmissionId);
            await Assert.That(reopened.IssueHistory(ContractIngressTests.Reference).Count).IsEqualTo(1);
            await Assert.That(reopened.ListWorkSubmissions(concurrent[0].WorkUnitId).Count).IsEqualTo(1);
        }

        fixture.Execute($"UPDATE issue_submissions SET state = 'completed' WHERE submission_id = '{concurrent[0].SubmissionId}'");
        using var retained = fixture.Open();
        var completed = retained.SubmitIssue("https://github.com/acme/widget/issues/12");
        await Assert.That(completed.SubmissionId).IsEqualTo(concurrent[0].SubmissionId);
        await Assert.That(completed.State).IsEqualTo("completed");
    }

    [Test]
    public async Task ExactSubmissionAssociationExposesContractAndExistingAttemptLineage()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var status = store.Status(fixture.RevisionId);

        var associated = store.AssociateIssueSubmission(submission.SubmissionId, status.Revision.ContractRevisionId);
        await Assert.That(associated.ContractRevisionId).IsEqualTo(fixture.RevisionId);
        await Assert.That(associated.AttemptIds).IsEmpty();
        var exact = store.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(exact.SubmissionId).IsEqualTo(associated.SubmissionId);
        await Assert.That(exact.ContractRevisionId).IsEqualTo(associated.ContractRevisionId);
        await Assert.That(store.IssueHistory(ContractIngressTests.Reference).Single().SubmissionId).IsEqualTo(associated.SubmissionId);

        var attempt = fixture.Admit(store);
        var linked = store.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(linked.AttemptIds).IsEquivalentTo([attempt.AttemptId]);
        await Assert.That(store.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId).AttemptIds)
            .IsEquivalentTo(linked.AttemptIds);
    }

    [Test]
    public async Task IssueHistoryUsesRetainedSequenceRatherThanClockOrHandleOrdering()
    {
        using var fixture = new StoreFixture();
        IssueSubmission first;
        using (var store = fixture.Initialize())
            first = store.SubmitIssue("https://github.com/acme/widget/issues/12");

        // Simulate a later retained state without adding the successor operation owned by a later ticket.
        fixture.Execute($"INSERT INTO issue_submissions (submission_id, work_unit_id, submission_sequence, issue_url, state, contract_revision_id, received_at) "
            + $"VALUES ('issue-sub-000', '{first.WorkUnitId}', 2, '{first.IssueUrl}', 'accepted', NULL, '{first.ReceivedAt}')");

        using var reopened = fixture.Open();
        var latest = reopened.FindIssueSubmission(ContractIngressTests.Reference)!;
        var history = reopened.IssueHistory(ContractIngressTests.Reference);
        await Assert.That(latest.SubmissionId).IsEqualTo("issue-sub-000");
        await Assert.That(latest.Sequence).IsEqualTo(2);
        await Assert.That(history.Select(item => item.Sequence).SequenceEqual([1L, 2L])).IsTrue();
    }

    [Test]
    public async Task FailedIssueSubmissionWriteRollsBackWorkUnitAndHandleTogether()
    {
        using var fixture = new StoreFixture();
        using (fixture.Initialize()) { }
        using (var store = fixture.Open())
        {
            fixture.Execute("CREATE TRIGGER interrupt_issue_submission AFTER INSERT ON issue_submissions "
                + "BEGIN SELECT RAISE(ABORT, 'interrupted issue submission'); END;");
            await Assert.That(() => store.SubmitIssue("https://github.com/acme/widget/issues/12"))
                .Throws<Microsoft.Data.Sqlite.SqliteException>();
        }
        fixture.Execute("DROP TRIGGER interrupt_issue_submission");

        using var reopened = fixture.Open();
        await Assert.That(reopened.FindWorkUnit(ContractIngressTests.Reference)).IsNull();
        await Assert.That(reopened.IssueHistory(ContractIngressTests.Reference)).IsEmpty();
    }
}
