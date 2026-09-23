BEGIN TRANSACTION;
CREATE TABLE admission_decisions (
    decision_id TEXT PRIMARY KEY,
    contract_revision_id TEXT NOT NULL UNIQUE REFERENCES contract_revisions(contract_revision_id),
    outcome TEXT NOT NULL CHECK (outcome IN ('admitted', 'rejected')),
    findings_json TEXT NOT NULL,
    policy_version TEXT NOT NULL,
    decided_at TEXT NOT NULL
) STRICT;
INSERT INTO "admission_decisions" VALUES('ad-02687293cf3853af2e8f440e33b34a6d2c0dd7c5ad5fecc8f209d071963a973b','cr-42cf6edb76c14bbe913bfd59427c7db207ebb4cad507433e921ba078dda76cd8','admitted','[]','broodling.dotnet.admission.v1/zeroshot-10.3.0','2026-09-22T18:21:06.9856811+00:00');
INSERT INTO "admission_decisions" VALUES('ad-395728bac7f7ea44e16d2f0e4c2ebae2605bfb9928275f4698cf4681424bd0a6','cr-13e6e6b9fb16d7c05295068f1c6b02586a9d9b241f572def5e625538a40ed510','admitted','[]','broodling.dotnet.admission.v1/zeroshot-10.3.0','2026-09-22T18:21:07.6030751+00:00');
CREATE TABLE attempt_abandonments (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    abandoned_at TEXT NOT NULL
) STRICT;
INSERT INTO "attempt_abandonments" VALUES('at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','Retained schema 6 abandonment','2026-09-22T18:21:07.9876341+00:00');
CREATE TABLE attempt_completions (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
    run_id TEXT NOT NULL UNIQUE,
    receipt_json TEXT NOT NULL CHECK (json_valid(receipt_json)),
    completed_at TEXT NOT NULL
) STRICT;
INSERT INTO "attempt_completions" VALUES('at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','cr-42cf6edb76c14bbe913bfd59427c7db207ebb4cad507433e921ba078dda76cd8','retained-schema6-run-12','{"version":"v1","mode":"pr","outcome":"opened","repository":"acme/widget","targetBranch":"main","headRevision":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","pullRequestId":"0000"}','2026-09-22T18:21:07.5894273+00:00');
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
INSERT INTO "attempts" VALUES('at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','cr-42cf6edb76c14bbe913bfd59427c7db207ebb4cad507433e921ba078dda76cd8',0,'/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/source/.git','06566e1f25f7d0932ab987cfc642660af34dc3c1','1258d0e25a6984a5e433338585e89fb0b503a0ba38e6a534f32dde301938f3a1','HEAD','/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts','/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts/at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts/at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f/worktree','broodling/at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','2026-09-22T18:21:07.1461265+00:00');
INSERT INTO "attempts" VALUES('at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f','cr-13e6e6b9fb16d7c05295068f1c6b02586a9d9b241f572def5e625538a40ed510',0,'/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/source/.git','06566e1f25f7d0932ab987cfc642660af34dc3c1','5616ce4554f3bc5f9232f095f7b3040608865318b5e9c243a04fdbacf5487996','HEAD','/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts','/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts/at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts/at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670/worktree','broodling/at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','2026-09-22T18:21:07.6583349+00:00');
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
INSERT INTO "contract_revisions" VALUES('cr-42cf6edb76c14bbe913bfd59427c7db207ebb4cad507433e921ba078dda76cd8','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a',1,'42cf6edb76c14bbe913bfd59427c7db207ebb4cad507433e921ba078dda76cd8',X'7B22776F726B556E69744964223A2277752D64623239396566336435316363636431303630666461323262333432646232633332663165356138643236366664313034313939626431363431663638323461222C22736F757263654174747269627574696F6E223A5B7B22736F757263654964223A227372632D65353433323364626163616239623935623364306165373632383134333736613937303835613164663630346230323731396465366338316565336234363932222C22636F6E74656E74536861323536223A2265393438396633376662333035316539656661316463393136303034643732373465376236333937356533323039373038393437323637663233393361396265227D5D2C226372697465726961223A5B7B22637269746572696F6E4964223A22616363657074616E6365222C2273746174656D656E74223A225072657365727665206F726967696E616C20726571756573742E222C2265766964656E6365506F70756C6174696F6E223A6E756C6C2C2276616C69646174696F6E5365616D223A22222C2276616C69646174696F6E416374696F6E223A22222C2266616C73696679696E674F62736572766174696F6E223A22222C2265766964656E6365456666656374446570656E64656E63696573223A5B5D2C226D656368616E6963616C45766964656E6365223A6E756C6C7D5D2C226F626C69676174696F6E73223A5B5D2C2270726572657175697369746573223A5B5D2C22726571756972656445666665637473223A5B7B226566666563744964223A227072222C2273746174656D656E74223A224F70656E205052222C226B696E64223A2270756C6C5F72657175657374222C227461726765744272616E6368223A226D61696E227D5D2C22686F7374417373756D7074696F6E73223A5B5D2C22636F6E73747275637465644279223A226D6F64656C5F65787472616374696F6E222C226E6F746573223A22222C2266696E616C4173737572616E63654D6174657269616C73223A6E756C6C7D','model_extraction',NULL,'2026-09-22T18:21:06.9453100+00:00');
INSERT INTO "contract_revisions" VALUES('cr-13e6e6b9fb16d7c05295068f1c6b02586a9d9b241f572def5e625538a40ed510','wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f',1,'13e6e6b9fb16d7c05295068f1c6b02586a9d9b241f572def5e625538a40ed510',X'7B22776F726B556E69744964223A2277752D34656438633830626461313463623530336131393966386138336536316631646463366233626634373733303364373363393366393530336463663435643766222C22736F757263654174747269627574696F6E223A5B7B22736F757263654964223A227372632D34303366396465643435636362323663343236376433633264343835316165363830643639663230663962306331393738616564356438653664336331313166222C22636F6E74656E74536861323536223A2265393438396633376662333035316539656661316463393136303034643732373465376236333937356533323039373038393437323637663233393361396265227D5D2C226372697465726961223A5B7B22637269746572696F6E4964223A22616363657074616E6365222C2273746174656D656E74223A225072657365727665206F726967696E616C20726571756573742E222C2265766964656E6365506F70756C6174696F6E223A6E756C6C2C2276616C69646174696F6E5365616D223A22222C2276616C69646174696F6E416374696F6E223A22222C2266616C73696679696E674F62736572766174696F6E223A22222C2265766964656E6365456666656374446570656E64656E63696573223A5B5D2C226D656368616E6963616C45766964656E6365223A6E756C6C7D5D2C226F626C69676174696F6E73223A5B5D2C2270726572657175697369746573223A5B5D2C22726571756972656445666665637473223A5B7B226566666563744964223A227072222C2273746174656D656E74223A224F70656E205052222C226B696E64223A2270756C6C5F72657175657374222C227461726765744272616E6368223A226D61696E227D5D2C22686F7374417373756D7074696F6E73223A5B5D2C22636F6E73747275637465644279223A226D6F64656C5F65787472616374696F6E222C226E6F746573223A22222C2266696E616C4173737572616E63654D6174657269616C73223A6E756C6C7D','model_extraction',NULL,'2026-09-22T18:21:07.6001396+00:00');
CREATE TABLE contract_sources (
    contract_revision_id TEXT NOT NULL REFERENCES contract_revisions(contract_revision_id),
    source_id TEXT NOT NULL REFERENCES entitled_sources(source_id),
    content_sha256 TEXT NOT NULL,
    PRIMARY KEY (contract_revision_id, source_id)
) STRICT;
INSERT INTO "contract_sources" VALUES('cr-42cf6edb76c14bbe913bfd59427c7db207ebb4cad507433e921ba078dda76cd8','src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be');
INSERT INTO "contract_sources" VALUES('cr-13e6e6b9fb16d7c05295068f1c6b02586a9d9b241f572def5e625538a40ed510','src-403f9ded45ccb26c4267d3c2d4851ae680d69f20f9b0c1978aed5d8e6d3c111f','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be');
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
INSERT INTO "entitled_sources" VALUES('src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','primary_issue','https://github.com/acme/widget/issues/12',X'00FF0D0A','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be','text/plain; charset=utf-8','caller','caller','Retain exact schema-six bytes','2026-09-22T18:21:06.7753222+00:00','2026-09-22T18:21:06.7753222+00:00');
INSERT INTO "entitled_sources" VALUES('src-403f9ded45ccb26c4267d3c2d4851ae680d69f20f9b0c1978aed5d8e6d3c111f','wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f','primary_issue','https://github.com/acme/widget/issues/13',X'00FF0D0A','e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be','text/plain; charset=utf-8','caller','caller','Retain exact schema-six bytes','2026-09-22T18:21:07.5971043+00:00','2026-09-22T18:21:07.5971043+00:00');
CREATE TABLE native_submissions (
    attempt_id TEXT PRIMARY KEY REFERENCES worktree_provisions(attempt_id),
    submission_key TEXT NOT NULL UNIQUE,
    request_json TEXT NOT NULL CHECK (json_valid(request_json)),
    state TEXT NOT NULL CHECK (state IN ('prepared', 'dispatched', 'correlated', 'blocked')),
    run_id TEXT UNIQUE,
    CHECK ((state = 'correlated' AND run_id IS NOT NULL AND length(trim(run_id)) > 0)
        OR (state <> 'correlated' AND run_id IS NULL))
) STRICT;
INSERT INTO "native_submissions" VALUES('at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','broodling:dotnet:v1:at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','{"submissionKey":"broodling:dotnet:v1:at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f","title":"Broodling Attempt at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f","task":"Complete this admitted software-development Work Unit. The frozen Contract and entitled source material below govern scope and acceptance. Candidate edits cannot amend that authority. Implement the criteria and run declared/relevant checks; independently verify the actual outcome. The sole authorized external effect is native pull-request delivery. Do not publish, push, create or update a PR, merge, change issues, deploy, or perform other authoritative effects yourself; the native delivery node alone owns the authorized PR effect.\n\n{\u0022contract\u0022:{\u0022workUnitId\u0022:\u0022wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a\u0022,\u0022sourceAttribution\u0022:[{\u0022sourceId\u0022:\u0022src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692\u0022,\u0022contentSha256\u0022:\u0022e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be\u0022}],\u0022criteria\u0022:[{\u0022criterionId\u0022:\u0022acceptance\u0022,\u0022statement\u0022:\u0022Preserve original request.\u0022,\u0022evidencePopulation\u0022:null,\u0022validationSeam\u0022:\u0022\u0022,\u0022validationAction\u0022:\u0022\u0022,\u0022falsifyingObservation\u0022:\u0022\u0022,\u0022evidenceEffectDependencies\u0022:[],\u0022mechanicalEvidence\u0022:null}],\u0022obligations\u0022:[],\u0022prerequisites\u0022:[],\u0022requiredEffects\u0022:[{\u0022effectId\u0022:\u0022pr\u0022,\u0022statement\u0022:\u0022Open PR\u0022,\u0022kind\u0022:\u0022pull_request\u0022,\u0022targetBranch\u0022:\u0022main\u0022}],\u0022hostAssumptions\u0022:[],\u0022constructedBy\u0022:\u0022model_extraction\u0022,\u0022notes\u0022:\u0022\u0022,\u0022finalAssuranceMaterials\u0022:null},\u0022admittedInstructions\u0022:[{\u0022sourceId\u0022:\u0022src-e54323dbacab9b95b3d0ae762814376a97085a1df604b02719de6c81ee3b4692\u0022,\u0022kind\u0022:\u0022primary_issue\u0022,\u0022locator\u0022:\u0022https://github.com/acme/widget/issues/12\u0022,\u0022mediaType\u0022:\u0022text/plain; charset=utf-8\u0022,\u0022contentSha256\u0022:\u0022e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be\u0022,\u0022encoding\u0022:\u0022base64\u0022,\u0022content\u0022:\u0022AP8NCg==\u0022}],\u0022comparisonBase\u0022:\u002206566e1f25f7d0932ab987cfc642660af34dc3c1\u0022}","preset":{"name":"software-change","delivery":"pull_request"},"runtime":{"harness":"codex","provider":"gateway","model":"gpt-5.6-sol","effort":"medium","size":"small","session_scope":"execution"},"workspace":"/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts/at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f/worktree","repository":"/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/source/.git","branch":"broodling/at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f","startingCommit":"06566e1f25f7d0932ab987cfc642660af34dc3c1","materialSha256":"1258d0e25a6984a5e433338585e89fb0b503a0ba38e6a534f32dde301938f3a1","originUrl":"https://github.com/acme/widget.git","target":{"locator":{"kind":"direct","address":"http://127.0.0.1:8123","sdkVersion":"10.3.0.post1"},"stateDirectory":"/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/native","environment":{"HOME":"","CODEX_HOME":"","LANG":"","LC_ALL":"","SYSTEMROOT":"","TEMP":"","TMP":"","TMPDIR":"","USERPROFILE":"","XDG_CACHE_HOME":"","XDG_CONFIG_HOME":"","PATH":"/home/faviann/.local/lib/node_modules/@openai/codex/node_modules/@openai/codex-linux-x64/vendor/x86_64-unknown-linux-musl/codex-path:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/usr/local/bin:/usr/bin:/bin:/usr/local/games:/usr/games"},"codexProfile":null},"source":{"repository":"acme/widget","branch":"main","revision":"06566e1f25f7d0932ab987cfc642660af34dc3c1"}}','correlated','retained-schema6-run-12');
INSERT INTO "native_submissions" VALUES('at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','broodling:dotnet:v1:at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','{"submissionKey":"broodling:dotnet:v1:at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670","title":"Broodling Attempt at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670","task":"Complete this admitted software-development Work Unit. The frozen Contract and entitled source material below govern scope and acceptance. Candidate edits cannot amend that authority. Implement the criteria and run declared/relevant checks; independently verify the actual outcome. The sole authorized external effect is native pull-request delivery. Do not publish, push, create or update a PR, merge, change issues, deploy, or perform other authoritative effects yourself; the native delivery node alone owns the authorized PR effect.\n\n{\u0022contract\u0022:{\u0022workUnitId\u0022:\u0022wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f\u0022,\u0022sourceAttribution\u0022:[{\u0022sourceId\u0022:\u0022src-403f9ded45ccb26c4267d3c2d4851ae680d69f20f9b0c1978aed5d8e6d3c111f\u0022,\u0022contentSha256\u0022:\u0022e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be\u0022}],\u0022criteria\u0022:[{\u0022criterionId\u0022:\u0022acceptance\u0022,\u0022statement\u0022:\u0022Preserve original request.\u0022,\u0022evidencePopulation\u0022:null,\u0022validationSeam\u0022:\u0022\u0022,\u0022validationAction\u0022:\u0022\u0022,\u0022falsifyingObservation\u0022:\u0022\u0022,\u0022evidenceEffectDependencies\u0022:[],\u0022mechanicalEvidence\u0022:null}],\u0022obligations\u0022:[],\u0022prerequisites\u0022:[],\u0022requiredEffects\u0022:[{\u0022effectId\u0022:\u0022pr\u0022,\u0022statement\u0022:\u0022Open PR\u0022,\u0022kind\u0022:\u0022pull_request\u0022,\u0022targetBranch\u0022:\u0022main\u0022}],\u0022hostAssumptions\u0022:[],\u0022constructedBy\u0022:\u0022model_extraction\u0022,\u0022notes\u0022:\u0022\u0022,\u0022finalAssuranceMaterials\u0022:null},\u0022admittedInstructions\u0022:[{\u0022sourceId\u0022:\u0022src-403f9ded45ccb26c4267d3c2d4851ae680d69f20f9b0c1978aed5d8e6d3c111f\u0022,\u0022kind\u0022:\u0022primary_issue\u0022,\u0022locator\u0022:\u0022https://github.com/acme/widget/issues/13\u0022,\u0022mediaType\u0022:\u0022text/plain; charset=utf-8\u0022,\u0022contentSha256\u0022:\u0022e9489f37fb3051e9efa1dc916004d7274e7b63975e3209708947267f2393a9be\u0022,\u0022encoding\u0022:\u0022base64\u0022,\u0022content\u0022:\u0022AP8NCg==\u0022}],\u0022comparisonBase\u0022:\u002206566e1f25f7d0932ab987cfc642660af34dc3c1\u0022}","preset":{"name":"software-change","delivery":"pull_request"},"runtime":{"harness":"codex","provider":"gateway","model":"gpt-5.6-sol","effort":"medium","size":"small","session_scope":"execution"},"workspace":"/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/attempts/at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670/worktree","repository":"/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/source/.git","branch":"broodling/at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670","startingCommit":"06566e1f25f7d0932ab987cfc642660af34dc3c1","materialSha256":"5616ce4554f3bc5f9232f095f7b3040608865318b5e9c243a04fdbacf5487996","originUrl":"https://github.com/acme/widget.git","target":{"locator":{"kind":"direct","address":"http://127.0.0.1:8123","sdkVersion":"10.3.0.post1"},"stateDirectory":"/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT/native","environment":{"HOME":"","CODEX_HOME":"","LANG":"","LC_ALL":"","SYSTEMROOT":"","TEMP":"","TMP":"","TMPDIR":"","USERPROFILE":"","XDG_CACHE_HOME":"","XDG_CONFIG_HOME":"","PATH":"/home/faviann/.local/lib/node_modules/@openai/codex/node_modules/@openai/codex-linux-x64/vendor/x86_64-unknown-linux-musl/codex-path:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/home/faviann/.local/bin:/home/faviann/.nix-profile/bin:/nix/var/nix/profiles/default/bin:/usr/local/bin:/usr/bin:/bin:/usr/local/games:/usr/games"},"codexProfile":null},"source":{"repository":"acme/widget","branch":"main","revision":"06566e1f25f7d0932ab987cfc642660af34dc3c1"}}','correlated','retained-schema6-run-13');
CREATE TABLE store_metadata (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    format TEXT NOT NULL,
    version INTEGER NOT NULL,
    definition_hash TEXT NOT NULL,
    manifest_hash TEXT NOT NULL,
    initialized_at TEXT NOT NULL
) STRICT;
INSERT INTO "store_metadata" VALUES(1,'broodling.dotnet',7,'1f56d5fa659afe8f91c8bc559b9de248cc1f9f85ced68cf674a27c85002302c6','84bfb92ddf4305e45e4543eb280ba4f36ad6eebc466f1a897ccce63c3ad56fe9','2026-09-22T18:21:06.6804719+00:00');
CREATE TABLE work_submissions (
    submission_id TEXT PRIMARY KEY,
    work_unit_id TEXT NOT NULL REFERENCES work_units(work_unit_id),
    submitted_repository TEXT NOT NULL,
    submitted_issue TEXT NOT NULL,
    received_at TEXT NOT NULL
) STRICT;
INSERT INTO "work_submissions" VALUES('sub-4f4347255b76459c8707bb26a5a285ac','wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','acme/widget','12','2026-09-22T18:21:06.7648300+00:00');
INSERT INTO "work_submissions" VALUES('sub-6dc5bffe4426450a8b0f6df9bd531baf','wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f','acme/widget','13','2026-09-22T18:21:07.5947465+00:00');
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
INSERT INTO "work_units" VALUES('wu-db299ef3d51cccd1060fda22b342db2c32f1e5a8d266fd104199bd1641f6824a','github.com/acme/widget#12','github.com','acme','widget',12,'https://github.com/acme/widget/issues/12',NULL,NULL,'2026-09-22T18:21:06.7618995+00:00');
INSERT INTO "work_units" VALUES('wu-4ed8c80bda14cb503a199f8a83e61f1ddc6b3bf477303d73c93f9503dcf45d7f','github.com/acme/widget#13','github.com','acme','widget',13,'https://github.com/acme/widget/issues/13',NULL,NULL,'2026-09-22T18:21:07.5946212+00:00');
CREATE TABLE worktree_provisions (
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(attempt_id),
    provisioned_at TEXT NOT NULL
) STRICT;
INSERT INTO "worktree_provisions" VALUES('at-e91ea13ddf5efbfa39742e83616e1bc90aebdf6e4ac0ddb941c917bea2d9860f','2026-09-22T18:21:07.3186732+00:00');
INSERT INTO "worktree_provisions" VALUES('at-a42519171d07397d3df9df6e6815e3aa2dc9e43998a43acabd67211d06d5b670','2026-09-22T18:21:07.7469902+00:00');
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
COMMIT;
