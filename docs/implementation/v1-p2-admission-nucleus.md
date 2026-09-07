# V1-P2 — admission and durable semantic nucleus

**Issue:** [#12](https://github.com/faviann/broodling/issues/12)
**Date:** 7 September 2026
**Governing pair:** [target v0.5](../governing/broodling-target-responsibility-boundary-design-v0.5.md),
[implementation/dependency plan v0.5](../governing/broodling-implementation-dependency-plan-v0.5.md).
**Gate dependency:** [G1-V1 PASS](../../qualification/v1-p1/issue-11-g1-v1.md).

This is the first Broodling product implementation. It covers the admission/store
boundary only: Work Unit identity, source entitlement, immutable Contract
revisions and the V1 no-effect Closability/admission decision. It does not begin
issue #13.

## 1. Selected product configuration

| Selection | Value | Why it is sufficient for P2 |
|---|---|---|
| Language/runtime | Python 3.13 (`requires-python = ">=3.13"`); recorded here on CPython **3.13.5**, Linux x86-64 | The G1-V1 profile was qualified through the official Python SDK on Python 3.13.x. P2 needs durable admission logic, not in-process Zeroshot access. |
| Packaging | one importable package, `broodling/`, plus `pyproject.toml` | The plan defers CLI/service packaging to V1-P5. A domain package is enough to run the store tests. |
| Persistence | one Broodling-owned SQLite database via the standard library `sqlite3`; recorded here on SQLite **3.46.1** | V1 is single-host. P2 needs transactional uniqueness and immutable administrative records, not a distributed database or an ORM. |
| Schema | version **1**, definition digest `376f0c21705ef12038b10cff4e66c1200116b145423d489bb0458c749e473c53` | Recorded in `schema_meta` at initialization and re-checked on every open, so a store written by different DDL is refused rather than migrated implicitly. |
| Table mode | SQLite `STRICT` tables (needs SQLite ≥ 3.37) | Makes column typing an enforced durable property instead of a convention. |
| Durability | `journal_mode=WAL`, `synchronous=FULL`, `foreign_keys=ON` | A committed fact survives process death; an interrupted write leaves nothing. |
| Store location | outside disposable Attempt worktrees; `BROODLING_STORE`, else `$XDG_STATE_HOME/broodling/broodling.sqlite3` | The record must outlive an abandoned Attempt and its retired worktree. |

### 1.1 Qualified external runtime boundary

P2 records this boundary and submits nothing to it. No Zeroshot SDK or sidecar is
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
Attempt, no worktree, no Zeroshot run. Only a committed `admitted` decision is
authority, which is what makes an interrupted admission unambiguous — a stored
revision with no decision is simply not admitted.

## 3. Retained implementation evidence

`python -m pytest tests` (or `python -m unittest discover -s tests`), 93 tests.

| Obligation | Tests |
|---|---|
| Deterministic schema initialization/versioning; store outside a disposable worktree | `tests/test_schema.py` |
| Duplicate/canonical Work Unit resolution; conflicting identity does not alias or overwrite | `tests/test_work_unit_identity.py` |
| Entitlement rejection; self-declaring payloads; exact source material and provenance | `tests/test_entitlement.py` |
| Immutable Contract behaviour; live-source drift; new revision for new meaning | `tests/test_contract_revisions.py` |
| Valid no-effect admission; effect, obligation, Closability and host-profile rejection; obligation preservation | `tests/test_admission.py` |
| Crash and reopen; partial transactions leave no half-admitted authority | `tests/test_crash_recovery.py` |
| Not a RunLedger mirror; harness not promoted into product code | `tests/test_store_boundary.py` |

The crash cases run the write in a child process that calls `os._exit` mid-flight
— nothing unwinds, so what survives is what SQLite actually committed.

## 4. Stop boundary

Not implemented here, by design: Attempts, worktree provisioning, B1
materialization, submission correlation/currentness beyond Work Unit identity,
Zeroshot run submission, the assurance graph, reviewer/adjudicator/final-assessor
wiring, candidate seals or provenance, evidence records, effects or GitHub
mutation, recovery/catch-up, final-result custody, schedulers, session management
and any product API/CLI.

`tests/test_store_boundary.py` asserts this boundary: the table set is fixed,
no table or column carries later-phase or RunLedger vocabulary, the store exposes
no Attempt/run/recovery operation, and the package imports only the standard
library — no Zeroshot client, no qualification harness, no subprocess or socket.

The qualification fixtures under `qualification/` remain evidence. None of them
was promoted into the product package.
