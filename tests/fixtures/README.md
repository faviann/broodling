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
fixtures and qualification campaigns are available only in Git history.

## Controlled stock DirectTarget

`stock-target` layers a controlled provider and forge over the actual
DirectTarget image. The pinned native binary, its HTTP/OECP server and the
approved execution asset are unchanged. The layer replaces only the hosted
target's fixed dependency paths:

- `codex` answers each node from its response schema. Its worker writes known
  candidate bytes to `README.md`, verifiers accept, and a corrected reply
  resumes the requested thread.
- `git` maps `https://github.com/acme/widget.git` to a mounted host bare
  repository and marks only that repository as a safe directory, because the
  target's isolated identities do not own it. Native clears the Git environment
  and global configuration, so this shim is the only way to add either setting.
- `gh` implements only the `gh api` calls native pull-request delivery makes:
  pull-request list/create, branch references and the policy query. It reports
  an open PR with no required checks.

A successful run produces a controlled PR receipt, not a real GitHub PR or
semantic-quality result. Credentials are fixed fake values and no fixture makes
a network call.

