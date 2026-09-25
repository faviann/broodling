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
        // An HTTP Attempt owns no local resource, so there is nothing to inspect.
        if (allocation.ResourceKind == AttemptRecord.Worktree)
        {
            WorktreeMaterialization.ValidatePaths(allocation, Path, inspectGit: false);
            if (Directory.Exists(allocation.Allocation.Enclosure)) WorktreeMaterialization.RequireMarker(allocation);
            else if (File.Exists(allocation.Allocation.Enclosure) || allocation.Provision is not null || submitted is { State: not "prepared" })
                throw new CessationUnconfirmed("The dispatched or acknowledged enclosure is missing or ambiguous; Attempt abandoned, containment remains with the operator.");
        }
        // An HTTP record routes on its retained format and ignores any bridge transport.
        if (submitted is { Format: NativeSubmission.Http, State: not "prepared" })
            await StopRetainedHttpAsync(submitted, cancellationToken);
        else if (submitted is { State: not "prepared" })
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
        // Abandonment has committed, so this writer serializes the proof against any dispatch intent.
        var basis = attempt.ResourceKind == AttemptRecord.Http ? "no_dispatch_intent" : WorktreeRetirementBasis(attempt);
        Execute("INSERT INTO attempt_retirements VALUES ($p0, $p1, $p2, NULL)", transaction, attemptId, basis, Now());
        var result = ReadRetirement(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Request native stop of an abandoned HTTP record with dispatch intent, without credentials,
    /// source custody or replay. A confirmed run is forced directly; an intended run only after its
    /// status matches every retained binding fact, which still establishes no correlation. Every
    /// outcome refuses: force never sent, a terminal result that proves no physical cessation, or
    /// <see cref="NativeTransportError"/> when a sent force has an uncertain outcome.
    /// </summary>
    private async Task StopRetainedHttpAsync(NativeSubmission submitted, CancellationToken cancellationToken)
    {
        var (run, identity) = submitted.Run is { } confirmed
            ? (confirmed, NativeRunIdentity.Confirmed)
            : (submitted.Frozen.Run(submitted.IntendedRunId!), NativeRunIdentity.Intended);
        var stop = await DirectTargetRun.StopAsync(run, identity, DirectTargetClock, cancellationToken);
        throw stop.Force switch
        {
            DirectTargetForce.Terminal => new CessationUnconfirmed(
                "Native stop observed a terminal result, which supplies no physical cessation proof. Attempt abandoned and quarantined.",
                nativeStopRequested: true),
            DirectTargetForce.NotSent => new CessationUnconfirmed(
                $"Native stop was not sent ({stop.Reason}). Attempt abandoned and quarantined; a later explicit stop may address the run."),
            _ => new NativeTransportError(stop.Reason!)
        };
    }

    private string WorktreeRetirementBasis(AttemptRecord attempt)
    {
        WorktreeMaterialization.ValidatePaths(attempt, Path, inspectGit: false);
        if (!Directory.Exists(attempt.Allocation.Enclosure))
        {
            if (File.Exists(attempt.Allocation.Enclosure) || attempt.Provision is not null)
                throw new CessationUnconfirmed("The acknowledged enclosure is missing or ambiguous.");
            return "never_materialized";
        }
        WorktreeMaterialization.RequireMarker(attempt);
        if (attempt.Provision is null)
            throw new CessationUnconfirmed("Interrupted provisioning has no durable acknowledgment; cessation is unknown.");
        return "never_dispatched";
    }

    /// <summary>
    /// Discard only the proven safe Attempt's exact checkout and branch. Keep enclosure and stable lock.
    /// An HTTP Attempt has no local resource: retirement only acknowledges its retained proof.
    /// </summary>
    public AttemptRetirement RetireAttempt(string attemptId)
    {
        var proof = FindRetirement(attemptId) ?? throw new CessationUnconfirmed("Retirement requires retained safe cessation proof.");
        if (proof.RetiredAt is not null) return proof;
        var attempt = GetAttempt(attemptId);
        if (attempt.ResourceKind == AttemptRecord.Http)
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            RequireRetirementSafety(ReadAttempt(attemptId, transaction), transaction);
            return AcknowledgeRetirement(attemptId, transaction);
        }
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
            || ReadRetirement(attempt.AttemptId, transaction) is not { Basis: "never_materialized" or "never_dispatched" or "no_dispatch_intent" })
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
