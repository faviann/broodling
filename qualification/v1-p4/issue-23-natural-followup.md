# Issue #23 — bounded natural-provider follow-up

Status: **BLOCKED — no actual-provider repair witness**. #24 has not begun.
Product baseline: `ce03c99d07ccd2ea9c1c8e4ac308e22fa0fa8d7f`.
This follow-up supplements the historical [blocker record](issue-23-real-provider-blocker.md)
and [implementation audit](issue-23-disposition.md); those published records remain unchanged.

## Authorized fixture and bound

The user authorized a different small normal engineering task, without forced
failure, fake findings, candidate tampering or graph/profile changes. The parent
bounded execution to **two independent runs of one frozen fixture**. Both ran;
no further attempts were started. These are independent qualification fixtures,
not replacement Attempts or a retry-policy exercise.

The [frozen harness](issue23_natural_provider.py) asks for an ordinary contacts CSV
import helper. Its visible Contract covers standard quoting, Unicode header
whitespace normalization, preservation of cell strings, blank-row handling,
empty/header-only documents, invalid headers and mismatched row lengths. The
three immutable valid-input smoke cases are explicitly non-exhaustive. No defect
or repair is prescribed. The initial implementation is instructed to satisfy
the complete Contract. All model roles use actual qualified Codex and the existing
product graph, provider profile and deterministic evidence leaf.

Harness SHA-256: `a5e6a9282972d654c9016237019323abc81ebeabb5f1332a8f50fc9ca8a5b7a8`.
Both runs have identical canonical Contract, entitled source and B1 file bytes:
`cr-41ed72fb809d4287a9125a7ea6462e98e3d26fd6da183c6dd425726eeb647bd3`.
See [preflight](evidence/issue-23-natural-preflight.json) and the
[parent evidence/hash audit](evidence/issue-23-natural-audit.json).

## Actual results

| Execution | Runtime run | Raw evidence | Result |
| --- | --- | --- | --- |
| 1 | `01a082b0-a546-70b0-ae8f-4583ff954e57` | [JSON](evidence/issue-23-natural-1.json), [SQLite](evidence/issue-23-natural-1.sqlite3) | Clean review, no repair, durable SUCCEEDED |
| 2 | `01a082b4-0521-7ee3-8be0-57fe476025a5` | [JSON](evidence/issue-23-natural-2.json), [SQLite](evidence/issue-23-natural-2.sqlite3) | Clean review, no repair, durable SUCCEEDED |

Both observed exactly `implement → initial_evidence_check → initial_review →
adjudicate_authority → final_assessment_authority_clean`. Initial review was
clean, adjudication was `none`, and the clean final assessment was accepted.
Neither supplies a substantive repair directive, repair occurrence, renewed
repair evidence, fresh repair review, resolution or repaired final assessment.
Both correctly record repair-witness `passed: false`; harness exit 1 expresses
that unmet witness predicate. Neither records an execution, diagnostic or cleanup error.

All three raw initial smoke cases passed with exit code zero in each run. The
checker remained byte-identical to B1. Independent static inspection of both
final implementations found no obvious unmet visible CSV requirement.

The frozen collector has a clean-path reporting caveat: parsing initial and
absent renewed evidence in one `try` resets both populations when renewed parsing
fails, so `initialRawPopulationPresent` is falsely reported as false. Its
`checkerUnchanged` predicate also requires absent renewed material. These flags
do not show missing initial smoke evidence or an altered checker. Raw evidence
is retained unchanged; no repair claim relies on these flags. More generally,
nonempty findings and AST changes alone would not establish a genuine semantic
repair without independent inspection of the actual violation and correction.

## Compatibility, review and preservation

Both actual executions exercised durable control stores outside the candidate
and ambient writable `/tmp`, preserving the landed #21/#22 placement decisions.
Cessation and fixture cleanup succeeded; retained custody and disposition rereads
matched exactly after cleanup. This supplies actual execution of the corrected
store placement, which the earlier historical record had only preflighted. It
does not supply the missing repair branch or G4 qualification.

The parent verified every recorded source hash against current files, unchanged
hashes throughout both runs, current graph/runtime identity, identical frozen
inputs, cleanup readback and all 87 product/test hashes in the prior verification
manifest. Product and regression sources remain unchanged from the baseline.
Prior SDK verification therefore remains applicable: 419 regression tests plus
10 dedicated disposition tests passed in two separate invocations, alongside
the recorded SDK-free suite. No full regression rerun was needed for this
qualification-only addition. The new harness passes Ruff checking and formatting;
`git diff --check` passes.

Fresh read-only subagent reviews found no hard repository-standard violations
and independently confirmed both clean-only results, source/profile compatibility,
raw smoke checks and cleanup readback. A nonblocking maintainability observation
was that the harness `run()` combines fixture setup, execution, export and cleanup;
no refactor was made to the frozen executed source. The independent evidence
review identified the collector caveat above.

Historical G1/G2/G3 and earlier P4 records remain unchanged. No product machinery,
profile change, effects, semantic recovery, automatic retry or later-phase work
was introduced. The two-run bound is exhausted. **#23 remains open and BLOCKED;
#24 must not begin without a genuine real-provider repair-to-disposition witness.**
