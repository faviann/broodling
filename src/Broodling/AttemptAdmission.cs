using Microsoft.Data.Sqlite;

namespace Broodling;

public sealed record OriginalB1(string Repository, string CommitOid, string MaterialSha256, string RequestedRevision)
{
    public string RetentionRef => "refs/broodling/starting/" + CommitOid;
}

public sealed record AttemptAbandonment(string AttemptId, string Reason, string AbandonedAt);

/// <summary>An HTTP Attempt owns no local resource: it retains only authority and shared Git custody of original B1.</summary>
public sealed record AttemptRecord(string AttemptId, string WorkUnitId, string ContractRevisionId, bool IsCurrent,
    OriginalB1 B1, string AdmittedAt, AttemptAbandonment? Abandonment,
    AttemptRetirement? Retirement = null, AttemptRetry? Retry = null, CompletionRefusal? CompletionRefusal = null);

public sealed partial class BroodlingStore
{
    /// <summary>
    /// Admit an HTTP DirectTarget Attempt: resolve and retain original B1 in the shared common Git directory,
    /// then commit the Attempt. No checkout or dispatch.
    /// </summary>
    public AttemptRecord AdmitHttpAttempt(string revisionId, string repository, string revision = "HEAD")
    {
        RequireUnpaused();
        RequireHttpDelivery(GetContractRevision(revisionId).Contract);
        return AdmitAttempt(revisionId, GitCustody.Resolve(repository, revision));
    }

    /// <summary>
    /// Admit an HTTP Attempt from one Issue submission's bundle-bound Contract, starting from the
    /// bundle's retained repository preparation. The caller cannot replace its repository or commit.
    /// </summary>
    public AttemptRecord AdmitHttpAttempt(string submissionId)
    {
        RequireUnpaused();
        var submission = GetIssueSubmission(submissionId);
        if (submission.ContractRevisionId is not { } revisionId)
            throw new AttemptAdmissionError("The Issue submission has no admitted Contract revision.");
        var repository = GetRequestBundle(submissionId).Repository
            ?? throw new AttemptAdmissionError("The Issue submission's RequestBundle has no retained repository preparation.");
        return AdmitAttempt(revisionId, repository.StartingState);
    }

    private static void RequireHttpDelivery(Contract contract)
    {
        try { Closability.AuthorizeDelivery(contract); }
        catch (InvalidContractProposal) { throw new AttemptAdmissionError("An HTTP Attempt requires exactly one authorized pull-request effect."); }
    }

    private AttemptRecord AdmitAttempt(string revisionId, StartingState state)
    {
        var contract = GetContractRevision(revisionId);
        if (!IsAdmitted(revisionId))
            throw new AttemptAdmissionError("An Attempt requires a committed admitted Contract decision.");
        GitCustody.AssertSupportedCheckout(state.Repository, state.CommitOid);
        var material = Digests.AdmittedMaterial(contract.Contract.SourceAttribution);
        var id = "at-" + Digests.Parts("broodling.application.attempt.http.v1", revisionId, state.Repository, state.CommitOid, material);
        // Git and SQLite cannot share a transaction. A crash may leave a harmless retention pin;
        // it must never leave an acknowledged Attempt without selected-object custody.
        GitCustody.Retain(state);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireOrdinaryAttemptAuthority(contract.WorkUnitId, revisionId, transaction);
        RequireIssueSubmissionNotCancelled(revisionId, transaction);
        if (RequireBundleAuthority(contract, transaction) is { } bundle && bundle.Repository?.StartingState != state)
            throw new AttemptAdmissionError("A bundle-bound Contract starts only from its retained repository preparation.");
        var existing = ReadAttempts("work_unit_id = $p0", contract.WorkUnitId, transaction).SingleOrDefault(attempt => attempt.IsCurrent);
        if (existing is not null)
        {
            if (existing.AttemptId != id)
                throw new AttemptConflict("This Work Unit already has a current Attempt with different Contract, source or B1 bindings.");
            transaction.Commit();
            return existing; // The first admission's requested spelling remains frozen.
        }
        if (ReadDecision(revisionId, transaction)?.Admitted != true)
            throw new AttemptAdmissionError("An Attempt requires a committed admitted Contract decision.");
        RequireUnpaused(transaction);
        var now = Now();
        InsertAttempt(new(id, contract.WorkUnitId, revisionId, true, new(state.Repository, state.CommitOid, material, state.RequestedRevision), now, null), transaction);
        var result = ReadAttempt(id, transaction);
        transaction.Commit();
        return result;
    }

    private void InsertAttempt(AttemptRecord attempt, SqliteTransaction transaction) =>
        Execute("INSERT INTO attempts VALUES ($p0, $p1, $p2, 1, $p3, $p4, $p5, $p6, $p7)", transaction,
            attempt.AttemptId, attempt.WorkUnitId, attempt.ContractRevisionId, attempt.B1.Repository,
            attempt.B1.CommitOid, attempt.B1.MaterialSha256, attempt.B1.RequestedRevision, attempt.AdmittedAt);

    /// <summary>
    /// A Contract acquires ordinary Attempt authority only in a Work Unit with no ended work, except that a
    /// revision's Contract is not held back by the Contracts preceding it once each of their dispatched
    /// Attempts is retired under verified maintenance. The <c>attempts_no_abandoned_work</c> trigger is the
    /// same rule.
    /// </summary>
    private void RequireOrdinaryAttemptAuthority(string workUnitId, string contractRevisionId, SqliteTransaction transaction)
    {
        RequireIncompleteWorkUnit(workUnitId, contractRevisionId, transaction);
        using var ended = Command("""
            SELECT 1 FROM attempts AS a WHERE a.work_unit_id = $p0 AND a.is_current = 0
              AND NOT (a.contract_revision_id IN (SELECT prior_contract_revision_id FROM revision_prior_contracts
                      WHERE contract_revision_id = $p1)
                  AND (NOT EXISTS (SELECT 1 FROM native_submissions WHERE attempt_id = a.attempt_id AND state <> 'prepared')
                      OR EXISTS (SELECT 1 FROM attempt_retirements WHERE attempt_id = a.attempt_id AND retired_at IS NOT NULL)))
            LIMIT 1
            """, transaction, workUnitId, contractRevisionId);
        if (ended.ExecuteScalar() is not null)
            throw new StaleAttempt("Ordinary admission cannot restore ended authority or authorize replacement; a revision also needs every earlier dispatched Attempt retired under verified maintenance.");
    }

    /// <summary>
    /// Completed work refuses new Attempt authority for the Work Unit, except that a revision's Contract
    /// disregards the completions of the Contracts preceding it. The <c>attempts_no_completed_work</c>
    /// trigger is the same rule.
    /// </summary>
    private void RequireIncompleteWorkUnit(string workUnitId, string contractRevisionId, SqliteTransaction transaction)
    {
        using var completed = Command("""
            SELECT 1 FROM attempt_completions WHERE work_unit_id = $p0
              AND contract_revision_id NOT IN (SELECT prior_contract_revision_id FROM revision_prior_contracts
                  WHERE contract_revision_id = $p1)
            LIMIT 1
            """, transaction, workUnitId, contractRevisionId);
        if (completed.ExecuteScalar() is not null)
            throw new StaleAttempt("Completed Work Units cannot acquire new Attempt authority.");
    }

    public AttemptRecord GetAttempt(string attemptId) => ReadAttempt(attemptId);
    public AttemptRecord? CurrentAttempt(string workUnitId) =>
        ReadAttempts("work_unit_id = $p0 AND is_current = 1", workUnitId).SingleOrDefault();

    /// <summary>Point-in-time authority only. Dependent lifecycle writes must recheck inside their transaction.</summary>
    public AttemptRecord RequireCurrentAttempt(string attemptId) => RequireCurrentAttempt(attemptId, null);
    private AttemptRecord RequireCurrentAttempt(string attemptId, SqliteTransaction? transaction)
    {
        var attempt = ReadAttempt(attemptId, transaction);
        if (!attempt.IsCurrent || attempt.Abandonment is not null)
            throw new StaleAttempt("The Attempt no longer holds durable current authority.");
        return attempt;
    }

    /// <summary>Commit irreversible ineligibility; no native stop or replacement is implied.</summary>
    public AttemptAbandonment AbandonAttempt(string attemptId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new AttemptAdmissionError("Abandonment requires a nonempty reason.");
        using var transaction = connection.BeginTransaction(deferred: false);
        var result = AbandonAttemptInTransaction(attemptId, reason, transaction);
        transaction.Commit();
        return result;
    }

    private AttemptAbandonment AbandonAttemptInTransaction(string attemptId, string reason, SqliteTransaction transaction)
    {
        var attempt = ReadAttempt(attemptId, transaction);
        if (attempt.Abandonment is { } previous) return previous;
        RequireCurrentAttempt(attemptId, transaction);
        Execute("INSERT INTO attempt_abandonments VALUES ($p0, $p1, $p2)", transaction, attemptId, reason, Now());
        return ReadAttempt(attemptId, transaction).Abandonment!;
    }

    private AttemptRecord ReadAttempt(string id, SqliteTransaction? transaction = null) =>
        ReadAttempts("attempt_id = $p0", id, transaction).SingleOrDefault() ?? throw new UnknownRecord("Unknown Attempt.");

    // The predicate is internal SQL, never caller-supplied text. A single SELECT gives coherent authority/abandonment.
    private IReadOnlyList<AttemptRecord> ReadAttempts(string predicate, string value, SqliteTransaction? transaction = null)
    {
        using var command = Command($"""
            SELECT a.*, b.reason, b.abandoned_at, r.reason, r.refused_at FROM attempts AS a
            LEFT JOIN attempt_abandonments AS b USING (attempt_id)
            LEFT JOIN completion_refusals AS r USING (attempt_id)
            WHERE {predicate} ORDER BY a.rowid
            """, transaction, value);
        using var row = command.ExecuteReader();
        var result = new List<AttemptRecord>();
        while (row.Read())
            result.Add(new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetInt64(3) == 1,
                new(row.GetString(4), row.GetString(5), row.GetString(6), row.GetString(7)), row.GetString(8),
                row.IsDBNull(9) ? null : new(row.GetString(0), row.GetString(9), row.GetString(10)),
                ReadRetirement(row.GetString(0), transaction), ReadRetry("attempt_id", row.GetString(0), transaction),
                row.IsDBNull(11) ? null : new(row.GetString(0), row.GetString(11), row.GetString(12))));
        return result.AsReadOnly();
    }
}
