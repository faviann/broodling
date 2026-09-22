BEGIN TRANSACTION;
CREATE TABLE admission_decisions (
    decision_id TEXT PRIMARY KEY,
    contract_revision_id TEXT NOT NULL UNIQUE REFERENCES contract_revisions(contract_revision_id),
    outcome TEXT NOT NULL CHECK (outcome IN ('admitted', 'rejected')),
    findings_json TEXT NOT NULL,
    policy_version TEXT NOT NULL,
    decided_at TEXT NOT NULL
) STRICT;
INSERT INTO "admission_decisions" VALUES('ad-71986f649ea2d66643a55e81126233349ba866e3ee3eb94fbc8e235287c4f44c','cr-fb357c7ca4e3c85dd55365f9d99427901f88272031c53ac68c2b146be4919aba','admitted','[]','broodling.dotnet.admission.v1/zeroshot-10.3.0','2026-09-22T15:29:11.0565967+00:00');
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
INSERT INTO "contract_revisions" VALUES('cr-fb357c7ca4e3c85dd55365f9d99427901f88272031c53ac68c2b146be4919aba','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a',1,'fb357c7ca4e3c85dd55365f9d99427901f88272031c53ac68c2b146be4919aba',X'7B22776F726B556E69744964223A2277752D64623239396566336435316363636431303630666461323262333432646232633332663165356138643236366664313034313939626431363431663638323461222C22736F757263654174747269627574696F6E223A5B7B22736F757263654964223A227372632D65353433323364626163616239623935623364306165373632383134333736613937303835613164663630346230323731396465366338316565336234363932222C22636F6E74656E74536861323536223A2265393438396633376662333035316539656661316463393136303034643732373465376236333937356533323039373038393437323637663233393361396265227D5D2C226372697465726961223A5B7B22637269746572696F6E4964223A2263222C2273746174656D656E74223A2252657461696E20746869732076322061646D697373696F6E2E222C2265766964656E6365506F70756C6174696F6E223A6E756C6C2C2276616C69646174696F6E5365616D223A22222C2276616C69646174696F6E416374696F6E223A22222C2266616C73696679696E674F62736572766174696F6E223A22222C2265766964656E6365456666656374446570656E64656E63696573223A5B5D2C226D656368616E6963616C45766964656E6365223A6E756C6C7D5D2C226F626C69676174696F6E73223A5B5D2C2270726572657175697369746573223A5B5D2C22726571756972656445666665637473223A5B5D2C22686F7374417373756D7074696F6E73223A5B5D2C22636F6E73747275637465644279223A226D6F64656C5F65787472616374696F6E222C226E6F746573223A22222C2266696E616C4173737572616E63654D6174657269616C73223A6E756C6C7D','model_extraction',NULL,'2026-09-22T15:29:11.0313071+00:00');
CREATE TABLE contract_sources (
    contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
    source_id TEXT NOT NULL REFERENCES entitled_sources(source_id),
    content_sha256 TEXT NOT NULL,
    PRIMARY KEY (contract_revision_id, source_id)
) STRICT;
INSERT INTO "contract_sources" VALUES('cr-fb357c7ca4e3c85dd55365f9d99427901f88272031c53ac68c2b146be4919aba','src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be');
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
INSERT INTO "entitled_sources" VALUES('src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','primary_issue','https://github.com/acme/widget/issues/12',X'00FF0D0A','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be','text/plain; charset=utf-8','caller','caller','Retain v2 bytes','2026-09-22T15:29:10.8982120+00:00','2026-09-22T15:29:10.8982120+00:00');
CREATE TABLE store_metadata (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    format TEXT NOT NULL,
    version INTEGER NOT NULL,
    definition_hash TEXT NOT NULL,
    manifest_hash TEXT NOT NULL,
    initialized_at TEXT NOT NULL
) STRICT;
INSERT INTO "store_metadata" VALUES(1,'broodling.dotnet',2,'8fa1224b5c310708ee4d6b65aff32a4aa0e790cab82694b50f49b542553e5850','e8956e5dbb788088635e6467b71f5ac6170cbbadfe481775fbda77965c347896','2026-09-22T15:29:10.8380819+00:00');
CREATE TABLE work_submissions (
    submission_id TEXT PRIMARY KEY,
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    submitted_repository TEXT NOT NULL,
    submitted_issue TEXT NOT NULL,
    received_at TEXT NOT NULL
) STRICT;
INSERT INTO "work_submissions" VALUES('sub-c4ff8532d2f749daa5fe8e776d5e4681','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','acme/widget','12','2026-09-22T15:29:10.8940541+00:00');
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
INSERT INTO "work_units" VALUES('wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','github.com/acme/widget#12','github.com','acme','widget',12,'https://github.com/acme/widget/issues/12',NULL,NULL,'2026-09-22T15:29:10.8918457+00:00');
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
COMMIT;
