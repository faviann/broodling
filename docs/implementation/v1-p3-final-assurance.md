# V1-P3 final assurance custody — issue #19

`FinalAssuranceCoordinator.capture(attempt_id)` retains the designated final
assessment of one already-correlated, current product run. It does not submit a
run, decide Work Unit disposition or recover a missed completed run.

## Frozen custody selection

A runnable capture requires a nonempty explicit declaration in its admitted
Contract revision, for example:

```python
final_assurance_materials=(
    FinalAssuranceMaterial("src/main.py", final_candidate=True, comparison_base=True),
    FinalAssuranceMaterial("fixtures/input.bin", final_candidate=True),
)
```

Each entry names a literal normalized repository-relative path and the state or
states to retain. At least one state must be selected. Duplicate paths, traversal,
absolute paths and `.git` components are refused. The declaration selects custody
only; it supplies neither candidate identity/applicability nor semantic authority.
There is no automatic discovery, globbing, whole-worktree archive or inference
from evidence-population/validation prose or `MechanicalEvidence.materials`.

Absent declarations remain absent from canonical Contract bytes. Adding or
changing selection produces a new immutable revision that needs admission and a
fresh Attempt. An existing Attempt keeps its original revision. The implementation
does not retire that Attempt or create a replacement; those are later boundaries.
Graph-only preparation remains compatible with older revisions, but their missing
custody declaration prevents capture before any observation begins.

For each selected state the collector retains exact regular-file bytes, symlink
target bytes, or explicit absence. Bytes are base64 encoded, with Git file kind/mode
retained separately; an empty file differs from absence. Current files are read
through directory descriptors without following symlink ancestors. B1 bytes come
from its pinned Git commit/tree/blob objects, independently of current HEAD and
without pathspec interpretation or content filters. The supported repository
object format is the existing V1 SHA-1 profile. Unreadable files, unavailable Git
objects, symlink/non-directory ancestors, directories and special files fail
closed. Errors are not converted into absence. Neither side adds content digests.

## Current public observation

The coordinator verifies the current Attempt, admitted immutable Contract,
dedicated worktree and persisted submission/run correlation against the exact
product graph/runtime and request. It allows graph-authorized mutation after
initial dispatch; it does not require the final worktree to equal B1.

The adapter uses the pinned public SDK: initial current `RunStatus`, then only
`watch(after=current.cursor)`, followed by the successful public `RunResult`.
It must observe the exact admitted designated final node and opaque execution ID
before terminal success, after the latest observed graph-authorized mutation.
It refuses a terminal-only final identity, a later mutation, an authority gap,
failed/stopped runs and interrupted/lost observation. No completed history scan,
private storage access, fallback watch, persisted cursor or partial capture exists.

The graph supplies structural candidate freshness. The record's generation
reference is the latest mutation node/execution pair in that graph interval, not
a worker ordinal, counter, digest or independently generated candidate identity.
Only the two designated final leaves export the required typed criterion-level
`finalRationale`; ordinary diagnostic identifiers cannot select authority.

## Atomic minimal custody

After normal observation, capture rechecks the immutable current binding, requires
complete criterion rationale and required raw observations/context/material, and
collects the declared candidate/B1 selection while the worktree is available.
One atomic immutable row contains:

- Contract revision reference, Attempt/run identity;
- designated final node/execution and last mutation node/execution;
- declared final/B1 bytes or explicit absence;
- graph-bound raw evidence and population/check/host/mode/artifact context;
- criterion-level final rationale and applicable assurance context.

Completeness checks establish presence and coverage, not semantic truth or evidence
sufficiency. The designated graph roles still decide those. Empty raw text is valid
material; missing or blank criterion rationale is not. Native run success alone
cannot create the record. No row is inserted if observation, collection or required
material fails. Cancellation/loss before commit leaves no completed custody and
cannot later be repaired by scanning the completed run.

Schema 4 has one `final_assurance` row per Attempt/run, protected against update,
delete and replacement. Exact schema 2/3 migrations preserve P2 facts. There are
no per-node records, status/log/usage mirrors or Work Unit success flags. Existing
custody can be reread without the disposable worktree or runtime; rereading an
already-retained record is not runtime recovery. Product code performs no cleanup,
retirement, abandonment, restart, replacement or disposition.

The [implementation evidence](../../qualification/v1-p3/issue-19-final-assurance.md)
records the authorized selection decision, actual SDK controls and limitations.
