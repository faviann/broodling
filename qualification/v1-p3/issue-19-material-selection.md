# Issue #19 — final material selection decision

**Issue status: BLOCKED pending a material-selection decision.** #18 is complete
at `b29701f1d19152d403e900939cd654339fe8198d` on `origin/main`. #20 has not begun.
No completed P3 custody, G3 PASS or Work Unit disposition is claimed here.

## Established facts

The [v0.5 target](../../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md)
sections 6 and 8.4 and [plan](../../docs/governing/broodling-implementation-dependency-plan-v0.5.md)
require enough exact final candidate material, required observations and rationale
to justify later disposition after disposable cleanup. The format is deliberately
a P3/P4 implementation decision; a generic archive is excluded.

[Issue #19](https://github.com/faviann/broodling/issues/19) requires missing final
custody to prevent a completed P3 record. It does not prescribe a current-candidate
file selection rule. Current Contract criteria describe semantic/evidence populations.
Their new `MechanicalEvidence.materials` field means required raw check material,
not a complete candidate source selection. Population and validation prose cannot
be reinterpreted as paths. B1 is immutable but its commit ID does not specify which
final changed and unchanged material must survive worktree cleanup.

[W6](../v1-p1/issue-10-w4-w6.md) retained an explicit fixture selection:
`candidate.txt`, `CANDIDATE-GOVERNING-TEXT.md`, comparison-base bytes and raw evidence.
The [qualification implementation](../v1-p1/issue10_qualify.py) selected those fixture
paths directly. That is bounded qualification evidence, not a product rule for
arbitrary admitted Contracts.

## Unresolved question and blocked work

What trusted declaration or policy identifies the exact final candidate and needed
B1 comparison material required for a complete #19 record?

The proposed small option is an explicit final-material declaration frozen with a
new admitted Contract revision. It would name required current-candidate and B1
comparison files without assigning semantics to existing prose or evidence paths.
The declaration's supported file/absence representation would be explicit; existing
revisions would not acquire new meaning retroactively.

The user has been asked to authorize that option or provide another governed
selection rule. Choosing the candidate source set, implementing its collection and
marking final custody complete are blocked on that answer. Copying only check
materials could omit source context; copying the whole worktree would invent an
archive policy. Neither interpretation has been selected.

## Independent work completed while awaiting the decision

- `ZeroshotSubmitter.observe_current(request, run_id)` validates the admitted product
  protocol/profile, reads the current public status, then watches only after that
  current cursor. It refuses initially terminal/stopping runs, interrupted/missing
  provenance and stream loss. It never scans completed history or falls back to
  unbounded `watch()`. The caller must establish the already-correlated current
  Attempt/request relationship before using this low-level observation seam.
- The ephemeral observation retains only the latest observed implement/repair
  reference and the exact designated final occurrence reference. These are opaque
  runtime IDs in the admitted graph's stable interval, not a model ordinal or a
  Broodling generation counter. Terminal status cannot first introduce a final ID.
- Only designated final verifier outputs bind required typed `finalRationale`
  entries into the public root result. Ordinary diagnostics remain non-authoritative.
  Empty rationale can still be a typed result; future custody must reject missing
  criterion-level material. Transport alone never constitutes completed P3 custody.
- Schema 4 adds one immutable Attempt-scoped `final_assurance` row with run reference
  and serialized custody payload. Exact published schema 2/3 migrations preserve
  P2 facts. Current correlation, uniqueness and insert/update/delete/replace guards
  protect the storage foundation. There is no partial-record lifecycle, per-node
  history, disposition flag or product custody writer yet.

The [partial actual SDK record](evidence/issue-19-current-observation.json) retains
clean, repaired, forged-diagnostic and empty-rationale transport controls. It makes
no completed-custody claim. The fixture gates implementation until the observer has
read its actual initial public status; the stream and runtime outcome are real.
Required evidence uses the actual #18 deterministic leaf. Other model roles are
controlled fixture substitutions, not semantic-reliability evidence.

Independent review found and corrected SQLite `INSERT OR REPLACE` bypass of the
new immutable row and a terminal-only final-ID edge case in the observer. The
regression tests cover both. No product interpretation has been chosen for the
outstanding material-selection question, and #19 remains open.

## Partial validation

- [SDK-free regression suite](evidence/issue-19-independent-without-sdk.txt):
  245 passed, 26 SDK-dependent skips, 1909 subtests.
- [Initial public control run](evidence/issue-19-public-initial.txt) preserved one
  test-fixture failure: the forged-ID fixture emitted undeclared diagnostic keys,
  correctly rejected by native typing. The fixture now places those non-authoritative
  claims in the already-declared diagnostic note. The [corrected group](evidence/issue-19-public-corrected.txt)
  passes clean, repaired and forged controls; the rejection and late-observation
  groups passed in the initial run. The separate four-case raw record also passes
  against the corrected current code and records zero completed custody rows.
- [Assurance regressions](evidence/issue-19-assurance-regressions.txt) exercise the
  existing #17 graph controls and admitted product path with the added final payload.
- Changed-file Ruff passes, retaining the pre-existing `PYI034` annotation exception
  in `store.py`. Product graph/runtime and source bytes are included in the partial
  raw record; prior qualification evidence is unchanged.

The storage and observer foundations are reviewable independent work. They do not
satisfy #19's candidate selection, complete-custody coordinator, required-material
loss/interruption custody controls or completion criteria. Those remain unfinished.
