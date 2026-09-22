BEGIN TRANSACTION;
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
INSERT INTO "entitled_sources" VALUES('src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','primary_issue','https://github.com/acme/widget/issues/12',X'00FF0D0A','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be','application/octet-stream','caller','caller','Retain reviewed original bytes','2026-09-21T00:00:00Z','2026-09-22T15:09:40.1757357+00:00');
CREATE TABLE store_metadata (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    format TEXT NOT NULL,
    version INTEGER NOT NULL,
    definition_hash TEXT NOT NULL,
    manifest_hash TEXT NOT NULL,
    initialized_at TEXT NOT NULL
) STRICT;
INSERT INTO "store_metadata" VALUES(1,'broodling.dotnet',1,'38cfb4cbb3e90254af0595c331bb5907cdbdfd525af0f551f6b3105932eb18fe','aba3daa44b13a0482137f57336e769e1af70e5ef5a10cd654666a6e9d7f60257','2026-09-22T15:07:04.8538767+00:00');
CREATE TABLE work_submissions (
    submission_id TEXT PRIMARY KEY,
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    submitted_repository TEXT NOT NULL,
    submitted_issue TEXT NOT NULL,
    received_at TEXT NOT NULL
) STRICT;
INSERT INTO "work_submissions" VALUES('sub-a33c32e67e924cff89554253591cc2ba','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','git@github.com:Acme/Widget.git','#12','2026-09-22T15:09:40.1668596+00:00');
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
INSERT INTO "work_units" VALUES('wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','github.com/acme/widget#12','github.com','acme','widget',12,'https://github.com/acme/widget/issues/12','repository-v1','issue-v1','2026-09-22T15:09:40.1601184+00:00');
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
COMMIT;
