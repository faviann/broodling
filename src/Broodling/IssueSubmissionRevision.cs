using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Request revised work for one explicitly named predecessor: a new Issue submission for the same Work
    /// Unit, captured and prepared afresh by ordinary progression. It is created at most once, only while the
    /// predecessor is the Work Unit's latest submission and has ended: rejected, cancelled, unchanged, or
    /// admitted with every Attempt ended. The Work Unit must have no current Attempt, and every dispatched
    /// Attempt must be retired under verified maintenance. A repeated or concurrent call returns the
    /// predecessor's exact successor, even after later revisions. Revision never amends the predecessor.
    /// </summary>
    public IssueSubmission ReviseIssueSubmission(string predecessorSubmissionId)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var predecessor = ReadIssueSubmission(predecessorSubmissionId, transaction)
            ?? throw new UnknownRecord("Unknown Issue submission.");
        if (predecessor.SuccessorSubmissionId is { } existing)
        {
            var successor = ReadIssueSubmission(existing, transaction)!;
            transaction.Commit();
            return successor;
        }
        if (ReadLatestIssueSubmission(predecessor.WorkUnitId, transaction)!.SubmissionId != predecessor.SubmissionId)
            throw new IssueSubmissionConflict("Only the Work Unit's latest Issue submission can be revised.");
        if (UnfinishedSubmissions(predecessor.SubmissionId, transaction).Count != 0)
            throw new IssueSubmissionConflict("The predecessor is still being prepared or progressed; revise it once it has ended.");
        if (ReadAttempts("work_unit_id = $p0 AND is_current = 1", predecessor.WorkUnitId, transaction).Count != 0)
            throw new IssueSubmissionConflict("The Work Unit still has a current Attempt; revise once it has ended.");
        using (var unretired = Command("""
            SELECT 1 FROM attempts AS a JOIN native_submissions AS n USING (attempt_id)
            LEFT JOIN attempt_retirements AS r USING (attempt_id)
            WHERE a.work_unit_id = $p0 AND n.state <> 'prepared' AND r.retired_at IS NULL LIMIT 1
            """, transaction, predecessor.WorkUnitId))
            if (unretired.ExecuteScalar() is not null)
                throw new IssueSubmissionConflict("A dispatched Attempt of this Work Unit is not yet retired under verified maintenance.");

        var successorId = "issue-sub-" + Guid.NewGuid().ToString("N");
        var now = Now();
        Execute("INSERT INTO issue_submissions VALUES ($p0, $p1, $p2, $p3, 'accepted', NULL, $p4)", transaction,
            successorId, predecessor.WorkUnitId, predecessor.Sequence + 1, predecessor.IssueUrl, now);
        Execute("INSERT INTO issue_submission_revisions VALUES ($p0, $p1, $p2)", transaction,
            successorId, predecessor.SubmissionId, now);
        var result = ReadIssueSubmission(successorId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Before any proposal: when this submission's work-defining identity equals that of an earlier admitted
    /// submission whose Contract has an Attempt, retain that as its end and throw
    /// <see cref="SubmissionInputsUnchanged"/>. The latest such submission is linked. Material that never gained
    /// a committed Contract, or whose Contract never ran, still seeks admission.
    /// </summary>
    private void RefuseUnchangedInputs(IssueSubmission submission, RequestBundle bundle)
    {
        IssueSubmission? admitted = null;
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            var identity = WorkDefiningIdentity(bundle, transaction);
            var earlier = new List<string>();
            using (var command = Command("""
                SELECT s.submission_id FROM issue_submissions AS s
                JOIN admission_decisions AS d USING (contract_revision_id)
                WHERE s.work_unit_id = $p0 AND s.submission_sequence < $p1 AND d.outcome = 'admitted'
                  AND EXISTS (SELECT 1 FROM attempts AS a WHERE a.contract_revision_id = s.contract_revision_id)
                ORDER BY s.submission_sequence DESC
                """, transaction, submission.WorkUnitId, submission.Sequence))
            using (var row = command.ExecuteReader())
                while (row.Read()) earlier.Add(row.GetString(0));
            foreach (var candidate in earlier)
            {
                var candidateBundle = ReadRequestBundle(candidate, transaction);
                var revision = ReadIssueSubmission(candidate, transaction)!.ContractRevisionId!;
                // Only bundle authority counts: an earlier unbound association was never admitted from its bundle.
                if (candidateBundle is null || ReadRevision(revision, transaction)!.Contract.RequestBundle != Binding(candidateBundle)
                    || !WorkDefiningIdentity(candidateBundle, transaction).AsSpan().SequenceEqual(identity))
                    continue;
                admitted = ReadIssueSubmission(candidate, transaction);
                break;
            }
            transaction.Commit();
        }
        if (admitted is null)
            return;

        IssueSubmissionUnchanged unchanged;
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            var current = ReadIssueSubmission(submission.SubmissionId, transaction)!;
            if (current.Unchanged is null)
            {
                if (current.State != "capturing" || current.ContractRevisionId is not null || current.ProposalRefusal is not null)
                    throw new IssueSubmissionConflict("The Issue submission can no longer be prepared.");
                Execute("INSERT INTO issue_submission_unchanged VALUES ($p0, $p1, $p2, $p3, $p4)", transaction,
                    submission.SubmissionId, admitted.SubmissionId, admitted.ContractRevisionId,
                    "This submission's work-defining request (the primary and referenced issues' titles and bodies, "
                    + "referenced comments' bodies, other references' content, starting commit and PR target) is identical to that "
                    + $"of already-admitted Issue submission {admitted.SubmissionId} (Contract revision {admitted.ContractRevisionId}), so it is neither "
                    + "proposed nor executed again; that Contract and its Attempts carry the existing outcome. Another execution of "
                    + "unchanged authority uses the explicit replacement operation.", Now());
                Execute("UPDATE issue_submissions SET state = 'unchanged' WHERE submission_id = $p0 AND state = 'capturing'",
                    transaction, submission.SubmissionId);
            }
            unchanged = ReadIssueSubmission(submission.SubmissionId, transaction)!.Unchanged!;
            transaction.Commit();
        }
        throw new SubmissionInputsUnchanged(unchanged);
    }

    /// <summary>
    /// A completed bundle's digest-verified manifest with its own bundle and submission IDs blanked, and with the
    /// retained GitHub responses of the primary issue and of each referenced issue or comment reduced to their
    /// work-defining fields: an issue's title and body, a comment's body. GitHub bookkeeping in those responses,
    /// such as <c>updated_at</c>, comment counts or reactions, therefore changes nothing. Every reference's
    /// selection and every other reference's content digest stay, as do the acquisition inputs, policy and
    /// limits, the starting revision and commit and the PR target branch. The manifest records no capture times.
    /// It serves only this comparison; retained bundles are never changed.
    /// </summary>
    private byte[] WorkDefiningIdentity(RequestBundle bundle, SqliteTransaction transaction)
    {
        var bytes = Encoding.UTF8.GetBytes(bundle.ManifestJson!);
        if (Digests.Bytes(bytes) != bundle.ManifestSha256)
            throw new RequestBundleConflict("The retained RequestBundle manifest does not match its digest.");
        BundleManifestV1? manifest;
        try { manifest = JsonSerializer.Deserialize<BundleManifestV1>(bytes, BundleManifestOptions); }
        catch (JsonException) { throw new RequestBundleConflict("The retained RequestBundle manifest is malformed."); }
        if (manifest is null)
            throw new RequestBundleConflict("The retained RequestBundle manifest is malformed.");
        var references = manifest.References.Select(reference =>
        {
            string[] fields = reference.ReferenceId == "primary" ? ["title", "body"]
                : !reference.ReferenceId.StartsWith("github:", StringComparison.Ordinal) ? []
                : reference.ReferenceId.Contains("#issuecomment-", StringComparison.Ordinal) ? ["body"] : ["title", "body"];
            if (fields.Length == 0 || reference.SourceId is not { } sourceId)
                return reference;
            var source = ReadSource(sourceId, transaction);
            if (source.ContentSha256 != reference.ContentSha256)
                throw new RequestBundleConflict("The retained source digest does not match the completed manifest membership.");
            string?[] values;
            try
            {
                using var document = JsonDocument.Parse(source.Content);
                values = fields.Select(field => document.RootElement.TryGetProperty(field, out var value)
                    && value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToArray();
            }
            // Only GitHub acquisition validates the response shape; a source captured otherwise keeps its digest.
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return reference;
            }
            // The source ID derives from the whole response, so the work-defining digest alone identifies it.
            return reference with { SourceId = null, ContentSha256 = Digests.Bytes(JsonSerializer.SerializeToUtf8Bytes(values)) };
        }).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(manifest with { BundleId = "", SubmissionId = "", References = references },
            BundleManifestOptions);
    }

    private string? ReadRevisionLink(string column, string byColumn, string submissionId, SqliteTransaction? transaction)
    {
        using var command = Command($"SELECT {column} FROM issue_submission_revisions WHERE {byColumn} = $p0",
            transaction, submissionId);
        return command.ExecuteScalar() as string;
    }

    private IssueSubmissionUnchanged? ReadIssueSubmissionUnchanged(string submissionId, SqliteTransaction? transaction)
    {
        using var command = Command("SELECT submission_id, admitted_submission_id, contract_revision_id, explanation, recorded_at "
            + "FROM issue_submission_unchanged WHERE submission_id = $p0", transaction, submissionId);
        using var row = command.ExecuteReader();
        return row.Read()
            ? new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4))
            : null;
    }
}
