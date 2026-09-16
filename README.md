# Broodling

Broodling admits one software-change Work Unit, gives its frozen Contract to
Zeroshot, and records the resulting Broodling lifecycle decision.
Scheduling, backlog selection, dependency waiting, and multi-project orchestration
are outside its scope.

Zeroshot owns execution: its standard `software-change` workflow implements,
independently reviews acceptance and code, and repairs. Broodling does not author
an execution graph, supervise provider processes, reconstruct execution history,
or independently re-prove the workflow's result.

## Current boundary

Broodling owns immutable source/Contract admission, one current Attempt and its
dedicated worktree, durable Attempt-to-run correlation, and the final no-effect
disposition. It submits `Preset("software-change", delivery="none")` and consumes
`Run.wait()`, including an already-completed run after reconnection. A successful
native workflow is sufficient; no separate mechanical-evidence or adjudication
record is required.

A source-attributed Contract can be admitted with acceptance criteria alone.
A finite evidence population, validation seam/action, and falsifying observation
are optional guidance, not a required prewritten validation plan. Zeroshot decides
how to implement and validate the change. Required effects, effect-dependent
evidence, and unsatisfied prerequisites still fail admission.

The supported target is single-host Linux x86-64 with **Zeroshot 10.3.0** and its
matching **Python SDK 10.3.0.post1**. See the
[current integration design](docs/implementation/zeroshot-native-integration.md)
for the responsibility boundary, release sources, migration, and limitations.

No authoritative effects are supported: a Contract requiring one is rejected.
Delivery is disabled, and a small Codex launcher applies explicit sandbox/network
policy, disables apps/plugins/hooks/notifications, excludes ambient Codex user
configuration and exec-policy rules, and uses isolated homes. Zeroshot still owns
provider sessions, execution, and stop behavior. Its workflow can read current
repository guidance; that context cannot
amend Broodling's frozen Contract or entitled source snapshots.

## Install and use

Use Python 3.13+, SQLite 3.37+, and Git:

```bash
python -m pip install -e '.[test]'
python -m pytest tests
```

The dependency is pinned to the official Linux x86-64 SDK release wheel,
including its SHA-256 digest; the wheel bundles the matching native engine.
Execution also requires the selected Codex CLI `0.153.4` and host-provisioned
authentication. Broodling neither installs Codex nor copies credentials.

This local no-effect profile requires a trusted host with no operator-managed
effect-capable MCP or extension configuration. Administrators can enforce an
empty `[mcp_servers]` allowlist in `requirements.toml`. Broodling does not scan or
override arbitrary managed configuration; this is a supported-host precondition,
not a universal no-effect proof. See the
[local policy limitation](docs/implementation/zeroshot-native-integration.md#local-policy-and-cleanup-limitation).

The example below starts with an already recorded and admitted Contract
revision and a clean committed source repository with a GitHub origin. The host
supplies an empty profile home and a separate Codex home
containing only `auth.json`. Keep the database, runtime state, profiles, and
durable Attempt workspaces outside the source checkout; preserve runtime state
for reconnecting to the run.

```python
import asyncio
from pathlib import Path

from broodling import (
    AttemptProvisioner,
    BroodlingStore,
    CodexProfile,
    SubmissionCoordinator,
    WorkUnitDispositionCoordinator,
    ZeroshotSubmitter,
)


def execute(admitted_revision_id: str):
    with BroodlingStore.open("/srv/broodling/state/broodling.sqlite3") as store:
        attempt = AttemptProvisioner(
            store, "/srv/broodling/attempts"
        ).admit_and_provision(
            admitted_revision_id, "/srv/source/repository"
        ).attempt

        engine = ZeroshotSubmitter(
            "/srv/broodling/zeroshot",
            codex_profile=CodexProfile(
                Path("/opt/codex/bin/codex"),
                Path("/srv/broodling/profile-home"),
                Path("/srv/broodling/codex-auth"),
            ),
        )
        SubmissionCoordinator(store, engine).submit(attempt.attempt_id)
        return asyncio.run(
            WorkUnitDispositionCoordinator(store, engine).finalize(attempt.attempt_id)
        )
```

The complete candidate stays in its owned worktree. A frozen
`finalAssuranceMaterials` selection may additionally retain requested candidate
and B1 bytes; this is result retention, not independent execution evidence.
Cancelling a wait only detaches; waiting again can consume the same result.

## Cleanup limitation

The native local target exposes no general physical-cessation receipt for
escaped descendants or controller loss. Broodling requests native stop and
abandons the Attempt, but **does not authorize automatic deletion or retry for
Attempts dispatched under this integration**. Even a terminal label is not cleanup
authority. Its worktree remains quarantined; manual recovery needs an independently
established safe host boundary. Broodling provides no operator override that silently grants that
authority. An Attempt that never dispatched can still be safely retired and
explicitly replaced from its original B1.

Already-completed historical retirements remain recorded facts; unfinished old
cessation proof cannot authorize new cleanup.

## Tests and history

The default [test suite](tests/README.md) covers Broodling-owned invariants and
the published SDK/native seam using a controlled, non-networked provider
fixture. It is not a paid-provider or sandbox qualification campaign.

This design supersedes the execution/proof requirements in the older governing
documents. The [source audit](docs/implementation/zeroshot-current-source-audit.md)
explains the current dependency investigation.
[Qualification records](qualification/README.md),
[older governing documents](docs/governing), and the
[baseline inventory](docs/baseline/p0-g0-inventory.md) remain historical evidence
for their recorded versions and profiles; they do not qualify this integration.
