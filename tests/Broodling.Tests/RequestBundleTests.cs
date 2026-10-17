using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RequestBundleTests
{
    [Test]
    public async Task InterruptedCaptureResumesMissingMembersWithoutReplacingFirstCapturedBytesOrInputs()
    {
        using var fixture = new StoreFixture();
        IssueSubmission submission;
        RequestBundle bundle;
        string firstSourceId;
        var plan = new RequestBundlePlan(
            "capture-inputs-v1"u8.ToArray(), "policy-v1"u8.ToArray());

        using (var store = fixture.Initialize())
        {
            submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            bundle = store.BeginRequestBundleCapture(submission.SubmissionId, plan);
            store.RegisterRequestBundleReference(bundle.BundleId, "primary", "primary selector"u8.ToArray());
            var first = store.CaptureRequestBundleSource(bundle.BundleId, "primary",
                CallerSource("caller://issue/12", "captured first\n"u8.ToArray()));
            firstSourceId = first.SourceId!;
        }

        using var reopened = fixture.Open();
        var changedPlan = new RequestBundlePlan("changed-inputs"u8.ToArray(),
            plan.AcquisitionPolicy);
        await Assert.That(() => reopened.BeginRequestBundleCapture(submission.SubmissionId, changedPlan))
            .Throws<RequestBundleConflict>();

        var resumed = reopened.BeginRequestBundleCapture(submission.SubmissionId, plan);
        reopened.RegisterRequestBundleReference(resumed.BundleId, "supporting", "discovered from primary"u8.ToArray());
        var retainedFirst = reopened.CaptureRequestBundleSource(resumed.BundleId, "primary",
            CallerSource("caller://issue/12", "edited after interruption\n"u8.ToArray()));
        var supporting = reopened.CaptureRequestBundleSource(resumed.BundleId, "supporting",
            CallerSource("caller://supporting", "captured after reopen\n"u8.ToArray()));
        var complete = reopened.CompleteRequestBundleCapture(resumed.BundleId);
        var retained = reopened.GetRequestBundle(submission.SubmissionId);

        await Assert.That(retainedFirst.SourceId).IsEqualTo(firstSourceId);
        await Assert.That(retained.State).IsEqualTo("complete");
        await Assert.That(retained.AcquisitionInputs).IsEquivalentTo("capture-inputs-v1"u8.ToArray());
        await Assert.That(retained.AcquisitionPolicy).IsEquivalentTo("policy-v1"u8.ToArray());
        await Assert.That(retained.ManifestSha256).IsEqualTo(complete.ManifestSha256);
        await Assert.That(retainedFirst.SourceId).IsEqualTo(firstSourceId);
        await Assert.That(supporting.IsCaptured).IsTrue();
        await Assert.That(retained.References.Select(reference => reference.ReferenceId))
            .IsEquivalentTo(["primary", "supporting"]);
        await Assert.That(reopened.GetEntitledSource(firstSourceId).Content.SequenceEqual("captured first\n"u8.ToArray())).IsTrue();
    }

    [Test]
    public async Task FailedFirstCaptureCheckpointRollsBackItsSourceSnapshotAtomically()
    {
        using var fixture = new StoreFixture();
        string submissionId;
        string bundleId;
        using (var store = fixture.Initialize())
        {
            var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            submissionId = submission.SubmissionId;
            var bundle = store.BeginRequestBundleCapture(submissionId,
                new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray()));
            bundleId = bundle.BundleId;
            store.RegisterRequestBundleReference(bundleId, "primary", "primary selector"u8.ToArray());
        }

        using (var store = fixture.Open())
        {
            fixture.Execute("CREATE TRIGGER fail_bundle_checkpoint BEFORE UPDATE OF source_id ON request_bundle_references "
                + "BEGIN SELECT RAISE(ABORT, 'interrupted source checkpoint'); END;");
            await Assert.That(() => store.CaptureRequestBundleSource(bundleId, "primary",
                CallerSource("caller://first-attempt", "failed bytes"u8.ToArray()))).Throws<SqliteException>();
            fixture.Execute("DROP TRIGGER fail_bundle_checkpoint");
        }

        using var reopened = fixture.Open();
        var workUnitId = reopened.GetIssueSubmission(submissionId).WorkUnitId;
        await Assert.That(reopened.ListEntitledSources(workUnitId).Count).IsEqualTo(0);
        var captured = reopened.CaptureRequestBundleSource(bundleId, "primary",
            CallerSource("caller://retry", "durable retry bytes"u8.ToArray()));
        reopened.CompleteRequestBundleCapture(bundleId);
        await Assert.That(captured.ContentSha256).IsEqualTo(Digests.Bytes("durable retry bytes"u8));
        await Assert.That(reopened.GetEntitledSource(captured.SourceId!).Content.SequenceEqual("durable retry bytes"u8.ToArray())).IsTrue();
    }

    [Test]
    public async Task CompletedBundleIdentityManifestAndMembershipCannotBeExtendedOrRewritten()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var plan = new RequestBundlePlan(
            "inputs"u8.ToArray(), "policy"u8.ToArray());
        var pending = store.BeginRequestBundleCapture(submission.SubmissionId, plan);
        store.RegisterRequestBundleReference(pending.BundleId, "primary", "selector"u8.ToArray());
        var captured = store.CaptureRequestBundleSource(pending.BundleId, "primary",
            CallerSource("caller://immutable", "original snapshot"u8.ToArray()));
        var completed = store.CompleteRequestBundleCapture(pending.BundleId);

        await Assert.That(() => store.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("other inputs"u8.ToArray(), plan.AcquisitionPolicy))).Throws<RequestBundleConflict>();
        var replayed = store.CaptureRequestBundleSource(completed.BundleId, "primary",
            CallerSource("caller://immutable", "replacement"u8.ToArray()));
        await Assert.That(replayed.SourceId).IsEqualTo(captured.SourceId);
        await Assert.That(() => store.RegisterRequestBundleReference(completed.BundleId, "extra", "extra selector"u8.ToArray()))
            .Throws<RequestBundleConflict>();

        await Assert.That(() => fixture.Execute($"UPDATE request_bundles SET manifest_sha256 = '{new string('0', 64)}' WHERE bundle_id = '{completed.BundleId}'"))
            .Throws<SqliteException>();
        await Assert.That(() => fixture.Execute($"UPDATE request_bundles SET bundle_id = 'bundle-rewritten' WHERE bundle_id = '{completed.BundleId}'"))
            .Throws<SqliteException>();
        await Assert.That(() => fixture.Execute($"UPDATE request_bundle_references SET selector = X'00' WHERE bundle_id = '{completed.BundleId}' AND reference_id = 'primary'"))
            .Throws<SqliteException>();
        await Assert.That(() => fixture.Execute($"INSERT INTO request_bundle_references (bundle_id, reference_id, ordinal, capture_kind, selector) VALUES ('{completed.BundleId}', 'extra', 1, 'source', X'00')"))
            .Throws<SqliteException>();

        var reopened = store.GetRequestBundle(submission.SubmissionId);
        await Assert.That(reopened.BundleId).IsEqualTo(completed.BundleId);
        await Assert.That(reopened.ManifestSha256).IsEqualTo(completed.ManifestSha256);
        await Assert.That(reopened.ManifestSha256)
            .IsEqualTo(Digests.Bytes(System.Text.Encoding.UTF8.GetBytes(reopened.ManifestJson!)));
        await Assert.That(reopened.References.Select(reference => reference.ReferenceId)).IsEquivalentTo(["primary"]);
    }

    [Test]
    public async Task ContractAssociationWaitsForIncompleteBundleAndThenRequiresItsBinding()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var bundle = store.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray()));
        store.RegisterRequestBundleReference(bundle.BundleId, "primary", "selector"u8.ToArray());

        await Assert.That(() => store.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(store.GetIssueSubmission(submission.SubmissionId).ContractRevisionId).IsNull();

        store.RegisterRequestBundleReference(bundle.BundleId, "supporting", "discovered later"u8.ToArray());
        store.CaptureRequestBundleSource(bundle.BundleId, "primary",
            CallerSource("caller://primary", "primary bytes"u8.ToArray()));
        store.CaptureRequestBundleSource(bundle.BundleId, "supporting",
            CallerSource("caller://supporting", "supporting bytes"u8.ToArray()));
        var completed = store.CompleteRequestBundleCapture(bundle.BundleId);
        await Assert.That(completed.State).IsEqualTo("complete");

        // Completion does not open the old route: a Contract admitted without this bundle's
        // binding cannot become the bundled submission's authority.
        await Assert.That(() => store.AssociateIssueSubmission(submission.SubmissionId, fixture.RevisionId))
            .Throws<IssueSubmissionConflict>();
        await Assert.That(store.GetIssueSubmission(submission.SubmissionId).ContractRevisionId).IsNull();
    }

    private static SourceSubmission CallerSource(string locator, byte[] bytes) =>
        new("caller_statement", locator, bytes, origin: "caller",
            entitlement: new SourceEntitlement("caller", "controlled captured material"));
}
