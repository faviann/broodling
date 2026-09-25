using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace Broodling;

/// <summary>An HTTP successor chooses no workspace root or target here; its preparation freezes its own binding.</summary>
public sealed record AttemptRetry(string RetryKey, string PredecessorId, string AttemptId, string? WorkspaceRoot, string? TargetJson, string RequestedAt);

public sealed partial class BroodlingStore
{
    public AttemptRetry? FindRetry(string retryKey) => ReadRetry("retry_key", retryKey);

    private AttemptRetry? ReadRetry(string column, string id, SqliteTransaction? transaction = null)
    {
        using var command = Command($"SELECT retry_key, predecessor_id, attempt_id, workspace_root, target_json, requested_at FROM attempt_retries WHERE {column} = $p0", transaction, id);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.IsDBNull(3) ? null : row.GetString(3),
            row.IsDBNull(4) ? null : row.GetString(4), row.GetString(5)) : null;
    }

    /// <summary>One explicit successor from original B1. Commit identity/allocation/target/lineage before checkout or dispatch.</summary>
    public AttemptRecord AdmitRetry(string predecessorId, string retryKey, string workspaceRoot, NativeProfile profile)
    {
        if (string.IsNullOrWhiteSpace(retryKey)) throw new AttemptAdmissionError("Replacement requires a nonempty explicit retry key.");
        if (!System.IO.Path.IsPathFullyQualified(workspaceRoot)) throw new UnsupportedWorkspaceRoot("The workspace root must be absolute.");
        var root = PhysicalPaths.Resolve(workspaceRoot);
        if (PhysicalPaths.IsWithinTemporaryRoot(root)) throw new UnsupportedWorkspaceRoot("Replacement requires durable workspace storage.");
        if (PhysicalPaths.IsWithinDisposable(root)) throw new UnsupportedWorkspaceRoot("Replacement cannot be nested in a disposable enclosure.");
        return AdmitRetry(predecessorId, retryKey, (root, profile));
    }

    /// <summary>One explicit HTTP successor from original B1, again without any local enclosure or worktree.</summary>
    public AttemptRecord AdmitRetry(string predecessorId, string retryKey)
    {
        if (string.IsNullOrWhiteSpace(retryKey)) throw new AttemptAdmissionError("Replacement requires a nonempty explicit retry key.");
        return AdmitRetry(predecessorId, retryKey, worktree: null);
    }

    /// <summary>A null worktree choice replaces an HTTP predecessor; the successor keeps the predecessor's resource kind.</summary>
    private AttemptRecord AdmitRetry(string predecessorId, string retryKey, (string Root, NativeProfile Profile)? worktree)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var predecessor = ReadAttempt(predecessorId, transaction);
        if ((predecessor.ResourceKind == AttemptRecord.Http) != (worktree is null))
            throw new AttemptAdmissionError("Replacement must keep the predecessor's resource kind.");
        var contract = ReadRevision(predecessor.ContractRevisionId, transaction)!;
        DeliveryAuthorization delivery;
        try { delivery = Closability.AuthorizeDelivery(contract.Contract); }
        catch (InvalidContractProposal) { throw new AttemptAdmissionError("The frozen effect authority is unsupported."); }
        var root = worktree?.Root;
        var target = worktree?.Profile.Target(delivery.Mode).ToJsonString();
        if (ReadRetry("retry_key", retryKey, transaction) is { } existing)
        {
            if (existing.PredecessorId != predecessorId || existing.WorkspaceRoot != root
                || !JsonNode.DeepEquals(existing.TargetJson is null ? null : JsonNode.Parse(existing.TargetJson),
                    target is null ? null : JsonNode.Parse(target)))
                throw new AttemptConflict("The retry key already binds different predecessor, workspace root or target parameters.");
            var historical = ReadAttempt(existing.AttemptId, transaction);
            transaction.Commit();
            return historical; // Historical identity only; never restore authority or materialize here.
        }
        RequireIncompleteWorkUnit(predecessor.WorkUnitId, transaction);
        if (predecessor.Abandonment is null || ReadRetirement(predecessorId, transaction)?.RetiredAt is null)
            throw new AttemptAdmissionError("Replacement requires abandonment and completed safe retirement.");
        RequireRetirementSafety(predecessor, transaction);
        if (ReadRetry("predecessor_id", predecessorId, transaction) is not null)
            throw new AttemptConflict("This predecessor already has an explicit successor.");
        if (ReadAttempts("work_unit_id = $p0 AND is_current = 1", predecessor.WorkUnitId, transaction).Count != 0)
            throw new AttemptConflict("This Work Unit already has a current Attempt.");

        // Never resolve current HEAD, requested spelling, or candidate changes to choose retry material.
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
        var allocation = root is null ? null : new WorkspaceAllocation(root, System.IO.Path.Combine(root, id),
            System.IO.Path.Combine(root, id, "worktree"), "broodling/" + id, now);
        var successor = new AttemptRecord(id, predecessor.WorkUnitId, predecessor.ContractRevisionId, true, predecessor.B1,
            predecessor.ResourceKind, allocation, now, null);
        if (allocation is not null) WorktreeMaterialization.ValidatePaths(successor, Path);
        // The deferred FK requires the new Attempt to commit in this very transaction.
        Execute("INSERT INTO attempt_retries VALUES ($p0, $p1, $p2, $p3, $p4, $p5)", transaction, retryKey, predecessorId, id, root, target, now);
        InsertAttempt(successor, transaction);
        var result = ReadAttempt(id, transaction);
        transaction.Commit();
        return result;
    }

    public NativeSubmission PrepareRetry(string predecessorId, string retryKey, string workspaceRoot, NativeProfile profile)
    {
        var attempt = AdmitRetry(predecessorId, retryKey, workspaceRoot, profile);
        RequireCurrentAttempt(attempt.AttemptId);
        if (FindSubmission(attempt.AttemptId) is { } prepared) return prepared;
        ProvisionAttempt(attempt.AttemptId);
        return PrepareSubmission(attempt.AttemptId, profile);
    }

    public Task<NativeSubmission> RetryAsync(string predecessorId, string retryKey, string workspaceRoot, NativeProfile profile,
        INativeTransport transport, DispatchCredentials? credentials = null, CancellationToken cancellationToken = default)
    {
        var prepared = PrepareRetry(predecessorId, retryKey, workspaceRoot, profile);
        return DispatchAsync(prepared.AttemptId, profile, transport, credentials, cancellationToken);
    }
}
