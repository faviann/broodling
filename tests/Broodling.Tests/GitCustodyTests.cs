using TUnit.Assertions;
using TUnit.Core;

namespace Broodling.Tests;

public sealed class GitCustodyTests
{
    [Test]
    [Arguments("unstaged")]
    [Arguments("staged")]
    [Arguments("untracked")]
    public async Task DirtyStartingMaterialIsNamedAndLeavesNoAttempt(string kind)
    {
        using var fixture = new AttemptFixture();
        var name = kind == "untracked" ? "untracked material.txt" : "original.txt";
        File.WriteAllText(System.IO.Path.Combine(fixture.Repository, name), "uncommitted bytes");
        if (kind == "staged") fixture.Git("add", name);
        using var store = fixture.State.Open();
        try { fixture.Admit(store); throw new InvalidOperationException("Expected refusal"); }
        catch (UnsupportedStartingState error)
        {
            await Assert.That(error.Message.Contains(name, StringComparison.Ordinal)).IsTrue();
        }
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("core.autocrlf", "true")]
    [Arguments("core.fsmonitor", "true")]
    [Arguments("core.sparseCheckout", "true")]
    [Arguments("core.symlinks", "false")]
    [Arguments("core.eol", "crlf")]
    [Arguments("includeIf.onbranch:future.path", "/nonexistent/config")]
    public async Task UnsupportedCheckoutConfigurationRefusesBeforeAdmission(string key, string value)
    {
        using var fixture = new AttemptFixture();
        fixture.Git("config", key, value);
        using var store = fixture.State.Open();
        await Assert.That(() => fixture.Admit(store)).Throws<UnsupportedStartingState>();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExternalFilterIsRefusedBeforeStatusCouldExecuteIt()
    {
        using var fixture = new AttemptFixture();
        var sentinel = System.IO.Path.Combine(fixture.State.Root, "filter-ran");
        File.WriteAllText(System.IO.Path.Combine(fixture.GitDirectory, "info", "attributes"), "* filter=unsafe\n");
        fixture.Git("config", "filter.unsafe.clean", "touch " + sentinel);
        File.WriteAllText(System.IO.Path.Combine(fixture.Repository, "original.txt"), "dirty\n");
        using var store = fixture.State.Open();
        await Assert.That(() => fixture.Admit(store)).Throws<UnsupportedStartingState>();
        await Assert.That(File.Exists(sentinel)).IsFalse();
    }

    [Test]
    [Arguments("committed")]
    [Arguments("info")]
    public async Task EffectiveTransformationAttributesAreRefusedFromSelectedTreeOrInfo(string location)
    {
        using var fixture = new AttemptFixture();
        var revision = fixture.Head;
        if (location == "committed")
        {
            File.WriteAllText(System.IO.Path.Combine(fixture.Repository, ".gitattributes"), "*.txt text\n");
            fixture.Git("add", ".gitattributes");
            fixture.Git("commit", "-m", "attributes");
            revision = fixture.Git("rev-parse", "HEAD").Trim();
            fixture.Git("reset", "--hard", fixture.Head); // selected revision's attributes, not the current index
        }
        else File.WriteAllText(System.IO.Path.Combine(fixture.GitDirectory, "info", "attributes"), "*.txt ident\n");
        using var store = fixture.State.Open();
        await Assert.That(() => fixture.Admit(store, revision)).Throws<UnsupportedStartingState>();
    }

    [Test]
    [Arguments("symbolic")]
    [Arguments("conflicting")]
    public async Task ExistingRetentionPinsRefuseWithoutRewritingOrAcknowledgingAttempt(string kind)
    {
        using var fixture = new AttemptFixture();
        var other = fixture.Commit("other commit\n");
        var reference = "refs/broodling/starting/" + fixture.Head;
        if (kind == "symbolic") fixture.Git("symbolic-ref", reference, "refs/heads/main");
        else fixture.Git("update-ref", reference, other);
        using var store = fixture.State.Open();
        await Assert.That(() => fixture.Admit(store)).Throws<UnsupportedStartingState>();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
        await Assert.That(fixture.Git("rev-parse", reference).Trim()).IsEqualTo(other);
        if (kind == "symbolic") await Assert.That(fixture.Git("symbolic-ref", reference).Trim()).IsEqualTo("refs/heads/main");
    }

    [Test]
    public async Task MissingSelectedBlobRefusesEvenWithReplacementAndPromisorConfiguration()
    {
        using var fixture = new AttemptFixture();
        var selectedBlob = fixture.Git("rev-parse", fixture.Head + ":original.txt").Trim();
        var newer = fixture.Commit("available replacement bytes\n");
        var replacementBlob = fixture.Git("rev-parse", newer + ":original.txt").Trim();
        fixture.Git("replace", selectedBlob, replacementBlob);
        var sentinel = System.IO.Path.Combine(fixture.State.Root, "fetch-ran");
        fixture.Git("config", "remote.unavailable.url", "ext::touch " + sentinel);
        fixture.Git("config", "remote.unavailable.promisor", "true");
        fixture.Git("config", "remote.unavailable.partialclonefilter", "blob:none");
        fixture.Git("config", "protocol.ext.allow", "always");
        fixture.DeleteObject(selectedBlob);
        using var store = fixture.State.Open();
        await Assert.That(() => fixture.Admit(store)).Throws<UnsupportedStartingState>();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
        await Assert.That(File.Exists(sentinel)).IsFalse();
    }

    [Test]
    public async Task AncestorOnlyObjectLossDoesNotRejectSelectedSnapshot()
    {
        using var fixture = new AttemptFixture();
        var oldTree = fixture.Git("rev-parse", fixture.Head + "^{tree}").Trim();
        var oldBlob = fixture.Git("rev-parse", fixture.Head + ":original.txt").Trim();
        var selected = fixture.Commit("selected complete snapshot\n");
        fixture.DeleteObject(fixture.Head);
        fixture.DeleteObject(oldTree);
        fixture.DeleteObject(oldBlob);
        using var store = fixture.State.Open();
        var attempt = fixture.Admit(store, selected);
        await Assert.That(attempt.B1.CommitOid).IsEqualTo(selected);
        await Assert.That(fixture.Git("cat-file", "blob", selected + ":original.txt")).IsEqualTo("selected complete snapshot\n");
    }

    [Test]
    public async Task OriginalB1SurvivesAllOtherRefsAndReflogsBeingRemovedAndRealGitGc()
    {
        using var fixture = new AttemptFixture();
        AttemptRecord attempt;
        using (var store = fixture.State.Open()) attempt = fixture.Admit(store);
        // An unrelated root leaves original B1 reachable only through Broodling's pin.
        fixture.Git("checkout", "--orphan", "unrelated");
        fixture.Git("rm", "-rf", ".");
        File.WriteAllText(System.IO.Path.Combine(fixture.Repository, "unrelated.txt"), "unrelated\n");
        fixture.Git("add", ".");
        fixture.Git("commit", "-m", "unrelated root");
        fixture.Git("update-ref", "-d", "refs/heads/main");
        fixture.Git("reflog", "expire", "--expire=now", "--all");
        fixture.Git("gc", "--prune=now");
        await Assert.That(fixture.Git("cat-file", "blob", fixture.Head + ":original.txt")).IsEqualTo("original selected bytes\n");
        using var reopened = fixture.State.Open();
        await Assert.That(fixture.Admit(reopened)).IsEqualTo(attempt);
    }

    [Test]
    public async Task RetentionSuppressesReferenceTransactionHooksAndBareRepositoriesAreSupported()
    {
        using var fixture = new AttemptFixture();
        var bare = System.IO.Path.Combine(fixture.State.Root, "bare.git");
        fixture.Git("clone", "--bare", fixture.Repository, bare);
        var sentinel = System.IO.Path.Combine(fixture.State.Root, "hook-ran");
        var hook = System.IO.Path.Combine(bare, "hooks", "reference-transaction");
        ExecutableFile.Write(hook, "#!/bin/sh\ntouch '" + sentinel + "'\nexit 1\n");
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The qualified Git profile requires Linux.");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var store = fixture.State.Open();
        var attempt = store.AdmitAttempt(fixture.RevisionId, bare, fixture.Workspaces);
        await Assert.That(attempt.B1.CommitOid).IsEqualTo(fixture.Head);
        await Assert.That(attempt.B1.Repository).IsEqualTo(bare);
        await Assert.That(File.Exists(sentinel)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task InheritedGitRedirectsAndInjectedConfigurationCannotChangeSelectedCustody()
    {
        using var fixture = new AttemptFixture();
        var variables = new Dictionary<string, string?>
        {
            ["GIT_DIR"] = "/nonexistent/foreign.git", ["GIT_WORK_TREE"] = "/nonexistent/foreign",
            ["GIT_INDEX_FILE"] = "/nonexistent/index", ["GIT_OBJECT_DIRECTORY"] = "/nonexistent/objects",
            ["GIT_CONFIG_COUNT"] = "1", ["GIT_CONFIG_KEY_0"] = "core.autocrlf", ["GIT_CONFIG_VALUE_0"] = "true"
        };
        var original = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (key, value) in variables) Environment.SetEnvironmentVariable(key, value);
            using var store = fixture.State.Open();
            await Assert.That(fixture.Admit(store).B1.CommitOid).IsEqualTo(fixture.Head);
        }
        finally
        {
            foreach (var (key, value) in original) Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Test]
    public async Task WorkspaceRootsRefuseTemporaryRepositoryCommonDirectoryAndDisposableLocationsIncludingLinks()
    {
        using var fixture = new AttemptFixture();
        var disposable = System.IO.Path.Combine(fixture.State.Root, "disposable");
        Directory.CreateDirectory(disposable);
        File.WriteAllText(System.IO.Path.Combine(disposable, ".broodling-disposable-worktree"), "another-attempt");
        var temporaryLink = System.IO.Path.Combine(fixture.State.Root, "temp-link");
        Directory.CreateSymbolicLink(temporaryLink, "/tmp");
        var disposableLink = System.IO.Path.Combine(fixture.State.Root, "disposable-link");
        Directory.CreateSymbolicLink(disposableLink, disposable);
        var sourceLink = System.IO.Path.Combine(fixture.State.Root, "source-link");
        Directory.CreateSymbolicLink(sourceLink, fixture.Repository);
        using var store = fixture.State.Open();
        foreach (var root in new[] { "relative", "/tmp/attempts", "/var/tmp/attempts", "/dev/shm/attempts", "/run/attempts",
            fixture.Repository, fixture.GitDirectory, System.IO.Path.Combine(fixture.Repository, "nested"),
            System.IO.Path.Combine(temporaryLink, "attempts"), System.IO.Path.Combine(disposableLink, "attempts"), sourceLink })
            await Assert.That(() => fixture.Admit(store, root: root)).Throws<UnsupportedWorkspaceRoot>();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MissingSelectedTreeAndNonCommitRevisionsRefuseWithoutAllocation()
    {
        using var fixture = new AttemptFixture();
        var tree = fixture.Git("rev-parse", fixture.Head + "^{tree}").Trim();
        var blob = fixture.Git("rev-parse", fixture.Head + ":original.txt").Trim();
        using var store = fixture.State.Open();
        foreach (var revision in new[] { tree, blob, "--help", "missing-branch" })
            await Assert.That(() => fixture.Admit(store, revision)).Throws<UnsupportedStartingState>();
        fixture.DeleteObject(tree);
        await Assert.That(() => fixture.Admit(store)).Throws<UnsupportedStartingState>();
        await Assert.That(store.Status(fixture.RevisionId).Attempts.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LinkedSourceAndSymlinkSpellingsShareCommonGitIdentityAndRejectNestedRoots()
    {
        using var fixture = new AttemptFixture();
        var linked = System.IO.Path.Combine(fixture.State.Root, "linked-source");
        fixture.Git("worktree", "add", "--detach", linked, fixture.Head);
        var alias = System.IO.Path.Combine(fixture.State.Root, "source-alias");
        Directory.CreateSymbolicLink(alias, fixture.Repository);
        using var store = fixture.State.Open();
        foreach (var root in new[] { linked, fixture.GitDirectory })
            await Assert.That(() => store.AdmitAttempt(fixture.RevisionId, linked, root)).Throws<UnsupportedWorkspaceRoot>();
        var first = store.AdmitAttempt(fixture.RevisionId, linked, fixture.Workspaces);
        await Assert.That(first.B1.Repository).IsEqualTo(fixture.GitDirectory);
        await Assert.That(store.AdmitAttempt(fixture.RevisionId, alias, fixture.Workspaces)).IsEqualTo(first);
    }
}
