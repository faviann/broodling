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

Start with the
[current architecture and operating status](docs/governing/current.md).
Broodling is post-MVP; completed phase plans and qualification campaigns are not
current requirements. The concise [P5 outcome](evaluation/p5/README.md) retains
the scoped **FAIL** that limits use: delivery worked, but the one accepted v6
change was semantically incorrect. The native integration design below supplies
implementation details.

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

For first internal use, follow the [single-host deployment procedure](deployment/README.md).
It installs an immutable Broodling release, persistent local DirectTarget and
operator CLI, with the actual-target dependency check and restart/recovery
instructions. The [deployment validation record](deployment/validation.md)
identifies what was exercised. The Python API remains available below.

Use Python 3.13+, SQLite 3.37+, and Git:

```bash
python -m pip install -e '.[test]'
python -m pytest tests
```

`Broodling` is the caller-facing Python API: submit one explicit GitHub issue
with a typed Contract proposer and exact effect authority, inspect its pinned
lineage, and wait for its receipt-backed disposition. It composes the existing
ingress, admission, workspace, submission and disposition services. See the
[invocation API and recovery behavior](docs/implementation/invocation.md).
The [ingress API](docs/implementation/work-reference-ingress.md) remains available
for admission alone. Comments and referenced documents require separate explicit
entitlement, and proposal generation has no bundled model.

The [#74 first-use decision](https://github.com/faviann/broodling/issues/74)
retains P5's scoped **FAIL**. This is an operator-supervised internal PR-proposal
workflow: every delivered PR requires independent human/operator review of its
exact revision against the frozen request and Contract, with appropriate tests/CI,
before a separate merge decision. `SUCCEEDED` does not certify semantic correctness
or authorize automatic merge, deployment or downstream release.

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

The example below submits an issue already reviewed to require only a candidate
change, local checks and one explicitly authorized PR targeting `main`. Its simple
proposer preserves the whole issue body as a criterion; a general proposer must
also represent unsupported obligations and unresolved prerequisites. The source
checkout must be clean and committed with the matching GitHub origin. Supply an
authenticated `gh` for issue capture and an existing supported DirectTarget with
the GitHub CLI version/capabilities checked by the
[deployment package](deployment/README.md).
Keep the database, runtime state and durable Attempt workspaces outside the source
checkout. The [deployment package](deployment/README.md) provisions this profile.

```python
import asyncio
import json
import os

from broodling import (
    Broodling,
    BroodlingStore,
    Contract,
    Criterion,
    RequiredEffect,
    WorkReference,
    ZeroshotSubmitter,
)


def propose_reviewed_change(inputs):
    primary = next(source for source in inputs.sources if source.kind == "primary_issue")
    return Contract(
        work_unit_id=inputs.work_unit.work_unit_id,
        source_attribution=inputs.source_attribution,
        criteria=(Criterion("request", json.loads(primary.content)["body"]),),
        required_effects=inputs.required_effects,
        constructed_by=inputs.constructed_by,
    )


def execute():
    with BroodlingStore.open("/srv/broodling/state/broodling.sqlite3") as store:
        engine = ZeroshotSubmitter(
            "/srv/broodling/zeroshot",
            delivery_target_origin=os.environ["ZEROSHOT_TARGET_ORIGIN"],
            github_token=os.environ["GH_TOKEN"],
            gateway_base_url=os.environ["GATEWAY_BASE_URL"],
            gateway_api_key=os.environ["GATEWAY_API_KEY"],
        )
        app = Broodling(store, engine, "/srv/broodling/attempts")
        status = app.submit(
            WorkReference.parse("acme/widget", 123),
            propose_reviewed_change,
            repository="/srv/source/repository",
            constructed_by="caller",
            required_effects=(
                RequiredEffect("pr", "Deliver a PR targeting main.", "pull_request", "main"),
            ),
        )
        # Retain these identifiers for status/resume/wait after reopening the store.
        print(status.revision.contract_revision_id, status.decision.outcome)
        if not status.decision.admitted or status.abandonment:
            return status
        print(status.attempt.attempt_id, status.submission.run_id)
        return asyncio.run(app.wait(status.attempt.attempt_id))
```

`app.status(revision_id)` reads retained facts without external calls.
`app.history(reference)` lists recorded revisions, including handles retained
when a submission failed before returning.
`app.resume(revision_id)` continues the same admitted invocation after a restart;
`await app.wait(attempt_id)` consumes its result. `await app.stop(attempt_id, reason)`
abandons authority and requests native stop; dispatched worktrees remain
quarantined. These methods preserve the existing errors and lifecycle decisions.

For no-effect LocalTarget work, explicitly pass `required_effects=()`, omit the
PR target/credential arguments and supply the isolated local `CodexProfile`
described in the [integration design](docs/implementation/zeroshot-native-integration.md).
That profile requires an empty profile home and a separate Codex home containing
only `auth.json`; it retains the stable-result limitation described below.

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
outside the supported profile. They require separately scoped design work and do
not block the selected DirectTarget profile.

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

## Tests and archived history

The default [test suite](tests/README.md) covers Broodling-owned invariants and
the published SDK/native seam using a controlled, non-networked provider
fixture. Gateway policy tests use synthetic credentials and need no real gateway
access. The suite is not a paid-provider or sandbox qualification campaign.

The [current authority](docs/governing/current.md#documentation-authority) names
the small current document set. Superseded governing designs, qualification
harnesses and raw evaluation campaigns are available in Git history, including
the [complete pre-cleanup tree](https://github.com/faviann/broodling/tree/348e1f469c04fecbc24f4088e6eb438a3934e872).
They define only their recorded revisions and profiles. The retained
[P5 summary](evaluation/p5/README.md) carries forward the evidence-backed
limitation relevant to current operation.
