# Zeroshot result-handoff audit

Investigated 16 September 2026 against the current stable Zeroshot 10.3.0
source at immutable commit
[`054ad3fd6c763b98d12f5b2e90830b97116561ad`](https://github.com/the-open-engine/zeroshot/tree/054ad3fd6c763b98d12f5b2e90830b97116561ad).
**Status: completed research adopted by PR #50.** The conditional PR handoff
recommended below is implemented in the [native integration](zeroshot-native-integration.md).
Read the [current architecture and P5 plan](../governing/current.md) for governing
scope and next work. This followed the initial no-Git-delivery proposal in the
[source audit](zeroshot-current-source-audit.md), not an outstanding alternative
design. It does not amend historical qualification or evidence. Descriptions of
upstream merge delivery below do not grant Broodling merge support.

## Finding

Zeroshot has a reliable, content-addressed handoff for the standard
`software-change` workflow only when GitHub delivery is enabled. Pull-request
delivery returns the exact committed and pushed review-head revision; merge
delivery additionally returns the authoritative merged revision. Both modes
are external authoritative effects.

There is no supported local commit, local branch, archive, or artifact delivery
mode in 10.3.0. The public delivery selector is closed to `none`,
`pull_request`, and `merge`. With `none`, an accepted software-change run returns
JSON null and leaves the candidate as mutable files in the supplied workspace.
Consequently, **no current supported mode simultaneously provides a stable
Zeroshot-produced candidate and preserves a no-effect Work Unit's
no-authoritative-external-effects requirement**.

Broodling should not bridge that gap by watching processes or by racing to
create its own commit after a no-delivery terminal result. Such a commit would
not itself be the result accepted by Zeroshot, and independently excluding a
write between acceptance and capture would recreate the supervision problem
this integration is meant to remove.

The design consequence is conditional, not a blanket grant to deliver. The
Work Unit's frozen authorization must select the native graph before submission:

| Authorized Work Unit result/delivery | Zeroshot preset | Successful handoff |
| --- | --- | --- |
| Open or update a GitHub pull request | `Preset("software-change", delivery="pull_request")` | Retain and validate the returned PR receipt; use `headRevision` as the stable candidate identity |
| No authoritative external effect | `Preset("software-change", delivery="none")` | No successful Broodling disposition on 10.3; do not manufacture a commit, snapshot, or PR |

The second row is a known Zeroshot 10.3.0 capability gap when the Work Unit also
needs a stable accepted local candidate. It should fail closed at the product
boundary rather than silently widen authority or add result-capture machinery
to Broodling.

## Public Python result and delivery surface

The Python selector is:

```python
@dataclass(frozen=True, slots=True)
class Preset:
    name: str
    delivery: str = field(default="none", kw_only=True)
```

Its documented delivery values are `none`, `pull_request`, and `merge`.
Terminal results have this public shape:

```python
@dataclass(frozen=True, slots=True, kw_only=True)
class RunResult:
    run_id: str
    succeeded: bool
    output: JsonValue = None
    failure: str | None = None
```

Sources: [Python `Preset` and target types](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/runtime.py),
[Python result types](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/runs.py),
[SDK submission and reconnection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py).

The SDK does not project a delivery receipt into another Python dataclass:
`RunResult.output` remains a JSON value whose exact object contract is authored
and checked by the native graph. `RunStatus.source.revision` is the immutable
submitted source revision, not the resulting candidate revision, so it cannot
substitute for a delivery receipt.

The built-in catalog itself uses a closed `TemplateDelivery` enum containing
only `None`, `PullRequest`, and `Merge`. Delivery is valid only for
`software-change`, and either effectful mode adds a native Git-delivery binding
requiring `GH_TOKEN`. Source: [built-in template catalog](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_templates/catalog.rs).

## Exact conditional submission configuration

The public Python value is `delivery="pull_request"`; `"pr"` is only the
successful receipt's `mode` value and is not a valid SDK selector. Broodling
should derive that selector from the frozen Work Unit authorization, not from a
process-wide default. The SDK passes the selected preset unchanged to native
template materialization. Sources: [Python preset and target types](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/runtime.py#L14-L68),
[SDK native arguments](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L360-L407).

For Broodling's no-effect `LocalTarget(workspace=...)` integration, do not pass
`repository`, `branch`, or `revision`: native rejects those overrides without a
named target. That target therefore remains suitable only for the no-delivery
workflow whose source is the assigned local worktree.

For an authorized pull request, Broodling uses a named `DirectTarget` and passes
the frozen `repository`, target `branch`, and B1 `revision`. This is not an
execution workaround: those are the public source selectors supported by named
targets. It avoids mutating Broodling's deliberately synthetic Attempt branch
into the PR base, and lets Zeroshot own its checkout, workflow, commit, push, and
receipt as one delivery operation.

Only for `pull_request`, Broodling supplies `OPENAI_API_KEY` and `GH_TOKEN`
through the SDK's environment value source. With an explicit
`Client(environment=...)` mapping, that mapping is complete. The default OpenAI
runtime declares `openai: [OPENAI_API_KEY]`, while the template adds `GH_TOKEN`
only to its native Git-delivery binding. DirectTarget has no managed connection
store or dynamic resolver; its submission therefore forwards these exact
ephemeral values. Broodling requires each current value to be nonblank and at
most 4,096 bytes before dispatch.

Sources: [local source snapshot](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs#L70-L135),
[local delivery target binding](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs#L248-L269),
[local override rejection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cli/parser/convert.rs#L359-L379),
[SDK environment semantics](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L91-L116),
[template-owned credential binding](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_templates/catalog.rs#L57-L72),
[delivery authorization](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/adapter.rs#L205-L225).

Named `DirectTarget` or `HostedTarget` runs support `repository="owner/name"`,
`branch="target-branch"`, and optionally an exact `revision` as submission
arguments; otherwise Zeroshot resolves repository and branch from
the invoking worktree. `GH_TOKEN` is also used for named-target source checkout.
Broodling requires a named target for authorized PR work because its linked
worktree is intentionally attached to a synthetic Attempt branch, not the
authorized PR base. Named source arguments preserve that separation without
branch manipulation or a second local checkout. Sources: [Python run options](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L155-L204),
[target source selection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/docs/concepts/targets.md#L70-L101).

## What each standard workflow returns

| Delivery | Successful `RunResult.output` | Stable candidate identity | Effects |
| --- | --- | --- | --- |
| `none` | `None` (JSON null) | None; the accepted files remain in the supplied mutable workspace | No native GitHub delivery |
| `pull_request` | Receipt object with `version`, `mode`, `outcome`, `repository`, `targetBranch`, `headRevision`, and `pullRequestId` | `headRevision`, a 40-character Git commit OID for the exact review head | Commits all workspace changes, pushes a deterministic run branch, and creates or updates a GitHub PR |
| `merge` | The same identity plus `mergeRevision` | `headRevision` identifies the delivery-approved PR head; `mergeRevision` identifies the authoritative integrated result | The PR effects above, CI/merge-policy observation, and a GitHub merge request |

The current successful receipt forms are therefore equivalent to:

```json
{
  "version": "v1",
  "mode": "pr",
  "outcome": "opened",
  "repository": "owner/repository",
  "targetBranch": "main",
  "headRevision": "<40-character Git OID>",
  "pullRequestId": "<GitHub PR number>"
}
```

and:

```json
{
  "version": "v2",
  "mode": "merge",
  "outcome": "merged",
  "repository": "owner/repository",
  "targetBranch": "main",
  "headRevision": "<reviewed 40-character Git OID>",
  "mergeRevision": "<merged 40-character Git OID>",
  "pullRequestId": "<GitHub PR number>"
}
```

The native result contract rejects unknown fields, checks the receipt's mode and
success outcome, validates both revisions where applicable, requires the head
revision to differ from the admitted base, and binds repository and target
branch to the delivery target. Source: [delivery receipt contract](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/contract.rs).

Native-v2 does not offer an artifact fallback: it rejects node outcomes carrying
artifact references rather than persisting artifact bytes or receipts. Sources:
[native-v2 ledger restriction](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger/state.rs),
[data-plane limitation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/docs/reference/cluster/data-plane.md).

## Acceptance and delivery ordering

The standard workflow first runs the implementation worker, then runs acceptance
and code verifiers in parallel. Only when both emit `accepted` does the graph
either succeed with null output (`none`) or dispatch the delivery node. Git
delivery is therefore part of the graph after acceptance, not an unrecorded
post-run hook. Source: [standard software-change graph](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_templates.rs).

The delivery node runs `git add --all`, commits pending changes with hooks
disabled, requires the resulting HEAD to be a non-base descendant, pushes a
deterministic branch derived from the run ID, and creates or rediscovers the
GitHub review. Sources: [Git revision preparation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/git.rs),
[delivery adapter](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/adapter.rs).

If delivery reports a recoverable Git, push, CI, or merge-conflict problem, the
standard graph runs its delivery-repair worker and then sends the resulting
workspace through acceptance and code review again before another delivery
attempt. Thus a successful receipt does not silently identify unreviewed repair
edits. Source: [delivery repair routing tests](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_templates/tests.rs).

In merge mode, GitHub policy can require an authorized PR-head update after the
agent reviews. Zeroshot validates the new receipt identity, synchronizes that
head into the local workspace, and continues delivery. Therefore
`headRevision` should be described as the exact head accepted by the complete
Zeroshot workflow, not invariably as byte-for-byte output of the earlier agent
review. Source: [authorized head-update adoption](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/adapter/head.rs).

Pull-request success is deliberately narrower than merge success. Zeroshot
reports `opened` once GitHub authoritatively confirms the bound PR is open,
including when its checks are pending or failed or it currently conflicts. The
receipt proves the reviewed candidate was committed and published at
`headRevision`; it does not claim CI passed or that GitHub accepted a merge.
Merge success is emitted only after GitHub reports the PR merged and supplies a
valid `mergeRevision`. Source: [delivery review progression](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/adapter.rs).

## Idempotency, reconnection, and restart limits

- A stable `submission_key` plus an identical request returns the existing run;
  changed submission content under the same key is a conflict. It does not
  create a second delivery.
- Delivery is part of the submitted graph, not a separately identified request.
  Switching `none` to `pull_request` changes the graph and therefore the hashed
  submission. An exact retry must reuse the same key and the same frozen
  delivery authorization; an authorization change requires a newly admitted
  Work Unit attempt/key (and re-execution), not a delivery upgrade of the old
  no-effect run. Credential bytes are resolved runtime values rather than part
  of the secret-free submitted identity.
- `Client.get_run(run_id).wait()` can consume an already-terminal durable result
  from a later client. Cancelling or losing the waiter does not cancel the run.
- During one live delivery execution, Zeroshot retries bounded transient GitHub
  operations, uses a deterministic run branch, and uses create-or-rediscover
  semantics for the PR. Its preflight also reconciles a known published head
  before retrying delivery.
- Those properties do not make a local controller restart an execution-recovery
  mechanism. Reopening a local controller reconciles a nonterminal stored run to
  `runtime_lost`; it does not reconstruct the runtime. A terminal result and its
  delivery receipt remain durable and reconnectable.

Sources: [SDK submit/wait implementation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py),
[local duplicate/conflict handling](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cli/local.rs),
[submission digest](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cloud/backend.rs#L192-L199),
[exact replay/conflict check](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger/sqlite.rs#L338-L354),
[delivery preflight and reconciliation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/adapter/preflight.rs),
[portable-controller reopen semantics](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_portable_controller/controller.rs).

The receipt is a stable identity, not permanent storage. A Git OID identifies
immutable content, but a repository operator can later close the PR, move or
delete the run branch, or eventually make an unreferenced object unavailable.
If Broodling needs long-term custody, it must retain or copy the identified Git
object after handoff; that custody operation need not reinterpret execution, but
it cannot turn `delivery="none"` into a Zeroshot-authored receipt.

## Fit for Broodling

For a no-effect Work Unit, 10.3.0 has a concrete capability gap: **the standard
workflow cannot hand back a stable, locally retained accepted candidate**. The
clean thin-layer response is to keep `delivery="none"` and fail closed if a
stable candidate is also required. A future supported Zeroshot delivery such as
`commit` or `local_snapshot` could close that gap if its successful output
included the exact commit/tree OID and it performed its final snapshot inside
the accepted workflow, after all writers settle.

For a Work Unit explicitly authorized to open or update a GitHub PR,
`pull_request` is the existing stable handoff. Broodling should require
`succeeded is True`, validate the complete `RunResult.output` receipt against
the authorized repository and target branch, retain that receipt with the
disposition, and use `headRevision` as the candidate identity. Native already
rejects unknown receipt fields, a wrong version/mode/outcome or target, an
invalid PR ID or revision, a base-equal head, and any merge revision in PR mode.
The terminal gate also requires delivery to be the last writer and all other
writers to have settled before it began; otherwise it changes apparent graph
success to `delivery_unconfirmed`. Broodling need not infer success from a
worktree or logs. Sources: [native
receipt validation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/contract.rs#L65-L108),
[terminal delivery gate](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_supervisor.rs#L386-L465).

There is no basis in current source for using a private commit OID from
`delivery="none"`: that workflow does not create or return one. Nor is there a
public switch that asks the Git-delivery node to stop after committing but before
pushing/opening a PR. Keeping no external effects while manufacturing either
behavior in Broodling would move Zeroshot execution/delivery responsibility back
across the intended boundary.
