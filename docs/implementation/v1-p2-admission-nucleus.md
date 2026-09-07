# V1-P2 — admission and durable semantic nucleus

**Issues:** [#12](https://github.com/faviann/broodling/issues/12) (admission
nucleus), [#13](https://github.com/faviann/broodling/issues/13) (Attempt, B1 and
disposable worktree)
**Date:** 7 September 2026
**Governing pair:** [target v0.5](../governing/broodling-target-responsibility-boundary-design-v0.5.md),
[implementation/dependency plan v0.5](../governing/broodling-implementation-dependency-plan-v0.5.md).
**Gate dependency:** [G1-V1 PASS](../../qualification/v1-p1/issue-11-g1-v1.md).

This is the Broodling product implementation of V1-P2. Issue #12 built the
admission/store boundary: Work Unit identity, source entitlement, immutable
Contract revisions and the V1 no-effect Closability/admission decision. Issue #13
added the durable Attempt layer on top of it: one immutable Attempt bound to one
admitted Contract revision, its original starting state B1, and the one dedicated
disposable worktree that Attempt exclusively owns.

Sections 1 and 2 are #12; section 3 is #13.

## 1. Selected product configuration

| Selection | Value | Why it is sufficient for P2 |
|---|---|---|
| Language/runtime | Python 3.13 (`requires-python = ">=3.13"`); recorded here on CPython **3.13.5**, Linux x86-64 | The G1-V1 profile was qualified through the official Python SDK on Python 3.13.x. P2 needs durable admission logic, not in-process Zeroshot access. |
| Packaging | one importable package, `broodling/`, plus `pyproject.toml` | The plan defers CLI/service packaging to V1-P5. A domain package is enough to run the store tests. |
| Persistence | one Broodling-owned SQLite database via the standard library `sqlite3`; recorded here on SQLite **3.46.1** | V1 is single-host. P2 needs transactional uniqueness and immutable administrative records, not a distributed database or an ORM. |
| Schema | version **2**, definition digest `bbd7b68bdc66e6bc626f6f5d556e0efa3c9398476eb78b3f3ad75400ade29779` | Recorded in `schema_meta` at initialization and re-checked on every open, so a store written by different DDL is refused rather than migrated implicitly. |
| Table mode | SQLite `STRICT` tables (needs SQLite ≥ 3.37) | Makes column typing an enforced durable property instead of a convention. |
| Durability | `journal_mode=WAL`, `synchronous=FULL`, `foreign_keys=ON` | A committed fact survives process death; an interrupted write leaves nothing. |
| Store location | outside disposable Attempt worktrees; `BROODLING_STORE`, else `$XDG_STATE_HOME/broodling/broodling.sqlite3` | The record must outlive an abandoned Attempt and its retired worktree. |
| Worktree root | caller-configured, absolute, durable; `/tmp`, `/dev/shm`, `/var/tmp` and `/run` refused, as is any root inside a repository or another Attempt's enclosure | The qualified profile requires a dedicated non-temporary worktree root. A root a reboot or a tmpfs eviction can clear is not durable Attempt state. |
| Git | the local `git` binary, invoked with a fixed argument vector and a `GIT_*`-stripped environment (`broodling/git.py`) | Worktree provisioning is host-local administrative setup. Nothing else in the package starts a process, and no module opens a socket or network client. |

### 1.1 Qualified external runtime boundary

V1-P2 records this boundary and submits nothing to it. No Zeroshot SDK or sidecar is
imported, installed or executed by the product package, and **no P1
qualification was re-run** for this issue.

| Item | Value |
|---|---|
| Integration | official Python SDK `LocalTarget` → matching Rust sidecar |
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995` |
| SDK | `zeroshot-rust 0.1.0.dev0`, wheel SHA-256 `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| Sidecar | `zeroshot-rust 0.1.0`, SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Gate | G1-V1 **PASS**, `qualification/v1-p1/issue-11-g1-v1.md` |

The same values are machine-readable in `broodling/profile.py` and are copied into
every admission decision row, so a decision records the configuration that made
it.

## 2. What the store holds

```text
work_units                     one repository + one primary issue
  work_unit_submissions        retained raw ingress forms
  entitled_sources             exact admitted bytes + entitling authority
  contract_revisions           immutable canonical Contract bytes
    contract_source_attributions
  admission_decisions          one decision per revision, with findings
  attempts                     one current Attempt: one revision + B1
    worktree_assignments       the one worktree path/branch it owns
```

Durable ids are derived, not allocated: a Work Unit id is a digest of its
canonical reference key, a source id of its Work Unit + kind + locator + content
digest, a Contract revision id of its Work Unit + canonical Contract digest.
Restarting the process, or rebuilding the store, resolves the same identities.

Append-only tables carry `BEFORE UPDATE`/`BEFORE DELETE` triggers, so amending an
admitted Contract aborts in the database rather than relying on Python
discipline. `work_units` permits exactly one narrowing update: pinning a
previously unset upstream repository/issue identity.

### 2.1 Identity and aliasing

The canonical key is `host/owner/repository#issue`. HTTPS, SSH, `scp`-like and
bare `owner/repo` forms, case differences and a `.git` suffix all resolve to one
Work Unit. A different host, owner, repository or issue is a different Work Unit.
Optional upstream identities (for example GitHub node ids) are not part of the
key — a plain URL must still resolve — but once pinned they turn a renamed or
recreated repository/issue into a `WorkUnitIdentityConflict` instead of a silent
alias onto the same path.

### 2.2 Entitlement

Entitlement is decided from how a payload was *presented* — who produced it and
who granted it — and never from the payload's own content. The entitlement module
does not parse submitted bytes at all.

* the primary issue is entitled by Broodling policy, and its locator must be this
  Work Unit's primary issue;
* other material needs an explicit grant from `caller` or `broodling_policy`,
  with a stated basis;
* a payload of origin `model_extraction`, `candidate_output` or
  `referenced_material` can never become an entitled source, even carrying a
  grant.

Model extraction may still propose Contract structure: `constructed_by` records
it, and every attributed source is checked against the entitled snapshots of that
Work Unit, at the exact content digest the Contract pins.

### 2.3 Contract revisions

A Contract serializes to canonical JSON; its digest is its meaning. Recording the
same meaning twice resolves the same revision. Changed meaning becomes the next
`revision_number`, with `supersedes_revision_id` pointing at the previous one —
never an in-place edit. Live issue edits after admission produce a *new* entitled
source snapshot and leave the stored revision's bytes and its attributed material
untouched.

The Contract carries the V1 fields admission needs: criteria (each with a finite
evidence population or a declared validation surface, a validation seam, a
validation action and a falsifying observation), obligations, prerequisites, the
required-effect set and host/runtime assumptions.

The model is deliberately permissive about *content*: an unbounded population, a
`push` effect and a `multi_host` assumption are all representable. They have to
be, or a rejection could not preserve the obligation it refused.

### 2.4 Closability and admission

`broodling.closability.assess` is a pure function of the Contract and the
qualified profile constants. It refuses, with the causing obligation preserved
verbatim:

| Code | Refuses |
|---|---|
| `required_effect_present` | any non-empty required authoritative-effect set |
| `effect_dependent_evidence` | a criterion whose evidence depends on an effect |
| `unsupported_external_obligation` | commit-as-delivery, push, PR, merge, issue mutation, publication, deployment |
| `unrecognized_obligation_kind` | an obligation kind that is not a supported V1 kind |
| `missing_finite_evidence_population` | no finite population and no declared validation surface |
| `missing_validation_seam` / `missing_validation_action` / `missing_falsifying_observation` | a criterion that cannot be closed |
| `unsatisfied_prerequisite` | a prerequisite not satisfied inside the profile — handed back, not awaited |
| `unsupported_host_assumption` | any host/runtime assumption outside the qualified single-host profile |
| `no_criteria` / `no_entitled_source_attribution` | a Contract with nothing to close against or no entitled source |

Supported obligation kinds and host assumptions are allowlists, so an unknown
value fails closed instead of being assumed local.

Admission writes one immutable decision per revision and creates nothing else: no
Attempt, no worktree, no Zeroshot run. Admitting the Attempt is a separate later
step (section 3), and it requires this decision to already be committed. Only a
committed `admitted` decision is authority, which is what makes an interrupted
admission unambiguous — a stored revision with no decision is simply not
admitted.

## 3. Attempt, B1 and the disposable worktree (#13)

Admission says a Contract *may* be executed. An Attempt is the execution episode
itself: one immutable identity, permanently bound to one admitted Contract
revision, one original starting state, and one worktree it owns exclusively.

### 3.1 B1 — the supported starting-state policy

B1 is **an exact immutable Git commit object id plus the frozen admitted
instruction/source bytes the Contract already pins**. Nothing else is supported,
and nothing more is needed: a commit is already immutable, and #12 already stores
the admitted bytes.

`attempts` records the repository's shared Git directory, the 40-character commit
id, the digest of the attributed material, and the revision expression the caller
asked for. Moving live `HEAD`, moving a branch tip and editing the issue text
afterwards all leave those columns alone — the attempts table takes no updates at
all.

A starting state the policy cannot carry is refused by name:

| Refusal | Cause |
|---|---|
| `UnsupportedStartingState` | uncommitted, staged or untracked material in the source repository, listed path by path |
| `UnsupportedStartingState` | a revision that does not name a commit, or a directory that is not a Git repository |
| `UnsupportedWorkspaceRoot` | a relative root, a root under `/tmp`, `/dev/shm`, `/var/tmp` or `/run`, a root inside the source repository, or one inside another Attempt's enclosure |
| `AttemptAdmissionError` | a Contract revision with no committed `admitted` decision |
| `AttemptConflict` | a request that would make a second current Attempt for one Work Unit |
| `WorktreeOwnershipConflict` | a path, branch or enclosure that belongs to different Broodling state |

Dirty starting material is *not* committed on the caller's behalf, and *not*
reclassified as environment state. Both would be Broodling deciding what the
admitted material is. This is a narrow policy, not a source-sealing subsystem.

### 3.2 One current Attempt

The Attempt id is derived — `at-` + a digest over the Contract revision, the
repository, the commit and the material digest — so repeating an identical
admission resolves the Attempt that already exists rather than minting a rival.
A request differing in revision or B1 derives a *different* id, and then:

```sql
CREATE UNIQUE INDEX attempts_one_current_per_work_unit
    ON attempts (work_unit_id) WHERE is_current = 1;
```

refuses it. The index, not the Python pre-check, is the enforcement: two
processes admitting concurrently serialize on the write lock, and the loser
either observes the winner's identical Attempt or gets `AttemptConflict`. It can
never open a second current authority. Retiring an Attempt so a replacement can
become current is V1-P4 abandon/restart and is deliberately absent.

Currentness is witnessed where it is decided.
`test_identical_concurrent_admissions_alone_resolve_one_attempt` races four
admissions and stops there, with no provisioning outcome standing in front of
the result — so a provisioning failure can never again make this fact ambiguous,
which is what the [G2-V1 review](../../qualification/v1-p2/issue-15-g2-v1.md)
found it had. The full diagnosis is in
[issue-13-concurrency.md](../../qualification/v1-p2/issue-13-concurrency.md).

### 3.3 Allocation before provisioning

`AttemptProvisioner.admit` resolves B1, then writes the Attempt *and* its
worktree claim in one transaction — before any directory exists:

```text
<workspace root>/<owner>-<repo>-<issue>-<attempt>/     enclosure, marked disposable
<workspace root>/<owner>-<repo>-<issue>-<attempt>/worktree/   the candidate tree
branch broodling/<owner>-<repo>-<issue>/<attempt>
```

Path and branch are derived from the Attempt id, so a retry after a crash
converges on the directory the interrupted run was creating. `worktree_path` is
globally unique and `(repository, branch)` is unique, so two Work Units — including
two on the same repository — cannot own one worktree or one branch. Ownership is
queryable: `worktree_owner(path)`, `branch_owner(repository, branch)`,
`current_attempt(work_unit_id)`.

The `.broodling-disposable-worktree` marker sits on the **enclosure**, not inside
the checkout. It names the owning Attempt, so provisioning can tell its own
directory from somebody else's, and it keeps the #12 store-location guard honest:
the Broodling store refuses to open anywhere beneath a marked enclosure. Keeping
it out of the worktree leaves the candidate tree exactly B1 and nothing else.

### 3.4 Provisioning, and what a crash leaves behind

`AttemptProvisioner.provision` creates one attached worktree on the unique local
branch at exactly B1. The branch is attached because the qualified sidecar
profile rejects a detached HEAD; it is disposable runtime scaffolding, never
pushed and never delivery.

The operation is idempotent and converging, which is the whole crash-safety
argument:

| Crash window | What survives | What the retry does |
|---|---|---|
| Mid-admission transaction | nothing | derives the same Attempt id and admits it |
| After the Attempt commits, before any host work | one current Attempt, assignment `allocated` | provisions that Attempt at that B1 |
| After `git worktree add`, before the acknowledgement | the worktree and branch, still `allocated` | adopts them, acknowledges; no second worktree or branch |
| After the acknowledgement | everything | recognizes the live worktree and returns |
| Worktree directory deleted | the registration and branch | rematerializes at the recorded B1, not live `HEAD` |

Convergence is never a takeover. A path holding foreign files, a registration on
another branch, an enclosure marked by another Attempt, or a branch sitting at
some commit that is not this Attempt's B1 all raise
`WorktreeOwnershipConflict` rather than being adopted or overwritten. A
rematerialized worktree is built from B1, so it carries no prior candidate,
directive, evidence, acceptance or session state; no cross-Attempt reuse path
exists.

### 3.5 One live provisioner per Attempt

The table above reasons about *interrupted* runs, whose leftovers have stopped
changing. A *concurrent* run is a different problem. `git worktree add` publishes
its result in stages — the registration, then the attached branch, then the
checked-out tree — and none of the intermediate states is distinguishable, by
reading, from foreign or half-built state. A reader that lands inside one either
refuses its own Attempt's worktree or acknowledges one that has not finished
arriving. On a B1 of any real size the last window is the wide one: measured at
880–1020 ms against 3.4 ms for the others.

Materializing one Attempt is therefore single-writer on this host. `provision`
holds `flock` on `.broodling-provisioning.lock` in that Attempt's enclosure for
the whole operation, so every observation it makes is of settled state and every
concurrent caller is handed the same finished worktree at B1.

The lock is host-local mutual exclusion between live processes, never durable
authority — that stays with the store's constraints, which is why
`tests/test_store_boundary.py` lets only `provisioning.py` import `fcntl`. The
kernel drops it when a process dies, so the crash windows above recover by
repetition exactly as before, and it is scoped to the enclosure, which is exactly
one Attempt's scaffolding, so two Work Units never queue on each other. V1 is
single-host by qualification; this is not, and must not become, a multi-host
lease or fencing mechanism.

## 4. Retained implementation evidence

`python -m pytest tests` (or `python -m unittest discover -s tests`), 175 tests.
The Attempt tests drive the real `git` binary against fixture repositories. They
need a durable workspace root, which by definition cannot be `/tmp`: they use
`~/.cache/broodling-tests`, overridable with `BROODLING_TEST_WORKSPACE_ROOT`, and
skip if that location is itself non-durable.

| Obligation | Tests |
|---|---|
| Deterministic schema initialization/versioning; store outside a disposable worktree | `tests/test_schema.py` |
| Duplicate/canonical Work Unit resolution; conflicting identity does not alias or overwrite | `tests/test_work_unit_identity.py` |
| Entitlement rejection; self-declaring payloads; exact source material and provenance | `tests/test_entitlement.py` |
| Immutable Contract behaviour; live-source drift; new revision for new meaning | `tests/test_contract_revisions.py` |
| Valid no-effect admission; effect, obligation, Closability and host-profile rejection; obligation preservation | `tests/test_admission.py` |
| Crash and reopen; partial transactions leave no half-admitted authority | `tests/test_crash_recovery.py` |
| Supported and unsupported B1 material; live-HEAD drift; frozen-material digest | `tests/test_starting_state.py` |
| Attempt/revision binding; one-current enforcement; Attempt immutability; workspace-root policy | `tests/test_attempt_admission.py` |
| Worktree at exactly B1; idempotent and rematerializing provisioning; same-repository distinct worktrees; ownership conflicts; no delivery effect | `tests/test_worktree_provisioning.py` |
| Attempt/provisioning crash windows; acknowledgement loss; concurrent identical and conflicting admission; concurrent provisioning convergence and exclusion | `tests/test_attempt_crash_recovery.py` |
| Not a RunLedger mirror; harness not promoted into product code; only provisioning may take a host lock | `tests/test_store_boundary.py` |
| Reproducible concurrency evidence: Git's publication stages, and a classified N-way race | `qualification/v1-p2/issue13_concurrency.py` |

The crash cases run the write in a child process that calls `os._exit` mid-flight
— nothing unwinds, so what survives is what SQLite and Git actually committed.
The concurrency cases start their children behind a gate file so they really
race, and each child reports what *it* was handed rather than what the durable
state settled to afterwards: the durable state is correct in cases where the
caller was not, and only the caller can see the difference.

## 5. Stop boundary

Not implemented here, by design: Zeroshot run submission and run correlation, the
assurance graph, reviewer/adjudicator/final-assessor wiring, candidate seals or
provenance, evidence records, stop/abandon/restart and worktree retirement,
completed-run recovery or catch-up, final-result custody and disposition, effects
or GitHub mutation, multi-host leases or fencing, schedulers, session management
and any product API/CLI. The provisioning lock of §3.5 is host-local mutual
exclusion between live processes on one machine; it holds no durable state,
survives nothing, and is not a step towards a lease or fencing system.

`tests/test_store_boundary.py` asserts this boundary: the table set is fixed, no
table or column carries later-phase or RunLedger vocabulary, the store exposes no
run/recovery/abandon/review operation, only `broodling/git.py` may start a
process, and no module imports a socket or network client, a Zeroshot client or
the qualification harness. `tests/test_worktree_provisioning.py` adds the
behavioural half: after provisioning, a configured remote has no refs, the source
checkout is untouched, and the only new local ref is the Attempt's disposable
branch.

The qualification fixtures under `qualification/` remain evidence. None of them
was promoted into the product package.
