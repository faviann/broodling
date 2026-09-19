# Broodling

Broodling admits one software-change Work Unit, gives its frozen Contract to
Zeroshot, and records the resulting Broodling lifecycle decision.
Scheduling, backlog selection, dependency waiting, and multi-project orchestration
are outside its scope.

Zeroshot owns execution: its standard `software-change` workflow implements,
independently reviews acceptance and code, and repairs. Broodling does not author
an execution graph, supervise provider processes, reconstruct execution history,
or independently re-prove the workflow's result.

## Source of truth

Start with the [current architecture and P5 plan](docs/governing/current.md).
It governs current scope, identifies historical documents and defines the next
phase: native-workflow product evaluation, not the superseded v0.5 assurance
checklist. The [skeptical P5 review](evaluation/p5/reviews/2026-09-19-issue-66/README.md)
is complete with a scoped **FAIL** verdict. V6 established the compatible delivery
boundary but accepted an incorrect T1 result; v5 remains separately blocked by
infrastructure failure. R02–R08 remain unstarted in both cohorts. No broader
release decision is inferred. The native integration design below supplies
implementation details; old gate passes do not qualify this profile.

## Current boundary

Broodling owns immutable source/Contract admission, one current Attempt and its
dedicated worktree, durable Attempt-to-run correlation, and the final lifecycle
decision. The frozen Work Unit selects delivery: an empty effect set submits
`Preset("software-change", delivery="none")`; exactly one authorized
`pull_request` effect naming its target branch submits native PR delivery. No
other effect is inferred. Broodling consumes `Run.wait()`, including an
already-completed run after reconnection, without a separate mechanical-evidence
or adjudication record.

A source-attributed Contract can be admitted with acceptance criteria alone.
A finite evidence population, validation seam/action, and falsifying observation
are optional guidance, not a required prewritten validation plan. Zeroshot decides
how to implement and validate the change. Unsupported required effects,
effect-dependent evidence, and unsatisfied prerequisites still fail admission.

The supported target is single-host Linux x86-64 with **Zeroshot 10.3.0** and its
matching **Python SDK 10.3.0.post1**. See the
[current integration design](docs/implementation/zeroshot-native-integration.md)
for the responsibility boundary, release sources, migration, and limitations.

No authoritative effect is implicit. Unsupported, mixed, or multiple effects are
rejected. No-effect local runs use a small Codex launcher with explicit sandbox/network
policy that disables apps/plugins/hooks/notifications, excludes ambient Codex user
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

`ContractIngress.from_github` now supplies the front half: acquire one explicit
issue, freeze its exact source bytes, validate caller effect authority in a typed
Contract from a caller-supplied proposer, and record deterministic admission.
See the [ingress API and example](docs/implementation/work-reference-ingress.md).
It stops at admission; no Attempt is created. Comments and referenced documents
require separate explicit entitlement, and proposal generation has no bundled
model. The [#74 first-use decision](https://github.com/faviann/broodling/issues/74)
retains P5's scoped FAIL and requires independent operator review of every
subsequently delivered PR before a separate merge decision.

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

The example below executes a Contract already recorded and admitted with exactly
one authorized pull_request effect naming its target branch. It starts from a
clean committed source repository with a GitHub origin. The host
supplies an empty profile home and a separate Codex home
containing only `auth.json`. Keep the database, runtime state, profiles, and
durable Attempt workspaces outside the source checkout; preserve runtime state
for reconnecting to the run.

```python
import asyncio
import os
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
            # Required only when this Contract authorizes pull_request delivery.
            delivery_target_origin="https://zeroshot.example.internal",
            github_token=os.environ.get("GH_TOKEN"),
            gateway_base_url=os.environ.get("GATEWAY_BASE_URL"),
            gateway_api_key=os.environ.get("GATEWAY_API_KEY"),
        )
        SubmissionCoordinator(store, engine).submit(attempt.attempt_id)
        return asyncio.run(
            WorkUnitDispositionCoordinator(store, engine).finalize(attempt.attempt_id)
        )
```

For no-effect LocalTarget work, omit `delivery_target_origin`, `github_token`, and
both gateway arguments; keep the isolated local Codex/OpenAI profile. This does
not remove the no-effect stable result limitation described below.

For an authorized PR, Zeroshot runs against the explicit repository, target
branch, and B1 revision on the configured direct target. PR delivery is admitted
only for a GitHub Work Unit. Its successful PR
receipt is retained verbatim, and `headRevision` is the stable accepted result.
Set `GATEWAY_BASE_URL` to exactly `https://cliproxy.local.faviann.com/v1` and supply a
current nonempty `GATEWAY_API_KEY` and `GH_TOKEN`. These explicit inputs are checked
on every initial or replayed dispatch and passed only in the SDK environment;
their values are not persisted in the invocation. Zeroshot selects the two
gateway fields for its `gateway` connection and reserves `GH_TOKEN` for source
checkout and native delivery. No `OPENAI_API_KEY` is sent. Missing gateway fields,
a different gateway URL, or conflicting provider credentials in the dispatch
environment fail closed. The CLIProxyAPI endpoint is separate from the configured
Zeroshot DirectTarget origin. Cancelling a wait only detaches; waiting again can
consume the same result without dispatch credentials or gateway configuration.

The deliberately simple V1 authorized-PR profile uses Zeroshot's standard
`software-change` workflow, this supported DirectTarget path, Codex, the `gateway`
provider through CLIProxyAPI, `gpt-5.6-sol`, medium reasoning effort, and one
`UniformRuntime` across the workflow. Harness/model selection, per-node runtimes,
skill/tool capability profiles, fleet orchestration, and node-local OAuth are
post-V1 concerns. The
[issue #72 dependency finding](docs/implementation/zeroshot-node-local-authorized-pr-audit.md)
remains the source-backed starting point for a future node-local OAuth profile;
it does not block the selected V1 DirectTarget profile.

For a no-effect Work Unit, Zeroshot 10.3 returns null and leaves only a mutable
local worktree. Broodling may run that no-effect workflow, but it fails closed at
final disposition because no stable accepted result exists. It does not silently
open a PR, manufacture a post-run snapshot, or supervise writers. This is an
explicit Zeroshot capability gap pending a supported local result delivery.

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
fixture. Gateway policy tests use synthetic credentials and need no real gateway
access. The suite is not a paid-provider or sandbox qualification campaign.

The [current authority](docs/governing/current.md#documentation-authority)
classifies current and historical documents explicitly. The
[source audit](docs/implementation/zeroshot-current-source-audit.md) and
[result-handoff audit](docs/implementation/zeroshot-result-handoff-audit.md)
record the dependency investigation leading to the adopted native integration.
[Qualification records](qualification/README.md),
[historical governing plans](docs/governing/broodling-implementation-dependency-plan-v0.5.md), and the
[baseline inventory](docs/baseline/p0-g0-inventory.md) remain historical evidence
for their recorded versions and profiles; they do not qualify this integration.
