using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace Broodling;

public sealed record AttemptRetry(string RetryKey, string PredecessorId, string AttemptId, string WorkspaceRoot, string TargetJson, string RequestedAt);

public sealed partial class BroodlingStore
{
    public AttemptRetry? FindRetry(string retryKey) => ReadRetry("retry_key", retryKey);

    private AttemptRetry? ReadRetry(string column, string id, SqliteTransaction? transaction = null)
    {
        using var command = Command($"SELECT retry_key, predecessor_id, attempt_id, workspace_root, target_json, requested_at FROM attempt_retries WHERE {column} = $p0", transaction, id);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5)) : null;
    }

    /// <summary>One explicit successor from original B1. Commit identity/allocation/target/lineage before checkout or dispatch.</summary>
    public AttemptRecord AdmitRetry(string predecessorId, string retryKey, string workspaceRoot, NativeProfile profile)
    {
        if (string.IsNullOrWhiteSpace(retryKey)) throw new AttemptAdmissionError("Replacement requires a nonempty explicit retry key.");
        if (!System.IO.Path.IsPathFullyQualified(workspaceRoot)) throw new UnsupportedWorkspaceRoot("The workspace root must be absolute.");
        var root = PhysicalPaths.Resolve(workspaceRoot);
        if (PhysicalPaths.IsWithinTemporaryRoot(root)) throw new UnsupportedWorkspaceRoot("Replacement requires durable workspace storage.");
        if (PhysicalPaths.IsWithinDisposable(root)) throw new UnsupportedWorkspaceRoot("Replacement cannot be nested in a disposable enclosure.");
        using var transaction = connection.BeginTransaction(deferred: false);
        var predecessor = ReadAttempt(predecessorId, transaction);
        var contract = ReadRevision(predecessor.ContractRevisionId, transaction)!;
        var target = profile.Target(contract.Contract.RequiredEffects.Count == 0 ? "none" : "pull_request").ToJsonString();
        if (ReadRetry("retry_key", retryKey, transaction) is { } existing)
        {
            if (existing.PredecessorId != predecessorId || existing.WorkspaceRoot != root
                || !JsonNode.DeepEquals(JsonNode.Parse(existing.TargetJson), JsonNode.Parse(target)))
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
        var enclosure = System.IO.Path.Combine(root, id);
        var now = Now();
        var allocation = new WorkspaceAllocation(root, enclosure, System.IO.Path.Combine(enclosure, "worktree"), "broodling/" + id, now);
        var successor = new AttemptRecord(id, predecessor.WorkUnitId, predecessor.ContractRevisionId, true, predecessor.B1, allocation, now, null);
        WorktreeMaterialization.ValidatePaths(successor, Path);
        // The deferred FK requires the new Attempt to commit in this very transaction.
        Execute("INSERT INTO attempt_retries VALUES ($p0, $p1, $p2, $p3, $p4, $p5)", transaction, retryKey, predecessorId, id, root, target, now);
        Execute("INSERT INTO attempts VALUES ($p0, $p1, $p2, 1, $p3, $p4, $p5, $p6, $p7, $p8, $p9, $p10, $p11)", transaction,
            id, predecessor.WorkUnitId, predecessor.ContractRevisionId, predecessor.B1.Repository, predecessor.B1.CommitOid,
            predecessor.B1.MaterialSha256, predecessor.B1.RequestedRevision, root, enclosure, allocation.WorktreePath, allocation.Branch, now);
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
