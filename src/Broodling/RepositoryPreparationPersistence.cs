namespace Broodling;

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Acquire and retain the repository selected for one accepted Issue
    /// submission. External acquisition happens without holding SQLite's writer;
    /// the final repository identity and preparation row commit together.
    /// </summary>
    public async Task<RepositoryPreparation> PrepareRequestBundleRepositoryAsync(string bundleId,
        string repositoryRoot, GitHubRepositoryCredentials credentials,
        GitHubRepositorySource? source = null, CancellationToken cancellationToken = default)
    {
        WorkReference reference;
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            var bundle = ReadRequestBundleById(bundleId, transaction)
                ?? throw new UnknownRecord("Unknown RequestBundle.");
            if (ReadRepositoryPreparation(bundleId, transaction) is { } existing)
            {
                transaction.Commit();
                return existing;
            }
            if (bundle.State != "capturing" || !SubmissionCanCapture(bundle.SubmissionId, transaction))
                throw new RequestBundleConflict("Repository preparation requires an open RequestBundle capture.");
            var initialSubmission = ReadIssueSubmission(bundle.SubmissionId, transaction)!;
            var initialWork = ReadWorkUnit(initialSubmission.WorkUnitId, transaction)!;
            reference = WorkReference.Parse(initialWork.Host + "/" + initialWork.Owner + "/" + initialWork.Repository,
                initialWork.IssueNumber, initialWork.RepositoryIdentity, initialWork.IssueIdentity);
            transaction.Commit();
        }

        var acquired = await (source ?? new GitHubRepositorySource()).AcquireAsync(reference, credentials,
            repositoryRoot, cancellationToken);

        using var write = connection.BeginTransaction(deferred: false);
        var current = ReadRequestBundleById(bundleId, write)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        var submission = ReadIssueSubmission(current.SubmissionId, write)!;
        var work = ReadWorkUnit(submission.WorkUnitId, write)!;
        if (work.RepositoryIdentity is not null && work.RepositoryIdentity != acquired.RepositoryIdentity)
            throw new WorkUnitIdentityConflict("The acquired GitHub repository identity conflicts with the Work Unit pin.");
        if (ReadRepositoryPreparation(bundleId, write) is { } replay)
        {
            if (!SamePreparation(replay, acquired))
                throw new RequestBundleConflict("The RequestBundle repository preparation changed during acquisition.");
            write.Commit();
            return replay;
        }
        if (current.State != "capturing" || !SubmissionCanCapture(current.SubmissionId, write))
            throw new RequestBundleConflict("The Issue submission is no longer eligible for repository preparation.");

        Execute("UPDATE work_units SET repository_identity = COALESCE(repository_identity, $p0) WHERE work_unit_id = $p1",
            write, acquired.RepositoryIdentity, work.WorkUnitId);
        var preparedAt = DateTimeOffset.UtcNow.ToString("O");
        Execute("INSERT INTO request_bundle_repositories VALUES ($p0, $p1, $p2, $p3, $p4, $p5)",
            write, bundleId, acquired.Repository, acquired.DefaultBranch, acquired.StartingRevision,
            acquired.StartingCommit, preparedAt);
        var result = ReadRepositoryPreparation(bundleId, write)!;
        write.Commit();
        return result;
    }

    /// <summary>Register a repository file using the retained starting commit, never a moving branch.</summary>
    public RequestBundleReference RegisterRequestBundleRepositoryFile(string bundleId, string referenceId,
        byte[] selector, string path)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var preparation = ReadRepositoryPreparation(bundleId, transaction)
            ?? throw new RequestBundleConflict("Repository preparation is required before repository-file selection.");
        transaction.Commit();
        return RegisterRequestBundleReference(bundleId,
            RequestBundleReferenceInput.GitBlob(referenceId, selector, preparation.Repository,
                preparation.StartingCommit, path));
    }

    private static bool SamePreparation(RepositoryPreparation prepared, AcquiredRepository acquired) =>
        prepared.Repository == acquired.Repository
        && prepared.DefaultBranch == acquired.DefaultBranch
        && prepared.StartingRevision == acquired.StartingRevision
        && prepared.StartingCommit == acquired.StartingCommit;
}
