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
    /// predecessor's exact successor, even after later revisions. Earlier submissions are never amended.
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
    /// Before any proposal: when this submission's frozen request environment is identical to an earlier
    /// admitted submission's, retain that as its end and throw <see cref="SubmissionInputsUnchanged"/>. The
    /// latest such submission is linked. Only authority already admitted counts, so material that never
    /// gained a committed Contract still seeks admission.
    /// </summary>
    private void RefuseUnchangedInputs(IssueSubmission submission, RequestBundle bundle)
    {
        var environment = FrozenEnvironment(bundle);
        IssueSubmission? admitted = null;
        using (var transaction = connection.BeginTransaction(deferred: true))
        {
            var earlier = new List<string>();
            using (var command = Command("""
                SELECT s.submission_id FROM issue_submissions AS s
                JOIN admission_decisions AS d USING (contract_revision_id)
                WHERE s.work_unit_id = $p0 AND s.submission_sequence < $p1 AND d.outcome = 'admitted'
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
                    || !FrozenEnvironment(candidateBundle).AsSpan().SequenceEqual(environment))
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
                    $"This submission's frozen request, references, starting commit and PR target are identical to already-admitted "
                    + $"Issue submission {admitted.SubmissionId} (Contract revision {admitted.ContractRevisionId}), so it is neither "
                    + "proposed nor executed again; that submission's Attempts carry the existing outcome. Another execution of "
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
    /// The digest-verified manifest of a completed bundle with only its own identities blanked: the bundle
    /// and submission IDs. Everything captured stays, including each reference's selection and content digest,
    /// the starting revision and commit and the PR target branch. The manifest records no capture times.
    /// </summary>
    private static byte[] FrozenEnvironment(RequestBundle bundle)
    {
        var bytes = Encoding.UTF8.GetBytes(bundle.ManifestJson!);
        if (Digests.Bytes(bytes) != bundle.ManifestSha256)
            throw new RequestBundleConflict("The retained RequestBundle manifest does not match its digest.");
        BundleManifestV1? manifest;
        try { manifest = JsonSerializer.Deserialize<BundleManifestV1>(bytes, BundleManifestOptions); }
        catch (JsonException) { throw new RequestBundleConflict("The retained RequestBundle manifest is malformed."); }
        if (manifest is null)
            throw new RequestBundleConflict("The retained RequestBundle manifest is malformed.");
        return JsonSerializer.SerializeToUtf8Bytes(manifest with { BundleId = "", SubmissionId = "" }, BundleManifestOptions);
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
