# Issue #21 — cessation capability assessment

Assessment date: 8 September 2026. Implementation status: IN PROGRESS.
Issue #21 is not complete; no G4 verdict is recorded.

The phase entered at `8838d98696822d31f8bbf96633ac925c15541124`, with
G3-V1 PASS recorded by #20. The current v0.5 target and plan govern, followed
by G1 qualification, the P2/P3 implementation, G2/G3 evidence and #21.
Historical governing and qualification records remain unchanged.

## Established boundary

The current product profile does not yet provide a supported proof that
controller loss leaves no surviving real-provider writer:

- [W5's retained limits](../v1-p1/issue-8-w1-w2-w5-w7.md) explicitly scope
  cessation to controlled leaves. Its
  [launcher](../v1-p1/bin/codex) used a parent watchdog and Bubblewrap
  `--unshare-pid` / `--die-with-parent` containment.
- The current [product launcher](../../broodling/codex_bin/codex) directly
  executes real Codex. The retained actual-provider confinement evidence
  addresses normal completion; it does not establish controller-loss safety.
- At pinned Zeroshot revision `d0909615d6ba3c179b58bce15a059f40400ec995`,
  `zeroshot-rust/src/native_v2_portable_controller/controller.rs` opens a dead
  controller for observation with `runtime=None`. Its
  `destroy_or_confirm_absent` returns confirmed when no runtime is present,
  without inspecting or terminating provider OS processes.
- The same revision's `execution/process/platform_unix.rs` configures process
  groups, without a provider parent-death barrier. Controller SIGKILL bypasses
  cleanup owned by the killed process.
- Public SDK `Run.force_stop()` returns an already-stored terminal result.
  Repeating it after `runtime_lost` supplies no additional cessation proof.

These are source and retained-evidence findings, independently inspected by the
parent and an issue-scoped read-only subagent. They are not a newly executed
real-provider orphan experiment and do not relabel G1/G2/G3.

An ambiguous submission has an additional fail-closed boundary: `list_runs()`
does not expose the submission key or complete immutable request identity.
Ordinary submission replay can launch work if the first dispatch was never
accepted. It cannot be used unconditionally after abandonment merely to locate
an old run. Unknown existing-run identity remains blocking.

## Unresolved decision

The user has been asked whether to extend the actual product launch profile
with bounded parent-death/descendant containment and requalify the affected
controls, or retain the profile and block dispatched retirement/replacement
pending a stronger supported runtime capability. No choice is inferred from
silence.

Affected evidence for a profile extension includes read-only assurance,
mutator confinement, same-repository sibling/shared-Git protection, detached
writer cessation, controller loss, no-effect/network controls and all five
distinct #21 fault windows. One compatible product configuration must supply
the evidence; controlled and actual-provider observations remain distinct.

Independent work is limited to irreversible durable abandonment/currentness,
stale administrative/capture fencing, and a public stop adapter whose terminal
diagnostics do not authorize retirement. No replacement, disposition or later
issue implementation is authorized before #21 is complete.

## Implemented foundation

Schema 5 adds one immutable abandonment reason/time row. Its insertion removes
currentness in the same transaction; SQL prevents reversal, rebinding and
`INSERT OR REPLACE` resurrection. Ordinary admission cannot exploit the empty
current slot. Published v2/v3/v4 migrations retain original rows and bindings;
test DDL is checked against the published schema digests.

Provisioning checks currentness before creating scaffolding and again after
acquiring its existing enclosure lock. The host mutation and acknowledgment
share the currentness transaction. Submission retains its existing transaction
around the external dispatch/correlation call and uses the centralized
currentness check. P3 capture still checks currentness before and after its
external observation; retained historical custody remains readable.

`ZeroshotSubmitter.stop_known()` uses only the public known-run stop/status
operations. Its result is diagnostic-only and carries no semantic output or
cessation/retirement authority. The caller must first durably abandon the
Attempt; this adapter is not yet a complete product abandonment coordinator.

The independent review found an SQL replacement bypass during implementation.
A duplicate-Attempt insertion guard and cross-Work-Unit regression now close it;
the reviewer reran the regression and found no remaining blocker within this
partial foundation scope. A separate test races a paused external submission
acknowledgment against abandonment and checks permanent stale authority afterward.

Product retirement, a complete stop coordinator, supported real cessation,
the five actual product stop/loss witnesses, and #21 completion remain pending.
There is no new profile, runtime ledger, session manager, candidate seal,
replacement admission or disposition machinery in this foundation.

## Foundation verification and limits

- [Full SDK-free regression](evidence/issue-21-foundation-without-sdk.txt):
  **287 tests and 2,063 subtests passed; 33 SDK-dependent tests skipped**.
  Command: `.venv/bin/python -m pytest tests -q`.
- [Affected pinned-SDK regression](evidence/issue-21-foundation-sdk.txt):
  **119 tests and 84 subtests passed, no skips**. The selected files cover
  abandonment, stop adapter, current observation, submission/crashes/migration,
  P3 custody storage/completeness, Attempt crashes and worktree provisioning.
  The existing SDK submission/crash cases use the actual official SDK/sidecar;
  stop adapter cases use explicit SDK doubles. No real-provider cessation
  witness is claimed.
- Changed-file Ruff checks and formatting pass, excluding the pre-existing
  `store.py` `__enter__` annotation rule `PYI034`; `git diff --check` passes.
- The first SDK-free run overlapped an in-progress schema edit, causing 19
  child-process tests to reject different parent/child schema digests. The
  source was frozen and the complete suite rerun successfully above. An early
  pinned-SDK baseline run was deliberately interrupted when implementation
  began; it is not claimed as a completed regression.

The SDK source SHA-256 remains
`0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e`,
version `0.1.0.dev0`, and sidecar SHA-256 remains
`9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.
Host: Linux `6.17.13-2-pve`, Python `3.13.5`, SQLite `3.46.1`.
The product graph, runtime declaration, Codex profile/launcher, evidence leaf,
final custody implementation and every governing/G1/G2/G3 record are unchanged
from the phase-entry commit. The provider remains the previously qualified
Codex `0.153.4` / `gpt-5.6-sol` low-effort profile; no new real-provider run was
performed for this foundation.

These checks support only the landed foundation. They do not satisfy all #21
acceptance criteria, authorize #22 or establish G4-V1 PASS.
