using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Durable acceptance before Contract preparation. Attempt IDs are derived from
/// the existing Contract/Attempt records and are never a second execution ledger.
/// </summary>
public sealed record IssueSubmission(string SubmissionId, string WorkUnitId, long Sequence, string IssueUrl,
    string State, string ReceivedAt, string? ContractRevisionId, IReadOnlyList<string> AttemptIds);

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Create the first ordinary submission for a GitHub issue, or return the
    /// latest retained submission. URL parsing happens before any durable write
    /// or upstream call.
    /// </summary>
    public IssueSubmission SubmitIssue(string issueUrl)
    {
        var reference = WorkReference.ParseIssueUrl(issueUrl);
        return SubmitIssue(reference);
    }

    /// <summary>Submit an already parsed supported GitHub Work Unit.</summary>
    public IssueSubmission SubmitIssue(WorkReference reference)
    {
        if (reference is null || reference.Host != "github.com")
            throw new InvalidWorkReference("Issue submission requires a supported GitHub Work Unit.");

        using var transaction = connection.BeginTransaction(deferred: false);
        var work = ReadWorkUnit(reference.WorkUnitId, transaction);
        if (work is null)
        {
            Execute("""
                INSERT INTO work_units VALUES ($p0, $p1, $p2, $p3, $p4, $p5, $p6, $p7, $p8, $p9)
                """, transaction, reference.WorkUnitId, reference.Key, reference.Host, reference.Owner,
                reference.Repository, reference.IssueNumber, reference.IssueLocator,
                reference.RepositoryIdentity, reference.IssueIdentity, Now());
            work = ReadWorkUnit(reference.WorkUnitId, transaction)!;
        }
        else
        {
            CheckPins(work, reference);
            Execute("""
                UPDATE work_units SET repository_identity = COALESCE(repository_identity, $p0),
                    issue_identity = COALESCE(issue_identity, $p1) WHERE work_unit_id = $p2
                """, transaction, reference.RepositoryIdentity, reference.IssueIdentity, reference.WorkUnitId);
        }

        if (ReadLatestIssueSubmission(reference.WorkUnitId, transaction) is { } existing)
        {
            transaction.Commit();
            return existing;
        }

        var now = Now();
        Execute("INSERT INTO work_submissions VALUES ($p0, $p1, $p2, $p3, $p4)", transaction,
            "sub-" + Guid.NewGuid().ToString("N"), reference.WorkUnitId,
            reference.SubmittedRepository, reference.SubmittedIssue, now);
        long sequence;
        using (var nextSequence = Command("SELECT COALESCE(MAX(submission_sequence), 0) + 1 FROM issue_submissions WHERE work_unit_id = $p0",
            transaction, reference.WorkUnitId))
            sequence = Convert.ToInt64(nextSequence.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        Execute("INSERT INTO issue_submissions VALUES ($p0, $p1, $p2, $p3, 'accepted', NULL, $p4)", transaction,
            "issue-sub-" + Guid.NewGuid().ToString("N"), reference.WorkUnitId, sequence, reference.IssueLocator, now);
        var result = ReadLatestIssueSubmission(reference.WorkUnitId, transaction)!;
        transaction.Commit();
        return result;
    }

    /// <summary>Exact read by the durable acceptance handle.</summary>
    public IssueSubmission GetIssueSubmission(string submissionId)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadIssueSubmission(submissionId, transaction)
            ?? throw new UnknownRecord("Unknown Issue submission.");
        transaction.Commit();
        return result;
    }

    /// <summary>Return the latest ordinary submission for a supported issue, without side effects.</summary>
    public IssueSubmission? FindIssueSubmission(string issueUrl) =>
        FindIssueSubmission(WorkReference.ParseIssueUrl(issueUrl));

    public IssueSubmission? FindIssueSubmission(WorkReference reference)
    {
        if (reference is null || reference.Host != "github.com")
            throw new InvalidWorkReference("Issue submission requires a supported GitHub Work Unit.");
        using var transaction = connection.BeginTransaction(deferred: true);
        var work = ReadWorkUnit(reference.WorkUnitId, transaction);
        if (work is null)
        {
            transaction.Commit();
            return null;
        }
        CheckPins(work, reference);
        var result = ReadLatestIssueSubmission(reference.WorkUnitId, transaction);
        transaction.Commit();
        return result;
    }

    public IReadOnlyList<IssueSubmission> IssueHistory(string issueUrl) =>
        IssueHistory(WorkReference.ParseIssueUrl(issueUrl));

    /// <summary>Read all retained submissions for one issue, oldest first.</summary>
    public IReadOnlyList<IssueSubmission> IssueHistory(WorkReference reference)
    {
        if (reference is null || reference.Host != "github.com")
            throw new InvalidWorkReference("Issue submission requires a supported GitHub Work Unit.");
        using var transaction = connection.BeginTransaction(deferred: true);
        var work = ReadWorkUnit(reference.WorkUnitId, transaction);
        if (work is null)
        {
            transaction.Commit();
            return Array.Empty<IssueSubmission>();
        }
        CheckPins(work, reference);
        var ids = new List<string>();
        using (var command = Command("SELECT submission_id FROM issue_submissions WHERE work_unit_id = $p0 ORDER BY submission_sequence", transaction,
            reference.WorkUnitId))
        using (var row = command.ExecuteReader())
            while (row.Read()) ids.Add(row.GetString(0));
        var result = ids.Select(id => ReadIssueSubmission(id, transaction)!).ToArray();
        transaction.Commit();
        return Array.AsReadOnly(result);
    }

    /// <summary>Bind one exact submission to one existing Contract revision, once.</summary>
    public IssueSubmission AssociateIssueSubmission(string submissionId, string contractRevisionId)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var submission = ReadIssueSubmission(submissionId, transaction)
            ?? throw new UnknownRecord("Unknown Issue submission.");
        if (submission.ContractRevisionId is { } existing)
        {
            if (existing != contractRevisionId)
                throw new IssueSubmissionConflict("The Issue submission is already bound to another Contract revision.");
            transaction.Commit();
            return submission;
        }

        var revision = ReadRevision(contractRevisionId, transaction)
            ?? throw new UnknownRecord("Unknown Contract revision.");
        if (revision.WorkUnitId != submission.WorkUnitId)
            throw new IssueSubmissionConflict("The Contract revision belongs to another Work Unit.");
        Execute("UPDATE issue_submissions SET contract_revision_id = $p0 WHERE submission_id = $p1", transaction,
            contractRevisionId, submissionId);
        var result = ReadIssueSubmission(submissionId, transaction)!;
        transaction.Commit();
        return result;
    }

    private IssueSubmission? ReadLatestIssueSubmission(string workUnitId, SqliteTransaction? transaction = null)
    {
        string? id;
        using (var command = Command("SELECT submission_id FROM issue_submissions WHERE work_unit_id = $p0 ORDER BY submission_sequence DESC LIMIT 1",
            transaction, workUnitId))
            id = command.ExecuteScalar() as string;
        return id is null ? null : ReadIssueSubmission(id, transaction);
    }

    private IssueSubmission? ReadIssueSubmission(string submissionId, SqliteTransaction? transaction = null)
    {
        string id, workUnitId, issueUrl, state, receivedAt;
        long sequence;
        string? contractRevisionId;
        using (var command = Command("SELECT submission_id, work_unit_id, submission_sequence, issue_url, state, received_at, contract_revision_id FROM issue_submissions WHERE submission_id = $p0",
            transaction, submissionId))
        using (var row = command.ExecuteReader())
        {
            if (!row.Read()) return null;
            id = row.GetString(0);
            workUnitId = row.GetString(1);
            sequence = row.GetInt64(2);
            issueUrl = row.GetString(3);
            state = row.GetString(4);
            receivedAt = row.GetString(5);
            contractRevisionId = row.IsDBNull(6) ? null : row.GetString(6);
        }
        var attempts = contractRevisionId is null ? Array.Empty<string>() : ReadIssueSubmissionAttempts(contractRevisionId, transaction);
        return new(id, workUnitId, sequence, issueUrl, state, receivedAt,
            contractRevisionId, Array.AsReadOnly(attempts));
    }

    private string[] ReadIssueSubmissionAttempts(string contractRevisionId, SqliteTransaction? transaction)
    {
        using var command = Command("SELECT attempt_id FROM attempts WHERE contract_revision_id = $p0 ORDER BY admitted_at, attempt_id",
            transaction, contractRevisionId);
        using var row = command.ExecuteReader();
        var result = new List<string>();
        while (row.Read()) result.Add(row.GetString(0));
        return result.ToArray();
    }
}
