# Issue #4 — Q3 fail-closed routing and sticky-obligation qualification

**Qualification date:** 6 September 2026 (UTC)

**Scope:** Broodling issue #4 and Q3 only

**Governing inputs:** target v0.3 §§2.3, 7.1–7.5, 9.3, 13 and implementation plan
v0.3 §§2 F4 and 4 P1

**Integration:** official async Python SDK `LocalTarget` → matching Rust sidecar → local one-run
controller

**Verdict:** **Q3 PASS; G1-core remains BLOCKED by issue #3 Q1/Q6**

The admitted GraphSpec, not the qualification driver, owns validation, state writes, routing, and
the three-round repair bound. No Broodling response validator, scheduler, occurrence reader, or
copy/admit hop was introduced.

## Exact tested build and evidence

Zeroshot was clean at
[`d0909615d6ba3c179b58bce15a059f40400ec995`](https://github.com/the-open-engine/zeroshot/tree/d0909615d6ba3c179b58bce15a059f40400ec995)
(tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`). The official SDK wheel was built
unpatched from that checkout with the matching sidecar.

| Item | Exact value |
|---|---|
| SDK distribution/version | `zeroshot-rust 0.1.0.dev0` |
| wheel SHA-256 | `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| sidecar / SHA-256 | `zeroshot-rust 0.1.0` / `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Rust / Cargo | `rustc 1.97.0 (2d8144b78 2026-07-07)` / `cargo 1.97.0 (c980f4866 2026-06-30)` |
| Python / platform | `3.13.5` / Linux x86-64, glibc 2.41 |
| controlled leaf SHA-256 | `5634d646fce2f77c8ff5820b649cd21af0424f2e643d8ee487289771d3febab5` |
| issue #4 harness SHA-256 | `843233e430a92077e00b1415211804df42623b4f070946f48ce608ae9ced5f55` |
| full run record SHA-256 | `e17485caeee5322932a5508b1d26d468ffcec238abae39aea8342a1171bf3275` |

The complete submitted graph/runtime values, build identity, durable results, public route
transitions, controlled inputs, run IDs, and cursors are retained in
[`evidence/issue-4-run-record.json`](evidence/issue-4-run-record.json). Only the provider leaf was
controlled. SDK transport, Rust preflight/admission, Git source resolution, controller, response
validation, graph state/dataflow, reduction/routing, and durable terminal results were real.

## Qualified protocol

The fixture uses one canonical route-affecting representation: the designated adjudicator's enum
signal is also written directly into typed graph state. Review and repair have no write binding to
that state. Initial adjudication may emit only `none` or `open_d1`. After `open_d1`, the structurally
bounded resolution occurrences may emit only `open_d1` or the explicit `resolve_d1`; omission can
therefore neither restore `none` nor reach final assessment. This specializes the shared small
implement → review → adjudicate → repair/review/adjudicate → final-assessment graph without adding
a product node, validator, or scheduler.

The pinned graph contract supplies signal/output state writes and the four worker error labels
([`graph.rs` lines 165–230](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/crates/openengine-cluster-protocol/src/graph.rs#L165-L230)).
The runner validates output, diagnostic, and signal shapes separately
([`response.rs` lines 140–174](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_runner/response.rs#L140-L174)).
The fixture avoids relying on that separate validation for semantic consistency: the adjudication
payload is required to be `null`, so a second contradictory decision representation is malformed.

## Results

| Witness | Observed terminal result | Last meaningful route |
|---|---|---|
| completed empty-review control | success, obligation `none` | adjudicate → final assessment |
| successfully observed RED/finding control | success, obligation `resolve_d1` | adjudicate → repair → clean review → resolution authority → final assessment |
| crash | `execution_unusable` | review; no adjudication/final assessment |
| timeout | `execution_unusable` | review; no adjudication/final assessment |
| refusal / force-stop | `force_stopped` | review active; no later dispatch |
| malformed output | `execution_unusable` | review; no adjudication/final assessment |
| missing required output | `execution_unusable` | review; no adjudication/final assessment |
| failed applicability | `identity_or_applicability_failed` | applicability check; no review |
| contradictory payload / clean signal | `execution_unusable` | adjudicator; no clean route |
| ordinary reviewer emits authority-shaped result | `execution_unusable` | review; no authority acquired |
| fresh empty reviews plus omitted resolution | `obligations_exhausted` | exactly three repair/review/resolution rounds; no final assessment |
| final gap after clean adjudication | `semantic_gap` | resolution authority → separate final assessor |

The recorded repair input is exactly `{"directive":"open_d1"}`. The following resolution input is
exactly `{"findings":"clean","outstanding":"open_d1"}`. Thus the new empty findings container did
not erase the accepted directive, and repair received the adjudicated directive token rather than
raw findings. In the positive repair control, `resolve_d1` from the designated resolution authority
routed directly to final assessment; there was no Broodling copy or admission step.

For the refusal witness, the public SDK requested force-stop while `review` was active. The pinned
supervisor defines forced settlement and active-execution closure as `refusal`
([`runtime.rs` lines 373–403](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_supervisor/runtime.rs#L373-L403));
the supported external terminal projection is `force_stopped`. No accepting node was dispatched.
This witness does not claim the completed-occurrence visibility that issue #3 found missing.

## Disposition

All issue #4 acceptance criteria pass through the selected real integration, so Q3 is PASS and the
issue may close. No Q3-specific Zeroshot dependency was exposed. G1-core does **not** pass: issue #3
already records the unresolved Q1/Q6 completed-occurrence recovery dependency, which this
qualification neither reads around nor attempts to solve.

## Reproduction

Build the sidecar and wheel exactly as in the shared [P1 harness instructions](README.md), create a
short-path disposable Git workspace, then run:

```bash
P1_ROOT=/path/to/the/P1-build-directory
ISSUE4_ROOT=$(mktemp -d /dev/shm/i4.XXXXXX)
WHEEL=$(find "$P1_ROOT/dist" -maxdepth 1 -name '*.whl' -print -quit)
"$P1_ROOT/venv/bin/python" qualification/p1/issue4_qualify.py \
  --workspace "$ISSUE4_ROOT/w" \
  --state-dir "$ISSUE4_ROOT/s" \
  --evidence-root "$ISSUE4_ROOT/e" \
  --output qualification/p1/evidence/issue-4-run-record.json \
  --zeroshot-source /home/faviann/repos/zeroshot \
  --wheel "$WHEEL" \
  --cargo /home/faviann/.cargo/bin/cargo \
  --rustc /home/faviann/.cargo/bin/rustc
```

The qualification script only asserts the retained outcomes after the runs finish. Those
assertions are not in the execution path and do not validate or reroute node results.
