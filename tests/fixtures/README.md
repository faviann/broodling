# Controlled native-workflow provider

`software-change-codex` is a deterministic executable implementing the small
Codex JSON-lines protocol needed by these integration tests. The official
Python SDK and bundled Rust engine run the standard `software-change` preset;
only the provider is replaced.

The fixture returns an empty worker response and accepted read-only verifier
responses. When the frozen task contains `BROODLING_TEST_WRITE`, its worker
writes known candidate bytes to `README.md`. It makes no provider calls, uses
no real credentials, and performs no delivery. Its responses let tests exercise
Broodling submission/correlation, completed-result consumption and mutable
candidate changes through the public native seam. A null no-effect result is
refused for successful disposition; this fixture does not produce a stable
accepted candidate or a real PR receipt.

This is deliberately not an independent implementation of review, repair, or
graph semantics. Tests do not inspect Zeroshot's private ledger or demand a
specific execution trace. A successful fixture run does not qualify real-provider
quality, sandbox strength, or physical cessation.

Fixture runtime state/sockets are disposable and use `/dev/shm`; candidate
worktrees use the durable test root. Historical custom-graph/evidence/supervisor
fixtures were removed with those Broodling responsibilities. Their recorded
qualification evidence remains in [qualification](../../qualification/README.md)
at its original version.
