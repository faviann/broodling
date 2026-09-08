"""The Broodling-owned SQLite schema.

Scope discipline: this schema holds Work Unit identity, entitled source
snapshots, immutable Contract revisions, admission decisions, and the immutable
Attempt/B1/worktree-ownership records that admission allocates. It is
deliberately *not* a Zeroshot RunLedger mirror — there is no table for runs, node
occurrences, provider sessions, candidate seals, effect intents/receipts or
completed-occurrence projections. One final_assurance row retains only completed
P3 custody for an Attempt. One immutable disposition references that custody,
and a durable start marker forbids interrupted finalization recovery. Immutable
abandonment removes current authority without asserting runtime cessation.

Immutability is enforced in the database, not only in Python: append-only tables
carry ``BEFORE UPDATE``/``BEFORE DELETE`` triggers, so a direct SQL amendment of
an admitted Contract, or a rebinding of an Attempt to another Contract revision,
aborts. Exclusivity is enforced the same way: one current Attempt per Work Unit
is a partial unique index, and one owner per worktree path/branch is a unique
constraint, so two racing writers cannot both win.
"""

from __future__ import annotations

import hashlib

SCHEMA_VERSION = 8
V7_SCHEMA_SHA256 = "3aa00171fa58a56034c501433606eefb49d89f382393327728fbe6265a07b603"
V6_SCHEMA_SHA256 = "f0177a07384d8546a9d8f7971015e1b0d2a399215557882b9b2f10c2fe6fd195"
V5_SCHEMA_SHA256 = "8dfd3296120e6d859a77a4cb1141c3cca73dbe09836372791d36cafa91c8d1d5"
V4_SCHEMA_SHA256 = "f0971db85da6eb32727725b2163f96167576b8bf29a55fcc3d75c263b8961697"
V3_SCHEMA_SHA256 = "663acf981b7b5379bde50f9446d12bee922c81bcbdcc1b4e7f15f6c4e5ea007f"
V2_SCHEMA_SHA256 = "bbd7b68bdc66e6bc626f6f5d556e0efa3c9398476eb78b3f3ad75400ade29779"

#: Tables this schema owns. The set is asserted by the tests, so a RunLedger
#: mirror cannot be added without the boundary test failing.
TABLES: tuple[str, ...] = (
    "admission_decisions",
    "attempt_abandonments",
    "attempt_finalizations",
    "attempt_retirements",
    "attempt_retries",
    "attempt_submissions",
    "attempts",
    "contract_revisions",
    "contract_source_attributions",
    "entitled_sources",
    "final_assurance",
    "schema_meta",
    "work_unit_dispositions",
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

ABANDONMENT_SQL = """
CREATE TABLE attempt_abandonments (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts (attempt_id),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    abandoned_at TEXT NOT NULL
) STRICT;

CREATE TRIGGER attempt_abandonments_current BEFORE INSERT ON attempt_abandonments
WHEN NOT EXISTS (
    SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1
)
BEGIN
    SELECT RAISE(ABORT, 'only a current Attempt can first be abandoned');
END;

CREATE TRIGGER attempt_abandonments_no_replace BEFORE INSERT ON attempt_abandonments
WHEN EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'abandonment is irreversible');
END;

CREATE TRIGGER attempt_abandonments_no_update BEFORE UPDATE ON attempt_abandonments
BEGIN
    SELECT RAISE(ABORT, 'abandonment is immutable');
END;

CREATE TRIGGER attempt_abandonments_no_delete BEFORE DELETE ON attempt_abandonments
BEGIN
    SELECT RAISE(ABORT, 'abandonment is durable');
END;

DROP TRIGGER attempts_no_update;
CREATE TRIGGER attempts_no_update BEFORE UPDATE ON attempts
WHEN OLD.attempt_id <> NEW.attempt_id
  OR OLD.work_unit_id <> NEW.work_unit_id
  OR OLD.contract_revision_id <> NEW.contract_revision_id
  OR OLD.b1_repository <> NEW.b1_repository
  OR OLD.b1_commit_oid <> NEW.b1_commit_oid
  OR OLD.b1_material_sha256 <> NEW.b1_material_sha256
  OR OLD.b1_requested_revision <> NEW.b1_requested_revision
  OR OLD.admitted_at <> NEW.admitted_at
  OR NOT (OLD.is_current = 1 AND NEW.is_current = 0)
  OR NOT EXISTS (
      SELECT 1 FROM attempt_abandonments WHERE attempt_id = OLD.attempt_id
  )
BEGIN
    SELECT RAISE(ABORT, 'Attempt bindings are immutable; only abandonment removes currentness');
END;

CREATE TRIGGER attempt_abandonments_remove_current AFTER INSERT ON attempt_abandonments
BEGIN
    UPDATE attempts SET is_current = 0 WHERE attempt_id = NEW.attempt_id;
END;

CREATE TRIGGER attempts_no_abandonment_bypass BEFORE INSERT ON attempts
WHEN EXISTS (
    SELECT 1 FROM attempts AS a JOIN attempt_abandonments AS b USING (attempt_id)
    WHERE a.work_unit_id = NEW.work_unit_id
)
BEGIN
    SELECT RAISE(ABORT, 'an abandoned Work Unit requires explicit safe replacement authority');
END;

CREATE TRIGGER attempts_no_replace BEFORE INSERT ON attempts
WHEN EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'Attempt identity and bindings cannot be replaced');
END;

CREATE TRIGGER worktree_assignments_require_current BEFORE UPDATE ON worktree_assignments
WHEN NOT EXISTS (
    SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1
)
BEGIN
    SELECT RAISE(ABORT, 'only a current Attempt may acknowledge provisioning');
END;
"""

RETIREMENT_SQL = """
CREATE TABLE attempt_retirements (
    attempt_id TEXT PRIMARY KEY REFERENCES attempt_abandonments (attempt_id),
    ceased_at TEXT NOT NULL,
    proof_json TEXT NOT NULL,
    retired_at TEXT
) STRICT;

CREATE TRIGGER attempt_retirements_no_replace BEFORE INSERT ON attempt_retirements
WHEN EXISTS (SELECT 1 FROM attempt_retirements WHERE attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'cessation proof cannot be replaced');
END;

CREATE TRIGGER attempt_retirements_stable BEFORE UPDATE ON attempt_retirements
WHEN OLD.attempt_id <> NEW.attempt_id
  OR OLD.ceased_at <> NEW.ceased_at
  OR OLD.proof_json <> NEW.proof_json
  OR OLD.retired_at IS NOT NULL
  OR NEW.retired_at IS NULL
BEGIN
    SELECT RAISE(ABORT, 'only retirement acknowledgment may follow cessation');
END;

CREATE TRIGGER attempt_retirements_no_delete BEFORE DELETE ON attempt_retirements
BEGIN
    SELECT RAISE(ABORT, 'cessation and retirement facts are durable');
END;
"""

RETRY_SQL = """
CREATE TABLE attempt_retries (
    retry_id TEXT PRIMARY KEY CHECK (length(trim(retry_id)) > 0),
    predecessor_attempt_id TEXT NOT NULL UNIQUE REFERENCES attempt_retirements (attempt_id),
    attempt_id TEXT NOT NULL UNIQUE REFERENCES attempts (attempt_id) DEFERRABLE INITIALLY DEFERRED,
    workspace_root TEXT NOT NULL,
    target_json TEXT NOT NULL,
    requested_at TEXT NOT NULL
) STRICT;

CREATE TRIGGER attempt_retries_safe BEFORE INSERT ON attempt_retries
WHEN NOT EXISTS (
    SELECT 1 FROM attempt_retirements AS r
    JOIN attempt_abandonments AS b USING (attempt_id)
    JOIN attempts AS a USING (attempt_id)
    WHERE r.attempt_id = NEW.predecessor_attempt_id
      AND r.retired_at IS NOT NULL AND a.is_current = 0
      AND NOT EXISTS (
          SELECT 1 FROM attempts WHERE work_unit_id = a.work_unit_id AND is_current = 1
      )
) OR EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'retry requires an abandoned ceased retired predecessor and no current Attempt');
END;

CREATE TRIGGER attempt_retries_no_replace BEFORE INSERT ON attempt_retries
WHEN EXISTS (
    SELECT 1 FROM attempt_retries WHERE retry_id = NEW.retry_id
      OR predecessor_attempt_id = NEW.predecessor_attempt_id OR attempt_id = NEW.attempt_id
)
BEGIN
    SELECT RAISE(ABORT, 'retry identity and lineage cannot be replaced');
END;

CREATE TRIGGER attempt_retries_no_update BEFORE UPDATE ON attempt_retries
BEGIN
    SELECT RAISE(ABORT, 'retry identity and lineage are immutable');
END;

CREATE TRIGGER attempt_retries_no_delete BEFORE DELETE ON attempt_retries
BEGIN
    SELECT RAISE(ABORT, 'retry identity and lineage are durable');
END;

DROP TRIGGER attempts_no_abandonment_bypass;
CREATE TRIGGER attempts_no_abandonment_bypass BEFORE INSERT ON attempts
WHEN (EXISTS (
    SELECT 1 FROM attempts AS a JOIN attempt_abandonments AS b USING (attempt_id)
    WHERE a.work_unit_id = NEW.work_unit_id
) OR EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id))
AND NOT EXISTS (
    SELECT 1 FROM attempt_retries AS r
    JOIN attempts AS p ON p.attempt_id = r.predecessor_attempt_id
    JOIN attempt_retirements AS t ON t.attempt_id = p.attempt_id
    WHERE r.attempt_id = NEW.attempt_id AND t.retired_at IS NOT NULL
      AND p.work_unit_id = NEW.work_unit_id
      AND p.contract_revision_id = NEW.contract_revision_id
      AND p.b1_repository = NEW.b1_repository
      AND p.b1_commit_oid = NEW.b1_commit_oid
      AND p.b1_material_sha256 = NEW.b1_material_sha256
      AND p.b1_requested_revision = NEW.b1_requested_revision
)
BEGIN
    SELECT RAISE(ABORT, 'an abandoned Work Unit requires explicit safe replacement authority');
END;
"""

DISPOSITION_SQL = """
CREATE TABLE attempt_finalizations (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts (attempt_id),
    started_at TEXT NOT NULL
) STRICT;

CREATE TRIGGER attempt_finalizations_current BEFORE INSERT ON attempt_finalizations
WHEN NOT EXISTS (
    SELECT 1 FROM attempts AS a
    JOIN attempt_submissions AS s USING (attempt_id)
    JOIN admission_decisions AS d USING (contract_revision_id)
    WHERE a.attempt_id = NEW.attempt_id AND a.is_current = 1
      AND s.state = 'correlated' AND d.outcome = 'admitted'
      AND NOT EXISTS (SELECT 1 FROM final_assurance WHERE attempt_id = a.attempt_id)
      AND d.work_unit_id = a.work_unit_id
      AND NOT EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = a.attempt_id)
      AND NOT EXISTS (SELECT 1 FROM work_unit_dispositions WHERE work_unit_id = a.work_unit_id)
)
BEGIN
    SELECT RAISE(ABORT, 'finalization requires current admitted correlated authority');
END;

CREATE TABLE work_unit_dispositions (
    work_unit_id TEXT PRIMARY KEY REFERENCES work_units (work_unit_id),
    contract_revision_id TEXT NOT NULL REFERENCES contract_revisions (contract_revision_id),
    attempt_id TEXT NOT NULL UNIQUE REFERENCES final_assurance (attempt_id),
    outcome TEXT NOT NULL CHECK (outcome = 'SUCCEEDED'),
    completed_at TEXT NOT NULL
) STRICT;

CREATE TRIGGER work_unit_dispositions_bound BEFORE INSERT ON work_unit_dispositions
WHEN NOT EXISTS (
    SELECT 1 FROM attempts AS a
    JOIN contract_revisions AS c USING (contract_revision_id)
    JOIN admission_decisions AS d USING (contract_revision_id)
    JOIN attempt_submissions AS s USING (attempt_id)
    JOIN final_assurance AS f USING (attempt_id)
    JOIN attempt_finalizations AS z USING (attempt_id)
    WHERE a.attempt_id = NEW.attempt_id AND a.is_current = 1
      AND a.work_unit_id = NEW.work_unit_id
      AND a.contract_revision_id = NEW.contract_revision_id
      AND c.work_unit_id = a.work_unit_id AND d.work_unit_id = a.work_unit_id
      AND d.outcome = 'admitted' AND s.state = 'correlated'
      AND f.zeroshot_run_id = s.zeroshot_run_id
      AND json_type(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 'array'
      AND json_array_length(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 0
      AND json_extract(f.record_json, '$.attemptId') = a.attempt_id
      AND json_extract(f.record_json, '$.contractRevisionId') = a.contract_revision_id
      AND json_extract(f.record_json, '$.runId') = s.zeroshot_run_id
      AND NOT EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = a.attempt_id)
)
BEGIN
    SELECT RAISE(ABORT, 'disposition requires same admitted no-effect Contract and current complete custody');
END;

CREATE TRIGGER attempt_abandonments_no_completed BEFORE INSERT ON attempt_abandonments
WHEN EXISTS (SELECT 1 FROM work_unit_dispositions WHERE attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'completed disposition cannot be abandoned');
END;

CREATE TRIGGER attempts_no_completed_work_unit BEFORE INSERT ON attempts
WHEN EXISTS (SELECT 1 FROM work_unit_dispositions WHERE work_unit_id = NEW.work_unit_id)
BEGIN
    SELECT RAISE(ABORT, 'completed Work Unit cannot acquire new Attempt authority');
END;

DROP TRIGGER attempts_no_update;
CREATE TRIGGER attempts_no_update BEFORE UPDATE ON attempts
WHEN OLD.attempt_id <> NEW.attempt_id
  OR OLD.work_unit_id <> NEW.work_unit_id
  OR OLD.contract_revision_id <> NEW.contract_revision_id
  OR OLD.b1_repository <> NEW.b1_repository
  OR OLD.b1_commit_oid <> NEW.b1_commit_oid
  OR OLD.b1_material_sha256 <> NEW.b1_material_sha256
  OR OLD.b1_requested_revision <> NEW.b1_requested_revision
  OR OLD.admitted_at <> NEW.admitted_at
  OR NOT (OLD.is_current = 1 AND NEW.is_current = 0)
  OR NOT (EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = OLD.attempt_id)
          OR EXISTS (SELECT 1 FROM work_unit_dispositions WHERE attempt_id = OLD.attempt_id))
BEGIN
    SELECT RAISE(ABORT, 'Attempt bindings are immutable; only abandonment or disposition removes currentness');
END;

CREATE TRIGGER work_unit_dispositions_remove_current AFTER INSERT ON work_unit_dispositions
BEGIN
    UPDATE attempts SET is_current = 0 WHERE attempt_id = NEW.attempt_id;
END;
"""

for _table, _identity in (
    ("attempt_finalizations", "attempt_id = NEW.attempt_id"),
    (
        "work_unit_dispositions",
        "work_unit_id = NEW.work_unit_id OR attempt_id = NEW.attempt_id",
    ),
):
    DISPOSITION_SQL += f"""
CREATE TRIGGER {_table}_no_replace BEFORE INSERT ON {_table}
WHEN EXISTS (SELECT 1 FROM {_table} WHERE {_identity})
BEGIN
    SELECT RAISE(ABORT, '{_table} cannot be replaced');
END;
CREATE TRIGGER {_table}_no_update BEFORE UPDATE ON {_table}
BEGIN
    SELECT RAISE(ABORT, '{_table} is immutable');
END;
CREATE TRIGGER {_table}_no_delete BEFORE DELETE ON {_table}
BEGIN
    SELECT RAISE(ABORT, '{_table} is durable');
END;
"""

SCHEMA_SQL += (
    SUBMISSION_SQL
    + ASSURANCE_SQL
    + ABANDONMENT_SQL
    + RETIREMENT_SQL
    + RETRY_SQL
    + DISPOSITION_SQL
)

#: Digest of the exact DDL this build initializes. Recorded in ``schema_meta`` so
#: a store written by a different DDL text is detected on reopen.
SCHEMA_SHA256 = hashlib.sha256(SCHEMA_SQL.encode("utf-8")).hexdigest()
