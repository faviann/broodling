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
