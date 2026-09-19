# Prospective P5 v6 — freeze and current gate

**Prepared 19 September 2026. No v6 trial has started. No live execution is
authorized by this preparation. P5 readiness remains NOT REVIEWED.**

| Identity | Recorded value |
| --- | --- |
| Protocol | `p5-native-pr-v6` |
| Cohort | `2026-09-19-v6` |
| Immutable protocol/setup freeze | `4a2b0b8a0340b747e805f51da218a77ad81278f9` |
| Selected compatible product/dependency/test/helper baseline | `da5db3167eda0ef458cf0875b5a1c64138637b9d` (#82) |
| Next issue | [#83: prepare and execute R01 once](https://github.com/faviann/broodling/issues/83), live authorization pending |
| Parent gate | [#62](https://github.com/faviann/broodling/issues/62) |
| New target/repository/input bindings | Not yet created or verified for v6 |

The [protocol](https://github.com/faviann/broodling/blob/4a2b0b8a0340b747e805f51da218a77ad81278f9/evaluation/p5/v6/protocol.md)
and [baseline/setup requirements](https://github.com/faviann/broodling/blob/4a2b0b8a0340b747e805f51da218a77ad81278f9/evaluation/p5/v6/setup.md)
are frozen at the commit above. This README is mutable navigation and status,
not a replacement for those frozen bytes or a live evidence record.

## Preparation review

The source/provenance review found one evidenced reason for a successor: v5
R01's incompatible target GitHub CLI, followed by #82's pinned compatible image
and pre-admission check. The v6 freeze changes neither product code nor the frozen
corpus/judge, v2 decision rules, v3 operational policy, gateway/provider/model/
runtime or Broodling responsibility boundary. Fresh cohort/resources prevent
reuse of v5's consumed state and prior output. No new cleanup/model-tuning phase
or R02-R08 execution issue was created.

Baseline evidence is the retained #82 report: 356 tests and 270 subtests passed,
including the incompatible-target-before-admission regression. It is cited as
existing evidence, not a test run performed by this preparation. Reuse requires
actual relevant-tree/dependency compatibility; #83 must validate and record any
v6 helper/test adaptation and the actual target before admission. No independent
P5 readiness review, provider execution, target deployment or new admission was
performed here.

## Separate accounting and next gate

| Cohort | P | S | D | U | A | J_A | R01 | R02-R08 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Retained v5 | 8 | 1 | 1 | 1 | 0 | 0 | NOT_DELIVERED / IF | NOT_STARTED; continuation settled BLOCKED / NOT RUN |
| Prospective v6 | 8 | 0 | 0 | 0 | 0 | 0 | NOT_STARTED | NOT_STARTED; gated |

V5 remains settled at
[`445c6d773ab772cb00f98a274d4bb8fce3e21b84`](https://github.com/faviann/broodling/blob/445c6d773ab772cb00f98a274d4bb8fce3e21b84/evaluation/p5/runs/2026-09-19-v5-r01/README.md).
#79 and #68 stay closed; the partial PR, bundle and quarantined resources remain
retained, not repaired, replaced or reclassified. Report both cohorts separately.

Current order: **#82 complete → v6 freeze recorded → #83 non-provider setup and
compatible validation → fresh owner authorization for v6 R01 only → one R01 →
stop and record its outcome.** Only compatible successful R01 boundary/CO evidence
can justify a separately authorized v6 continuation. #66 may review a settled
incomplete package instead. Neither a freeze nor a green suite establishes live
PR delivery, task quality or P5 readiness.
