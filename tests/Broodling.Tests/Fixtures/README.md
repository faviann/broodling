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

The F Python files here are test fixtures only. `receipt-sdk.py` substitutes SDK
constructors/results around the production translator and proves precise PR
receipt transport, not real DirectTarget delivery. `corrupt-submit.py` succeeds
at version probing and corrupts only the submit response. `gateway-profile.py`
materializes the pinned native gateway profile locally without executing work.
`slow-codex` and `inspect-codex` are controlled provider witnesses for waiter
detachment/stop and C# exec policy; they use no provider account.
