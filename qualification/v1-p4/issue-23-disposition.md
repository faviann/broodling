# Issue #23 — normal no-effect disposition

Status: **BLOCKED — actual-provider repair evidence remains unmet**.
Baseline: accepted #22 at `854555f4be0b5cfe925cc2df53a123737823fb79`.
This is an implementation record, not G4-V1 PASS.

The implementation and deterministic verification are retained separately from
the [actual-provider blocker](issue-23-real-provider-blocker.md). Two unchanged
finite provider fixtures completed clean-only paths. Neither satisfies the
required real repair-to-disposition slice. #23 is not complete; #24 has not begun.

## Acceptance audit

Each obligation requires current implementation and discriminating retained
evidence before completion. Pending entries are not acceptance claims.

| Obligation | Required support | Status |
| --- | --- | --- |
| Minimal durable justified disposition | Immutable Work Unit result references complete existing P3 custody and original Contract/Attempt/run/structural occurrences | Supported: schema 8 and current clean/repair/forged controls |
| Strict no-effect success | Same admitted canonical Contract explicitly declares empty required effects; missing/nonempty declarations refuse | Supported: canonical-byte and SQL checks; foundation declaration negatives |
| Current final semantic authority | Qualified P3 live observer and fresh custody, transactional non-abandonment/currentness, no second semantic validator | Supported: fresh P3 capture; no-salvage, stop and A1/A2 controls |
| Complete material and rationale | Missing declared material, raw evidence, rationale or unresolved obligations cannot complete | Supported: missing selection/material/rationale and semantic-gap controls |
| Minimal non-success handback | Durable abandonment reason plus existing trustworthy submission/custody diagnostics; cessation remains separately proved | Supported: immutable abandonment plus existing scoped references; current cessation controls |
| Concurrent finalization and stop | Identical committed result after duplicate/lost acknowledgment; both stop/commit orders preserve one irreversible authority | Supported: two-process duplicate, waiting-owner death, both durable orders and late retry refusal |
| Interrupted finalization | Output, capture, complete custody, before commit, after commit: restart reads complete disposition or abandons | Supported: five actual SIGKILL windows including complete P3 custody before disposition |
| Cleanup durability | Full justification readable after controlled disposable worktree/runtime/provider cleanup without observation | Supported: exact retained justification readback without live observation or disposable state |
| A1/A2 isolation | Delayed old capture/result/admin calls cannot complete A1 or change A2 authority/inputs | Supported: delayed old observation/custody against A2 success and repeated scoped administration |
| Actual real-provider repair slice | Real substantive directive, actual repair, renewed evidence, fresh independent review, eligible resolution and current final assessment yield justified disposition | **BLOCKED: both actual runs were clean-only; corrected durable-store fixture also remains unexecuted** |
| Compatible prior boundaries | Current regression/integration controls and explicit assessment of affected W5/W6/W7 controls | Current regression passes; graph/profile unchanged; actual-provider compatibility remains limited by the documented fixture placement and missing repair witness |
| Preservation and review | Prior records unchanged; independent standards/spec review; commit and evidence reachable from origin/main; accurate status comment | Reviews and preservation audit complete; published status must retain the blocker |

## Actual-provider witness design

The finite fixture transparently stages an initial implementation that preserves
a concrete legacy boundary defect pending adjudicated correction. Final Contract
criteria still require the corrected behavior. Every model role uses the actual
qualified provider; the existing deterministic evidence leaf remains unchanged.
No role response or authority signal is supplied by the harness. This is an
integration witness, not evidence of natural model error rates or broad semantic
quality. A clean-only run, missing repair, failed run or incomplete disposition
does not satisfy the obligation and must remain recorded as such.

## Implemented boundary

`WorkUnitDispositionCoordinator.finalize` owns normal P3 capture through the
disposition write. Schema 8 adds one immutable start marker per Attempt and one
immutable Work Unit disposition referencing the existing P3 row. Per-Attempt
`flock` files beside the durable store serialize callers; they contain no runtime
or semantic history. An existing completed disposition returns unchanged. A prior
start without completion, or preexisting standalone P3 custody, follows #21
abandonment instead of recovery. Physical cessation remains separately required.

The disposition transaction checks the same admitted immutable Contract's exact
empty effect declaration, currentness, non-abandonment and custody identity.
Its insert removes current authority. SQL guards reject replacement/update/delete,
late abandonment and new Attempt allocation for the completed Work Unit. Existing
P3 structural observation, material selection and completeness checks remain the
semantic/custody boundary. Versions 2–7 migrate without changing historical DDL
digests or promoting existing custody.

## Verification

- [Current SDK-free suite](evidence/issue-23-sdk-free.txt): 373 tests and 2,429
  subtests passed; 56 SDK-dependent tests skipped.
- [Current disposition controls](evidence/issue-23-controls.json) and
  [log](evidence/issue-23-controls.txt): 10 tests / 19 retained scenarios passed
  in 519.383 seconds on the pinned actual SDK/sidecar. All invocation/final
  source hashes are equal and match the retained implementation and fixtures.
  Model roles are controlled timing fixtures; these are not the real-provider
  repair witness.
- Foundation controls cover exact v7 and concurrent migration, immutable start
  markers, missing/foreign custody bindings and six unsupported/missing effect
  declarations. Older migrations and current P2/P3/P4 checks remain in regression.
- [Independent reviews](issue-23-review.md) found no confirmed product-spec
  defects, hard standards violations or actionable smell findings. Evidence audit
  confirmed that real repair remains unmet.
- [Current broader SDK regression](evidence/issue-23-sdk-regression.txt): 419 tests
  and 2,521 subtests passed in 2,125.24 seconds. Its invocation excludes
  `test_disposition_public.py`, which the complete dedicated suite above covers.
  Together the two passing invocations cover all 429 current tests on the pinned
  SDK. This is explicitly a split regression/control run, not a claim that one
  invocation ran the entire suite.
- Changed Python files pass Ruff; the unchanged `store.py` `PYI034` finding is
  excluded from its targeted check. Product/test source hashes match the frozen
  inventory. Prior governing, G1/G2/G3 and #21/#22 records remain unchanged.
- [Parent integrity record](evidence/issue-23-verification.json) verifies all
  current control/product hashes, exact historical provider-harness snapshots,
  graph/runtime identities and retained evidence hashes. Its status explicitly
  separates passing mechanical verification from blocked actual repair evidence.

W5 launcher, containment, source-placement and provider-profile code is unchanged
from accepted #22, as are the graph and runtime. Current regression reruns the
existing containment, G2 correlation, P3 authority/custody and #21/#22 lifecycle
controls alongside the new W6 disposition boundary. The known actual-provider
scratch-store fixture limitation is explicit in the blocker; no new W5 profile
equivalence or successful real repair is inferred from these mechanical results.

## Developmental evidence

The initial SDK-free run found obsolete tests prohibiting any disposition table
and the newly authorized bounded standard-library lock. Those scope assertions
were updated narrowly; the current full SDK-free result above passes.
Only trailing whitespace was removed from that initial text log for repository
checks; its observations and counts are unchanged. Raw provider JSON/SQLite and
the exact historical harness snapshots remain byte-for-byte preserved.

An early subagent run imported an intermediate schema before its final SQL guard
change, while child processes imported the later schema. Its five crash controls
refused that mixed definition before observing the runtime. The explicitly labeled
[development excerpt](evidence/issue-23-controls-initial.txt) preserves this result;
it is not a fabricated full raw log or current product failure.

The first complete control invocation collected a fixture gate that also blocked
the stop operation's status call. The test was corrected during that run; its
developmental waiter was explicitly released so cleanup could finish. All ten
methods passed, but the [developmental record](evidence/issue-23-controls-developmental.json)
correctly has `mechanicsPassed: false` because source hashes changed. The complete
unchanged rerun above replaces it as current control evidence. No product change
was needed for either fixture correction.

## Scope

The product graph and profile are unchanged. The narrow finalization owner/start record
contains no runtime history, candidate seal or semantic recovery state. Existing
P3 custody remains separately readable and cannot by itself authorize success
after interruption. Physical abandonment/retirement and explicit original-B1
replacement retain the accepted #21/#22 boundaries. No effects, publication,
scheduler, session manager, automatic retry, public interface or P5 work belongs
to this issue.
