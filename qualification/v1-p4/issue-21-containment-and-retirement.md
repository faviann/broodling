# Issue #21 — containment and retirement implementation

Implementation status: **IN PROGRESS / profile decision required**.
Issue #21 is not complete. No G4 review or verdict is recorded.

The user authorized extending the actual provider launch profile with the
smallest W5-style parent-death/descendant-containment mechanism and rerunning
affected controls. This continuation starts from `b228b14689c25e16f2b717ec75f6bccf77ce06e7`.
The [earlier blocker/foundation](issue-21-cessation-boundary.md), governing pair,
G1/G2/G3 evidence and historical verdicts remain unchanged.

## Implemented and independently checked

The [implementation boundary](../../docs/implementation/v1-p4-abandonment.md)
describes the new physical launch fence, durable namespace-init receipt and
pidfd teardown check. This adapts W5 PID-namespace/parent-death containment;
the small trusted entry gates one provider command and supplies no retry,
scheduling, semantic or session-management policy. Both real Codex and the
deterministic evidence leaf enter it. Existing Codex isolation flags and inner
sandbox behavior remain unchanged.

Abandonment commits first. Known-run stop uses the public SDK; a terminal
label cannot establish cessation. Missing dispatched run identity, legacy
profiles, inaccessible runtime state, live namespaces and interrupted allocated
provisioning remain blocking. No new coding work is launched to discover an old
run. Physical safety then permits deletion of only the exact owned worktree and
local branch; the enclosure/lock, store and retained P3 custody survive.

Schema 6 retains immutable cessation proof/time and one monotonic retirement
acknowledgment. Exact schema 2–5 migrations preserve old facts. A retirement Git
child inherits the provisioning flock; a killed caller cannot release exclusion
while that child continues removing the tree. Hooks are disabled only for the
exact local administrative deletion commands.

A read-only skeptical subagent found no remaining concrete unsafe authorization
path in the implemented cessation/retirement scope. Its requested process-level
retirement tests were added, including a direct nonblocking flock probe after
caller SIGKILL. Parent review independently checked these boundaries and the
retained provider/profile identities. This is not a completion review of all #21
acceptance criteria.

## Retained observations

- [Actual-provider record](issue-21-provider-containment.md): real controller-loss
  detached-writer cessation; sensitive intentionally uncontained survivor;
  real read-only reviewer/adjudicator/final-assessor; and a separately labeled
  durable-source mutator/sibling/shared-Git/local-remote/network control.
- [Profile integrity audit](evidence/issue-21-provider-integrity.json): all five
  positive actual-provider records match current launcher/containment hashes
  and the unchanged product graph/runtime. This equality does not erase their
  explicitly different source placements or the negative result below.
- [Product SDK lifecycle evidence](evidence/issue-21-lifecycle.json) and
  [execution log](evidence/issue-21-lifecycle-tests.txt): **5 tests / 8 controls
  passed**. Controlled semantic leaves exercise stop during repair mutation and
  adjudication, controller SIGKILL, actual caller-process exit after a directive
  and after final custody, process death before public stop and after terminal
  observation, and abandonment of runtime success with retained P3 custody.
  Every control retires through the product API after physical cessation;
  old custody remains historical and stale capture/submission is refused.
- [Retirement tests](evidence/issue-21-retirement-tests.txt): **11 tests and
  4 subtests passed**, including killed-caller/inherited-Git-lock exclusion,
  process death after removal before acknowledgment, exact-owned deletion,
  unknown/ambiguous refusal and schema-5 abandonment preservation.
- [Full SDK-free regression](evidence/issue-21-cessation-without-sdk.txt):
  **309 tests and 2,160 subtests passed, 38 SDK-dependent skips**.
  Separately, the current pinned-SDK clean/repaired/forged final-custody test
  passed all three cases. A fresh full pinned-SDK suite is still required before
  claiming #21 complete after the profile decision is resolved.

The lifecycle record verifies all its retained source hashes against the final
files. The provider report explains the initial `/dev/shm` mount regression,
socket-path rejection and controlled diagnostic-fixture mistakes; none is
silently counted as a positive result.

## Remaining blocker and options surfaced

The actual mutator's [temporary-source counterexample](evidence/issue-21-provider-mutation.json)
changed shared Git configuration and refs because the source repository's common
Git directory was under `/tmp`, an existing Codex workspace-write scratch root.
The product accepts that source placement. The original durable-source layout
passes the same probe, but that positive does not fix admission or authorize
stitching incompatible assumptions together.

The user has been asked to choose the narrow pre-dispatch validation rule that
rejects shared Git directories in writable temporary roots, or outer read-only
protection for shared metadata. Neither is implemented by inference. Profile
compatibility and #21 completion remain blocked pending that choice and the
affected verification. No #22 implementation has begun.

Active sibling-run liveness, any additional required integrated controls and the
complete acceptance audit also remain for the final #21 verification. No
replacement, disposition, recovery/catch-up, effects, candidate sealing or
later-phase work is introduced by this continuation.
