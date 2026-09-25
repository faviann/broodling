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
        await Assert.That(store.FindRetirement(attempt.AttemptId)).IsNull();
        await Assert.That(() => store.RetireAttempt(attempt.AttemptId)).Throws<CessationUnconfirmed>();
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
        var transport = new ControlledTransport { Submit = (_, _) => throw new NativeTransportError() };
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
        await Assert.That(() => fixture.Git.State.Execute($"INSERT INTO attempt_retirements VALUES ('{successor.AttemptId}', 'never_dispatched', 'now', NULL)"))
            .Throws<SqliteException>();
    }

    [DllImport("libc")] private static extern int open(string path, int flags);
    [DllImport("libc")] private static extern int flock(int fd, int flags);
    [DllImport("libc")] private static extern int close(int fd);
}

/// <summary>Stop needs only the stopper: it can never redispatch, wait or read status.</summary>
internal sealed class StopTransport(Func<NativeRunBinding, Task<NativeResult>> stop) : INativeStopper
{
    public Task<NativeResult> StopAsync(NativeRunBinding run, CancellationToken cancellationToken = default) => stop(run);
}
