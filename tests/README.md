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

- every supported spelling of it — scheme, `.git` suffix, trailing slash, case,
  `scp`-like remote, bare `owner/repo`, issue as number, `#n` or issue URL —
  resolves one canonical key, one derived Work Unit id and one issue locator,
  and a store holds exactly one Work Unit for all of them;
- two references differing in exactly **one** canonical component never share a
  key or a Work Unit id. Distinctness is generated component by component on
  purpose: two independently drawn references practically never collide, so an
  implementation ignoring one component would never be caught that way;
- malformed input fails closed with `InvalidWorkReference` and records nothing.

`test_store_state_machine.py` drives the durable operations an operator can
actually repeat — resolve a reference, entitle a source and admit a Contract
revision, admit the current Attempt, abandon it, restart the store — and after
every step rechecks: at most one current Attempt per Work Unit and never an
abandoned one; abandonment irreversible, immutable and still refusing current
authority; identity stable and non-aliasing across restarts; revisions and
Attempt bindings never rewritten. Every rule also requires a refused operation to
leave the durable tables exactly as they were.

The machine stores only facts it has already observed from the store (which
reference resolved which Work Unit, which revision/B1 pair produced which
Attempt, which Attempts it abandoned). It does not decide which operation
*ought* to succeed, so it is not a second implementation of admission: refusals
are tolerated unless an already-observed fact contradicts them.

## Discrimination evidence

Each module ends with a `DiscriminationTests` case that runs its property against
a deliberately defective implementation and requires the failure, so the
properties are known to reject a wrong implementation rather than restate this
one.

- A host-insensitive canonicalization (every forge collapsed onto the default
  host) fails the non-aliasing property. Shrinking lands on the minimal
  counterexample: two references identical apart from the host, e.g.
  `github.com/0/0#1` against `gitlab.com/0/0#1`.
- Allocating Attempt identity per call instead of deriving it from the Contract
  revision, B1 and admitted material fails the state machine with
  `identical re-admission was refused`, shrunk to one `resolve_reference`,
  one `admit_contract_revision` and two identical `admit_attempt` steps.

Both assertions check the reported counterexample, including the
`@reproduce_failure` blob, rather than only that something failed.

## Boundary

This is the whole adoption. No further conversion is planned: direct SQL tests
stay where database enforcement is the subject, crash/process/containment/race
witnesses stay as they are, and no generic model of the product exists. If a
future property needs a framework of custom strategies to express, that is a
signal to keep it out, not to build the framework.
