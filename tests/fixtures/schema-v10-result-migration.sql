DROP TRIGGER work_unit_dispositions_bound;
CREATE TRIGGER work_unit_dispositions_bound BEFORE INSERT ON work_unit_dispositions
WHEN NOT EXISTS (
    SELECT 1 FROM attempts AS a
    JOIN work_units AS w USING (work_unit_id)
    JOIN contract_revisions AS c USING (contract_revision_id)
    JOIN admission_decisions AS d USING (contract_revision_id)
    JOIN attempt_submissions AS s USING (attempt_id)
    JOIN final_assurance AS f USING (attempt_id)
    WHERE a.attempt_id = NEW.attempt_id AND a.is_current = 1
      AND a.work_unit_id = NEW.work_unit_id
      AND a.contract_revision_id = NEW.contract_revision_id
      AND c.work_unit_id = a.work_unit_id AND d.work_unit_id = a.work_unit_id
      AND d.outcome = 'admitted' AND s.state = 'correlated'
      AND f.zeroshot_run_id = s.zeroshot_run_id
      AND json_type(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 'array'
      AND json_array_length(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects') = 1
      AND json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].kind') = 'pull_request'
      AND json_extract(CAST(c.canonical_bytes AS TEXT), '$.requiredEffects[0].targetBranch')
          = json_extract(s.request_json, '$.delivery.targetBranch')
      AND json_extract(s.request_json, '$.preset.name') = 'software-change'
      AND json_extract(s.request_json, '$.preset.delivery') = 'pull_request'
      AND w.host = 'github.com'
      AND json_extract(s.request_json, '$.delivery.repository') = w.owner || '/' || w.repository
      AND json_extract(s.request_json, '$.delivery.baseRevision') = a.b1_commit_oid
      AND json_extract(f.record_json, '$.format') = 'broodling.final-assurance/v3'
      AND json_extract(f.record_json, '$.attemptId') = a.attempt_id
      AND json_extract(f.record_json, '$.contractRevisionId') = a.contract_revision_id
      AND json_extract(f.record_json, '$.runId') = s.zeroshot_run_id
      AND json_extract(f.record_json, '$.workflow.name') = 'software-change'
      AND json_extract(f.record_json, '$.workflow.delivery') = 'pull_request'
      AND (SELECT count(*) FROM json_each(f.record_json)) = 7
      AND json_type(f.record_json, '$.deliveryReceipt') = 'object'
      AND (SELECT count(*) FROM json_each(f.record_json, '$.deliveryReceipt')) = 7
      AND json_extract(f.record_json, '$.acceptedRevision')
          = json_extract(f.record_json, '$.deliveryReceipt.headRevision')
      AND json_extract(f.record_json, '$.deliveryReceipt.version') = 'v1'
      AND json_extract(f.record_json, '$.deliveryReceipt.mode') = 'pr'
      AND json_extract(f.record_json, '$.deliveryReceipt.outcome') = 'opened'
      AND json_extract(f.record_json, '$.deliveryReceipt.repository')
          = json_extract(s.request_json, '$.delivery.repository')
      AND json_extract(f.record_json, '$.deliveryReceipt.targetBranch')
          = json_extract(s.request_json, '$.delivery.targetBranch')
      AND json_extract(f.record_json, '$.deliveryReceipt.headRevision')
          <> a.b1_commit_oid
      AND length(json_extract(f.record_json, '$.deliveryReceipt.headRevision')) = 40
      AND json_extract(f.record_json, '$.deliveryReceipt.headRevision')
          NOT GLOB '*[^0-9a-f]*'
      AND json_extract(f.record_json, '$.deliveryReceipt.pullRequestId')
          NOT GLOB '*[^0-9]*'
      AND json_extract(f.record_json, '$.deliveryReceipt.pullRequestId') <> ''
      AND NOT EXISTS (SELECT 1 FROM attempt_abandonments WHERE attempt_id = a.attempt_id)
)
BEGIN
    SELECT RAISE(ABORT, 'disposition requires the current PR-authorized Attempt and matching stable Zeroshot receipt');
END;
