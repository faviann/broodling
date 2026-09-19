# P5 v6 — R01 settled FAIL / FA

**R01 is consumed and independently judged FA. Native outcome and Broodling
disposition are SUCCEEDED; the accepted revision violates frozen T1 behavior.
R02–R08 remain NOT_STARTED. No continuation is eligible or authorized.**

See the [final #83 evidence](../runs/2026-09-19-v6-r01/README.md),
[classification](../runs/2026-09-19-v6-r01/R01/classification.json), and
[consumed-slot handoff](handoff.md). Only completed native run
`01a0b9ee-476e-7b02-a216-ae885ac1db4b` was used for post-delivery judging.

| Identity | Value |
| --- | --- |
| Protocol / cohort | `p5-native-pr-v6` / `2026-09-19-v6` |
| Immutable protocol/setup freeze | `4a2b0b8a0340b747e805f51da218a77ad81278f9` |
| Compatible product baseline | `da5db3167eda0ef458cf0875b5a1c64138637b9d` (#82) |
| Compatible tested helper revision | `2fd972c17a4b4edff4c591dd1c8acc06e1b9e896`; 372 tests / 291 subtests |
| Accepted and judged revision | `248d67d35fc8fe6ac5dba9a0fb8cae831ae22631` |
| Frozen v1 judge | PASS, 18/18; independent full-criteria review FAIL |
| Parent / execution issue | [#62](https://github.com/faviann/broodling/issues/62) / [#83](https://github.com/faviann/broodling/issues/83) |

The [protocol](protocol.md) and [setup requirements](setup.md) remain frozen.
This README is mutable navigation. Earlier pre-admission claims are preserved
in the evidence package and Git history.

| Cohort | P | S | D | U | A | J_A | R01 | R02–R08 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| v6 | 8 | 1 | 1 | 1 | 1 | 1 | FAIL / FA | NOT_STARTED |
| Retained v5 | 8 | 1 | 1 | 1 | 0 | 0 | NOT_DELIVERED / IF | NOT_STARTED; #68 BLOCKED / NOT RUN |

The cohorts remain separate. The compatible v6 delivery boundary passes, but
the frozen FA rule makes a positive recommendation impossible for this cohort.
P5 readiness remains NOT REVIEWED by #66; no release verdict is inferred.
No R01 replacement, R02–R08 execution or continuation issue was created.
