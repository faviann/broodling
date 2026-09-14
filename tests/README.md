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

# Mutation testing as a test-suite review (issue #34)

`mutmut` is an optional developer tool here, not a gate and not part of any test
run. It answers one question: when a test claims to protect an invariant, does a
plausible violation of that invariant actually make it fail? Mutation score is
not a target, and no test in this repository exists to kill a mutant that
corresponds to no product failure.

## Running it

```bash
pip install -e '.[test,mutation]'
mutmut run                    # the configured slice, well under two minutes
mutmut results                # everything not killed
mutmut show <mutant-name>     # the diff for one
mutmut run <mutant-name> ...  # re-run named mutants after a change
```

`mutmut run` takes only `--max-children` and mutant names; the slice itself is
`[tool.mutmut]` in `pyproject.toml`, so aiming it at other code means editing
`source_paths` and `pytest_add_cli_args_test_selection` there. The default is
`broodling/identity.py` against the two modules that claim identity, which keeps
a campaign to seconds and off the provider, containment, crash and qualification
tests #34 asks not to mutate by default. Mutating other store or constraint code
is a reasonable thing to do deliberately; pair it with a test selection that is
actually about that code, or the run reports weakness in tests that never
claimed the invariant.

Two config settings are not obvious and should not be removed:

- `also_copy` names the package and the root `conftest.py`. mutmut runs the
  suite from a `mutants/` copy that otherwise holds only the mutated file, so
  without this the tests cannot import `broodling` at all.
- `process_isolation = "forkserver"` with `forkserver_warmup = "none"` exists for
  the property modules. Forking workers from a process that has already run the
  suite makes Hypothesis report `differing_executors`, which is a true statement
  about that arrangement; forking from a server that never ran a test keeps each
  worker's Hypothesis state its own, rather than suppressing a health check that
  is telling the truth.

## What it found, and what it could not

**mutmut's finding: the Work Unit id derivation is a compatibility contract.**
A mutant that changed `digest`'s separator survived, and the first reading of
that was wrong — that v1-P2 §2 promises only that a restart or a rebuilt store
resolves the *same* identities, which a consistently different separator
preserves. The store says otherwise: `work_units.work_unit_id` is persisted,
stores migrate in place from schema v2 onward, and `resolve_work_unit` looks a
Work Unit up **by the id it derives**. A build that changed the recipe would miss
a Work Unit its own store already holds, then fail to insert the replacement
against the existing `reference_key`. `test_work_unit_id_is_derived_not_allocated`
cannot catch that — it compares two computations by the *same* build, which agree
however the recipe is spelled — so
`test_the_v1_work_unit_id_derivation_is_frozen` pins the one id the V1 scheme
derives for `github.com/faviann/broodling#12`. A new scheme owes a migration;
that test is what makes it a decision rather than an accident.

**Found by hand, not by mutmut: the worktree-uniqueness witnesses were green for
the wrong reason.** #34 named this as a diagnostic candidate and was right. Both
raw-SQL tests fabricated a rival `attempt_id` by appending `-rival` to a real
one; `worktree_assignments.attempt_id` references `attempts`, so with
`PRAGMA foreign_keys = ON` the insert died on `SQLITE_CONSTRAINT_FOREIGNKEY`
before the uniqueness it claimed to prove was consulted. Deleting both `UNIQUE`
constraints from the schema left every test in
`test_worktree_provisioning.py` passing. The rival is now a real, non-current
Attempt row, each claim leaves every unique column but its target free, and the
assertion is `sqlite_errorname == "SQLITE_CONSTRAINT_UNIQUE"` — enough to
separate a duplicate from the foreign key, primary key and ownership triggers,
without pinning SQLite's message text.

mutmut could not have found that one: the invariant lives in a SQL schema
string, where the only mutation available is to the literal as a whole, and a
broken schema kills every test at once. The manual recipe generalizes better
than the tool does — **delete a constraint, run the tests that name it; if they
still pass, they are witnessing something else** — and is worth repeating
whenever a test asserts that the database, rather than Python, refuses
something.

## Mutants deliberately left alive

A mutant is worth killing only if it corresponds to a product failure of a
guarantee this repository actually states. Most survivors of the identity slice
fall into these categories, and finding them again is not a new result:

- **Refusal message text** — `InvalidWorkReference(None)`, an uppercased or
  `XX`-padded message. Messages are diagnostics; pinning them couples tests to
  wording.
- **Normalization of spellings nothing promises** — a trailing-dot FQDN, a
  leading slash in an `scp`-like path, a second `://` in one reference, and the
  `http`/`git` schemes the parser tolerates. v1-P2 §2.1 promises HTTPS, SSH,
  `scp`-like and bare `owner/repo`; asserting the rest freezes incidental parser
  tolerance into a contract, which is the rule
  `test_work_reference_properties.py` already follows.
- **Literally equivalent mutations** — `"\x1f"` → `"\x1F"` is the same character,
  `"utf-8"` → `"UTF-8"` the same codec.
- **The `isinstance` guard in `canonicalize_repository`** turning `or` into
  `and`, so a non-string repository raises `AttributeError` instead of
  `InvalidWorkReference`. Every promised repository form is a string; the issue
  side is different and legitimately accepts an `int`, which the property does
  generate. Nothing requires ingress to refuse arbitrary typed values with a
  particular exception.
- **The retained raw ingress** — `submitted_repository`/`submitted_issue`
  replaced with `str(None)`. Nothing reads those columns back: `store.py` writes
  them in `_record_submission`, and the only reader, `submission_count`, counts
  rows. No obligation in the v1-P2 traceability table asks for retained ingress
  and no API, report or qualification artifact consumes it. The one mention —
  `work_unit_submissions   retained raw ingress forms` in the §2 inventory of
  what the store holds — is a caption on a schema record, not a requirement;
  contrast `entitled_sources   exact admitted bytes + entitling authority`, which
  has an obligation row and tests of its own. `submission_count` stays asserted,
  because that *is* store API. If a consumer ever needs the submitted spelling,
  the test belongs with that consumer.

The general rule behind all of these: before writing a test to kill a mutant,
find the requirement or the consumer it protects. A schema record with no reader
is neither.

## The two identity modules overlap, and neither subsumes the other

`test_work_unit_identity.py` and `test_work_reference_properties.py` cover mostly
the same ground, and a campaign against each alone showed each killing mutants
the other misses. That asymmetry is structural, and is the reason both stay:

- the properties generate canonical *lowercase* components and vary case only
  per rendering, so uppercasing `owner`, `repository` or `host` consistently
  leaves every generated spelling agreeing. Canonical form is the named tests'
  subject;
- the properties submit no upstream identities, so
  `repository_identity`/`issue_identity` pinning and
  `WorkUnitIdentityConflict` are the named tests' subject;
- the properties reach boundaries and component combinations no hand-written
  matrix lists — issue number 1, the rule separating schemeless
  `host/owner/repo` from bare `owner/repo`, case variation inside a generated
  spelling.

### What was consolidated, and the criterion

#30 asks that manual cases genuinely subsumed by a property be removed, so each
named case was checked against two questions that both had to be yes: does a
property cover the same meaningful class of input, or a broader one, and require
the same outcome; and does the case carry no separate regression, documentation
or mechanism value? The criterion is class coverage, not a literal superset of
examples — a generated strategy will not reproduce every string a hand-written
list happened to use, and requiring it to would keep example lists alive for the
sake of their literals.

Three cases in `DistinctIdentityTests` were subsumed and removed: plain
non-aliasing (generated over every canonical component rather than two named
neighbours), the disagreeing issue locator (over host, owner or repository
rather than owner alone), and unusable references (over every invalid class the
named list held).

The rest of the module stays for reasons of its own, not because the module as a
whole has unique kills:

- `CANONICAL_FORMS` and `test_repeated_canonical_ingress_resolves_one_work_unit`
  are the named documentation of which ingress forms are supported, and the
  property module derives its generated spellings from it — deleting it would
  leave the property asserting a contract nothing states;
- `test_every_submission_is_retained_against_the_one_work_unit` is retention
  across a *repeated* submission; the property draws distinct spellings;
- `test_identity_survives_reopen` is identity across a store restart and pins
  `first_seen_at`, which neither property carries;
- `test_the_v1_work_unit_id_derivation_is_frozen` is the persisted-identity
  contract above, and the only case that would notice a changed recipe;
- `test_work_unit_id_is_derived_not_allocated` states the v1-P2 §2 guarantee that
  durable ids are *derived, not allocated* as an equation — the id a
  `WorkReference` computes with no store against the id the store resolves —
  rather than inferring it from ingress behaving consistently;
- the `UpstreamIdentityPinningTests` and `IdentityStabilityTests` cases are each
  the only statement of their mechanism in the suite.

## Keeping the tool

`mutmut` stays an optional extra with a bounded config because re-running it
after changing identity or store code is cheap and has paid for itself once. It
is not in CI and there is no score target. If a later campaign returns only
message-text and unpromised-tolerance survivors, the honest move is to drop the
extra rather than tune it.

# Two test lanes (issue #39)

Broodling regression is the default lane and runs on every change. The
real-Zeroshot integration/qualification witnesses are opt-in:

```bash
python -m pytest                                 # Broodling regression
python -m unittest discover -s tests             # the same lane, no plugins
BROODLING_ZEROSHOT_LANE=1 python -m pytest       # + the real-Zeroshot lane
BROODLING_ZEROSHOT_LANE=1 python -m unittest discover -s tests
```

The lane needs the G1-V1 qualified SDK/sidecar installed as well as the variable
set; without the SDK its tests skip either way, and the skip message says which
of the two is missing.

`tests/zeroshot_lane.py` is the whole mechanism: one `unittest.skipUnless` built
from an environment variable, applied as `@qualification_lane` to nine test
classes. An environment variable rather than a pytest marker because the suite is
also documented to run under `unittest discover`, where markers do not exist —
and `skipUnless` needs no plugin, conftest hook or configuration in either
runner.

The four qualification entry points that load a lane class by name —
`qualification/v1-p3/issue19_capture.py` and `v1-p4/issue21_lifecycle.py`,
`issue22_lifecycle.py`, `issue23_controls.py` — select the lane themselves with
`os.environ.setdefault` before importing the test module. Those campaigns *are*
the lane, their documented commands are unchanged, and each records
`mechanicsPassed` only when nothing skipped, so a lane that failed to select
would fail the campaign rather than quietly produce an empty record.

## Why the split, measured

Every test in the suite was timed on the qualified profile (pinned SDK and
sidecar built from `d090961`, so nothing skips):

| Lane | Result | Time |
| --- | --- | --- |
| Whole suite before the split | 431 passed, 0 skipped | 48:59 |
| Broodling regression (default) | 390 passed, 39 skipped | 2:40 |
| Both lanes | 431 passed, 0 skipped | ~49 min, unchanged |

The before/both rows were measured on the branch point; #43 has since net
removed two identity cases, so the same runs report 429 today. The split itself
accounts for the 39.

56 tests required the real SDK and accounted for **2844.5s of 2937.3s — 96.8% of
the runtime**; the other 356 cost 92.8s between them. Cost inside the real-SDK
set is just as lopsided, which is what made a judgement split possible rather
than an all-or-nothing one:

| Group | Tests | Time |
| --- | --- | --- |
| Moved to the lane | 39 | 2784.9s |
| Real SDK, kept in regression | 17 | 59.6s |
| Controlled or SDK-free | 356 | 92.8s |

## What is in the lane, and why

Real Zeroshot execution whose cost is paid for a gate rather than for the next
commit. Each entry keeps its full witness; only the moment it runs changed.

| Module | Tests | Protects |
| --- | --- | --- |
| `test_assurance_graph.py` | 3 | G3-V1 actual graph negative/positive controls (#17) |
| `test_evidence_graph.py` | 1 | G3-V1 graph-local mechanical evidence controls (#18) |
| `test_assurance_public.py` | 1 | actual admitted P3 Attempts through the public coordinator |
| `test_final_assurance_capture.py` | 7 | #19 completed custody against the actual SDK path |
| `test_final_assurance_public.py` | 4 | #19 current-run observation on the real graph |
| `test_disposition_public.py` | 10 | G4-V1 no-effect disposition and finalization races |
| `test_replacement_public.py` | 6 | actual A1→A2 replacement witnesses |
| `test_abandonment_public.py` | 6 | stop/cessation/retirement windows on the pinned SDK |
| `test_stop_protocol_compatibility.py` | 1 | published #21 protocol can still cease and retire |

G3-V1 requires *actual* graph controls and G4-V1 requires a real-provider
no-effect vertical slice, so none of these was replaced by a double. The gate
still has the witness it asks for; a developer no longer pays for it on every
run.

## What stayed in regression, and why

The P2 submission witnesses use the real SDK with an inert graph — no provider,
no long run — and cost about 65 seconds for 17 tests:

- `test_submission.py::PublicSubmissionTests` and `PublicSourceConflictTests`
  (6): acknowledgement loss replayed against the real public boundary, and the
  conflict a genuinely different request or source produces under one submission
  key;
- `test_submission_crashes.py::SubmissionCrashTests` (11): process death at each
  window around dispatch and correlation, which needs a real accepted run on the
  other side to be a window at all.

These are the G2-V1 obligation — "duplicate ingress/ack loss preserves one
authority/run relationship" — and the cross-boundary behaviour is the guarantee,
not a detail Broodling merely consumes. #39 lists duplicate-submission and
ack-loss conflict semantics among the candidates to review, and the review
answer is that 65 seconds buys the one real demonstration that Broodling's
reconciliation converges on the run Zeroshot already accepted. Moving them would
save a rounding error and cost a G2 witness.

## Tests whose subject is really Zeroshot

Two of #39's candidates are exactly what it suspected. Both are now in the lane,
and both have a Broodling-owned counterpart that already runs in regression
against a controlled double, so the default lane loses no assertion:

- `test_final_assurance_public.py::test_completed_run_cannot_be_used_for_late_observation`
  drives a real run to completion to show `observe_current` refuses a terminal
  run. The Broodling half — a `finished` status refused with `UnsupportedRuntime`
  and the forward watch never opened — is already asserted by
  `test_current_run_observation.py::test_initial_terminal_or_stopping_run_never_opens_history`,
  including that `after` stays `None`. What the real run adds is that the pinned
  SDK reports a completed run as terminal, which is Zeroshot's behaviour.
- `test_abandonment_public.py::test_concurrent_stop_callers_converge_after_public_stop`
  records a pinned-controller quirk in its own comment: the controller "exits
  after terminal publication without draining all concurrent RPC replies", so
  one caller may see `TargetError: transport disconnected`. Durable convergence
  of concurrent abandoners is already covered without the SDK by
  `test_abandonment_foundation.py::test_concurrent_abandoners_converge`.

Neither was deleted. A dependency-behaviour witness is worth keeping where it
records an assumption Broodling relies on — it is worth keeping *out* of the
per-commit loop.

## What the default lane still covers with doubles

Moving 39 tests removed no category of Broodling behaviour from regression.
Request construction, persistence, refusal, reconciliation and observation
interpretation are all held by controlled tests that were already there:
`test_current_run_observation.py` (10) for the observation seam,
`test_stop_adapter.py` (8) for administrative stop, `test_submission.py`'s
`SubmissionControls`, `QualifiedAdapterTests` and `AdditionalSubmissionControls`
(18) for the submission contract, plus the abandonment, disposition, retry,
custody, final-material and containment foundations. `test_containment.py` keeps
real process witnesses without needing the SDK at all.

# Minimal real-Zeroshot witnesses (issue #45)

#39 moved the expensive witnesses out of the per-commit loop. This changes what
runs *inside* that lane: the two G3-V1 campaigns were replaying complete Zeroshot
runs to establish properties of a file in this repository.

One rule, applied to every run in turn: **if the claim is decidable from
Broodling's own authored artifact, decide it there; if the claim is Zeroshot's
execution behaviour, keep a real witness.** Nothing was weakened to go faster, no
run was replaced by a test-side reducer, scheduler or graph interpreter, and no
retained qualification record was rewritten.

## Before and after

Real Zeroshot runs created by the two campaigns:

| Campaign | Before | After |
| --- | --- | --- |
| `test_assurance_graph.py` setup (#17 controls) | 38 | 16 |
| `test_evidence_graph.py` (#18 controls) | 12 | 7 |
| Both | 50 | 23 |

Wall clock on the qualified profile (pinned SDK and sidecar, nothing skipped),
the two campaigns run back to back under `pytest --durations`:

| Phase | Before | After |
| --- | --- | --- |
| `AssuranceGraphTests` setup | 591.4s | 244.8s |
| `EvidenceGraphTests::test_admitted_evidence_controls` | 206.0s | 192.5s |
| Both modules end to end | 798.2s (13:18) | 437.8s (7:17) |

A second run of the same two sets, after the #45 review changes, measured 254.2s
and 198.6s — the spread below is this host, not the change.

The assurance number is the honest one: 58% fewer runs, 59% less time. The
evidence number is not, and the reason is worth writing down rather than
rounding away. Timed scenario by scenario in one sitting, the campaign looks
like this:

| Scenario | Cost | Kept |
| --- | --- | --- |
| `sticky` | 74.9s | yes — three rounds |
| `repair-renewed` | 39.5s | yes — two evidence occurrences |
| `missing-renewed` | 24.9s | yes |
| `contradiction` | 23.4s | no |
| `wrong-mode` | 23.1s | no |
| `wrong-artifact` | 22.5s | no |
| `valid` | 22.2s | yes |
| `wrong-host` | 22.0s | no |
| `insufficient` | 21.9s | no |
| `wrong-population` | 21.7s | yes |
| `timeout-descendant` | 15.0s | yes |
| `missing-initial` | 10.7s | yes |

Sum: 321.7s before, 208.8s after — the five removed permutations cost 112.9s
between them. That the same two sets measure 206.0s and 192.5s under `pytest`
minutes earlier is this host's variance, not a finding: the twelve-scenario
campaign has been recorded at 327s, 322s and 206s on unchanged code. The run
count is the number that means something here; the wall clock is a consequence,
and what is left is dominated by the two multi-round scenarios nobody proposes
to remove.

The fast tests that took over the removed claims cost 1.5s for 15 tests, in the
default regression lane, on every commit.

## Why a crash at eleven nodes was one claim, not eleven

`assurance_graph()` is Broodling's authored GraphSpec — a dictionary this
repository writes. Whether every executable node has an unusable-outcome guard,
whether an error branch can reach an accepting sink, and what state a repair node
is allowed to read are all decidable by reading it. Reading it also decides them
for a node added tomorrow, which eleven hand-listed crash scenarios would not.

What is *not* decidable there is whether Zeroshot turns exit code 70, an
unparseable payload, an omitted signal or a node that never answers into the node
error those guards name. That is the dependency's behaviour, and it keeps a real
witness — one per class of provider misbehaviour, at one representative node.

`tests/test_assurance_graph_structure.py` holds the structural half. The
requirement it has to prove is per *occurrence*, not per name: an aggregate
"every node appears in some error guard" would pass a graph whose guards sat on
the wrong routes, leaving each occurrence running on into whatever follows it.
So each executable node is resolved to the construct execution continues into
once that node finishes, and the guard has to be there — with the reverse
direction asserted too, so no route catches an occurrence that is not its own.

It was checked against sixteen hand-built mutants of
`broodling/assurance_graph.py`. Nine change what the graph does: a dropped error
guard on `repair` and on `round_complete`, a final gap routed to `succeed`, a
widened repair input, a downgraded missing-evidence reason, an unbounded repair
loop, a review that writes the obligation, a diagnostic identifier bound to the
frozen Contract, and an exhausted bound routed to the final assessment. Seven
change only *where* a guard sits: implement's and repair's guards swapped, the
review route guarding the evidence check, the resolution route guarding
`round_complete`, a final route guarding the adjudicator, the adjudication route
guarding the initial review, and `round_complete`'s guard dropped from the
post-loop route. All sixteen were killed; the seven placement mutants are the
ones the earlier aggregate assertion would have survived.

## The assurance campaign, removed run by removed run

Twenty-two runs were removed and sixteen kept, out of thirty-eight. The crash
matrix generated one case for each of the five clean-route nodes, the five
repair-round nodes and `final_assessment_authority_repaired` — **eleven**, all
removed, `crash-round_complete` included.

| Removed | Count | What it protected | Now protected by |
| --- | --- | --- | --- |
| `crash-{node}` | 11 | each executable node's unusable execution fails closed | structural: every executable occurrence is caught by the route it continues into, and every such branch is a `fail`. Runtime: `open-control-crash`, a separate case that stays, witnesses that a real crash becomes a node error |
| `{missing,malformed,default,hang}-round_complete` | 4 | a control fault inside the loop stops before the final assessment | structural: the loop's `until` and `post_repair_bound_route` both take `round_complete`'s error to a `fail`. Runtime: `open-control-crash`, which is a crash at that same node |
| `authority-claim-implement`, `authority-claim-repair` | 2 | a mutation node's authority claim cannot bind state | structural: both mutation nodes are `step`s with null output, no signals and no write bindings. Runtime: `authority-claim-initial_review` |
| `missing-payload-initial_review` | 1 | a declared output payload is required | `missing-payload-adjudicate_authority` — the payload that actually drives repair |
| `missing-after-repair` | 1 | required material removed at the renewed occurrence | structural: both evidence routes carry the same missing→fail branch. Runtime: the #18 campaign's `missing-renewed`, which removes the material for real |
| `repair-input-canary` | 1 | repair receives the directive, not raw findings | folded into `repair-resolve`, which takes the identical route, plus the structural binding assertion |
| `forged-diagnostics` | 1 | forged Contract/source/evidence/predecessor IDs confer no authority | the fixture now emits forged identifiers on *every* response, so every retained run carries the canary; structurally, no write binding reads the diagnostic channel |
| `contradictory-clean` | 1 | prose contradicting the node's own signal cannot bypass repair | folded into the adjudicator's diagnostic on every route |

The sixteen retained, because execution behaviour is the claim: `clean`,
`repair-resolve`, `repeat-labels`, `missing-initial`, `sticky-exhaust`,
`refusal`, `final-gap`, `sticky-omission`, `open-control-crash`,
`missing-initial_review`, `malformed-initial_review`, `default-initial_review`,
`missing-payload-adjudicate_authority`, `authority-claim-initial_review`,
`hang-initial_review` and `widened-binding-canary`. 22 removed plus 16 retained
is the 38 the campaign started with.

The canary stays a real run because the v0.5 plan asks for it by hand, and
because an isolation assertion is only worth having if a widened binding would
actually trip it. It submits a modified graph, and so does the hang; both now
declare what they changed in a per-case `testOnlyGraphDeviation`, and
`declared_graph_deviations_match_submitted_graphs` holds that declaration to the
recorded `graphSha256` so the retained record cannot claim a deviation it did
not make, or omit one it did.

## The evidence campaign: six permutations of one route

`wrong-population`, `wrong-host`, `wrong-mode`, `wrong-artifact`, `contradiction`
and `insufficient` differ only in bytes inside the candidate the declared check
prints. All six take the same route to the same `semantic_gap`, through the same
bindings, against the same real deterministic leaf. What they jointly established
about *Broodling* is that whatever the mismatch, the leaf carries the raw
material and the frozen population into the assessor's input unaltered, and never
judges it — which `tests/test_evidence_material_fidelity.py` asserts against the
real leaf, for all six dimensions, without Zeroshot.

`wrong-population` keeps its complete run: the v0.5 plan names it for G3-V1, and
one witness is still needed that available-but-insufficient evidence fails at the
final assessment rather than at the availability signal, which reports production
only.

## What did not change

`qualification/v1-p3/issue17_controls.py` and `issue18_evidence.py` run exactly
as documented and still produce their records; each now carries a `witnessScope`
field saying what moved and where. Retained historical records —
`issue-17-controls.json`, `issue-18-evidence.json` and every W-level record — are
untouched. They remain evidence of what was observed when they were written, not
a description of what the suite runs today.

#33 parallelism is the next question, not a substitute for this one: 23 runs
scheduled concurrently is a different proposition from 50.
