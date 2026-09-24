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

        var foreignReference = WorkReference.Parse("acme/widget", 13);
        var foreign = store.AdmitSources(foreignReference,
            [new SourceSubmission("primary_issue", foreignReference.IssueLocator, "Foreign work unit."u8.ToArray(),
                entitlement: new("caller", "Reviewed foreign request"))], ContractIngressTests.Propose, []);
        await Assert.That(() => store.AssociateIssueSubmission(submission.SubmissionId, foreign.Revision.ContractRevisionId))
            .Throws<IssueSubmissionConflict>();
        var unbound = store.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(unbound.ContractRevisionId).IsNull();
        await Assert.That(unbound.AttemptIds).IsEmpty();

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

        // Seed a later retained row; successor creation remains a later ticket.
        const string secondSubmissionId = "issue-sub-second";
        fixture.State.Execute($"INSERT INTO issue_submissions (submission_id, work_unit_id, submission_sequence, issue_url, state, contract_revision_id, received_at) "
            + $"VALUES ('{secondSubmissionId}', '{submission.WorkUnitId}', 2, '{submission.IssueUrl}', 'accepted', NULL, '{submission.ReceivedAt}')");
        var second = store.GetIssueSubmission(secondSubmissionId);
        await Assert.That(second.WorkUnitId).IsEqualTo(submission.WorkUnitId);
        var secondLinked = store.AssociateIssueSubmission(secondSubmissionId, fixture.RevisionId);
        await Assert.That(secondLinked.ContractRevisionId).IsEqualTo(fixture.RevisionId);
        await Assert.That(secondLinked.AttemptIds).IsEquivalentTo(linked.AttemptIds);
        await Assert.That(store.GetIssueSubmission(submission.SubmissionId).AttemptIds)
            .IsEquivalentTo(secondLinked.AttemptIds);

        var revised = store.AdmitSources(ContractIngressTests.Reference,
            [ContractIngressTests.Primary("A revised retained request."u8.ToArray())], ContractIngressTests.Propose, []);
        await Assert.That(revised.Revision.ContractRevisionId).IsNotEqualTo(fixture.RevisionId);
        await Assert.That(() => store.AssociateIssueSubmission(submission.SubmissionId, revised.Revision.ContractRevisionId))
            .Throws<IssueSubmissionConflict>();

        var preserved = store.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(preserved.SubmissionId).IsEqualTo(submission.SubmissionId);
        await Assert.That(preserved.ContractRevisionId).IsEqualTo(fixture.RevisionId);
        await Assert.That(preserved.AttemptIds).IsEquivalentTo([attempt.AttemptId]);
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
        var replay = reopened.SubmitIssue("https://github.com/acme/widget/issues/12");
        var original = reopened.GetIssueSubmission(first.SubmissionId);
        await Assert.That(replay.SubmissionId).IsEqualTo(latest.SubmissionId);
        await Assert.That(replay.Sequence).IsEqualTo(latest.Sequence);
        await Assert.That(original.SubmissionId).IsEqualTo(first.SubmissionId);
        await Assert.That(original.Sequence).IsEqualTo(first.Sequence);
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

    [Test]
    public async Task CancelledSubmissionSurvivesReopenAndCannotAcquireContractOrAttemptAuthority()
    {
        using var fixture = new AttemptFixture();
        IssueSubmission cancelled;
        using (var store = fixture.State.Open())
        {
            var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            store.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId);
            cancelled = await store.CancelIssueSubmissionAsync(submission.SubmissionId,
                "caller withdrew the request", new ControlledTransport());

            await Assert.That(cancelled.State).IsEqualTo("cancelled");
            await Assert.That(cancelled.ContractRevisionId).IsEqualTo(fixture.RevisionId);
            await Assert.That(cancelled.AttemptIds).IsEmpty();
            await Assert.That(cancelled.Cancellation).IsNotNull();
            await Assert.That(cancelled.Cancellation!.AttemptId).IsNull();
            await Assert.That(cancelled.Cancellation.Reason).IsEqualTo("caller withdrew the request");
        }

        using var reopened = fixture.State.Open();
        var recovered = reopened.GetIssueSubmission(cancelled.SubmissionId);
        await Assert.That(recovered.State).IsEqualTo("cancelled");
        await Assert.That(recovered.Cancellation!.AttemptId).IsNull();
        await Assert.That(() => fixture.Admit(reopened)).Throws<IssueSubmissionConflict>();
        await Assert.That(recovered.AttemptIds).IsEmpty();
    }

    [Test]
    public async Task UnboundCancellationOrdersAgainstAssociationAndRefusesReopenedCapture()
    {
        using var fixture = new AttemptFixture();
        IssueSubmission submission;
        using (var seed = fixture.State.Open())
            submission = seed.SubmitIssue("https://github.com/acme/widget/issues/12");

        using var barrier = new Barrier(2);
        var cancellation = Task.Run(async () =>
        {
            using var store = fixture.State.Open();
            barrier.SignalAndWait();
            return await store.CancelIssueSubmissionAsync(submission.SubmissionId, "cancel before association");
        });
        var association = Task.Run(() =>
        {
            using var store = fixture.State.Open();
            barrier.SignalAndWait();
            try
            {
                return (Bound: (IssueSubmission?)store.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId),
                    Error: (Exception?)null);
            }
            catch (Exception error)
            {
                return (Bound: (IssueSubmission?)null, Error: error);
            }
        });

        var cancelled = await cancellation;
        var associated = await association;
        await Assert.That(associated.Error is null || associated.Error is IssueSubmissionConflict).IsTrue();

        using var reopened = fixture.State.Open();
        var recovered = reopened.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(cancelled.State).IsEqualTo("cancelled");
        await Assert.That(recovered.State).IsEqualTo("cancelled");
        await Assert.That(recovered.Cancellation!.AttemptId).IsNull();
        await Assert.That(() => reopened.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(() => reopened.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray())))
            .Throws<RequestBundleConflict>();

        if (associated.Bound is not null)
            await Assert.That(associated.Bound.ContractRevisionId).IsEqualTo(fixture.RevisionId);
    }

    [Test]
    public async Task CancellingSharedContractSubmissionAfterDispatchPreservesSurvivorAndLastCancellationStopsExactly()
    {
        using var fixture = new NativeFixture();
        IssueSubmission first;
        IssueSubmission firstCancelled;
        string survivorState;
        string[] survivorAttemptIds;
        const string secondSubmissionId = "issue-sub-dispatched-survivor";
        AttemptRecord attempt;
        NativeSubmission dispatched;

        using (var store = fixture.Git.State.Open())
        {
            first = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            store.AssociateIssueSubmission(first.SubmissionId, fixture.Git.RevisionId);
            fixture.Git.State.Execute($"INSERT INTO issue_submissions (submission_id, work_unit_id, submission_sequence, issue_url, state, contract_revision_id, received_at) "
                + $"VALUES ('{secondSubmissionId}', '{first.WorkUnitId}', 2, '{first.IssueUrl}', 'accepted', '{fixture.Git.RevisionId}', '{first.ReceivedAt}')");

            attempt = fixture.Provision(store);
            var transport = new ControlledTransport { Submit = (_, _) => Task.FromResult("shared-run") };
            dispatched = await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport);
            var survivorBeforeCancellation = store.GetIssueSubmission(secondSubmissionId);
            survivorState = survivorBeforeCancellation.State;
            survivorAttemptIds = survivorBeforeCancellation.AttemptIds.ToArray();

            firstCancelled = await store.CancelIssueSubmissionAsync(first.SubmissionId,
                "withdraw first shared ticket", transport);
            await Assert.That(firstCancelled.State).IsEqualTo("cancelled");
            await Assert.That(firstCancelled.Cancellation!.AttemptId).IsNull();
            var survivorAfterCancellation = store.GetIssueSubmission(secondSubmissionId);
            await Assert.That(survivorAfterCancellation.State).IsEqualTo(survivorState);
            await Assert.That(survivorAfterCancellation.AttemptIds).IsEquivalentTo(survivorAttemptIds);
            await Assert.That(store.GetAttempt(attempt.AttemptId).IsCurrent).IsTrue();
            await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
            await Assert.That(transport.StopCalls).IsEqualTo(0);
        }

        using var reopened = fixture.Git.State.Open();
        var transportAfterReopen = new ControlledTransport
        {
            Stop = (locator, runId, _) =>
            {
                using var observer = fixture.Git.State.Open();
                var cancelled = observer.GetIssueSubmission(secondSubmissionId);
                if (cancelled.State != "cancelled" || cancelled.Cancellation?.AttemptId != attempt.AttemptId)
                    throw new Exception("Native stop observed before exact cancellation binding committed.");
                if (observer.GetAttempt(attempt.AttemptId).Abandonment?.Reason != "withdraw last shared ticket")
                    throw new Exception("Native stop observed before exact Attempt abandonment committed.");
                if (locator != dispatched.Locator || runId != dispatched.RunId)
                    throw new Exception("Native stop did not receive the exact dispatched locator and run.");
                return Task.FromResult(new NativeResult(runId, true, default, null));
            }
        };

        await Assert.That(async () => await reopened.CancelIssueSubmissionAsync(secondSubmissionId,
            "withdraw last shared ticket", transportAfterReopen)).Throws<CessationUnconfirmed>();
        await Assert.That(transportAfterReopen.StopCalls).IsEqualTo(1);
        var stopCallsBeforeFirstReplay = transportAfterReopen.StopCalls;
        var replayedFirst = await reopened.CancelIssueSubmissionAsync(first.SubmissionId,
            "replayed first shared ticket", transportAfterReopen);
        await Assert.That(replayedFirst.State).IsEqualTo(firstCancelled.State);
        await Assert.That(replayedFirst.Cancellation!.AttemptId).IsNull();
        await Assert.That(transportAfterReopen.StopCalls).IsEqualTo(stopCallsBeforeFirstReplay);
        var firstRecovered = reopened.GetIssueSubmission(first.SubmissionId);
        await Assert.That(firstRecovered.Cancellation!.AttemptId).IsNull();
        await Assert.That(reopened.GetIssueSubmission(secondSubmissionId).Cancellation!.AttemptId)
            .IsEqualTo(attempt.AttemptId);
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).Abandonment!.Reason)
            .IsEqualTo("withdraw last shared ticket");
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).IsCurrent).IsFalse();
    }

    [Test]
    public async Task CancellationAndAbandonmentWriteRollsBackAsOneTransaction()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        store.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId);
        var attempt = fixture.Admit(store);
        fixture.State.Execute("CREATE TRIGGER fail_cancellation_abandonment BEFORE INSERT ON attempt_abandonments "
            + "BEGIN SELECT RAISE(ABORT, 'controlled cancellation rollback'); END;");

        await Assert.That(async () => await store.CancelIssueSubmissionAsync(submission.SubmissionId, "rollback me"))
            .Throws<Microsoft.Data.Sqlite.SqliteException>();
        fixture.State.Execute("DROP TRIGGER fail_cancellation_abandonment");

        using var reopened = fixture.State.Open();
        var retained = reopened.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(retained.State).IsEqualTo("accepted");
        await Assert.That(retained.Cancellation).IsNull();
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).Abandonment).IsNull();
        await Assert.That(reopened.GetAttempt(attempt.AttemptId).IsCurrent).IsTrue();
    }

    [Test]
    public async Task CancellationReplayRetainsOriginalAttemptAcrossLegitimateSafeReplacement()
    {
        using var fixture = new NativeFixture();
        IssueSubmission first;
        AttemptRecord original;
        const string secondSubmissionId = "issue-sub-final-stop";
        using (var store = fixture.Git.State.Open())
        {
            first = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            store.AssociateIssueSubmission(first.SubmissionId, fixture.Git.RevisionId);
            fixture.Git.State.Execute($"INSERT INTO issue_submissions (submission_id, work_unit_id, submission_sequence, issue_url, state, contract_revision_id, received_at) "
                + $"VALUES ('{secondSubmissionId}', '{first.WorkUnitId}', 2, '{first.IssueUrl}', 'accepted', '{fixture.Git.RevisionId}', '{first.ReceivedAt}')");
            original = fixture.Git.Admit(store);

            var cancelledFirst = await store.CancelIssueSubmissionAsync(first.SubmissionId, "withdraw first");
            await Assert.That(cancelledFirst.Cancellation!.AttemptId).IsNull();
            await Assert.That(store.GetAttempt(original.AttemptId).IsCurrent).IsTrue();
            await Assert.That(store.GetAttempt(original.AttemptId).Abandonment).IsNull();
        }

        using var reopened = fixture.Git.State.Open();
        var cancelledLast = await reopened.CancelIssueSubmissionAsync(secondSubmissionId, "withdraw last");
        await Assert.That(cancelledLast.Cancellation!.AttemptId).IsEqualTo(original.AttemptId);
        await Assert.That(reopened.GetAttempt(original.AttemptId).Abandonment!.Reason).IsEqualTo("withdraw last");
        reopened.RetireAttempt(original.AttemptId);
        var successor = reopened.AdmitRetry(original.AttemptId, "after-cancellation", fixture.Git.Workspaces, fixture.Profile);
        reopened.ProvisionAttempt(successor.AttemptId);

        var replayed = await reopened.CancelIssueSubmissionAsync(first.SubmissionId, "replayed first cancellation",
            new ControlledTransport());
        await Assert.That(replayed.Cancellation!.AttemptId).IsNull();
        await Assert.That(reopened.GetAttempt(original.AttemptId).Abandonment!.Reason).IsEqualTo("withdraw last");
        await Assert.That(reopened.GetAttempt(successor.AttemptId).IsCurrent).IsTrue();
        await Assert.That(reopened.FindRetirement(successor.AttemptId)).IsNull();

        var replayedLast = await reopened.CancelIssueSubmissionAsync(secondSubmissionId, "replayed last cancellation",
            new ControlledTransport());
        await Assert.That(replayedLast.Cancellation!.AttemptId).IsEqualTo(original.AttemptId);
        await Assert.That(reopened.GetAttempt(successor.AttemptId).IsCurrent).IsTrue();
        await Assert.That(reopened.FindRetirement(successor.AttemptId)).IsNull();
    }

    [Test]
    public async Task CancellingOneSharedContractSubmissionDoesNotBlockAnUncancelledRetainedTicket()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var first = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        store.AssociateIssueSubmission(first.SubmissionId, fixture.RevisionId);
        const string secondSubmissionId = "issue-sub-active-ticket";
        fixture.State.Execute($"INSERT INTO issue_submissions (submission_id, work_unit_id, submission_sequence, issue_url, state, contract_revision_id, received_at) "
            + $"VALUES ('{secondSubmissionId}', '{first.WorkUnitId}', 2, '{first.IssueUrl}', 'accepted', '{fixture.RevisionId}', '{first.ReceivedAt}')");

        await store.CancelIssueSubmissionAsync(first.SubmissionId, "withdraw only the first ticket");
        var attempt = fixture.Admit(store);
        await Assert.That(attempt.IsCurrent).IsTrue();
        await Assert.That(store.GetIssueSubmission(secondSubmissionId).State).IsEqualTo("accepted");
    }
}
