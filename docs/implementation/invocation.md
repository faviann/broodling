# One-work-reference invocation

`Broodling` implements [#76](https://github.com/faviann/broodling/issues/76) as a
small Python API over the existing domain services:

```text
explicit GitHub issue → entitled sources → proposed Contract → admission
  → Attempt + dedicated workspace/B1 → native submission → receipt/disposition
```

The [current architecture](../governing/current.md), completed
[#75 ingress](work-reference-ingress.md) and
[#74 first-use decision](https://github.com/faviann/broodling/issues/74) govern this
surface. It adds no scheduler, execution graph, provider manager, result ledger,
retry policy or semantic success rule. Zeroshot still owns execution.

## Invocation shape

Construct `Broodling(store, submitter, workspace_root)` with an open
`BroodlingStore`, the existing `ZeroshotSubmitter` and a durable workspace root.
The caller owns the store's lifetime. Keep it open while awaiting operations.
The [README example](../../README.md#install-and-use) shows the complete minimal
configuration and a caller-supplied typed proposer.

| Method | Existing responsibilities exposed |
| --- | --- |
| `submit(reference, propose, *, repository, required_effects, revision="HEAD", additional_sources=(), constructed_by="model_extraction")` | Capture the GitHub issue, construct/admit the Contract, provision its Attempt and dispatch; return `InvocationStatus`. Rejected admission returns its findings without an Attempt. |
| `status(contract_revision_id)` | Read the exact revision's retained lineage without GitHub, provider or Zeroshot calls. |
| `history(reference)` | Read recorded revision statuses for that Work Unit, oldest first; return an empty tuple when none exists. No acquisition or proposal generation. |
| `resume(contract_revision_id, *, repository=None, revision="HEAD")` | Admit/continue stored authority without source capture or proposal generation; return `InvocationStatus`. A repository is required only before first Attempt admission. |
| `await wait(attempt_id)` | Reconnect to the bound run, consume its result and return the existing `WorkUnitDisposition`; replay returns the retained disposition. |
| `await stop(attempt_id, reason)` | Record abandonment and request native stop through the existing coordinator; return `AttemptRetirement` only when the existing cessation requirements permit it. |

`submit`, `resume`, `status` and `history` are synchronous. `wait` and `stop` are
asynchronous; the README uses `asyncio.run` from synchronous caller code. Call
`submit`/`resume` outside a running event loop because native dispatch uses the
existing synchronous SDK adapter. Keep the supported
single-host ownership model; the facade does not provide an async dispatch
service or cross-Work-Unit concurrency policy.

`reference` is the existing `WorkReference`; `propose` receives
`ContractProposalInput` and returns the existing typed `Contract`. Proposal
generation has no bundled model. The caller must explicitly declare
`required_effects`, including `()` for no effect. Exactly one `pull_request`
effect must name its target branch. Mixed, multiple or unsupported effects remain
rejected. Complete captured source material remains authoritative alongside the
Contract. A proposer must preserve every obligation and cannot self-entitle
sources or change effect authority. Deterministic admission does not prove that
natural-language extraction is complete or correct. Additional sources require the explicit caller entitlement
described by [ingress](work-reference-ingress.md).

Initial PR dispatch uses the existing fixed native `software-change` /
DirectTarget / Codex / CLIProxyAPI `gateway` profile, `gpt-5.6-sol` and medium
reasoning effort. Configure the target origin, current `GH_TOKEN`, exact
`GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1` and current
`GATEWAY_API_KEY` through `ZeroshotSubmitter`. Use the pinned dependencies and
compatible [actual-target GitHub CLI](../../evaluation/p5/direct-target-gh-compatibility.md).
The API does not install/configure a target, select another runtime or package
the first deployment; that remains #77.

## Status and pinned identity

`InvocationStatus` groups existing records rather than computing a new lifecycle
state: `work_unit`, entitled `sources`, exact Contract `revision`, admission
`decision`, `attempt`, `worktree`, `submission`, `abandonment` and `disposition`.
Absent records are `None`. A revision recorded before admission can therefore
have no decision. Submission exposes the retained submission key/state and
Zeroshot run ID; a run ID alone is not a successful Broodling disposition.
Each status uses a coherent read snapshot without reserving SQLite's writer slot
from lifecycle operations. `history` reads each revision's status independently.

Retain `status.revision.contract_revision_id` and
`status.attempt.attempt_id` when present. `status(revision_id)` never substitutes
a newer revision. For the requested revision it selects its current Attempt, or
its last admitted Attempt when none remains current. Thus terminal lineage stays
inspectable after success or abandonment. Work Unit identity is shared across
revisions, but revision/Attempt/result bindings remain exact.

On successful authorized PR delivery, `disposition.outcome == "SUCCEEDED"` and
`disposition.result` retains `attemptId`, `contractRevisionId`, `runId`, native
`workflow`, stable `acceptedRevision` and the complete `deliveryReceipt`.
This is the existing receipt-backed lifecycle decision. It adds no separate
failure or semantic-certification state.

## Repetition, reconnection and handback

Repeating `submit` reacquires source bytes and reruns the proposer. Identical
source bytes and Contract meaning resolve the same revision, original B1,
Attempt, frozen submission key and run. A changed local `HEAD` does not replace
an already admitted B1. Changed issue bytes, even metadata, or changed proposal
meaning produce a different immutable revision; they never amend the earlier
Attempt. Another revision cannot displace a current Attempt: existing admission
and conflict rules still apply.

Use `resume(revision_id)` to continue exactly recorded authority after reopening
the store. Before first Attempt admission, provide `repository` and optionally
`revision` to select B1. Once admitted, the retained repository/B1 govern recovery.
An interrupted pre-dispatch operation continues the same Attempt. An ambiguous
dispatch replays only its frozen request/key under the existing submission
rules. Prepared or acknowledgement-loss replay still requires valid dispatch
configuration and current credentials; secrets are not persisted.

After durable run correlation, resume, wait and stop use the frozen run/target
locator without dispatch credentials, reacquisition, reproposal or reconstruction
from current runtime settings. They still require the relevant native target to
be reachable when making a native call. A retained final disposition is returned
without contacting it. Status never refreshes native execution progress.

The facade preserves domain errors rather than converting every refusal into a
new result shape:

| Condition | Caller-visible behavior |
| --- | --- |
| Source acquisition, entitlement or malformed/authority-changing proposal fails | Existing exception; already captured facts remain retained. |
| Valid Contract is not closable | Rejected admission and findings in status; no Attempt or dispatch. |
| Provisioning, configuration or submission fails | Existing exception; retained admission/Attempt/submission facts support inspection and the permitted same-invocation recovery. |
| Native run fails | `wait` durably abandons the Attempt and raises `SubmissionNotReady`; inspect `status(revision_id).abandonment`. |
| Receipt, run binding or current Attempt authority is invalid | Existing refusal, with no successful disposition. |
| Wait is cancelled or loses transport | The caller detaches; await the same Attempt again. Cancellation does not abandon or stop execution. |
| Explicit stop | Authority is abandoned first. A dispatched Attempt requests native stop when its run is known, then raises `CessationUnconfirmed`; its workspace stays quarantined. Unknown dispatched run identity also remains quarantined. |

Exceptions do not roll back earlier durable steps. If `submit` raises before
returning identifiers, use `history(reference)` to inspect retained revisions and
select the exact revision to resume. Failures before a revision was recorded
leave no revision handle. The facade does not add a separate error ledger.

`history` checks supplied upstream repository/issue IDs against already-pinned
identities and raises `WorkUnitIdentityConflict` on a mismatch. It never creates
a Work Unit, records ingress or pins a previously unknown ID. Matching or omitted
IDs allow observation; without supplied IDs, this offline lookup cannot detect
upstream recreation at the same path. Unknown references return no history.

`resume` returns existing rejection,
abandonment or disposition without automatically creating a replacement Attempt.
Stopping an Attempt that never dispatched can establish the existing retirement
proof, but this facade does not perform retirement/deletion or retry.

## Operating limitations

The scoped P5 **FAIL** remains. Use only as an operator-supervised internal
PR-proposal workflow. Every delivered PR requires independent human/operator
review of the exact accepted revision against the frozen request and Contract,
with appropriate repository tests/CI, before a separate merge decision. Native
reviewer acceptance, automated checks and Broodling `SUCCEEDED` do not certify
semantic correctness or authorize automatic merge, deployment or release. This
external review remains outside Broodling and is not a pre-disposition step.

No-effect execution remains admissible but cannot produce successful disposition:
Zeroshot 10.3 supplies no stable accepted local result, so `wait` fails closed with
`SubmissionNotReady`. The facade does not manufacture a snapshot or open an
unauthorized PR. Every dispatched Attempt remains ineligible for automatic
deletion or replacement, even after success or stop. Native terminal labels do
not establish physical cessation; retain quarantined workspaces/state.

No backlog selection, dependency waiting, scheduling, budgeting, Cerebrate,
broader effects, alternate runtimes, multi-host coordination or deployment
packaging is introduced. The focused integration tests use real Broodling
services/local Git with controlled GitHub and SDK boundaries; they do not claim
a new provider-quality evaluation or live deployment validation.
