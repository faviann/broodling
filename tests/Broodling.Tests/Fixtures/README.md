# Retained .NET v1 state

`dotnet-v1.sql` is a SQLite dump produced on 22 September 2026 by the actual
[#132](https://github.com/faviann/broodling/issues/132) application at `7caa132`:
explicit initialization, one `ResolveWorkUnit` with repository/issue pins, and
one `EntitleSource` with a caller grant and binary bytes `00 ff 0d 0a`.
Its original metadata, schema manifest, identities, timestamps and source
provenance are retained. Restoring this fixture does not use the new schema
builder. The upgrade test checks the complete old fact rows before/after the
explicit upgrade, ordinary-open refusal, and subsequent admission/reopen.

This is .NET-created state, not a Python database or compatibility fixture.
