# Issue #6 — Q7 exact-effect coordination qualification

**Qualification date:** 6 September 2026 (UTC)

**Scope:** Broodling issue #6, Q7 / G1-effects only

**Governing inputs:** target v0.3 §§1.4, 5.4, 10, 13–14 and implementation plan v0.3
§§2 F7 and 4 P1 Q7/G1-effects

**Integration:** official async Python SDK `LocalTarget` → matching unpatched Rust sidecar →
local one-run controller and native `GitDelivery` lane

**Verdict:** **Q7 BLOCKED; G1-effects BLOCKED**

The selected integration has no trusted generic exact-effect operation. Its only trusted effect
bindings are PR and merge delivery. Both bundle a workspace commit, deterministic run-branch push,
and pull-request creation or rediscovery; merge adds review/check observation and a merge request.
Neither bundle matches the two deliberately narrower fixture intents. Broodling did not widen those
intents, route an effect through an agent, add a callback scheduler, or fabricate a receipt.

This result is independent of the G1-core blockers recorded by issues #3 and #5. It blocks P6 and
every effect-dependent or publication-dependent Contract. It does not block a no-effect slice after
G1-core passes independently.

## Exact tested build and evidence

Zeroshot was clean at
[`d0909615d6ba3c179b58bce15a059f40400ec995`](https://github.com/the-open-engine/zeroshot/tree/d0909615d6ba3c179b58bce15a059f40400ec995)
(tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`). The official SDK wheel and matching
sidecar were built unpatched from that checkout.

| Item | Exact value |
|---|---|
| SDK distribution/version | `zeroshot-rust 0.1.0.dev0` |
| wheel SHA-256 | `153d19c2ea1b5f6a58cd06a20d6c4003071ced0669c3155a94b476a3bb2dd9ae` |
| sidecar / SHA-256 | `zeroshot-rust 0.1.0` / `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Python / platform | `3.13.5` / Linux x86-64, glibc 2.41 |
| controlled leaf SHA-256 | `59c1a251a76e14a4f4352608fe3b5cbaba157e0029cae67a3c3f573ebdbe1e35` |
| issue #6 harness SHA-256 | `57d16228b43d58e44e06b4c75e839b736a6cb1eaa093de475f63386bafe83ef2` |
| full run record SHA-256 | `1b22c7a697356e7d75f4b74e5d5526fcc72ebbe615be2f481dfebcba71205176` |

The complete submitted graphs and runtimes, run IDs, terminal results and statuses, durable logs,
controlled provider invocations, workspace revisions, build identity, and rejected-admission
diagnostic are retained in
[`evidence/issue-6-run-record.json`](evidence/issue-6-run-record.json).

Only the two agent leaves were controlled. SDK transport, Rust preflight/admission, source
resolution, controller, graph progression, native Git preparation and delivery, durable status,
terminal result, and logs were real. The target was the deliberately non-owned
`example/broodling-p1-fixture`, and `GH_TOKEN` was a literal known-invalid test value whose value
was not retained. Thus the sidecar could exercise its local preparation and remote handoff while no
GitHub mutation could authenticate or succeed. This is controlled-mechanics qualification, not
production Git/GitHub delivery certification.

## Required witnesses

| Acceptance criterion | Observation | Result |
|---|---|---|
| Effect before assessment | Run `01a077ef-779b-73b0-997b-e7b96d446107` prepared candidate bytes, entered trusted PR delivery before assessment, committed the workspace, and attempted the fixed run-branch push. Push failed closed; no receipt existed and assessment did not execute. | **BLOCKED** — the fixture authorized controlled prototype publication, not an added commit/PR bundle. |
| Effect after assessment | Run `01a077ef-7a95-7d61-b059-c1f57c2b8e7a` recorded the controlled semantic-acceptance occurrence before entering trusted merge delivery. Delivery committed the workspace and attempted its fixed push. | **BLOCKED** — the fixture authorized one post-acceptance marker, not commit/push/PR/merge. |
| Exact intent consumption; no extra mutation | Both modes accept only their fixed delivery contract. The pre-assessment workspace advanced from `4fe2dfe8…` to `f18a7e9a…`; the post-assessment workspace advanced from `4fe2dfe8…` to `ad579fd1…`. Both new commits have subject `feat: complete Zeroshot task` and author `Zeroshot <delivery@zeroshot.invalid>`. | **FAIL** — local commit is an unavoidable extra effect for either narrower intent; the remaining bundle was attempted but safely rejected before remote mutation. |
| Trusted result gates the graph in one run | The before-assessment graph did not race into assessment after delivery failed. The after-assessment graph did not enter delivery until after the acceptance occurrence. Both are direct GraphSpec dependencies in one run, with no Broodling scheduler. | **PASS for ordering/fail-closed mechanics**, but no exact authorized operation produced the required trusted result. |
| Post-run reconciliation | The retained SDK result/logs show the delivery attempt and terminal failure, and the local commit is readable after the run. There is no matching exact intent/receipt to reconcile, and no public generic exact-effect readback operation exists. | **BLOCKED** — stock review inspection is internal to bundled live delivery, not an independent exact-intent reconciliation surface. |
| Record exact verdict and limits | This report and retained machine record identify the source/build/profile, reproductions, observations, missing capability, consuming gate, and controlled-mechanics limit. | **PASS** |

The run-specific revisions are not accepted effect receipts: each is evidence of the stock
adapter's unauthorized-for-the-fixture local commit.

## Why stock delivery cannot qualify these intents

The source describes the two graph workers as PR or merge modes over one implementation and states
that both commit, push, and create or rediscover a review
([`native_v2_delivery.rs` lines 1–6](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_delivery.rs#L1-L6)).
The worker dispatch recognizes only those two modes
([lines 69–97](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_delivery.rs#L69-L97)).
Rust admission rejects any other worker attached to `GitDelivery`
([`validation.rs` lines 167–195](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_admission/validation.rs#L167-L195)).
The third fixture demonstrates that boundary through the official SDK: `builtin.trusted-effect@1`
was rejected with `request.invalid` at `q7_exact_effect` before a run was created.

For an admitted stock worker, preparation calls `prepare_head`, constructs a deterministic delivery
branch, pushes it, and then synchronizes a review before any successful outcome can exist
([`adapter.rs` lines 209–228](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_delivery/adapter.rs#L209-L228)).
`prepare_head` stages all workspace changes and creates the fixed Zeroshot commit when needed
([`git.rs` lines 26–55](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_delivery/git.rs#L26-L55)).
The live logs and resulting commits match those source paths exactly.

Binding the delivery node's typed result into later graph state is possible, and the executed graph
shows that a failed delivery cannot race past the result guard. What is missing is the operation
whose result would be valid for the admitted narrow intent. Substituting an `Agent` binding would
only give a model process workspace/shell capability; it would not make the runtime consume one
exact authorized source/payload/target or provide trusted readback. That path was therefore not
used.

## Concrete dependency and disposition

**Missing Zeroshot/integration capability:** a supported trusted generic exact-effect operation
that:

- consumes one durable authorized intent with exact source, payload, target, and current
  prerequisites;
- performs only that operation, without an implicit commit/push/PR/merge bundle;
- synchronously returns a verified result to dependent graph nodes within the same live run; and
- exposes exact post-run readback/reconciliation that preserves the original intent and Attempt
  origin without starting a coding Attempt or fabricating a node occurrence.

This dependency may be satisfied by a narrow supported Zeroshot host capability or a qualified
external trusted primitive. This result does not prescribe its API or move effect authority out of
Broodling. Until it exists and Q7 is rerun successfully, **G1-effects remains BLOCKED**, consuming
gate P6 remains blocked, and required effects must remain explicit unsupported capabilities rather
than being removed from future Contracts.

## Reproduction

Build the unpatched pinned sidecar and SDK wheel as described in
[`README.md`](README.md), then create a disposable Git workspace whose attached branch is
`qualification/p1` and whose origin is
`https://github.com/example/broodling-p1-fixture.git`. With the wheel installed into the disposable
venv:

```bash
P1_ROOT=$(mktemp -d /dev/shm/broodling-issue6.XXXXXX)

"$P1_ROOT/venv/bin/python" qualification/p1/issue6_qualify.py \
  --workspace-root "$P1_ROOT/workspaces" \
  --workspace-template "$P1_ROOT/template" \
  --state-dir "$P1_ROOT/native-state" \
  --evidence-root "$P1_ROOT/evidence" \
  --output "$P1_ROOT/issue-6-run-record.json" \
  --zeroshot-source /home/faviann/repos/zeroshot \
  --wheel "$P1_ROOT/dist/zeroshot_rust-0.1.0.dev0-py3-none-linux_x86_64.whl"
```

The driver refuses a dirty or differently pinned Zeroshot checkout. It supplies its literal invalid
fixture credential internally; do not substitute a real token or a repository you can mutate for
this controlled reproduction.
