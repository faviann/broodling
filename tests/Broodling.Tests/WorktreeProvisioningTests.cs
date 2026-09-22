using Microsoft.Data.Sqlite;
using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class WorktreeProvisioningTests
{
    [Test]
    public async Task InterruptedMarkerPublicationConvergesButForeignPendingMaterialRefuses()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        Directory.CreateDirectory(attempt.Allocation.Enclosure);
        var pending = System.IO.Path.Combine(attempt.Allocation.Enclosure, ".broodling-marker-pending");
        File.WriteAllText(pending, "foreign bytes");
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<WorktreeOwnershipConflict>();
        await Assert.That(File.ReadAllText(pending)).IsEqualTo("foreign bytes");
        File.WriteAllText(pending, attempt.AttemptId[..20]);
        var result = store.ProvisionAttempt(attempt.AttemptId);
        await Assert.That(result.Provision).IsNotNull();
        await Assert.That(File.Exists(pending)).IsFalse();
        await Assert.That(File.ReadAllText(System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.MarkerName))).IsEqualTo(attempt.AttemptId + "\n");
    }

    [Test]
    public async Task OriginalB1IsMaterializedAndOwnedCandidateReplayPreservesLaterWork()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        fixture.Commit("current HEAD is not B1\n");
        fixture.Git("update-ref", "-d", attempt.B1.RetentionRef);
        var provisioned = store.ProvisionAttempt(attempt.AttemptId);
        var path = provisioned.Allocation.WorktreePath;
        await Assert.That(provisioned.Provision).IsNotNull();
        await Assert.That(AttemptFixture.RunGit(path, "rev-parse", "HEAD").Trim()).IsEqualTo(fixture.Head);
        await Assert.That(File.ReadAllText(System.IO.Path.Combine(path, "original.txt"))).IsEqualTo("original selected bytes\n");
        await Assert.That(AttemptFixture.RunGit(path, "status", "--porcelain")).IsEqualTo("");
        await Assert.That(fixture.Git("rev-parse", attempt.B1.RetentionRef).Trim()).IsEqualTo(fixture.Head);
        await Assert.That(File.Exists(System.IO.Path.Combine(path, WorktreeMaterialization.MarkerName))).IsFalse();
        File.WriteAllText(System.IO.Path.Combine(path, "original.txt"), "candidate progress\n");
        AttemptFixture.RunGit(path, "commit", "-am", "candidate advanced");
        var advanced = AttemptFixture.RunGit(path, "rev-parse", "HEAD");
        File.WriteAllText(System.IO.Path.Combine(path, "untracked.txt"), "keep me");
        using var reopened = fixture.State.Open();
        await Assert.That(reopened.ProvisionAttempt(attempt.AttemptId)).IsEqualTo(provisioned);
        await Assert.That(AttemptFixture.RunGit(path, "rev-parse", "HEAD")).IsEqualTo(advanced);
        await Assert.That(File.ReadAllText(System.IO.Path.Combine(path, "untracked.txt"))).IsEqualTo("keep me");
        await Assert.That(reopened.Status(fixture.RevisionId).Attempts.Single()).IsEqualTo(provisioned);
        await Assert.That(reopened.History(ContractIngressTests.Reference).Single().Attempts.Single()).IsEqualTo(provisioned);
    }

    [Test]
    public async Task BranchOnlyAndLostAcknowledgmentConvergeAndMissingOwnedCheckoutRebuildsAtB1()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        WorktreeMaterialization.ClaimEnclosure(attempt);
        fixture.Git("branch", attempt.Allocation.Branch, attempt.B1.CommitOid);
        fixture.State.Execute("CREATE TRIGGER fail_provision BEFORE INSERT ON worktree_provisions BEGIN SELECT RAISE(ABORT, 'injected acknowledgment failure'); END;");
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<SqliteException>();
        await Assert.That(store.GetAttempt(attempt.AttemptId).Provision).IsNull();
        await Assert.That(File.Exists(System.IO.Path.Combine(attempt.Allocation.WorktreePath, "original.txt"))).IsTrue();
        fixture.State.Execute("DROP TRIGGER fail_provision");
        var first = store.ProvisionAttempt(attempt.AttemptId);
        fixture.Commit("later source bytes\n");
        Directory.Delete(attempt.Allocation.WorktreePath, true);
        await Assert.That(store.ProvisionAttempt(attempt.AttemptId)).IsEqualTo(first);
        await Assert.That(AttemptFixture.RunGit(attempt.Allocation.WorktreePath, "rev-parse", "HEAD").Trim()).IsEqualTo(fixture.Head);
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(1);
        await Assert.That(fixture.Git("worktree", "list", "--porcelain").Split('\n').Count(line => line.StartsWith("worktree "))).IsEqualTo(2);
    }

    [Test]
    [Arguments("unmarked-path")]
    [Arguments("foreign-marker")]
    [Arguments("marker-symlink")]
    [Arguments("branch-tip")]
    [Arguments("branch-elsewhere")]
    [Arguments("branch-symbolic")]
    [Arguments("candidate-symlink")]
    [Arguments("root-symlink")]
    [Arguments("common-symlink")]
    [Arguments("lock-symlink")]
    [Arguments("foreign-common")]
    [Arguments("detached")]
    public async Task ForeignOwnershipIsRefusedWithoutAdoption(string kind)
    {
        using var fixture = new AttemptFixture();
        using var foreign = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        var a = attempt.Allocation;
        var sentinel = System.IO.Path.Combine(foreign.Repository, "sentinel");
        File.WriteAllText(sentinel, "foreign bytes");
        if (kind != "unmarked-path") WorktreeMaterialization.ClaimEnclosure(attempt);
        switch (kind)
        {
            case "unmarked-path": Directory.CreateDirectory(a.WorktreePath); File.WriteAllText(System.IO.Path.Combine(a.WorktreePath, "foreign"), "retain"); break;
            case "foreign-marker": File.WriteAllText(System.IO.Path.Combine(a.Enclosure, WorktreeMaterialization.MarkerName), "foreign\n"); break;
            case "marker-symlink":
                File.Delete(System.IO.Path.Combine(a.Enclosure, WorktreeMaterialization.MarkerName));
                File.CreateSymbolicLink(System.IO.Path.Combine(a.Enclosure, WorktreeMaterialization.MarkerName), sentinel); break;
            case "branch-tip": fixture.Git("branch", a.Branch, fixture.Commit("foreign branch\n")); break;
            case "branch-elsewhere": fixture.Git("worktree", "add", "-b", a.Branch, System.IO.Path.Combine(fixture.State.Root, "foreign-tree"), fixture.Head); break;
            case "branch-symbolic": fixture.Git("symbolic-ref", "refs/heads/" + a.Branch, "refs/heads/main"); break;
            case "candidate-symlink": Directory.CreateSymbolicLink(a.WorktreePath, foreign.Repository); break;
            case "root-symlink":
                Directory.Move(a.WorkspaceRoot, a.WorkspaceRoot + "-moved");
                Directory.CreateSymbolicLink(a.WorkspaceRoot, a.WorkspaceRoot + "-moved"); break;
            case "common-symlink":
                Directory.Move(fixture.GitDirectory, fixture.GitDirectory + "-moved");
                Directory.CreateSymbolicLink(fixture.GitDirectory, fixture.GitDirectory + "-moved"); break;
            case "lock-symlink": File.CreateSymbolicLink(System.IO.Path.Combine(a.Enclosure, WorktreeMaterialization.LockName), sentinel); break;
            case "foreign-common":
                store.ProvisionAttempt(attempt.AttemptId);
                File.WriteAllText(System.IO.Path.Combine(a.WorktreePath, ".git"), "gitdir: " + foreign.GitDirectory + "\n"); break;
            case "detached":
                store.ProvisionAttempt(attempt.AttemptId);
                AttemptFixture.RunGit(a.WorktreePath, "checkout", "--detach"); break;
        }
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<BroodlingException>();
        await Assert.That(File.ReadAllText(sentinel)).IsEqualTo("foreign bytes");
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task EnclosureCannotEncloseAnAlreadyOpenDurableStore()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        var nestedPath = System.IO.Path.Combine(attempt.Allocation.Enclosure, "state.sqlite3");
        // A store opened before a marker existed must still be refused at provisioning.
        Directory.CreateDirectory(attempt.Allocation.Enclosure);
        File.Copy(fixture.State.Path, nestedPath);
        // Backup via SQLite includes WAL facts and avoids testing a partial copy.
        using (var source = fixture.State.Connect())
        using (var target = new SqliteConnection($"Data Source={nestedPath};Pooling=False"))
        { target.Open(); source.BackupDatabase(target); }
        using var nested = fixture.State.Application.OpenStore(nestedPath);
        await Assert.That(() => nested.ProvisionAttempt(attempt.AttemptId)).Throws<WorktreeOwnershipConflict>();
        await Assert.That(File.Exists(nestedPath)).IsTrue();
        await Assert.That(Directory.Exists(attempt.Allocation.WorktreePath)).IsFalse();
    }

    [Test]
    public async Task ProvisioningRechecksCheckoutPolicyAndSuppressesHooks()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        var hook = System.IO.Path.Combine(fixture.GitDirectory, "hooks", "post-checkout");
        var canary = System.IO.Path.Combine(fixture.State.Root, "hook-ran");
        File.WriteAllText(hook, $"#!/bin/sh\ntouch '{canary}'\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        fixture.Git("config", "filter.test.smudge", "touch " + canary);
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<UnsupportedStartingState>();
        fixture.Git("config", "--unset", "filter.test.smudge");
        var attributes = System.IO.Path.Combine(fixture.GitDirectory, "info", "attributes");
        File.WriteAllText(attributes, "original.txt ident\n");
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<UnsupportedStartingState>();
        File.Delete(attributes);
        await Assert.That(store.GetAttempt(attempt.AttemptId).Provision).IsNull();
        store.ProvisionAttempt(attempt.AttemptId);
        await Assert.That(File.Exists(canary)).IsFalse();
    }

    [Test]
    public async Task AbandonedAuthorityCannotMaterializeOrGainAcknowledgment()
    {
        using var fixture = new AttemptFixture();
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store);
        store.AbandonAttempt(attempt.AttemptId, "ended");
        await Assert.That(() => store.ProvisionAttempt(attempt.AttemptId)).Throws<StaleAttempt>();
        await Assert.That(Directory.Exists(attempt.Allocation.Enclosure)).IsFalse();
        await Assert.That(() => fixture.State.Execute($"INSERT INTO worktree_provisions VALUES ('{attempt.AttemptId}', 'now')")).Throws<SqliteException>();
    }
}
