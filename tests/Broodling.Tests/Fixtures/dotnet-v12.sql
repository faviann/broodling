BEGIN TRANSACTION;
CREATE TABLE admission_decisions (
    decision_id TEXT PRIMARY KEY,
    contract_revision_id TEXT NOT NULL UNIQUE REFERENCES contract_revisions(contract_revision_id),
    outcome TEXT NOT NULL CHECK (outcome IN ('admitted', 'rejected')),
    findings_json TEXT NOT NULL,
    policy_version TEXT NOT NULL,
    decided_at TEXT NOT NULL
) STRICT;
INSERT INTO "admission_decisions" VALUES('ad-2746acd049a192459c47ebbbac08cff757a0741c1417635a185bc9a00f169550','cr-a988fea8330188d6c3ac235dd1a1f522044567f139c529586eed26fcbf7769be','admitted','[]','broodling.dotnet.admission.v1/zeroshot-10.3.0','2026-09-25T02:14:14.3714417+00:00');
CREATE TABLE attempt_abandonments (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    abandoned_at TEXT NOT NULL
) STRICT;
CREATE TABLE attempt_completions (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
    run_id TEXT NOT NULL UNIQUE,
    receipt_json TEXT NOT NULL CHECK (json_valid(receipt_json)),
    completed_at TEXT NOT NULL
) STRICT;
CREATE TABLE attempt_retirements (
    attempt_id TEXT PRIMARY KEY REFERENCES attempt_abandonments(attempt_id),
    basis TEXT NOT NULL CHECK (basis IN ('never_materialized', 'never_dispatched')),
    ceased_at TEXT NOT NULL,
    retired_at TEXT
) STRICT;
CREATE TABLE attempt_retries (
    retry_key TEXT PRIMARY KEY CHECK (length(trim(retry_key)) > 0),
    predecessor_id TEXT NOT NULL UNIQUE REFERENCES attempt_retirements(attempt_id),
    attempt_id TEXT NOT NULL UNIQUE REFERENCES attempts(attempt_id) DEFERRABLE INITIALLY DEFERRED,
    workspace_root TEXT NOT NULL,
    target_json TEXT NOT NULL CHECK (json_valid(target_json) AND json_type(target_json) = 'object'),
    requested_at TEXT NOT NULL,
    CHECK (predecessor_id <> attempt_id)
) STRICT;
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
INSERT INTO "contract_revisions" VALUES('cr-a988fea8330188d6c3ac235dd1a1f522044567f139c529586eed26fcbf7769be','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a',1,'a988fea8330188d6c3ac235dd1a1f522044567f139c529586eed26fcbf7769be',X'7B22776F726B556E69744964223A2277752D64623239396566336435316363636431303630666461323262333432646232633332663165356138643236366664313034313939626431363431663638323461222C22736F757263654174747269627574696F6E223A5B7B22736F757263654964223A227372632D34333533323762326135653536633736613165343631356264633665393832366533666664313739366663363861363830633336323265386435616338313166222C22636F6E74656E74536861323536223A2236346662323633646566333261323961316562386561333263386433323339643131323730303237633336373633663533633637346237373264373461353432227D5D2C226372697465726961223A5B7B22637269746572696F6E4964223A22616363657074616E6365222C2273746174656D656E74223A2250726573657276652074686520636F6D706C65746520726571756573742E222C2265766964656E6365506F70756C6174696F6E223A6E756C6C2C2276616C69646174696F6E5365616D223A22222C2276616C69646174696F6E416374696F6E223A22222C2266616C73696679696E674F62736572766174696F6E223A22222C2265766964656E6365456666656374446570656E64656E63696573223A5B5D2C226D656368616E6963616C45766964656E6365223A6E756C6C7D5D2C226F626C69676174696F6E73223A5B5D2C2270726572657175697369746573223A5B5D2C22726571756972656445666665637473223A5B5D2C22686F7374417373756D7074696F6E73223A5B5D2C22636F6E73747275637465644279223A2263616C6C6572222C226E6F746573223A22222C2266696E616C4173737572616E63654D6174657269616C73223A6E756C6C7D','caller',NULL,'2026-09-25T02:14:14.3248841+00:00');
CREATE TABLE contract_sources (
    contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
    source_id TEXT NOT NULL REFERENCES entitled_sources(source_id),
    content_sha256 TEXT NOT NULL,
    PRIMARY KEY (contract_revision_id, source_id)
) STRICT;
INSERT INTO "contract_sources" VALUES('cr-a988fea8330188d6c3ac235dd1a1f522044567f139c529586eed26fcbf7769be','src-435327b2a5e56c76a1e4615bdc6e9826e3ffd1796fc68a680c3622e8d5ac811f','64fb263def32a29a1eb8ea32c8d3239d11270027c36763f53c674b772d74a542');
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
INSERT INTO "entitled_sources" VALUES('src-435327b2a5e56c76a1e4615bdc6e9826e3ffd1796fc68a680c3622e8d5ac811f','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','primary_issue','https://github.com/acme/widget/issues/12',X'54686520636F6D706C65746520726576696577656420726571756573742E0A','64fb263def32a29a1eb8ea32c8d3239d11270027c36763f53c674b772d74a542','text/plain; charset=utf-8','caller','caller','Reviewed supplied issue bytes','2026-09-25T02:14:14.2238715+00:00','2026-09-25T02:14:14.2238715+00:00');
INSERT INTO "entitled_sources" VALUES('src-8454b5ffd81ef89a79c31818d856c02f32d8773ca8377e042177d6d81818eb53','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','primary_issue','https://github.com/acme/widget/issues/12',X'72657461696E6564206973737565','856674dc505945e91107f42bdd71d665ff7ec110d5d91c4fe6f783a91b77fa13','text/plain; charset=utf-8','caller','broodling_policy','primary_authoritative_work_reference','2026-09-25T02:14:14.3972999+00:00','2026-09-25T02:14:14.3972999+00:00');
CREATE TABLE installation_control (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    admission_dispatch_paused INTEGER NOT NULL CHECK (admission_dispatch_paused IN (0, 1)),
    changed_at TEXT NOT NULL
) STRICT;
INSERT INTO "installation_control" VALUES(1,1,'2026-09-25T02:14:14.4194735+00:00');
CREATE TABLE issue_submission_cancellations (
    submission_id TEXT PRIMARY KEY REFERENCES issue_submissions(submission_id),
    attempt_id TEXT REFERENCES attempts(attempt_id),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    cancelled_at TEXT NOT NULL
) STRICT;
INSERT INTO "issue_submission_cancellations" VALUES('issue-sub-8404a7bb10224ac8a8748301f3d31c99',NULL,'Operator withdrew the request.','2026-09-25T02:14:14.4181221+00:00');
CREATE TABLE issue_submissions (
    submission_id TEXT PRIMARY KEY,
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    submission_sequence INTEGER NOT NULL CHECK (submission_sequence > 0),
    issue_url TEXT NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('accepted', 'capturing', 'preparing', 'admitted', 'rejected', 'cancelled', 'interrupted', 'abandoned', 'completed')),
    contract_revision_id TEXT REFERENCES contract_revisions(contract_revision_id),
    received_at TEXT NOT NULL,
    UNIQUE (work_unit_id, submission_sequence)
) STRICT;
INSERT INTO "issue_submissions" VALUES('issue-sub-f21756a815684a93b0a7663e10bd284d','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a',1,'https://github.com/acme/widget/issues/12','capturing',NULL,'2026-09-25T02:14:14.3859224+00:00');
INSERT INTO "issue_submissions" VALUES('issue-sub-8404a7bb10224ac8a8748301f3d31c99','wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f',1,'https://github.com/acme/widget/issues/13','cancelled',NULL,'2026-09-25T02:14:14.4155018+00:00');
CREATE TABLE native_submissions (
    attempt_id TEXT PRIMARY KEY REFERENCES worktree_provisions(attempt_id),
    submission_key TEXT NOT NULL UNIQUE,
    request_json TEXT NOT NULL CHECK (json_valid(request_json)),
    state TEXT NOT NULL CHECK (state IN ('prepared', 'dispatched', 'correlated', 'blocked')),
    run_id TEXT UNIQUE,
    CHECK ((state = 'correlated' AND run_id IS NOT NULL AND length(trim(run_id)) > 0)
        OR (state <> 'correlated' AND run_id IS NULL))
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
INSERT INTO "request_bundle_references" VALUES('bundle-8fc0ab54afee22f2db949512ea0c133a5096426d39aff37d954ca3d7c17dfbe4','issue',0,'source',X'69737375652073656C6563746F72','src-8454b5ffd81ef89a79c31818d856c02f32d8773ca8377e042177d6d81818eb53','856674dc505945e91107f42bdd71d665ff7ec110d5d91c4fe6f783a91b77fa13',NULL,NULL,NULL,NULL,NULL,NULL);
CREATE TABLE request_bundle_repositories (
    bundle_id TEXT PRIMARY KEY REFERENCES request_bundles(bundle_id),
    repository TEXT NOT NULL,
    default_branch TEXT NOT NULL,
    starting_revision TEXT NOT NULL,
    starting_commit_oid TEXT NOT NULL CHECK (length(starting_commit_oid) = 40
        AND starting_commit_oid NOT GLOB '*[^0-9a-f]*'),
    prepared_at TEXT NOT NULL
) STRICT;
CREATE TABLE request_bundles (
    bundle_id TEXT PRIMARY KEY,
    submission_id TEXT NOT NULL UNIQUE REFERENCES issue_submissions(submission_id),
    acquisition_inputs BLOB NOT NULL,
    acquisition_policy BLOB NOT NULL,
    acquisition_limits BLOB NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('capturing', 'complete')),
    manifest_json TEXT,
    manifest_sha256 TEXT CHECK (manifest_sha256 IS NULL OR length(manifest_sha256) = 64),
    created_at TEXT NOT NULL,
    completed_at TEXT,
    CHECK ((state = 'capturing' AND manifest_json IS NULL AND manifest_sha256 IS NULL AND completed_at IS NULL)
        OR (state = 'complete' AND manifest_json IS NOT NULL AND manifest_sha256 IS NOT NULL AND completed_at IS NOT NULL))
) STRICT;
INSERT INTO "request_bundles" VALUES('bundle-8fc0ab54afee22f2db949512ea0c133a5096426d39aff37d954ca3d7c17dfbe4','issue-sub-f21756a815684a93b0a7663e10bd284d',X'696E707574732D7631',X'706F6C6963792D7631',X'6C696D6974732D7631','complete','{"version":"v1","bundleId":"bundle-8fc0ab54afee22f2db949512ea0c133a5096426d39aff37d954ca3d7c17dfbe4","submissionId":"issue-sub-f21756a815684a93b0a7663e10bd284d","workUnitId":"wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a","acquisitionInputs":"aW5wdXRzLXYx","acquisitionPolicy":"cG9saWN5LXYx","acquisitionLimits":"bGltaXRzLXYx","repository":null,"references":[{"referenceId":"issue","ordinal":0,"captureKind":"source","selector":"aXNzdWUgc2VsZWN0b3I=","sourceId":"src-8454b5ffd81ef89a79c31818d856c02f32d8773ca8377e042177d6d81818eb53","contentSha256":"856674dc505945e91107f42bdd71d665ff7ec110d5d91c4fe6f783a91b77fa13","gitCommitOid":null,"gitPath":null,"gitBlobOid":null}]}','a75469a5b2b5858f1c5497a420ad1102ac71537a8cba0ae87d18104cbdb58e6f','2026-09-25T02:14:14.3895924+00:00','2026-09-25T02:14:14.4143748+00:00');
CREATE TABLE store_metadata (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    format TEXT NOT NULL,
    version INTEGER NOT NULL,
    definition_hash TEXT NOT NULL,
    manifest_hash TEXT NOT NULL,
    initialized_at TEXT NOT NULL
) STRICT;
INSERT INTO "store_metadata" VALUES(1,'broodling.dotnet',12,'b4f859fcb7847afc686bb46a72af1c6b1f80fedfd6f6fc76909fe1274f371a95','06e016bef175105c38b3329629e46cdc1aabca4c44ab94abbf143c53e9178f5e','2026-09-25T02:14:14.1338703+00:00');
CREATE TABLE work_submissions (
    submission_id TEXT PRIMARY KEY,
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    submitted_repository TEXT NOT NULL,
    submitted_issue TEXT NOT NULL,
    received_at TEXT NOT NULL
) STRICT;
INSERT INTO "work_submissions" VALUES('sub-533f962ac5174cc3840f0e6500cbe09d','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','acme/widget','12','2026-09-25T02:14:14.2212088+00:00');
INSERT INTO "work_submissions" VALUES('sub-df1e6ef4d84248a290a37efff5794d66','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','acme/widget','https://github.com/acme/widget/issues/12','2026-09-25T02:14:14.3859224+00:00');
INSERT INTO "work_submissions" VALUES('sub-8acfcf4a67d54a519c31154202d40c13','wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f','acme/widget','https://github.com/acme/widget/issues/13','2026-09-25T02:14:14.4155018+00:00');
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
INSERT INTO "work_units" VALUES('wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','github.com/acme/widget#12','github.com','acme','widget',12,'https://github.com/acme/widget/issues/12',NULL,NULL,'2026-09-25T02:14:14.2190470+00:00');
INSERT INTO "work_units" VALUES('wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f','github.com/acme/widget#13','github.com','acme','widget',13,'https://github.com/acme/widget/issues/13',NULL,NULL,'2026-09-25T02:14:14.4153441+00:00');
CREATE TABLE worktree_provisions (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
    provisioned_at TEXT NOT NULL
) STRICT;
CREATE INDEX submissions_by_work ON work_submissions(work_unit_id);
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
CREATE UNIQUE INDEX one_current_attempt ON attempts(work_unit_id) WHERE is_current = 1;
CREATE INDEX attempts_by_revision ON attempts(contract_revision_id);
CREATE TRIGGER attempts_require_admission BEFORE INSERT ON attempts
WHEN NEW.is_current <> 1 OR NOT EXISTS (
    SELECT 1 FROM contract_revisions AS revision
    JOIN admission_decisions AS decision USING (contract_revision_id)
    WHERE revision.contract_revision_id = NEW.contract_revision_id
      AND revision.work_unit_id = NEW.work_unit_id AND decision.outcome = 'admitted'
)
BEGIN SELECT RAISE(ABORT, 'Attempt requires admitted authority for its Work Unit'); END;
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
CREATE TRIGGER provision_requires_current BEFORE INSERT ON worktree_provisions
WHEN NOT EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id AND is_current = 1)
BEGIN SELECT RAISE(ABORT, 'provisioning requires current Attempt authority'); END;
CREATE TRIGGER provisions_no_update BEFORE UPDATE ON worktree_provisions
BEGIN SELECT RAISE(ABORT, 'first provisioning acknowledgment is immutable'); END;
CREATE TRIGGER provisions_no_delete BEFORE DELETE ON worktree_provisions
BEGIN SELECT RAISE(ABORT, 'provisioning history is immutable'); END;
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
CREATE TRIGGER retirement_requires_safe_history BEFORE INSERT ON attempt_retirements
WHEN NEW.retired_at IS NOT NULL
  OR EXISTS (SELECT 1 FROM attempt_retirements WHERE attempt_id = NEW.attempt_id)
  OR EXISTS (SELECT 1 FROM native_submissions WHERE attempt_id = NEW.attempt_id AND state <> 'prepared')
  OR (NEW.basis = 'never_materialized' AND EXISTS (SELECT 1 FROM worktree_provisions WHERE attempt_id = NEW.attempt_id))
  OR (NEW.basis = 'never_dispatched' AND NOT EXISTS (SELECT 1 FROM worktree_provisions WHERE attempt_id = NEW.attempt_id))
BEGIN SELECT RAISE(ABORT, 'retirement requires abandoned never-dispatched history'); END;
CREATE TRIGGER retirement_stable BEFORE UPDATE ON attempt_retirements
WHEN OLD.attempt_id <> NEW.attempt_id OR OLD.basis <> NEW.basis OR OLD.ceased_at <> NEW.ceased_at
  OR OLD.retired_at IS NOT NULL OR NEW.retired_at IS NULL
BEGIN SELECT RAISE(ABORT, 'cessation proof and retirement acknowledgment are immutable'); END;
CREATE TRIGGER retirement_no_delete BEFORE DELETE ON attempt_retirements
BEGIN SELECT RAISE(ABORT, 'retirement history is immutable'); END;
CREATE TRIGGER retry_requires_retirement BEFORE INSERT ON attempt_retries
WHEN EXISTS (SELECT 1 FROM attempt_retries WHERE retry_key = NEW.retry_key
    OR predecessor_id = NEW.predecessor_id OR attempt_id = NEW.attempt_id)
  OR NOT EXISTS (
    SELECT 1 FROM attempt_retirements AS r JOIN attempt_abandonments USING (attempt_id)
    JOIN attempts AS a USING (attempt_id)
    WHERE r.attempt_id = NEW.predecessor_id AND r.retired_at IS NOT NULL AND a.is_current = 0
      AND NOT EXISTS (SELECT 1 FROM attempts AS current WHERE current.work_unit_id = a.work_unit_id AND current.is_current = 1)
      AND NOT EXISTS (SELECT 1 FROM native_submissions AS s WHERE s.attempt_id = a.attempt_id AND s.state <> 'prepared')
) OR EXISTS (SELECT 1 FROM attempts WHERE attempt_id = NEW.attempt_id)
BEGIN SELECT RAISE(ABORT, 'retry requires safely retired predecessor and a new successor'); END;
CREATE TRIGGER retries_no_update BEFORE UPDATE ON attempt_retries
BEGIN SELECT RAISE(ABORT, 'retry identity and parameters are immutable'); END;
CREATE TRIGGER retries_no_delete BEFORE DELETE ON attempt_retries
BEGIN SELECT RAISE(ABORT, 'retry lineage is immutable'); END;
CREATE TRIGGER attempts_no_abandoned_work BEFORE INSERT ON attempts
WHEN EXISTS (SELECT 1 FROM attempts WHERE work_unit_id = NEW.work_unit_id AND is_current = 0)
  AND NOT EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id)
BEGIN SELECT RAISE(ABORT, 'ended Work Unit requires explicit safe replacement'); END;
CREATE TRIGGER retry_preserves_bindings BEFORE INSERT ON attempts
WHEN EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id)
  AND NOT EXISTS (
    SELECT 1 FROM attempt_retries AS r JOIN attempts AS p ON p.attempt_id = r.predecessor_id
    WHERE r.attempt_id = NEW.attempt_id AND NEW.work_unit_id = p.work_unit_id
      AND NEW.contract_revision_id = p.contract_revision_id AND NEW.b1_repository = p.b1_repository
      AND NEW.b1_commit_oid = p.b1_commit_oid AND NEW.b1_material_sha256 = p.b1_material_sha256
      AND NEW.b1_requested_revision = p.b1_requested_revision AND NEW.workspace_root = r.workspace_root
      AND NEW.enclosure = r.workspace_root || '/' || NEW.attempt_id
      AND NEW.worktree_path = NEW.enclosure || '/worktree' AND NEW.branch = 'broodling/' || NEW.attempt_id
)
BEGIN SELECT RAISE(ABORT, 'replacement must preserve original B1 and chosen allocation'); END;
CREATE TRIGGER retry_submission_target BEFORE INSERT ON native_submissions
WHEN EXISTS (SELECT 1 FROM attempt_retries WHERE attempt_id = NEW.attempt_id
    AND json(target_json) IS NOT json_extract(NEW.request_json, '$.target'))
BEGIN SELECT RAISE(ABORT, 'replacement must preserve its chosen target'); END;
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
  OR OLD.created_at <> NEW.created_at OR OLD.state <> 'capturing' OR NEW.state <> 'complete'
  OR NEW.manifest_json IS NULL OR NEW.manifest_sha256 IS NULL OR NEW.completed_at IS NULL
  OR EXISTS (SELECT 1 FROM request_bundle_references AS r
      WHERE (r.bundle_id = OLD.bundle_id OR r.bundle_id = NEW.bundle_id)
        AND (r.content_sha256 IS NULL OR (r.capture_kind = 'source' AND r.source_id IS NULL)
            OR (r.capture_kind = 'git_blob' AND (r.git_repository IS NULL OR r.git_commit_oid IS NULL OR r.git_blob_oid IS NULL))))
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
COMMIT;
