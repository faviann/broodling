# P5 native authorized-PR evaluation protocol v3

**ID: `p5-native-pr-v3` — prospective operational-policy amendment, 17 September 2026.**  
**No live P5 provider trial has started.** Version 1, version 2, and the 17 September
blocked preflight record remain preserved provenance and are not reinterpreted as
live evidence.

This version supersedes `p5-native-pr-v2` only for **future** P5 live dispatch.
It does not change the frozen corpus, task criteria, evaluator, outcome classes,
counting rules, exact-revision judging, or v2 claim-level evidence gate. It changes
the operational prerequisites that had become hard blockers during planning.

The immutable freeze identity for a future run is the commit that first adds this
file. Record that commit in the campaign evidence and tracker before the first
admission. Later documentation-only commits do not silently change this protocol.

## Authority and rationale

Follow [`docs/governing/current.md`](../../../docs/governing/current.md), issue
#62, current production code/tests, then this protocol. Issue #60 intentionally
re-established Broodling as a thin native integration: Broodling owns admission,
current Attempt/run authority, exact effect authorization, receipt validation and
lifecycle disposition; Zeroshot owns standard workflow execution, review, repair,
provider sessions and authorized native PR delivery.

The v1/v2 preflight correctly identified facts that matter to a live evaluation,
but it also promoted operator process controls into mandatory P5 gates. The current
product does not own a budget supervisor, campaign deadline, DirectTarget sandbox
policy, target teardown system, reviewer-assignment system or evidence-archive
service. P5 should verify the current product boundary and produce interpretable
evidence, not add those systems by prerequisite.

The owner policy for these first actively monitored real-provider runs is:

- the eight-trial corpus remains bounded and preregistered;
- there is **no P5-specific dollar ceiling or wall-time ceiling**;
- the owner actively supervises execution and can stop the target;
- operational setup and verification should be automated where practical rather
  than converted into Broodling product requirements.

Observed spend and elapsed time remain useful descriptive evidence when available;
they are not dispatch authorization or acceptance thresholds.

## Measurement rules preserved from v1/v2

The following remain frozen exactly as already defined unless a later prospective
measurement amendment explicitly changes them before any affected dispatch:

- eight serial slots R01-R08, four tasks with two repetitions each, in the frozen
  order from v1;
- R01 is the live boundary trial and first quality observation, counted once;
- criteria-only admission, original B1, one authorized `pull_request` effect and
  standard `software-change` workflow;
- production-selected real-provider execution for every counted live run;
- no provider substitution, seeded repair campaign, replacement of a dispatched
  Attempt, ninth confirmatory run or rerun-until-green;
- retain every started slot and keep native result, Broodling disposition and
  evaluator judgment distinct;
- judge the exact receipt `headRevision` against the original frozen criteria and
  B1, never the mutable PR tip or worker self-report;
- v1 outcome classes/counting and v2's claim-level evidence decision rules,
  including zero false acceptance for a positive recommendation and the existing
  treatment of UR/IF/CG/I/LR;
- no-effect stable-result refusal and permanent Broodling quarantine of dispatched
  Attempts remain current product limits, not evaluation defects to patch around.

The 17 September v2 blocked record at
`evaluation/p5/runs/2026-09-17-v2-preflight/` remains evidence that no R01-R08 slot
was started under v1/v2. Its B01 default-suite result may be reused prospectively
when the production/dependency/test trees are unchanged; it does not become live
boundary or quality evidence.

## Current hard prerequisites for future live dispatch

These are required because they follow from the current product boundary or are
needed to interpret the evaluation honestly and safely. They are setup facts, not
new Broodling features.

1. **Known compatible baseline.** Record the product/dependency/protocol identity.
   A supported full-suite result with SDK/native coverage must exist for that
   product/dependency/test tree. Reuse the retained 330-test B01 result when tree
   identity proves it is still applicable; rerun only when the relevant baseline
   changed or the retained evidence is unusable.
2. **Authorized disposable GitHub target.** Use a designated disposable repository
   and target branch at the frozen B1. The Contract must carry exactly the
   authorized PR effect. Setup tooling may create/reset/verify the repository,
   branch and fixture; the resulting identities are recorded before the affected
   trial dispatch.
3. **Real product inputs for each slot.** Each trial must have the source
   attribution/entitlement, GitHub primary issue, Work Unit, immutable Contract,
   B1, current Attempt and dedicated local workspace required by the current
   Broodling APIs. These identities need not be manually preallocated. Evaluation
   tooling may generate and record them from the frozen corpus just before use,
   while preserving the frozen task text and one-Attempt/quarantine rules.
4. **Configured DirectTarget and current credentials.** A DirectTarget must be
   configured for the selected PR runtime, with provider authentication and a
   current delivery credential capable of the authorized repository/branch effect.
   Record sanitized target/runtime identities and the verification performed. Do
   not inspect or retain secret values. Exhaustive target-package/version attestation
   is not required when the supported runtime can be exercised as configured.
5. **Active operator stop capability.** The owner/operator supervises live work and
   can stop/contain the DirectTarget externally if needed. There is no requirement
   for a P5-specific time limit, formal teardown runbook, reserved stop margin or
   Broodling supervisor. The bounded corpus and serial execution limit exposure;
   preserve enough host/storage capacity for the known quarantined Attempts.
6. **Independent judging without worker leakage.** Frozen judge/reference material
   must not be supplied to the implementation worker. After an accepted result,
   independently evaluate the exact retained revision and preserve the reasoning
   and outputs outside the disposable execution checkout. A separate human,
   credential-free host, network-disabled sandbox or formal reviewer assignment is
   not intrinsically required; use them only when they materially improve the
   evaluation.
7. **Durable-enough evidence.** Preserve the protocol/baseline, setup transcript,
   source/Contract/B1 identities, run/result/receipt/disposition records, exact
   judged revisions and sanitized target facts outside disposable execution
   workspaces. Retain enough Git material to reproduce the exact-revision judgment
   after target cleanup. No long-term custody service or archive SLA is required.

## Setup is not a trial and should be automated

Automate the repeatable preparation and verification above where practical:
fixture repository/branch preparation, task issue/input creation, Broodling record
creation, baseline identity checks, DirectTarget configuration checks, evidence
directory initialization and post-run evidence capture. Such helpers are
**evaluation tooling**, not new production admission requirements or APIs.

A setup check may be retried before the first admission of a slot without consuming
that slot. Do not make a provider task run merely to satisfy preflight. If a fact
can only be established by the real trial itself, do not invent a stronger
preflight gate: start the preregistered slot when the reasonable non-effectful
checks above are satisfied and classify the actual outcome under the frozen rules.
Once a slot reaches its first admission attempt, v1/v2 started-slot accounting and
no-rerun rules apply.

## Requirements relaxed or removed from the old B01-B09 preflight

| Old item | v3 disposition | Reason |
| --- | --- | --- |
| B01 supported suite | **Retained** | Needed to separate known product regressions from live-target observations; existing retained result can be reused when the relevant tree is unchanged. |
| B02 repository/branch | **Retained, setup relaxed** | Exact authorized PR authority is part of the product boundary; manual pre-provisioning is not. |
| B03 entitled allocations | **Retained, setup relaxed** | Entitled source/issue/Work Unit/Contract/B1/Attempt facts are real product inputs; eight manually preallocated IDs and administrative reservations are not. |
| B04 DirectTarget/provider | **Retained, attestation relaxed** | A real compatible target/provider is essential to the live claim; exhaustive operator profile/package attestations are not product requirements. |
| B05 sandbox/trust inventory | **Relaxed** | DirectTarget sandbox policy belongs to its operator. Preserve only assumptions material to safety or evaluation contamination, especially keeping hidden judging material out of worker inputs. |
| B06 authentication/credentials | **Retained, paperwork relaxed** | Current provider and delivery authority are necessary to execute the supported PR path; formal approval references/expiry attestations are not. |
| B07 run/spend/time approval | **Removed as a P5 hard gate**, except exact PR effect authority | No P5-specific dollar or wall-time ceiling is desired. The fixed corpus bounds trial count; the owner actively supervises and may stop the target. No budget-enforcement, billing-lag or stop-margin prerequisite is required. |
| B08 containment/teardown/capacity | **Relaxed** | Active external stop/containment capability and sufficient capacity for the bounded quarantined corpus are enough. A formal teardown procedure, reserved thresholds and free-space accounting after every slot are not MVP/P5 requirements. |
| B09 judging/retention | **Retained in minimal form, paperwork relaxed** | Independent exact-revision judging and durable-enough evidence are necessary for an honest evaluation; an approved offline sandbox, separately assigned reviewer and archive-owner/retention bureaucracy are not inherently necessary. |

The prior `USD 20/trial`, `USD 160 total`, `30 minutes/trial` and `240 minutes total`
figures remain historical v1/v2 planning values only. They do not constrain v3.
Likewise, the prior `0 runs / USD 0` approval state explains why the recorded v2
attempt stopped before admission; it is not a permanent product authorization
model carried into v3.

## Stop and safety rules

Stop or pause further live dispatch when the owner/operator chooses to stop,
external conditions become unsafe, the target/effect authority changes, the
product/runtime baseline becomes incompatible, evidence integrity is lost, an
unauthorized effect occurs, or the frozen v2 claim-level positive gate becomes
impossible. Preserve the resulting partial evidence honestly.

Use the existing native stop/abandonment boundary for a known run when appropriate,
and external DirectTarget containment when necessary. Neither a native terminal
label nor operator teardown grants Broodling cleanup/replacement authority for a
dispatched Attempt. Do not add an automatic supervisor to satisfy this protocol.

Do **not** stop merely because a P5-specific dollar/time budget was not predeclared,
a billing feed is unavailable, a formal operator signature is missing, or the
DirectTarget lacks a separately documented sandbox/teardown policy, provided the
actual retained prerequisites above are satisfied and the owner continues active
supervision.

## Evidence and prospective changes

For future live evidence, record this protocol's immutable freeze commit, the
compatible product/dependency baseline, the automated/manual setup results, the
actual repository/branch/target identities, each slot's product records and run
identity, native result/receipt, Broodling disposition, exact judged revision and
classification. Record cost/latency/correction/churn only where observable and
state missing observations as unavailable rather than turning them into blockers.

Any later change to the frozen corpus, task criteria, provider/runtime selection,
outcome definitions, counting rules or v2 claim-level acceptance rules still
requires a new prospective protocol version before affected dispatch. Operational
setup can improve without a new version when it does not change those measurement
semantics or the product boundary.

No live provider run, PR delivery, credential probe or target mutation is performed
by this protocol amendment itself.
