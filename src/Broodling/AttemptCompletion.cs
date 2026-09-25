using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>One exact Attempt's authorized delivery; neither semantic certification nor cleanup authority.</summary>
public sealed record AttemptCompletion(string AttemptId, string WorkUnitId, string ContractRevisionId,
    string RunId, string ReceiptJson, string CompletedAt)
{
    public string Outcome => "SUCCEEDED";
    public string Workflow => "software-change";
    public JsonElement DeliveryReceipt => JsonSerializer.Deserialize<JsonElement>(ReceiptJson);
    public string AcceptedRevision => DeliveryReceipt.GetProperty("headRevision").GetString()!;
}

public sealed partial class BroodlingStore
{
    /// <summary>Exact historical lookup, with no current/latest substitution or native contact.</summary>
    public AttemptCompletion? FindCompletion(string attemptId) => ReadCompletion(attemptId);

    private AttemptCompletion? ReadCompletion(string attemptId, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT attempt_id, work_unit_id, contract_revision_id, run_id, receipt_json, completed_at FROM attempt_completions WHERE attempt_id = $p0", transaction, attemptId);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5)) : null;
    }

    private NativeSubmission RequireCompletionBinding(string attemptId, SqliteTransaction transaction)
    {
        var attempt = RequireCurrentAttempt(attemptId, transaction);
        var submission = ReadSubmission(attemptId, transaction);
        if (submission is not { State: "correlated", RunId: not null })
            throw new SubmissionNotReady("Completion requires an already-correlated native run; an unacknowledged submission is resumed, never waited on.");
        // Reconstruct from admitted facts and frozen execution settings, never today's workspace/configuration.
        ValidateFrozenInvocation(attempt, submission, transaction);
        return submission;
    }

    /// <summary>Wait without a writer reservation; pin the accepted commit, then retain receipt, disposition and authority loss atomically.</summary>
    /// <remarks>An HTTP record uses its retained binding and ignores any bridge transport.</remarks>
    public async Task<AttemptCompletion> WaitAsync(string attemptId, INativeReader? transport, CancellationToken cancellationToken = default)
    {
        if (FindCompletion(attemptId) is { } existing) return existing;
        NativeSubmission submitted;
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            if (ReadCompletion(attemptId, transaction) is { } retained) return retained;
            submitted = RequireCompletionBinding(attemptId, transaction);
            transaction.Commit();
        }
        // Cancellation and transport errors detach the caller; neither abandons nor requests stop.
        var result = submitted.Format == NativeSubmission.Http
            ? await DirectTargetRun.WaitAsync(submitted.Run!, directTargetRoot, DirectTargetClock, cancellationToken)
            : await (transport ?? throw new SubmissionNotReady("Waiting on a bridge run requires native transport."))
                .WaitAsync(submitted.Run!, cancellationToken);
        if (result.RunId != submitted.RunId) throw new SubmissionConflict("The result belongs to another native run.");
        if (!result.Succeeded)
        {
            var reason = "Zeroshot run failed: " + result.Failure;
            AbandonAttempt(attemptId, reason);
            throw new SubmissionNotReady(reason);
        }
        var receipt = AcceptedReceipt(submitted, result.Output);
        // Outside any writer: a failed pin or write leaves the Attempt current to consume this result again.
        var frozen = submitted.Frozen;
        try
        {
            await GitCustody.RetainAcceptedAsync(frozen.Repository, frozen.OriginUrl,
                result.Output.GetProperty("headRevision").GetString()!, cancellationToken);
        }
        catch (UnsupportedStartingState error) { throw new ResultRetentionError(error.Message); }
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            if (ReadCompletion(attemptId, transaction) is { } retained) return retained;
            var current = RequireCompletionBinding(attemptId, transaction);
            if (current != submitted) throw new SubmissionConflict("The result lost its immutable Attempt/run binding.");
            var attempt = ReadAttempt(attemptId, transaction);
            Execute("INSERT INTO attempt_completions VALUES ($p0, $p1, $p2, $p3, $p4, $p5)", transaction,
                attemptId, attempt.WorkUnitId, attempt.ContractRevisionId, result.RunId, receipt, Now());
            var completed = ReadCompletion(attemptId, transaction)!;
            transaction.Commit();
            return completed;
        }
    }

    private static string AcceptedReceipt(NativeSubmission submitted, JsonElement output)
    {
        var frozen = submitted.Frozen;
        if (frozen.Delivery == "none")
            throw new SubmissionNotReady("Zeroshot 10.3 no-effect runs provide no stable accepted result; local result handoff is an upstream capability gap.");
        string[] fields = ["version", "mode", "outcome", "repository", "targetBranch", "headRevision", "pullRequestId"];
        if (output.ValueKind != JsonValueKind.Object || output.EnumerateObject().Count() != fields.Length
            || fields.Any(field => !output.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String))
            throw new SubmissionConflict("The successful run returned no complete authorized delivery receipt.");
        var head = output.GetProperty("headRevision").GetString()!;
        var pr = output.GetProperty("pullRequestId").GetString()!;
        var source = frozen.Source;
        if (frozen.Delivery != "pull_request" || source is null
            || output.GetProperty("version").GetString() != "v1"
            || output.GetProperty("mode").GetString() != "pr"
            || output.GetProperty("outcome").GetString() != "opened"
            || output.GetProperty("repository").GetString() != source.Repository
            || output.GetProperty("targetBranch").GetString() != source.Branch
            || head.Length != 40 || head.Any(character => !"0123456789abcdef".Contains(character))
            || head == source.Revision || pr.Length == 0 || pr.Any(character => character is < '0' or > '9'))
            throw new SubmissionConflict("The delivery receipt does not match frozen PR authority.");
        return output.GetRawText();
    }
}
