"""The Broodling-owned SQLite schema.

Scope discipline: this schema holds Work Unit identity, entitled source
snapshots, immutable Contract revisions and admission decisions. It is
deliberately *not* a Zeroshot RunLedger mirror — there is no table for runs,
node occurrences, provider sessions, candidate seals, effect intents/receipts or
completed-occurrence projections, and adding one is a schema change that the
schema-shape tests will fail.

Immutability is enforced in the database, not only in Python: append-only tables
carry ``BEFORE UPDATE``/``BEFORE DELETE`` triggers, so a direct SQL amendment of
an admitted Contract aborts.
"""

from __future__ import annotations

import hashlib

SCHEMA_VERSION = 1

#: Marker file that names a directory as a disposable Attempt worktree. The store
#: refuses to live under one; V1-P2 does not create worktrees.
DISPOSABLE_WORKTREE_MARKER = ".broodling-disposable-worktree"

#: Tables this schema owns. The set is asserted by the tests, so a RunLedger
#: mirror cannot be added without the boundary test failing.
TABLES: tuple[str, ...] = (
    "admission_decisions",
    "contract_revisions",
    "contract_source_attributions",
    "entitled_sources",
    "schema_meta",
    "work_unit_submissions",
    "work_units",
)

SCHEMA_SQL = """
CREATE TABLE schema_meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) STRICT;

CREATE TABLE work_units (
    work_unit_id        TEXT PRIMARY KEY,
    reference_key       TEXT NOT NULL UNIQUE,
    host                TEXT NOT NULL,
    owner               TEXT NOT NULL,
    repository          TEXT NOT NULL,
    issue_number        INTEGER NOT NULL,
    issue_locator       TEXT NOT NULL,
    repository_identity TEXT,
    issue_identity      TEXT,
    first_seen_at       TEXT NOT NULL,
    UNIQUE (host, owner, repository, issue_number),
    CHECK (issue_number > 0)
) STRICT;

CREATE TABLE work_unit_submissions (
    submission_id         TEXT PRIMARY KEY,
    work_unit_id          TEXT NOT NULL REFERENCES work_units (work_unit_id),
    submitted_repository  TEXT NOT NULL,
    submitted_issue       TEXT NOT NULL,
    received_at           TEXT NOT NULL
) STRICT;

CREATE INDEX work_unit_submissions_by_unit
    ON work_unit_submissions (work_unit_id);

CREATE TABLE entitled_sources (
    source_id         TEXT PRIMARY KEY,
    work_unit_id      TEXT NOT NULL REFERENCES work_units (work_unit_id),
    kind              TEXT NOT NULL,
    locator           TEXT NOT NULL,
    content           BLOB NOT NULL,
    content_sha256    TEXT NOT NULL,
    media_type        TEXT NOT NULL,
    entitled_by       TEXT NOT NULL,
    entitlement_basis TEXT NOT NULL,
    retrieved_at      TEXT NOT NULL,
    recorded_at       TEXT NOT NULL,
    UNIQUE (work_unit_id, kind, locator, content_sha256),
    CHECK (entitled_by IN ('caller', 'broodling_policy'))
) STRICT;

CREATE INDEX entitled_sources_by_unit ON entitled_sources (work_unit_id);

CREATE TABLE contract_revisions (
    contract_revision_id   TEXT PRIMARY KEY,
    work_unit_id           TEXT NOT NULL REFERENCES work_units (work_unit_id),
    revision_number        INTEGER NOT NULL,
    contract_sha256        TEXT NOT NULL,
    canonical_bytes        BLOB NOT NULL,
    constructed_by         TEXT NOT NULL,
    supersedes_revision_id TEXT REFERENCES contract_revisions (contract_revision_id),
    recorded_at            TEXT NOT NULL,
    UNIQUE (work_unit_id, revision_number),
    UNIQUE (work_unit_id, contract_sha256),
    CHECK (revision_number > 0)
) STRICT;

CREATE TABLE contract_source_attributions (
    contract_revision_id TEXT NOT NULL
        REFERENCES contract_revisions (contract_revision_id),
    source_id            TEXT NOT NULL REFERENCES entitled_sources (source_id),
    content_sha256       TEXT NOT NULL,
    PRIMARY KEY (contract_revision_id, source_id)
) STRICT;

CREATE TABLE admission_decisions (
    decision_id          TEXT PRIMARY KEY,
    contract_revision_id TEXT NOT NULL UNIQUE
        REFERENCES contract_revisions (contract_revision_id),
    work_unit_id         TEXT NOT NULL REFERENCES work_units (work_unit_id),
    outcome              TEXT NOT NULL,
    findings_json        TEXT NOT NULL,
    configuration_json   TEXT NOT NULL,
    decided_at           TEXT NOT NULL,
    CHECK (outcome IN ('admitted', 'rejected'))
) STRICT;

CREATE INDEX admission_decisions_by_unit ON admission_decisions (work_unit_id);

CREATE TRIGGER work_units_no_delete BEFORE DELETE ON work_units
BEGIN
    SELECT RAISE(ABORT, 'work_units is append-only');
END;

CREATE TRIGGER work_units_identity_is_stable BEFORE UPDATE ON work_units
WHEN OLD.work_unit_id <> NEW.work_unit_id
     OR OLD.reference_key <> NEW.reference_key
     OR OLD.host <> NEW.host
     OR OLD.owner <> NEW.owner
     OR OLD.repository <> NEW.repository
     OR OLD.issue_number <> NEW.issue_number
     OR OLD.issue_locator <> NEW.issue_locator
     OR OLD.first_seen_at <> NEW.first_seen_at
     OR (OLD.repository_identity IS NOT NULL
         AND (NEW.repository_identity IS NULL
              OR NEW.repository_identity <> OLD.repository_identity))
     OR (OLD.issue_identity IS NOT NULL
         AND (NEW.issue_identity IS NULL
              OR NEW.issue_identity <> OLD.issue_identity))
BEGIN
    SELECT RAISE(ABORT,
        'work unit identity is stable; only an unset upstream identity may be pinned');
END;

CREATE TRIGGER entitled_sources_no_update BEFORE UPDATE ON entitled_sources
BEGIN
    SELECT RAISE(ABORT, 'entitled source snapshots are immutable');
END;

CREATE TRIGGER entitled_sources_no_delete BEFORE DELETE ON entitled_sources
BEGIN
    SELECT RAISE(ABORT, 'entitled source snapshots are immutable');
END;

CREATE TRIGGER contract_revisions_no_update BEFORE UPDATE ON contract_revisions
BEGIN
    SELECT RAISE(ABORT,
        'a Contract revision is immutable; new meaning requires a new revision');
END;

CREATE TRIGGER contract_revisions_no_delete BEFORE DELETE ON contract_revisions
BEGIN
    SELECT RAISE(ABORT,
        'a Contract revision is immutable; new meaning requires a new revision');
END;

CREATE TRIGGER contract_source_attributions_no_update
BEFORE UPDATE ON contract_source_attributions
BEGIN
    SELECT RAISE(ABORT, 'Contract source attribution is immutable');
END;

CREATE TRIGGER contract_source_attributions_no_delete
BEFORE DELETE ON contract_source_attributions
BEGIN
    SELECT RAISE(ABORT, 'Contract source attribution is immutable');
END;

CREATE TRIGGER admission_decisions_no_update BEFORE UPDATE ON admission_decisions
BEGIN
    SELECT RAISE(ABORT, 'an admission decision is immutable');
END;

CREATE TRIGGER admission_decisions_no_delete BEFORE DELETE ON admission_decisions
BEGIN
    SELECT RAISE(ABORT, 'an admission decision is immutable');
END;
"""

#: Digest of the exact DDL this build initializes. Recorded in ``schema_meta`` so
#: a store written by a different DDL text is detected on reopen.
SCHEMA_SHA256 = hashlib.sha256(SCHEMA_SQL.encode("utf-8")).hexdigest()
