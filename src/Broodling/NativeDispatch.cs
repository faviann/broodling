using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed record NativeSubmission(string AttemptId, string SubmissionKey, string RequestJson, string State, string? RunId)
{
    public NativeLocator Locator => NativeLocator.Read(JsonNode.Parse(RequestJson)!["target"]!["locator"]!);
}

public sealed partial class BroodlingStore
{
    public NativeSubmission? FindSubmission(string attemptId) => ReadSubmission(attemptId);

    private NativeSubmission? ReadSubmission(string attemptId, SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT attempt_id, submission_key, request_json, state, run_id FROM native_submissions WHERE attempt_id = $p0", transaction, attemptId);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4)) : null;
    }

    /// <summary>Freeze admitted facts and supported policy before any external submission.</summary>
    public NativeSubmission PrepareSubmission(string attemptId, NativeProfile profile)
    {
        var attempt = GetAttempt(attemptId);
        using var held = DispatchLock(attempt);
        using var transaction = connection.BeginTransaction(deferred: false);
        attempt = RequireCurrentAttempt(attemptId, transaction);
        RequireUnpausedUnlessReplacement(attempt, transaction);
        var request = BuildInvocation(attempt, transaction, profile);
        var previous = ReadSubmission(attemptId, transaction);
        if (previous is not null)
        {
            if (request != previous.RequestJson) throw new SubmissionConflict("The invocation differs from the retained request.");
            transaction.Commit();
            return previous;
        }
        ValidateDispatchSource(attempt, requireB1: true);
        Execute("INSERT INTO native_submissions VALUES ($p0, $p1, $p2, 'prepared', NULL)", transaction,
            attemptId, "broodling:dotnet:v1:" + attemptId, request);
        var result = ReadSubmission(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>Durable intent, external call without SQLite writer, then convergent factual correlation.</summary>
    public async Task<NativeSubmission> DispatchAsync(string attemptId, NativeProfile profile, INativeTransport transport,
        DispatchCredentials? credentials = null, CancellationToken cancellationToken = default)
    {
        var attempt = RequireCurrentAttempt(attemptId);
        var record = FindSubmission(attemptId) ?? PrepareSubmission(attemptId, profile);
        if (record.State == "correlated") return record; // No old workspace/profile/credential dependency.
        if (record.State == "blocked") throw new SubmissionConflict("The native submission has a retained conflict.");
        RequireUnpaused();
        var request = JsonNode.Parse(record.RequestJson)!.AsObject();
        // Version/executable/credential checks may be slow. Never hold the SQLite writer for them.
        var ephemeral = profile.ValidateDispatch(request, attempt, credentials);
        // Held from before the dispatched intent until the transport can no longer submit.
        using var initiation = HoldInitiation();
        using (var held = DispatchLock(attempt))
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            attempt = RequireCurrentAttempt(attemptId, transaction);
            record = ReadSubmission(attemptId, transaction)!;
            if (record.State == "correlated") { transaction.Commit(); return record; }
            if (record.State == "blocked") throw new SubmissionConflict("The native submission has a retained conflict.");
            // Replaying a dispatched intent can still create the run, so it is a dispatch too.
            RequireUnpaused(transaction);
            ValidateFrozenInvocation(attempt, record, transaction);
            if (!JsonNode.DeepEquals(request["target"], profile.Target((string)request["preset"]!["delivery"]!)))
                throw new SubmissionConflict("The configured execution target changed.");
            ValidateDispatchSource(attempt, requireB1: record.State == "prepared");
            RequireOrigin(attempt, request);
            if (record.State == "prepared")
                Execute("UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = $p0", transaction, attemptId);
            transaction.Commit();
        }

        string? runId = null;
        SubmissionConflict? conflict = null;
        try
        {
            runId = transport is IInitiationAwareNativeTransport aware
                ? await aware.SubmitAsync(record.RequestJson, ephemeral, initiation, cancellationToken)
                : await transport.SubmitAsync(record.RequestJson, ephemeral, cancellationToken);
        }
        catch (SubmissionConflict error) { conflict = error; }
        if (conflict is null && string.IsNullOrWhiteSpace(runId))
            throw new NativeTransportError(); // Remains durably dispatched and unresolved.

        bool stale;
        NativeSubmission settled;
        using (var held = DispatchLock(attempt))
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            attempt = ReadAttempt(attemptId, transaction); // Late acknowledgment can retain facts after abandonment.
            settled = ReadSubmission(attemptId, transaction)!;
            if (settled.RequestJson != record.RequestJson)
                throw new SubmissionConflict("The frozen invocation changed during dispatch.");
            ValidateFrozenInvocation(attempt, settled, transaction);
            if (settled.State == "correlated")
            {
                if (conflict is null ? runId != settled.RunId
                    : !string.IsNullOrWhiteSpace(conflict.ExistingRunId) && conflict.ExistingRunId != settled.RunId)
                    throw new SubmissionConflict("Concurrent acknowledgments disagree on native run identity.");
                conflict = null;
            }
            else
            {
                if (settled.State != "dispatched") throw new SubmissionConflict("The native submission has a retained conflict.");
                var drifted = ValidateDispatchSource(attempt, requireB1: false);
                RequireOrigin(attempt, request);
                if (conflict is not null && drifted && !string.IsNullOrWhiteSpace(conflict.ExistingRunId))
                {
                    runId = conflict.ExistingRunId;
                    conflict = null;
                }
                // A message/run ID alone never proves owned HEAD drift.
                if (conflict is not null)
                    Execute("UPDATE native_submissions SET state = 'blocked' WHERE attempt_id = $p0", transaction, attemptId);
                else
                    Execute("UPDATE native_submissions SET state = 'correlated', run_id = $p0 WHERE attempt_id = $p1", transaction, runId, attemptId);
                settled = ReadSubmission(attemptId, transaction)!;
            }
            stale = !attempt.IsCurrent || attempt.Abandonment is not null;
            transaction.Commit();
        }
        if (conflict is not null) throw conflict;
        if (stale) throw new StaleAttempt("Authority was lost while native acknowledgment was in flight; factual correlation is retained.");
        return settled;
    }

    private AdministrativeGitProcess.EnclosureLock DispatchLock(AttemptRecord attempt)
    {
        // Do not create scaffolding to inspect/replay a dispatched candidate.
        // Like C, inspect settled Git only after acquiring the stable enclosure lock.
        if (attempt.Provision is null) throw new SubmissionNotReady("The Attempt has no materialization acknowledgment.");
        WorktreeMaterialization.ValidatePaths(attempt, Path, inspectGit: false);
        WorktreeMaterialization.RequireMarker(attempt);
        return AdministrativeGitProcess.EnclosureLock.Acquire(System.IO.Path.Combine(attempt.Allocation.Enclosure, WorktreeMaterialization.LockName));
    }

    private bool ValidateDispatchSource(AttemptRecord attempt, bool requireB1)
    {
        WorktreeMaterialization.RequireOwned(attempt, Path);
        var path = attempt.Allocation.WorktreePath;
        if (requireB1)
        {
            GitCustody.AssertSupportedCheckout(attempt.B1.Repository, attempt.B1.CommitOid);
            GitCustody.AssertSupportedCheckout(path, attempt.B1.CommitOid);
        }
        var drifted = GitCustody.Text(path, "rev-parse", "HEAD").Trim() != attempt.B1.CommitOid;
        if (requireB1 && (drifted || GitCustody.Text(path, "status", "--porcelain=v1", "--untracked-files=all", "--no-renames").Length != 0))
            throw new SubmissionNotReady("First dispatch requires the clean admitted B1.");
        return drifted;
    }

    private static string Origin(AttemptRecord attempt)
    {
        var value = GitCustody.Run(attempt.Allocation.WorktreePath, ["config", "--get", "remote.origin.url"]);
        if (value.ExitCode == 1) return "";
        if (value.ExitCode != 0) throw new SubmissionConflict("Cannot inspect the frozen source origin.");
        return Encoding.UTF8.GetString(value.Output).Trim();
    }

    private static void RequireOrigin(AttemptRecord attempt, JsonObject request)
    {
        if (Origin(attempt) != (string?)request["originUrl"])
            throw new SubmissionConflict("The source origin changed.");
    }

    private void ValidateFrozenInvocation(AttemptRecord attempt, NativeSubmission record, SqliteTransaction transaction)
    {
        var frozen = JsonNode.Parse(record.RequestJson)!.AsObject();
        if (record.SubmissionKey != "broodling:dotnet:v1:" + attempt.AttemptId
            || BuildInvocation(attempt, transaction, frozen: frozen) != record.RequestJson)
            throw new SubmissionConflict("The retained invocation differs from immutable admitted authority.");
    }

    private string BuildInvocation(AttemptRecord attempt, SqliteTransaction transaction, NativeProfile? profile = null, JsonObject? frozen = null)
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
            string content, encoding;
            try { content = new UTF8Encoding(false, true).GetString(source.Content); encoding = "utf-8"; }
            catch (DecoderFallbackException) { content = Convert.ToBase64String(source.Content); encoding = "base64"; }
            instructions.Add(new JsonObject { ["sourceId"] = source.SourceId, ["kind"] = source.Kind, ["locator"] = source.Locator,
                ["mediaType"] = source.MediaType, ["contentSha256"] = source.ContentSha256, ["encoding"] = encoding, ["content"] = content });
        }
        DeliveryAuthorization authorization;
        try { authorization = Closability.AuthorizeDelivery(revision.Contract); }
        catch (InvalidContractProposal) { throw new SubmissionNotReady("The frozen effect authority is unsupported."); }
        var delivery = authorization.Mode;
        if (delivery == "pull_request" && work.Host != "github.com")
            throw new SubmissionNotReady("The frozen effect authority is unsupported.");
        var authority = new JsonObject { ["contract"] = JsonNode.Parse(revision.CanonicalBytes), ["admittedInstructions"] = instructions, ["comparisonBase"] = attempt.B1.CommitOid };
        var task = "Complete this admitted software-development Work Unit. The frozen Contract and entitled source material below govern scope and acceptance. "
            + "Candidate edits cannot amend that authority. Implement the criteria and run declared/relevant checks; independently verify the actual outcome. "
            + (delivery == "none" ? "The required-effect set is empty. Keep changes in this assigned worktree."
                : "The sole authorized external effect is native pull-request delivery. Do not publish, push, create or update a PR, merge, change issues, deploy, or perform other authoritative effects yourself; the native delivery node alone owns the authorized PR effect.")
            + "\n\n" + authority.ToJsonString();
        var request = new JsonObject
        {
            ["submissionKey"] = "broodling:dotnet:v1:" + attempt.AttemptId, ["title"] = "Broodling Attempt " + attempt.AttemptId,
            ["task"] = task, ["preset"] = new JsonObject { ["name"] = "software-change", ["delivery"] = delivery },
            ["runtime"] = NativeProfile.Runtime(delivery), ["workspace"] = attempt.Allocation.WorktreePath,
            ["repository"] = attempt.B1.Repository, ["branch"] = attempt.Allocation.Branch, ["startingCommit"] = attempt.B1.CommitOid,
            ["materialSha256"] = attempt.B1.MaterialSha256,
            ["originUrl"] = frozen is null ? Origin(attempt) : (string)frozen["originUrl"]!,
            ["target"] = frozen is null ? profile!.Target(delivery) : frozen["target"]!.DeepClone()
        };
        if (delivery == "pull_request") request["source"] = new JsonObject
        {
            ["repository"] = work.Owner + "/" + work.Repository, ["branch"] = authorization.TargetBranch, ["revision"] = attempt.B1.CommitOid
        };
        if (ReadRetry("attempt_id", attempt.AttemptId, transaction) is { } retry
            && !JsonNode.DeepEquals(JsonNode.Parse(retry.TargetJson), request["target"]))
            throw new AttemptConflict("The replacement target differs from its durable retry request.");
        return request.ToJsonString();
    }
}
