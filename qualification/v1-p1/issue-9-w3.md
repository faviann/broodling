# Issue #9 — W3 actual V1 assurance protocol

## Verdict

**W3 PASS. G1-V1 remains NOT PASSED.**

The admitted graph passed issue #9's structural-freshness, evidence-gating, sticky-obligation,
authority, repair-input and fail-closed controls through the official Python SDK and matching Rust
sidecar. This is qualification evidence, not Broodling product code. W4 and W6 remain for issue
#10, followed by the separate G1-V1 review in issue #11.

The machine record is
[`evidence/issue-9-run-record.json`](evidence/issue-9-run-record.json). It contains the exact
GraphSpec, RuntimePlan, initial state, node contracts, role map, repair bound, run IDs, terminal
results and every controlled-leaf input/occurrence.

## Actual protocol and encoding

```text
IMPLEMENT (mutation; writes c1)
  → initial required-evidence check → initial REVIEW → ADJUDICATE authority
      ├─ no directive → FINAL SEMANTIC ASSESSMENT
      └─ open_d1 → bounded repair loop (maximum 3)
           REPAIR (mutation; writes next generation)
             → renewed required-evidence check → fresh REVIEW → resolution authority
                 ├─ resolved_d1 → FINAL SEMANTIC ASSESSMENT
                 └─ open_d1 at bound → obligations_exhausted
```

The typed state is the frozen Contract string, a structural candidate-generation ordinal
`c0..c4`, evidence state `unchecked|valid|missing`, review state
`unexecuted|clean|found`, and obligation state `none|open_d1|resolved_d1`. The repair bound is
three. Implement and repair are ordinary mutating steps. Evidence checks and reviews are read-only
verifiers. Adjudication, eligible resolution and final assessment are the only designated authority
roles. The loop-control occurrence is also read-only. All sessions use `sessionScope=execution`.

Candidate applicability is established only by admitted graph order. Completion of implement or
repair writes the next ordinal; the graph then unconditionally executes a graph-local raw-file
availability check and fresh assurance for that ordinal. The ordinal is not a seal, digest,
manifest or provenance identity. No observer, external applicability check or processor-supplied
identifier participates.

Verifier signals are the sole route-affecting processor representation. Only designated authority
signals can update obligation state. Diagnostics are unbound, and ordinary outputs cannot acquire
an authority schema. Error branches route timeout, crash, malformed and missing required output to
`execution_unusable`; designated refusal routes to `authority_gap`; missing raw evidence routes to
`required_evidence_missing`; semantic insufficiency routes to `semantic_gap`.

One runtime fact mattered during construction: reaching Zeroshot's loop maximum completes the loop
rather than failing it. The admitted graph therefore has an explicit post-bound branch. If the
last designated resolution remains `open_d1`, the run terminates as `obligations_exhausted` before
final assessment.

## Observed controls

The C1 → repair → C2 control executed exactly:

```text
implement(c1) → initial_evidence_check(c1) → initial_review(c1, found)
→ adjudicate_authority(open_d1)
→ repair(c1 → c2, input limited to Contract/generation/directive)
→ repair_evidence_check(c2) → repair_review(c2, clean)
→ resolution_authority(open_d1 → resolved_d1)
→ final_assessment_authority_repaired(c2) → accepted
```

| Control | Actual terminal result |
|---|---|
| Missing raw evidence after implement | `required_evidence_missing` before review |
| Missing renewed raw evidence after repair | `required_evidence_missing` before C2 review |
| Crash / timeout / malformed / missing required output | `execution_unusable` |
| Designated authority refusal | `authority_gap` |
| Persistent `open_d1` for all three repairs | `obligations_exhausted`; no final assessor |
| Omitted eligible-resolution signal | `execution_unusable` |
| Implementer/reviewer/repair authority-shaped output | `execution_unusable` |
| Clean assurance followed by rejecting semantic assessment | `semantic_gap` |
| Completed clean review with criterion evidence judged insufficient | `semantic_gap` |

Positive controls accepted a completed empty review, a clean route, explicit eligible resolution,
and the C2 repair route. In the sticky control, each fresh review was clean while every resolution
occurrence still received `outstanding=open_d1`; omission and candidate advancement did not clear
it. This distinguishes explicit resolution from omission. The missing-evidence cases are valid
observed RED outcomes, unlike crash/timeout/malformed executions that never produced usable review.

The contradictory control returned a clean-looking diagnostic while the canonical adjudicator
signal opened `d1`; repair still ran. The forged-diagnostic control emitted forged Contract,
source, evidence and predecessor identifiers but retained the frozen Contract and same route. A
widened-binding canary put rejected-review and private-authority material in diagnostics; actual
repair input was exactly:

```json
{"candidateGeneration":"c1","contract":"frozen-contract-v1","directive":"open_d1"}
```

No raw/rejected finding or private adjudicator material appeared in repair input.

## Integration and compatible profile

The real boundary comprised SDK encoding and process transport, Rust preflight/admission, graph
verification/execution, typed response validation and correction attempts, state binding, routing,
bounded execution, occurrence history and durable terminal result. Only provider-leaf decisions
and faults were controlled.

The run used clean Zeroshot source
`d0909615d6ba3c179b58bce15a059f40400ec995` (tree
`03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`), SDK wheel SHA-256
`16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`, and sidecar SHA-256
`9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.
The machine record retains exact host, CLI, leaf and harness versions/hashes.

This graph uses the same agent/verifier worker kinds, sidecar-selected workspace-write/read-only
modes, execution-scoped sessions, build and no-effect assumptions as issue #8 commit `7931ac9`.
It adds no network or writable root and changes no issue-#8 W2 premise, so no affected W2 control
required rerun. Retained provider arguments show `workspace-write` only for implement/repair and
`read-only` for evidence, review, authority and loop-control occurrences. Controlled leaves expose
deterministic mechanics; semantic assessment labels remain
controlled model judgment and do not certify general model quality.

## Scope and consumers

This result qualifies W3 only. It introduces no candidate sealing/provenance subsystem, product
validator/router, scheduler, state store, effect implementation, external observer, W4/W6
qualification or V1-P2 implementation. Issue #10 must consume this exact graph/configuration or
explicitly requalify a relevant change. G3-V1 and issue #11 are later consumers.
