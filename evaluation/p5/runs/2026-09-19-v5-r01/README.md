# Issue #79: R01 settled as infrastructure failure

**Disposition: BLOCKED after one counted R01. Primary class: `IF`. R02-R08
remain NOT_STARTED. R01 must not be rerun or replaced.**

The separately authorized `p5-native-pr-v5` R01 started at
`2026-09-19T04:23:12.780370+00:00`, admitted one Contract and dispatched one
Attempt. Broodling durably correlated that Attempt with Zeroshot run
`01a0b7e7-712e-7e42-9e6b-d0d0a2dec6ea`. Credential-free reconnection observed
the native terminal result `failed / change_attempts_exhausted`. Broodling
abandoned the Attempt and recorded no successful disposition or receipt.

## Frozen classification

R01 is `IF` under the frozen v1 classification adopted by v5. The configured
DirectTarget image contains GitHub CLI 2.23.0. Zeroshot 10.3.0 native PR
delivery invokes `gh api graphql --paginate --slurp` after creating or updating
the PR, but that CLI does not support `--slurp`. Durable event 60 records
`unknown flag: --slurp`; the same failure recurs at events 119, 173 and 219.
Read-only inspection after the run confirms the installed version and absent
flag. A downloaded GitHub CLI 2.101.0 binary exposed the flag, but the worker
could not replace `/usr/bin/gh` in the target.

This is a concrete target-installation compatibility failure that prevented the
required native result/receipt and Broodling disposition. It therefore meets
`IF`, rather than `UR`. It is not `FA`: Broodling never recorded `SUCCEEDED`.
The cause is established, so the record is not indeterminate.

The later native repair loop and terminal `change_attempts_exhausted` remain
secondary observations. After repeated delivery-status failures, the run's
current remote head acquired a generated `__pycache__/tiny.cpython-311.pyc` and
reviewers rejected its scope. Frozen rules prohibit inferring a stable accepted
revision from this failed run or converting its partial PR into a successful
delivery.

See [classification](R01/classification.json), the sanitized [durable-log
readback](R01/zeroshot-log-readback.json), [Broodling state](R01/broodling-state-readback.json)
and [target containment](R01/target-containment.json).

## Partial native GitHub effect

Native delivery created open [PR #2](https://github.com/faviann/broodling-p5-v3-20260917/pull/2)
against `p5-eval` and pushed branch `zeroshot/v2-6fbed5ea695f49366fd9`.
The first observed head was `1d49f231e35169ff8ac8ed82997d4a8df6747502`;
the current head is `64213ae12edcd571bc313bbe7920b66dcd6c17d8`.
The PR is open and unmerged. It is evidence of a partial authorized native
effect, not a receipt-backed delivery.

The exact GitHub readback is retained in
[github-delivery-readback.json](R01/github-delivery-readback.json). The verified
[Git bundle](R01/github-delivery.bundle) preserves B1, both native commits, the
remote branch and PR refs independently of continued GitHub retention.

## Accounting and stop decision

Final counts are `P=8; S=1; D=1; U=1; A=0; J_A=0`. R01 has evaluator outcome
`NOT_DELIVERED`, no accepted revision judgment and primary class `IF`. R02-R08
remain `NOT_STARTED` because v5 permits only compatible successful R01 boundary
evidence to unlock #68. Descriptive fractions are `S/P=1/8`, `CO/S=0/1`,
`CO/P=0/8`; `UR/S=0/1`; `FA/J_A=N/A`, `FA/A=N/A`, and `A-J_A=0`.

The exact DirectTarget container was stopped after read-only evidence capture.
Its bind-mounted state and home, the original Broodling store/workspace, and the
dispatched Attempt remain quarantined. No provider work was rerun during
classification. A separate compatibility issue must be resolved before any new
prospective P5 cohort; it cannot reopen or replace this R01.

## Preserved preparation provenance

The pre-admission snapshot remains sealed by `PREPARATION-SHA256SUMS`. It records
the corrected v5 freeze `52eb3569b3671baa37426792a67b50058e2d223f`, compatible
baseline `9c799de1b5cfff16082e65531933e7a249544282`, 351 passing tests and
267 subtests, the exact frozen R01 authority, B1
`884bd64264df1515bee76a63f548db9cabe25a35`, and all readiness checks that
preceded the counted run. Those preparation facts are provenance; this terminal
record supersedes its earlier `NOT_STARTED` handoff state for R01 only.
