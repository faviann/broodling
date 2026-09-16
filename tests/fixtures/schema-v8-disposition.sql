
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

CREATE TRIGGER attempt_finalizations_no_replace BEFORE INSERT ON attempt_finalizations
WHEN EXISTS (SELECT 1 FROM attempt_finalizations WHERE attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'attempt_finalizations cannot be replaced');
END;
CREATE TRIGGER attempt_finalizations_no_update BEFORE UPDATE ON attempt_finalizations
BEGIN
    SELECT RAISE(ABORT, 'attempt_finalizations is immutable');
END;
CREATE TRIGGER attempt_finalizations_no_delete BEFORE DELETE ON attempt_finalizations
BEGIN
    SELECT RAISE(ABORT, 'attempt_finalizations is durable');
END;

CREATE TRIGGER work_unit_dispositions_no_replace BEFORE INSERT ON work_unit_dispositions
WHEN EXISTS (SELECT 1 FROM work_unit_dispositions WHERE work_unit_id = NEW.work_unit_id OR attempt_id = NEW.attempt_id)
BEGIN
    SELECT RAISE(ABORT, 'work_unit_dispositions cannot be replaced');
END;
CREATE TRIGGER work_unit_dispositions_no_update BEFORE UPDATE ON work_unit_dispositions
BEGIN
    SELECT RAISE(ABORT, 'work_unit_dispositions is immutable');
END;
CREATE TRIGGER work_unit_dispositions_no_delete BEFORE DELETE ON work_unit_dispositions
BEGIN
    SELECT RAISE(ABORT, 'work_unit_dispositions is durable');
END;
