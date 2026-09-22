namespace Broodling;

public sealed partial class BroodlingStore
{
    /// <summary>Converge an existing allocation; never allocate, dispatch, reset candidate HEAD or grant cleanup authority.</summary>
    public AttemptRecord ProvisionAttempt(string attemptId)
    {
        AdministrativeGitProcess.RequireSupportedHost();
        AttemptRecord attempt;
        // Claim only with current authority. Never wait for the host lock inside SQLite:
        // all materialization writers order enclosure lock -> SQLite writer.
        using (var claim = connection.BeginTransaction(deferred: false))
        {
            attempt = RequireCurrentAttempt(attemptId, claim);
            WorktreeMaterialization.ValidatePaths(attempt, Path, inspectGit: false);
            WorktreeMaterialization.ClaimEnclosure(attempt);
            claim.Commit();
        }
        using var enclosureLock = AdministrativeGitProcess.EnclosureLock.Acquire(
            System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName));
        using var transaction = connection.BeginTransaction(deferred: false);
        attempt = RequireCurrentAttempt(attemptId, transaction);
        WorktreeMaterialization.ValidatePaths(attempt, Path);
        WorktreeMaterialization.RequireMarker(attempt);
        WorktreeMaterialization.Materialize(attempt, enclosureLock);
        RequireCurrentAttempt(attemptId, transaction);
        if (attempt.Provision is null)
            Execute("INSERT INTO worktree_provisions VALUES ($p0, $p1)", transaction, attemptId, Now());
        var result = ReadAttempt(attemptId, transaction);
        transaction.Commit();
        return result;
    }
}

internal static class WorktreeMaterialization
{
    internal const string MarkerName = ".broodling-disposable-worktree";
    internal const string LockName = ".broodling-provisioning.lock";
    private const string PendingMarkerName = ".broodling-marker-pending";
    private sealed record Entry(string Path, string? Branch);

    internal static void ValidatePaths(AttemptRecord attempt, string storePath, bool inspectGit = true)
    {
        var a = attempt.Allocation;
        foreach (var path in new[] { a.WorkspaceRoot, a.Enclosure, a.WorktreePath, attempt.B1.Repository })
            RequirePhysical(path);
        if (a.Enclosure != System.IO.Path.Combine(a.WorkspaceRoot, attempt.AttemptId)
            || a.WorktreePath != System.IO.Path.Combine(a.Enclosure, "worktree")
            || a.Branch != "broodling/" + attempt.AttemptId)
            Refuse("The allocation no longer has its dedicated Attempt identity.");
        foreach (var forbidden in new[] { "/tmp", "/var/tmp", "/dev/shm", "/run" })
            if (PhysicalPaths.Contains(forbidden, a.WorkspaceRoot)) Refuse("The allocation is not on durable storage.");
        if (PhysicalPaths.IsWithinDisposable(a.WorkspaceRoot)) Refuse("The allocation is nested in another disposable enclosure.");
        var common = attempt.B1.Repository;
        if (Overlaps(a.Enclosure, common) || PhysicalPaths.Contains(a.Enclosure, PhysicalPaths.Resolve(storePath))
            || PhysicalPaths.Contains(common, PhysicalPaths.Resolve(storePath)))
            Refuse("Candidate, source Git and durable store must remain separate.");
        if (!inspectGit) return;
        if (PhysicalPaths.Resolve(GitCustody.Text(common, "rev-parse", "--path-format=absolute", "--git-common-dir").Trim()) != common)
            Refuse("The retained source no longer names its original common Git directory.");
        foreach (var entry in Entries(common).Where(entry => entry.Path != a.WorktreePath))
            if (PhysicalPaths.Contains(entry.Path, a.WorkspaceRoot) || PhysicalPaths.Contains(a.Enclosure, entry.Path))
                Refuse("The allocation overlaps another source checkout.");
    }

    private static bool Overlaps(string left, string right) => PhysicalPaths.Contains(left, right) || PhysicalPaths.Contains(right, left);
    private static void RequirePhysical(string path)
    {
        if (PhysicalPaths.Resolve(path) != path || new FileInfo(path).LinkTarget is not null)
            Refuse("An owned path was replaced by a symbolic link or physical alias.");
    }

    internal static void ClaimEnclosure(AttemptRecord attempt)
    {
        var enclosure = attempt.Allocation.Enclosure;
        var marker = System.IO.Path.Combine(enclosure, MarkerName);
        if (File.Exists(marker) || new FileInfo(marker).LinkTarget is not null)
        {
            RequireMarker(attempt);
            return;
        }
        var pending = System.IO.Path.Combine(enclosure, PendingMarkerName);
        if (Directory.Exists(enclosure) && Directory.EnumerateFileSystemEntries(enclosure).Any(path => path != pending))
            Refuse("An unmarked enclosure contains foreign material.");
        Directory.CreateDirectory(enclosure);
        RequirePhysical(pending);
        var markerBytes = System.Text.Encoding.UTF8.GetBytes(attempt.AttemptId + "\n");
        if (File.Exists(pending) && !markerBytes.AsSpan().StartsWith(File.ReadAllBytes(pending)))
            Refuse("A pending enclosure marker contains foreign material.");
        File.Delete(pending); // Do not truncate a possible hard link left at the staging pathname.
        // Atomic publication avoids a partially written ownership marker after caller death.
        using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            output.Write(markerBytes);
            output.Flush(flushToDisk: true);
        }
        File.Move(pending, marker);
    }

    internal static void RequireMarker(AttemptRecord attempt)
    {
        var marker = System.IO.Path.Combine(attempt.Allocation.Enclosure, MarkerName);
        RequirePhysical(marker);
        if (!File.Exists(marker) || File.ReadAllText(marker) != attempt.AttemptId + "\n")
            Refuse("The enclosure marker does not name this Attempt.");
    }

    internal static void Materialize(AttemptRecord attempt, AdministrativeGitProcess.EnclosureLock enclosureLock)
    {
        var a = attempt.Allocation;
        var repository = attempt.B1.Repository;
        GitCustody.Retain(new(repository, attempt.B1.CommitOid, attempt.B1.RequestedRevision), enclosureLock);
        GitCustody.AssertSupportedCheckout(repository, attempt.B1.CommitOid);
        var entries = Entries(repository);
        var entry = entries.SingleOrDefault(entry => entry.Path == a.WorktreePath);
        if (entries.Any(other => other.Path != a.WorktreePath && other.Branch == a.Branch))
            Refuse("The assigned branch is attached to another worktree.");
        if (entry is not null && entry.Branch != a.Branch)
            Refuse("The assigned path is registered on a foreign or detached branch.");
        if (entry is not null && File.Exists(System.IO.Path.Combine(a.WorktreePath, ".git")))
        {
            RequireAttached(attempt);
            // Preserve baseline replay semantics: an owned live candidate may have advanced.
            return;
        }
        if (File.Exists(a.WorktreePath) || Directory.Exists(a.WorktreePath) && Directory.EnumerateFileSystemEntries(a.WorktreePath).Any())
            Refuse("The assigned path contains material without a live owned worktree.");
        var reference = "refs/heads/" + a.Branch;
        var symbolic = GitCustody.Run(repository, ["symbolic-ref", "--quiet", reference]);
        if (symbolic.ExitCode != 1) Refuse("The assigned branch is symbolic or cannot be inspected.");
        var exists = GitCustody.Run(repository, ["show-ref", "--verify", "--quiet", reference]);
        if (exists.ExitCode is not (0 or 1)) Refuse("The assigned branch cannot be inspected.");
        if (exists.ExitCode == 0 && GitCustody.Text(repository, "show-ref", "--verify", "--hash", reference).Trim() != attempt.B1.CommitOid)
            Refuse("The assigned branch is not at original B1; provisioning will not move it.");
        var args = new List<string> { "worktree", "add" };
        if (entry is not null) args.Add("--force"); // Only the exact missing, registered owned path.
        if (exists.ExitCode == 1) args.AddRange(["-b", a.Branch]);
        args.Add("--");
        args.Add(a.WorktreePath);
        args.Add(exists.ExitCode == 0 ? a.Branch : attempt.B1.CommitOid);
        var created = GitCustody.Run(repository, args.ToArray(), enclosureLock: enclosureLock);
        if (created.ExitCode != 0)
            throw new WorktreeProvisioningError("Git worktree creation did not complete: " + created.Error.Trim());
        RequireAttached(attempt);
        if (GitCustody.Text(a.WorktreePath, "rev-parse", "HEAD").Trim() != attempt.B1.CommitOid)
            Refuse("The new worktree did not materialize at original B1.");
    }

    private static void RequireAttached(AttemptRecord attempt)
    {
        var path = attempt.Allocation.WorktreePath;
        RequirePhysical(System.IO.Path.Combine(path, ".git"));
        var common = PhysicalPaths.Resolve(GitCustody.Text(path, "rev-parse", "--path-format=absolute", "--git-common-dir").Trim());
        var top = PhysicalPaths.Resolve(GitCustody.Text(path, "rev-parse", "--show-toplevel").Trim());
        if (common != attempt.B1.Repository || top != path
            || GitCustody.Text(path, "symbolic-ref", "--quiet", "HEAD").Trim() != "refs/heads/" + attempt.Allocation.Branch)
            Refuse("The live worktree does not match its repository, path and branch assignment.");
    }

    private static List<Entry> Entries(string repository)
    {
        var result = new List<Entry>();
        string? path = null, branch = null;
        foreach (var field in GitCustody.Text(repository, "worktree", "list", "--porcelain", "-z").Split('\0'))
        {
            if (field.StartsWith("worktree ", StringComparison.Ordinal)) path = field[9..];
            else if (field.StartsWith("branch refs/heads/", StringComparison.Ordinal)) branch = field[18..];
            else if (field == "" && path is not null) { result.Add(new(path, branch)); path = branch = null; }
        }
        return result;
    }

    private static void Refuse(string message) => throw new WorktreeOwnershipConflict(message);
}
