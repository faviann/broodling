# Independent retained-boundary audit: v6 R01

Reviewer/session: `/root/boundary_audit`, an independent delegated audit session,
2026-09-19. This reviewer did not implement or participate in the run's native
review or repair. The audit covered retained boundary and accounting evidence;
it did not inspect candidate code or another reviewer's candidate findings and
does not replace independent full-criteria outcome judgment.

Fixed native run: `01a0b9ee-476e-7b02-a216-ae885ac1db4b`.
Frozen v6 protocol/setup commit:
`4a2b0b8a0340b747e805f51da218a77ad81278f9`.

**Finding:** no blocking defect was found in the audited retained admission,
invocation, correlation, detached consumption, receipt/disposition, reopened-store
replay, compatibility or quarantine evidence. Actual GitHub corroboration,
retained exact-revision Git material and independent full-criteria judgment are
separate requirements; this audit alone does not establish `CO` or P5 readiness.

## Authority and admission

The audit read the frozen v6 protocol and setup requirements, inherited v1 corpus,
classifications and counting rules, v2 claim-level gates and v3 operational policy.
The current v6 protocol/setup bytes match their immutable freeze, and tracked
v1-v5 protocol/helper material has no diff from the selected #82 baseline.

[Launch preflight](../launch-preflight.json) at
`2026-09-19T13:49:55.332891+00:00` precedes the
[execution-start record](execution-start.json) at
`2026-09-19T13:49:55.356750+00:00`, which precedes the
[admitted decision](admission-decision.json) at
`2026-09-19T13:49:55.370019+00:00`. The retained driver writes the start/count
record before its first admission operation. The start record identifies the
separate authorized start action and active supervision; this audit does not
claim to independently observe historical operator presence.

[Input records](input-records.json), the
[prepared issue body](../../2026-09-19-v6-setup/R01/issue-body.md),
[Contract](../../2026-09-19-v6-setup/R01/contract.json) and
[invocation](invocation.json) preserve exact frozen T1 scope and behavior. The
Contract is caller-constructed and criteria-only: empty validation fields,
obligations and prerequisites, no selected final materials, and the sole native
PR effect against `p5-eval`. The entitled source bytes and content digest match
the preparation; its stored entitlement is `broodling_policy` /
`primary_authoritative_work_reference`. The Contract's domain-separated product
digest matches the persisted digest and input record.

The invocation fixes original B1
`884bd64264df1515bee76a63f548db9cabe25a35`, repository
`faviann/broodling-p5-v6-20260919`, target branch `p5-eval`, standard
`software-change` / `pull_request` and the frozen Codex/gateway/gpt-5.6-sol,
medium/small/execution runtime. Its persisted target origin is
`http://127.0.0.1:18768`. The tested SDK source supplies this as a
`UniformRuntime` and keeps dispatch credentials in the SDK environment.

## Durable correlation and completed-result handoff

The original store was opened using SQLite URI `mode=ro`:

```text
/home/faviann/.local/share/broodling-p5-v6/r01-inputs/broodling.sqlite3
```

`PRAGMA integrity_check` returned `ok`. It contains exactly one admission,
Attempt, worktree assignment, submission, final-assurance record and disposition.
There are zero abandonment, retirement and retry records.

The persisted request equals [invocation.json](invocation.json), and the SHA-256
of its stored canonical request string equals the hash in
[correlation.json](correlation.json):
`831099afb140e8379ee59c5291288bd0c1b32f14542440e313464d66d2aecaff`.
The submission is `correlated`; its submission key, Attempt ID and run ID match
the retained evidence. The Attempt's immutable bindings and assignment match
[attempt.json](attempt.json).

[Reattachment 1](reattachment-1.json) and
[detached completed consumption](detached-completed-result.json) identify a new
caller PID `478803`, distinct from launch PID `472291`. The native result was
already present with phase `finished` and cursor `v2:64` at
`2026-09-19T14:01:07.970175+00:00`. Consumption is recorded at
`2026-09-19T14:01:08.169865+00:00`, with first disposition absent. The first
disposition follows at `2026-09-19T14:01:08.185522+00:00`.

Source inspection confirms the retained tested driver observes finished status
through the persisted DirectTarget origin with an empty SDK environment,
consumes that result, checks that disposition is still absent, and only then
finalizes using the already-consumed result. It forbids current dispatch and
legacy credentials during reconnection. Only one of its two permitted caller
reattachment records is present. This is retained completed-result consumption,
not controller resurrection or a fresh trial.

## Receipt, atomic disposition and reopened store

[Native output](native-result.json), [receipt](receipt.json),
[disposition](disposition.json), stored final-assurance JSON and
[reopened-store replay](reopened-store-replay.json) agree on:

```text
runId:        01a0b9ee-476e-7b02-a216-ae885ac1db4b
repository:   faviann/broodling-p5-v6-20260919
targetBranch: p5-eval
pullRequestId: 2
headRevision: 248d67d35fc8fe6ac5dba9a0fb8cae831ae22631
```

The native result is successful with no failure; Broodling's recorded outcome is
`SUCCEEDED`. The persisted final-assurance JSON is byte-for-byte equal to the
disposition's `result_json`; its `acceptedRevision` equals the receipt head.
The persisted disposition fields equal the retained disposition. No receipt or
product record was manufactured or amended by this audit.

The previously completed reopened-store replay records identical result and
disposition, zero native waits, zero submissions and credential-free operation.
Source inspection confirms it closes the first store, reopens it, and uses a
submitter that raises if `wait` or `submit` is attempted. It checks both returned
disposition equality and retained justification equality. The audit independently
compared those retained values with the original store; it did not execute replay.

## Compatible baseline and retained resources

[Baseline verification](../../2026-09-19-v6-setup/baseline-verification.json) and
[tool validation](../tool-validation.json) retain the fresh supported suite:
372 tests and 291 subtests passed, with no skips or missing SDK/native coverage.
Relevant product, dependency, test and helper files have no diff from tested
commit `2fd972c17a4b4edff4c591dd1c8acc06e1b9e896`.
The retained driver's SHA-256 is
`4e8d9d9c6686467ebfbc60f4e308f077e530be38f6b84d3c524ad92d216d06e9`,
matching launch evidence. No suite rerun was needed or performed.

Every installed SDK package file listed in
[dependency verification](../../2026-09-19-v6-setup/dependency-verification.json)
was independently hashed and matched, including the native executable. The
cached official SDK wheel also matches its pinned hash.

Read-only Docker inspection confirms original v6 container
`6dcb51921a9b6a71e3275429c2459a735457860745aa1d1164330364c2953d6d`,
image
`sha256:3511d1b7134167b6a845bdc0536532a2265cba580e77b475e26d25d0382f4e5a`,
and only its original fresh v6 `target-state` and `target-home` mounts.
[Target verification](../target-verification.json) and launch preflight retain
the actual-container `/usr/bin/gh 2.101.0` / `api_paginate_slurp=true` check before
start. The v5 stopped container and original image remain present with the state
recorded in [historical preservation](../historical-preservation-after.json).

The original v6 store, source, dedicated Attempt workspace, target state and
target home remain present. The store now has `is_current=0` for the successful
Attempt; this is expected behavior of its
`work_unit_dispositions_remove_current` trigger. The admission snapshot's
`is_current=true` records earlier authority. Clearing current authority after
disposition does not release dispatched quarantine or permit replacement.

The setup `SHA256SUMS` verified completely. At audit time the historical live
preparation manifest differed only for `slots.json`, which had advanced to
delivered-awaiting-judgment. Historical zero-start statements remain preparation
facts; final status and its manifest must be retained separately or clearly
updated without rewriting that history. Host `du` could not traverse protected
target run directories, so its totals are incomplete and are not used as complete
resource accounting. Read-only `df` showed ample remaining capacity; no cleanup
was performed.

## Accounting and limits

Before exact-revision judgment, the audited counts are
`P=8; S=1; D=1; U=1; A=1; J_A=0`. R02-R08 remain `NOT_STARTED`.
A determinate independent judgment of the accepted revision makes `J_A=1`.
`CO` additionally requires actual exact PR corroboration, retained Git material,
all frozen automated checks and full-criteria review passing. An established
criterion violation in this accepted revision is `FA`; missing required exact
evidence cannot be promoted to `CO`.

If R01 is independently established as `CO`, descriptive fractions are
`S/P=1/8`, `CO/S=1/1`, `CO/P=1/8`, `UR/S=0/1`, `FA/J_A=0/1`, with
`A-J_A=0`. These do not establish campaign readiness or authorize R02.
V5 remains separately `NOT_DELIVERED / IF`,
`P=8; S=1; D=1; U=1; A=0; J_A=0`; denominators must not be pooled.

Checks performed were file reads and JSON comparisons; `git diff`/`git show`;
SHA-256 verification; read-only SQLite queries; sanitized `docker inspect`;
and filesystem existence/capacity inspection. No execution driver was invoked,
no provider work was run or replayed, no native reattachment or retained-result
replay was executed, and no remote issue was changed by this reviewer. The only
file written by this reviewer is this audit report.
