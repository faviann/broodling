# P5 native authorized-PR evaluation protocol v6

**ID: `p5-native-pr-v6`. Cohort: `2026-09-19-v6`.**
**Prospective DirectTarget compatibility successor, 19 September 2026.**

V6 is a new, separately accounted cohort, not a resumption, replacement or
reclassification of v5 R01. At preparation, v6 R01-R08 are `NOT_STARTED`:
`P=8; S=0; D=0; U=0; A=0; J_A=0`. No live execution is authorized by this
preparation. The immutable freeze is the commit first adding this file and
[setup.md](setup.md); record its full SHA in the v6 navigation record and #62
before any admission. Do not edit frozen protocol bytes in place.

## Provenance and the only prospective change

The corrected v5 freeze remains `52eb3569b3671baa37426792a67b50058e2d223f`.
Its [R01 settlement](https://github.com/faviann/broodling/blob/445c6d773ab772cb00f98a274d4bb8fce3e21b84/evaluation/p5/runs/2026-09-19-v5-r01/README.md)
at `445c6d773ab772cb00f98a274d4bb8fce3e21b84` is final:
`NOT_DELIVERED / IF`, native run `01a0b7e7-712e-7e42-9e6b-d0d0a2dec6ea`,
no successful receipt or Broodling disposition, and
`P=8; S=1; D=1; U=1; A=0; J_A=0`. V5 R02-R08 remain `NOT_STARTED`;
#79 and #68 remain settled. That cohort cannot support a positive P5 recommendation.

Native delivery encountered GitHub CLI 2.23.0's missing `api --slurp` capability.
Completed #82, commit `da5db3167eda0ef458cf0875b5a1c64138637b9d`, supplies the
[corrected image and non-provider validation](https://github.com/faviann/broodling/blob/da5db3167eda0ef458cf0875b5a1c64138637b9d/evaluation/p5/direct-target-gh-compatibility.md).
V6 changes only the selected DirectTarget image to that pinned GitHub CLI
2.101.0 image and retains its actual-container compatibility check before
counting/admission. The new cohort identity and fresh operational bindings keep
this changed installation separate from the failed cohort. The workstation's
own `gh` version is not evidence about the target's `/usr/bin/gh`.

Preserve all existing v1-v5 protocol, setup, driver and evidence bytes as retained
at the #82 commit. In particular, do not edit v5's consumed-slot markers or its
historical pre-admission `NOT_STARTED` snapshots. Preserve the quarantined v5
Attempt/store/workspace, stopped container, original image and target state/home.
Leave [partial PR #2](https://github.com/faviann/broodling-p5-v3-20260917/pull/2),
its branch and retained Git bundle alone: do not update, close, merge, repair or
use its output as v6 input. The bundle preserves B1 and the observed native heads,
including `64213ae12edcd571bc313bbe7920b66dcd6c17d8`.

## Unchanged authority, profile and measurement

Follow [current governing authority](../../../docs/governing/current.md) and #62.
Except for this new cohort and the compatibility delta, inherit:

- v1's [corpus, exact task text and B1](../v1/corpus.md), fixture, judge,
  classifications, all-started accounting, live-boundary evidence and
  retry/reconciliation limits, frozen at
  `717e94b3548d1dc029bfb54e2758e9929f1d93bd`;
- [v2's claim-level decision rules](../v2/protocol.md), frozen at
  `1d581717509de182410c3ab2f54d3f65768d8d7f`, rather than v1's numeric threshold;
- [v3's operational policy](../v3/protocol.md), frozen at
  `de1da18479fdf846d04994190132357fcb0a87ef`, rather than superseded budget,
  wall-time and administrative preflight gates; and
- [v5's corrected gateway binding](../v5/protocol.md), frozen at
  `52eb3569b3671baa37426792a67b50058e2d223f`.

Keep Zeroshot **10.3.0**, Python SDK **10.3.0.post1**, Codex **0.153.4**,
standard `software-change`, native `pull_request` delivery through DirectTarget,
and exact Contract-authorized repository/target branch/B1. Keep one
`UniformRuntime` across all executable nodes: Codex harness, `gateway` provider,
`gpt-5.6-sol`, medium effort, size `small`, execution-scoped sessions.

Dispatch supplies current `GATEWAY_API_KEY`,
`GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1` and `GH_TOKEN` only in
the SDK environment, including identical authorized acknowledgement-loss replay.
No `OPENAI_API_KEY`, endpoint substitution or conflicting legacy credentials.
Keep credentials out of persisted requests, Contracts, runtime plans, logs and
evidence. Reconnect/wait/stop uses the persisted DirectTarget origin without
current dispatch credentials. That origin is not the gateway URL.

The original B1 remains `884bd64264df1515bee76a63f548db9cabe25a35`; the target
branch remains `p5-eval` at B1 throughout. The eight serial slots remain:

| Slot | Task | Repetition | Initial state |
| --- | --- | --- | --- |
| R01 | T1 strict port parsing | 1 | NOT_STARTED |
| R02 | T2 stable deduplication | 1 | NOT_STARTED |
| R03 | T3 CSV rendering | 1 | NOT_STARTED |
| R04 | T4 frozen access authority | 1 | NOT_STARTED |
| R05 | T4 frozen access authority | 2 | NOT_STARTED |
| R06 | T3 CSV rendering | 2 | NOT_STARTED |
| R07 | T2 stable deduplication | 2 | NOT_STARTED |
| R08 | T1 strict port parsing | 2 | NOT_STARTED |

R01 is both the sole v6 boundary trial and its first preregistered quality slot.
It is not an extra warm-up run. All admissions are criteria-only. No worker
receives frozen judging/reference material or prior-cohort results. No task,
criterion, judging case, repair policy or runtime selection is tuned to v5 output.

## Execution gate and reporting

The next execution issue combines only fresh v6 setup and the one R01 boundary.
It requires the [compatible baseline and setup checks](setup.md), then fresh owner
live authorization. Creating that issue, this freeze, setup checks or #82 closure
is not authorization to admit or dispatch. No provider task is a preflight probe.

R01 starts at its first admission attempt, with evidence/start accounting written
before that operation. A pre-admission setup failure may be corrected without
consuming the slot; once started it cannot be rerun or replaced. Preserve v1's
bounded identical-invocation reconciliation and credential-free reattachments;
these are not new independent trials or controller resurrection.

Successful boundary evidence must include criteria-only admission, frozen
invocation, durable current Attempt/run correlation, a detached caller consuming
an already-completed result before first disposition, matching native PR receipt
and atomic Broodling disposition, then a reopened store returning the identical
retained result/disposition without re-execution. Corroborate the actual PR and
exact receipt `headRevision`, retain its Git objects, and independently judge that
revision against the frozen criteria and B1. Never infer acceptance from an open
PR, mutable tip or worker report.

Only compatible successful R01 boundary evidence and its determinate correct
judgment can make a v6 R02-R08 continuation eligible. Stop the R01 issue before
R02. Do not reopen closed v5 #68; create a minimal v6 continuation only after that
gate is met and separately authorized. #66 may review either cohort's settled
incomplete package without demanding extra runs. Keep review status separate
from PASS/FAIL/BLOCKED and release decisions.

Apply the unchanged v2 positive gate and stop rules: account for all eight slots;
judge every accepted revision; demonstrate each task with CO; tolerate no FA or
remaining IF/CG/I/LR; at most one isolated UR with its paired repetition CO may
remain for skeptical review. Stop when that gate is impossible or authority,
compatibility, safety or evidence integrity is lost. Raw counts/fractions remain
descriptive, with zero denominators N/A. Report v5 alongside v6, never pool their
denominators or hide v5's failure behind a later success.

Broodling's product boundary is unchanged. Zeroshot owns execution/review/repair
and authorized native delivery; Broodling owns admission/current authority/exact
effects/receipt validation/lifecycle disposition. No-effect stable-result refusal
and quarantine of every dispatched Attempt remain. External containment grants
no cleanup or replacement authority. No supervisor, graph/adjudicator machinery,
new CLI/service, broader effects, model tuning or product changes are authorized.
