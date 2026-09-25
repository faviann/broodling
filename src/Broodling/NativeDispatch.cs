using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// One Attempt's retained submission. <see cref="State"/> is its monotonic phase; any phase after
/// <c>prepared</c> is committed dispatch intent. <see cref="RunId"/> is confirmed correlation only.
/// An <c>http.v1</c> record also carries its <see cref="IntendedRunId"/>, which never implies
/// acceptance, a monotonic <see cref="ReplayBlockedReason"/>, its retained asset identity and
/// Broodling-only binding; a <c>bridge</c> record has none of these.
/// </summary>
public sealed record NativeSubmission(string AttemptId, string SubmissionKey, string RequestJson, string State, string? RunId,
    string Format = NativeSubmission.Bridge, string? IntendedRunId = null, string? ReplayBlockedReason = null,
    string? AssetSha256 = null, string? BindingJson = null)
{
    public const string Bridge = "bridge";
    public const string Http = "http.v1";

    public NativeLocator Locator => Frozen.Locator;
    internal FrozenSubmission Frozen => Format == Http ? FrozenSubmission.ReadHttp(RequestJson, BindingJson!) : FrozenSubmission.Read(RequestJson);
    /// <summary>The retained binding for read/stop; null until correlation.</summary>
    internal NativeRunBinding? Run => RunId is null ? null : Frozen.Run(RunId);
}

public sealed partial class BroodlingStore
{
    public NativeSubmission? FindSubmission(string attemptId) => ReadSubmission(attemptId);

    private NativeSubmission? ReadSubmission(string attemptId, SqliteTransaction? transaction = null)
    {
        using var command = Command("""
            SELECT attempt_id, submission_key, request_json, state, run_id, format, intended_run_id,
                replay_blocked_reason, asset_sha256, binding_json FROM native_submissions WHERE attempt_id = $p0
            """, transaction, attemptId);
        using var row = command.ExecuteReader();
        string? Optional(int index) => row.IsDBNull(index) ? null : row.GetString(index);
        return row.Read() ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), Optional(4),
            row.GetString(5), Optional(6), Optional(7), Optional(8), Optional(9)) : null;
    }

    /// <summary>The Python bridge path serves worktree Attempts only; it never reads an HTTP record as a bridge request.</summary>
    private static void RequireBridgeAttempt(AttemptRecord attempt)
    {
        if (attempt.ResourceKind == AttemptRecord.Http)
            throw new SubmissionNotReady("An HTTP Attempt uses HTTP submission preparation, not the bridge.");
    }

    /// <summary>Freeze admitted facts and supported policy before any external submission.</summary>
    public NativeSubmission PrepareSubmission(string attemptId, NativeProfile profile)
    {
        var attempt = GetAttempt(attemptId);
        RequireBridgeAttempt(attempt);
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
        Execute("""
            INSERT INTO native_submissions (attempt_id, format, submission_key, request_json, state)
            VALUES ($p0, 'bridge', $p1, $p2, 'prepared')
            """, transaction, attemptId, "broodling:dotnet:v1:" + attemptId, request);
        var result = ReadSubmission(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>Durable intent, external call without SQLite writer, then convergent factual correlation.</summary>
    public async Task<NativeSubmission> DispatchAsync(string attemptId, NativeProfile profile, INativeTransport transport,
        CancellationToken cancellationToken = default)
    {
        var attempt = RequireCurrentAttempt(attemptId);
        RequireBridgeAttempt(attempt);
        var record = FindSubmission(attemptId) ?? PrepareSubmission(attemptId, profile);
        if (record.State == "correlated") return record; // No old workspace/profile/credential dependency.
        if (record.State == "blocked") throw new SubmissionConflict("The native submission has a retained conflict.");
        RequireUnpaused();
        var frozen = record.Frozen;
        // Version/executable checks may be slow. Never hold the SQLite writer for them.
        profile.ValidateDispatch(record, attempt);
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
            if (!JsonNode.DeepEquals(JsonNode.Parse(record.RequestJson)!["target"], profile.Target(frozen.Delivery)))
                throw new SubmissionConflict("The configured execution target changed.");
            ValidateDispatchSource(attempt, requireB1: record.State == "prepared");
            RequireOrigin(attempt, frozen);
            if (record.State == "prepared")
                Execute("UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = $p0", transaction, attemptId);
            transaction.Commit();
        }

        string? runId = null;
        SubmissionConflict? conflict = null;
        try
        {
            runId = transport is IInitiationAwareNativeTransport aware
                ? await aware.SubmitAsync(record.RequestJson, initiation, cancellationToken)
                : await transport.SubmitAsync(record.RequestJson, cancellationToken);
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
                RequireOrigin(attempt, frozen);
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
        if (stale)
        {
            // Correlation is committed before this handoff. Reuse the exact
            // Attempt/run binding and existing stop semantics; never replay the
            // submission to discover or replace a lost acknowledgment.
            var nativeStopRequested = false;
            try
            {
                await StopAsync(attemptId, "Native acknowledgment arrived after Attempt abandonment.", transport, cancellationToken);
            }
            catch (CessationUnconfirmed cessation)
            {
                nativeStopRequested = cessation.NativeStopRequested;
            }
            var observation = nativeStopRequested
                ? "native stop was requested, but physical cessation remains unconfirmed."
                : "native stop was not requested because cessation could not be safely addressed; physical cessation remains unconfirmed.";
            throw new StaleAttempt($"Authority was lost while native acknowledgment was in flight; factual correlation is retained and {observation}",
                nativeStopRequested);
        }
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

    private static void RequireOrigin(AttemptRecord attempt, FrozenSubmission frozen)
    {
        if (Origin(attempt) != frozen.OriginUrl)
            throw new SubmissionConflict("The source origin changed.");
    }

    private void ValidateFrozenInvocation(AttemptRecord attempt, NativeSubmission record, SqliteTransaction transaction)
    {
        if (record.Format == NativeSubmission.Http)
        {
            RequireRetainedHttpSubmission(attempt, record, transaction);
            return;
        }
        var frozen = JsonNode.Parse(record.RequestJson)!.AsObject();
        if (record.SubmissionKey != "broodling:dotnet:v1:" + attempt.AttemptId
            || BuildInvocation(attempt, transaction, frozen: frozen) != record.RequestJson)
            throw new SubmissionConflict("The retained invocation differs from immutable admitted authority.");
    }

    private string BuildInvocation(AttemptRecord attempt, SqliteTransaction transaction, NativeProfile? profile = null, JsonObject? frozen = null)
    {
        var (task, _, authorization) = AdmittedTask(attempt, transaction);
        var delivery = authorization.Mode;
        var request = new JsonObject
        {
            ["submissionKey"] = "broodling:dotnet:v1:" + attempt.AttemptId, ["title"] = "Broodling Attempt " + attempt.AttemptId,
            ["task"] = task, ["preset"] = new JsonObject { ["name"] = "software-change", ["delivery"] = delivery },
            ["runtime"] = NativeProfile.Runtime(), ["workspace"] = attempt.Allocation.WorktreePath,
            ["repository"] = attempt.B1.Repository, ["branch"] = attempt.Allocation.Branch, ["startingCommit"] = attempt.B1.CommitOid,
            ["materialSha256"] = attempt.B1.MaterialSha256,
            ["originUrl"] = frozen is null ? Origin(attempt) : (string)frozen["originUrl"]!,
            ["target"] = frozen is null ? profile!.Target(delivery) : frozen["target"]!.DeepClone()
        };
        if (ReadRetry("attempt_id", attempt.AttemptId, transaction) is { } retry
            && (retry.TargetJson is null || !JsonNode.DeepEquals(JsonNode.Parse(retry.TargetJson), request["target"])))
            throw new AttemptConflict("The replacement target differs from its durable retry request.");
        return request.ToJsonString();
    }

    /// <summary>The DirectTarget image's read-only helper for one captured reference of a sealed RequestBundle.</summary>
    internal const string ReferenceReader = "/usr/local/bin/broodling-reference";

    /// <summary>
    /// The complete frozen task: admitted Contract, exact entitled bytes and original B1, from retained authority only.
    /// A bundle-bound Contract also carries its compact manifest and on-demand reference access; without
    /// <paramref name="referenceAccess"/> it is the projection earlier releases froze for the same authority.
    /// </summary>
    private (string Task, WorkUnit Work, DeliveryAuthorization Authorization) AdmittedTask(AttemptRecord attempt, SqliteTransaction transaction,
        bool referenceAccess = true)
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
                : "The sole authorized external effect is native pull-request delivery. Do not publish, push, create or update a PR, merge, change issues, deploy, or perform other authoritative effects yourself; the native delivery node alone owns the authorized PR effect.");
        if (referenceAccess && revision.Contract.RequestBundle is { } binding)
        {
            authority["requestBundle"] = CompactManifest(binding, transaction);
            task += " The requestBundle below lists the available references of this Work Unit's RequestBundle without their bodies. "
                + $"Read one when you need it with `{ReferenceReader} <bundleId> <referenceId>`, which prints its exact captured bytes. "
                + "A reference is material to consult: it adds no requested work, cannot amend the Contract or Executable Request, and authorizes no effect.";
        }
        return (task + "\n\n" + authority.ToJsonString(), work, authorization);
    }

    /// <summary>
    /// The bound bundle's references, identities and digests without bodies, from its digest-verified
    /// retained manifest. Capture writes JSON selectors; any other selector keeps its manifest base64.
    /// </summary>
    private JsonObject CompactManifest(ContractRequestBundle binding, SqliteTransaction transaction)
    {
        var bundle = ReadRequestBundleById(binding.BundleId, transaction);
        if (bundle is not { State: "complete", ManifestJson: { } json } || bundle.ManifestSha256 != binding.ManifestSha256
            || Digests.Bytes(Encoding.UTF8.GetBytes(json)) != binding.ManifestSha256)
            throw new SubmissionConflict("The bound RequestBundle manifest differs from admitted authority.");
        var references = new JsonArray();
        foreach (var reference in JsonSerializer.Deserialize<BundleManifestV1>(json, BundleManifestOptions)!.References)
        {
            var entry = new JsonObject { ["referenceId"] = reference.ReferenceId, ["captureKind"] = reference.CaptureKind };
            try { entry["selector"] = JsonNode.Parse(Convert.FromBase64String(reference.Selector)); }
            catch (Exception error) when (error is JsonException or ArgumentException) { entry["selectorBase64"] = reference.Selector; }
            entry["contentSha256"] = reference.ContentSha256;
            if (reference.GitPath is not null)
            {
                entry["gitCommitOid"] = reference.GitCommitOid;
                entry["gitPath"] = reference.GitPath;
            }
            references.Add(entry);
        }
        return new JsonObject { ["bundleId"] = binding.BundleId, ["manifestSha256"] = binding.ManifestSha256, ["references"] = references };
    }
}
