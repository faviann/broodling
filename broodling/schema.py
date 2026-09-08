"""The Broodling-owned SQLite schema.

Scope discipline: this schema holds Work Unit identity, entitled source
snapshots, immutable Contract revisions, admission decisions, and the immutable
Attempt/B1/worktree-ownership records that admission allocates. It is
deliberately *not* a Zeroshot RunLedger mirror — there is no table for runs, node
occurrences, provider sessions, candidate seals, effect intents/receipts or
completed-occurrence projections. One final_assurance row retains only completed
P3 custody for an Attempt; it does not decide Work Unit disposition.

Immutability is enforced in the database, not only in Python: append-only tables
carry ``BEFORE UPDATE``/``BEFORE DELETE`` triggers, so a direct SQL amendment of
an admitted Contract, or a rebinding of an Attempt to another Contract revision,
aborts. Exclusivity is enforced the same way: one current Attempt per Work Unit
is a partial unique index, and one owner per worktree path/branch is a unique
constraint, so two racing writers cannot both win.
"""

from __future__ import annotations

import hashlib

SCHEMA_VERSION = 4
V3_SCHEMA_SHA256 = "663acf981b7b5379bde50f9446d12bee922c81bcbdcc1b4e7f15f6c4e5ea007f"
V2_SCHEMA_SHA256 = "bbd7b68bdc66e6bc626f6f5d556e0efa3c9398476eb78b3f3ad75400ade29779"

#: Tables this schema owns. The set is asserted by the tests, so a RunLedger
#: mirror cannot be added without the boundary test failing.
TABLES: tuple[str, ...] = (
    "admission_decisions",
    "attempt_submissions",
    "attempts",
    "contract_revisions",
    "contract_source_attributions",
    "entitled_sources",
    "final_assurance",
    "schema_meta",
    "work_unit_submissions",
    "work_units",
    "worktree_assignments",
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

CREATE TABLE attempts (
    attempt_id            TEXT PRIMARY KEY,
    work_unit_id          TEXT NOT NULL REFERENCES work_units (work_unit_id),
    contract_revision_id  TEXT NOT NULL
        REFERENCES contract_revisions (contract_revision_id),
    is_current            INTEGER NOT NULL,
    b1_repository         TEXT NOT NULL,
    b1_commit_oid         TEXT NOT NULL,
    b1_material_sha256    TEXT NOT NULL,
    b1_requested_revision TEXT NOT NULL,
    admitted_at           TEXT NOT NULL,
    CHECK (is_current IN (0, 1)),
    CHECK (length(b1_commit_oid) = 40),
    CHECK (length(b1_material_sha256) = 64)
) STRICT;

CREATE UNIQUE INDEX attempts_one_current_per_work_unit
    ON attempts (work_unit_id) WHERE is_current = 1;

CREATE INDEX attempts_by_contract_revision
    ON attempts (contract_revision_id);

CREATE TABLE worktree_assignments (
    attempt_id     TEXT PRIMARY KEY REFERENCES attempts (attempt_id),
    work_unit_id   TEXT NOT NULL REFERENCES work_units (work_unit_id),
    repository     TEXT NOT NULL,
    worktree_path  TEXT NOT NULL UNIQUE,
    branch         TEXT NOT NULL,
    state          TEXT NOT NULL,
    allocated_at   TEXT NOT NULL,
    provisioned_at TEXT,
    UNIQUE (repository, branch),
    CHECK (state IN ('allocated', 'provisioned')),
    CHECK ((state = 'allocated') = (provisioned_at IS NULL))
) STRICT;

CREATE INDEX worktree_assignments_by_unit
    ON worktree_assignments (work_unit_id);

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

CREATE TRIGGER attempts_no_update BEFORE UPDATE ON attempts
BEGIN
    SELECT RAISE(ABORT,
        'an Attempt is immutable and cannot be rebound to another Contract revision');
END;

CREATE TRIGGER attempts_no_delete BEFORE DELETE ON attempts
BEGIN
    SELECT RAISE(ABORT, 'an Attempt is immutable');
END;

CREATE TRIGGER worktree_assignments_ownership_is_stable
BEFORE UPDATE ON worktree_assignments
WHEN OLD.attempt_id <> NEW.attempt_id
     OR OLD.work_unit_id <> NEW.work_unit_id
     OR OLD.repository <> NEW.repository
     OR OLD.worktree_path <> NEW.worktree_path
     OR OLD.branch <> NEW.branch
     OR OLD.allocated_at <> NEW.allocated_at
     OR OLD.state <> 'allocated'
     OR NEW.state <> 'provisioned'
BEGIN
    SELECT RAISE(ABORT,
        'worktree ownership is stable; only allocated -> provisioned may be recorded');
END;

CREATE TRIGGER worktree_assignments_no_delete BEFORE DELETE ON worktree_assignments
BEGIN
    SELECT RAISE(ABORT, 'worktree ownership is durable');
END;
"""

SUBMISSION_SQL = """
CREATE TABLE attempt_submissions (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts (attempt_id),
    submission_key TEXT NOT NULL UNIQUE,
    request_json TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('prepared', 'dispatched', 'correlated', 'blocked')),
    zeroshot_run_id TEXT UNIQUE,
    error_detail TEXT,
    CHECK ((state = 'correlated') = (zeroshot_run_id IS NOT NULL)),
    CHECK (zeroshot_run_id IS NULL OR length(trim(zeroshot_run_id)) > 0),
    CHECK ((state = 'blocked') = (error_detail IS NOT NULL))
) STRICT;

CREATE TRIGGER attempt_submissions_stable BEFORE UPDATE ON attempt_submissions
WHEN OLD.attempt_id <> NEW.attempt_id
  OR OLD.submission_key <> NEW.submission_key
  OR OLD.request_json <> NEW.request_json
  OR NOT ((OLD.state = 'prepared' AND NEW.state = 'dispatched')
       OR (OLD.state = 'dispatched' AND NEW.state IN ('correlated', 'blocked')))
BEGIN
    SELECT RAISE(ABORT, 'submission identity and correlation are immutable');
END;

CREATE TRIGGER attempt_submissions_no_delete BEFORE DELETE ON attempt_submissions
BEGIN
    SELECT RAISE(ABORT, 'submission identity is durable');
END;
"""

ASSURANCE_SQL = """
CREATE TABLE final_assurance (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts (attempt_id),
    zeroshot_run_id TEXT NOT NULL UNIQUE,
    record_json TEXT NOT NULL
) STRICT;

CREATE TRIGGER final_assurance_correlated_current BEFORE INSERT ON final_assurance
WHEN NOT EXISTS (
    SELECT 1 FROM attempt_submissions AS s JOIN attempts AS a USING (attempt_id)
    WHERE s.attempt_id = NEW.attempt_id AND s.state = 'correlated'
      AND s.zeroshot_run_id = NEW.zeroshot_run_id AND a.is_current = 1
)
BEGIN
    SELECT RAISE(ABORT, 'final assurance requires the current correlated Attempt');
END;

CREATE TRIGGER final_assurance_no_replace BEFORE INSERT ON final_assurance
WHEN EXISTS (
    SELECT 1 FROM final_assurance
    WHERE attempt_id = NEW.attempt_id OR zeroshot_run_id = NEW.zeroshot_run_id
)
BEGIN
    SELECT RAISE(ABORT, 'final assurance custody cannot be replaced');
END;

CREATE TRIGGER final_assurance_no_update BEFORE UPDATE ON final_assurance
BEGIN
    SELECT RAISE(ABORT, 'final assurance custody is immutable');
END;

CREATE TRIGGER final_assurance_no_delete BEFORE DELETE ON final_assurance
BEGIN
    SELECT RAISE(ABORT, 'final assurance custody is durable');
END;
"""

SCHEMA_SQL += SUBMISSION_SQL + ASSURANCE_SQL

#: Digest of the exact DDL this build initializes. Recorded in ``schema_meta`` so
#: a store written by a different DDL text is detected on reopen.
SCHEMA_SHA256 = hashlib.sha256(SCHEMA_SQL.encode("utf-8")).hexdigest()
