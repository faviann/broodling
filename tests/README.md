# Property-based tests (issue #30)

Two bounded property modules sit beside the hand-written suite. Everything else
stays as it is: mechanism-specific lifecycle, containment, Git, provider, crash
and direct-SQL tests keep their real witnesses, because the external behavior
*is* the guarantee there and a generated sequence would not observe it.

| Module | Subject |
| --- | --- |
| `test_work_reference_properties.py` | Work Reference canonicalization and Work Unit identity |
| `test_store_state_machine.py` | durable store/Attempt invariants over generated operation sequences |

## Running them

Hypothesis is a test-only dependency, so the product package keeps its empty
dependency list:

```bash
pip install -e '.[test]'
```

Test-only is not optional: with the extra missing, both modules fail to import
and the run reports that, under `pytest` and under `unittest discover`. There is
no skip guard, because nothing in this repository requires the suite to run
without its test dependencies, and a module that silently disappears takes its
invariants with it.

`property_support.py` holds the shared settings profile and nothing else. It is
`derandomize=True`, `database=None`, `print_blob=True` and `deadline=None`: a run
generates the same inputs on every host, a failure shrinks normally and prints
the `@reproduce_failure` blob that replays the exact counterexample, and no
example database is written into the working tree. Per-example deadlines are off
because store writes and temporary-directory work vary by an order of magnitude
between a warm and a cold host.

## What the properties assert

`test_work_reference_properties.py` generates a canonical identity
(host, owner, repository, issue) and then:

- every *promised* spelling of it resolves one canonical key, one derived Work
  Unit id and one issue locator, and a store holds exactly one Work Unit for all
  of them. The generated forms are exactly what the product promises —
  [v1-P2 §2.1](../docs/implementation/v1-p2-admission-nucleus.md) ("HTTPS, SSH,
  `scp`-like and bare `owner/repo` forms, case differences and a `.git` suffix
  all resolve to one Work Unit") plus the named examples in
  `test_work_unit_identity.CanonicalIngressTests` (schemeless `host/owner/repo`,
  a trailing slash, and the issue as a number, a string, `#n` or its canonical
  issue URL). Spellings the parser merely tolerates are left out on purpose: a
  property that asserts them valid turns incidental tolerance into a contract;
- two references differing in exactly **one** canonical component never share a
  key or a Work Unit id. Distinctness is generated component by component on
  purpose: two independently drawn references practically never collide, so an
  implementation ignoring one component would never be caught that way;
- malformed input fails closed with `InvalidWorkReference`. A `WorkReference` is
  a validated value and the store accepts nothing else, so there is no path from
  malformed ingress to a durable write to assert against.

`test_store_state_machine.py` drives the durable operations an operator can
actually repeat — resolve a reference, entitle a source and admit a Contract
revision, admit the current Attempt, abandon it, restart the store — and after
every step rechecks: at most one current Attempt per Work Unit and never an
abandoned one; abandonment irreversible, immutable and still refusing current
authority; identity stable and non-aliasing across restarts; revisions and
Attempt bindings never rewritten.

One rule is a deliberate repetition: it takes an Attempt the machine already
observed, reads the Contract revision and B1 back off that Attempt record, and
admits again with exactly those bindings. Admission must return the same Attempt
identity and bindings and leave it the Work Unit's one current authority. #30
lists repeated idempotent operations among the invariants to check, and leaving
that to Hypothesis drawing the same revision/B1 pair twice by chance made it a
coin flip — the committed configuration reached it zero times before the rule
existed and converges on an existing Attempt ten times with it. Admission after
abandonment is a different question and stays with the refusal path below.

Each rule that can be refused then checks what *that* refusal must not have
changed, which is the "no partial authority" outcome #30 asks for rather than
database-wide immutability. A refused Attempt admission must leave the Work
Unit's current Attempt exactly as it was — including still none — and must not
move that Attempt's exclusive worktree path or branch. A refused abandonment must
leave the Attempt record, currentness included, and the abandonment fact exactly
as they were. The comparisons are whole records read back through the store's own
readers, so an in-place rewrite is caught, not just an appearing or disappearing
row. Bookkeeping that grants no authority is deliberately not frozen: a rejected
admission decision, for one, is a durable record the product means to keep.

The machine stores only facts it has already observed from the store (which
reference resolved which Work Unit, which revision/B1 pair produced which
Attempt, which Attempts it abandoned). It does not decide which operation
*ought* to succeed, so it is not a second implementation of admission: refusals
are tolerated unless an already-observed fact contradicts them.

It runs 50 examples of up to 40 steps. The step budget is not arbitrary: at 12
steps one measured run reached only four Attempt admissions and no refusal at
all, while at 40 the same run reaches 139 admissions including 43 conflicts and
35 stale refusals. Depth is what brings re-admission, conflict and
post-abandonment paths into reach, so a shallower budget leaves the durable
paths this test exists for largely unexercised.

## Discrimination evidence (recorded, not retested)

Issue #30 asks for a demonstration that these properties reject a wrong
implementation rather than restate this one. The demonstrations were run against
temporarily introduced defects while the properties were written, and the results
are recorded here. The broken implementations and the tests that drove them are
**not** part of the suite: retesting a synthetic mutation, or Hypothesis's own
shrinking and reporting, is not assurance about Broodling.

Both defects were simulated by replacing one function for the duration of a
single run, with no product change.

1. **Canonicalization that ignores the forge host** — `canonicalize_repository`
   patched to return the default host for every input. The non-aliasing property
   failed and shrank to two references identical apart from the host
   (`github.com/0/0#1` against `gitlab.com/0/0#1`), which is the smallest input
   that exercises the discarded component.
2. **Attempt identity allocated per call** — `store.derive_attempt_id` patched to
   return a fresh UUID instead of deriving identity from the Contract revision,
   B1 and admitted material. The state machine failed with
   `identical re-admission was refused`, shrunk to one reference resolution, one
   Contract revision and one repeated admission. With repetition now a rule
   rather than a coincidence, the committed configuration reports it, from the
   repetition rule and from a collision in the ordinary admission rule.

Recorded on 12 September 2026 against this branch, Hypothesis 6.168.0, CPython
3.13.5.

## Boundary

This is the whole adoption. No further conversion is planned: direct SQL tests
stay where database enforcement is the subject, crash/process/containment/race
witnesses stay as they are, and no generic model of the product exists. If a
future property needs a framework of custom strategies to express, that is a
signal to keep it out, not to build the framework.
