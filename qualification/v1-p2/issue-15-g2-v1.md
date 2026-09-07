# Issue #15 — G2-V1 admission-nucleus gate review

**Review date:** 7 September 2026 (America/Toronto)  
**Review status:** **COMPLETE**  
**G2-V1 verdict:** **BLOCKED**  
**Reviewed reachable Broodling head before this record:** `f42f00d89e662f110254f8779fd3d953b9a125ce`  
**Scope:** gate/evidence review only; no implementation, remediation, V1-P3 assurance work, abandon/restart work, completed-run recovery/catch-up, candidate sealing/provenance, effects, scheduling or later-phase machinery

The review itself is complete, but G2-V1 does **not** pass. The reachable #12/#13 product nucleus supports the Work Unit, frozen Contract, no-effect admission, immutable B1, one-current-Attempt and exclusive-worktree parts of P2. The submission/correlation end of the chain is not currently sufficient to establish G2-V1.

Three current blockers prevent PASS:

1. **#14 committed implementation/evidence is not reachable in the repository.** Issue #14's closeout names short commit `012c32f`, but GitHub cannot resolve that commit; the only reachable branch is `main`, still at #13 commit `f42f00d89e662f110254f8779fd3d953b9a125ce` before this gate record. The reachable product has schema version 2 and no `attempt_submissions`, `broodling/submission.py` or `broodling/zeroshot_sdk.py`. Therefore #12→#13→#14 cannot presently be reviewed as one committed implementation chain. **Owner: #14.**
2. **#14's reported post-accept/pre-correlation replay behavior does not prove one unambiguous Attempt↔run relationship once the run has changed its worktree from B1.** The qualified public SDK exposes `SubmissionConflictError.existing_run_id`, and the G1-V1 W1 evidence already demonstrated that a conflicting request or changed-source replay under the same key raises that public conflict naming the already-existing run. #14's closeout instead records the B1-drift case as terminal `blocked` rather than correlating that exposed existing run identity. That leaves the Attempt without the required durable run correlation in exactly the crash window G2-V1 requires. No private history scan or completed-occurrence recovery is needed to state this failure; the missing behavior is at the already-qualified public submission/conflict boundary. **Owner: #14.**
3. **The concurrent-identical-Attempt evidence is ambiguous.** The gate input records one isolated failure of the pre-existing `test_two_identical_concurrent_admissions_resolve_one_attempt` during repeated full-suite runs. The reachable #13 implementation has a strong database invariant—a partial unique index permits at most one current Attempt per Work Unit, and `BEGIN IMMEDIATE` serializes admission—but the retained gate evidence does not diagnose the observed failing race or retain enough failing-state evidence to establish that it was harmless to the required identical-request convergence/currentness behavior. G2-V1 explicitly says an ambiguous currentness fact prevents PASS. **Owner: #13.**

V1-P3 remains blocked until these P2 obligations are demonstrated. This record does not prescribe or implement their fixes.

## 1. Governing basis

Authority/evidence order used by this review:

1. [target v0.5](https://github.com/faviann/broodling/blob/6a866c5304415e2c03b09f442312adfbccac3c68/docs/governing/broodling-target-responsibility-boundary-design-v0.5.md);
2. [implementation/dependency plan v0.5](https://github.com/faviann/broodling/blob/6a866c5304415e2c03b09f442312adfbccac3c68/docs/governing/broodling-implementation-dependency-plan-v0.5.md), especially V1-P2/G2-V1;
3. [G1-V1 PASS](https://github.com/faviann/broodling/blob/eebbff192d99797638799f1a624e681dc89bb686/qualification/v1-p1/issue-11-g1-v1.md);
4. #12 implementation at [`f7db2eca16077a273ca5fcdec13c927658dcc073`](https://github.com/faviann/broodling/commit/f7db2eca16077a273ca5fcdec13c927658dcc073) and its [closeout evidence](https://github.com/faviann/broodling/issues/12#issuecomment-5571927547);
5. #13 implementation at [`f42f00d89e662f110254f8779fd3d953b9a125ce`](https://github.com/faviann/broodling/commit/f42f00d89e662f110254f8779fd3d953b9a125ce) and its [closeout evidence](https://github.com/faviann/broodling/issues/13#issuecomment-5572296372);
6. #14 [closeout report](https://github.com/faviann/broodling/issues/14#issuecomment-5574590131), together with the reachable repository state and the qualified Zeroshot public conflict contract.

The target rule relevant to blocker 2 is explicit: before submission Broodling persists stable submission identity, and **ambiguous submission remains blocking until the existing run is reconciled**; a submission conflict never authorizes another run or Attempt. G2-V1 makes the corresponding P2 requirement more concrete: a crash after Zeroshot accepts a run but before Broodling persists its run ID must still leave one recoverable Attempt↔run relationship through the public submission contract.

The qualified public SDK at Zeroshot `d0909615d6ba3c179b58bce15a059f40400ec995` defines [`SubmissionConflictError`](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/sdks/python/src/zeroshot/run_errors.py) with a public `existing_run_id` field. Its diagnostic projector obtains that value from `details.existingRunId` in [`_process.py`](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/sdks/python/src/zeroshot/_process.py). G1-V1's retained W1 evidence at [`7931ac9`](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue-8-w1-w2-w5-w7.md) demonstrated both conflicting-request and changed-source replay raising `SubmissionConflictError` **naming the one existing run**, with no second run created.

That public existing-run identity is evidence already inside the qualified profile. Treating the conflict as terminal `blocked` without recording the exposed run correlation does not satisfy the G2 requirement merely because it safely refuses a duplicate.

## 2. Required implementation-selection review

| Selection | Review | Result |
|---|---|---|
| Python 3.13 product code | Reachable `pyproject.toml` requires Python `>=3.13`; #12/#13 record CPython 3.13.5. | **PASS** |
| Broodling-owned SQLite outside disposable worktrees | Reachable product uses stdlib `sqlite3`, `WAL`, `synchronous=FULL`; store path is outside Attempt worktrees. #13 schema version is 2. | **PASS for reachable #12/#13** |
| Official Zeroshot Python SDK / matching sidecar externally | G1-V1 qualifies this boundary; #14 reports using it and records the qualified source/wheel/sidecar identities, but its product adapter is not reachable in the repository. | **UNPROVED for committed #14 product** |
| No unnecessary technology expansion | Reachable product has no dependency, web/service framework, Rust embedding, ORM/event sourcing, distributed store, RunLedger mirror, candidate-seal catalog, scheduler/session manager or later-phase subsystem. | **PASS on reachable tree** |

No incompatible technology selection was found. The gate is blocked by missing/insufficient P2 behavior/evidence, not by a need to reopen general technology selection.

## 3. G2-V1 acceptance-criterion review

| G2-V1 obligation | Review result | Evidence / reason |
|---|---|---|
| #12–#14 complete as one committed compatible chain | **BLOCKED — #14** | #12 and #13 are reachable as `f7db2ec` → `f42f00d`. #14's named `012c32f` is not resolvable, `main` remained at #13 before this gate record, and its claimed submission modules/schema v3 are absent from the reachable product. |
| Work Unit identity | **PASS** | #12's canonical `host/owner/repository#issue` identity and database uniqueness resolve repeated ingress to one Work Unit; conflicting pinned upstream identity raises rather than aliases/overwrites. |
| Source entitlement + frozen Contract | **PASS** | Entitlement is granted by caller/Broodling policy, never payload self-assertion; exact entitled bytes/source digests are attributed to immutable canonical Contract bytes with append-only database protection. Live source changes create new snapshots/revisions rather than amending the admitted revision. |
| No-effect Closability | **PASS** | Valid empty-required-effect Contracts admit; required effects, effect-dependent evidence, unsupported external/publication obligations, missing finite evidence/validation facts and unqualified host assumptions reject while preserving the refused obligation. |
| Immutable B1 | **PASS** | #13 binds the Attempt to exact commit OID plus frozen admitted material digest. Dirty/staged/untracked starting state is rejected. Provision/rematerialization uses recorded B1 rather than later live HEAD. |
| Exclusive worktree/current Attempt | **BLOCKED — #13 evidence ambiguity** | Reachable schema has one-current partial unique index and globally unique worktree path plus `(repository, branch)` ownership. Same-repository distinct-worktree tests are retained. However the direct concurrent-identical-Attempt witness had one unexplained isolated failure in repeated full-suite runs; current evidence does not establish that the observed failure was harmless to identical concurrent convergence/currentness. |
| Attempt immutability | **PASS** | Attempt row is append-only/undeletable and permanently references one Contract revision and B1. No replacement/retirement operation exists in P2. |
| Stable Zeroshot submission | **BLOCKED — #14** | #14 reports stable key/request persistence and normal/B1-preserving replay. But its committed implementation is unavailable, and its recorded B1-drift replay ends `blocked` instead of persisting the public existing run identity. That does not meet the post-accept/pre-ID crash requirement. |
| Run correlation/currentness | **BLOCKED — #14; #13 evidence ambiguity** | G2 requires exactly one run to become correlated to the current Attempt. In the B1-drift acknowledgement-loss window, #14 reports no run correlation even though the public conflict names the existing run. #13's unexplained concurrency-witness failure also leaves currentness evidence ambiguous. |
| Crash integrity | **BLOCKED — #14** | #12 admission and #13 Attempt/worktree crash windows are well covered and converge without duplicate current authority. The submission crash window after Zeroshot acceptance but before run-ID persistence is not closed when the run has mutated the worktree before restart. |
| Qualified-profile preservation | **UNPROVED for #14 committed product; no incompatible change identified** | #12/#13 preserve the single-host/dedicated-worktree/no-effect policy. #14's report names the exact qualified SDK/sidecar hashes and no-effect checks, but the corresponding committed adapter/tests cannot be inspected. |
| Scope audit | **PASS on reachable product; #14 report is scope-compatible** | The reachable tree contains no V1-P3 graph/evidence/final disposition, abandon/restart, completed-run scanning/catch-up, cross-Attempt reuse, candidate seal/provenance, effects/GitDelivery, scheduler/session manager, second router/validator or runtime-history mirror. #14's closeout also reports those cuts held. |
| Gate record | **PASS by this commit** | This record separates review completion from gate verdict, records versions/evidence/limitations and names the owning implementation issues for blockers. |
| PASS only when every obligation passes | **PASS as a gate rule; verdict is BLOCKED** | Current obligations above are unproved/failed, so this review does not record G2-V1 PASS. |

## 4. Integrated discriminating scenario

The required integrated scenario is **not currently established end-to-end by committed product evidence**.

The #12/#13 portion is supported:

```text
same explicit issue/repo ingress twice
  → one Work Unit
  → one frozen no-effect Contract
  → one immutable Attempt at B1
  → one exclusive worktree
```

#14 reports the following narrower positive while the worktree remains exactly B1:

```text
one durable submission key
  → Client.submit accepts one run
  → caller-visible acknowledgement loss
  → unchanged replay from B1
  → same run id
  → one correlation
```

But the G2 crash obligation also covers the admissible timing where the accepted mutating run advances the worktree before Broodling restarts:

```text
Attempt A current at B1
  → durable key/request K
  → Zeroshot accepts run R for K
  → Broodling loses R before persisting it
  → R begins graph-authorized mutation; worktree no longer equals B1
  → restart/reconcile K
  → public replay raises SubmissionConflictError(existing_run_id=R)
  → current #14 report records terminal blocked
  → Broodling still has no durable A ↔ R correlation
```

That is not a duplicate-run safety failure: no second run is authorized. It is a **correlation/recoverability failure**. G2 requires the relationship to remain unambiguous, and the already-qualified public conflict carries the identity needed to know which existing run owns K. The gate does not prescribe how #14 should change; it records only that current behavior does not establish the required result.

Rejecting controls outside that gap are supported by #12/#13 and the #14 report: live issue/HEAD drift does not rewrite the frozen Contract/B1; effect-bearing/effect-dependent Contracts reject; worktree/branch ownership collisions fail; differing current Attempt admission conflicts; conflicting submission never authorizes a fresh key/run/Attempt; and pre-submit stale Attempt checks are reported. The unexplained concurrent-identical-Attempt test failure prevents the concurrency/currentness control from being considered fully closed.

## 5. Blocking failure disposition

### 5.1 Owner #14 — committed submission implementation/evidence is missing

**Exact missing evidence:** make the #14 implementation/test commit reachable as repository evidence and part of the reviewable #12→#13→#14 chain. At review time GitHub exposes only `main` at #13 commit `f42f00d...`; the #14 closeout's `012c32f` cannot be resolved and its claimed schema v3/submission modules are absent.

This gate does not reconstruct or implement #14 from its narrative.

### 5.2 Owner #14 — post-accept/pre-correlation replay after B1 mutation

**Invariant:** a crash after Zeroshot accepts the run but before Broodling persists the run ID must still leave one unambiguous Attempt↔run relationship; ambiguous submission remains blocking **until the existing run is reconciled**.

**Smallest reproducer:** lose the returned run ID after `Client.submit()` accepts run R, allow R to make a graph-authorized mutation so the dedicated worktree is no longer B1, then restart and replay the persisted key/request. The qualified public SDK returns `SubmissionConflictError(existing_run_id=R)`. The #14 report says current product records `blocked` rather than correlating R.

**Exact missing behavior/evidence:** demonstrate through the supported public submission/conflict contract that this window durably establishes the existing run identity as the one run correlated to the Attempt, without minting a new key/run/Attempt and without private RunLedger/SQLite history scanning. The gate does not prescribe the implementation.

### 5.3 Owner #13 — concurrent-identical-Attempt evidence ambiguity

**Invariant:** one Work Unit has at most one current Attempt and concurrent/repeated identical Attempt admission cannot create competing persisted authority; the identical request must converge on the one current Attempt or otherwise fail in a way shown not to violate the G2 currentness contract.

**Smallest reproducer:** `tests/test_attempt_crash_recovery.py::ConcurrentAdmissionTests::test_two_identical_concurrent_admissions_resolve_one_attempt` races two child processes behind the same gate on the same Work Unit/revision/B1 and expects both to resolve the same derived Attempt ID with one persisted Attempt.

**Exact missing evidence:** the recorded isolated failure across repeated full-suite runs has no retained diagnosis/state artifact sufficient for this gate to classify it as harmless. Retain/reproduce enough evidence to establish what failed and that the resulting durable state satisfies the currentness/convergence invariant, or correct the owning implementation if it does not. The database uniqueness mechanism is necessary evidence for at-most-one current row, but it does not by itself explain the observed failing end-to-end concurrency witness.

## 6. Versions and evidence identity

| Item | Reviewed value |
|---|---|
| Governing target/plan | v0.5 at `6a866c5304415e2c03b09f442312adfbccac3c68` |
| G1-V1 | PASS record at `eebbff192d99797638799f1a624e681dc89bb686` |
| #12 implementation | `f7db2eca16077a273ca5fcdec13c927658dcc073` |
| #13 / reachable product head before gate | `f42f00d89e662f110254f8779fd3d953b9a125ce` |
| #14 closeout claim | `012c32f` — **not resolvable/reachable at review time** |
| Product Python | CPython 3.13.5 recorded; package requires `>=3.13` |
| Reachable SQLite/schema | SQLite 3.46.1 recorded; schema version 2 at #13 |
| #14 reported schema | version 3 with `attempt_submissions` — **not reachable for inspection** |
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995` |
| SDK | `zeroshot-rust 0.1.0.dev0`; wheel SHA-256 `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| Sidecar | `zeroshot-rust 0.1.0`; SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Effects | required effects `[]`; no GitDelivery/publication |

## 7. Preserved scope cuts and limitations

This review adds no V1-P3 work, assurance graph integration, evidence/final-result implementation, V1-P4 abandon/restart behavior, completed-run occurrence scanning/catch-up, candidate sealing/provenance, cross-Attempt semantic reuse, effects/intents/receipts/reconciliation, GitDelivery/publication, scheduler/session manager, response validator, runtime-history mirror, distributed store or multi-host claim.

The #14 replay blocker is specifically **not** a reason to introduce private Zeroshot history recovery. The public qualified conflict already exposes the existing run identity; this review merely determines that the reported Broodling behavior does not currently turn that public fact into the required durable Attempt↔run relationship.

The concurrency blocker likewise does not assert that #13 definitely created duplicate authority. It records the narrower gate conclusion required by issue #15: an unexplained failure of the direct concurrency witness leaves currentness evidence ambiguous, and an ambiguous current G2 fact prevents PASS.

## 8. Gate disposition

**Review status: COMPLETE.**  
**G2-V1: BLOCKED.**

Smallest owning implementation issues:

- **#14** — committed submission implementation/evidence must be reachable, and the post-accept/pre-ID crash window after worktree mutation must preserve one durable Attempt↔existing-run relationship through the qualified public conflict surface;
- **#13** — the isolated concurrent-identical-Attempt witness failure must be retained/diagnosed sufficiently to establish the G2 concurrency/currentness invariant.

Issue #15 may close because the gate review is complete. **V1-P3 and all later V1 product phases remain blocked until a subsequent G2-V1 review can establish every current P2 obligation and record PASS.**