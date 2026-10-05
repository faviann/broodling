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

# Schema-1 state from the revision before #233

`application-v1-local-target.sql` is a `broodling.application` version-1 dump
produced on 5 October 2026 by the actual application at `67c7ac9`, the parent
of the #233 change, built in Release: `initialize-store` and nothing else. Its
schema still defines the structures that #233 removed in place
(`worktree_provisions`, the `attempts` allocation columns and `resource_kind`,
the `bridge` submission format and the `blocked` state), so its definition and
manifest hashes differ from the current schema 1. `StoreLifecycleTests` restores
it and checks that ordinary open and explicit upgrade refuse it with
`incompatible_store`, and initialization refuses its path with `store_exists`,
without changing any file. No upgrade exists; nothing
had been deployed. Python's `sqlite3.Connection.iterdump` produced the dump.

- Application assembly SHA-256: `3ee1fa9604eaa955f0f4eec097100955635b0a79df3038044346c703c68bc005`.
- Original definition hash: `2ef2d8752c19220da9dfaa8800520032ce0982a76de5c59b599884a5dc0d1d60`.
- Original manifest hash: `95a34c26df246c160787dcf2c908ade68acdac019628cbfeb243834cce8ad0a5`.
- SQL fixture SHA-256: `301876f4ccb5cb30c25ead305725dba7a951afda37b37f5ceac4292b5c236474`.
