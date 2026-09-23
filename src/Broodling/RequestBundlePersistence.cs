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
        string WorkUnitId, string AcquisitionInputs, string AcquisitionPolicy, string AcquisitionLimits,
        IReadOnlyList<BundleManifestReferenceV1> References);

    private sealed record BundleManifestReferenceV1(string ReferenceId, int Ordinal, string CaptureKind,
        string Selector, string? SourceId, string? ContentSha256, string? GitCommitOid, string? GitPath,
        string? GitBlobOid);

    /// <summary>Freeze capture inputs and policy for one existing Issue submission.</summary>
    public RequestBundle BeginRequestBundleCapture(string submissionId, RequestBundlePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var inputs = plan.AcquisitionInputs;
        var policy = plan.AcquisitionPolicy;
        var limits = plan.AcquisitionLimits;
        using var transaction = connection.BeginTransaction(deferred: false);
        var submission = ReadIssueSubmission(submissionId, transaction)
            ?? throw new UnknownRecord("Unknown Issue submission.");
        if (ReadRequestBundle(submissionId, transaction) is { } existing)
        {
            if (!existing.AcquisitionInputs.AsSpan().SequenceEqual(inputs)
                || !existing.AcquisitionPolicy.AsSpan().SequenceEqual(policy)
                || !existing.AcquisitionLimits.AsSpan().SequenceEqual(limits))
                throw new RequestBundleConflict("The Issue submission already has different frozen capture inputs or acquisition limits.");
            transaction.Commit();
            return existing;
        }
        if (submission.ContractRevisionId is not null || submission.State is not ("accepted" or "capturing"))
            throw new RequestBundleConflict("Capture can begin only for an unprepared accepted Issue submission.");

        var bundleId = "bundle-" + Digests.Parts("broodling.request-bundle.v1", submissionId);
        var now = DateTimeOffset.UtcNow.ToString("O");
        Execute("UPDATE issue_submissions SET state = 'capturing' WHERE submission_id = $p0 AND state = 'accepted'",
            transaction, submissionId);
        Execute("INSERT INTO request_bundles VALUES ($p0, $p1, $p2, $p3, $p4, 'capturing', NULL, NULL, $p5, NULL)",
            transaction, bundleId, submissionId, inputs, policy, limits, now);
        var result = ReadRequestBundle(submissionId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Register one selection as acquisition discovers it. Registrations remain
    /// open until completion; the same identity can be replayed after reopen.
    /// </summary>
    public RequestBundleReference RegisterRequestBundleReference(string bundleId,
        RequestBundleReferenceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var transaction = connection.BeginTransaction(deferred: false);
        var bundle = ReadRequestBundleById(bundleId, transaction)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        if (ReadBundleReference(bundleId, input.ReferenceId, transaction) is { } existing)
        {
            if (!ReferenceInputMatches(existing, input, transaction))
                throw new RequestBundleConflict("A registered RequestBundle reference cannot change its selector or capture input.");
            transaction.Commit();
            return existing;
        }
        if (bundle.State != "capturing" || !SubmissionCanCapture(bundle.SubmissionId, transaction))
            throw new RequestBundleConflict("RequestBundle membership can grow only during capture.");

        long ordinal;
        using (var next = Command("SELECT COALESCE(MAX(ordinal), -1) + 1 FROM request_bundle_references WHERE bundle_id = $p0",
            transaction, bundleId))
            ordinal = Convert.ToInt64(next.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        Execute("""
            INSERT INTO request_bundle_references
                (bundle_id, reference_id, ordinal, capture_kind, selector, source_id, content_sha256,
                 git_repository_input, git_revision_input, git_path, git_repository, git_commit_oid, git_blob_oid)
            VALUES ($p0, $p1, $p2, $p3, $p4, NULL, NULL, $p5, $p6, $p7, NULL, NULL, NULL)
            """, transaction, bundleId, input.ReferenceId, ordinal, input.CaptureKind, input.Selector,
            input.GitRepository, input.GitRevision, input.GitPath);
        var result = ReadBundleReference(bundleId, input.ReferenceId, transaction)!;
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
        if (reference.CaptureKind != "source")
            throw new RequestBundleConflict("This RequestBundle reference is not a source snapshot.");
        if (bundle.State == "complete")
            throw new RequestBundleConflict("A completed RequestBundle cannot be refreshed.");
        if (reference.IsCaptured)
        {
            transaction.Commit();
            return reference;
        }
        if (!SubmissionCanCapture(bundle.SubmissionId, transaction))
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

    /// <summary>Capture one exact local Git blob after pinning its commit in existing Git custody.</summary>
    public RequestBundleReference CaptureRequestBundleGitBlob(string bundleId, string referenceId)
    {
        string repositoryInput, revisionInput, path;
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            var bundle = ReadRequestBundleById(bundleId, transaction)
                ?? throw new UnknownRecord("Unknown RequestBundle.");
            var reference = ReadBundleReference(bundleId, referenceId, transaction)
                ?? throw new UnknownRecord("Unknown RequestBundle reference.");
            if (reference.CaptureKind != "git_blob")
                throw new RequestBundleConflict("This RequestBundle reference is not a Git file.");
            if (bundle.State == "complete")
                throw new RequestBundleConflict("A completed RequestBundle cannot be refreshed.");
            if (reference.IsCaptured)
            {
                transaction.Commit();
                return reference;
            }
            if (!SubmissionCanCapture(bundle.SubmissionId, transaction))
                throw new RequestBundleConflict("This Issue submission is no longer eligible for capture.");
            ReadGitInput(bundleId, referenceId, transaction, out repositoryInput, out revisionInput, out path);
            transaction.Commit();
        }

        // Do Git work without holding SQLite's writer. Nothing is a durable
        // capture until this exact result and membership checkpoint commit.
        var state = GitCustody.ResolvePinned(repositoryInput, revisionInput);
        GitCustody.Retain(state);
        var blob = GitCustody.ReadPinnedBlob(state.Repository, state.CommitOid, path);
        var contentSha256 = Digests.Bytes(blob.Content);

        using var write = connection.BeginTransaction(deferred: false);
        var currentBundle = ReadRequestBundleById(bundleId, write)
            ?? throw new UnknownRecord("Unknown RequestBundle.");
        var current = ReadBundleReference(bundleId, referenceId, write)
            ?? throw new UnknownRecord("Unknown RequestBundle reference.");
        if (current.IsCaptured)
        {
            write.Commit();
            return current;
        }
        if (currentBundle.State != "capturing" || !SubmissionCanCapture(currentBundle.SubmissionId, write))
            throw new RequestBundleConflict("This Issue submission is no longer eligible for capture.");
        Execute("""
            UPDATE request_bundle_references
            SET git_repository = $p0, git_commit_oid = $p1, git_blob_oid = $p2, content_sha256 = $p3
            WHERE bundle_id = $p4 AND reference_id = $p5 AND git_commit_oid IS NULL
            """, write, state.Repository, state.CommitOid, blob.BlobOid, contentSha256, bundleId, referenceId);
        var result = ReadBundleReference(bundleId, referenceId, write)!;
        write.Commit();
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
            Convert.ToBase64String(bundle.AcquisitionLimits), bundle.References.Select(reference =>
                new BundleManifestReferenceV1(reference.ReferenceId, reference.Ordinal, reference.CaptureKind,
                    Convert.ToBase64String(reference.Selector), reference.SourceId, reference.ContentSha256,
                    reference.GitCommitOid, reference.GitPath, reference.GitBlobOid)).ToArray());
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

    /// <summary>Inspect one submission's bundle and its exact registered membership.</summary>
    public RequestBundle GetRequestBundle(string submissionId)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadRequestBundle(submissionId, transaction)
            ?? throw new UnknownRecord("The Issue submission has no RequestBundle.");
        transaction.Commit();
        return result;
    }

    /// <summary>Read captured content only through a completed bundle membership.</summary>
    public RequestBundleReferenceContent ReadRequestBundleReference(string bundleId, string referenceId)
    {
        RequestBundleReference reference;
        EntitledSource? source = null;
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            var bundle = ReadRequestBundleById(bundleId, transaction)
                ?? throw new UnknownRecord("Unknown RequestBundle.");
            if (bundle.State != "complete")
                throw new RequestBundleConflict("Only a completed RequestBundle can be read.");
            reference = ReadBundleReference(bundleId, referenceId, transaction)
                ?? throw new UnknownRecord("The reference is outside this RequestBundle.");
            if (!reference.IsCaptured)
                throw new RequestBundleConflict("The selected RequestBundle reference has no captured content.");
            if (reference.CaptureKind == "source")
                source = ReadSource(reference.SourceId!, transaction);
            transaction.Commit();
        }

        byte[] content;
        if (source is not null)
        {
            content = source.Content;
            if (source.ContentSha256 != reference.ContentSha256)
                throw new RequestBundleConflict("The retained source digest does not match the completed manifest membership.");
        }
        else
        {
            var blob = GitCustody.ReadPinnedBlob(reference.GitRepository!, reference.GitCommitOid!,
                reference.GitPath!, reference.GitBlobOid);
            content = blob.Content;
            if (Digests.Bytes(content) != reference.ContentSha256)
                throw new RequestBundleConflict("The retained Git content digest does not match the completed manifest membership.");
        }
        return new(bundleId, referenceId, reference.ContentSha256!, content,
            reference.GitCommitOid, reference.GitPath);
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
        byte[] inputs, policy, limits;
        string? manifestJson, manifestSha256, completedAt;
        using (var command = Command("SELECT bundle_id, submission_id, acquisition_inputs, acquisition_policy, acquisition_limits, state, manifest_json, manifest_sha256, created_at, completed_at FROM request_bundles WHERE bundle_id = $p0",
            transaction, bundleId))
        using (var row = command.ExecuteReader())
        {
            if (!row.Read()) return null;
            id = row.GetString(0);
            submissionId = row.GetString(1);
            inputs = (byte[])row[2];
            policy = (byte[])row[3];
            limits = (byte[])row[4];
            state = row.GetString(5);
            manifestJson = row.IsDBNull(6) ? null : row.GetString(6);
            manifestSha256 = row.IsDBNull(7) ? null : row.GetString(7);
            createdAt = row.GetString(8);
            completedAt = row.IsDBNull(9) ? null : row.GetString(9);
        }
        var referenceIds = new List<string>();
        using (var command = Command("SELECT reference_id FROM request_bundle_references WHERE bundle_id = $p0 ORDER BY ordinal",
            transaction, id))
        using (var rows = command.ExecuteReader())
            while (rows.Read())
                referenceIds.Add(rows.GetString(0));
        var references = referenceIds.Select(referenceId => ReadBundleReference(id, referenceId, transaction)!).ToArray();
        return new(id, submissionId, state, inputs, policy, limits, manifestJson, manifestSha256,
            createdAt, completedAt, references);
    }

    private RequestBundleReference? ReadBundleReference(string bundleId, string referenceId,
        SqliteTransaction? transaction)
    {
        using var command = Command("SELECT bundle_id, reference_id, ordinal, capture_kind, selector, source_id, content_sha256, git_repository, git_commit_oid, git_path, git_blob_oid FROM request_bundle_references WHERE bundle_id = $p0 AND reference_id = $p1",
            transaction, bundleId, referenceId);
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new(row.GetString(0), row.GetString(1), checked((int)row.GetInt64(2)), row.GetString(3),
            (byte[])row[4], row.IsDBNull(5) ? null : row.GetString(5),
            row.IsDBNull(6) ? null : row.GetString(6), row.IsDBNull(7) ? null : row.GetString(7),
            row.IsDBNull(8) ? null : row.GetString(8), row.IsDBNull(9) ? null : row.GetString(9),
            row.IsDBNull(10) ? null : row.GetString(10));
    }

    private bool ReferenceInputMatches(RequestBundleReference reference, RequestBundleReferenceInput input,
        SqliteTransaction transaction)
    {
        if (reference.CaptureKind != input.CaptureKind
            || !reference.Selector.AsSpan().SequenceEqual(input.Selector)) return false;
        using var command = Command("SELECT git_repository_input, git_revision_input, git_path FROM request_bundle_references WHERE bundle_id = $p0 AND reference_id = $p1",
            transaction, reference.BundleId, reference.ReferenceId);
        using var row = command.ExecuteReader();
        if (!row.Read()) return false;
        return NullableText(row, 0) == input.GitRepository
            && NullableText(row, 1) == input.GitRevision
            && NullableText(row, 2) == input.GitPath;
    }

    private static string? NullableText(SqliteDataReader row, int ordinal) =>
        row.IsDBNull(ordinal) ? null : row.GetString(ordinal);

    private void ReadGitInput(string bundleId, string referenceId, SqliteTransaction transaction,
        out string repository, out string revision, out string path)
    {
        using var command = Command("SELECT git_repository_input, git_revision_input, git_path FROM request_bundle_references WHERE bundle_id = $p0 AND reference_id = $p1",
            transaction, bundleId, referenceId);
        using var row = command.ExecuteReader();
        if (!row.Read() || row.IsDBNull(0) || row.IsDBNull(1) || row.IsDBNull(2))
            throw new RequestBundleConflict("The registered Git capture input is incomplete.");
        repository = row.GetString(0);
        revision = row.GetString(1);
        path = row.GetString(2);
    }
}
