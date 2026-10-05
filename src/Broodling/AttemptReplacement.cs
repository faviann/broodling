using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>A successor's preparation freezes its own binding; the lineage carries only identities.</summary>
public sealed record AttemptRetry(string RetryKey, string PredecessorId, string AttemptId, string RequestedAt);

public sealed partial class BroodlingStore
{
    public AttemptRetry? FindRetry(string retryKey) => ReadRetry("retry_key", retryKey);

    private AttemptRetry? ReadRetry(string column, string id, SqliteTransaction? transaction = null)
    {
        using var command = Command($"SELECT retry_key, predecessor_id, attempt_id, requested_at FROM attempt_retries WHERE {column} = $p0", transaction, id);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3)) : null;
    }

    /// <summary>One explicit successor from original B1. Commit identity and lineage before any preparation or dispatch.</summary>
    public AttemptRecord AdmitRetry(string predecessorId, string retryKey)
    {
        if (string.IsNullOrWhiteSpace(retryKey)) throw new AttemptAdmissionError("Replacement requires a nonempty explicit retry key.");
        using var transaction = connection.BeginTransaction(deferred: false);
        var predecessor = ReadAttempt(predecessorId, transaction);
        var contract = ReadRevision(predecessor.ContractRevisionId, transaction)!;
        try { Closability.AuthorizeDelivery(contract.Contract); }
        catch (InvalidContractProposal) { throw new AttemptAdmissionError("The frozen effect authority is unsupported."); }
        if (ReadRetry("retry_key", retryKey, transaction) is { } existing)
        {
            if (existing.PredecessorId != predecessorId)
                throw new AttemptConflict("The retry key already binds a different predecessor.");
            var historical = ReadAttempt(existing.AttemptId, transaction);
            transaction.Commit();
            return historical; // Historical identity only; never restore authority here.
        }
        RequireIncompleteWorkUnit(predecessor.WorkUnitId, predecessor.ContractRevisionId, transaction);
        // The retry_requires_retirement trigger refuses the same: only the latest authority may execute again.
        using (var superseded = Command("SELECT 1 FROM superseded_contracts WHERE contract_revision_id = $p0",
            transaction, predecessor.ContractRevisionId))
            if (superseded.ExecuteScalar() is not null)
                throw new AttemptConflict("The Work Unit's latest submission supersedes this Attempt's Contract; it is not replaced.");
        RequireBundleAuthority(contract, transaction);
        if (predecessor.Abandonment is null || ReadRetirement(predecessorId, transaction) is not { RetiredAt: not null } retirement)
            throw new AttemptAdmissionError("Replacement requires abandonment and completed retirement.");
        // Verified stopped-target maintenance is the only retirement of dispatched work, and its successor is
        // admitted only within a maintenance pause. Every other predecessor must still prove it never dispatched.
        if (retirement.Basis == "stopped_target") RequireMaintenancePause(transaction);
        else RequireRetirementSafety(predecessor, transaction);
        if (ReadRetry("predecessor_id", predecessorId, transaction) is not null)
            throw new AttemptConflict("This predecessor already has an explicit successor.");
        if (ReadAttempts("work_unit_id = $p0 AND is_current = 1", predecessor.WorkUnitId, transaction).Count != 0)
            throw new AttemptConflict("This Work Unit already has a current Attempt.");

        // Never resolve current HEAD or requested spelling to choose retry material.
        var pins = contract.Contract.SourceAttribution;
        var material = Digests.AdmittedMaterial(pins);
        if (material != predecessor.B1.MaterialSha256) throw new SourceAttributionError("Original B1 source bindings changed.");
        foreach (var pin in pins)
        {
            var source = ReadSource(pin.SourceId, transaction);
            if (source.WorkUnitId != predecessor.WorkUnitId || source.ContentSha256 != pin.ContentSha256 || Digests.Bytes(source.Content) != pin.ContentSha256)
                throw new SourceAttributionError("Original entitled source material changed.");
        }
        GitCustody.Retain(new(predecessor.B1.Repository, predecessor.B1.CommitOid, predecessor.B1.RequestedRevision));
        var id = "at-" + Digests.Parts("broodling.dotnet.attempt.retry.v1", predecessorId, retryKey);
        var now = Now();
        // The deferred FK requires the new Attempt to commit in this very transaction.
        Execute("INSERT INTO attempt_retries VALUES ($p0, $p1, $p2, $p3)", transaction, retryKey, predecessorId, id, now);
        InsertAttempt(new(id, predecessor.WorkUnitId, predecessor.ContractRevisionId, true, predecessor.B1, now, null), transaction);
        var result = ReadAttempt(id, transaction);
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Admit and prepare one explicit successor, never dispatching it. It targets the predecessor's
    /// retained DirectTarget origin; for a <c>stopped_target</c> predecessor that is the origin its
    /// maintenance check named. Repeating it returns the successor's existing submission unchanged.
    /// </summary>
    public NativeSubmission PrepareRetry(string predecessorId, string retryKey)
    {
        // Resolved first, so a predecessor without a retained origin leaves no successor behind.
        var origin = FindSubmission(predecessorId)?.Origin
            ?? throw new SubmissionNotReady("The predecessor has no retained DirectTarget origin.");
        var attempt = AdmitRetry(predecessorId, retryKey);
        return FindSubmission(attempt.AttemptId) ?? PrepareHttpSubmission(attempt.AttemptId, origin);
    }
}
