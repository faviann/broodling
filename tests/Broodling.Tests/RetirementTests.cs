using Broodling.Host;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;
using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class RetirementTests
{
    [Test]
    [Arguments("absent")]
    [Arguments("prepared")]
    [Arguments("provisioned")]
    public async Task SafeNeverDispatchedProofAndRetirementSurviveReopen(string state)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Git.Admit(store);
        if (state != "absent") store.ProvisionAttempt(attempt.AttemptId);
        if (state == "prepared") store.PrepareSubmission(attempt.AttemptId, fixture.Profile);
        var proof = await store.StopAsync(attempt.AttemptId, "first reason", new ControlledTransport());
        await Assert.That(proof.Basis).IsEqualTo(state == "absent" ? "never_materialized" : "never_dispatched");
        await Assert.That(proof.RetiredAt).IsNull();
        using var reopened = fixture.Git.State.Open();
        await Assert.That(await reopened.StopAsync(attempt.AttemptId, "second reason", new ControlledTransport())).IsEqualTo(proof);
        var retired = reopened.RetireAttempt(attempt.AttemptId);
        await Assert.That(retired.RetiredAt).IsNotNull();
        await Assert.That(reopened.RetireAttempt(attempt.AttemptId)).IsEqualTo(retired);
        await Assert.That(store.Status(attempt.ContractRevisionId).Attempts.Single().Retirement).IsEqualTo(retired);
        await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment!.Reason).IsEqualTo("first reason");
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<StaleAttempt>();
        await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsFalse();
    }

    [Test]
    [Arguments("enclosure-only")]
    [Arguments("unacknowledged-checkout")]
    [Arguments("missing-acknowledged-enclosure")]
    public async Task AmbiguousProvisioningNeverGrantsSafeProof(string state)
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        if (state == "missing-acknowledged-enclosure")
        {
            store.ProvisionAttempt(attempt.AttemptId);
            Directory.Delete(attempt.Allocation.Enclosure, true);
        }
        else
        {
            WorktreeMaterialization.ClaimEnclosure(attempt);
            if (state == "unacknowledged-checkout")
            {
                using var held = AdministrativeGitProcess.EnclosureLock.Acquire(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName));
                WorktreeMaterialization.Materialize(attempt, held);
            }
        }
        await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "stop ambiguous setup", new ControlledTransport())).Throws<CessationUnconfirmed>();
        await Assert.That(store.GetAttempt(attempt.AttemptId).IsCurrent).IsFalse();
        // A vanished local directory never reinterprets the record as a no-directory HTTP Attempt.
        await Assert.That(store.GetAttempt(attempt.AttemptId).ResourceKind).IsEqualTo(AttemptRecord.Worktree);
        await Assert.That(() => fixture.State.Execute($"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at) VALUES ('{attempt.AttemptId}', 'no_dispatch_intent', 'now')"))
            .Throws<SqliteException>();
        await Assert.That(store.FindRetirement(attempt.AttemptId)).IsNull();
        await Assert.That(() => store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
    }

    [Test]
    public async Task HttpUndispatchedStopAndRetirementNeedNoLocalResourceAndKeepCustody()
    {
        using var fixture = new AttemptFixture();
        var local = fixture.LocalResources();
        AttemptRecord attempt;
        AttemptRetirement proof;
        using (var store = fixture.State.Open())
        {
            attempt = fixture.AdmitHttp(store);
            await Assert.That(() => store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
            store.AbandonAttempt(attempt.AttemptId, "first reason");
            // A worktree basis cannot describe an Attempt that never owned local material.
            await Assert.That(() => fixture.State.Execute($"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at) VALUES ('{attempt.AttemptId}', 'never_materialized', 'now')"))
                .Throws<SqliteException>();
            proof = await store.StopAsync(attempt.AttemptId, "later reason", transport: null);
            await Assert.That(proof.Basis).IsEqualTo("no_dispatch_intent");
            await Assert.That(proof.RetiredAt).IsNull();
        }
        using var reopened = fixture.State.Open();
        await Assert.That(await reopened.StopAsync(attempt.AttemptId, "second reason", transport: null)).IsEqualTo(proof);
        var retired = reopened.RetireAttempt(attempt.AttemptId);
        await Assert.That(retired.RetiredAt).IsNotNull();
        await Assert.That(reopened.RetireAttempt(attempt.AttemptId)).IsEqualTo(retired);
        var retained = reopened.GetAttempt(attempt.AttemptId);
        await Assert.That(retained.Retirement).IsEqualTo(retired);
        await Assert.That(retained.Abandonment!.Reason).IsEqualTo("first reason");
        await Assert.That(retained.B1).IsEqualTo(attempt.B1);
        await Assert.That(retained.ResourceKind).IsEqualTo(AttemptRecord.Http);
        await Assert.That(fixture.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(attempt.B1.CommitOid);
        await Assert.That(fixture.LocalResources()).IsEqualTo(local);
    }

    [Test]
    [Arguments("success")]
    [Arguments("force_stopped")]
    [Arguments("runtime_lost")]
    [Arguments("unavailable")]
    [Arguments("cancelled")]
    public async Task AbandonmentCommitsBeforeNativeStopAndEveryDispatchedOutcomeStaysQuarantined(string outcome)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var submission = await store.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport());
        // Known-run stop must work from retained identity with source/workspace/configuration unavailable.
        Directory.Delete(attempt.Allocation.WorktreePath, true);
        Directory.Delete(fixture.Home);
        Directory.Move(fixture.Git.Repository, fixture.Git.Repository + "-offline");
        var stops = 0;
        var transport = new StopTransport(async run =>
        {
            var id = run.RunId;
            using var observer = fixture.Git.State.Open();
            await Assert.That(observer.GetAttempt(attempt.AttemptId).Abandonment!.Reason).IsEqualTo("operator stop");
            await Assert.That(observer.CurrentAttempt(attempt.WorkUnitId)).IsNull();
            // The exact retained binding, including frozen title/size, not adapter configuration.
            await Assert.That(run).IsEqualTo(new NativeRunBinding(submission.Locator, submission.RunId!,
                "Broodling Attempt " + attempt.AttemptId, "small", null));
            stops++;
            if (outcome == "unavailable") throw new NativeTransportError();
            if (outcome == "cancelled") throw new OperationCanceledException();
            return new(id, outcome == "success", JsonSerializer.SerializeToElement<object?>(null), outcome == "success" ? null : outcome);
        });
        if (outcome == "unavailable")
            await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "operator stop", transport)).Throws<NativeTransportError>();
        else if (outcome == "cancelled")
            await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "operator stop", transport)).Throws<OperationCanceledException>();
        else
            await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "operator stop", transport)).Throws<CessationUnconfirmed>();
        await Assert.That(stops).IsEqualTo(1);
        await Assert.That(store.FindRetirement(attempt.AttemptId)).IsNull();
        await Assert.That(store.Status(attempt.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(attempt.AttemptId);
        await Assert.That(() => store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
        await Assert.That(() => store.AdmitRetry(attempt.AttemptId, "unsafe", fixture.Git.Workspaces, fixture.Profile)).Throws<AttemptAdmissionError>();
        await Assert.That(store.AbandonAttempt(attempt.AttemptId, "late reason").Reason).IsEqualTo("operator stop");
    }

    [Test]
    public async Task UnresolvedDispatchNeverReplaysToDiscoverRunOrGrantCleanup()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        var transport = new ControlledTransport { Submit = _ => throw new NativeTransportError() };
        await Assert.That(async () => await store.DispatchAsync(attempt.AttemptId, fixture.Profile, transport)).Throws<NativeTransportError>();
        await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "unresolved", transport)).Throws<CessationUnconfirmed>();
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(store.FindSubmission(attempt.AttemptId)!.RunId).IsNull();
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<StaleAttempt>();
        await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsTrue();
    }

    [Test]
    [Arguments("missing-enclosure")]
    [Arguments("foreign-marker")]
    [Arguments("physical-alias")]
    public async Task DispatchedStopPreservesPhysicalEnclosureOwnershipRefusalsAfterAbandonment(string change)
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        await store.DispatchAsync(attempt.AttemptId, fixture.Profile, new ControlledTransport());
        var enclosure = attempt.Allocation.Enclosure;
        if (change == "missing-enclosure") Directory.Delete(enclosure, true);
        else if (change == "foreign-marker") File.WriteAllText(Path.Combine(enclosure, WorktreeMaterialization.MarkerName), "foreign");
        else { Directory.Move(enclosure, enclosure + "-saved"); Directory.CreateSymbolicLink(enclosure, enclosure + "-saved"); }
        if (change == "missing-enclosure")
            await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "stop", new ControlledTransport())).Throws<CessationUnconfirmed>();
        else
            await Assert.That(async () => await store.StopAsync(attempt.AttemptId, "stop", new ControlledTransport())).Throws<WorktreeOwnershipConflict>();
        await Assert.That(store.GetAttempt(attempt.AttemptId).Abandonment).IsNotNull();
        await Assert.That(store.FindRetirement(attempt.AttemptId)).IsNull();
    }

    [Test]
    public async Task DirtySafeCheckoutIsDiscardedWhileSourceSiblingsStoreEnclosureAndLockInodeSurvive()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = store.ProvisionAttempt(fixture.Admit(store).AttemptId);
        var sibling = Path.Combine(fixture.State.Root, "sibling");
        fixture.Git("worktree", "add", "-b", "sibling", sibling);
        File.WriteAllText(Path.Combine(attempt.Allocation.WorktreePath, "discard-me"), "candidate only");
        File.WriteAllText(Path.Combine(attempt.Allocation.WorktreePath, "original.txt"), "dirty abandoned bytes");
        var lockFd = open(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName), 0x80002);
        if (lockFd < 0) throw new Exception("Cannot open existing lock");
        try
        {
            await store.StopAsync(attempt.AttemptId, "discard", new ControlledTransport());
            store.RetireAttempt(attempt.AttemptId);
            await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsFalse();
            await Assert.That(Directory.Exists(attempt.Allocation.Enclosure)).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.MarkerName))).IsEqualTo(attempt.AttemptId + "\n");
            await Assert.That(File.Exists(fixture.State.Path)).IsTrue();
            await Assert.That(AttemptFixture.RunGit(sibling, "rev-parse", "HEAD").Trim()).IsEqualTo(fixture.Head);
            await Assert.That(fixture.Git("rev-parse", "HEAD").Trim()).IsEqualTo(fixture.Head);
            await Assert.That(fixture.Git("branch", "--list", attempt.Allocation.Branch)).IsEqualTo("");
            // Lock the original open inode. Opening the retained pathname must contend with it.
            await Assert.That(flock(lockFd, 6)).IsEqualTo(0);
            await Assert.That(ProvisioningProcessTests.LockIsFree(attempt)).IsFalse();
        }
        finally { close(lockFd); }
    }

    [Test]
    [Arguments("marker")]
    [Arguments("foreign-branch")]
    [Arguments("branch-elsewhere")]
    [Arguments("symbolic-branch")]
    [Arguments("unregistered")]
    [Arguments("alias")]
    [Arguments("foreign-common-git")]
    public async Task RetirementRefusesChangedPhysicalAndGitOwnershipBeforeDeletion(string change)
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = store.ProvisionAttempt(fixture.Admit(store).AttemptId);
        await store.StopAsync(attempt.AttemptId, "stop", new ControlledTransport());
        var path = attempt.Allocation.WorktreePath;
        switch (change)
        {
            case "marker": File.WriteAllText(Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.MarkerName), "foreign"); break;
            case "foreign-branch": AttemptFixture.RunGit(path, "checkout", "-b", "foreign"); break;
            case "branch-elsewhere":
                AttemptFixture.RunGit(path, "checkout", "--detach");
                fixture.Git("worktree", "add", Path.Combine(fixture.State.Root, "foreign-checkout"), attempt.Allocation.Branch); break;
            case "symbolic-branch": fixture.Git("symbolic-ref", "refs/heads/" + attempt.Allocation.Branch, "refs/heads/main"); break;
            case "unregistered":
                Directory.Move(path, path + "-saved"); fixture.Git("worktree", "prune"); Directory.Move(path + "-saved", path); break;
            case "alias": Directory.Move(path, path + "-saved"); Directory.CreateSymbolicLink(path, path + "-saved"); break;
            case "foreign-common-git": File.WriteAllText(Path.Combine(path, ".git"), "gitdir: " + fixture.GitDirectory + "\n"); break;
        }
        await Assert.That(() => store.RetireAttempt(attempt.AttemptId)).Throws<WorktreeOwnershipConflict>();
        await Assert.That(Directory.Exists(path)).IsTrue();
        await Assert.That(store.FindRetirement(attempt.AttemptId)!.RetiredAt).IsNull();
        await Assert.That(fixture.Git("rev-parse", "HEAD").Trim()).IsEqualTo(fixture.Head);
    }

    [Test]
    public async Task SqlRetainsSafeProofAndCannotManufactureDispatchedCleanupAuthority()
    {
        using var fixture = new NativeFixture();
        using var store = fixture.Git.State.Open();
        var attempt = fixture.Provision(store);
        await store.StopAsync(attempt.AttemptId, "safe", new ControlledTransport());
        foreach (var sql in new[] {
            "UPDATE attempt_retirements SET basis = 'never_materialized'",
            "DELETE FROM attempt_retirements",
            "INSERT OR REPLACE INTO attempt_retirements SELECT * FROM attempt_retirements" })
            await Assert.That(() => fixture.Git.State.Execute(sql)).Throws<SqliteException>();
        store.RetireAttempt(attempt.AttemptId);
        await Assert.That(() => fixture.Git.State.Execute("UPDATE attempt_retirements SET retired_at = NULL")).Throws<SqliteException>();
        var successor = store.AdmitRetry(attempt.AttemptId, "explicit", fixture.Git.Workspaces, fixture.Profile);
        store.ProvisionAttempt(successor.AttemptId);
        await store.DispatchAsync(successor.AttemptId, fixture.Profile, new ControlledTransport());
        store.AbandonAttempt(successor.AttemptId, "dispatched");
        await Assert.That(() => fixture.Git.State.Execute($"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at) VALUES ('{successor.AttemptId}', 'never_dispatched', 'now')"))
            .Throws<SqliteException>();
        // Verified maintenance is DirectTarget-only; dispatched LocalTarget work keeps its policy.
        store.PauseInstallation();
        await Assert.That(() => store.RetireStoppedTargetAttempt(successor.AttemptId, Check(HttpFixture.Target))).Throws<CessationUnconfirmed>();
        await Assert.That(() => fixture.Git.State.Execute(StoppedTargetInsert(successor.AttemptId))).Throws<SqliteException>();
    }

    [DllImport("libc")] private static extern int open(string path, int flags);
    [DllImport("libc")] private static extern int flock(int fd, int flags);
    [DllImport("libc")] private static extern int close(int fd);

    [Test]
    [Arguments("matching")]
    [Arguments("foreign")]
    [Arguments("unknown")]
    public async Task DispatchedHttpStopForcesTheIntendedRunOnlyAfterAMatchingStatus(string precheck)
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "dispatched");
        var run = submission.Frozen.Run(submission.IntendedRunId!);
        if (precheck == "unknown") target.Reply = NotFound;
        else target.Projections.Enqueue(DirectTargetSessionTests.Running(precheck == "foreign" ? run with { Title = "Another run" } : run));
        target.Projections.Enqueue(HttpForceStopped(run));
        fixture.Store.PauseInstallation(); // Stop remains available while paused.

        var refusal = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator stop", null));
        await Assert.That(refusal.NativeStopRequested).IsEqualTo(precheck == "matching");
        await Assert.That(target.Count("run/force")).IsEqualTo(precheck == "matching" ? 1 : 0);
        if (precheck == "matching")
            await Assert.That((string)target.Messages.Last()["params"]!["runId"]!).IsEqualTo(submission.IntendedRunId);
        await HttpQuarantined(fixture, "dispatched", "operator stop");
    }

    [Test]
    public async Task CorrelatedHttpStopForcesTheConfirmedRunWithoutPrecheck()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        target.Projections.Enqueue(HttpForceStopped(submission.Run!));

        var refusal = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator stop", null));
        await Assert.That(refusal.NativeStopRequested).IsTrue();
        await Assert.That(target.Count("run/status")).IsEqualTo(0);
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await HttpQuarantined(fixture, "correlated", "operator stop");
    }

    [Test]
    public async Task UnansweredHttpForceIsAnUncertainTimeoutAfterCommittedAbandonment()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin, "correlated");
        target.Projections.Enqueue(null);
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;

        var stop = fixture.Store.StopAsync(fixture.Attempt.AttemptId, "operator stop", null);
        await target.Stalled.Task.WaitAsync(DirectTargetSessionTests.Patience);
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment).IsNotNull();
        clock.Advance(DirectTargetLimits.Stop);
        await DirectTargetSessionTests.Fails(() => stop, "TimeoutError");
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await HttpQuarantined(fixture, "correlated", "operator stop");
    }

    [Test]
    public async Task RepeatedHttpStopCanForceARunThatWasUnknownAtFirst()
    {
        await using var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "dispatched");
        var run = submission.Frozen.Run(submission.IntendedRunId!);
        target.Reply = NotFound;
        var first = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "first stop", null));
        await Assert.That(first.NativeStopRequested).IsFalse();

        // The delayed request is accepted later; the same intended ID is now addressable.
        target.Reply = (_, _) => null;
        target.Projections.Enqueue(DirectTargetSessionTests.Running(run));
        target.Projections.Enqueue(HttpForceStopped(run));
        var second = await Refusal<CessationUnconfirmed>(() => fixture.Store.StopAsync(fixture.Attempt.AttemptId, "second stop", null));
        await Assert.That(second.NativeStopRequested).IsTrue();
        await Assert.That(target.Count("run/force")).IsEqualTo(1);
        await HttpQuarantined(fixture, "dispatched", "first stop");
    }

    [Test]
    public async Task IssueCancellationAbandonsTheHttpAttemptBeforeContactingTheTarget()
    {
        await using var target = new StockTarget { StallAt = "discovery" };
        using var fixture = new HttpFixture();
        fixture.PrepareAt(target.Origin, "dispatched");
        var submission = fixture.Store.SubmitIssue("https://github.com/acme/widget/issues/12");
        fixture.Store.AssociateIssueSubmission(submission.SubmissionId, fixture.Attempt.ContractRevisionId);
        var clock = new FakeTimeProvider();
        fixture.Store.DirectTargetClock = clock;

        var cancel = fixture.Store.CancelIssueSubmissionAsync(submission.SubmissionId, "requester withdrew", null);
        await target.Stalled.Task.WaitAsync(DirectTargetSessionTests.Patience);
        await Assert.That(fixture.Store.GetAttempt(fixture.Attempt.AttemptId).Abandonment!.Reason).IsEqualTo("requester withdrew");
        clock.Advance(DirectTargetLimits.Stop);
        var refusal = await Refusal<CessationUnconfirmed>(() => cancel);
        await Assert.That(refusal.NativeStopRequested).IsFalse();
        var cancelled = fixture.Store.GetIssueSubmission(submission.SubmissionId);
        await Assert.That(cancelled.State).IsEqualTo("cancelled");
        await Assert.That(cancelled.Cancellation!.AttemptId).IsEqualTo(fixture.Attempt.AttemptId);
        await HttpQuarantined(fixture, "dispatched", "requester withdrew");
    }

    [Test]
    [Arguments("dispatched")]
    [Arguments("correlated")]
    public async Task VerifiedStoppedTargetRetiresAbandonedDispatchedHttpWorkWithoutResolvingOrReplacingIt(string state)
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), state);
        var id = fixture.Attempt.AttemptId;
        fixture.Store.AbandonAttempt(id, "operator stop");
        var local = fixture.Git.LocalResources();
        await Assert.That(() => fixture.Git.State.Execute(StoppedTargetInsert(id))).Throws<SqliteException>(); // SQL requires the pause.
        fixture.Store.PauseInstallation();
        var check = Check(submission.Locator.Address);
        check = check with { VerifiedAt = check.VerifiedAt.ToOffset(TimeSpan.FromHours(-4)) };

        var retired = fixture.Store.RetireStoppedTargetAttempt(id, check);
        await Assert.That(retired.Basis).IsEqualTo("stopped_target");
        await Assert.That(retired.StoppedTarget).IsEqualTo(check);
        await Assert.That(retired.CeasedAt).IsEqualTo(check.VerifiedAt.ToUniversalTime().ToString("O")); // The same UTC form as other retained times.
        await Assert.That(retired.RetiredAt).IsNotNull();
        using var reopened = fixture.Git.State.Open();
        // The retained retirement is returned as recorded; a later check is neither needed nor recorded.
        await Assert.That(reopened.RetireStoppedTargetAttempt(id, Check(submission.Locator.Address))).IsEqualTo(retired);
        await Assert.That(await reopened.StopAsync(id, "later stop", null)).IsEqualTo(retired);
        var status = reopened.Status(fixture.Attempt.ContractRevisionId);
        await Assert.That(status.Attempts.Single().Retirement).IsEqualTo(retired);
        await Assert.That(status.Attempts.Single().Abandonment!.Reason).IsEqualTo("operator stop");
        await Assert.That(status.QuarantinedAttemptIds.Count).IsEqualTo(0);
        // Retirement resolves no acceptance uncertainty and grants no replacement.
        await Assert.That(reopened.FindSubmission(id)).IsEqualTo(submission);
        await Assert.That(reopened.GetInstallationStatus().UnresolvedDispatches).IsEqualTo(state == "dispatched" ? 1 : 0);
        await Assert.That(() => reopened.AdmitRetry(id, "replacement")).Throws<CessationUnconfirmed>();
        await Assert.That(fixture.Git.LocalResources()).IsEqualTo(local);
        await Assert.That(fixture.Git.Git("rev-parse", fixture.Attempt.B1.RetentionRef).Trim()).IsEqualTo(fixture.Attempt.B1.CommitOid);
    }

    [Test]
    public async Task VerifiedMaintenanceRetiresASuccessfulHttpAttemptWithoutAbandoningItsResult()
    {
        var target = new StockTarget();
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(target.Origin, "correlated");
        var accepted = fixture.Git.Deliver();
        target.Projections.Enqueue(AttemptCompletionTests.HttpFinished(submission, "succeeded", CompletionFixture.Receipt(head: accepted)));
        var completion = await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null);
        await target.DisposeAsync();
        fixture.Store.PauseInstallation();
        // A successful result whose accepted pin is gone is an unresolved retention condition.
        fixture.Git.Git("update-ref", "-d", "refs/broodling/accepted/" + accepted);
        await Assert.That(() => fixture.Store.RetireStoppedTargetAttempt(fixture.Attempt.AttemptId, Check(submission.Locator.Address)))
            .Throws<ResultRetentionError>();
        fixture.Git.Git("update-ref", "refs/broodling/accepted/" + accepted, accepted);

        var retired = fixture.Store.RetireStoppedTargetAttempt(fixture.Attempt.AttemptId, Check(submission.Locator.Address));
        await Assert.That(retired.Basis).IsEqualTo("stopped_target");
        var status = fixture.Store.Status(fixture.Attempt.ContractRevisionId);
        await Assert.That(status.Attempts.Single().Retirement).IsEqualTo(retired);
        await Assert.That(status.Attempts.Single().Abandonment).IsNull();
        await Assert.That(status.Completions.Single()).IsEqualTo(completion);
        await Assert.That(await fixture.Store.WaitAsync(fixture.Attempt.AttemptId, null)).IsEqualTo(completion);
        await Assert.That(fixture.Git.Git("rev-parse", "refs/broodling/accepted/" + accepted).Trim()).IsEqualTo(accepted);
    }

    [Test]
    [Arguments("unpaused")]
    [Arguments("check-before-pause")]
    [Arguments("future-check")]
    [Arguments("foreign-target")]
    [Arguments("initiating")]
    [Arguments("current")]
    [Arguments("missing-b1")]
    public async Task MaintenanceRetirementRefusesUnlessEveryCurrentConditionHolds(string condition)
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), "correlated");
        var id = fixture.Attempt.AttemptId;
        // A current Attempt stays refused whatever its native label; the others are abandoned.
        if (condition != "current") fixture.Store.AbandonAttempt(id, "operator stop");
        fixture.Store.PauseInstallation();
        var check = Check(submission.Locator.Address);
        // A check made before a release and re-pause belongs to the earlier pause epoch.
        if (condition == "check-before-pause") { fixture.Store.ReleaseInstallation(); fixture.Store.PauseInstallation(); }
        if (condition == "unpaused") fixture.Store.ReleaseInstallation();
        // A future check would otherwise also satisfy any later pause.
        if (condition == "future-check") check = check with { VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        if (condition == "foreign-target") check = check with { DirectOrigin = "http://127.0.0.1:10" };
        if (condition == "missing-b1") fixture.Git.Git("update-ref", "-d", fixture.Attempt.B1.RetentionRef);
        using var initiating = condition == "initiating"
            ? AdministrativeGitProcess.EnclosureLock.AcquireExisting(fixture.Git.State.Path, shared: true) : null;

        BroodlingException? refusal = null;
        try { fixture.Store.RetireStoppedTargetAttempt(id, check); }
        catch (BroodlingException error) { refusal = error; }
        await Assert.That(refusal?.Code).IsEqualTo(condition switch
        {
            "current" => "cessation_unconfirmed", "missing-b1" => "submission_not_ready", _ => "maintenance_unverified"
        });
        await Assert.That(fixture.Store.FindRetirement(id)).IsNull();
        await Assert.That(fixture.Store.Status(fixture.Attempt.ContractRevisionId).QuarantinedAttemptIds.Single()).IsEqualTo(id);
    }

    [Test]
    public async Task RetireAttemptCommandRefusesAnIncompleteOrLooseCheckThenRecordsTheSuppliedOne()
    {
        using var fixture = new HttpFixture();
        var submission = fixture.PrepareAt(new Uri(HttpFixture.Target), "dispatched");
        var id = fixture.Attempt.AttemptId;
        fixture.Store.AbandonAttempt(id, "operator stop");
        fixture.Store.PauseInstallation();
        var check = Check(submission.Locator.Address);
        var json = JsonSerializer.SerializeToNode(check, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        var incomplete = json.DeepClone().AsObject();
        incomplete["homeMount"] = " ";
        var home = json["homeMount"]!.ToJsonString();
        var (path, application) = (fixture.Git.State.Path, fixture.Git.State.Application);

        var output = new StringWriter();
        var error = new StringWriter();
        // A blank member reaches the operation's own completeness guard.
        await Assert.That(StoreCommands.Run(["retire-attempt", path, id, incomplete.ToJsonString()], application, output, error)).IsEqualTo(1);
        await Assert.That((string)JsonNode.Parse(error.ToString())!["error"]!).IsEqualTo("maintenance_unverified");
        // The parser accepts each member once and exactly spelled.
        foreach (var loose in new[] { json.ToJsonString().Replace("\"homeMount\"", "\"HomeMount\""), json.ToJsonString()[..^1] + ",\"homeMount\":" + home + "}" })
            await Assert.That(StoreCommands.Run(["retire-attempt", path, id, loose], application, output, error)).IsEqualTo(1);
        await Assert.That(fixture.Store.FindRetirement(id)).IsNull();
        await Assert.That(StoreCommands.Run(["retire-attempt", path, id, json.ToJsonString()], application, output, error)).IsEqualTo(0);
        await Assert.That((string)JsonNode.Parse(output.ToString())!["basis"]!).IsEqualTo("stopped_target");
        await Assert.That(fixture.Store.FindRetirement(id)!.StoppedTarget).IsEqualTo(check);
        // The stop handback reports the verified retirement, not quarantine or pending retirement.
        output = new StringWriter();
        await Assert.That(await InvocationCommands.RunAsync(["stop", path, id, "later stop"], application, output, error)).IsEqualTo(0);
        var stop = JsonNode.Parse(output.ToString())!;
        await Assert.That((bool)stop["quarantined"]!).IsFalse();
        await Assert.That((string)stop["message"]!).IsEqualTo("Attempt abandoned and retired under verified stopped-target maintenance.");
    }

    private static StoppedTargetCheck Check(string origin) =>
        new(origin, "broodling-target", "/srv/broodling/target-state", "/srv/broodling/target-home", DateTimeOffset.UtcNow);

    private static string StoppedTargetInsert(string attemptId) =>
        $"INSERT INTO attempt_retirements (attempt_id, basis, ceased_at, stopped_target_json) VALUES ('{attemptId}', 'stopped_target', 'now', '{{}}')";

    private static string? NotFound(JsonObject request, string id) => (string)request["method"]! != "run/status" ? null
        : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject
            { ["code"] = -32000, ["message"] = "run was not found", ["data"] = new JsonObject { ["code"] = "NOT_FOUND" } } }.ToJsonString();

    private static JsonObject HttpForceStopped(NativeRunBinding run) => DirectTargetSessionTests.Projection(new JsonObject
    {
        ["phase"] = "finished", ["terminalResult"] = new JsonObject { ["status"] = "failed", ["reason"] = "force_stopped" }
    }, run);

    private static async Task<T> Refusal<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    /// <summary>Abandonment stays committed, stop output never correlates, and dispatch intent keeps the Attempt quarantined.</summary>
    private static async Task HttpQuarantined(HttpFixture fixture, string state, string reason)
    {
        var attempt = fixture.Store.GetAttempt(fixture.Attempt.AttemptId);
        await Assert.That(attempt.Abandonment!.Reason).IsEqualTo(reason);
        await Assert.That(fixture.Store.FindSubmission(attempt.AttemptId)!.State).IsEqualTo(state);
        await Assert.That(fixture.Store.FindRetirement(attempt.AttemptId)).IsNull();
        await Assert.That(() => fixture.Store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
    }
}

/// <summary>Stop needs only the stopper: it can never redispatch, wait or read status.</summary>
internal sealed class StopTransport(Func<NativeRunBinding, Task<NativeResult>> stop) : INativeStopper
{
    public Task<NativeResult> StopAsync(NativeRunBinding run, CancellationToken cancellationToken = default) => stop(run);
}
