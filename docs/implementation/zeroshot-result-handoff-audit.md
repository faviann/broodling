# Zeroshot result-handoff audit

Investigated 16 September 2026 against the current stable Zeroshot 10.3.0
source at immutable commit
[`054ad3fd6c763b98d12f5b2e90830b97116561ad`](https://github.com/the-open-engine/zeroshot/tree/054ad3fd6c763b98d12f5b2e90830b97116561ad).
This note is prospective implementation research for PR #50. It does not amend
historical qualification or evidence.

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
Zeroshot-produced candidate and preserves Broodling's no-authoritative-external-
effects requirement**.

Broodling should not bridge that gap by watching processes or by racing to
create its own commit after a no-delivery terminal result. Such a commit would
not itself be the result accepted by Zeroshot, and independently excluding a
write between acceptance and capture would recreate the supervision problem
this integration is meant to remove.

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
[delivery preflight and reconciliation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/adapter/preflight.rs),
[portable-controller reopen semantics](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_portable_controller/controller.rs).

The receipt is a stable identity, not permanent storage. A Git OID identifies
immutable content, but a repository operator can later close the PR, move or
delete the run branch, or eventually make an unreferenced object unavailable.
If Broodling needs long-term custody, it must retain or copy the identified Git
object after handoff; that custody operation need not reinterpret execution, but
it cannot turn `delivery="none"` into a Zeroshot-authored receipt.

## Fit for Broodling

If authoritative GitHub effects remain outside Broodling's admitted effect set,
10.3.0 has a concrete capability gap: **the standard workflow cannot hand back a
stable, locally retained accepted candidate**. The clean thin-layer response is
to fail closed rather than call a mutable worktree a stable result, and to seek
a supported Zeroshot delivery such as `commit` or `local_snapshot` whose
successful output includes the exact commit/tree OID. Such a mode should perform
its final snapshot inside the accepted workflow, after all writers settle, and
make that identity the graph's terminal output.

If opening a GitHub PR is an acceptable effect, `pull_request` is the simplest
existing handoff. Broodling can treat the successful `RunResult.output` receipt
as opaque Zeroshot authority and retain the entire receipt with the disposition;
`headRevision` is then the stable accepted candidate identity. Broodling does
not need to inspect execution history or prove worktree quiescence. `merge` is
strictly more effectful and supplies an integrated revision, so it is appropriate
only when merging is the actual product outcome.

There is no basis in current source for using a private commit OID from
`delivery="none"`: that workflow does not create or return one. Nor is there a
public switch that asks the Git-delivery node to stop after committing but before
pushing/opening a PR. Keeping no external effects while manufacturing either
behavior in Broodling would move Zeroshot execution/delivery responsibility back
across the intended boundary.
