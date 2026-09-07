# Issue #14 P2 fixtures

`submission_support.py` supplies an opaque, caller-owned GraphSpec with a single
`succeed` root and a RuntimePlan with no executable nodes. Tests exercise real
SDK encoding/preflight, sidecar admission, submission replay and conflicts, with
no provider, delivery or assurance graph.

The stronger post-acceptance crash witness uses a two-child sequence: one
mutating `step`, then `succeed`. `mutator-bin/codex` is a deterministic executable
implementing just the Codex JSON-lines protocol needed by that step; it calls no
provider. It waits for a filesystem gate, edits README.md and makes a local
candidate commit on the Attempt's disposable branch. The parent opens the gate
only after the Broodling child dies with `os._exit(97)`, before correlation. The
run therefore advances its own worktree after the caller process is gone.

That local candidate commit exercises resolved-source drift, not commit-as-
delivery. The fixture neither invokes GitDelivery nor pushes; it grants no
external effect capability. PATH selects this executable explicitly and is part
of the persisted request identity. This is not the W3 assurance graph and proves
no assurance or final-result semantics.

Tests use Git reads and fixture markers to synchronize the mutation. They never
call runtime status/history APIs or read private Zeroshot state. `/dev/shm` holds
only disposable test controller sockets/state; Attempt worktrees still live under
the qualified durable root. Production must retain the same runtime state across
replay; these fixtures remove their own resources after testing.
