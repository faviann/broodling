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

/// <summary>
/// The correlated run's terminal result can never complete this Attempt: it names another run or its
/// receipt does not match frozen PR authority, and reading the same run returns the same result. It ends
/// automatic observation only; the Attempt keeps its authority and disposition.
/// </summary>
public sealed record CompletionRefusal(string AttemptId, string Reason, string RefusedAt);

public sealed partial class BroodlingStore
{
    /// <summary>Exact historical lookup, with no current/latest substitution or native contact.</summary>
    public AttemptCompletion? FindCompletion(string attemptId) => ReadCompletion(attemptId);

    /// <summary>Correlated HTTP Attempts that still hold authority and have no retained refusal.</summary>
    internal IReadOnlyList<string> ObservableAttempts()
    {
        using var command = Command("""
            SELECT attempt_id FROM attempts JOIN native_submissions USING (attempt_id)
            WHERE is_current = 1 AND format = 'http.v1' AND state = 'correlated'
              AND attempt_id NOT IN (SELECT attempt_id FROM completion_refusals)
            ORDER BY attempts.rowid
            """);
        using var row = command.ExecuteReader();
        var result = new List<string>();
        while (row.Read()) result.Add(row.GetString(0));
        return result;
    }

    /// <summary>Retain a refusal for a still-current Attempt; ended authority already has its disposition.</summary>
    internal void RefuseCompletion(string attemptId, string reason)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        if (ReadAttempt(attemptId, transaction).IsCurrent)
            Execute("INSERT INTO completion_refusals VALUES ($p0, $p1, $p2) ON CONFLICT (attempt_id) DO NOTHING",
                transaction, attemptId, reason, Now());
        transaction.Commit();
    }

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
        if (result.RunId != submitted.RunId) throw new ReceiptRefused("The result belongs to another native run.");
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
            throw new ReceiptRefused("The successful run returned no complete authorized delivery receipt.");
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
            throw new ReceiptRefused("The delivery receipt does not match frozen PR authority.");
        return output.GetRawText();
    }
}
