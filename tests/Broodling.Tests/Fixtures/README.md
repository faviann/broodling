# Retained .NET state

`dotnet-v1.sql` is a SQLite dump produced on 22 September 2026 by the actual
[#132](https://github.com/faviann/broodling/issues/132) application at `7caa132`:
explicit initialization, one `ResolveWorkUnit` with repository/issue pins, and
one `EntitleSource` with a caller grant and binary bytes `00 ff 0d 0a`.
Its original metadata, schema manifest, identities, timestamps and source
provenance are retained. Restoring this fixture does not use the new schema
builder. The upgrade test checks the complete old fact rows before/after the
explicit upgrade, ordinary-open refusal, and subsequent admission/reopen.

This is .NET-created state, not a Python database or compatibility fixture.

`dotnet-v2.sql` was produced on 22 September 2026 by the actual schema-2
application at `aecb725`, before B's changes: explicit initialization followed by
`AdmitSources` for `acme/widget#12`, caller-granted binary bytes `00 ff 0d 0a`
and a criteria-only admitted Contract. It retains the original schema, metadata,
canonical Contract bytes, source bindings, decision and timestamps. The v2→v3
test checks every prior fact row, ordinary-open refusal, subsequent Attempt
allocation, reopen and repeated explicit upgrade. Fixture restoration disables
foreign keys only while loading the dump's table order; application opens enable
them.

`dotnet-v3.sql` was captured on 22 September 2026 from the actual pre-C
application at reviewed main `3569741388f4f80eac8af0e03c2862c0d0aabae5`, before
changing its schema. The capture restored the retained v2 fixture, explicitly
upgraded with that application's schema 3, admitted an Attempt against a new
local Git repository and abandoned it through the public API. The original
schema/metadata hashes, all prior facts, Attempt/B1/allocation and abandonment
rows are retained. Its capture repository was an owned disposable test fixture
and was removed; upgrade/observation never requires it to exist. The v3→v4 test
checks every old fact row, ordinary-open refusal, null provisioning history,
reopen and repeated upgrade. It does not synthesize v3 using C's schema builder.

`dotnet-v4.sql` was captured on 22 September 2026 by compiling the actual
pre-F application sources at `7ed346ab54aebd3916258c5f30ce22a58a915bb1`
in an isolated temporary build. The public API initialized a new owned test
store, admitted caller-granted binary issue bytes `00 ff 0d 0a` and a Contract,
allocated and provisioned an Attempt in real Git, then abandoned it. SQLite's
dump retains its original metadata, schema, sources, Contract, admission,
B1/allocation, materialization and abandonment. No F schema builder fabricated
these rows. Fixture restoration disables foreign keys only for dump ordering;
ordinary application opens enable them. Tests compare all old fact rows, refuse
ordinary open without changing the file, explicitly upgrade, reopen and repeat.

- Original definition hash: `ff667034acaaf821c1d8b760986fa92f38b2f1cb43dc1f68632470e47582ed05`.
- Original manifest hash: `00e8b937c1ba5f45b82e42ec7713c2ddff741cb358c6c6c22f8e67fc7726561a`.
- SQL fixture SHA-256: `048e1740dab94a56f926d748fe25b5e30b3bc0409464de30baa70de5065672c9`.

`dotnet-v5.sql` was captured on 22 September 2026 from F's actual application at
`05db972a9e68940a48f2b547d938eae36459eec6`, compiled before G changed any source.
Public initialization/admission/provisioning/preparation APIs created an owned
local test store and Git checkout, caller-entitled issue bytes, a PR-authorized
Contract, original B1/allocation, provisioning acknowledgment and prepared frozen
native request. No native dispatch or external effect occurred. The dump retains
original schema, metadata and every fact; G's schema builder did not fabricate
the fixture. Its old filesystem paths are evidence only. Tests compare all old
rows through deliberate upgrade, ordinary-open refusal, reopen and repeated
upgrade, and exercise the new currentness guard.

- Original definition hash: `8e73132b476ca3a6647e720a19a1a9b6d8ef4421db6d3789207e765592623a65`.
- Original manifest hash: `b2ae125490e59a61100a2afd06ff17709f79986b928a80fcf94f573d5ec14a99`.
- SQL fixture SHA-256: `3fe7cf1f409f45ec7314bd65466e1676584b3cccce099b322aa0071dbcb01555`.

`dotnet-v6.sql` is the verbatim root capture of G's actual pre-H public API on
22 September 2026. The assembly was built at
`8f02de76806a73d60177480b79687867ede42fcd`; production was unchanged through
`fdc1db5b6ae64aa0addab085435e8364291098a7`, merged as
`cacc268708d2785bb9344c3bf2793447ccfe0138`. Public APIs initialized the store,
admitted binary issue sources `00 ff 0d 0a` and PR Contracts for `acme/widget#12`
and `#13`, provisioned owned Git worktrees, and dispatched through a controlled
transport. Issue 12 retained receipt-backed completion; issue 13 retained
correlated abandonment. No real native dispatch, provider or external effect
occurred. H's schema builder did not construct this fixture.

The capture helper was `/tmp/broodling-139-v6-capture.mYiojM`; the owned capture
root `/home/faviann/.cache/broodling-tests/139-schema6.ne0ivT` was removed after
capture. Old Git/worktree/native paths are evidence only. The consolidated
Restore helper disables foreign keys only for dump ordering. Upgrade tests
compare every old fact row, refuse ordinary open without modifying the file,
upgrade through schema 7, 8 and 9 to current schema 11, reopen, repeat upgrade and
replay the exact completion without native access. The prepared v5 fixture
above is preserved unchanged.

- Assembly SHA-256: `51dd42d49beb6c88738fa48055e353520e9572c22b35167b7665e03afec9b229`.
- Original definition hash: `ee2c514222e63e6b1f9311b5bad12874255b230bd8e8b2718dfa6320146d875a`.
- Original manifest hash: `9e90c11e420be0274beb506c0dd44702f04dad402e0a945f4d21a68cc6d65881`.
- SQL fixture SHA-256: `b41038ccdf01327e6e71273579762c20870745227910dd37441dda901f580567`.

`dotnet-v7.sql` is an authentic schema-7 dump produced by explicitly upgrading
the retained v6 capture with the application at `d7a8f094f3886b117a6eebbf86d333d5c16bc8f0`
(the pre-#110 `origin/main` application). It retains the v6 facts and schema-7
metadata; the lifecycle test restores this file rather than synthesizing schema
7 with the new builder, refuses ordinary open, applies the recognized schema-8,
schema-9, schema-10 and schema-11 migrations, reopens and repeats the explicit upgrade.

- Original definition hash: `1f56d5fa659afe8f91c8bc559b9de248cc1f9f85ced68cf674a27c85002302c6`.
- Original manifest hash: `84bfb92ddf4305e45e4543eb280ba4f36ad6eebc466f1a897ccce63c3ad56fe9`.
- SQL fixture SHA-256: `b1cad7b0a331b8319859633a3290626848b4206ec3f60d2a9e6d04dca21293d1`.

`dotnet-v8.sql` is an authentic schema-8 dump produced on 23 September 2026
from the merged-main application at `f0b50e0c22ab0a10f27f5698f88653740942b775`.
An isolated build of that exact application restored `dotnet-v7.sql`, performed
the public `UpgradeStore` v7→v8, then called the public `SubmitIssue` API for
`https://github.com/acme/widget/issues/12`. The schema-8 application assembly
SHA-256 was `9ba7a38852b70e389c0b952d9a49ff4fba3405c8e36310274aa845f7ec682966`.
The v8→v10 test restores this dump rather than using the current schema builder;
it checks the retained issue-submission row and prior fact rows before and after
the explicit pause and RequestBundle migrations.

- Original definition hash: `e528411043038cc78363ceca2ef5204dafb311ce0d7133e956f3b68387693f66`.
- Original manifest hash: `8f1d0c4e7cb2e6f16de49efe43c94c3e9dd280304db843e598bd534da71fc33b`.
- SQL fixture SHA-256: `b2e53637995335400f332612e7db6c424ba653aa89b38ec611ebb4f6d8dfd10c`.

`dotnet-v9-paused.sql` is an authentic schema-9 dump generated from the real
pre-#106 application at `eb2e05ad480e1203ef090e0982e9c6ad93ad6fb5` (the #157
head). In an isolated checkout of that commit, the public `UpgradeStore` API
upgraded the retained schema-8 fixture, and the public `PauseInstallation` API
persisted a paused installation. The schema-11 lifecycle test restores this
state, refuses ordinary open without changing it, explicitly upgrades, and
checks the original rows and paused state across reopen.

- Application assembly SHA-256: `45101adc7b54590fc7e475098fe0a3225e82b5a2c0cabc1b4c61a918b4022514`.
- Original definition hash: `800ab31fd9f44ecc443d3549e5eb51cac114c4e94e2e43f47861a36c3ce586cd`.
- Original manifest hash: `a2353ba8b4f7e428e55103d9558387e08bb881558c030d38e69e070031a92683`.
- SQL fixture SHA-256: `1c00cb9dacd780ba2d06fe7730b12bc7ed7c840520f1900cb1ca2cfc45e4b8f3`.

`dotnet-v10.sql` is an authentic schema-10 dump produced on 24 September 2026
by the actual pre-#109 application at `ce9ce0a` (the parent of #109). An
isolated build restored the retained schema-8 fixture, explicitly upgraded it
through schema 9 and 10, and used the public RequestBundle capture API to
retain a completed bundle and source snapshot. The current schema-11 lifecycle
test restores this file rather than using the current schema builder, verifies
the retained RequestBundle facts before upgrade, then upgrades and reopens it.

- Application assembly SHA-256: `dbf8b260212c37d7b3c48965c1d44809a89ee2f2959cb1101c18399613da0b1b`.
- Original definition hash: `f2fa83780445a5e99bb025d7274f8c2d10e649d70d8aea2291f16409ea538d79`.
- Original schema manifest hash: `f64bc8320cd80434da542dfbb331dd89db21aa6968a63d397307190dcd4711b2`.
- Completed RequestBundle manifest hash: `f677bc2aec89e2c2e579058e5c7e4257d5128b8dbd10753c1872ca0b66e7f30a`.
- SQL fixture SHA-256: `95a7c4ae9e3fa9ebded8ad9c367364dd233f4afaccb8c862d7082b1260050f7e`.

The F Python files here are test fixtures only. `receipt-sdk.py` substitutes SDK
constructors/results around the production translator and proves precise PR
receipt transport, not real DirectTarget delivery. `corrupt-submit.py` succeeds
at version probing and corrupts only the submit response. `gateway-profile.py`
materializes the pinned native gateway profile locally without executing work.
`slow-codex` and `inspect-codex` are controlled provider witnesses for waiter
detachment/stop and C# exec policy; they use no provider account.
