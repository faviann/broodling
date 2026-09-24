namespace Broodling;

/// <summary>
/// Immutable service-owned repository state selected during preparation. The
/// target branch name and source commit are deliberately separate facts.
/// </summary>
public sealed record RepositoryPreparation(string BundleId, string Repository, string DefaultBranch,
    string StartingRevision, string StartingCommit, string PreparedAt)
{
    public string CommitOid => StartingCommit;
    public string TargetBranch => DefaultBranch;

    internal StartingState StartingState => new(Repository, StartingCommit, StartingRevision);
}
