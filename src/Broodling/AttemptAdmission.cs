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
public sealed record AttemptRecord(string AttemptId, string WorkUnitId, string ContractRevisionId, bool IsCurrent,
    OriginalB1 B1, WorkspaceAllocation Allocation, string AdmittedAt, AttemptAbandonment? Abandonment,
    WorktreeProvision? Provision = null);

public sealed partial class BroodlingStore
{
    /// <summary>Resolve and retain original B1, then atomically admit Attempt and allocation. No checkout or dispatch.</summary>
    public AttemptRecord AdmitAttempt(string revisionId, string repository, string workspaceRoot, string revision = "HEAD")
    {
        var contract = GetContractRevision(revisionId);
        if (!IsAdmitted(revisionId))
            throw new AttemptAdmissionError("An Attempt requires a committed admitted Contract decision.");
        var state = GitCustody.Resolve(repository, revision);
        var root = GitCustody.WorkspaceRoot(workspaceRoot, repository, state);
        var material = Digests.Parts(new[] { "broodling.dotnet.admitted-material.v1" }
            .Concat(contract.Contract.SourceAttribution.SelectMany(pin => new[] { pin.SourceId, pin.ContentSha256 })).ToArray());
        var id = "at-" + Digests.Parts("broodling.dotnet.attempt.v1", revisionId, state.Repository, state.CommitOid, material);
        // Git and SQLite cannot share a transaction. A crash may leave a harmless retention pin;
        // it must never leave an acknowledged Attempt without selected-object custody.
        GitCustody.Retain(state);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireOrdinaryAttemptAuthority(contract.WorkUnitId, transaction);
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
        var enclosure = System.IO.Path.Combine(root, id);
        var now = Now();
        Execute("""
            INSERT INTO attempts VALUES ($p0, $p1, $p2, 1, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11)
            """, transaction, id, contract.WorkUnitId, revisionId, state.Repository, state.CommitOid, material, revision,
            root, enclosure, System.IO.Path.Combine(enclosure, "worktree"), "broodling/" + id, now);
        var result = ReadAttempt(id, transaction);
        transaction.Commit();
        return result;
    }

    private void RequireOrdinaryAttemptAuthority(string workUnitId, SqliteTransaction transaction)
    {
        var previous = ReadAttempts("work_unit_id = $p0", workUnitId, transaction);
        if (previous.Any(attempt => attempt.Abandonment is not null || !attempt.IsCurrent))
            throw new StaleAttempt("Ordinary admission cannot restore ended authority or authorize replacement.");
        // G must add the Work-Unit-wide completed-disposition guard here and in schema triggers
        // when result/disposition storage lands; exact-Attempt results must not reopen completed work.
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
        var attempt = ReadAttempt(attemptId, transaction);
        if (attempt.Abandonment is { } previous)
        {
            transaction.Commit();
            return previous;
        }
        RequireCurrentAttempt(attemptId, transaction);
        Execute("INSERT INTO attempt_abandonments VALUES ($p0, $p1, $p2)", transaction, attemptId, reason, Now());
        var result = ReadAttempt(attemptId, transaction).Abandonment!;
        transaction.Commit();
        return result;
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
                new(row.GetString(4), row.GetString(5), row.GetString(6), row.GetString(7)),
                new(row.GetString(8), row.GetString(9), row.GetString(10), row.GetString(11), row.GetString(12)),
                row.GetString(12), row.IsDBNull(13) ? null : new(row.GetString(0), row.GetString(13), row.GetString(14)),
                row.IsDBNull(15) ? null : new(row.GetString(15))));
        return result.AsReadOnly();
    }
}
