using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// One Attempt's retained HTTP submission. <see cref="State"/> is its monotonic phase; any phase after
/// <c>prepared</c> is committed dispatch intent. <see cref="RunId"/> is confirmed correlation only.
/// <see cref="IntendedRunId"/> never implies acceptance; <see cref="ReplayBlockedReason"/> is monotonic;
/// the asset identity and Broodling-only binding are retained with the frozen request.
/// </summary>
public sealed record NativeSubmission(string AttemptId, string SubmissionKey, string RequestJson, string State, string? RunId,
    string IntendedRunId, string? ReplayBlockedReason, string AssetSha256, string BindingJson)
{
    /// <summary>The retained DirectTarget origin this submission was prepared for.</summary>
    public string Origin => Frozen.Origin;
    internal FrozenSubmission Frozen => FrozenSubmission.Read(RequestJson, BindingJson);
    /// <summary>The retained environment and target, which the current catalog must still serve to be contacted.</summary>
    public RetainedEnvironment Environment => RetainedEnvironment.Read(BindingJson);
    /// <summary>The retained binding for read/stop; null until correlation.</summary>
    internal NativeRunBinding? Run => RunId is null ? null : Frozen.Run(RunId);
}

public sealed partial class BroodlingStore
{
    public NativeSubmission? FindSubmission(string attemptId) => ReadSubmission(attemptId);

    private NativeSubmission? ReadSubmission(string attemptId, SqliteTransaction? transaction = null)
    {
        using var command = Command("""
            SELECT attempt_id, submission_key, request_json, state, run_id, intended_run_id,
                replay_blocked_reason, asset_sha256, binding_json FROM native_submissions WHERE attempt_id = $p0
            """, transaction, attemptId);
        using var row = command.ExecuteReader();
        string? Optional(int index) => row.IsDBNull(index) ? null : row.GetString(index);
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), Optional(4),
            row.GetString(5), Optional(6), row.GetString(7), row.GetString(8)) : null;
    }

    /// <summary>
    /// The largest task that preparation admits, measured as JSON-escaped UTF-8: an upper bound on what native writes
    /// into each node input. Native bounds a node input at 1 MiB (its ledger event); the other half is left for
    /// node instructions, the response contract and repair feedback.
    /// </summary>
    internal const int NativeTaskBytes = 512 * 1024;

    /// <summary>Separates the task's instructions from its authority JSON.</summary>
    private const string AuthoritySeparator = "\n\n";

    /// <summary>
    /// The task's JSON keeps HTML characters and most other text as written; it still writes characters outside the
    /// Basic Multilingual Plane, line and paragraph separators and some others as \u escapes.
    /// </summary>
    private static readonly JsonSerializerOptions TaskJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The complete frozen task: admitted Contract, exact entitled bytes and original B1, from retained authority only.
    /// </summary>
    private (string Task, WorkUnit Work, string TargetBranch) AdmittedTask(AttemptRecord attempt, SqliteTransaction transaction)
    {
        var revision = ReadRevision(attempt.ContractRevisionId, transaction) ?? throw new UnknownRecord("Unknown Contract revision.");
        if (ReadDecision(attempt.ContractRevisionId, transaction)?.Admitted != true)
            throw new SubmissionNotReady("The Attempt Contract is not admitted.");
        var work = ReadWorkUnit(attempt.WorkUnitId, transaction)!;
        var pins = revision.Contract.SourceAttribution;
        var material = Digests.AdmittedMaterial(pins);
        if (material != attempt.B1.MaterialSha256) throw new SubmissionConflict("Admitted B1 source material changed.");
        var instructions = new JsonArray();
        foreach (var pin in pins)
        {
            var source = ReadSource(pin.SourceId, transaction);
            if (source.WorkUnitId != work.WorkUnitId || source.ContentSha256 != pin.ContentSha256 || Digests.Bytes(source.Content) != pin.ContentSha256)
                throw new SubmissionConflict("Frozen source bytes changed.");
            var (encoding, content) = TaskContent(source.Content);
            instructions.Add(new JsonObject { ["sourceId"] = source.SourceId, ["kind"] = source.Kind, ["locator"] = source.Locator,
                ["mediaType"] = source.MediaType, ["contentSha256"] = source.ContentSha256, ["encoding"] = encoding, ["content"] = content });
        }
        string targetBranch;
        try { targetBranch = Closability.AuthorizeDelivery(revision.Contract); }
        catch (InvalidContractProposal) { throw new SubmissionNotReady("The frozen effect authority is unsupported."); }
        if (work.Host != "github.com")
            throw new SubmissionNotReady("The frozen effect authority is unsupported.");
        var authority = new JsonObject { ["contract"] = JsonNode.Parse(revision.CanonicalBytes), ["admittedInstructions"] = instructions, ["comparisonBase"] = attempt.B1.CommitOid };
        var task = "Complete this admitted software-development Work Unit. The frozen Contract and entitled source material below govern scope and acceptance. "
            + "Candidate edits cannot amend that authority. Implement the criteria and run declared/relevant checks; independently verify the actual outcome. "
            + "The sole authorized external effect is native pull-request delivery. Do not publish, push, create or update a PR, merge, change issues, deploy, or perform other authoritative effects yourself; the native delivery node alone owns the authorized PR effect."
            + AuthoritySeparator + authority.ToJsonString(TaskJson);
        // This JSON escapes at least what native's serde_json does, so its size bounds what native writes.
        var size = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(task, TaskJson));
        if (size > NativeTaskBytes)
            throw new NativeTaskTooLarge($"The native task needs {size} bytes; the supported limit is {NativeTaskBytes} bytes.");
        return (task, work, targetBranch);
    }

    /// <summary>Exact bytes as task text: strict UTF-8 as itself, anything else as base64.</summary>
    private static (string Encoding, string Content) TaskContent(byte[] bytes)
    {
        try { return ("utf-8", new UTF8Encoding(false, true).GetString(bytes)); }
        catch (DecoderFallbackException) { return ("base64", Convert.ToBase64String(bytes)); }
    }
}
