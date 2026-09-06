# Issue #3 — Q1, Q2, and Q6 external-integration qualification

**Qualification date:** 6 September 2026 (UTC)

**Scope:** Broodling issue #3 only

**Governing inputs:** target v0.3 §§2.3, 4.3, 5, 6.4, 14.1 and implementation plan v0.3 §4 P1

**Integration:** official async Python SDK `LocalTarget` → matching Rust sidecar → local one-run controller

**Verdict:** **Q1 BLOCKED; Q2 PASS; Q6 BLOCKED; G1-core remains BLOCKED**

The result is deliberately not converted into a Broodling workaround. The selected integration has
a supported admission-correlation and durable-terminal contract, but it does not have the supported
completed-occurrence recovery contract required by Q1 and consumed by Q6.

## Exact tested build and evidence

Zeroshot was clean at
[`d0909615d6ba3c179b58bce15a059f40400ec995`](https://github.com/the-open-engine/zeroshot/tree/d0909615d6ba3c179b58bce15a059f40400ec995)
(tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`). The official SDK wheel was built
unpatched from that checkout with the matching sidecar.

| Item | Exact value |
|---|---|
| SDK distribution/version | `zeroshot-rust 0.1.0.dev0` |
| wheel / SHA-256 | `zeroshot_rust-0.1.0.dev0-py3-none-linux_x86_64.whl` / `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| sidecar / SHA-256 | `zeroshot-rust 0.1.0` / `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Rust / Cargo | `rustc 1.97.0 (2d8144b78 2026-07-07)` / `cargo 1.97.0 (c980f4866 2026-06-30)` |
| Python / platform | `3.13.5` / Linux x86-64, glibc 2.41 |
| controlled leaf SHA-256 | `4e7bf555695b4ac1c0635b62f6177c1065478da680c1d448813cfab4e39dd241` |
| issue #3 harness SHA-256 | `7b4ef407ffce503a3f98155dd0491b256cf05b8cdbf15268df7e7b10e0d564db` |
| full run record SHA-256 | `1776dc4b82759450ff7eed8dc54f87d4fb87d276d20cbb6e9c5ddce230c925f5` |

The complete public status/watch/log replays, controlled-leaf invocations, run IDs, cursors,
source revisions, controller-kill evidence, and build identity are retained in
[`evidence/issue-3-run-record.json`](evidence/issue-3-run-record.json). Only the provider leaf was
controlled. SDK encoding and subprocess transport, Rust preflight/admission, source resolution,
controller, graph execution/reduction, observation, and force-stop were real. No SQLite reader,
log-derived authority contract, result copy, or second reducer was used.

## Results

### Q1 — BLOCKED: recoverable authority occurrences

The successful repeated-occurrence run `01a077aa-8edb-7063-9561-91ebd768ed06` reached durable
`finished` at `v2:50`. A new SDK client replayed two different opaque active selectors for the two
`adjudicate_authority` visits (`nv2-1324…` at `v2:14` and `nv2-fe6b…` at `v2:32`) and the final
authority selector at `v2:44`. This proves that retained watch history can distinguish visits while
they are projected as active. It does not expose their trusted bound inputs, successful/error/voided
outcomes, settled state, completion cursors, structural occurrences, or admitted graph/runtime
relationship.

The stopped run `01a077aa-9318-7151-891f-2073d9ce4340` requested force-stop while the second
adjudicator was active at `v2:32`, entered `stopping` at `v2:33`, and durably finished
`force_stopped` at `v2:36`. The failed run is the independently killed-controller witness
`01a077aa-968b-7d73-b49f-b579a270aac8`, which a fresh external observer durably terminalized as
`runtime_lost` at `v2:46`. Both replay after client restart, but neither supplies its settled
authority occurrences through the public API.

The source explains the observed boundary:

- Public [`RunStatus`, `RunResult`, and `LogEvent`](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/sdks/python/src/zeroshot/runs.py#L48-L153)
  expose active selectors, terminal graph result, and safe logs only. Public `Run` operations are
  status, watch, logs, wait, and force-stop.
- Internally, [`StoredRun`, `RunSnapshot`, `NodeSnapshot`, and `NodeState`](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/v2_run_ledger.rs#L62-L175)
  retain the admitted run, occurrence, node-attempt field, input, settled outcome/voiding, and
  cursors. Internal [`get`, `get_by_submission_key`, and `snapshot_and_tail`](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/v2_run_ledger.rs#L338-L366)
  are not an external SDK/protocol contract.

The runtime node-attempt field was not treated as a Broodling Attempt. Final graph output and logs
were not treated as occurrence recovery. Therefore Q1 cannot pass.

### Q2 — PASS: ambiguous submission and source-sensitive reconciliation

A separate Broodling-side client process submitted key `broodling-issue3-q2-ambiguous-v1` and
exited with code 87 immediately after SDK submission returned, without communicating the handle or
run ID to its parent. A new client observed exactly one new run and resubmitted the exact request at
source `8356fc0614a201b5e3b16af72eb1807848a1dc73`; Zeroshot returned the same run
`01a077aa-8c7f-75d0-acc3-60a79bf64a4a`.

The fixture then committed a workspace/HEAD change to
`5011b804a04ac9685d779622d74eb2444302b17f` and resubmitted the same key. The SDK raised
`SubmissionConflictError`, whose `existing_run_id` was the original run. Inventory still contained
one run only. Thus an exact replay recovers the existing identity, and a source/digest conflict
explicitly reconciles to that identity without creating or authorizing another run or Attempt.
This matches the pinned local path's source-sensitive digest and
[`existing_submission` conflict behavior](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_cli/local.rs#L170-L281).

### Q6 — BLOCKED: terminalization works; semantic catch-up does not

Three external-path witnesses reached the required mechanics:

1. The stop witness above issued force-stop while the second designated adjudicator was in flight.
   Earlier routing through `repair` and the second `review` corroborates that the first adjudicator
   completed before the stop boundary; the exact settled authority record is not externally
   recoverable.
2. The harness sent `SIGKILL` to the uniquely matched one-run controller for
   `01a077aa-968b-7d73-b49f-b579a270aac8` while final assessment was active. A restarted SDK client
   reopened the run and observed durable `runtime_lost`; no later node became active. This matches
   the supported dead-controller observer behavior, which finalizes a nonterminal run without
   reconstructing a runtime ([source](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_portable_controller/controller.rs#L69-L95)).
3. A separate Broodling-side harness process exited with code 86 after the first adjudicator had
   successfully routed to an active `repair` execution in run
   `01a077aa-9bce-7fa1-b623-4d4f08c647b8`, but before semantic catch-up. A replacement client
   stopped and replayed the run after restart.

Public `finished` plus its cursor is a stable durable terminal boundary, and no post-terminal
dispatch was observed. However, the selected transport cannot return every successful authority
occurrence committed by that boundary, distinguish it from completed error/voided outcomes, or
supply the first adjudicator directive to current/replacement semantic state exactly once. The
controlled transcript and successor routing are witness corroboration, not a canonical recovery
contract. Repeating recovery therefore cannot reference the missing exact settled occurrence.
Q6 cannot pass.

## Retention, custody, and concrete dependency

This qualification applies only to the exact unpatched source/build above. The explicit
`LocalTarget.state_dir` is under operator custody; public terminal status/watch/log observations
replayed while that directory was preserved and readable by the pinned build. No broader retention
SLA or cross-version migration guarantee was found. Merely retaining the directory does not expose
settled occurrence facts through a supported integration, and its private SQLite layout is not an
acceptable contract.

**Missing integration contract:** a supported read/recovery interface for the retained admitted
graph/runtime relationship and exact settled execution occurrences, including stable occurrence
identity, trusted bound input, successful/error/voided outcome, and completion cursor/order. It
must work for successful, failed, and stopped runs and support cursor/watermark-style idempotent
catch-up while its documented retention/version guarantees hold.

**Smallest reproducer:** run the checked-in issue #3 harness against the pinned SDK/sidecar, then
inspect any `afterClientRestart` public replay in the retained run record. Repeated active selectors
are visible, but settled occurrence input/outcome is absent. The Broodling-crash witness is the
minimal consuming failure: `repair` proves the directive-bearing predecessor routed durably, yet a
fresh client cannot recover that predecessor occurrence or directive.

**Consumer:** G1-core remains blocked; P2 must not make product execution depend on Zeroshot, and P4
authority catch-up/replacement cannot proceed on a substitute. After Zeroshot supplies a supported
surface, rerun this same witness set. Issue #3 acceptance criteria are not all satisfied, so the
issue must remain open.

## Reproduction

Build the sidecar and wheel exactly as in the shared [P1 harness instructions](README.md), create
the same disposable Git workspace, then run:

```bash
P1_ROOT=/path/to/the/P1-build-directory
ISSUE3_ROOT=$(mktemp -d /dev/shm/i3.XXXXXX)
WHEEL=$(find "$P1_ROOT/dist" -maxdepth 1 -name '*.whl' -print -quit)
"$P1_ROOT/venv/bin/python" qualification/p1/issue3_qualify.py \
  --workspace "$ISSUE3_ROOT/w" \
  --state-dir "$ISSUE3_ROOT/s" \
  --evidence-root "$ISSUE3_ROOT/e" \
  --output qualification/p1/evidence/issue-3-run-record.json \
  --zeroshot-source /home/faviann/repos/zeroshot \
  --wheel "$WHEEL" \
  --cargo /home/faviann/.cargo/bin/cargo \
  --rustc /home/faviann/.cargo/bin/rustc
```

The concrete temporary paths are evidentiary, not required locations. Keep state/workspace paths
short enough for the local Unix-domain controller socket.
