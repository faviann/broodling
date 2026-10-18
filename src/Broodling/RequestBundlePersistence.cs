using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed partial class BroodlingStore
{
    private static readonly JsonSerializerOptions BundleManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record BundleManifestV1(string Version, string BundleId, string SubmissionId,
        string WorkUnitId, string AcquisitionInputs, string AcquisitionPolicy,
        BundleManifestRepositoryV1? Repository,
        IReadOnlyList<BundleManifestReferenceV1> References);

    private sealed record BundleManifestRepositoryV1(string Repository, string DefaultBranch,
        string StartingRevision, string StartingCommit);

    private sealed record BundleManifestReferenceV1(string ReferenceId, int Ordinal, string Selector,
        string? SourceId, string? ContentSha256);

    /// <summary>Freeze capture inputs and policy for one existing Issue submission.</summary>
    public RequestBundle BeginRequestBundleCapture(string submissionId, RequestBundlePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var inputs = plan.AcquisitionInputs;
        var policy = plan.AcquisitionPolicy;
        using var transaction = connection.BeginTransaction(deferred: false);
        var submission = ReadIssueSubmission(submissionId, transaction)
            ?? throw new UnknownRecord("Unknown Issue submission.");
        if (ReadRequestBundle(submissionId, transaction) is { } existing)
        {
            if (!existing.AcquisitionInputs.AsSpan().SequenceEqual(inputs)
                || !existing.AcquisitionPolicy.AsSpan().SequenceEqual(policy))
                throw new RequestBundleConflict("The Issue submission already has different frozen capture inputs or policy.");
            transaction.Commit();
            return existing;
        }
        if (submission.ContractRevisionId is not null || submission.State is not ("accepted" or "capturing"))
            throw new RequestBundleConflict("Capture can begin only for an unprepared accepted Issue submission.");

        var bundleId = "bundle-" + Digests.Parts("broodling.request-bundle.v1", submissionId);
        var now = DateTimeOffset.UtcNow.ToString("O");
        Execute("UPDATE issue_submissions SET state = 'capturing' WHERE submission_id = $p0 AND state = 'accepted'",
            transaction, submissionId);
        Execute("INSERT INTO request_bundles VALUES ($p0, $p1, $p2, $p3, 'capturing', NULL, NULL, $p4, NULL, NULL)",
            transaction, bundleId, submissionId, inputs, policy, now);
        var result = ReadRequestBundle(submissionId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Register one member before its first capture. Registrations remain open until completion; the same
    /// identity can be replayed after reopen.
    /// </summary>
    public RequestBundleReference RegisterRequestBundleReference(string bundleId, string referenceId, byte[] selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (string.IsNullOrWhiteSpace(referenceId))
            throw new RequestBundleConflict("A RequestBundle reference identity is required.");
        try { StrictUtf8.GetByteCount(referenceId); }
        catch (EncoderFallbackException) { throw new RequestBundleConflict("Reference identity contains invalid Unicode."); }
        using var transaction = connection.BeginTransaction(deferred: false);
        var bundle = ReadRequestBundleById(bundleId, transaction)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        if (ReadBundleReference(bundleId, referenceId, transaction) is { } existing)
        {
            if (!existing.Selector.AsSpan().SequenceEqual(selector))
                throw new RequestBundleConflict("A registered RequestBundle reference cannot change its selector.");
            transaction.Commit();
            return existing;
        }
        if (bundle.State != "capturing" || !SubmissionCanCapture(bundle.SubmissionId, transaction))
            throw new RequestBundleConflict("RequestBundle membership can grow only during capture.");

        long ordinal;
        using (var next = Command("SELECT COALESCE(MAX(ordinal), -1) + 1 FROM request_bundle_references WHERE bundle_id = $p0",
            transaction, bundleId))
            ordinal = Convert.ToInt64(next.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        Execute("INSERT INTO request_bundle_references (bundle_id, reference_id, ordinal, selector) "
            + "VALUES ($p0, $p1, $p2, $p3)", transaction, bundleId, referenceId, ordinal, selector);
        var result = ReadBundleReference(bundleId, referenceId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>Commit a supplied capture and its first bundle association atomically.</summary>
    public RequestBundleReference CaptureRequestBundleSource(string bundleId, string referenceId,
        SourceSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        using var transaction = connection.BeginTransaction(deferred: false);
        var bundle = ReadRequestBundleById(bundleId, transaction)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        var reference = ReadBundleReference(bundleId, referenceId, transaction)
            ?? throw new UnknownRecord("Unknown RequestBundle reference.");
        if (reference.IsCaptured)
        {
            transaction.Commit();
            return reference;
        }
        if (bundle.State != "capturing" || !SubmissionCanCapture(bundle.SubmissionId, transaction))
            throw new RequestBundleConflict("This Issue submission is no longer eligible for capture.");
        var issueSubmission = ReadIssueSubmission(bundle.SubmissionId, transaction)!;
        var work = ReadWorkUnit(issueSubmission.WorkUnitId, transaction)!;
        var source = PersistEntitledSource(work, submission, transaction);
        Execute("UPDATE request_bundle_references SET source_id = $p0, content_sha256 = $p1 "
            + "WHERE bundle_id = $p2 AND reference_id = $p3 AND source_id IS NULL AND content_sha256 IS NULL",
            transaction, source.SourceId, source.ContentSha256, bundleId, referenceId);
        var result = ReadBundleReference(bundleId, referenceId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>Seal the reached membership and persist its canonical manifest once.</summary>
    public RequestBundle CompleteRequestBundleCapture(string bundleId)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var bundle = ReadRequestBundleById(bundleId, transaction)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        if (bundle.State == "complete")
        {
            transaction.Commit();
            return bundle;
        }
        if (!SubmissionCanCapture(bundle.SubmissionId, transaction))
            throw new RequestBundleConflict("This Issue submission is no longer eligible for capture completion.");
        if (bundle.References.Any(reference => !reference.IsCaptured))
            throw new RequestBundleConflict("Every registered RequestBundle reference must be captured before completion.");
        var issue = ReadIssueSubmission(bundle.SubmissionId, transaction)!;
        var manifest = new BundleManifestV1("v1", bundle.BundleId, bundle.SubmissionId, issue.WorkUnitId,
            Convert.ToBase64String(bundle.AcquisitionInputs), Convert.ToBase64String(bundle.AcquisitionPolicy),
            ReadRepositoryPreparation(bundle.BundleId, transaction) is { } repository
                ? new(repository.Repository, repository.DefaultBranch, repository.StartingRevision, repository.StartingCommit)
                : null, bundle.References.Select(reference =>
                new BundleManifestReferenceV1(reference.ReferenceId, reference.Ordinal,
                    Convert.ToBase64String(reference.Selector), reference.SourceId, reference.ContentSha256)).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, BundleManifestOptions);
        var manifestJson = Encoding.UTF8.GetString(bytes);
        var manifestSha256 = Digests.Bytes(bytes);
        Execute("UPDATE request_bundles SET state = 'complete', manifest_json = $p0, manifest_sha256 = $p1, completed_at = $p2 "
            + "WHERE bundle_id = $p3 AND state = 'capturing'", transaction,
            manifestJson, manifestSha256, DateTimeOffset.UtcNow.ToString("O"), bundleId);
        var completed = ReadRequestBundleById(bundleId, transaction)!;
        transaction.Commit();
        return completed;
    }

    /// <summary>
    /// Seal an open capture with its deterministic findings. The submission is
    /// rejected before any Contract; material captured so far stays inspectable.
    /// </summary>
    internal RequestBundle RefuseRequestBundleCapture(string bundleId, IReadOnlyList<RequestCaptureFinding> findings)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var bundle = ReadRequestBundleById(bundleId, transaction)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        if (bundle.State == "refused")
        {
            transaction.Commit();
            return bundle;
        }
        if (bundle.State != "capturing" || !SubmissionCanCapture(bundle.SubmissionId, transaction))
            throw new RequestBundleConflict("This Issue submission is no longer eligible for capture refusal.");
        Execute("UPDATE request_bundles SET state = 'refused', findings_json = $p0 WHERE bundle_id = $p1 AND state = 'capturing'",
            transaction, JsonSerializer.Serialize(findings), bundleId);
        Execute("UPDATE issue_submissions SET state = 'rejected' WHERE submission_id = $p0 AND state = 'capturing'",
            transaction, bundle.SubmissionId);
        var refused = ReadRequestBundleById(bundleId, transaction)!;
        transaction.Commit();
        return refused;
    }

    /// <summary>Inspect one submission's bundle and its exact registered membership.</summary>
    public RequestBundle GetRequestBundle(string submissionId)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadRequestBundle(submissionId, transaction)
            ?? throw new UnknownRecord("The Issue submission has no RequestBundle.");
        transaction.Commit();
        return result;
    }

    private bool SubmissionCanCapture(string submissionId, SqliteTransaction transaction)
    {
        using var command = Command("SELECT state, contract_revision_id FROM issue_submissions WHERE submission_id = $p0",
            transaction, submissionId);
        using var row = command.ExecuteReader();
        return row.Read() && row.IsDBNull(1) && row.GetString(0) == "capturing";
    }

    private RequestBundle? ReadRequestBundle(string submissionId, SqliteTransaction? transaction)
    {
        string? bundleId;
        using (var command = Command("SELECT bundle_id FROM request_bundles WHERE submission_id = $p0", transaction, submissionId))
            bundleId = command.ExecuteScalar() as string;
        return bundleId is null ? null : ReadRequestBundleById(bundleId, transaction);
    }

    private RequestBundle? ReadRequestBundleById(string bundleId, SqliteTransaction? transaction)
    {
        string id, submissionId, state, createdAt;
        byte[] inputs, policy;
        string? manifestJson, manifestSha256, completedAt, findingsJson;
        using (var command = Command("SELECT bundle_id, submission_id, acquisition_inputs, acquisition_policy, state, manifest_json, manifest_sha256, created_at, completed_at, findings_json FROM request_bundles WHERE bundle_id = $p0",
            transaction, bundleId))
        using (var row = command.ExecuteReader())
        {
            if (!row.Read()) return null;
            id = row.GetString(0);
            submissionId = row.GetString(1);
            inputs = (byte[])row[2];
            policy = (byte[])row[3];
            state = row.GetString(4);
            manifestJson = row.IsDBNull(5) ? null : row.GetString(5);
            manifestSha256 = row.IsDBNull(6) ? null : row.GetString(6);
            createdAt = row.GetString(7);
            completedAt = row.IsDBNull(8) ? null : row.GetString(8);
            findingsJson = row.IsDBNull(9) ? null : row.GetString(9);
        }
        var referenceIds = new List<string>();
        using (var command = Command("SELECT reference_id FROM request_bundle_references WHERE bundle_id = $p0 ORDER BY ordinal",
            transaction, id))
        using (var rows = command.ExecuteReader())
            while (rows.Read())
                referenceIds.Add(rows.GetString(0));
        var references = referenceIds.Select(referenceId => ReadBundleReference(id, referenceId, transaction)!).ToArray();
        var repository = ReadRepositoryPreparation(id, transaction);
        var findings = findingsJson is null ? [] : JsonSerializer.Deserialize<RequestCaptureFinding[]>(findingsJson)!;
        return new(id, submissionId, state, inputs, policy, manifestJson, manifestSha256,
            createdAt, completedAt, repository, references, findings);
    }

    private RequestBundleReference? ReadBundleReference(string bundleId, string referenceId,
        SqliteTransaction? transaction)
    {
        using var command = Command("SELECT bundle_id, reference_id, ordinal, selector, source_id, content_sha256 FROM request_bundle_references WHERE bundle_id = $p0 AND reference_id = $p1",
            transaction, bundleId, referenceId);
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new(row.GetString(0), row.GetString(1), checked((int)row.GetInt64(2)), (byte[])row[3],
            row.IsDBNull(4) ? null : row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5));
    }

    private RepositoryPreparation? ReadRepositoryPreparation(string bundleId, SqliteTransaction? transaction)
    {
        using var command = Command("SELECT bundle_id, repository, default_branch, starting_revision, starting_commit_oid, prepared_at "
            + "FROM request_bundle_repositories WHERE bundle_id = $p0", transaction, bundleId);
        using var row = command.ExecuteReader();
        return !row.Read() ? null : new(row.GetString(0), row.GetString(1), row.GetString(2),
            row.GetString(3), row.GetString(4), row.GetString(5));
    }
}
