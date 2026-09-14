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

One bounded campaign was run to answer a single question: when a test claims to
protect an invariant, does a plausible violation of that invariant actually make
it fail? Mutation score is not a target, mutation testing is not a gate, and
nothing here runs in an ordinary suite run.

## Running it

```bash
pip install -e '.[test,mutation]'
mutmut run                    # the configured slice: 169 mutants, ~90s
mutmut results                # everything not killed
mutmut show <mutant-name>     # the diff for one
mutmut run <mutant-name> ...  # re-run named mutants after a change
```

`mutmut run` takes only `--max-children` and mutant names; the slice itself is
`[tool.mutmut]` in `pyproject.toml`, so aiming it at other code means editing
`source_paths` and `pytest_add_cli_args_test_selection` there. The default is
`broodling/identity.py` against the two modules that claim identity, which is
what keeps a campaign to seconds and off the provider, containment, crash and
qualification tests that #34 asks not to mutate by default. Mutating other store
or constraint code is a reasonable thing to do deliberately; pair it with a test
selection that is actually about that code, or the run reports weakness in tests
that were never claiming the invariant.

Two config settings are not obvious. `also_copy` names the package and the root
`conftest.py`, because mutmut runs the suite from a `mutants/` copy that
otherwise holds only the mutated file. `process_isolation = "forkserver"` with
`forkserver_warmup = "none"` is there for the property modules: forking workers
from a process that has already run the suite makes Hypothesis report
`differing_executors`, which is a true statement about that arrangement, so the
workers fork from a server that never ran a test rather than suppressing a
health check that is telling the truth.

## What the campaign found

169 mutants, 126 killed, 42 alive, 1 unreachable from this slice
(`content_digest`, which the identity tests do not call). Two findings — one test
defect, and one mutant this document first classified wrongly:

1. **The worktree-ownership raw-SQL witnesses were green for the wrong reason.**
   #34 named this as a diagnostic candidate and was right. Both tests fabricated
   a rival `attempt_id` by appending `-rival` to a real one;
   `worktree_assignments.attempt_id` references `attempts`, so with
   `PRAGMA foreign_keys = ON` the insert died on `SQLITE_CONSTRAINT_FOREIGNKEY`
   before the uniqueness it claimed to prove was ever consulted. Shown by
   deleting both `UNIQUE` constraints from the schema: all 27 tests in
   `test_worktree_provisioning.py` still passed. The rival is now a real,
   non-current Attempt row, each claim leaves every unique column but its target
   free, and the assertion is `sqlite_errorname == "SQLITE_CONSTRAINT_UNIQUE"` —
   enough to separate a duplicate from the foreign key, primary key and
   ownership triggers, without pinning SQLite's message text. With the same two
   constraints deleted, both now fail.

2. **The Work Unit id derivation is a compatibility contract, and nothing said
   so.** Covered under *One survivor was reclassified* below: the `digest`
   separator mutant was left alive on a misreading of what the store guarantees,
   and it is now killed by a named witness of persisted identity compatibility.

Finding 1 is also the campaign's clearest limit: `mutmut` could not have found
it. That invariant lives in a SQL schema string, where the only mutation
available is to the literal as a whole, and a broken schema kills every test at
once. It was found by hand, and the recipe generalizes better than the tool does
— **delete a constraint, run the tests that name it; if they still pass, they are
witnessing something else.** Worth repeating whenever a test asserts that the
database, rather than Python, refuses something.

## Which mutants are left alive, and why

A mutant is worth killing only if it corresponds to a product failure of a
guarantee this repository actually states. Of the 42 alive:

- **24 mutate the text of a refusal message** (`InvalidWorkReference(None)`, an
  uppercased or `XX`-padded message). Messages are diagnostics; pinning them
  couples tests to wording.
- **9 change normalization of spellings nothing promises** — a trailing-dot
  FQDN, a leading slash in an `scp`-like path, a second `://` in one reference.
  v1-P2 §2.1 promises HTTPS, SSH, `scp`-like and bare `owner/repo`; asserting
  the rest would freeze incidental parser tolerance into a contract, which is the
  rule `test_work_reference_properties.py` already follows.
- **4 remove `http` or `git` from the accepted scheme set.** Same reason: both
  are tolerated, neither is promised.
- **2 are literally equivalent** — `"\x1f"` → `"\x1F"` is the same character and
  `"utf-8"` → `"UTF-8"` the same codec.
- **1 turns `canonicalize_repository`'s `isinstance` guard from `or` into
  `and`**, so a non-string repository raises `AttributeError` instead of
  `InvalidWorkReference`. Every promised repository form is a string — the issue
  side is different, and legitimately accepts an `int`, which the property does
  generate. Nothing requires ingress to refuse arbitrary typed values with a
  particular exception, so asserting it would be writing a contract to kill a
  mutant.
- **2 replace the retained raw ingress** with `str(None)`, so
  `work_unit_submissions` records the string `"None"` instead of the spelling
  submitted. Nothing in the product reads those two columns back: `store.py`
  writes them in `_record_submission` and the only reader, `submission_count`,
  counts rows. No obligation in the v1-P2 traceability table asks for retained
  ingress, and no API, report or qualification artifact consumes it. The one
  mention — `work_unit_submissions   retained raw ingress forms` in the §2
  inventory of what the store holds — is a caption on a schema record, not a
  requirement; contrast `entitled_sources   exact admitted bytes + entitling
  authority`, which has an obligation row and tests of its own. Asserting the
  column contents would be pinning the implementation's bookkeeping because a
  mutation exists, which is the thing #34 says not to do. If a consumer ever
  needs the submitted spelling — an operator report, a provenance answer — that
  consumer is what the test should go through.

That last pair is a reversal: an earlier revision of this branch asserted the
retained spellings, on the strength of the §2 caption. Checking for a consumer
rather than a record is what settled it, and the assertion is gone.
`submission_count` stays asserted, in the property and in
`test_work_unit_identity` — that one *is* store API, and how many times a Work
Unit was submitted is a number the store is asked for.

### One survivor was reclassified

The `digest` separator mutation — `"\x1f"` → `"XX\x1fXX"` — was first left alive
here on the reasoning that v1-P2 §2 promises a restart or a rebuilt store
resolves the *same* identities, which a consistently different separator
preserves. That was wrong about how the store works. `work_units.work_unit_id`
is persisted, stores migrate in place from schema v2 onward, and
`resolve_work_unit` looks a Work Unit up **by the id it derives**. So a build
that changed the recipe would miss a Work Unit its own store already holds and
then fail to insert the replacement against the existing `reference_key` — the
Work Unit becomes unreachable, and no amount of restarting fixes it. Persisted
identity compatibility is the guarantee, and the derivation recipe is part of it.

`test_work_unit_id_is_derived_not_allocated` could not catch this: it compares
two computations by the *same* build, which agree however the recipe is spelled.
`test_a_work_unit_persisted_by_the_v1_scheme_still_resolves` now pins the one id
the V1 scheme derives for `github.com/faviann/broodling#12`, which is the smallest
statement of the contract — change the recipe and the test says so, which is the
point at which a migration is owed. The two literally-equivalent `digest` mutants
still survive it, correctly.

## How the two identity modules relate

They overlap heavily, and neither subsumes the other. Running the same 169
mutants against each module alone:

| Test selection | Killed | Alive | Unreachable |
| --- | --- | --- | --- |
| `test_work_unit_identity.py` only | 121 | 47 | 1 |
| `test_work_reference_properties.py` only | 116 | 52 | 1 |
| both | 126 | 42 | 1 |

The single-module rows are the identity module as it stood *before* the
consolidation below and before the compatibility witness was added, which is the
comparison that motivated the consolidation; the combined row is current.

Of the 127 mutants both modules killed at the time of that comparison, 110 were
killed by each module on its own — the overlap is most of the coverage, and neither module would be
noticed missing by a mutation run alone. The 17 that separate them are what the
experiment is actually evidence about.

Four mutants only the properties kill: `number <= 0` → `<= 1` (issue number 1, a
boundary the named matrices never use), `"." in head and …` → `or` (the rule
separating schemeless `host/owner/repo` from bare `owner/repo`, reached because
generated owners may contain a dot), and two `strip` arguments reached through
the uppercased SSH rendering. (The retained-ingress pair counted here in an
earlier revision, until the assertion that killed them was withdrawn.)

Eleven only the named tests kill, and they are structural. `owner.lower()`,
`repository.lower()` and `host.lower()` → `.upper()` all survive the properties,
because the properties generate canonical lowercase components and vary case only
per rendering — uppercase everything consistently and no generated spelling
disagrees. The upstream-identity arguments (`repository_identity or None` and its
variants) survive too: the properties never submit upstream identities, and
`WorkUnitIdentityConflict` is the named tests' subject.

So neither module dominates. The properties cover the space around the named
matrices; the named matrices hold canonical *form* and upstream-identity pinning,
which a property generating canonical components cannot express.

### Consolidation

A module having unique kills says nothing about whether each case inside it is
still earning its place, and #30 asks that manual cases genuinely subsumed by a
property be removed. So the named cases were checked one at a time, against two
questions that both had to be answered yes: does a property cover the same
meaningful class of input, or a broader one, and require the same outcome, and
does the case carry no separate regression, documentation or mechanism value of
its own? Then the campaign was re-run with the case removed, as a check on the
reading rather than the reason for it.

The criterion is class coverage, not a literal superset of examples. A generated
strategy will not reproduce every string a hand-written list happened to use —
`"not-a-number"` is drawn from a different alphabet than the property's
non-numeric issue strategy — and requiring it to would keep example lists alive
for the sake of their literals. What matters is whether an input class the
product must handle stops being exercised, and whether the case said something
the property does not.

Three cases in `DistinctIdentityTests` came out subsumed and are gone:

| Removed | Subsumed by |
| --- | --- |
| `test_a_different_reference_never_aliases_onto_an_existing_unit` | `test_two_references_are_two_work_units`, which generates pairs differing in any one of host, owner, repository and issue number rather than the two neighbours this named. Its own comment already said the full matrix lived there |
| `test_repository_and_issue_locators_must_agree` | `test_a_disagreeing_issue_locator_is_refused`, which disagrees in host, owner or repository rather than only owner |
| `test_unusable_references_are_refused` | the property of the same name, which draws from every invalid class the named list held — empty, host-only, over-deep path, unsupported scheme, non-positive issue, non-numeric issue, absent issue — with different literals inside them |

Removing all three changed nothing about which mutants die: the same 127 killed
and the same 41 alive as before the removals. That is corroboration, not the
argument — a case that kills no mutant may still be the only statement of a
requirement, which is why the reading came first.

The rest of the module stays, and not merely because the module as a whole has
unique kills. Case by case:

- `CANONICAL_FORMS` and `test_repeated_canonical_ingress_resolves_one_work_unit`
  are the named documentation of which ingress forms are supported, and the
  property module derives its generated spellings from it — deleting it would
  leave the property asserting a contract nothing states;
- `test_every_submission_is_retained_against_the_one_work_unit` is retention
  across a *repeated* submission, which the property does not exercise: it draws
  distinct spellings;
- `test_identity_survives_reopen` is identity across a store restart, which
  neither property in this pair reopens a store to check, and it also pins
  `first_seen_at` — the state machine restarts a store but carries no
  first-seen fact across the restart;
- `test_a_work_unit_persisted_by_the_v1_scheme_still_resolves` is the persisted
  identity contract of finding 3, and the only case in the suite that would
  notice a changed derivation recipe;
- `test_work_unit_id_is_derived_not_allocated` overlaps generated store ingress,
  which resolves the same Work Unit for every spelling and would fail if ids
  were allocated per call. It stays anyway, as the direct named witness of the
  v1-P2 §2 architectural guarantee that durable ids are *derived, not allocated*:
  it compares the id a `WorkReference` computes with no store at all against the
  id the store resolves, which is the guarantee stated as an equation rather than
  inferred from ingress behaving consistently;
- the `UpstreamIdentityPinningTests` and `IdentityStabilityTests` cases are each
  the only statement of their mechanism in the suite — the properties submit no
  upstream identities and issue no direct SQL.

## Keeping the tool

`mutmut` stays as an optional extra with the bounded config, because one 90
second run produced a real test gap and the complementarity evidence above, and
because re-running it after changing identity or store code is cheap. It is not
in CI, there is no score target, and no test here exists to kill a mutant that
corresponds to no product failure. If a later campaign returns only message-text
and unpromised-tolerance survivors, the honest move is to drop the extra rather
than tune it.

Recorded on 13 September 2026, mutmut 3.8.0, Hypothesis 6.168.0, pytest 9.1.1,
CPython 3.13.5.
