# V1-P3 required evidence and independent review — issue #18

The admitted assurance graph now carries actual mechanical observations from
its evidence occurrences into fresh review and the designated adjudication,
resolution and final-assessment roles. Evidence availability permits assessment;
it never establishes semantic sufficiency or acceptance.

## Explicit Contract meaning

Each runnable criterion requires a frozen `MechanicalEvidence` declaration:

```python
MechanicalEvidence(
    argv=("/usr/bin/python3", "checks/check.py", "--case", "boundary"),
    cwd=".",
    materials=("fixtures/required-input.txt",),
)
```

`argv` is literal arguments with an absolute executable, without shell-string
interpretation. `cwd` is a normalized worktree-relative directory. `materials`
are exact required existing files relative to the worktree root, independently
of `cwd`. Standard output, standard error and the exit code are always selected.
Population, validation action and validation seam descriptions stay descriptive;
none becomes executable syntax or a file path.

The optional declaration is included in canonical Contract bytes only when
present. Adding or changing it creates a new revision requiring its own admission.
Historical revisions retain their exact bytes and no execution semantics are
inferred for them. The P2 admission profile remains unchanged; P3 preparation
refuses a revision without supported explicit declarations before dispatch.

## Bounded deterministic evidence leaf

The two existing evidence nodes remain Zeroshot agent occurrences, with the same
read-only mode, execution scope, timeout, required signals and failure routes.
Only those nodes bind the fixed `BROODLING_EVIDENCE_LEAF` connection. The bundled
launcher selects the deterministic product leaf from that admitted connection;
Contract text, node labels in model output and diagnostic identifiers cannot
select it. Zeroshot still owns occurrence creation, lifetime, typed validation
and routing. The leaf consumes the pinned harness's graph-input transport.

For each frozen criterion, the leaf executes exactly the declared check in a
Bubblewrap namespace with a read-only host/worktree view, isolated PID/network
namespaces, private temporary scratch, cleared environment and empty HOME. There
are no extra candidate writable roots. The trusted collector stays outside the
check's PID namespace. It reads required worktree-contained files and captures
actual stdout/stderr, exit code, declared argv/cwd/population and actual host/mode
context. These are typed graph payloads; they are not a generic evidence store.

This V1 declaration supports complete UTF-8 text observations/material. It
preserves line endings and fails closed for missing, escaped, non-file or
non-UTF-8 material, unavailable containment/executable, or abnormal execution.
Exit codes 0–127 are observations, including failed checks; codes 128 and above
are conservatively treated as abnormal/unavailable. Check output and required
material are never silently truncated. Checks requiring candidate writes,
network or unsupported binary material cannot use this profile.

The software/configuration identity includes the bundled launcher, evidence leaf
and Bubblewrap executable hashes. These identify trusted runtime software, not
candidate generations. Trusted executables must remain outside the candidate.
This explicit deterministic substitution follows the user's #18 implementation
selection under v0.5's small trusted graph-local checks. It adds no Zeroshot
runtime capability, authority role, graph route, retry or repair iteration.

## Governed bindings and reviewer profile

`comparisonBase` comes from the Attempt's immutable B1 commit; no response can
write it. `evidenceContent` holds observations and any production error. Only the
evidence nodes can write it, and a missing/error route stops before review.
Successful implementation and every successful repair structurally require a
new evidence occurrence and fresh review. No ordinal, candidate seal or external
observer establishes applicability.

Review receives only the frozen Contract, B1 comparison, evidence availability
and actual selected evidence, plus legitimate candidate material in the shared
read-only workspace. The existing W4 provider settings remain: Codex 0.153.4,
`gpt-5.6-sol`/low, execution-scoped fresh sessions, ephemeral execution, ignored
user config/rules, empty isolated HOME, initially authentication-only isolated
CODEX_HOME, network off and no extra writable roots. Candidate governing text
is assessment data under the Contract. It cannot rewrite the governing inputs.

Actual findings reach designated authority. Repair retains exactly the previous
three inputs: Contract, adjudicated obligation signal and directive/correction
content. Raw evidence/findings and private role narrative are excluded. Sticky
directive content survives renewed evidence and clean reviews until designated
resolution explicitly resolves the obligation.

## Boundary

The [issue #18 evidence record](../../qualification/v1-p3/issue-18-evidence-review.md)
links affected mechanical, real reviewer and containment controls and their
limitations. Historical W3/W4/G1/G2/#17 evidence and pre-integration #18 records
remain unchanged. This is input-side integration; final-assurance observation
and custody remain #19. There is no Work Unit disposition, recovery/catch-up,
effect, second scheduler/router/session manager or runtime-history mirror.
