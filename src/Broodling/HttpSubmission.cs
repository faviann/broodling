using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed partial class BroodlingStore
{
    internal const string HttpProtocol = "zeroshot.native-v2-target/v2";

    /// <summary>
    /// Freeze one complete, secret-free stock DirectTarget request for an HTTP Attempt, with no
    /// target contact or dispatch credentials. The prepared record is the preparation fact: it
    /// retains the approved asset bytes, intended run identity and exact bindings, and implies
    /// neither dispatch nor acceptance. Repeating it returns the first committed record,
    /// validated against retained content only.
    /// </summary>
    public NativeSubmission PrepareHttpSubmission(string attemptId, string directOrigin) =>
        PrepareHttpSubmission(attemptId, directOrigin, ExecutionAsset.LoadBundled);

    internal NativeSubmission PrepareHttpSubmission(string attemptId, string directOrigin, Func<ExecutionAsset> installed)
    {
        if (DirectTargetExchange.CanonicalOrigin(directOrigin) is null)
            throw new UnsupportedRuntime("The DirectTarget origin must be canonical HTTPS or literal-loopback HTTP.");
        var attempt = RequireCurrentAttempt(attemptId);
        if (attempt.ResourceKind != AttemptRecord.Http)
            throw new SubmissionNotReady("HTTP submission preparation requires an HTTP Attempt.");
        ExecutionAsset? asset = null;
        string? resultOrigin = null;
        if (FindSubmission(attemptId) is null)
        {
            // Only a new preparation reads the installed asset; a retained record never needs it.
            asset = installed();
            resultOrigin = ResultOrigin(attempt, ReadWorkUnit(attempt.WorkUnitId)!);
            // Git and SQLite cannot share a transaction. A crash may leave this exact pin without
            // a prepared record; it grants nothing, and a later preparation reuses it.
            GitCustody.Retain(new(attempt.B1.Repository, attempt.B1.CommitOid, attempt.B1.RequestedRevision));
        }
        using var transaction = connection.BeginTransaction(deferred: false);
        attempt = RequireCurrentAttempt(attemptId, transaction);
        RequireUnpausedUnlessReplacement(attempt, transaction);
        if (ReadSubmission(attemptId, transaction) is { } previous)
        {
            RequireRetainedHttpSubmission(attempt, previous, transaction);
            if (previous.Locator.Address != directOrigin)
                throw new SubmissionConflict("The DirectTarget origin differs from the retained binding.");
            transaction.Commit();
            return previous; // A losing concurrent preparer adopts the first identity; it never invents another.
        }
        var intended = Guid.CreateVersion7().ToString();
        Execute("INSERT OR IGNORE INTO execution_assets VALUES ($p0, $p1)", transaction, asset!.Sha256, asset.Content());
        // A row retained by an earlier preparation is reused, so it must still be the approved content.
        if (RetainedAsset(asset.Sha256, transaction) is null) throw RetainedDiffers();
        Execute("""
            INSERT INTO native_submissions (attempt_id, format, submission_key, request_json, state, intended_run_id,
                asset_sha256, binding_json) VALUES ($p0, 'http.v1', $p1, $p2, 'prepared', $p3, $p4, $p5)
            """, transaction, attemptId, HttpSubmissionKey(attemptId), HttpRequest(attempt, transaction, intended, asset),
            intended, asset.Sha256, HttpBinding(attempt, directOrigin, resultOrigin!, asset).ToJsonString());
        var result = ReadSubmission(attemptId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// First send or exact acknowledgement-loss replay of a prepared HTTP submission. A correlated
    /// record is handed back with no other prerequisite. Otherwise the send needs current authority,
    /// no retained replay block, no pause, current credentials and exact retained B1 custody; dispatch
    /// intent commits before discovery and the writer is released before any network I/O. Only the
    /// exact acknowledgement correlates; every other outcome leaves the intent unresolved. A
    /// correlation that arrives after abandonment is retained, then that exact run is stopped.
    /// </summary>
    public async Task<NativeSubmission> DispatchHttpAsync(string attemptId, DispatchCredentials? credentials,
        CancellationToken cancellationToken = default)
    {
        var record = FindSubmission(attemptId);
        if (record is { Format: NativeSubmission.Http, State: "correlated" }) return record;
        var attempt = RequireCurrentAttempt(attemptId);
        if (record is not { Format: NativeSubmission.Http })
            throw new SubmissionNotReady("HTTP dispatch requires a prepared HTTP submission.");
        if (record.ReplayBlockedReason is not null) throw ReplayBlocked();
        RequireUnpaused();
        // Credential and Git checks may be slow; they never hold the SQLite writer.
        var ephemeral = (credentials ?? throw new UnsupportedRuntime("Current PR dispatch credentials are required.")).Environment();
        GitCustody.RequireRetained(attempt.B1.Repository, attempt.B1.CommitOid);

        var conflict = false;
        // Held from before the intent commits until this caller can no longer send. It is local only:
        // bytes a target already buffered can still be accepted after it is released.
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
                if (record.State == "prepared")
                    Execute("UPDATE native_submissions SET state = 'dispatched' WHERE attempt_id = $p0", transaction, attemptId);
                transaction.Commit();
            }
            try
            {
                await DirectTargetSubmission.SubmitAsync(DirectTargetExchange.CanonicalOrigin(record.Locator.Address)!,
                    record.RequestJson, record.IntendedRunId!, ephemeral, DirectTargetClock, cancellationToken);
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
            if (record.Format != NativeSubmission.Http || record.IntendedRunId != sent)
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
            try { await StopAsync(attemptId, "Native acknowledgement arrived after Attempt abandonment.", null, cancellationToken); }
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
    /// the approved asset retained in this store and a binding this release supports. Missing or
    /// corrupt content refuses; nothing is regenerated from today's installed files or rebound.
    /// </summary>
    private void RequireRetainedHttpSubmission(AttemptRecord attempt, NativeSubmission record, SqliteTransaction transaction)
    {
        var asset = RetainedAsset(record.AssetSha256, transaction);
        var work = ReadWorkUnit(attempt.WorkUnitId, transaction)!;
        JsonNode? binding;
        try { binding = JsonNode.Parse(record.BindingJson ?? ""); }
        catch (JsonException) { binding = null; }
        var origin = (binding?["origin"] as JsonValue)?.TryGetValue<string>(out var text) == true ? text : "";
        var resultOrigin = (binding?["resultOrigin"] as JsonValue)?.TryGetValue<string>(out var result) == true ? result : "";
        if (record.Format != NativeSubmission.Http || asset is null || record.AssetSha256 != asset.Sha256
            || record.IntendedRunId is null || record.SubmissionKey != HttpSubmissionKey(attempt.AttemptId)
            || DirectTargetExchange.CanonicalOrigin(origin) is null || !ValidResultOrigin(resultOrigin, work)
            || !JsonNode.DeepEquals(binding, HttpBinding(attempt, origin, resultOrigin, asset))
            || HttpRequest(attempt, transaction, record.IntendedRunId, asset) != record.RequestJson)
            throw RetainedDiffers();
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
        var (task, work, authorization) = AdmittedTask(attempt, transaction);
        if (authorization.Mode != "pull_request")
            throw new SubmissionNotReady("An HTTP Attempt requires authorized pull-request delivery.");
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
                    ["repository"] = work.Owner + "/" + work.Repository, ["branch"] = authorization.TargetBranch,
                    ["revision"] = attempt.B1.CommitOid
                },
                ["submissionKey"] = HttpSubmissionKey(attempt.AttemptId)
            }
        }.ToJsonString();
    }

    /// <summary>Broodling-only retention facts: never sent, never secrets.</summary>
    private static JsonObject HttpBinding(AttemptRecord attempt, string origin, string resultOrigin, ExecutionAsset asset) => new()
    {
        ["protocol"] = HttpProtocol, ["origin"] = origin, ["repository"] = attempt.B1.Repository, ["resultOrigin"] = resultOrigin,
        ["native"] = new JsonObject
        {
            ["version"] = asset.Binding.NativeVersion, ["sourceRevision"] = asset.Binding.NativeSourceRevision,
            ["linuxX64ExecutableSha256"] = asset.Binding.NativeExecutableSha256
        }
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
        work.Host == "github.com" && NativeProfile.GitHubOriginRepository(origin) == work.Owner + "/" + work.Repository
        && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.UserInfo == "" || uri.Scheme == "ssh" && uri.UserInfo == "git");
}
