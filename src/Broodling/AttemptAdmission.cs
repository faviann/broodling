using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed record OriginalB1(string Repository, string CommitOid, string MaterialSha256, string RequestedRevision)
{
    public string RetentionRef => "refs/broodling/starting/" + CommitOid;
}

/// <summary>A reserved identity, not proof of checkout materialization or cleanup authority.</summary>
public sealed record WorkspaceAllocation(string WorkspaceRoot, string Enclosure, string WorktreePath, string Branch, string AllocatedAt);
public sealed record AttemptAbandonment(string AttemptId, string Reason, string AbandonedAt);
public sealed record WorktreeProvision(string ProvisionedAt);

/// <summary>
/// <see cref="ResourceKind"/> is the stored discriminator: <c>worktree</c> owns a local enclosure,
/// worktree and branch; <c>http</c> owns none and retains only authority and shared Git custody.
/// </summary>
public sealed record AttemptRecord(string AttemptId, string WorkUnitId, string ContractRevisionId, bool IsCurrent,
    OriginalB1 B1, string ResourceKind, [property: JsonPropertyName("allocation")] WorkspaceAllocation? WorktreeAllocation,
    string AdmittedAt, AttemptAbandonment? Abandonment,
    WorktreeProvision? Provision = null, AttemptRetirement? Retirement = null, AttemptRetry? Retry = null)
{
    public const string Worktree = "worktree";
    public const string Http = "http";

    /// <summary>The owned local allocation for worktree operations. An HTTP Attempt has none.</summary>
    [JsonIgnore]
    public WorkspaceAllocation Allocation => WorktreeAllocation
        ?? throw new WorktreeProvisioningError("An HTTP Attempt owns no local worktree allocation.");
}

public sealed partial class BroodlingStore
{
    /// <summary>Resolve and retain original B1, then atomically admit Attempt and allocation. No checkout or dispatch.</summary>
    public AttemptRecord AdmitAttempt(string revisionId, string repository, string workspaceRoot, string revision = "HEAD")
    {
        RequireUnpaused();
        var contract = GetContractRevision(revisionId);
        RequireLocalDelivery(contract.Contract);
        if (!IsAdmitted(revisionId))
            throw new AttemptAdmissionError("An Attempt requires a committed admitted Contract decision.");
        var state = GitCustody.Resolve(repository, revision);
        var root = GitCustody.WorkspaceRoot(workspaceRoot, repository, state);
        return AdmitAttempt(revisionId, state, root);
    }

    /// <summary>
    /// Admit an HTTP DirectTarget Attempt: retain original B1 in the shared common Git directory and
    /// commit the Attempt without any local enclosure, worktree, branch or workspace root.
    /// </summary>
    public AttemptRecord AdmitHttpAttempt(string revisionId, string repository, string revision = "HEAD")
    {
        RequireUnpaused();
        var contract = GetContractRevision(revisionId).Contract;
        if (contract.RequestBundle is not null)
            throw new AttemptAdmissionError("A bundle-bound Contract starts only from its retained preparation; admit it from its Issue submission.");
        RequireHttpDelivery(contract);
        return AdmitAttempt(revisionId, GitCustody.Resolve(repository, revision), root: null);
    }

    /// <summary>
    /// Admit an HTTP Attempt from one Issue submission's bundle-bound Contract, starting from the
    /// bundle's retained repository preparation. The caller cannot replace its repository or commit.
    /// </summary>
    public AttemptRecord AdmitHttpAttempt(string submissionId)
    {
        RequireUnpaused();
        var submission = GetIssueSubmission(submissionId);
        if (submission.ContractRevisionId is not { } revisionId)
            throw new AttemptAdmissionError("The Issue submission has no admitted Contract revision.");
        var bundle = GetRequestBundle(submissionId);
        // Bundle-bound admission already fixed the PR target from this same preparation.
        if (GetContractRevision(revisionId).Contract.RequestBundle is not { } binding || binding != Binding(bundle)
            || bundle.Repository is not { } repository)
            throw new AttemptAdmissionError("The Contract is not bound to this Issue submission's prepared RequestBundle.");
        return AdmitAttempt(revisionId, repository.StartingState, root: null);
    }

    /// <summary>HTTP DirectTarget is the authorized pull-request path; no-effect work keeps its local worktree.</summary>
    private static void RequireHttpDelivery(Contract contract)
    {
        try
        {
            if (Closability.AuthorizeDelivery(contract).Mode == "pull_request") return;
        }
        catch (InvalidContractProposal) { }
        throw new AttemptAdmissionError("An HTTP Attempt requires exactly one authorized pull-request effect.");
    }

    /// <summary>The bridge worktree path serves no-effect work; authorized PR work is always an HTTP Attempt.</summary>
    private static void RequireLocalDelivery(Contract contract)
    {
        try
        {
            if (Closability.AuthorizeDelivery(contract).Mode == "none") return;
        }
        catch (InvalidContractProposal) { }
        throw new AttemptAdmissionError("A worktree Attempt serves only no-effect work; authorized pull-request work uses an HTTP Attempt.");
    }

    /// <summary>A null root admits an HTTP Attempt; its separate identity domain never converges with a worktree Attempt.</summary>
    private AttemptRecord AdmitAttempt(string revisionId, StartingState state, string? root)
    {
        var contract = GetContractRevision(revisionId);
        if (!IsAdmitted(revisionId))
            throw new AttemptAdmissionError("An Attempt requires a committed admitted Contract decision.");
        GitCustody.AssertSupportedCheckout(state.Repository, state.CommitOid);
        var material = Digests.AdmittedMaterial(contract.Contract.SourceAttribution);
        var id = "at-" + Digests.Parts(root is null ? "broodling.application.attempt.http.v1" : "broodling.dotnet.attempt.v1",
            revisionId, state.Repository, state.CommitOid, material);
        // Git and SQLite cannot share a transaction. A crash may leave a harmless retention pin;
        // it must never leave an acknowledged Attempt without selected-object custody.
        GitCustody.Retain(state);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireOrdinaryAttemptAuthority(contract.WorkUnitId, transaction);
        RequireIssueSubmissionNotCancelled(revisionId, transaction);
        var existing = ReadAttempts("work_unit_id = $p0", contract.WorkUnitId, transaction).SingleOrDefault(attempt => attempt.IsCurrent);
        if (existing is not null)
        {
            if (existing.AttemptId != id)
                throw new AttemptConflict("This Work Unit already has a current Attempt with different Contract, source or B1 bindings.");
            transaction.Commit();
            return existing; // First allocation and requested spelling remain frozen, even if caller root changes.
        }
        if (ReadDecision(revisionId, transaction)?.Admitted != true)
            throw new AttemptAdmissionError("An Attempt requires a committed admitted Contract decision.");
        RequireUnpaused(transaction);
        var now = Now();
        var allocation = root is null ? null : new WorkspaceAllocation(root, System.IO.Path.Combine(root, id),
            System.IO.Path.Combine(root, id, "worktree"), "broodling/" + id, now);
        InsertAttempt(new(id, contract.WorkUnitId, revisionId, true, new(state.Repository, state.CommitOid, material, state.RequestedRevision),
            allocation is null ? AttemptRecord.Http : AttemptRecord.Worktree, allocation, now, null), transaction);
        var result = ReadAttempt(id, transaction);
        transaction.Commit();
        return result;
    }

    private void InsertAttempt(AttemptRecord attempt, SqliteTransaction transaction)
    {
        var allocation = attempt.WorktreeAllocation;
        Execute("""
            INSERT INTO attempts VALUES ($p0, $p1, $p2, 1, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11, $p12)
            """, transaction, attempt.AttemptId, attempt.WorkUnitId, attempt.ContractRevisionId, attempt.B1.Repository,
            attempt.B1.CommitOid, attempt.B1.MaterialSha256, attempt.B1.RequestedRevision, allocation?.WorkspaceRoot,
            allocation?.Enclosure, allocation?.WorktreePath, allocation?.Branch, attempt.AdmittedAt, attempt.ResourceKind);
    }

    private void RequireOrdinaryAttemptAuthority(string workUnitId, SqliteTransaction transaction)
    {
        RequireIncompleteWorkUnit(workUnitId, transaction);
        var previous = ReadAttempts("work_unit_id = $p0", workUnitId, transaction);
        if (previous.Any(attempt => attempt.Abandonment is not null || !attempt.IsCurrent))
            throw new StaleAttempt("Ordinary admission cannot restore ended authority or authorize replacement.");
    }

    private void RequireIncompleteWorkUnit(string workUnitId, SqliteTransaction transaction)
    {
        using var completed = Command("SELECT 1 FROM attempt_completions WHERE work_unit_id = $p0 LIMIT 1", transaction, workUnitId);
        if (completed.ExecuteScalar() is not null)
            throw new StaleAttempt("Completed Work Units cannot acquire new Attempt authority.");
    }

    public AttemptRecord GetAttempt(string attemptId) => ReadAttempt(attemptId);
    public AttemptRecord? CurrentAttempt(string workUnitId) =>
        ReadAttempts("work_unit_id = $p0 AND is_current = 1", workUnitId).SingleOrDefault();

    /// <summary>Point-in-time authority only. Dependent lifecycle writes must recheck inside their transaction.</summary>
    public AttemptRecord RequireCurrentAttempt(string attemptId) => RequireCurrentAttempt(attemptId, null);
    private AttemptRecord RequireCurrentAttempt(string attemptId, SqliteTransaction? transaction)
    {
        var attempt = ReadAttempt(attemptId, transaction);
        if (!attempt.IsCurrent || attempt.Abandonment is not null)
            throw new StaleAttempt("The Attempt no longer holds durable current authority.");
        return attempt;
    }

    /// <summary>Commit irreversible ineligibility; no native stop, cleanup or replacement is implied.</summary>
    public AttemptAbandonment AbandonAttempt(string attemptId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new AttemptAdmissionError("Abandonment requires a nonempty reason.");
        using var transaction = connection.BeginTransaction(deferred: false);
        var result = AbandonAttemptInTransaction(attemptId, reason, transaction);
        transaction.Commit();
        return result;
    }

    private AttemptAbandonment AbandonAttemptInTransaction(string attemptId, string reason,
        Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        var attempt = ReadAttempt(attemptId, transaction);
        if (attempt.Abandonment is { } previous) return previous;
        RequireCurrentAttempt(attemptId, transaction);
        Execute("INSERT INTO attempt_abandonments VALUES ($p0, $p1, $p2)", transaction, attemptId, reason, Now());
        return ReadAttempt(attemptId, transaction).Abandonment!;
    }

    private AttemptRecord ReadAttempt(string id, SqliteTransaction? transaction = null) =>
        ReadAttempts("attempt_id = $p0", id, transaction).SingleOrDefault() ?? throw new UnknownRecord("Unknown Attempt.");

    // The predicate is internal SQL, never caller-supplied text. A single SELECT gives coherent authority/abandonment.
    private IReadOnlyList<AttemptRecord> ReadAttempts(string predicate, string value, SqliteTransaction? transaction = null)
    {
        using var command = Command($"""
            SELECT a.*, b.reason, b.abandoned_at, p.provisioned_at FROM attempts AS a
            LEFT JOIN attempt_abandonments AS b USING (attempt_id)
            LEFT JOIN worktree_provisions AS p USING (attempt_id)
            WHERE {predicate} ORDER BY a.rowid
            """, transaction, value);
        using var row = command.ExecuteReader();
        var result = new List<AttemptRecord>();
        while (row.Read())
            result.Add(new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetInt64(3) == 1,
                new(row.GetString(4), row.GetString(5), row.GetString(6), row.GetString(7)), row.GetString(13),
                row.GetString(13) == AttemptRecord.Http ? null
                    : new(row.GetString(8), row.GetString(9), row.GetString(10), row.GetString(11), row.GetString(12)),
                row.GetString(12), row.IsDBNull(14) ? null : new(row.GetString(0), row.GetString(14), row.GetString(15)),
                row.IsDBNull(16) ? null : new(row.GetString(16)), ReadRetirement(row.GetString(0), transaction),
                ReadRetry("attempt_id", row.GetString(0), transaction)));
        return result.AsReadOnly();
    }
}
