# Retained pre-transition .NET state

The `dotnet-v*.sql` dumps are authentic state from earlier `broodling.dotnet`
applications. The HTTP integration requires fresh `broodling.application`
state, so `StoreLifecycleTests` restores each dump and checks that ordinary
open, explicit upgrade and initialization refuse it without changing any file.
The schema-12 case is also checked with its last write held only in an
uncheckpointed WAL. None of these is a Python database or compatibility
fixture. Schema-2 to schema-9 captures were removed with the upgrade path and
remain in Git history.

`dotnet-v1.sql` is a SQLite dump produced on 22 September 2026 by the actual
[#132](https://github.com/faviann/broodling/issues/132) application at `7caa132`:
explicit initialization, one `ResolveWorkUnit` with repository/issue pins, and
one `EntitleSource` with a caller grant and binary bytes `00 ff 0d 0a`.
Its original metadata, schema manifest, identities, timestamps and source
provenance are retained. Fixture restoration disables foreign keys only while
loading the dump's table order.

`dotnet-v10.sql` is an authentic schema-10 dump produced on 24 September 2026
by the actual pre-#109 application at `ce9ce0a` (the parent of #109). An
isolated build restored the retained schema-8 fixture, explicitly upgraded it
through schema 9 and 10, and used the public RequestBundle capture API to
retain a completed bundle and source snapshot. The lifecycle
test restores this file rather than using the current schema builder.

- Application assembly SHA-256: `dbf8b260212c37d7b3c48965c1d44809a89ee2f2959cb1101c18399613da0b1b`.
- Original definition hash: `f2fa83780445a5e99bb025d7274f8c2d10e649d70d8aea2291f16409ea538d79`.
- Original schema manifest hash: `f64bc8320cd80434da542dfbb331dd89db21aa6968a63d397307190dcd4711b2`.
- Completed RequestBundle manifest hash: `f677bc2aec89e2c2e579058e5c7e4257d5128b8dbd10753c1872ca0b66e7f30a`.
- SQL fixture SHA-256: `95a7c4ae9e3fa9ebded8ad9c367364dd233f4afaccb8c862d7082b1260050f7e`.

`dotnet-v12.sql` is the current pre-transition baseline: an authentic
schema-12 dump produced on 25 September 2026 by the actual application at
`1aec6c830c5caaa3988ae8d413bb62df2ec5afaa` (merged #107), built in Release
before any #171 change. Its public API initialized a new store, admitted a
caller-sourced Contract for `acme/widget#12`, submitted issue 12 and completed
its RequestBundle capture, submitted and cancelled issue 13 without an Attempt,
and persisted an installation pause. Python's `sqlite3.Connection.iterdump`
produced the dump; restoring it reproduces the original schema and every row.

- Application assembly SHA-256: `241b56fa10d2dc2cc607c931bcfbf63ab556b47d23625139b8260207b5754fea`.
- Original definition hash: `b4f859fcb7847afc686bb46a72af1c6b1f80fedfd6f6fc76909fe1274f371a95`.
- Original manifest hash: `06e016bef175105c38b3329629e46cdc1aabca4c44ab94abbf143c53e9178f5e`.
- SQL fixture SHA-256: `927458271c7171d3d84da1f37c08d4dcd08671610584b6a1ab4bc30b86c07f21`.

# Current-format state from an earlier application

`application-v1-unbound-association.sql` is a `broodling.application` version-1
dump produced on 25 September 2026 by the actual application at `aaa444a`, the
parent of #111, built in Release. #111 leaves the schema definition unchanged.
#118 later added the empty `completion_refusals` table and its three triggers to
the in-place version-1 definition. To keep this state openable, only its schema
identity was advanced: the four definitions were copied verbatim from a store
initialized by the #118 application and appended before `COMMIT`, and the stored
definition and manifest hashes were replaced with that store's values. The
restored schema then equals a freshly initialized store's schema exactly. No row
changed. #112 likewise added the empty `contract_proposal_refusals` table and its
four triggers. The same procedure appended those five definitions verbatim from a
store initialized by the #112 application and replaced both hashes again. #123
changed the `retry_requires_retirement` trigger in place; that one definition was
replaced verbatim from a store initialized by the #123 application, with both
hashes replaced again. #124 added `unchanged` to the `issue_submissions` state
check, changed the `attempts_no_completed_work`, `attempts_no_abandoned_work` and
`retry_requires_retirement` triggers, and added the empty
`issue_submission_revisions` and `issue_submission_unchanged` tables with their
seven triggers and the `revision_prior_contracts` and `superseded_contracts`
views. Those four definitions were replaced and the eleven new ones appended verbatim from a store initialized by the #124 application, with
both hashes replaced again; no row changed. Its public API
initialized a new store and, for `acme/widget#12` and `#13`, completed a
RequestBundle through the checkpoint API. It then associated each submission
with a pull-request Contract that has no RequestBundle binding: admitted
through `AdmitSources` for issue 12, and recorded with `RecordContractRevision`
but undecided for issue 13. `RequestAdmissionTests` restores it to check that
both associations stay readable but acquire no admission or Attempt authority.
Python's `sqlite3.Connection.iterdump` produced the dump.

- Application assembly SHA-256: `ced6bc100eed4d14b459636b3733ef3f76bc63ea283b2ba61875581e541217c6`.
- Original definition hash: `0488df48ef9ba6a74962fcb61977d6d9b937217534b499b2d630c844c7c3dcda`.
- Original manifest hash: `6c25204951751c5f22730d5125ae749d19edaf237d9fe0647746ffa7c880e4af`.
- Original SQL fixture SHA-256: `3aac2be573e36e88b9215c1932cbd906dd5d56dfbd62c4d1ae37b79b86484e67`.
- #118 definition hash: `8c5cd46fc733cada63fc2d3a1a8bee180d0d802fa9e803d50824c7fa195903b3`.
- #118 manifest hash: `1c70f7d77923da3d13b54c3fa8ea72c78d03f522612fad14e5080b2fbbb79a9c`.
- #118 SQL fixture SHA-256: `dfcefc8ec4f4241147b29c6d8ae4be913f97c4a1095c9ae325c54eb1b5f1dcb7`.
- #112 definition hash: `dd6895ba373a28777d12008f80e4ef1f8858e8bb8cde57f13adea2a8d8fbd853`.
- #112 manifest hash: `fa5ce4fd0fe07dfdb2e992f0a39debb89d3b70fca5804d39c51192e3800c00b0`.
- #112 SQL fixture SHA-256: `a99df9dc1a174adcca2fa812e9d0c3b8b1bae80c618c63f6c6964a7d44ba6fe2`.
- #123 definition hash: `4b64e2c4fd607657311af55e19ee528be25904b127e564594caff69ea6e1ba01`.
- #123 manifest hash: `3b1c6fa693cb4f4d01fea75f520bf26b56e052d88e685abf6c8a4fe8c56bc89f`.
- #123 SQL fixture SHA-256: `31e75b81ad6db8aad30515fc0ea558ef6177906e3eebef5da879ed265b151b47`.
- Current definition hash: `3a414aa75ea9dba6e051cc4a6eb68b49059b9f7c1c29f56d10d3744b3b06d799`.
- Current manifest hash: `1732a43b3b5a9e7f8a2738cc8071cd7baf16d68636ce80b06d5839112f0e53eb`.
- Current SQL fixture SHA-256: `7b5684de2a1f0c6204411f967bd843104a5ad481d57b58e0336950135757741e`.

# Other fixtures

The Python file here is a test fixture only. `corrupt-submit.py` succeeds at
version probing and corrupts only the LocalTarget bridge's submit response.
`slow-codex` and `inspect-codex` are controlled provider witnesses for waiter
detachment/stop and C# exec policy; they use no provider account.
