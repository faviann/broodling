using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed record AttemptRetirement(string AttemptId, string Basis, string CeasedAt, string? RetiredAt);

public sealed partial class BroodlingStore
{
    public AttemptRetirement? FindRetirement(string attemptId) => ReadRetirement(attemptId);

    private AttemptRetirement? ReadRetirement(string attemptId, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT attempt_id, basis, ceased_at, retired_at FROM attempt_retirements WHERE attempt_id = $p0", transaction, attemptId);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.IsDBNull(3) ? null : row.GetString(3)) : null;
    }

    /// <summary>Abandon before requesting native stop. A native result never grants retirement authority.</summary>
    public async Task<AttemptRetirement> StopAsync(string attemptId, string reason, INativeStopper? transport,
        CancellationToken cancellationToken = default)
    {
        AbandonAttempt(attemptId, reason); // Its own committed transaction, before any external call or host inspection.
        if (FindRetirement(attemptId) is { } existing) return existing;
        var submitted = FindSubmission(attemptId);
        var allocation = GetAttempt(attemptId);
        // Retained physical allocation/enclosure ownership still matters. A missing checkout
        // is allowed here; live Git inspection and deletion authority belong to retirement.
        WorktreeMaterialization.ValidatePaths(allocation, Path, inspectGit: false);
        if (Directory.Exists(allocation.Allocation.Enclosure)) WorktreeMaterialization.RequireMarker(allocation);
        else if (File.Exists(allocation.Allocation.Enclosure) || allocation.Provision is not null || submitted is { State: not "prepared" })
            throw new CessationUnconfirmed("The dispatched or acknowledged enclosure is missing or ambiguous; Attempt abandoned, containment remains with the operator.");
        if (submitted is { State: not "prepared" })
        {
            if (submitted.Run is not { } run)
                throw new CessationUnconfirmed("Dispatched run identity is unresolved. Attempt abandoned and quarantined; dispatch will not be replayed to discover it.");
            // Reconnect only from the retained run binding, with no dispatch credentials or live workspace dependency.
            if (transport is null)
                throw new SubmissionNotReady("Stopping a known native run requires native stop transport.");
            await transport.StopAsync(run, cancellationToken);
            throw new CessationUnconfirmed("Native stop supplies no physical cessation proof. Attempt abandoned and quarantined; checkout retained.", nativeStopRequested: true);
        }

        // Provisioning holds this same SQLite writer through host changes and acknowledgment.
        // Once abandonment commits, queued provisioners must refuse and no new dispatch can start.
        using var transaction = connection.BeginTransaction(deferred: false);
        if (ReadRetirement(attemptId, transaction) is { } retained) { transaction.Commit(); return retained; }
        var attempt = ReadAttempt(attemptId, transaction);
        if (ReadSubmission(attemptId, transaction) is { State: not "prepared" })
            throw new CessationUnconfirmed("Dispatched Attempts remain quarantined.");
        WorktreeMaterialization.ValidatePaths(attempt, Path, inspectGit: false);
        string basis;
        if (!Directory.Exists(attempt.Allocation.Enclosure))
        {
            if (File.Exists(attempt.Allocation.Enclosure) || attempt.Provision is not null)
                throw new CessationUnconfirmed("The acknowledged enclosure is missing or ambiguous.");
            basis = "never_materialized";
        }
        else
        {
            WorktreeMaterialization.RequireMarker(attempt);
            if (attempt.Provision is null)
                throw new CessationUnconfirmed("Interrupted provisioning has no durable acknowledgment; cessation is unknown.");
            basis = "never_dispatched";
        }
        Execute("INSERT INTO attempt_retirements VALUES ($p0, $p1, $p2, NULL)", transaction, attemptId, basis, Now());
        var result = ReadRetirement(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>Discard only the proven safe Attempt's exact checkout and branch. Keep enclosure and stable lock.</summary>
    public AttemptRetirement RetireAttempt(string attemptId)
    {
        var proof = FindRetirement(attemptId) ?? throw new CessationUnconfirmed("Retirement requires retained safe cessation proof.");
        if (proof.RetiredAt is not null) return proof;
        var attempt = GetAttempt(attemptId);
        WorktreeMaterialization.ValidatePaths(attempt, Path, inspectGit: false);
        if (!Directory.Exists(attempt.Allocation.Enclosure))
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            attempt = ReadAttempt(attemptId, transaction);
            RequireRetirementSafety(attempt, transaction);
            WorktreeMaterialization.ValidatePaths(attempt, Path);
            if (proof.Basis != "never_materialized" || File.Exists(attempt.Allocation.Enclosure))
                throw new CessationUnconfirmed("Retirement enclosure disappeared or changed.");
            // No mutation is needed, but refuse foreign Git ownership even without an enclosure.
            WorktreeMaterialization.RequireUnmaterialized(attempt);
            return AcknowledgeRetirement(attemptId, transaction);
        }
        AdministrativeGitProcess.RequireSupportedHost();
        WorktreeMaterialization.RequireMarker(attempt);
        using var held = AdministrativeGitProcess.EnclosureLock.Acquire(
            System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName));
        using var write = connection.BeginTransaction(deferred: false);
        proof = ReadRetirement(attemptId, write)!;
        if (proof.RetiredAt is not null) { write.Commit(); return proof; }
        attempt = ReadAttempt(attemptId, write);
        RequireRetirementSafety(attempt, write);
        WorktreeMaterialization.ValidatePaths(attempt, Path);
        WorktreeMaterialization.RequireMarker(attempt);
        WorktreeMaterialization.RemoveOwned(attempt, held);
        return AcknowledgeRetirement(attemptId, write);
    }

    private void RequireRetirementSafety(AttemptRecord attempt, SqliteTransaction transaction)
    {
        if (attempt.IsCurrent || attempt.Abandonment is null || ReadSubmission(attempt.AttemptId, transaction) is { State: not "prepared" }
            || ReadRetirement(attempt.AttemptId, transaction) is not { Basis: "never_materialized" or "never_dispatched" })
            throw new CessationUnconfirmed("Historical labels do not authorize new cleanup.");
    }

    private AttemptRetirement AcknowledgeRetirement(string attemptId, SqliteTransaction transaction)
    {
        Execute("UPDATE attempt_retirements SET retired_at = $p0 WHERE attempt_id = $p1 AND retired_at IS NULL", transaction, Now(), attemptId);
        var result = ReadRetirement(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }
}
