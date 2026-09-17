# P5 native authorized-PR evaluation protocol v1

**ID: `p5-native-pr-v1` — frozen, author-reviewed, 17 September 2026.**
**Live preflight: BLOCKED. Authorized dispatches: 0. Authorized spend: 0.**
This completes protocol/preflight work for #63, not live authorization, a peer
approval, #64/#65 execution, or a P5 pass. The #63 completion comment identifies
the immutable freeze commit and review decision. [Preflight](preflight.md)
records the actual evidence and blockers; [corpus](corpus.md) freezes task inputs,
B1, independent checks and judging. Do not edit this version in place after freeze.

## Authority, baseline and claims

Follow [current authority](../../../docs/governing/current.md), then #62/#63,
current production code and [tests/README](../../../tests/README.md). The native
[integration](../../../docs/implementation/zeroshot-native-integration.md),
[source audit](../../../docs/implementation/zeroshot-current-source-audit.md) and
[handoff audit](../../../docs/implementation/zeroshot-result-handoff-audit.md) are
supporting context. Historical G1–G4 material is unchanged and supplies no new pass.

Product baseline: `b901367b339c3ca715fa51ad91646b499a8fa068`. This protocol adds
only evaluation material. Pin the protocol commit separately from that product
baseline; later documentation-only commits do not silently change production.
The exact SDK wheel/hash, runtime selection and observed host are in preflight.

| Evidence layer | Claim and required observation | What does not establish it |
| --- | --- | --- |
| Controlled Broodling seam | Current default suite passes on its supported profile, including the actual bundled SDK/native controlled-provider checks; retain command, exit, counts and missing coverage | Source inspection, oracle self-tests, missing-SDK skips, or historical qualification |
| Live boundary (#64) | One real authorized PR from criteria-only admission, frozen invocation, durable run correlation, caller reconnection and matching receipt-backed atomic disposition; corroborate actual GitHub PR/commit and dispatched quarantine | Synthetic receipt, manual PR publication, local no-effect completion, or a provider's claim |
| Real-provider outcomes (#65) | Independently judge the eight fixed trials' exact accepted revisions against original criteria/B1, preserving every outcome and limit | Open PRs alone, controlled providers, native self-assessment, mutable worktrees, or a green default suite |

The live boundary must also show a detached caller consuming an already-completed
native result **before first disposition**, then a reopened store returning the
identical retained result/disposition without re-execution or present-day dispatch
credentials/profile. Preserve the native controller; caller reconnection is not
controller resurrection. Reuse deterministic tests for the fault permutations
mapped in preflight, not additional live crash/receipt campaigns.

Independent judging happens outside Broodling after native outcome observation;
it cannot amend the Contract, manufacture a receipt, or veto/replace a product
record. A false acceptance remains a recorded Broodling success and an evaluator
failure. An opened PR does not imply CI success or merge readiness.

## Fixed sample and allocation

Eight trials, four synthetic utility tasks, two repetitions each, strictly serial:

| Slot | Task | Repetition | Stage |
| --- | --- | --- | --- |
| R01 | T1 strict port parsing | 1 | #64 boundary; preregistered quality observation |
| R02 | T2 stable deduplication | 1 | #65 |
| R03 | T3 CSV rendering | 1 | #65 |
| R04 | T4 frozen access authority | 1 | #65 |
| R05 | T4 frozen access authority | 2 | #65 |
| R06 | T3 CSV rendering | 2 | #65 |
| R07 | T2 stable deduplication | 2 | #65 |
| R08 | T1 strict port parsing | 2 | #65 |

Four tasks cover valid basic changes, wrong scope/order, missed acceptance edges,
and conflicting lower-authority guidance. Two repetitions expose gross instability
at bounded cost; the reverse second pass fixes order in advance, not a randomized
or statistically independent population sample. Do not claim general reliability.

All implementation, native acceptance/code review, and any reached repair use the
production-selected real provider. **No provider substitution or seeded repair
campaign is authorized by v1**, including R01. Native routing/repair/retries remain
Zeroshot-owned. There is no extra boundary run outside the eight slots.

R01 is registered before its outcome and retained regardless of success/failure;
never select it into the sample only because it passed. Count it once in the
combined eight-slot accounting. It supplies a real-provider quality observation
only with genuine provider provenance and the same independent judging. A failed
or blocked R01 prevents R02–R08 from starting; the partial package can go to #66.
A substituted or incompatible R01 is retained as a protocol deviation, not replaced
by a ninth run or silently converted into valid quality evidence.

Every slot gets its own properly entitled GitHub primary issue, Work Unit,
Contract, local Attempt/workspace, submission key and native run. All use the same
original B1 and target branch. Do not reuse a prior dispatched Attempt or request
replacement to obtain another trial. Even successfully dispatched Attempts remain
quarantined. Allocate capacity for eight local assignments plus the corresponding
operator-owned target checkouts and durable state/evidence. Record paths, disk
usage, free-space thresholds and retained/quarantined resources after every slot.
No trial sees another trial's results, repair history, provider sessions or judging
material. Model/backend nondeterminism and shared-host/order effects remain limits.

## Counting and classifications

A slot **starts** at its first admission attempt; create its evidence record before
that operation. This keeps admission failures in the campaign. Record these counts:
`P=8` planned; `S` started slots (including early admission refusals); `D` slots
with durable dispatch intent or a submission call, whichever occurs first;
`U` distinct known native run IDs; `A` Broodling SUCCEEDED dispositions;
`J_A` accepted revisions with determinate independent judgments. An ambiguous
submission counts in D and consumes its reservation even without a known run ID.

Keep native `run_id/succeeded/output/failure`, Broodling admission/submission/
abandonment/disposition, and evaluator `PASS/FAIL/INDETERMINATE/NOT_DELIVERED`
as separate fields. Apply one primary classification to every started slot:

| Class | Frozen rule |
| --- | --- |
| CO — correct outcome | Authorized receipt-backed Broodling SUCCEEDED, actual exact PR revision corroborated, all frozen independent checks and full-criteria review PASS |
| FA — false acceptance | Broodling SUCCEEDED but that exact revision violates any original criterion, scope or frozen authority; a structurally valid PR receipt does not excuse it |
| UR — unjustified rejection/noncompletion | Valid feasible in-profile task does not reach correct delivery because of an evidenced product/workflow failure or semantic refusal, with no demonstrated infrastructure/capability cause; a completed wrong PR rejected by Broodling is not CO |
| LR — legitimate refusal | An original authoritative requirement actually demands an unsupported effect/obligation or unsatisfied prerequisite and is preserved/refused rather than waived; **no v1 live task is expected to fall here** |
| IF — infrastructure failure | Concrete auth, target, transport, host/storage or installation failure prevents the required observation/delivery; retain the diagnostic, do not infer it from any generic native failure |
| CG — capability gap | A demonstrated missing supported capability prevents the claim, such as controlled no-effect stable-result handoff; not a generic excuse for a difficult in-scope task |
| I — indeterminate | Missing/unobtainable exact result, unresolved reviewer disagreement, unestablished cause, or incompatible/contaminated evidence prevents defensible classification |

FA takes precedence over incidental infrastructure problems when a successful
accepted revision is demonstrably wrong. CO requires all its evidence. Otherwise
assign IF/CG/LR only with supporting evidence, UR only once the stated cause is
established, and I when it is not. Retain secondary flags (for example native-only
semantic false acceptance, invalid receipt, timeout, budget stop, contamination,
or protocol deviation) without hiding the primary outcome. A native success
without a Broodling disposition is never counted as a Broodling false acceptance.
No stable accepted revision is inferred for a failed/noncompleted run.

Report raw class counts and S/P, D, U, A, J_A; CO/S and CO/P; UR/S; FA/J_A with
unjudged accepted count A−J_A beside it, and FA/A only as a lower-bound observed
fraction when judgments are missing. Zero denominators are **N/A**, not 0% success
or a zero false-acceptance rate. Show unstarted, interrupted and incomplete slots
explicitly. IF/CG/I/LR are not silently removed from headline denominators.
Controlled test refusals/calibrations have their own evidence/counts and never
inflate live CO or sample size. Exploratory runs are not authorized under v1;
any later separately authorized diagnostics remain outside the confirmatory cohort.

## Readiness thresholds fixed before results

A **positive, bounded authorized-PR smoke-readiness recommendation** requires all
of the following, not merely an issue closure:

* Supported full default-suite PASS with no missing SDK/native coverage, and #64
  live boundary PASS on the recorded compatible baseline/target.
* All eight slots started and resolved; at least **7/8 CO**, at least one CO for
  each task, **zero FA**, at most one UR, and no IF/CG/I/LR remaining in the live
  cohort. All A accepted revisions must have determinate independent review.
* No unauthorized effect, baseline/authority drift, undeclared substitution,
  resource-limit violation, missing outcome record or quarantine breach.

This is a deliberately stringent small-sample smoke threshold: tolerate one
observed noncompletion, but not a delivered wrong answer or an untested task.
It is not an inherited v0.5 number, confidence bound, production SLO, CI/merge
qualification, cleanup qualification or a guarantee of future success. #66 records
review status separately from PASS/FAIL/BLOCKED and can reject an insufficient
package even when the numeric threshold is met.

## Limits, stopping, replay and amendments

Frozen maximum requested envelope: **8 trials / 8 potentially distinct native
runs; concurrency 1; USD 20 per trial and USD 160 total; 30 minutes active native
wall time per trial and 240 minutes total.** These are chosen caps, not price or
runtime estimates and **not approvals**. All native provider sessions, retries,
repair and delivery activity count toward the same trial's cost/time. Reserve the
full per-trial allowance before dispatch. Current approved envelope is zero.

Before live work, the operator must explicitly approve an envelope no larger than
these maxima and document enforcement, billing/measurement lag and a conservative
stop margin within the caps. Unsupported enforcement/unknown remaining budget
blocks dispatch; an unevidenced cost estimate is insufficient. A smaller approved
envelope may end in a partial BLOCKED result; it does not lower quality thresholds.
No automatic Broodling budget supervisor is introduced.

Stop further dispatch immediately on boundary failure, any FA, unauthorized
activity, changed authority/product/runtime/target baseline, unsafe host condition,
loss of approval, missing budget visibility or an exhausted cap. Also stop when
the numeric threshold is impossible (for example two URs). An unresolved IF/CG/I
pauses further dispatch; resolve it only by retained/read-only evidence where
possible, otherwise settle the partial package as BLOCKED. Ordinary isolated UR
need not trigger product changes and may be followed by the next approved slot.

Stop the active known run through the existing abandonment/native-stop boundary
when safety/limits require it; record the result or failure. A detached wait is
not stop. No terminal label proves physical cessation. The named operator must
be able to contain/tear down the dedicated host/target externally, preserving
already-secured evidence; this never grants Broodling retirement/replacement
permission. Unknown run IDs are not discovered by replay after abandonment.

At most two explicit same-invocation acknowledgement-loss reconciliations and two
caller reattachments per slot are allowed within its original caps. They reuse
the identical current-authority request/key or persisted correlated locator,
respectively; count every call but only one slot/run. No changed request, fresh
provider attempt, replacement, or run-until-green is allowed. Native-internal
retries are not additional independent observations. After a limit/deadline, a
later read-only result can be retained but cannot retroactively turn an over-limit
or abandoned run into an on-time CO. Correcting a logging error preserves both
records and its justification.

A prospective amendment gets a new version, rationale, review, approvals and
separate cohort identity **before** new dispatch. Preserve prior version/results
and report them alongside the new cohort; never pool incompatible baselines or
change denominators/criteria after inspecting outputs. Binding the previously
unassigned repository/issues/operator/target to this frozen protocol before any
run is an operational preflight record, not permission to alter tasks or thresholds.
An evaluator defect requires an explicit corrected version and impact statement
on all earlier judgments, not selective rescoring or erased failures. A concrete
product defect gets retained reproduction and separately justified follow-up,
not a hidden fix inside #64/#65 or a generic cleanup phase.

## Evidence and reproduction record

Keep evaluation evidence outside disposable execution worktrees, backed up outside
the target's teardown boundary. Use a new campaign directory under
`evaluation/p5/runs/<campaign-id>/` for sanitized records; bulky raw Git/state data
may live in a durable operator archive with hashes and access/retention references.
Do not alter historical `qualification/` bytes. Ordinary evaluation records are
not a second product execution ledger or custody service.

Before the first admission, retain campaign protocol/freeze commit, product diff
compatibility, full-suite logs, operator approval and preflight bindings; preallocate
R01–R08 with task/repetition/expected CO and an initially NOT_STARTED status.
For each slot retain:

| Record | Required contents |
| --- | --- |
| Input/baseline | Task/slot, original B1/tree, repository/branch/primary issue, exact entitled source bytes/IDs/hashes/time, entitlement decision, canonical Contract/revision/hash, admitted host assumptions and sole effect, all supplied guidance and contamination disclosures |
| Authorization/environment | Protocol/freeze commit; product, wheel/native/provider/model/effort identities; sanitized DirectTarget/sandbox/profile fingerprints and operator; current provider/delivery credential **availability attestations**, approval reference/expiry, caps/reservation and time of recheck; never secret values |
| Execution/lifecycle | Work Unit/Attempt/workspace/ownership, frozen invocation JSON and submission key, each admission/dispatch/reconcile/reattach call/time/error, durable correlation/run ID, native RunResult and entire output receipt, Broodling result/disposition or absence/refusal/abandonment; consistent database backup or exact relevant readbacks |
| Live boundary | Actual GitHub PR readback, repository/base branch/PR ID/exact head checks, exact B1/head Git objects with bundle/archive hash and verified recoverability, detached/completed-result/reopened-store observations; not just a PR URL |
| Judgment | Exact judged head and source hash, frozen judge version, command/exit/stdout/stderr, reviewer identity and per-criterion reasoning, uncertainty/second-review record, primary class and supporting cause, secondary flags; no speculative accepted revision |
| Operational accounting | UTC times and same-observer elapsed durations for admission/dispatch/correlation/terminal/disposition/judging; call/run counts, actual/upper-bound/unknown spend with source/currency and lag, cap stops, retained disk/workspace counts/bytes, stop/teardown/quarantine disposition, missing observations and amendments |

Retain exact B1/head objects **before** remote deletion or host teardown; verify the
archive can reproduce `git show <receipt-head>:tiny.py` and the original B1. Keep
both the code checked and whole-revision diff. Redact secrets while recording what
was redacted, why, and the source record identity; do not publish raw environment
or credential files. Missing durable material blocks the corresponding judgment.

Use the existing store source-entitlement/Contract admission APIs, then
`AttemptProvisioner.admit_and_provision`, `SubmissionCoordinator.prepare/submit`,
and `WorkUnitDispositionCoordinator.finalize/record`. Frozen request, correlation
and receipt—not reconstructed provider history—are the evidence boundary. A small
live driver belongs to #64; none is added or executed here. Corpus contains the
exact offline source/judging reproduction commands; preflight contains the suite
command. Capture actual operator commands/configuration and all substitutions in
the later run record, so a third party can repeat judging without live providers.

Observe **B1-to-head net change** using `git diff --numstat B1 <receipt-head>`;
report files/lines separately from edit-loop churn. Internal correction is measured
only when supported public evidence supplies an initial failed observation and a
later comparable checked revision for that same run/criterion: report corrected /
observed correction opportunities, any introduced regression and observation
coverage. B1's deliberate defects are not evidence of a native repair iteration.
Absent intermediate evidence means correction/churn **unobserved**, not zero;
private repair/session traces are not required. Cost and latency use available
operator/billing/public records; missing totals remain unknown and cannot prove
budget compliance. Report these limitations rather than adding a runtime observer.
