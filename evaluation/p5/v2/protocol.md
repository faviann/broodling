# P5 native authorized-PR evaluation protocol v2

**ID: `p5-native-pr-v2` — prospective amendment, author-reviewed, 17 September 2026.**  
**Live preflight: BLOCKED. Authorized dispatches: 0. Authorized spend: 0.**

This version prospectively supersedes `p5-native-pr-v1` for any future #64/#65
live dispatch. Version 1 remains frozen and unchanged at commit
`717e94b3548d1dc029bfb54e2758e9929f1d93bd`; no v1 live trial was started and no
provider/PR evaluation result was observed before this amendment. The product
baseline remains `b901367b339c3ca715fa51ad91646b499a8fa068`.

Except for the acceptance-decision and threshold language below, v2 adopts v1's
frozen [corpus and task definitions](../v1/corpus.md), [fixture](../v1/fixture),
[independent judge](../v1/judge.py), [preflight](../v1/preflight.md), classification
rules, denominators, run retention, retry/reconciliation treatment, budgets,
stopping/safety rules, evidence layout and reproduction requirements. Later run
records MUST identify `p5-native-pr-v2` and the immutable v2 freeze commit.

## Why this amendment exists

Issue #63 deliberately leaves whether and how to use quantitative thresholds as a
P5 protocol choice. The v1 corpus is intentionally small and purposive: four task
families exercise four distinct failure opportunities, with two repetitions each
to expose gross instability and fixed-order effects at bounded cost. The eight
trials are **not** a random or statistically independent population sample.

Accordingly, this corpus cannot support a statistically meaningful success-rate
or reliability threshold. In particular, `7/8` must not be interpreted as
`87.5% reliability`, a confidence bound, a production SLO or an estimated future
success probability. Raw ratios such as `CO/S`, `CO/P`, `UR/S`, `FA/J_A` and
`FA/A` remain useful descriptive observations and MUST still be reported under
v1's counting rules, but no percentage is the P5 acceptance threshold.

The prospective gate is therefore claim-level: establish the supported boundary,
exercise every selected task/failure opportunity, reject delivered wrong answers,
and retain any observed noncompletion without converting a tiny sample into a
statistical reliability claim.

## Frozen corpus and coverage decision

The corpus remains eight serial slots exactly as frozen in v1:

- T1 strict port parsing, two repetitions;
- T2 stable deduplication, two repetitions;
- T3 CSV rendering, two repetitions;
- T4 frozen access authority under conflicting lower-authority guidance, two
  repetitions.

R01 remains the #64 boundary trial and the first preregistered quality observation.
It counts once. No provider substitution, seeded repair campaign, replacement of a
dispatched Attempt, ninth confirmatory run, or rerun-until-green is authorized.
A failed or blocked R01 still prevents R02–R08 from starting.

The corpus size is justified as **bounded coverage**, not estimation: four tasks
cover materially different correctness/authority failure opportunities and the
second repetition checks whether the observed behavior is at least reproducible
enough to avoid an obvious one-off success. Adding more trials solely to create a
percentage would not make this deliberately selected corpus statistically
representative.

## Prospective P5 evidence decision rules

A positive bounded authorized-PR smoke-readiness recommendation may be considered
by #66 only when **all** of the following evidence rules hold:

1. **Controlled seam and live boundary are established.** The supported full
   default suite passes with no missing SDK/native coverage, and #64 establishes
   the real authorized-PR boundary on the recorded compatible baseline/target,
   including exact receipt-backed disposition and required reconnection evidence.
2. **The whole frozen corpus is accounted for.** All eight slots are started and
   resolved under the frozen rules; all accepted revisions receive determinate
   independent judgment; there are no hidden, discarded, replaced or selectively
   rerun observations.
3. **Every selected task claim is demonstrated.** Each of T1–T4 has at least one
   `CO` observation. A task with no correct observation leaves that selected claim
   unsupported and blocks a positive recommendation.
4. **Delivered wrong answers are not tolerated.** There are **zero `FA`**
   observations. Any false acceptance stops further confirmatory dispatch and
   blocks a positive recommendation for this cohort.
5. **Uncertainty is not converted into quality evidence.** No `IF`, `CG`, `I` or
   `LR` may remain in the completed live cohort for a positive recommendation.
   Such observations remain explicit evidence and settle the package as blocked
   or insufficient rather than being removed from denominators.
6. **Noncompletion is treated as a concrete failure observation, not a percentage
   allowance.** At most one isolated `UR` may remain for #66 to assess, and only
   when the other repetition of that same task is `CO`; a second `UR`, two
   non-correct repetitions of the same task, or a `UR` that leaves a task without
   a `CO` blocks a positive recommendation. An isolated `UR` remains a real
   product/workflow failure and MUST be called out as a limitation; #66 may still
   reject readiness if its cause materially undermines the authorized-PR claim.
7. **Authority and operational integrity hold.** There is no unauthorized effect,
   baseline/authority drift, undeclared substitution, resource-limit violation,
   missing required record, or quarantine breach.

These are explicit evidence/claim rules for this small campaign. They do not make
an isolated `UR` statistically acceptable, estimate an error rate, or require #66
to issue a positive recommendation merely because the minimum evidence pattern is
present. #66 remains the skeptical review of whether the retained evidence really
supports the scoped authorized-PR readiness statement.

## Counting and reporting

Retain v1's exact definitions of planned/started/dispatched/run/disposition/judged
counts and all primary classes (`CO`, `FA`, `UR`, `LR`, `IF`, `CG`, `I`). Keep
native result, Broodling disposition and independent judgment separate.

Report the same raw counts and descriptive fractions required by v1. Label them
**descriptive sample observations**, not threshold scores or reliability estimates.
Zero denominators remain `N/A`. Unstarted, interrupted, missing and indeterminate
observations remain visible. Controlled diagnostics never inflate the live corpus.

The predeclared outcome pattern, not a percentage, controls the gate. In
particular, `7/8 CO` has no special independent meaning in v2: the only permitted
seven-correct pattern is the one implied by the claim rules above—one isolated
`UR`, its paired repetition `CO`, every other task represented by `CO`, zero `FA`,
and no unresolved non-quality class.

## Stopping and amendments

All v1 safety, authority, budget, replay and amendment rules remain in force.
Replace v1's phrase "stop when the numeric threshold is impossible" with:

> **Stop further confirmatory dispatch when the frozen claim-level positive gate
> has become impossible for this cohort.**

Examples include any `FA`, a second `UR`, both repetitions of one task becoming
non-correct, or any other observation that makes one of the prospective evidence
rules above impossible to satisfy. An unresolved `IF`/`CG`/`I` still pauses
further dispatch under v1's rules; resolve only through retained/read-only evidence
where possible or settle the partial package as BLOCKED.

Future changes to corpus, criteria, classifications, evidence requirements,
provider/runtime selection or these decision rules require another prospective
version before dispatch. Preserve v1 and v2 provenance and never rescore or erase
observations merely to recover a passing pattern.

## Preflight and authorization status

This amendment changes no production code, dependency, provider/model selection,
corpus input, evaluator, budget cap, effect authorization or live infrastructure.
The v1 preflight blockers remain unresolved until separately evidenced. The v1
requested maximum envelope (8 serial trials, USD 20/trial and USD 160 total,
30 minutes active native time/trial and 240 total) remains a cap rather than
approval. **Approved live runs and spend remain zero.**

#64 therefore remains **NOT READY for live execution** until the existing preflight
blockers are satisfied and operator authorization explicitly acknowledges the
immutable v2 freeze commit. This amendment itself performs no live provider call,
PR delivery, credential probe or target setup.
