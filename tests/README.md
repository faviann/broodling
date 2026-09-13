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

A refused admission then checks what *that* refusal must not have changed, which
is the "no partial authority" outcome #30 asks for rather than database-wide
immutability: the Work Unit's current Attempt exactly as it was — including still
none — and that Attempt's exclusive worktree path and branch unmoved. The
comparisons are whole records read back through the store's own readers, so an
in-place rewrite is caught, not just an appearing or disappearing row.
Bookkeeping that grants no authority is deliberately not frozen: a rejected
admission decision, for one, is a durable record the product means to keep.

Abandonment has no refusal path to check here. It is the only modeled transition
that makes an Attempt non-current, and a repeat returns the record already
committed instead of refusing, so its only outcome is that record — which is
where idempotent abandonment and immutable abandonment identity are asserted,
with the irreversible loss of current authority carried by the invariant.

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

# Accidental-hang safety net (issue #32)

`pytest-timeout` is configured once, in `pyproject.toml`:

```toml
timeout = 1800
timeout_method = "signal"
```

It exists so a wedged subprocess, provider fixture, polling loop or awaited SDK
call fails with a traceback instead of hanging an unattended run. It is **not**
a product timing guarantee and it is not evidence about Broodling: a
`Failed: Timeout (>1800.0s) from pytest-timeout` says the test never finished,
never that cessation, terminalization, containment or any other guarantee was
established or refuted. The deadlines the tests own — the 10/30/40/60/90/100
second waits, barriers and `asyncio.wait_for` windows in the lifecycle,
containment, race and provider tests — remain the assertions wherever timing or
cessation is the subject, and none of them was changed, shortened or replaced by
this net.

## Why 1800 seconds

The timeout applies to each of a test's setup, call and teardown phases
separately, so a `setUpClass` that builds real provider controls is measured on
its own. The number comes from measuring this suite on the qualified profile
(the pinned SDK and its matching sidecar installed, so none of the provider
tests skip):

| Phase | Measured |
| --- | --- |
| `test_assurance_graph.AssuranceGraphTests` setup (#17 controls) | 615s alone, 619s in the full run |
| `test_evidence_graph.EvidenceGraphTests::test_admitted_evidence_controls` call | 327s |
| next thirteen qualified lifecycle/provider calls | 222s down to 63s |
| whole qualified suite, 430 tests end to end | 54 minutes |
| whole suite without the SDK (all 56 provider tests skipped) | 105s, no test over 5s |

An earlier 600s default was tried first and cut the #17 control fixture at
600.01s — a real fixture doing real work, not a hang. That is exactly the
spurious failure this net must not produce, so the default sits at roughly three
times the longest legitimate phase, which still leaves a host two to three times
slower than this one uncut. A wedged phase is then reported within half an hour
instead of never, which is the whole claim being made for it.

The configuration was then validated against the whole qualified suite with the
pinned SDK and sidecar built from the #2 harness's recorded source revision:
430 passed, nothing skipped, no timeout failure, in 53:56. Unit, store, property,
subprocess, crash, containment and race modules were also run on their own, and
the suite still passes under `python -m unittest discover -s tests` (431 tests,
56 provider tests skipped without the SDK), where the plugin does not apply at
all.

## Why the signal method

`signal` raises inside the stuck test, on the main thread, at the exact call
that hung. Two consequences matter here:

- **cleanup survives.** `tearDown`, `addCleanup` and `finally` blocks still run,
  so the fixtures that kill launcher children, reap worktrees and close stores
  do their work. The `thread` method calls `os._exit` on the whole run instead:
  no teardown, and the very processes these tests exist to account for would be
  left behind.
- **the report names the stuck boundary.** The failure carries the full
  traceback through the test into the blocking call, plus whatever the test
  already printed.

Every test body in this suite runs on the main thread — the worker threads in
the race tests are barrier helpers with their own deadlines — so the alarm
always lands where it can be raised.

## Demonstration (recorded, not committed)

No hanging or permanently slow fixture is committed. The net was demonstrated
with a local control run outside `tests/`: a test that starts a child sleeping
for 300 seconds and blocks on `child.wait()`, with an `addCleanup` that kills
and reaps it, run at `--timeout=5`. The test failed with
`Failed: Timeout (>5.0s) from pytest-timeout`, the traceback ending in
`subprocess.Popen._try_wait` at the `os.waitpid` call that hung; the cleanup ran
and printed the reaped child's return code, no child survived the run, and the
following test in the same file still executed. Recorded on 13 September 2026,
pytest-timeout 2.4.0, pytest 9.1.1, CPython 3.13.5.

## Scope

One default, no per-test overrides. Every non-SDK test finishes in seconds and
every SDK test is bounded by its own protocol deadlines, so a narrower per-test
mark would buy no earlier diagnosis while adding a second timing number to keep
correct next to the deadline the test already states.

The net covers `pytest` only. `python -m unittest discover -s tests`, which the
README also documents, has no equivalent and runs without it; that is a property
of the runner, not a reason to add product-side timeouts.

# Forward-observation refusal under full-suite load (issue #40)

One full qualified-suite run failed
`test_disposition_public.PublicDispositionTests::test_standalone_p3_custody_does_not_authorize_later_finalization`
with `UnsupportedRuntime: normal successful final occurrence was not observed`,
raised by `ZeroshotSubmitter.observe_current`. It never reproduced afterwards.
Two explanations were open: a race in this control, or a gap in the product's
current-run forward observation. The evidence says the control, and that the
refusal itself could not say so.

## Forward observation cannot miss an occurrence after its cursor

`observe_current` reads one status, then watches strictly after that status's
cursor. That stream is a durable replay, not a live feed: the pinned sidecar
keeps every run event in its ledger, emits one status per non-log event, prunes
nothing, and the local watch has no lossy buffer. So no speed of run can outrun
the observer, and a terminal result arriving early cannot cost it an occurrence.

Demonstrated rather than assumed: with the watch stream held closed until the
run was already `finished`, the whole occurrence sequence — `implement` at
`v2:2`, `final_assessment_authority_clean` at `v2:25`, the terminal result at
`v2:31` — still arrived in order within milliseconds of opening it, and the
capture succeeded. The only way `observe_current` can hold no occurrence is for
the run to have passed them *before* its first status, which `observe_released`
prevents by construction: it releases the model from inside that first status
call, so the run is still gated on `implement` when the cursor is taken.

## What that message actually meant

The refusal covered three different facts at once — the run failed, no mutation
occurrence was observed, no final occurrence was observed — so the recorded
traceback could not distinguish them, and the issue's reading ("held neither")
was an inference. The retained G4 disposition controls show the same string
recorded for `rejected-semantic-gap` and `rejected-missing-rationale`, which are
*failed runs* with their occurrences fully observed. A failed run is now refused
as `normal observation ended in a failed run: <reason>`, which is the same
fail-closed refusal naming which invariant broke; nothing that was refused
before is admitted now.

## The control's own release deadline

`observe_released` releases the model after the adapter's first `status()`
returns, and the fixture's `implement` leaf waited only 30 seconds for that
release. That budget competed with unrelated suite load, and the runtime retries
a failed provider once inside the same execution, so a crossed deadline was
silent — it produced no new `node_started` and no status the observer could see.

Reproduced deliberately by stalling the release, which is the point in the
sequence the load acts on:

| Release delayed by | Before | After |
| --- | --- | --- |
| 35s | first provider attempt failed, `Codex provider failed; continuing once`, retry succeeded, capture passed | one clean `implement` execution, capture passed |
| 68s | both attempts failed, `implement` completed `error`, run terminal `failed`/`execution_unusable`, **`normal successful final occurrence was not observed`** | one clean `implement` execution, capture passed |

At 68 seconds the control manufactured the exact recorded failure while the
mutation occurrence was observed all along, at `v2:2`, and the run's terminal
result was `succeeded=False`. The leaf's deadline is now
`RELEASE_WEDGE_SECONDS = 120`: strictly greater than `observe_released`'s 90s
observer window, so no release that is still legitimate can cross it, and small
enough that both provider attempts fit inside the graph's 300s node timeout. It
is a wedge net like the `pytest-timeout` default above, not a control, and no
test assertion, observer window or provenance check was weakened for it.

## What remains unproven

The original run's failure reason was not retained, so this is the mechanism
that reproduces the recorded message under load, not a proof that this exact
gate is what crossed in that run. Any other load-induced run failure — a node
exceeding its 300s timeout, a genuine provider flake — produces the same refusal
for the same correct reason. Should it recur, the refusal now names which of the
two it was, and #33's parallelism is the load that would show it.

Both reproductions were run outside `tests/` against this fixture, with no
product change, and are recorded rather than committed: a stalled-release or
held-watch control asserts nothing about Broodling once the boundary it found is
fixed. Recorded on 13 September 2026 on the qualified profile (pinned SDK and
sidecar from `d090961`), CPython 3.13.5.
