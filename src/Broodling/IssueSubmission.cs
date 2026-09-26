using Microsoft.Data.Sqlite;

namespace Broodling;

/// <summary>
/// Immutable cancellation fact for one exact Issue submission. Its nullable
/// AttemptId is the cancellation's immutable stop/no-stop binding; it does not
/// represent the full shared Attempt lineage exposed by IssueSubmission.
/// </summary>
public sealed record IssueSubmissionCancellation(string SubmissionId, string? AttemptId, string Reason, string CancelledAt);

/// <summary>
/// Retained acceptance for one exact Issue submission. AttemptIds are derived
/// from the existing shared Contract/Attempt records, never copied into a second
/// execution ledger; Cancellation separately records this submission's binding.
/// </summary>
public sealed record IssueSubmission(string SubmissionId, string WorkUnitId, long Sequence, string IssueUrl,
    string State, string ReceivedAt, string? ContractRevisionId, IReadOnlyList<string> AttemptIds)
{
    /// <summary>The immutable first cancellation stop/no-stop binding, when this submission was cancelled.</summary>
    public IssueSubmissionCancellation? Cancellation { get; init; }

    /// <summary>The retained refusal of this submission's Contract proposal; it then has no Contract.</summary>
    public ContractProposalRefusal? ProposalRefusal { get; init; }

    /// <summary>The submission this revision explicitly names; null for a Work Unit's first submission.</summary>
    public string? PredecessorSubmissionId { get; init; }

    /// <summary>The revision that names this submission as its predecessor, if one was requested.</summary>
    public string? SuccessorSubmissionId { get; init; }

    /// <summary>Why this submission ended without a Contract: its inputs repeat already-admitted authority.</summary>
    public IssueSubmissionUnchanged? Unchanged { get; init; }
}

/// <summary>
/// Retained end of a submission whose work-defining request identity equals that of an earlier admitted
/// submission whose Contract has an Attempt. It links that submission and its Contract, whose Attempts carry the
/// existing outcome.
/// </summary>
public sealed record IssueSubmissionUnchanged(string SubmissionId, string AdmittedSubmissionId,
    string ContractRevisionId, string Explanation, string RecordedAt);

/// <summary>One retained reason a bundle-bound Contract proposal was refused before any revision.</summary>
public sealed record ContractProposalFinding(string Code, string Detail);

/// <summary>Immutable refusal of one Issue submission's proposal; the submission is rejected.</summary>
public sealed record ContractProposalRefusal(string SubmissionId, IReadOnlyList<ContractProposalFinding> Findings,
    string RefusedAt);

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

    /// <summary>
    /// Bind one exact submission to one existing Contract revision, once. A submission
    /// with a RequestBundle accepts only a Contract bound to that exact completed bundle,
    /// which <see cref="AdmitRequestBundle"/> records together with this association.
    /// </summary>
    public IssueSubmission AssociateIssueSubmission(string submissionId, string contractRevisionId)
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        // Its Contract is what exempts a revision from earlier ended work, so only its own bundle's admission binds it.
        if (ReadRevisionLink("predecessor_submission_id", "successor_submission_id", submissionId, transaction) is not null)
            throw new IssueSubmissionConflict("A revision acquires its Contract only through its own RequestBundle's admission.");
        var result = AssociateIssueSubmission(submissionId, contractRevisionId, transaction);
        transaction.Commit();
        return result;
    }

    private IssueSubmission AssociateIssueSubmission(string submissionId, string contractRevisionId,
        SqliteTransaction transaction)
    {
        var submission = ReadIssueSubmission(submissionId, transaction)
            ?? throw new UnknownRecord("Unknown Issue submission.");
        if (submission.State == "cancelled")
            throw new IssueSubmissionConflict("A cancelled Issue submission cannot acquire Contract authority.");
        if (submission.ContractRevisionId is { } existing && existing != contractRevisionId)
            throw new IssueSubmissionConflict("The Issue submission is already bound to another Contract revision.");

        var revision = ReadRevision(contractRevisionId, transaction)
            ?? throw new UnknownRecord("Unknown Contract revision.");
        if (revision.WorkUnitId != submission.WorkUnitId)
            throw new IssueSubmissionConflict("The Contract revision belongs to another Work Unit.");
        // Capture eligibility requires an unbound submission, so binding before
        // completion would strand the bundle. The writer reservation orders this
        // check against completion.
        var bundle = ReadRequestBundle(submissionId, transaction);
        if (bundle is { State: not "complete" })
            throw new IssueSubmissionConflict("The Issue submission's RequestBundle must complete before Contract association.");
        // Checked on replay too: an older unbound association is not confirmed as bundle authority.
        if (revision.Contract.RequestBundle != Binding(bundle))
            throw new IssueSubmissionConflict("The Contract revision is not bound to this Issue submission's RequestBundle.");
        if (submission.ContractRevisionId is not null)
            return submission;
        Execute("UPDATE issue_submissions SET contract_revision_id = $p0 WHERE submission_id = $p1", transaction,
            contractRevisionId, submissionId);
        return ReadIssueSubmission(submissionId, transaction)!;
    }

    /// <summary>
    /// Cancel one retained Issue submission before any later admission can use
    /// it. If it is the last non-cancelled submission for its shared Contract
    /// and has an Attempt, cancellation and abandonment commit in one SQLite
    /// transaction before the existing native-stop path is entered. When another
    /// submission survives, this cancellation records no stop and leaves that
    /// Attempt available to the survivor.
    /// </summary>
    public async Task<IssueSubmission> CancelIssueSubmissionAsync(string submissionId, string reason,
        INativeStopper? transport = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new IssueSubmissionConflict("Submission cancellation requires a nonempty reason.");

        string? attemptId;
        var stopReason = reason;
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            var submission = ReadIssueSubmission(submissionId, transaction)
                ?? throw new UnknownRecord("Unknown Issue submission.");
            var cancellation = ReadIssueSubmissionCancellation(submissionId, transaction);
            if (cancellation is not null)
            {
                if (submission.State != "cancelled")
                    throw new IssueSubmissionConflict("The Issue submission has an invalid cancellation state.");
                // Replay is bound to the first durable target. In particular, do
                // not inspect current or last Attempt state here: a later
                // replacement belongs to a different authority generation.
                attemptId = cancellation.AttemptId;
                stopReason = cancellation.Reason;
            }
            else
            {
                if (submission.State == "completed")
                    throw new IssueSubmissionConflict("A completed Issue submission cannot be cancelled.");
                var attempts = submission.ContractRevisionId is null
                    ? Array.Empty<AttemptRecord>()
                    : ReadAttempts("contract_revision_id = $p0", submission.ContractRevisionId, transaction).ToArray();
                if (attempts.Any(attempt => ReadCompletion(attempt.AttemptId, transaction) is not null))
                    throw new IssueSubmissionConflict("A completed Issue submission cannot be cancelled.");

                // A shared Contract's Attempt lineage is not per-submission stop
                // authority. Bind no stop when another retained submission can
                // still authorize ordinary progression; the nullable binding is
                // immutable cancellation evidence, not a replacement lineage.
                var ownsStop = submission.ContractRevisionId is not null
                    && !HasOtherNonCancelledIssueSubmission(submission.ContractRevisionId, submissionId, transaction);
                attemptId = ownsStop
                    ? attempts.FirstOrDefault(attempt => attempt.IsCurrent)?.AttemptId
                        ?? attempts.LastOrDefault()?.AttemptId
                    : null;
                Execute("INSERT INTO issue_submission_cancellations VALUES ($p0, $p1, $p2, $p3)",
                    transaction, submissionId, attemptId, reason, Now());
                Execute("UPDATE issue_submissions SET state = 'cancelled' WHERE submission_id = $p0",
                    transaction, submissionId);
                if (attemptId is not null)
                {
                    var attempt = attempts.Single(item => item.AttemptId == attemptId);
                    if (attempt.Abandonment is null)
                        AbandonAttemptInTransaction(attemptId, reason, transaction);
                }
            }
            transaction.Commit();
        }

        if (attemptId is not null)
        {
            // The existing exact Attempt stop path decides whether an external
            // request is needed. Safe undispatched retirement needs no transport;
            // a known native run still requires one, after cancellation commits.
            await StopAsync(attemptId, stopReason, transport, cancellationToken);
        }
        return GetIssueSubmission(submissionId);
    }

    private bool HasOtherNonCancelledIssueSubmission(string contractRevisionId, string submissionId,
        SqliteTransaction transaction)
    {
        using var command = Command("SELECT 1 FROM issue_submissions "
            + "WHERE contract_revision_id = $p0 AND submission_id <> $p1 AND state <> 'cancelled' LIMIT 1",
            transaction, contractRevisionId, submissionId);
        return command.ExecuteScalar() is not null;
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
            contractRevisionId, Array.AsReadOnly(attempts))
        {
            Cancellation = ReadIssueSubmissionCancellation(id, transaction),
            ProposalRefusal = ReadContractProposalRefusal(id, transaction),
            PredecessorSubmissionId = ReadRevisionLink("predecessor_submission_id", "successor_submission_id", id, transaction),
            SuccessorSubmissionId = ReadRevisionLink("successor_submission_id", "predecessor_submission_id", id, transaction),
            Unchanged = ReadIssueSubmissionUnchanged(id, transaction)
        };
    }

    private ContractProposalRefusal? ReadContractProposalRefusal(string submissionId, SqliteTransaction? transaction)
    {
        using var command = Command("SELECT submission_id, findings_json, refused_at FROM contract_proposal_refusals WHERE submission_id = $p0",
            transaction, submissionId);
        using var row = command.ExecuteReader();
        return row.Read()
            ? new(row.GetString(0), Array.AsReadOnly(
                System.Text.Json.JsonSerializer.Deserialize<ContractProposalFinding[]>(row.GetString(1))!), row.GetString(2))
            : null;
    }

    private IssueSubmissionCancellation? ReadIssueSubmissionCancellation(string submissionId,
        SqliteTransaction? transaction = null)
    {
        using var command = Command("SELECT submission_id, attempt_id, reason, cancelled_at FROM issue_submission_cancellations WHERE submission_id = $p0",
            transaction, submissionId);
        using var row = command.ExecuteReader();
        return row.Read()
            ? new(row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1), row.GetString(2), row.GetString(3))
            : null;
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
