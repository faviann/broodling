using System.Text;
using Microsoft.Data.Sqlite;

namespace Broodling;

internal static class StoreSchema
{
    internal const string Format = "broodling.dotnet";
    internal const int Version = 6;
    internal static string DefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(Sql));
    internal static string VersionOneDefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(VersionOneSql));

    internal static string VersionTwoDefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(VersionTwoSql));
    internal const string VersionTwoSql = VersionOneSql + "\n" + AdmissionSql;
    internal static string VersionThreeDefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(VersionThreeSql));
    internal const string VersionThreeSql = VersionTwoSql + "\n" + AttemptSql;
    internal static string VersionFourDefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(VersionFourSql));
    internal const string VersionFourSql = VersionThreeSql + "\n" + ProvisioningSql;
    internal static string VersionFiveDefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(VersionFiveSql));
    internal const string VersionFiveSql = VersionFourSql + "\n" + DispatchSql;
    internal const string Sql = VersionFiveSql + "\n" + CompletionSql;

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
              AND s.submission_key = 'broodling:dotnet:v1:' || a.attempt_id
              AND json_extract(s.request_json, '$.submissionKey') = s.submission_key
              AND json_type(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 'array'
              AND json_array_length(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 1
              AND json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].kind') = 'pull_request'
              AND json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].targetBranch')
                  = json_extract(s.request_json, '$.source.branch')
              AND json_extract(s.request_json, '$.preset.name') = 'software-change'
              AND json_extract(s.request_json, '$.preset.delivery') = 'pull_request'
              AND w.host = 'github.com'
              AND json_extract(s.request_json, '$.source.repository') = w.owner || '/' || w.repository
              AND json_extract(s.request_json, '$.source.revision') = a.b1_commit_oid
              AND json_type(NEW.receipt_json) = 'object'
              AND (SELECT count(*) FROM json_each(NEW.receipt_json)) = 7
              AND (SELECT count(*) FROM json_each(NEW.receipt_json) WHERE type = 'text'
                  AND key IN ('version', 'mode', 'outcome', 'repository', 'targetBranch', 'headRevision', 'pullRequestId')) = 7
              AND json_extract(NEW.receipt_json, '$.version') = 'v1'
              AND json_extract(NEW.receipt_json, '$.mode') = 'pr'
              AND json_extract(NEW.receipt_json, '$.outcome') = 'opened'
              AND json_extract(NEW.receipt_json, '$.repository') = json_extract(s.request_json, '$.source.repository')
              AND json_extract(NEW.receipt_json, '$.targetBranch') = json_extract(s.request_json, '$.source.branch')
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
        WHEN EXISTS (SELECT 1 FROM attempt_completions WHERE work_unit_id = NEW.work_unit_id)
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

    internal const string DispatchSql = """
        CREATE TABLE native_submissions (
            attempt_id TEXT PRIMARY KEY REFERENCES worktree_provisions(attempt_id),
            submission_key TEXT NOT NULL UNIQUE,
            request_json TEXT NOT NULL CHECK (json_valid(request_json)),
            state TEXT NOT NULL CHECK (state IN ('prepared', 'dispatched', 'correlated', 'blocked')),
            run_id TEXT UNIQUE,
            CHECK ((state = 'correlated' AND run_id IS NOT NULL AND length(trim(run_id)) > 0)
                OR (state <> 'correlated' AND run_id IS NULL))
        ) STRICT;
        CREATE TRIGGER submission_requires_current BEFORE INSERT ON native_submissions
        WHEN NEW.state <> 'prepared' OR NOT EXISTS (
            SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1)
        BEGIN SELECT RAISE(ABORT, 'preparation requires current provisioned authority'); END;
        CREATE TRIGGER submission_binding_stable BEFORE UPDATE ON native_submissions
        WHEN OLD.attempt_id <> NEW.attempt_id OR OLD.submission_key <> NEW.submission_key
          OR OLD.request_json <> NEW.request_json
          OR NOT ((OLD.state = 'prepared' AND NEW.state = 'dispatched')
            OR (OLD.state = 'dispatched' AND NEW.state IN ('correlated', 'blocked')))
        BEGIN SELECT RAISE(ABORT, 'frozen dispatch and correlation are irreversible'); END;
        CREATE TRIGGER dispatch_requires_current BEFORE UPDATE ON native_submissions
        WHEN NEW.state = 'dispatched' AND NOT EXISTS (
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
        WHEN NOT EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1)
        BEGIN SELECT RAISE(ABORT, 'provisioning requires current Attempt authority'); END;
        CREATE TRIGGER provisions_no_update BEFORE UPDATE ON worktree_provisions
        BEGIN SELECT RAISE(ABORT, 'first provisioning acknowledgment is immutable'); END;
        CREATE TRIGGER provisions_no_delete BEFORE DELETE ON worktree_provisions
        BEGIN SELECT RAISE(ABORT, 'provisioning history is immutable'); END;
        """;

    // Separate format and version space from the Python executable reference.
    internal const string VersionOneSql = """
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
            kind TEXT NOT NULL CHECK (kind IN ('primary_issue', 'referenced_document', 'repository_file', 'caller_statement')),
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
            workspace_root TEXT NOT NULL,
            enclosure TEXT NOT NULL UNIQUE,
            worktree_path TEXT NOT NULL UNIQUE,
            branch TEXT NOT NULL,
            admitted_at TEXT NOT NULL,
            UNIQUE (b1_repository, branch)
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
          OR OLD.workspace_root <> NEW.workspace_root OR OLD.enclosure <> NEW.enclosure
          OR OLD.worktree_path <> NEW.worktree_path OR OLD.branch <> NEW.branch
          OR OLD.admitted_at <> NEW.admitted_at OR OLD.is_current < NEW.is_current
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
