# Issue #22 — explicit original-B1 replacement

Status: **COMPLETE — #22 acceptance audit satisfied**.
Baseline is accepted #21 at `cb9b9a6e3c80bbe6c5c84a225d8d06601d737138`.
The user approved the frozen-instruction binding and checkout restriction below.
This record does not authorize #23.

## Implementation and acceptance mapping

| Obligation | Implementation and discriminating support |
| --- | --- |
| Safe predecessor before replacement | Schema 7 retry lineage requires durable abandonment, cessation and acknowledged retirement, with no current competitor. Vacant currentness and stop intent alone are insufficient. `test_retry_admission.py` exercises each incomplete boundary. |
| Same immutable Contract/original B1 | Allocation verifies the original commit object and exact frozen attribution/byte digests. It never resolves live HEAD. `test_retry_admission.py` covers drift, missing objects/material and preserved identities. |
| Explicit retry, exclusive allocation | Immutable retry ID binds predecessor, successor, workspace root and target. Allocation commits before host work. Duplicate requests return their historical A2; changed parameters/stale predecessors cannot allocate A3. SQL, concurrency and migration controls retain original rows. |
| Fresh worktree/run | `RetryCoordinator` reuses recorded-B1 provisioning, ownership markers and `prepare_assurance`. Git creation inherits the enclosure lock, disables hooks and replacement refs; orphan-child exclusion is tested. New Attempt/path/branch/key/run IDs are observed in SDK controls. |
| No old semantic input | Replacement preparation permits only current product graph/runtime and original Contract/B1/frozen snapshots. Actual provider stdin controls exclude A1 candidate, evidence, finding, directive, acceptance and history canaries. Historical custody remains outside A2 input. |
| Fresh profile paths | Canonical empty HOME/auth-only CODEX_HOME are reserved transactionally. Reservations exclude historical provider/runtime context and other Attempt enclosures. Ordinary preparation, dispatch and allocation cannot consume them through nominal homes, declared environment paths, SDK state or workspace allocation. |
| Repetition and caller loss | Setup crashes, accepted acknowledgement loss and concurrent identical retries converge on the same A2. Dispatched repetition uses existing public correlation without resetting legitimate graph mutation. Existing G2 changed-HEAD conflict controls remain required. |
| Races against unfinished administration | `test_retry_races.py` uses independent stores and held cessation, retirement and ordinary-provisioning boundaries. Retry cannot allocate before safe retirement; stale concurrent ingress/provision/stop cannot restore A1 or affect A2. |
| Late A1 isolation | Delayed actual final observation after A2 becomes current is rejected; stale submission/capture/provisioning/administration cannot restore A1 or touch A2. Retained old custody remains readable. |

## Approved frozen-instruction binding

The product now derives `admittedInstructions` only from the exact entitled
snapshots pinned by the Attempt's original immutable Contract. Each typed record
contains source ID, kind, locator, media type, SHA-256, encoding and content.
Valid UTF-8 is preserved exactly, including line endings; other bytes use explicit
base64 with an exact roundtrip. Missing or changed material fails closed.

Only the implementer receives this input; reviewers, adjudicators, repair and
final assessors do not. No worker can write the binding. The original frozen
Contract governs these admitted instructions. No A1 candidate, observation,
rationale, summary or provider/runtime history participates in construction.
`test_frozen_instructions.py` verifies source-only material, newer issue drift,
restart, binary content and role-limited binding.

The exact graph accepted at #21 is recognized by one pinned canonical digest
solely for administrative stop. The same runtime/profile and physical cessation
checks remain mandatory. Modified old graphs fail; old semantic observation,
repreparation and replacement initialization are not thereby authorized.
`test_stop_protocol_compatibility.py` includes actual SDK controller-loss,
stop and retirement. No general protocol router or semantic recovery was added.

## Approved V1 checkout restriction

The retained [counterexample](evidence/issue-22-checkout-counterexample.json)
showed an unpinned smudge filter inserting an abandoned-only canary while its
clean filter hid the difference from Git status. The user approved refusal of
unsupported checkout transformations instead of normalization or new materialization.

`checkout_profile.assert_supported_checkout` inspects effective Git configuration
and attributes against the pinned commit with `check-attr --source`. It rejects
configured external clean/smudge/process drivers, active byte-conversion
attributes, incompatible line-ending/symlink/sparse settings, external fsmonitor
and context-dependent conditional includes. Global/system/info attributes are
included. Inability to inspect is a refusal. The qualified single-host profile
excludes hostile concurrent host changes to these inputs.

Checks precede admission status, provisioning and the clean-B1 checks at
preparation/first dispatch. No filter or normalization runs; no index is changed.
`test_checkout_profile.py` proves ordinary/bare source admission, original-tree
attribute selection despite HEAD drift, global/info/config rejection, and no
filter execution or provider dispatch when conversion appears after provisioning
or after prepared-request persistence. The #21 canonical `/tmp` shared-Git
restriction and physical containment implementation remain unchanged.

## Verification and retained scope

- [SDK-free suite](evidence/issue-22-sdk-free.txt): 365 tests and 2,367 subtests
  passed; 46 SDK-dependent tests skipped.
- [Legacy-stop compatibility](evidence/issue-22-legacy-stop-sdk.txt): all 7 tests
  and 2 subtests passed on the pinned SDK, including actual controller loss.
- [Source and compatibility controls](evidence/issue-22-source-and-compatibility-controls.txt):
  21 tests and 12 subtests passed, covering checkout conversion refusal, frozen
  snapshot/binary/role binding and exact legacy administrative compatibility.
- [Direct retry races](evidence/issue-22-retry-races.txt): 3 tests passed with
  explicit held-stop, held-retirement and ordinary-provisioning interleavings.
- [Current SDK lifecycle](evidence/issue-22-lifecycle.json) and
  [log](evidence/issue-22-lifecycle-tests.txt): 6 tests / 10 scenarios passed in
  406.660 seconds. Invocation/final hashes are equal and match current files.
  Provider responses are controlled; the native SDK, product graph/launcher and
  candidate mutation are real. Changed-HEAD public conflict remains covered by
  the separate current G2 regression controls.
- [Actual provider source witness](evidence/issue-22-real-provider.json): actual
  Codex 0.153.4 received unchanged product stdin and acted on the exact original
  source-only instruction. The nonce was absent from Contract prose and B1;
  all seeded A1 inputs were absent. Only the implementer received the binding.
  A1 and other roles were controlled. Runtime success is diagnostic only here;
  this is no semantic acceptance, disposition or G4 claim.
- [Parent profile audit](evidence/issue-22-parent-profile-audit.json) verifies all
  seven current requalification records: mutation, reviewer, adjudicator, final
  assessor, real-provider controller loss, intentionally uncontained sensitivity
  control, and pre-dispatch unsafe-source rejection. All checks pass, including
  sibling/shared-Git isolation and network/no-effect boundaries. Current graph,
  runtime and source hashes are retained alongside each control's exact scope.
- [Full SDK-enabled regression](evidence/issue-22-sdk-full.txt): 407 tests and
  2,459 subtests passed in 2,241.13 seconds; the one failure was the pre-fix
  null-input fixture assertion that this process had collected before correction.
  The complete affected 6-test lifecycle suite above subsequently ran from the
  corrected source and passed. The three final race tests were added after full
  collection and passed separately on the same pinned SDK. Every current test is
  covered by passing evidence; this is explicitly **not** a claim that one full
  SDK invocation was entirely green.
- [Independent review](issue-22-review.md): final specification review found no
  mandatory product-code fixes. Standards review found no hard violations and
  one optional shared-input-constructor suggestion.

Earlier lifecycle/configuration records and explained failures remain historical,
not substitutes for current qualification. The input-binding initial regression
failure retained in `issue-22-input-binding-initial.txt` identified the existing
P3 custody input comparison that also needed the new original-source binding;
that comparison is updated without relaxing current-run or custody requirements.
All G1/G2/G3 and #21 governing/qualification records remain unchanged.
New #22 text test logs have trailing whitespace removed; observations and JSON
evidence are otherwise retained as produced.

The source-only witness and controlled lifecycle initially assumed every role's
provider input was an object. The product round-control node correctly has null
input. Both TypeErrors are retained in the initial fixture-error records; the
checks now treat null as containing no bindings while still checking every raw
prompt for the source canary. The complete corrected lifecycle and actual-provider
witness passed. Product code did not change for this fixture correction.

The first reviewer boundary probe lacked captured command stdout even though the
CLI event recorded command exit zero. Its later model diagnostic was deliberately
not accepted as command evidence; the record remains in
`issue-22-provider-reviewer-missing-command-output.json`. An unchanged repeat
produced independently captured probe output and passed. No product/profile
change or weaker acceptance check was used to obtain that evidence.

There is no Work Unit disposition, automatic retry policy, completed-run history
recovery, semantic salvage, effect delivery, candidate sealing, session manager
or second runtime authority in this issue.
