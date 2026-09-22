using System.Text;
using Microsoft.Data.Sqlite;

namespace Broodling;

internal static class StoreSchema
{
    internal const string Format = "broodling.dotnet";
    internal const int Version = 2;
    internal static string DefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(Sql));
    internal static string VersionOneDefinitionHash => Digests.Bytes(Encoding.UTF8.GetBytes(VersionOneSql));

    internal const string Sql = VersionOneSql + "\n" + AdmissionSql;

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
