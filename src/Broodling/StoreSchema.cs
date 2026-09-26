using System.Text;
using Microsoft.Data.Sqlite;

namespace Broodling;

internal static class StoreSchema
{
    // Fresh-state identity for the HTTP integration. Pre-transition `broodling.dotnet`
    // schemas are refused unchanged, never upgraded.
    internal const string Format = "broodling.application";
    internal const int Version = 1;
    internal static string DefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(Sql));
    internal static SchemaIdentity Current => new(Format, Version, DefinitionHash);

    // deployment/application-schemas.json freezes each released identity. A released definition is
    // never edited, so a later schema change increments Version and gives every older released
    // identity one disposition: listed in UpgradesFrom with its upgrade, or refused here with a
    // documented reason (ApplicationSchemaFreezeTests).
    internal const string ReleasedRefusal = "released_schema_refused";
    internal static readonly IReadOnlyDictionary<SchemaIdentity, string> Refused = new Dictionary<SchemaIdentity, string>();
    /// <summary>
    /// Released identities that <c>upgrade-store</c> upgrades to <see cref="Current"/>: none. The
    /// release record reports this list, so an upgrade is added here together with its implementation.
    /// </summary>
    internal static readonly IReadOnlyList<SchemaIdentity> UpgradesFrom = [];
    internal const string Sql = IdentitySql + "\n" + AdmissionSql + "\n" + AttemptSql + "\n" + ProvisioningSql
        + "\n" + DispatchSql + "\n" + CompletionSql + "\n" + RetirementSql + "\n" + IssueSubmissionSql
        + "\n" + InstallationSql + "\n" + RequestBundleSql + "\n" + CancellationSql + "\n" + RepositoryPreparationSql
        + "\n" + ProposalRefusalSql + "\n" + RevisionSql;

    // A refused proposal leaves findings and no Contract; the submission can never bind one later.
    internal const string ProposalRefusalSql = """
        CREATE TABLE contract_proposal_refusals (
            submission_id TEXT PRIMARY KEY REFERENCES issue_submissions(submission_id),
            findings_json TEXT NOT NULL CHECK (json_valid(findings_json) AND json_array_length(findings_json) > 0),
            refused_at TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER contract_proposal_refusal_bound BEFORE INSERT ON contract_proposal_refusals
        WHEN NOT EXISTS (
            SELECT 1 FROM issue_submissions AS s JOIN request_bundles AS b USING (submission_id)
            WHERE s.submission_id = NEW.submission_id AND s.state = 'capturing'
              AND s.contract_revision_id IS NULL AND b.state = 'complete'
        )
        BEGIN SELECT RAISE(ABORT, 'Contract proposal refusal requires an unbound submission with a completed RequestBundle'); END;
        CREATE TRIGGER contract_proposal_refusal_no_update BEFORE UPDATE ON contract_proposal_refusals
        BEGIN SELECT RAISE(ABORT, 'Contract proposal refusal is immutable'); END;
        CREATE TRIGGER contract_proposal_refusal_no_delete BEFORE DELETE ON contract_proposal_refusals
        BEGIN SELECT RAISE(ABORT, 'Contract proposal refusal is durable'); END;
        CREATE TRIGGER contract_proposal_refusal_final BEFORE UPDATE OF contract_revision_id ON issue_submissions
        WHEN NEW.contract_revision_id IS NOT NULL
          AND EXISTS (SELECT 1 FROM contract_proposal_refusals WHERE submission_id = NEW.submission_id)
        BEGIN SELECT RAISE(ABORT, 'A refused Contract proposal is final for its Issue submission'); END;
        """;

    // A revision links one successor submission to its predecessor, the Work Unit's latest submission when
    // it was requested. A successor's Contract may acquire Attempt authority past the completed or ended work
    // of the Contracts that precede it, once every dispatched Attempt among them is retired; nothing else does.
    // An unchanged successor retains its link to the already-admitted authority it repeats, and no Contract.
    internal const string RevisionSql = """
        CREATE TABLE issue_submission_revisions (
            successor_submission_id TEXT PRIMARY KEY REFERENCES issue_submissions(submission_id),
            predecessor_submission_id TEXT NOT NULL UNIQUE REFERENCES issue_submissions(submission_id),
            requested_at TEXT NOT NULL,
            CHECK (successor_submission_id <> predecessor_submission_id)
        ) STRICT;
        CREATE TRIGGER issue_submission_revision_bound BEFORE INSERT ON issue_submission_revisions
        WHEN NOT EXISTS (
            SELECT 1 FROM issue_submissions AS successor JOIN issue_submissions AS predecessor USING (work_unit_id)
            WHERE successor.submission_id = NEW.successor_submission_id
              AND predecessor.submission_id = NEW.predecessor_submission_id
              AND successor.submission_sequence = predecessor.submission_sequence + 1
              AND successor.state = 'accepted' AND successor.contract_revision_id IS NULL
        )
        BEGIN SELECT RAISE(ABORT, 'A revision links a new submission to the latest submission of its Work Unit'); END;
        CREATE TRIGGER issue_submission_revision_no_update BEFORE UPDATE ON issue_submission_revisions
        BEGIN SELECT RAISE(ABORT, 'Issue submission revision lineage is immutable'); END;
        CREATE TRIGGER issue_submission_revision_no_delete BEFORE DELETE ON issue_submission_revisions
        BEGIN SELECT RAISE(ABORT, 'Issue submission revision lineage is durable'); END;
        CREATE VIEW revision_prior_contracts AS
        SELECT successor.contract_revision_id AS contract_revision_id,
            earlier.contract_revision_id AS prior_contract_revision_id
        FROM issue_submission_revisions AS link
        JOIN issue_submissions AS successor ON successor.submission_id = link.successor_submission_id
        JOIN issue_submissions AS earlier ON earlier.work_unit_id = successor.work_unit_id
            AND earlier.submission_sequence < successor.submission_sequence
        WHERE successor.contract_revision_id IS NOT NULL AND earlier.contract_revision_id IS NOT NULL
          AND earlier.contract_revision_id <> successor.contract_revision_id;
        -- The Work Unit's latest submission supersedes a Contract unless it is bound to that Contract or
        -- ended unchanged with a link to it; a superseded Contract's Attempts are never replaced.
        CREATE VIEW superseded_contracts AS
        SELECT DISTINCT bound.contract_revision_id AS contract_revision_id
        FROM issue_submissions AS bound
        JOIN issue_submissions AS latest ON latest.work_unit_id = bound.work_unit_id
            AND latest.submission_sequence = (SELECT MAX(submission_sequence) FROM issue_submissions
                WHERE work_unit_id = bound.work_unit_id)
        WHERE bound.contract_revision_id IS NOT NULL
          AND latest.contract_revision_id IS NOT bound.contract_revision_id
          AND NOT EXISTS (SELECT 1 FROM issue_submission_unchanged AS u
              WHERE u.submission_id = latest.submission_id AND u.contract_revision_id = bound.contract_revision_id);

        CREATE TABLE issue_submission_unchanged (
            submission_id TEXT PRIMARY KEY REFERENCES issue_submissions(submission_id),
            admitted_submission_id TEXT NOT NULL REFERENCES issue_submissions(submission_id),
            contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
            explanation TEXT NOT NULL CHECK (length(trim(explanation)) > 0),
            recorded_at TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER issue_submission_unchanged_bound BEFORE INSERT ON issue_submission_unchanged
        WHEN NOT EXISTS (
            SELECT 1 FROM issue_submissions AS s JOIN request_bundles AS b USING (submission_id)
            JOIN issue_submissions AS admitted ON admitted.work_unit_id = s.work_unit_id
            JOIN admission_decisions AS d ON d.contract_revision_id = admitted.contract_revision_id
            WHERE s.submission_id = NEW.submission_id AND s.state = 'capturing'
              AND s.contract_revision_id IS NULL AND b.state = 'complete'
              AND admitted.submission_id = NEW.admitted_submission_id
              AND admitted.submission_sequence < s.submission_sequence
              AND admitted.contract_revision_id = NEW.contract_revision_id AND d.outcome = 'admitted'
        )
        BEGIN SELECT RAISE(ABORT, 'An unchanged submission links an earlier admitted submission of its Work Unit'); END;
        CREATE TRIGGER issue_submission_unchanged_no_update BEFORE UPDATE ON issue_submission_unchanged
        BEGIN SELECT RAISE(ABORT, 'An unchanged-input explanation is immutable'); END;
        CREATE TRIGGER issue_submission_unchanged_no_delete BEFORE DELETE ON issue_submission_unchanged
        BEGIN SELECT RAISE(ABORT, 'An unchanged-input explanation is durable'); END;
        CREATE TRIGGER issue_submission_unchanged_final BEFORE UPDATE OF contract_revision_id ON issue_submissions
        WHEN NEW.contract_revision_id IS NOT NULL
          AND EXISTS (SELECT 1 FROM issue_submission_unchanged WHERE submission_id = NEW.submission_id)
        BEGIN SELECT RAISE(ABORT, 'An unchanged Issue submission never acquires Contract authority'); END;
        """;

    internal const string CancellationSql = """
        CREATE TABLE issue_submission_cancellations (
            submission_id TEXT PRIMARY KEY REFERENCES issue_submissions(submission_id),
            attempt_id TEXT REFERENCES attempts(attempt_id),
            reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
            cancelled_at TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER issue_submission_cancellation_bound BEFORE INSERT ON issue_submission_cancellations
        WHEN NOT EXISTS (
            SELECT 1 FROM issue_submissions
            WHERE submission_id = NEW.submission_id AND state NOT IN ('cancelled', 'completed')
        ) OR (NEW.attempt_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM attempts AS a
            JOIN issue_submissions AS s ON s.submission_id = NEW.submission_id
            WHERE a.attempt_id = NEW.attempt_id
              AND a.work_unit_id = s.work_unit_id
              AND a.contract_revision_id IS s.contract_revision_id
        ))
        BEGIN SELECT RAISE(ABORT, 'Issue submission cancellation must bind its exact retained Attempt'); END;
        CREATE TRIGGER issue_submission_cancellation_no_update BEFORE UPDATE ON issue_submission_cancellations
        BEGIN SELECT RAISE(ABORT, 'Issue submission cancellation is immutable'); END;
        CREATE TRIGGER issue_submission_cancellation_no_delete BEFORE DELETE ON issue_submission_cancellations
        BEGIN SELECT RAISE(ABORT, 'Issue submission cancellation is durable'); END;
        CREATE TRIGGER cancelled_submission_no_admission BEFORE INSERT ON admission_decisions
        WHEN EXISTS (
            SELECT 1 FROM issue_submissions
            WHERE contract_revision_id = NEW.contract_revision_id AND state = 'cancelled'
        ) AND NOT EXISTS (
            SELECT 1 FROM issue_submissions
            WHERE contract_revision_id = NEW.contract_revision_id AND state <> 'cancelled'
        )
        BEGIN SELECT RAISE(ABORT, 'cancelled Issue submission cannot acquire Contract admission'); END;
        CREATE TRIGGER cancelled_submission_no_attempt BEFORE INSERT ON attempts
        WHEN EXISTS (
            SELECT 1 FROM issue_submissions
            WHERE contract_revision_id = NEW.contract_revision_id AND state = 'cancelled'
        ) AND NOT EXISTS (
            SELECT 1 FROM issue_submissions
            WHERE contract_revision_id = NEW.contract_revision_id AND state <> 'cancelled'
        ) AND NOT EXISTS (
            SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id
        )
        BEGIN SELECT RAISE(ABORT, 'cancelled Issue submission cannot acquire ordinary Attempt authority'); END;
        CREATE TRIGGER issue_submission_cancel_state BEFORE UPDATE OF state ON issue_submissions
        WHEN (NEW.state = 'cancelled' AND NOT EXISTS (
            SELECT 1 FROM issue_submission_cancellations WHERE submission_id = NEW.submission_id
        )) OR (OLD.state = 'cancelled' AND NEW.state <> 'cancelled')
        BEGIN SELECT RAISE(ABORT, 'Issue submission cancellation requires an immutable cancellation fact'); END;
        """;
    internal const string IssueSubmissionSql = """
        CREATE TABLE issue_submissions (
            submission_id TEXT PRIMARY KEY,
            work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
            submission_sequence INTEGER NOT NULL CHECK (submission_sequence > 0),
            issue_url TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('accepted', 'capturing', 'preparing', 'admitted', 'rejected', 'cancelled', 'interrupted', 'abandoned', 'completed', 'unchanged')),
            contract_revision_id TEXT REFERENCES contract_revisions(contract_revision_id),
            received_at TEXT NOT NULL,
            UNIQUE (work_unit_id, submission_sequence)
        ) STRICT;
        CREATE TRIGGER issue_submission_binding BEFORE INSERT ON issue_submissions
        WHEN NEW.contract_revision_id IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM contract_revisions
            WHERE contract_revision_id = NEW.contract_revision_id AND work_unit_id = NEW.work_unit_id
        )
        BEGIN SELECT RAISE(ABORT, 'Issue submission Contract must belong to its Work Unit'); END;
        CREATE TRIGGER issue_submission_update BEFORE UPDATE ON issue_submissions
        WHEN OLD.submission_id <> NEW.submission_id OR OLD.work_unit_id <> NEW.work_unit_id
          OR OLD.submission_sequence <> NEW.submission_sequence
          OR OLD.issue_url <> NEW.issue_url OR OLD.received_at <> NEW.received_at
          OR (OLD.contract_revision_id IS NOT NULL AND OLD.contract_revision_id IS NOT NEW.contract_revision_id)
          OR (NEW.contract_revision_id IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM contract_revisions
              WHERE contract_revision_id = NEW.contract_revision_id AND work_unit_id = NEW.work_unit_id
          ))
        BEGIN SELECT RAISE(ABORT, 'Issue submission identity and Contract binding are immutable'); END;
        CREATE TRIGGER issue_submissions_no_delete BEFORE DELETE ON issue_submissions
        BEGIN SELECT RAISE(ABORT, 'Issue submission history is immutable'); END;
        """;

    internal const string RequestBundleSql = """
        CREATE TABLE request_bundles (
            bundle_id TEXT PRIMARY KEY,
            submission_id TEXT NOT NULL UNIQUE REFERENCES issue_submissions(submission_id),
            acquisition_inputs BLOB NOT NULL,
            acquisition_policy BLOB NOT NULL,
            acquisition_limits BLOB NOT NULL,
            state TEXT NOT NULL CHECK (state IN ('capturing', 'complete', 'refused')),
            manifest_json TEXT,
            manifest_sha256 TEXT CHECK (manifest_sha256 IS NULL OR length(manifest_sha256) = 64),
            created_at TEXT NOT NULL,
            completed_at TEXT,
            findings_json TEXT CHECK (findings_json IS NULL OR json_valid(findings_json)),
            CHECK ((state = 'capturing' AND manifest_json IS NULL AND manifest_sha256 IS NULL AND completed_at IS NULL)
                OR (state = 'complete' AND manifest_json IS NOT NULL AND manifest_sha256 IS NOT NULL AND completed_at IS NOT NULL)
                OR (state = 'refused' AND manifest_json IS NULL AND manifest_sha256 IS NULL AND completed_at IS NULL)),
            CHECK ((state = 'refused') = (findings_json IS NOT NULL))
        ) STRICT;
        CREATE TABLE request_bundle_references (
            bundle_id TEXT NOT NULL REFERENCES request_bundles(bundle_id),
            reference_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
            capture_kind TEXT NOT NULL CHECK (capture_kind IN ('source', 'git_blob')),
            selector BLOB NOT NULL,
            source_id TEXT REFERENCES entitled_sources(source_id),
            content_sha256 TEXT CHECK (content_sha256 IS NULL OR length(content_sha256) = 64),
            git_repository_input TEXT,
            git_revision_input TEXT,
            git_path TEXT,
            git_repository TEXT,
            git_commit_oid TEXT,
            git_blob_oid TEXT,
            PRIMARY KEY (bundle_id, reference_id),
            UNIQUE (bundle_id, ordinal),
            CHECK ((capture_kind = 'source' AND git_repository_input IS NULL AND git_revision_input IS NULL
                    AND git_path IS NULL AND git_repository IS NULL AND git_commit_oid IS NULL AND git_blob_oid IS NULL
                    AND ((source_id IS NULL AND content_sha256 IS NULL) OR (source_id IS NOT NULL AND content_sha256 IS NOT NULL)))
                OR (capture_kind = 'git_blob' AND source_id IS NULL AND git_repository_input IS NOT NULL
                    AND git_revision_input IS NOT NULL AND git_path IS NOT NULL
                    AND ((git_repository IS NULL AND git_commit_oid IS NULL AND git_blob_oid IS NULL AND content_sha256 IS NULL)
                        OR (git_repository IS NOT NULL AND git_commit_oid IS NOT NULL AND git_blob_oid IS NOT NULL AND content_sha256 IS NOT NULL))))
        ) STRICT;
        CREATE TRIGGER request_bundle_binding BEFORE INSERT ON request_bundles
        WHEN NOT EXISTS (
            SELECT 1 FROM issue_submissions
            WHERE submission_id = NEW.submission_id AND contract_revision_id IS NULL
              AND state IN ('accepted', 'capturing')
        )
        BEGIN SELECT RAISE(ABORT, 'RequestBundle requires an unprepared Issue submission'); END;
        CREATE TRIGGER request_bundle_update BEFORE UPDATE ON request_bundles
        WHEN OLD.bundle_id <> NEW.bundle_id OR OLD.submission_id <> NEW.submission_id
          OR OLD.acquisition_inputs <> NEW.acquisition_inputs
          OR OLD.acquisition_policy <> NEW.acquisition_policy
          OR OLD.acquisition_limits <> NEW.acquisition_limits
          OR OLD.created_at <> NEW.created_at OR OLD.state <> 'capturing'
          OR (NEW.state = 'complete' AND EXISTS (SELECT 1 FROM request_bundle_references AS r
              WHERE (r.bundle_id = OLD.bundle_id OR r.bundle_id = NEW.bundle_id)
                AND (r.content_sha256 IS NULL OR (r.capture_kind = 'source' AND r.source_id IS NULL)
                    OR (r.capture_kind = 'git_blob' AND (r.git_repository IS NULL OR r.git_commit_oid IS NULL OR r.git_blob_oid IS NULL)))))
        BEGIN SELECT RAISE(ABORT, 'RequestBundle identity and completed manifest are immutable'); END;
        CREATE TRIGGER request_bundles_no_delete BEFORE DELETE ON request_bundles
        BEGIN SELECT RAISE(ABORT, 'RequestBundle history is immutable'); END;
        CREATE TRIGGER request_bundle_reference_insert BEFORE INSERT ON request_bundle_references
        WHEN NOT EXISTS (SELECT 1 FROM request_bundles WHERE bundle_id = NEW.bundle_id AND state = 'capturing')
        BEGIN SELECT RAISE(ABORT, 'RequestBundle membership is sealed after completion'); END;
        CREATE TRIGGER request_bundle_reference_update BEFORE UPDATE ON request_bundle_references
        WHEN OLD.bundle_id IS NOT NEW.bundle_id OR OLD.reference_id IS NOT NEW.reference_id
          OR OLD.ordinal IS NOT NEW.ordinal OR OLD.capture_kind IS NOT NEW.capture_kind
          OR OLD.selector IS NOT NEW.selector OR OLD.git_repository_input IS NOT NEW.git_repository_input
          OR OLD.git_revision_input IS NOT NEW.git_revision_input OR OLD.git_path IS NOT NEW.git_path
          OR NOT EXISTS (SELECT 1 FROM request_bundles WHERE bundle_id = OLD.bundle_id AND state = 'capturing')
          OR OLD.content_sha256 IS NOT NULL OR NEW.content_sha256 IS NULL
          OR (OLD.capture_kind = 'source' AND (NEW.source_id IS NULL OR NEW.git_repository IS NOT OLD.git_repository
              OR NEW.git_commit_oid IS NOT OLD.git_commit_oid OR NEW.git_blob_oid IS NOT OLD.git_blob_oid
              OR NOT EXISTS (SELECT 1 FROM entitled_sources AS s JOIN issue_submissions AS i
                  ON i.work_unit_id = s.work_unit_id
                  WHERE i.submission_id = (SELECT submission_id FROM request_bundles WHERE bundle_id = NEW.bundle_id)
                    AND s.source_id = NEW.source_id AND s.content_sha256 = NEW.content_sha256)))
          OR (OLD.capture_kind = 'git_blob' AND (NEW.source_id IS NOT OLD.source_id
              OR NEW.git_repository IS NULL OR NEW.git_commit_oid IS NULL OR NEW.git_blob_oid IS NULL))
        BEGIN SELECT RAISE(ABORT, 'RequestBundle reference identity and first capture are immutable'); END;
        CREATE TRIGGER request_bundle_references_no_delete BEFORE DELETE ON request_bundle_references
        BEGIN SELECT RAISE(ABORT, 'RequestBundle membership is immutable'); END;
        """;

    internal const string RepositoryPreparationSql = """
        CREATE TABLE request_bundle_repositories (
            bundle_id TEXT PRIMARY KEY REFERENCES request_bundles(bundle_id),
            repository TEXT NOT NULL,
            default_branch TEXT NOT NULL,
            starting_revision TEXT NOT NULL,
            starting_commit_oid TEXT NOT NULL CHECK (length(starting_commit_oid) = 40
                AND starting_commit_oid NOT GLOB '*[^0-9a-f]*'),
            prepared_at TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER request_bundle_repository_insert BEFORE INSERT ON request_bundle_repositories
        WHEN NOT EXISTS (SELECT 1 FROM request_bundles WHERE bundle_id = NEW.bundle_id AND state = 'capturing')
        BEGIN SELECT RAISE(ABORT, 'Repository preparation requires an open RequestBundle capture'); END;
        CREATE TRIGGER request_bundle_repository_update BEFORE UPDATE ON request_bundle_repositories
        WHEN OLD.bundle_id <> NEW.bundle_id OR OLD.repository <> NEW.repository
          OR OLD.default_branch <> NEW.default_branch OR OLD.starting_revision <> NEW.starting_revision
          OR OLD.starting_commit_oid <> NEW.starting_commit_oid OR OLD.prepared_at <> NEW.prepared_at
        BEGIN SELECT RAISE(ABORT, 'Repository preparation is immutable'); END;
        CREATE TRIGGER request_bundle_repository_no_delete BEFORE DELETE ON request_bundle_repositories
        BEGIN SELECT RAISE(ABORT, 'Repository preparation history is immutable'); END;
        """;

    // Result and disposition are one row: no intermediate successful custody can commit.
    internal const string CompletionSql = """
        CREATE TABLE attempt_completions (
            attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
            work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
            contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
            run_id TEXT NOT NULL UNIQUE,
            receipt_json TEXT NOT NULL CHECK (json_valid(receipt_json)),
            completed_at TEXT NOT NULL
        ) STRICT;
        CREATE INDEX completions_by_work ON attempt_completions(work_unit_id);
        CREATE TRIGGER completion_bound BEFORE INSERT ON attempt_completions
        WHEN NOT EXISTS (
            SELECT 1 FROM attempts AS a
            JOIN work_units AS w USING (work_unit_id)
            JOIN contract_revisions AS c USING (contract_revision_id)
            JOIN admission_decisions AS d USING (contract_revision_id)
            JOIN native_submissions AS s USING (attempt_id)
            WHERE a.attempt_id = NEW.attempt_id AND a.is_current = 1
              AND a.work_unit_id = NEW.work_unit_id AND c.work_unit_id = a.work_unit_id
              AND a.contract_revision_id = NEW.contract_revision_id AND d.outcome = 'admitted'
              AND s.state = 'correlated' AND s.run_id = NEW.run_id
              AND json_type(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 'array'
              AND json_array_length(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 1
              AND json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].kind') = 'pull_request'
              AND w.host = 'github.com'
              AND s.format = 'http.v1' AND s.run_id = s.intended_run_id
              AND s.submission_key = 'broodling:http:v1:' || a.attempt_id
              AND json_extract(s.request_json, '$.runId') = s.intended_run_id
              AND json_extract(s.request_json, '$.submission.submissionKey') = s.submission_key
              AND json_extract(s.request_json, '$.submission.source.branch')
                  = json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].targetBranch')
              AND json_extract(s.request_json, '$.submission.source.repository') = w.owner || '/' || w.repository
              AND json_extract(s.request_json, '$.submission.source.revision') = a.b1_commit_oid
              AND json_type(NEW.receipt_json) = 'object'
              AND (SELECT count(*) FROM json_each(NEW.receipt_json)) = 7
              AND (SELECT count(*) FROM json_each(NEW.receipt_json) WHERE type = 'text'
                  AND key IN ('version', 'mode', 'outcome', 'repository', 'targetBranch', 'headRevision', 'pullRequestId')) = 7
              AND json_extract(NEW.receipt_json, '$.version') = 'v1'
              AND json_extract(NEW.receipt_json, '$.mode') = 'pr'
              AND json_extract(NEW.receipt_json, '$.outcome') = 'opened'
              AND json_extract(NEW.receipt_json, '$.repository') = w.owner || '/' || w.repository
              AND json_extract(NEW.receipt_json, '$.targetBranch')
                  = json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].targetBranch')
              AND length(json_extract(NEW.receipt_json, '$.headRevision')) = 40
              AND json_extract(NEW.receipt_json, '$.headRevision') NOT GLOB '*[^0-9a-f]*'
              AND instr(json_extract(NEW.receipt_json, '$.headRevision'), char(0)) = 0
              AND json_extract(NEW.receipt_json, '$.headRevision') <> a.b1_commit_oid
              AND length(json_extract(NEW.receipt_json, '$.pullRequestId')) > 0
              AND json_extract(NEW.receipt_json, '$.pullRequestId') NOT GLOB '*[^0-9]*'
              AND instr(json_extract(NEW.receipt_json, '$.pullRequestId'), char(0)) = 0
              AND NOT EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = a.attempt_id)
        )
        BEGIN SELECT RAISE(ABORT, 'completion requires current correlated PR authority and exact receipt'); END;
        CREATE TRIGGER completion_no_replace BEFORE INSERT ON attempt_completions
        WHEN EXISTS (SELECT 1 FROM attempt_completions WHERE attempt_id = NEW.attempt_id OR run_id = NEW.run_id)
        BEGIN SELECT RAISE(ABORT, 'completion cannot be replaced'); END;
        CREATE TRIGGER completion_no_update BEFORE UPDATE ON attempt_completions
        BEGIN SELECT RAISE(ABORT, 'completion is immutable'); END;
        CREATE TRIGGER completion_no_delete BEFORE DELETE ON attempt_completions
        BEGIN SELECT RAISE(ABORT, 'completion is durable'); END;
        CREATE TABLE completion_refusals (
            attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
            reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
            refused_at TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER completion_refusal_bound BEFORE INSERT ON completion_refusals
        WHEN NOT EXISTS (
            SELECT 1 FROM attempts JOIN native_submissions USING (attempt_id)
            WHERE attempt_id = NEW.attempt_id AND is_current = 1 AND format = 'http.v1' AND state = 'correlated'
        )
        BEGIN SELECT RAISE(ABORT, 'completion refusal requires a current correlated HTTP Attempt'); END;
        CREATE TRIGGER completion_refusal_no_update BEFORE UPDATE ON completion_refusals
        BEGIN SELECT RAISE(ABORT, 'completion refusal is immutable'); END;
        CREATE TRIGGER completion_refusal_no_delete BEFORE DELETE ON completion_refusals
        BEGIN SELECT RAISE(ABORT, 'completion refusal is durable'); END;
        CREATE TRIGGER completion_ends_authority AFTER INSERT ON attempt_completions
        BEGIN UPDATE attempts SET is_current = 0 WHERE attempt_id = NEW.attempt_id; END;
        CREATE TRIGGER attempts_currentness_justified BEFORE UPDATE OF is_current ON attempts
        WHEN OLD.is_current = 1 AND NEW.is_current = 0
          AND NOT EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = OLD.attempt_id)
          AND NOT EXISTS (SELECT 1 FROM attempt_completions WHERE attempt_id = OLD.attempt_id)
        BEGIN SELECT RAISE(ABORT, 'only abandonment or completion removes current authority'); END;
        CREATE TRIGGER abandonment_no_completed BEFORE INSERT ON attempt_abandonments
        WHEN EXISTS (SELECT 1 FROM attempt_completions WHERE attempt_id = NEW.attempt_id)
        BEGIN SELECT RAISE(ABORT, 'completed Attempt cannot be abandoned'); END;
        CREATE TRIGGER attempts_no_completed_work BEFORE INSERT ON attempts
        WHEN EXISTS (SELECT 1 FROM attempt_completions WHERE work_unit_id = NEW.work_unit_id
            AND contract_revision_id NOT IN (SELECT prior_contract_revision_id FROM revision_prior_contracts
                WHERE contract_revision_id = NEW.contract_revision_id))
        BEGIN SELECT RAISE(ABORT, 'completed Work Unit cannot acquire new Attempt authority'); END;
        CREATE TRIGGER attempts_no_replace BEFORE INSERT ON attempts
        WHEN EXISTS (
            SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id
              OR enclosure = NEW.enclosure OR worktree_path = NEW.worktree_path
              OR (b1_repository = NEW.b1_repository AND branch = NEW.branch)
              OR (work_unit_id = NEW.work_unit_id AND is_current = 1 AND NEW.is_current = 1)
        )
        BEGIN SELECT RAISE(ABORT, 'Attempt identity and allocation cannot be replaced'); END;
        """;

    // `stopped_target` is the only dispatched basis: a non-current HTTP Attempt (abandoned or
    // completed) retired while paused, with the host's stopped-target check recorded.
    internal const string RetirementSql = """
        CREATE TABLE attempt_retirements (
            attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
            basis TEXT NOT NULL CHECK (basis IN ('never_materialized', 'never_dispatched', 'no_dispatch_intent', 'stopped_target')),
            ceased_at TEXT NOT NULL,
            retired_at TEXT,
            stopped_target_json TEXT CHECK (json_valid(stopped_target_json) AND json_type(stopped_target_json) = 'object'),
            CHECK ((basis = 'stopped_target') = (stopped_target_json IS NOT NULL))
        ) STRICT;
        CREATE TRIGGER retirement_requires_safe_history BEFORE INSERT ON attempt_retirements
        WHEN NEW.retired_at IS NOT NULL
          OR EXISTS (SELECT 1 FROM attempt_retirements WHERE attempt_id = NEW.attempt_id)
          OR (NEW.basis <> 'stopped_target' AND (
            NOT EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = NEW.attempt_id)
            OR EXISTS (SELECT 1 FROM native_submissions WHERE attempt_id = NEW.attempt_id AND state <> 'prepared')))
          OR (NEW.basis = 'stopped_target' AND NOT EXISTS (
            SELECT 1 FROM attempts AS a JOIN native_submissions AS s USING (attempt_id)
            WHERE a.attempt_id = NEW.attempt_id AND a.is_current = 0 AND s.state <> 'prepared'
              AND (SELECT admission_dispatch_paused FROM installation_control WHERE singleton = 1) = 1))
          OR (NEW.basis = 'never_materialized' AND EXISTS (SELECT 1 FROM worktree_provisions WHERE attempt_id = NEW.attempt_id))
          OR (NEW.basis = 'never_dispatched' AND NOT EXISTS (SELECT 1 FROM worktree_provisions WHERE attempt_id = NEW.attempt_id))
          OR NOT EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id
            AND (resource_kind = 'http') = (NEW.basis IN ('no_dispatch_intent', 'stopped_target')))
        BEGIN SELECT RAISE(ABORT, 'retirement requires abandoned never-dispatched history or verified maintenance'); END;
        CREATE TRIGGER retirement_stable BEFORE UPDATE ON attempt_retirements
        WHEN OLD.attempt_id <> NEW.attempt_id OR OLD.basis <> NEW.basis OR OLD.ceased_at <> NEW.ceased_at
          OR OLD.stopped_target_json IS NOT NEW.stopped_target_json
          OR OLD.retired_at IS NOT NULL OR NEW.retired_at IS NULL
        BEGIN SELECT RAISE(ABORT, 'cessation proof and retirement acknowledgment are immutable'); END;
        CREATE TRIGGER retirement_no_delete BEFORE DELETE ON attempt_retirements
        BEGIN SELECT RAISE(ABORT, 'retirement history is immutable'); END;

        CREATE TABLE attempt_retries (
            retry_key TEXT PRIMARY KEY CHECK (length(trim(retry_key)) > 0),
            predecessor_id TEXT NOT NULL UNIQUE REFERENCES attempt_retirements(attempt_id),
            attempt_id TEXT NOT NULL UNIQUE REFERENCES attempts(attempt_id) DEFERRABLE INITIALLY DEFERRED,
            workspace_root TEXT,
            target_json TEXT CHECK (json_valid(target_json) AND json_type(target_json) = 'object'),
            requested_at TEXT NOT NULL,
            CHECK (predecessor_id <> attempt_id),
            CHECK ((workspace_root IS NULL) = (target_json IS NULL))
        ) STRICT;
        CREATE TRIGGER retry_requires_retirement BEFORE INSERT ON attempt_retries
        WHEN EXISTS (SELECT 1 FROM attempt_retries WHERE retry_key = NEW.retry_key
            OR predecessor_id = NEW.predecessor_id OR attempt_id = NEW.attempt_id)
          OR NOT EXISTS (
            SELECT 1 FROM attempt_retirements AS r JOIN attempt_abandonments USING (attempt_id)
            JOIN attempts AS a USING (attempt_id)
            WHERE r.attempt_id = NEW.predecessor_id AND r.retired_at IS NOT NULL AND a.is_current = 0
              AND NOT EXISTS (SELECT 1 FROM attempts AS current WHERE current.work_unit_id = a.work_unit_id AND current.is_current = 1)
              AND (r.basis = 'stopped_target'
                OR NOT EXISTS (SELECT 1 FROM native_submissions AS s WHERE s.attempt_id = a.attempt_id AND s.state <> 'prepared'))
        ) OR EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id)
          OR EXISTS (SELECT 1 FROM attempts JOIN superseded_contracts USING (contract_revision_id)
            WHERE attempt_id = NEW.predecessor_id)
        BEGIN SELECT RAISE(ABORT, 'retry requires safely retired predecessor and a new successor'); END;
        CREATE TRIGGER retries_no_update BEFORE UPDATE ON attempt_retries
        BEGIN SELECT RAISE(ABORT, 'retry identity and parameters are immutable'); END;
        CREATE TRIGGER retries_no_delete BEFORE DELETE ON attempt_retries
        BEGIN SELECT RAISE(ABORT, 'retry lineage is immutable'); END;

        DROP TRIGGER attempts_no_abandoned_work;
        CREATE TRIGGER attempts_no_abandoned_work BEFORE INSERT ON attempts
        WHEN EXISTS (SELECT 1 FROM attempts AS a WHERE a.work_unit_id = NEW.work_unit_id AND a.is_current = 0
            AND NOT (a.contract_revision_id IN (SELECT prior_contract_revision_id FROM revision_prior_contracts
                    WHERE contract_revision_id = NEW.contract_revision_id)
                AND (NOT EXISTS (SELECT 1 FROM native_submissions WHERE attempt_id = a.attempt_id AND state <> 'prepared')
                    OR EXISTS (SELECT 1 FROM attempt_retirements WHERE attempt_id = a.attempt_id AND retired_at IS NOT NULL))))
          AND NOT EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id)
        BEGIN SELECT RAISE(ABORT, 'ended Work Unit requires explicit safe replacement'); END;
        CREATE TRIGGER retry_preserves_bindings BEFORE INSERT ON attempts
        WHEN EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id)
          AND NOT EXISTS (
            SELECT 1 FROM attempt_retries AS r JOIN attempts AS p ON p.attempt_id = r.predecessor_id
            WHERE r.attempt_id = NEW.attempt_id AND NEW.work_unit_id = p.work_unit_id
              AND NEW.contract_revision_id = p.contract_revision_id AND NEW.b1_repository = p.b1_repository
              AND NEW.b1_commit_oid = p.b1_commit_oid AND NEW.b1_material_sha256 = p.b1_material_sha256
              AND NEW.b1_requested_revision = p.b1_requested_revision AND NEW.resource_kind = p.resource_kind
              AND ((NEW.resource_kind = 'http' AND r.workspace_root IS NULL)
                OR (NEW.resource_kind = 'worktree' AND NEW.workspace_root = r.workspace_root
                  AND NEW.enclosure = r.workspace_root || '/' || NEW.attempt_id
                  AND NEW.worktree_path = NEW.enclosure || '/worktree' AND NEW.branch = 'broodling/' || NEW.attempt_id))
        )
        BEGIN SELECT RAISE(ABORT, 'replacement must preserve original B1 and chosen allocation'); END;
        CREATE TRIGGER retry_submission_target BEFORE INSERT ON native_submissions
        WHEN EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id AND target_json IS NOT NULL
            AND json(target_json) IS NOT json_extract(NEW.request_json, '$.target'))
        BEGIN SELECT RAISE(ABORT, 'replacement must preserve its chosen target'); END;
        """;

    internal const string InstallationSql = """
        CREATE TABLE installation_control (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            admission_dispatch_paused INTEGER NOT NULL CHECK (admission_dispatch_paused IN (0, 1)),
            changed_at TEXT NOT NULL
        ) STRICT;
        """;

    // One row per Attempt is the single source of dispatch-intent truth for every format:
    // `state <> 'prepared'` means intent was committed. `bridge` rows belong to provisioned
    // worktree Attempts; `http.v1` rows to HTTP Attempts, with a separate intended identity,
    // confirmed `run_id` and monotonic replay block instead of an absorbing `blocked` state.
    internal const string DispatchSql = """
        CREATE TABLE execution_assets (
            asset_sha256 TEXT PRIMARY KEY CHECK (length(asset_sha256) = 64 AND asset_sha256 NOT GLOB '*[^0-9a-f]*'),
            content BLOB NOT NULL
        ) STRICT;
        CREATE TRIGGER execution_assets_no_update BEFORE UPDATE ON execution_assets
        BEGIN SELECT RAISE(ABORT, 'execution asset content is immutable'); END;
        CREATE TRIGGER execution_assets_no_delete BEFORE DELETE ON execution_assets
        BEGIN SELECT RAISE(ABORT, 'execution asset content is durable'); END;

        CREATE TABLE native_submissions (
            attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
            format TEXT NOT NULL CHECK (format IN ('bridge', 'http.v1')),
            submission_key TEXT NOT NULL UNIQUE,
            request_json TEXT NOT NULL CHECK (json_valid(request_json)),
            state TEXT NOT NULL CHECK (state IN ('prepared', 'dispatched', 'correlated', 'blocked')),
            run_id TEXT UNIQUE,
            intended_run_id TEXT UNIQUE CHECK (intended_run_id GLOB
                '[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]-[0-9a-f][0-9a-f][0-9a-f][0-9a-f]-7[0-9a-f][0-9a-f][0-9a-f]-[89ab][0-9a-f][0-9a-f][0-9a-f]-[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]'),
            replay_blocked_reason TEXT CHECK (replay_blocked_reason = 'submission_conflict'),
            asset_sha256 TEXT REFERENCES execution_assets(asset_sha256),
            binding_json TEXT CHECK (json_valid(binding_json) AND json_type(binding_json) = 'object'),
            CHECK ((format = 'bridge' AND intended_run_id IS NULL AND replay_blocked_reason IS NULL
                    AND asset_sha256 IS NULL AND binding_json IS NULL
                    AND ((state = 'correlated' AND run_id IS NOT NULL AND length(trim(run_id)) > 0)
                        OR (state <> 'correlated' AND run_id IS NULL)))
                OR (format = 'http.v1' AND state <> 'blocked' AND intended_run_id IS NOT NULL
                    AND asset_sha256 IS NOT NULL AND binding_json IS NOT NULL
                    AND (replay_blocked_reason IS NULL OR state <> 'prepared')
                    AND ((state = 'correlated' AND run_id IS intended_run_id)
                        OR (state <> 'correlated' AND run_id IS NULL))))
        ) STRICT;
        CREATE TRIGGER submission_requires_current BEFORE INSERT ON native_submissions
        WHEN NEW.state <> 'prepared' OR NOT EXISTS (
            SELECT 1 FROM attempts AS a
            WHERE a.attempt_id = NEW.attempt_id AND a.is_current = 1
              AND ((NEW.format = 'bridge' AND a.resource_kind = 'worktree'
                    AND EXISTS (SELECT 1 FROM worktree_provisions WHERE attempt_id = NEW.attempt_id))
                OR (NEW.format = 'http.v1' AND a.resource_kind = 'http'
                    AND NEW.submission_key = 'broodling:http:v1:' || a.attempt_id
                    AND (SELECT count(*) FROM json_each(NEW.request_json)) = 2
                    AND json_extract(NEW.request_json, '$.runId') = NEW.intended_run_id
                    AND json_extract(NEW.request_json, '$.submission.submissionKey') = NEW.submission_key
                    AND json_extract(NEW.request_json, '$.submission.source.revision') = a.b1_commit_oid
                    AND json_extract(NEW.binding_json, '$.repository') = a.b1_repository))
        )
        BEGIN SELECT RAISE(ABORT, 'preparation requires current authority for its resource kind'); END;
        CREATE TRIGGER submission_intended_exclusive BEFORE INSERT ON native_submissions
        WHEN EXISTS (SELECT 1 FROM native_submissions WHERE run_id = NEW.intended_run_id)
        BEGIN SELECT RAISE(ABORT, 'intended run identity names another Attempt''s execution'); END;
        CREATE TRIGGER submission_binding_stable BEFORE UPDATE ON native_submissions
        WHEN OLD.attempt_id <> NEW.attempt_id OR OLD.format <> NEW.format
          OR OLD.submission_key <> NEW.submission_key OR OLD.request_json <> NEW.request_json
          OR OLD.intended_run_id IS NOT NEW.intended_run_id OR OLD.asset_sha256 IS NOT NEW.asset_sha256
          OR OLD.binding_json IS NOT NEW.binding_json
          OR (OLD.replay_blocked_reason IS NOT NULL AND OLD.replay_blocked_reason IS NOT NEW.replay_blocked_reason)
          OR NOT ((OLD.state = 'prepared' AND NEW.state = 'dispatched')
            OR (OLD.state = 'dispatched' AND NEW.state = 'correlated')
            OR (OLD.format = 'bridge' AND OLD.state = 'dispatched' AND NEW.state = 'blocked')
            OR (OLD.format = 'http.v1' AND OLD.state = NEW.state AND OLD.run_id IS NEW.run_id
                AND OLD.replay_blocked_reason IS NULL AND NEW.replay_blocked_reason IS NOT NULL))
        BEGIN SELECT RAISE(ABORT, 'frozen dispatch and correlation are irreversible'); END;
        CREATE TRIGGER correlation_exclusive BEFORE UPDATE OF run_id ON native_submissions
        WHEN NEW.run_id IS NOT NULL AND EXISTS (
            SELECT 1 FROM native_submissions WHERE intended_run_id = NEW.run_id AND attempt_id <> NEW.attempt_id)
        BEGIN SELECT RAISE(ABORT, 'confirmed run identity is another Attempt''s intended execution'); END;
        CREATE TRIGGER dispatch_requires_current BEFORE UPDATE ON native_submissions
        WHEN NEW.state = 'dispatched' AND OLD.state <> 'dispatched' AND NOT EXISTS (
            SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1)
        BEGIN SELECT RAISE(ABORT, 'dispatch requires current authority'); END;
        CREATE TRIGGER submissions_retained BEFORE DELETE ON native_submissions
        BEGIN SELECT RAISE(ABORT, 'native dispatch history is immutable'); END;
        """;

    internal const string ProvisioningSql = """
        CREATE TABLE worktree_provisions (
            attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
            provisioned_at TEXT NOT NULL
        ) STRICT;
        CREATE TRIGGER provision_requires_current BEFORE INSERT ON worktree_provisions
        WHEN NOT EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1
            AND resource_kind = 'worktree')
        BEGIN SELECT RAISE(ABORT, 'provisioning requires current Attempt authority'); END;
        CREATE TRIGGER provisions_no_update BEFORE UPDATE ON worktree_provisions
        BEGIN SELECT RAISE(ABORT, 'first provisioning acknowledgment is immutable'); END;
        CREATE TRIGGER provisions_no_delete BEFORE DELETE ON worktree_provisions
        BEGIN SELECT RAISE(ABORT, 'provisioning history is immutable'); END;
        """;

    internal const string IdentitySql = """
        CREATE TABLE store_metadata (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            format TEXT NOT NULL,
            version INTEGER NOT NULL,
            definition_hash TEXT NOT NULL,
            manifest_hash TEXT NOT NULL,
            initialized_at TEXT NOT NULL
        ) STRICT;

        CREATE TABLE work_units (
            work_unit_id TEXT PRIMARY KEY,
            reference_key TEXT NOT NULL UNIQUE,
            host TEXT NOT NULL,
            owner TEXT NOT NULL,
            repository TEXT NOT NULL,
            issue_number INTEGER NOT NULL CHECK (issue_number > 0),
            issue_locator TEXT NOT NULL,
            repository_identity TEXT,
            issue_identity TEXT,
            first_seen_at TEXT NOT NULL,
            UNIQUE (host, owner, repository, issue_number)
        ) STRICT;

        CREATE TABLE work_submissions (
            submission_id TEXT PRIMARY KEY,
            work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
            submitted_repository TEXT NOT NULL,
            submitted_issue TEXT NOT NULL,
            received_at TEXT NOT NULL
        ) STRICT;
        CREATE INDEX submissions_by_work ON work_submissions(work_unit_id);

        CREATE TABLE entitled_sources (
            source_id TEXT PRIMARY KEY,
            work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
            kind TEXT NOT NULL CHECK (kind IN ('primary_issue', 'referenced_document', 'repository_file', 'caller_statement', 'executable_request')),
            locator TEXT NOT NULL,
            content BLOB NOT NULL,
            content_sha256 TEXT NOT NULL CHECK (length(content_sha256) = 64),
            media_type TEXT NOT NULL,
            origin TEXT NOT NULL CHECK (origin IN ('caller', 'broodling_policy')),
            entitled_by TEXT NOT NULL CHECK (entitled_by IN ('caller', 'broodling_policy')),
            entitlement_basis TEXT NOT NULL,
            retrieved_at TEXT NOT NULL,
            recorded_at TEXT NOT NULL,
            UNIQUE (work_unit_id, kind, locator, content_sha256)
        ) STRICT;
        CREATE INDEX sources_by_work ON entitled_sources(work_unit_id);

        CREATE TRIGGER work_identity_stable BEFORE UPDATE ON work_units
        WHEN OLD.work_unit_id <> NEW.work_unit_id
          OR OLD.reference_key <> NEW.reference_key
          OR OLD.host <> NEW.host
          OR OLD.owner <> NEW.owner
          OR OLD.repository <> NEW.repository
          OR OLD.issue_number <> NEW.issue_number
          OR OLD.issue_locator <> NEW.issue_locator
          OR OLD.first_seen_at <> NEW.first_seen_at
          OR (OLD.repository_identity IS NOT NULL AND OLD.repository_identity IS NOT NEW.repository_identity)
          OR (OLD.issue_identity IS NOT NULL AND OLD.issue_identity IS NOT NEW.issue_identity)
        BEGIN SELECT RAISE(ABORT, 'work identity is immutable; unset upstream identities may be pinned once'); END;
        CREATE TRIGGER work_no_delete BEFORE DELETE ON work_units
        BEGIN SELECT RAISE(ABORT, 'work identity is immutable'); END;
        CREATE TRIGGER submissions_no_update BEFORE UPDATE ON work_submissions
        BEGIN SELECT RAISE(ABORT, 'submissions are immutable'); END;
        CREATE TRIGGER submissions_no_delete BEFORE DELETE ON work_submissions
        BEGIN SELECT RAISE(ABORT, 'submissions are immutable'); END;
        CREATE TRIGGER sources_no_update BEFORE UPDATE ON entitled_sources
        BEGIN SELECT RAISE(ABORT, 'source snapshots are immutable'); END;
        CREATE TRIGGER sources_no_delete BEFORE DELETE ON entitled_sources
        BEGIN SELECT RAISE(ABORT, 'source snapshots are immutable'); END;
        """;

    internal const string AdmissionSql = """
        CREATE TABLE contract_revisions (
            contract_revision_id TEXT PRIMARY KEY,
            work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
            revision_number INTEGER NOT NULL CHECK (revision_number > 0),
            contract_sha256 TEXT NOT NULL CHECK (length(contract_sha256) = 64),
            canonical_bytes BLOB NOT NULL,
            constructed_by TEXT NOT NULL CHECK (constructed_by IN ('caller', 'broodling_policy', 'model_extraction')),
            supersedes_revision_id TEXT REFERENCES contract_revisions(contract_revision_id),
            recorded_at TEXT NOT NULL,
            UNIQUE (work_unit_id, revision_number),
            UNIQUE (work_unit_id, contract_sha256)
        ) STRICT;

        CREATE TABLE contract_sources (
            contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
            source_id TEXT NOT NULL REFERENCES entitled_sources(source_id),
            content_sha256 TEXT NOT NULL,
            PRIMARY KEY (contract_revision_id, source_id)
        ) STRICT;

        CREATE TABLE admission_decisions (
            decision_id TEXT PRIMARY KEY,
            contract_revision_id TEXT NOT NULL UNIQUE REFERENCES contract_revisions(contract_revision_id),
            outcome TEXT NOT NULL CHECK (outcome IN ('admitted', 'rejected')),
            findings_json TEXT NOT NULL,
            policy_version TEXT NOT NULL,
            decided_at TEXT NOT NULL
        ) STRICT;

        CREATE TRIGGER contract_sources_match BEFORE INSERT ON contract_sources
        WHEN NOT EXISTS (
            SELECT 1 FROM contract_revisions AS revision
            JOIN entitled_sources AS source ON source.work_unit_id = revision.work_unit_id
            JOIN json_each(CAST(revision.canonical_bytes AS TEXT), '$.sourceAttribution') AS pin
            WHERE revision.contract_revision_id = NEW.contract_revision_id
              AND source.source_id = NEW.source_id AND source.content_sha256 = NEW.content_sha256
              AND json_extract(pin.value, '$.sourceId') = NEW.source_id
              AND json_extract(pin.value, '$.contentSha256') = NEW.content_sha256
        )
        BEGIN SELECT RAISE(ABORT, 'Contract attribution must pin an entitled source of its Work Unit'); END;
        CREATE TRIGGER revisions_no_update BEFORE UPDATE ON contract_revisions
        BEGIN SELECT RAISE(ABORT, 'Contract revisions are immutable'); END;
        CREATE TRIGGER revisions_no_delete BEFORE DELETE ON contract_revisions
        BEGIN SELECT RAISE(ABORT, 'Contract revisions are immutable'); END;
        CREATE TRIGGER contract_sources_no_update BEFORE UPDATE ON contract_sources
        BEGIN SELECT RAISE(ABORT, 'Contract attribution is immutable'); END;
        CREATE TRIGGER contract_sources_no_delete BEFORE DELETE ON contract_sources
        BEGIN SELECT RAISE(ABORT, 'Contract attribution is immutable'); END;
        CREATE TRIGGER decisions_no_update BEFORE UPDATE ON admission_decisions
        BEGIN SELECT RAISE(ABORT, 'Admission decisions are immutable'); END;
        CREATE TRIGGER decisions_no_delete BEFORE DELETE ON admission_decisions
        BEGIN SELECT RAISE(ABORT, 'Admission decisions are immutable'); END;
        """;

    // Allocation is part of the Attempt row: neither can commit without the other.
    // An `http` Attempt owns no local enclosure, worktree or branch; its kind is stored, never inferred.
    internal const string AttemptSql = """
        CREATE TABLE attempts (
            attempt_id TEXT PRIMARY KEY,
            work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
            contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
            is_current INTEGER NOT NULL CHECK (is_current IN (0, 1)),
            b1_repository TEXT NOT NULL,
            b1_commit_oid TEXT NOT NULL CHECK (length(b1_commit_oid) = 40 AND b1_commit_oid NOT GLOB '*[^0-9a-f]*'),
            b1_material_sha256 TEXT NOT NULL CHECK (length(b1_material_sha256) = 64),
            b1_requested_revision TEXT NOT NULL,
            workspace_root TEXT,
            enclosure TEXT UNIQUE,
            worktree_path TEXT UNIQUE,
            branch TEXT,
            admitted_at TEXT NOT NULL,
            resource_kind TEXT NOT NULL,
            UNIQUE (b1_repository, branch),
            CHECK ((resource_kind = 'worktree' AND workspace_root IS NOT NULL AND enclosure IS NOT NULL
                    AND worktree_path IS NOT NULL AND branch IS NOT NULL)
                OR (resource_kind = 'http' AND workspace_root IS NULL AND enclosure IS NULL
                    AND worktree_path IS NULL AND branch IS NULL))
        ) STRICT;
        CREATE UNIQUE INDEX one_current_attempt ON attempts(work_unit_id) WHERE is_current = 1;
        CREATE INDEX attempts_by_revision ON attempts(contract_revision_id);

        CREATE TABLE attempt_abandonments (
            attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
            reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
            abandoned_at TEXT NOT NULL
        ) STRICT;

        CREATE TRIGGER attempts_require_admission BEFORE INSERT ON attempts
        WHEN NEW.is_current <> 1 OR NOT EXISTS (
            SELECT 1 FROM contract_revisions AS revision
            JOIN admission_decisions AS decision USING (contract_revision_id)
            WHERE revision.contract_revision_id = NEW.contract_revision_id
              AND revision.work_unit_id = NEW.work_unit_id AND decision.outcome = 'admitted'
        )
        BEGIN SELECT RAISE(ABORT, 'Attempt requires admitted authority for its Work Unit'); END;
        CREATE TRIGGER attempts_no_abandoned_work BEFORE INSERT ON attempts
        WHEN EXISTS (
            SELECT 1 FROM attempts JOIN attempt_abandonments USING (attempt_id)
            WHERE work_unit_id = NEW.work_unit_id
        )
        BEGIN SELECT RAISE(ABORT, 'abandoned Work Unit cannot acquire ordinary admission'); END;
        CREATE TRIGGER attempts_binding_stable BEFORE UPDATE ON attempts
        WHEN OLD.attempt_id <> NEW.attempt_id OR OLD.work_unit_id <> NEW.work_unit_id
          OR OLD.contract_revision_id <> NEW.contract_revision_id
          OR OLD.b1_repository <> NEW.b1_repository OR OLD.b1_commit_oid <> NEW.b1_commit_oid
          OR OLD.b1_material_sha256 <> NEW.b1_material_sha256
          OR OLD.b1_requested_revision <> NEW.b1_requested_revision
          OR OLD.workspace_root IS NOT NEW.workspace_root OR OLD.enclosure IS NOT NEW.enclosure
          OR OLD.worktree_path IS NOT NEW.worktree_path OR OLD.branch IS NOT NEW.branch
          OR OLD.admitted_at <> NEW.admitted_at OR OLD.resource_kind <> NEW.resource_kind
          OR OLD.is_current < NEW.is_current
        BEGIN SELECT RAISE(ABORT, 'Attempt bindings are immutable and authority cannot be restored'); END;
        CREATE TRIGGER attempts_no_delete BEFORE DELETE ON attempts
        BEGIN SELECT RAISE(ABORT, 'Attempt history is immutable'); END;
        CREATE TRIGGER abandonment_requires_current BEFORE INSERT ON attempt_abandonments
        WHEN NOT EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1)
        BEGIN SELECT RAISE(ABORT, 'only the current Attempt may be abandoned'); END;
        CREATE TRIGGER abandonment_ends_authority AFTER INSERT ON attempt_abandonments
        BEGIN UPDATE attempts SET is_current = 0 WHERE attempt_id = NEW.attempt_id; END;
        CREATE TRIGGER abandonment_no_update BEFORE UPDATE ON attempt_abandonments
        BEGIN SELECT RAISE(ABORT, 'abandonment is immutable'); END;
        CREATE TRIGGER abandonment_no_delete BEFORE DELETE ON attempt_abandonments
        BEGIN SELECT RAISE(ABORT, 'abandonment is irreversible'); END;
        """;

    internal static string ManifestHash(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*' ORDER BY type, name";
        using var reader = command.ExecuteReader();
        var definitions = new List<string>();
        while (reader.Read())
            for (var index = 0; index < 4; index++)
                definitions.Add(reader.GetString(index));
        return Digests.Parts(definitions.ToArray());
    }
}
