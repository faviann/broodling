using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RequestBundleTests
{
    [Test]
    public async Task InterruptedCaptureResumesMissingReferencesWithoutReplacingFirstCapturedBytesOrInputs()
    {
        using var fixture = new StoreFixture();
        IssueSubmission submission;
        RequestBundle bundle;
        string firstSourceId;
        var plan = new RequestBundlePlan(
            "capture-inputs-v1"u8.ToArray(), "policy-v1"u8.ToArray(), "limits-v1"u8.ToArray());

        using (var store = fixture.Initialize())
        {
            submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
            bundle = store.BeginRequestBundleCapture(submission.SubmissionId, plan);
            store.RegisterRequestBundleReference(bundle.BundleId,
                RequestBundleReferenceInput.Source("primary", "primary selector"u8.ToArray()));
            var first = store.CaptureRequestBundleSource(bundle.BundleId, "primary",
                CallerSource("caller://issue/12", "captured first\n"u8.ToArray()));
            firstSourceId = first.SourceId!;
        }

        using var reopened = fixture.Open();
        var changedPlan = new RequestBundlePlan("changed-inputs"u8.ToArray(),
            plan.AcquisitionPolicy, plan.AcquisitionLimits);
        await Assert.That(() => reopened.BeginRequestBundleCapture(submission.SubmissionId, changedPlan))
            .Throws<RequestBundleConflict>();

        var resumed = reopened.BeginRequestBundleCapture(submission.SubmissionId, plan);
        reopened.RegisterRequestBundleReference(resumed.BundleId,
            RequestBundleReferenceInput.Source("supporting", "discovered from primary"u8.ToArray()));
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
        await Assert.That(retained.AcquisitionLimits).IsEquivalentTo("limits-v1"u8.ToArray());
        await Assert.That(retained.ManifestSha256).IsEqualTo(complete.ManifestSha256);
        await Assert.That(retainedFirst.SourceId).IsEqualTo(firstSourceId);
        await Assert.That(supporting.IsCaptured).IsTrue();
        await Assert.That(retained.References.Select(reference => reference.ReferenceId))
            .IsEquivalentTo(["primary", "supporting"]);
        var read = reopened.ReadRequestBundleReference(retained.BundleId, "primary");
        await Assert.That(read.Content.SequenceEqual("captured first\n"u8.ToArray())).IsTrue();
        await Assert.That(read.ContentSha256).IsEqualTo(retainedFirst.ContentSha256);
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
                new RequestBundlePlan("inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
            bundleId = bundle.BundleId;
            store.RegisterRequestBundleReference(bundleId,
                RequestBundleReferenceInput.Source("primary", "primary selector"u8.ToArray()));
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
        var completed = reopened.CompleteRequestBundleCapture(bundleId);
        var read = reopened.ReadRequestBundleReference(completed.BundleId, "primary");
        await Assert.That(captured.ContentSha256).IsEqualTo(Digests.Bytes("durable retry bytes"u8));
        await Assert.That(read.Content.SequenceEqual("durable retry bytes"u8.ToArray())).IsTrue();
    }

    [Test]
    public async Task CompletedBundleIdentityManifestAndMembershipCannotBeExtendedOrRewritten()
    {
        using var fixture = new StoreFixture();
        using var store = fixture.Initialize();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var plan = new RequestBundlePlan(
            "inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray());
        var pending = store.BeginRequestBundleCapture(submission.SubmissionId, plan);
        store.RegisterRequestBundleReference(pending.BundleId,
            RequestBundleReferenceInput.Source("primary", "selector"u8.ToArray()));
        store.CaptureRequestBundleSource(pending.BundleId, "primary",
            CallerSource("caller://immutable", "original snapshot"u8.ToArray()));
        var completed = store.CompleteRequestBundleCapture(pending.BundleId);

        await Assert.That(() => store.BeginRequestBundleCapture(submission.SubmissionId,
            new RequestBundlePlan("other inputs"u8.ToArray(), plan.AcquisitionPolicy,
                plan.AcquisitionLimits))).Throws<RequestBundleConflict>();
        await Assert.That(() => store.CaptureRequestBundleSource(completed.BundleId, "primary",
            CallerSource("caller://immutable", "replacement"u8.ToArray()))).Throws<RequestBundleConflict>();
        await Assert.That(() => store.RegisterRequestBundleReference(completed.BundleId,
            RequestBundleReferenceInput.Source("extra", "extra selector"u8.ToArray())))
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
    public async Task BundleReadReturnsPinnedGitBytesAndRejectsReferencesOutsideItsMembership()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var submission = store.SubmitIssue("https://github.com/acme/widget/issues/12");
        var plan = new RequestBundlePlan(
            "git inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray());
        var bundle = store.BeginRequestBundleCapture(submission.SubmissionId, plan);
        store.RegisterRequestBundleReference(bundle.BundleId,
            RequestBundleReferenceInput.GitBlob("repository-file", "selected file"u8.ToArray(),
                fixture.Repository, "HEAD", "original.txt"));
        store.CaptureRequestBundleGitBlob(bundle.BundleId, "repository-file");
        var completed = store.CompleteRequestBundleCapture(bundle.BundleId);

        _ = fixture.Commit("later working tree bytes\n");
        fixture.Git("checkout", "--orphan", "unrelated");
        fixture.Git("rm", "-rf", ".");
        File.WriteAllText(System.IO.Path.Combine(fixture.Repository, "unrelated.txt"), "unrelated\n");
        fixture.Git("add", ".");
        fixture.Git("commit", "-m", "unrelated root");
        fixture.Git("update-ref", "-d", "refs/heads/main");
        fixture.Git("reflog", "expire", "--expire=now", "--all");
        fixture.Git("gc", "--prune=now");

        var read = store.ReadRequestBundleReference(completed.BundleId, "repository-file");
        await Assert.That(read.Content.SequenceEqual("original selected bytes\n"u8.ToArray())).IsTrue();
        await Assert.That(read.GitCommitOid).IsEqualTo(fixture.Head);

        var otherSubmission = store.SubmitIssue("https://github.com/acme/widget/issues/13");
        var otherBundle = store.BeginRequestBundleCapture(otherSubmission.SubmissionId,
            new RequestBundlePlan("other inputs"u8.ToArray(), "policy"u8.ToArray(), "limits"u8.ToArray()));
        store.RegisterRequestBundleReference(otherBundle.BundleId,
            RequestBundleReferenceInput.Source("other-reference", "other selector"u8.ToArray()));
        store.CaptureRequestBundleSource(otherBundle.BundleId, "other-reference",
            CallerSource("caller://other-bundle", [0, 255, 1, 128]));
        var otherCompleted = store.CompleteRequestBundleCapture(otherBundle.BundleId);
        var otherRead = store.ReadRequestBundleReference(otherCompleted.BundleId, "other-reference");
        await Assert.That(otherRead.Content.SequenceEqual(new byte[] { 0, 255, 1, 128 })).IsTrue();
        await Assert.That(() => store.ReadRequestBundleReference(completed.BundleId, "other-reference"))
            .Throws<UnknownRecord>();
    }

    private static SourceSubmission CallerSource(string locator, byte[] bytes) =>
        new("caller_statement", locator, bytes, origin: "caller",
            entitlement: new SourceEntitlement("caller", "controlled captured material"));
}
