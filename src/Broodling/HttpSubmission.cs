using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Zeroshot;

namespace Broodling;

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Freeze one complete, secret-free stock DirectTarget request for an HTTP Attempt, with no
    /// target contact or dispatch credentials. The prepared record is the preparation fact: it
    /// retains the approved asset bytes, intended run identity, exact bindings and the Attempt's environment,
    /// resolved once from the declaration at original B1 against <paramref name="environments"/>. It implies
    /// neither dispatch nor acceptance. Repeating it returns the first committed record, validated against
    /// retained content only, whatever the catalog now says.
    /// </summary>
    public NativeSubmission PrepareHttpSubmission(string attemptId, EnvironmentCatalog environments) =>
        PrepareHttpSubmission(attemptId, environments, ExecutionAsset.LoadBundled);

    internal NativeSubmission PrepareHttpSubmission(string attemptId, EnvironmentCatalog environments, Func<ExecutionAsset> installed)
    {
        var attempt = RequireCurrentAttempt(attemptId);
        ExecutionAsset? asset = null;
        EnvironmentResolution? environment = null;
        string? resultOrigin = null;
        if (FindSubmission(attemptId) is null)
        {
            // Git and SQLite cannot share a transaction. A crash may leave this exact pin without
            // a prepared record; it grants nothing, and a later preparation reuses it.
            GitCustody.Retain(new(attempt.B1.Repository, attempt.B1.CommitOid, attempt.B1.RequestedRevision));
            // Only a new preparation reads the declaration and the installed asset; a retained record carries
            // both. A refusal leaves no record.
            environment = InheritsFrom(attempt) is { } inherited
                ? ReplacementEnvironment(inherited.PredecessorId, inherited.EnvironmentUpdate, environments)
                : environments.Resolve(EnvironmentDeclaration.Read(attempt.B1.Repository, attempt.B1.CommitOid));
            asset = installed();
            resultOrigin = ResultOrigin(attempt, ReadWorkUnit(attempt.WorkUnitId)!);
        }
        using var transaction = connection.BeginTransaction(deferred: false);
        attempt = RequireCurrentAttempt(attemptId, transaction);
        RequireUnpausedUnlessReplacement(attempt, transaction);
        if (ReadSubmission(attemptId, transaction) is { } previous)
        {
            RequireRetainedHttpSubmission(attempt, previous, transaction);
            transaction.Commit();
            return previous; // A losing concurrent preparer adopts the first record and its environment; it never invents another.
        }
        // A successor of work retired under verified maintenance is first prepared only within a maintenance pause.
        if (attempt.Retry is { } retry && ReadRetirement(retry.PredecessorId, transaction)?.Basis == "stopped_target")
            RequireMaintenancePause(transaction);
        var intended = Guid.CreateVersion7().ToString();
        Execute("INSERT OR IGNORE INTO execution_assets VALUES ($p0, $p1)", transaction, asset!.Sha256, asset.Content());
        // A row retained by an earlier preparation is reused, so it must still be the approved content.
        if (RetainedAsset(asset.Sha256, transaction) is null) throw RetainedDiffers();
        foreach (var document in environment!.Documents)
        {
            Execute("INSERT OR IGNORE INTO environment_documents VALUES ($p0, $p1)", transaction, Digests.Bytes(document), document);
            if (RetainedDocument(Digests.Bytes(document), transaction) is null) throw RetainedDiffers();
        }
        Execute("""
            INSERT INTO native_submissions (attempt_id, submission_key, request_json, state, intended_run_id, asset_sha256, binding_json)
            VALUES ($p0, $p1, $p2, 'prepared', $p3, $p4, $p5)
            """, transaction, attemptId, HttpSubmissionKey(attemptId), RetainedRequest(HttpRequest(attempt, transaction, intended, asset)),
            intended, asset.Sha256, HttpBinding(attempt, resultOrigin!, environment).ToJsonString());
        var result = ReadSubmission(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// The retry whose predecessor's retained environment a successor's environment comes from; null for a first Attempt
    /// and for a successor of a predecessor that was never prepared, which resolves like a first Attempt.
    /// </summary>
    private AttemptRetry? InheritsFrom(AttemptRecord attempt) =>
        attempt.Retry is { } retry && ReadSubmission(retry.PredecessorId) is not null ? retry : null;

    /// <summary>
    /// A successor's environment. By default exactly its predecessor's retained resolution, refused as
    /// <c>environment_changed</c> unless <paramref name="environments"/> still serves it exactly. With
    /// <paramref name="update"/>, the operator's recorded sign-off: the predecessor's requirement, copied as retained,
    /// resolved to what the catalog now names for the predecessor's environment, which therefore still satisfies it.
    /// </summary>
    private EnvironmentResolution ReplacementEnvironment(string predecessorId, bool update, EnvironmentCatalog environments,
        SqliteTransaction? transaction = null)
    {
        var record = ReadSubmission(predecessorId, transaction) ?? throw new SubmissionNotReady("The predecessor has no retained environment.");
        if (!update)
        {
            var inherited = RetainedEnvironment(ReadAttempt(predecessorId, transaction), record, transaction);
            if (environments.Change(record.Environment) is { } change)
                throw new EnvironmentRefused("environment_changed", $"The predecessor's environment changed: {change}. "
                    + "Its successor inherits its exact environment; replace it with an explicit environment update "
                    + "(--update-environment) to sign off on what the catalog names now.");
            return inherited;
        }
        var name = record.Environment.Name;
        var requirement = EnvironmentResolution.ReadRequirement(JsonNode.Parse(record.BindingJson)?["environment"]?["requirement"],
            digest => RetainedDocument(digest, transaction)) ?? throw RetainedDiffers();
        var current = environments.Find(name) ?? throw new EnvironmentRefused("environment_unapproved",
            $"The predecessor's environment {name} is no longer in the operator's environment catalog; an environment update keeps the predecessor's environment name.");
        return new(requirement, current.Adoption, current.Target);
    }

    /// <summary>
    /// First send or exact acknowledgement-loss replay of a prepared HTTP submission. A correlated
    /// record is handed back with no other prerequisite. Otherwise the send needs current authority,
    /// no retained replay block, no pause, a catalog that still serves the retained environment exactly (the same
    /// name, adopted image and target), current credentials, exact retained B1 custody, a readable control token for the
    /// retained origin and a readable configured root; dispatch intent commits before any target contact and the writer is released before any
    /// network I/O. Only the exact acknowledgement correlates; every other outcome leaves the intent
    /// unresolved. A correlation that arrives after abandonment is retained, then that exact run is stopped.
    /// </summary>
    public async Task<NativeSubmission> DispatchHttpAsync(string attemptId, DispatchCredentials? credentials,
        CancellationToken cancellationToken = default)
    {
        var record = FindSubmission(attemptId);
        if (record is { State: "correlated" }) return record;
        var attempt = RequireCurrentAttempt(attemptId);
        if (record is null)
            throw new SubmissionNotReady("HTTP dispatch requires a prepared HTTP submission.");
        if (record.ReplayBlockedReason is not null) throw ReplayBlocked();
        RequireUnpaused();
        // A removed entry, another image or a reassigned origin never rebinds the Attempt: nothing is recorded or sent.
        if (environments?.Change(record.Environment) is { } change)
            throw new EnvironmentRefused("environment_changed", $"The environment changed: {change}. This Attempt was prepared for "
                + $"{record.Environment.Name} ({record.Environment.Image}) at {record.Environment.Target.Origin}. Stop it, retire it "
                + "under verified maintenance and replace it with an explicit environment update.");
        // Credential and Git checks may be slow; they never hold the SQLite writer.
        var ephemeral = (credentials ?? throw new UnsupportedRuntime("Current PR dispatch credentials are required.")).TargetRun();
        GitCustody.RequireRetained(attempt.B1.Repository, attempt.B1.CommitOid);
        var origin = DirectTargetExchange.CanonicalOrigin(record.Origin) ?? throw RetainedDiffers();

        var conflict = false;
        PreparedSubmission prepared;
        // The client is created before intent: a missing control token or root means nothing can be sent,
        // so it records nothing. Each connection reads the root again. Initiation is held from before the intent commits
        // until this caller can no longer send. It is local only: bytes a target already buffered can
        // still be accepted after it is released.
        await using (var client = DirectTargetClient.Open(origin, directTarget))
        using (HoldInitiation())
        {
            using (var transaction = connection.BeginTransaction(deferred: false))
            {
                attempt = RequireCurrentAttempt(attemptId, transaction);
                record = ReadSubmission(attemptId, transaction)!;
                if (record.State == "correlated") { transaction.Commit(); return record; }
                if (record.ReplayBlockedReason is not null) throw ReplayBlocked();
                // A replay can create the run too, so it passes the same gate as a first send.
                RequireUnpaused(transaction);
                RequireRetainedHttpSubmission(attempt, record, transaction);
                // Exactly the retained bytes are sent: an explicit replay regenerates nothing.
                prepared = DirectTargetSubmission.Import(record.RequestJson);
                if (record.State == "prepared")
                    Execute("UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = $p0", transaction, attemptId);
                transaction.Commit();
            }
            try
            {
                await DirectTargetSubmission.SubmitAsync(client, prepared, ephemeral, DirectTargetClock, cancellationToken);
            }
            catch (SubmissionConflict) { conflict = true; }
        }

        bool stale;
        var sent = record.IntendedRunId;
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            // Retaining a fact needs only the record that was sent, whose content SQL keeps immutable:
            // no custody, credentials, installed files or current authority. A late acknowledgement
            // is still acceptance evidence.
            attempt = ReadAttempt(attemptId, transaction);
            record = ReadSubmission(attemptId, transaction)!;
            if (record.IntendedRunId != sent)
                throw new SubmissionConflict("The HTTP submission changed while its request was in flight.");
            if (conflict)
                Execute("UPDATE native_submissions SET replay_blocked_reason = 'submission_conflict' WHERE attempt_id = $p0 AND replay_blocked_reason IS NULL",
                    transaction, attemptId);
            else if (record.State == "dispatched")
                Execute("UPDATE native_submissions SET state = 'correlated', run_id = intended_run_id WHERE attempt_id = $p0",
                    transaction, attemptId);
            record = ReadSubmission(attemptId, transaction)!;
            // Completion also ends authority; a duplicate acknowledgement never disturbs completed work.
            stale = record.State == "correlated" && (!attempt.IsCurrent || attempt.Abandonment is not null)
                && ReadCompletion(attemptId, transaction) is null;
            transaction.Commit();
        }
        if (record.State != "correlated") throw ReplayBlocked();
        if (stale)
        {
            // Correlation is committed first. The ordinary stop path then forces exactly the confirmed
            // run; it never replays, restores authority or proves cessation.
            var nativeStopRequested = false;
            try { await StopAsync(attemptId, "Native acknowledgement arrived after Attempt abandonment.", cancellationToken); }
            catch (CessationUnconfirmed cessation) { nativeStopRequested = cessation.NativeStopRequested; }
            catch (NativeTransportError) { nativeStopRequested = true; } // Force was sent; its outcome is uncertain.
            throw new StaleAttempt("Authority was lost while the acknowledgement was in flight; factual correlation is retained and "
                + (nativeStopRequested
                    ? "native stop of that exact run was requested, but physical cessation remains unconfirmed."
                    : "native stop was not sent; an explicit stop may address the run later."), nativeStopRequested);
        }
        return record;
    }

    private static SubmissionConflict ReplayBlocked() =>
        new("The target reported a conflicting immutable submission; this Attempt sends no further requests.");

    /// <summary>
    /// The retained record must still be exactly what preparation froze from admitted authority,
    /// the approved asset retained in this store, a binding this release supports and the environment its
    /// retained documents state. Missing or corrupt content refuses; nothing is regenerated from today's
    /// installed files, repository or catalog, and nothing is rebound.
    /// </summary>
    private void RequireRetainedHttpSubmission(AttemptRecord attempt, NativeSubmission record, SqliteTransaction transaction)
    {
        var asset = RetainedAsset(record.AssetSha256, transaction);
        var work = ReadWorkUnit(attempt.WorkUnitId, transaction)!;
        JsonNode? binding;
        try { binding = JsonNode.Parse(record.BindingJson); }
        catch (JsonException) { binding = null; }
        var resultOrigin = (binding?["resultOrigin"] as JsonValue)?.TryGetValue<string>(out var result) == true ? result : "";
        var environment = RetainedEnvironment(attempt, record, transaction);
        if (asset is null || record.AssetSha256 != asset.Sha256 || record.SubmissionKey != HttpSubmissionKey(attempt.AttemptId)
            || DirectTargetExchange.CanonicalOrigin(environment.Target.Origin) is null || !ValidResultOrigin(resultOrigin, work)
            || !JsonNode.DeepEquals(binding, HttpBinding(attempt, resultOrigin, environment))
            || HttpRequest(attempt, transaction, record.IntendedRunId, asset) != record.RequestJson)
            throw RetainedDiffers();
    }

    /// <summary>
    /// The environment <paramref name="record"/> retains, rebuilt from its binding and retained documents only. A
    /// retained adoption this release cannot use refuses with that reason.
    /// </summary>
    private EnvironmentResolution RetainedEnvironment(AttemptRecord attempt, NativeSubmission record, SqliteTransaction? transaction = null)
    {
        EnvironmentResolution? environment;
        try
        {
            environment = EnvironmentResolution.Read(JsonNode.Parse(record.BindingJson)?["environment"],
                digest => RetainedDocument(digest, transaction));
        }
        catch (JsonException) { throw RetainedDiffers(); }
        catch (UnsupportedRuntime)
        {
            throw new SubmissionConflict("This Attempt was prepared for an environment this release's native binding does not support; "
                + "stop and retire it, then replace it with an explicit environment update.");
        }
        if (environment is null || environment.Requirement.Declaration is { } declaration
                && (declaration.Path != EnvironmentDeclaration.Path || declaration.Commit != attempt.B1.CommitOid))
            throw RetainedDiffers();
        return environment;
    }

    private byte[]? RetainedDocument(string sha256, SqliteTransaction? transaction)
    {
        using var command = Command("SELECT content FROM environment_documents WHERE sha256 = $p0", transaction, sha256);
        return command.ExecuteScalar() is byte[] content && Digests.Bytes(content) == sha256 ? content : null;
    }

    /// <summary>
    /// The request exactly as the SDK imports it: preparation retains these UTF-8 bytes, and dispatch imports
    /// them again, so replay sends the same run ID, submission key, source and asset content.
    /// </summary>
    private static string RetainedRequest(string requestJson)
    {
        DirectTargetSubmission.Import(requestJson);
        return requestJson;
    }

    private ExecutionAsset? RetainedAsset(string? sha256, SqliteTransaction transaction)
    {
        using var command = Command("SELECT content FROM execution_assets WHERE asset_sha256 = $p0", transaction, sha256);
        return ExecutionAsset.FromRetained(command.ExecuteScalar() as byte[]);
    }

    private static SubmissionConflict RetainedDiffers() =>
        new("The retained HTTP submission differs from admitted authority, its approved asset or a supported binding.");

    private static string HttpSubmissionKey(string attemptId) => "broodling:http:v1:" + attemptId;

    /// <summary>The stock <c>TargetRunRequest</c> without its ephemeral <c>connections</c> and <c>githubToken</c>.</summary>
    private string HttpRequest(AttemptRecord attempt, SqliteTransaction transaction, string intendedRunId, ExecutionAsset asset)
    {
        var (task, work, targetBranch) = AdmittedTask(attempt, transaction);
        return new JsonObject
        {
            ["runId"] = intendedRunId,
            ["submission"] = new JsonObject
            {
                ["title"] = "Broodling Attempt " + attempt.AttemptId, ["graph"] = asset.Graph(), ["runtime"] = asset.Runtime(),
                ["initialInput"] = new JsonObject { ["task"] = task },
                // The authorized PR branch stays separate from the exact original B1 revision.
                ["source"] = new JsonObject
                {
                    ["repository"] = work.Owner + "/" + work.Repository, ["branch"] = targetBranch,
                    ["revision"] = attempt.B1.CommitOid
                },
                ["submissionKey"] = HttpSubmissionKey(attempt.AttemptId)
            }
        }.ToJsonString();
    }

    /// <summary>Broodling-only retention facts: never sent, never secrets.</summary>
    private static JsonObject HttpBinding(AttemptRecord attempt, string resultOrigin, EnvironmentResolution environment) => new()
    {
        ["protocol"] = DirectTargetDiscovery.Kind, ["repository"] = attempt.B1.Repository,
        ["resultOrigin"] = resultOrigin, ["native"] = DirectTargetBinding.Native(), ["environment"] = environment.Json()
    };

    /// <summary>Captured once from the retained common Git directory; later remote changes never rebind it.</summary>
    private static string ResultOrigin(AttemptRecord attempt, WorkUnit work)
    {
        var value = GitCustody.Run(attempt.B1.Repository, ["config", "--get", "remote.origin.url"]);
        if (value.ExitCode is not (0 or 1)) throw new SubmissionNotReady("Cannot inspect the retained source origin.");
        var origin = value.ExitCode == 0 ? Encoding.UTF8.GetString(value.Output).Trim() : "";
        if (!ValidResultOrigin(origin, work))
            throw new SubmissionNotReady("The result-fetch origin must name the admitted GitHub repository, without credentials.");
        return origin;
    }

    /// <summary>A URL origin carries no userinfo, except the conventional <c>git</c> user of an SSH URL.</summary>
    private static bool ValidResultOrigin(string origin, WorkUnit work) =>
        work.Host == "github.com" && GitHubOriginRepository(origin) == work.Owner + "/" + work.Repository
        && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.UserInfo == "" || uri.Scheme == "ssh" && uri.UserInfo == "git");

    // Git remotes are not work-reference input: a bare owner/name is a local path, not implicit github.com.
    internal static string? GitHubOriginRepository(string origin)
    {
        string path;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
            path = uri.AbsolutePath;
        }
        else
        {
            var separator = origin.IndexOf(':');
            if (separator < 0 || !origin[..separator].Split('@')[^1].Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
            path = origin[(separator + 1)..];
        }
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        var parts = path.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0) ? path : null;
    }
}
