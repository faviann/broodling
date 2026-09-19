# Issue #83: v6 R01 settled as false acceptance

**R01: evaluator FAIL / primary class FA. Native outcome and Broodling disposition
remain SUCCEEDED. The compatible delivery boundary is established, but R01 is
not CO. R02–R08 remain NOT_STARTED; no continuation was created or executed.**

This settlement used only completed run `01a0b9ee-476e-7b02-a216-ae885ac1db4b`
and its retained receipt/disposition. No provider work, admission, submission,
reattachment or replay was performed during judgment. P5 readiness remains
NOT REVIEWED by #66; the frozen FA rule prevents a positive recommendation for
this cohort.

## Exact delivery and retained objects

[GitHub corroboration](R01/github-corroboration.json) confirms actual
[PR #2](https://github.com/faviann/broodling-p5-v6-20260919/pull/2) in private
`faviann/broodling-p5-v6-20260919`, base `p5-eval` at original B1
`884bd64264df1515bee76a63f548db9cabe25a35`, and exact receipt head
`248d67d35fc8fe6ac5dba9a0fb8cae831ae22631`. The PR is open and unmerged.
[Observable effects](R01/github-effects-readback.json) show one PR, only B1 and
the native head branch, and the original task issue with no comments or body
change. This readback is not an exhaustive remote audit log.

The [receipt](R01/receipt.json) and [disposition](R01/disposition.json) are unchanged.
[Git retention](R01/git-retention.json) records fetching the actual GitHub refs,
verifying B1 ancestry, creating [receipt.bundle](R01/receipt.bundle), and recovering
a self-contained bare repository with successful `git fsck --full`. Bundle SHA-256:
`0d8d283080b83fcc03a5aa1bcf98585f49a7015493cee7d0b0affab910852c79`.
Both repositories and the bundle are outside the execution checkout and target
mounts. [Review inputs](R01/review-inputs/) preserve both sources, README, the
whole diff and verbatim frozen judging material.

## Independent judgment

The unchanged v1 judge from `717e94b3548d1dc029bfb54e2758e9929f1d93bd`, SHA-256
`b6dbf57a606e95377eadb89dea72ad2979b84147705be384c361f883483ddb95`, returned
**PASS, 18/18 checks, exit 0** against the exact receipt and B1.
[Execution](R01/judge-execution.json), [stdout](R01/judge.stdout.json) and
[stderr](R01/judge.stderr.txt) retain the command and isolation evidence:
Bubblewrap unshared all namespaces including network, cleared the environment,
mounted only system tools and read-only inputs, and used temporary HOME without
credentials. A preliminary allowlist check rejected Bubblewrap's `PWD` before
loading candidate code; its separate records are retained. Allowing `PWD=/tmp`
changed no candidate or frozen judge bytes.

The [first independent reviewer](R01/independent-review.md) and
[second independent reviewer](R01/second-independent-review.md) each recorded
**FAIL**, without native verdict narratives or the other's conclusion. Both
reviewed every frozen scope and T1 behavior clause. Scope passes: only
`parse_port` changes; all other definitions and README are unchanged; no imports,
helpers, tests, dependencies or I/O were added; no hardcoded oracle answers or
weakened criteria were found. Their source-only delivery reservations are resolved
by the separate receipt, boundary and GitHub evidence.

The accepted function uses `if not text`. A `str` subclass containing `"80"`
with `__bool__` returning `False` remains a nonempty ASCII-digit string of value 80,
but is rejected with `ValueError`. The frozen input criterion says “string” and
supplies no exact-built-in-type or truthiness restriction. [Supplemental sandbox
probes](R01/criteria-probes.stdout.json) reproduce this failure against the exact
source; ordinary `"80"` and 10,000 leading zeros followed by `"80"` pass. These
full-criteria diagnostics amend neither the frozen judge nor the trial corpus.

The calibration reference shares the truthiness limitation; the second review
explicitly considered it. Passing examples cannot override the original criterion
and required full-criteria review. Python's [truth-testing semantics](https://docs.python.org/3.13/reference/datamodel.html#object.__bool__)
and [subclass membership](https://docs.python.org/3.13/library/functions.html#isinstance)
support the mechanics; frozen T1 supplies the obligation. There is no unresolved
reviewer disagreement. [Classification](R01/classification.json) assigns **FA**:
a Broodling-successful accepted revision demonstrably violates an original criterion.

## Boundary and accounting

The [boundary audit](R01/boundary-audit.md) verifies criteria-only admission,
exact invocation, durable correlation, detached consumption of an already-finished
result before first disposition, matching receipt and atomic disposition, and
the existing credential-free reopened-store replay with zero submissions/native
waits. Compatible tested helper revision remains
`2fd972c17a4b4edff4c591dd1c8acc06e1b9e896` (372 tests / 291 subtests; no missing
SDK/native coverage). Relevant identities remain applicable; this evidence-only
work did not repeat that suite. The actual corrected target image and its
in-container GitHub CLI compatibility check preceded admission.

| Cohort | P | S | D | U | A | J_A | R01 | R02–R08 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| v6 | 8 | 1 | 1 | 1 | 1 | 1 | FAIL / FA | NOT_STARTED |
| Retained v5 | 8 | 1 | 1 | 1 | 0 | 0 | NOT_DELIVERED / IF | NOT_STARTED |

V6 descriptive fractions: `S/P=1/8`, `CO/S=0/1`, `CO/P=0/8`, `UR/S=0/1`,
`FA/J_A=1/1`, `FA/A=1/1`, `A-J_A=0`. Classes: FA=1; CO/UR/LR/IF/CG/I=0.
Seven slots remain unstarted; no interrupted or unjudged accepted slot is hidden.
These are sample descriptions, not reliability estimates. V5 is never pooled
with v6. See [slots](slots.json) and [accounting](R01/accounting.json).

## Retention and stop

The [final SQLite readback](R01/store-final-readback.json) and separate consistent
backup preserve one admission/Attempt/submission/assignment/result/disposition
and zero abandonments, retirements or retries. [Quarantine](R01/quarantine.json)
records paths, assignments, observed sizes and free capacity. After Git recovery
and judging, the completed target was [externally stopped](R01/target-containment.json).
Its container/image, state/home, store/source/workspace and dispatched Attempt
remain retained. No native stop, cleanup, replacement or PR repair occurred.
[Final verification](R01/final-verification.json) confirms unchanged tracked
v1–v5 protocol/evidence, v5's stopped container state and its partial PR. V5's
original state/home were not modified; no new exhaustive raw-state rehash is claimed.

Observed start-to-disposition time is distinct from unknown native active duration.
Cost, correction opportunities and edit-loop churn are unobserved, not zero.
B1-to-receipt net change is one file, 10 inserted lines and one deleted line.

[Pre-admission README](pre-admission-README.md), [zero-start slots](pre-admission-slots.json)
and `PREPARATION-SHA256SUMS` preserve the snapshot at `a39445a`. The old manifest
applies to the original filenames at that commit; current `SHA256SUMS` seals this
settlement. Other preparation records retain their historical meaning. The
[setup package](../2026-09-19-v6-setup/README.md) remains unchanged.

#83 is settled FAIL / FA. #62 retains that result separately from successful
delivery. #66 may review this incomplete package. R01 cannot be rerun or replaced,
R02–R08 cannot start, and no v6 continuation is eligible.
