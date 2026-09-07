# Issue #16 — G2-V1 successor admission-nucleus gate re-review

**Review date:** 7 September 2026 (America/Toronto)  
**Review status:** **COMPLETE**  
**G2-V1 verdict:** **PASS**  
**Reviewed Broodling head before this record:** `9e37148b6de8de27935815d0f66497f8c066157f`  
**Scope:** gate/evidence review only; no implementation, remediation, V1-P3 assurance work, V1-P4 abandon/restart work, completed-run recovery/catch-up, candidate sealing/provenance, effects, scheduling, or later-phase machinery

G2-V1 now passes for the deliberately constrained **single-host, one-current-Attempt/one-dedicated-worktree, no-effect V1 profile**.

This is a successor review to issue #15. It does **not** edit, replace, or relabel the historical [`#15` G2-V1 BLOCKED record](issue-15-g2-v1.md) at `00db4a2e403622bcbf9e8fe5a7a0727737ca29a6`. That record remains correct provenance for the repository state and evidence available when it was written. The present PASS rests on the later #13 remediation/evidence and the rebuilt #14 implementation/evidence that are now reachable on `main`.

All original G2-V1 criteria have been re-reviewed as one current product chain. The three blockers recorded by #15 are discharged:

1. **#14 is now reachable and reviewable.** Its rebuilt implementation commit `40ca55d8fd1db2837206f353908b96e81bac66fe` is a direct descendant of #13's remediated/evidenced base `7b334bfedc8bc3bd25a4d61e105670c844310b51`; retained #14 evidence is the next commit, `9e37148b6de8de27935815d0f66497f8c066157f`.
2. **The post-accept/pre-run-ID crash gap is closed without private history.** A real SDK/sidecar witness hard-kills Broodling after Zeroshot publicly accepts run `R` but before the run ID is persisted, then lets that accepted graph mutate the Attempt's dedicated worktree. Restart replays the same durable public submission contract; the qualified `SubmissionConflictError.existing_run_id` path exposes `R`, and Broodling records one immutable Attempt↔`R` correlation. No completed-run scan, RunLedger/SQLite read, candidate seal, or new Attempt/run is used.
3. **The #13 concurrency ambiguity is resolved.** Retained diagnosis separates admission currentness from concurrent provisioning. Across the recorded population, the old failure never produced competing authority; it exposed a real provisioning race in which callers could observe staged `git worktree add` state. `3806128` makes one Attempt's provisioning single-writer on the host with an enclosure-scoped `flock`, while database constraints remain the sole durable authority. Separate currentness and provisioning witnesses now discriminate those facts, and the remediated bulk-worktree race converges with every caller receiving the complete B1 worktree.

No remaining G2-V1 blocker or failure was found.

## 1. Governing basis and evidence order

Authority/evidence was applied in the requested order:

1. current [target v0.5](../../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md), Git blob `3d9fddbc3ed548e952478bc1c18fbb749604a22a`;
2. current [implementation/dependency plan v0.5](../../docs/governing/broodling-implementation-dependency-plan-v0.5.md), Git blob `e7d59b78f86fe7c2c8890ce0b794c7924799f22f`;
3. [G1-V1 PASS](../v1-p1/issue-11-g1-v1.md) at `eebbff192d99797638799f1a624e681dc89bb686`;
4. original [G2-V1 BLOCKED](issue-15-g2-v1.md) at `00db4a2e403622bcbf9e8fe5a7a0727737ca29a6`;
5. #12 implementation `f7db2eca16077a273ca5fcdec13c927658dcc073`;
6. #13 implementation `f42f00d89e662f110254f8779fd3d953b9a125ce`, remediation `38061285bcb3bf35b63b6240d65f606875bfbc4f`, and retained [concurrency/provisioning evidence](issue-13-concurrency.md) at `7b334bfedc8bc3bd25a4d61e105670c844310b51`;
7. #14 rebuilt implementation `40ca55d8fd1db2837206f353908b96e81bac66fe` and retained [correlation evidence](issue-14-correlation.md) at reviewed head `9e37148b6de8de27935815d0f66497f8c066157f`.

The governing v0.5 rule relevant to the repaired #14 behavior is important: **B1 is the immutable admitted starting state and is a pre-first-submit currentness condition.** After a run has been validly submitted, graph-authorized mutating execution may advance the candidate in the exclusively owned Attempt worktree. V1 candidate applicability is structural over that graph-ordered mutation. Requiring the worktree to remain B1 after accepted execution would contradict the qualified profile rather than strengthen it.

The other boundaries remain unchanged: V1 is single-host and no-effect; one current Attempt owns one dedicated disposable worktree; other Work Units use different worktrees; no candidate seal/manifest establishes applicability; completed-run recovery/catch-up is not a V1-P2 dependency; and V1-P3 assurance integration is later work.

## 2. Reachable current implementation chain

The current repository history is a coherent chain, not a collection of local positives:

```text
f7db2ec  #12 admission / Contract nucleus
   ↓
f42f00d  #13 Attempt / B1 / exclusive worktree
   ↓
00db4a2  historical #15 G2-V1 BLOCKED record (documentation only)
   ↓
3806128  #13 provisioning concurrency remediation
   ↓
7b334bf  #13 retained diagnosis/evidence
   ↓
40ca55d  rebuilt #14 submission/run correlation
   ↓
9e37148  #14 retained correlation evidence — reviewed head
```

Repository comparison establishes:

- `f7db2ec` → `9e37148`: `9e37148` is six commits ahead and zero behind;
- `7b334bf` → `40ca55d`: `40ca55d` is exactly one commit ahead and zero behind;
- `40ca55d` → `9e37148`: `9e37148` is exactly one commit ahead and zero behind, and that last commit contains retained #14 evidence only.

Therefore #14's implementation is now inspectable as a direct extension of the remediated #12/#13 product. The old unreachable `012c32f` reference remains historical issue-comment provenance only and is not used by this gate.

The #14 v2→v3 schema migration also discriminates compatibility rather than assuming it: it recognizes only the exact published v2 schema fingerprint, preserves the existing Work Unit/Contract/entitlement/admission/Attempt/worktree rows, creates only the submission-correlation table/triggers, and refuses an unrecognized v2 definition.

## 3. Required implementation-selection review

| Selection | Current evidence | Result |
|---|---|---|
| Python 3.13 product code | `pyproject.toml` requires Python `>=3.13`; retained P2 evidence records CPython 3.13.5. | **PASS** |
| Broodling-owned SQLite outside disposable worktrees | Current schema is v3, stdlib `sqlite3`, `STRICT`, WAL and `synchronous=FULL`; P2 records only admission/currentness/worktree/submission administration. Retained #14 evidence records SQLite 3.46.1. | **PASS** |
| Official Zeroshot Python SDK / matching sidecar externally | #14 uses only the public Python SDK `Client.submit` through `LocalTarget`, and verifies the exact G1-V1 qualified SDK source/version and sidecar hash before dispatch. | **PASS** |
| No unnecessary technology expansion | No Rust embedding, web/service framework, ORM/event-sourcing system, distributed store, RunLedger mirror, candidate-seal catalog, scheduler/session manager, second router/response validator or later-phase subsystem is present. | **PASS** |

Current product version is `broodling 0.1.0`. The qualified external integration remains Zeroshot source `d0909615d6ba3c179b58bce15a059f40400ec995`, SDK `zeroshot-rust 0.1.0.dev0`, wheel SHA-256 `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`, and sidecar SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.

## 4. G2-V1 acceptance-criterion re-review

| G2-V1 obligation | Result | Current evidence / reasoning |
|---|---|---|
| #12–#14 complete as one committed compatible chain | **PASS** | The rebuilt #14 is reachable directly above the #13 remediation/evidence base. `40ca55d` is one commit above `7b334bf`; `9e37148` retains its evidence. Current full-suite evidence executes the #12/#13 tests alongside #14. |
| Work Unit identity | **PASS** | #12's canonical repository+primary-issue identity/uniqueness remains present. The #14 v3 migration preserves the existing Work Unit facts, and the current normal repeated-ingress submission witness resolves the same Work Unit lineage. |
| Source entitlement + frozen Contract | **PASS** | Entitled-source and Contract tables remain immutable/append-only, with attribution to exact entitled bytes. Model/source-like output cannot self-entitle. The v3 migration preserves these rows; #14 currentness rereads the admitted immutable Contract and frozen attributed material rather than live issue text. |
| No-effect Closability | **PASS** | #12's rejection of required effects, effect-dependent evidence, unsupported external/publication obligations and unsupported host assumptions remains intact. #14 additionally rejects any RuntimePlan containing `git_delivery` and supplies the sidecar only a `PATH` environment. No requirement is deleted to achieve admission. |
| Immutable B1 | **PASS** | Each Attempt retains exact B1 commit OID plus frozen admitted material digest. Provisioning/rematerialization uses that B1 despite later live-HEAD movement. For submission, clean B1 is enforced before first dispatch; after dispatch, an owned graph-authorized HEAD advance is permitted and does not rewrite B1. |
| Exclusive worktree/current Attempt | **PASS** | The partial unique current-Attempt index and unique worktree-path/(repository, branch) constraints remain. #13's new admission-only race witnesses currentness separately. The enclosure-scoped host lock removes the staged-provisioning ambiguity; the remediated 4-racer/3000-file record has 0/12 violating rounds and all callers receive the same complete B1 worktree. |
| Attempt immutability | **PASS** | `attempts_no_update`/`attempts_no_delete` remain; an Attempt cannot be rebound to another Contract or B1. Crash/retry converges on the same deterministic Attempt and assignment. No P4 retirement/replacement operation exists in P2. |
| Stable Zeroshot submission | **PASS** | One key and canonical request are stored before the SDK call; the separate durable `dispatched` transition precedes external dispatch. Normal replay and acknowledgement-loss replay recover the same run. Changed request/target/origin/source ownership is rejected and never mints a new key/run/Attempt. |
| Run correlation/currentness | **PASS** | Exactly one public run ID can be stored per Attempt and one Attempt per run ID. Stale Attempts cannot prepare, dispatch or reconcile. Concurrent first submission/replay serializes to one correlation. Crucially, post-dispatch graph-authorized HEAD drift may use the public conflict's nonempty `existing_run_id` to correlate the already-existing run; a true/unexplained conflict remains blocked. |
| Crash integrity | **PASS** | #12 Contract/admission and #13 Attempt/worktree crash windows remain covered. #14 now covers prepare, post-prepare, post-dispatch/pre-call, post-accept/pre-ID, post-accept+mutation/pre-ID, correlation-write, post-correlation and a second process death after the public conflict. Every recovery retains one immutable key/request/Attempt/run lineage without competing authority. |
| Qualified-profile preservation | **PASS** | #13's `flock` is single-host live-process mutual exclusion scoped to one Attempt enclosure, not durable authority, a lease or multi-host fence. #14 verifies the same G1-V1 SDK/sidecar build, uses the Attempt's dedicated attached worktree, preserves the no-effect boundary and relies on the qualified exclusive-writer assumption after dispatch. |
| Scope audit | **PASS** | Current boundary tests prohibit runtime-history mirrors and later-phase vocabulary. The adapter exposes submit/public-conflict only: no status/history/watch/wait/logs/list-runs/get-run/force-stop/private SQLite/RunLedger access. No V1-P3 assurance/final-result wiring, P4 abandon/restart behavior, completed-run catch-up, cross-Attempt semantic reuse, candidate seal/provenance, effects/receipts/reconciliation, GitDelivery/publication, scheduler or session manager is introduced. |
| Successor gate record | **PASS by this commit** | This file records review status and verdict separately, immutable evidence identities, exact versions/profile, limitations and the disposition of the historical blockers. |
| PASS only when every current P2 obligation passes | **PASS as gate rule; verdict is PASS** | No current G2-V1 obligation remains blocked, failed or ambiguous. |

The retained #14 full product-suite record is **211 passed plus 1775 subtests**, including **17 real SDK/sidecar cases**. With the qualified SDK unavailable, **194 pass and 17 are explicitly skipped** rather than silently substituted. The unchanged #13 admission/provisioning/crash tests are part of those suites.

## 5. Integrated discriminating scenario

The required integrated G2 scenario is established, including a stronger version of the acknowledgement-loss timing that originally blocked #15:

```text
same explicit issue/repository ingress twice
  → one Work Unit
  → one frozen no-effect Contract
  → one immutable current Attempt at original B1
  → one exclusively owned attached worktree
  → one durable submission key + canonical request
  → durable dispatch intent while worktree is clean B1
  → Zeroshot publicly accepts run R
  → Broodling process dies before persisting R
  → accepted graph's authorized mutating step advances that same worktree
  → restart retains the same Attempt/key/request but HEAD is no longer B1
  → replay of the persisted public submission contract
  → SubmissionConflictError(existing_run_id = R)
  → one immutable Attempt ↔ R correlation
```

The retained machine witness `evidence/issue-14-graph-crash.json` records:

- `passed: true`;
- implementation commit `40ca55d...`;
- one Work Unit, one Contract revision, one Attempt, one worktree assignment and one submission row;
- B1 `da0c7d6eaaf87fb4c8b7999d281f3e3456dafb29`;
- a different graph-mutated HEAD `fef36bf1d0774f4a1707f9bf5711ace87767c222`;
- one submission key/request;
- one correlated public run `01a07d93-75cd-7f61-a1ed-796b35222a35`;
- the exact G1-V1 qualified SDK/sidecar build.

The witness asserts the ordering, rather than inferring it from the final state: the child exits after public acceptance and before run-ID persistence; the parent first confirms the worktree is still B1 and the correlation is absent; only then is the accepted graph mutation released. This makes the post-accept/pre-ID/mutated-worktree window discriminating.

The paired #14 regression is also discriminating: injecting the rejected old policy that requires B1 on replay makes the mutation acknowledgement-loss test fail with `SubmissionNotReady`; the current policy passes the same test. The PASS therefore does not come from a test that would also accept the old blocker.

Rejecting controls cover the other G2 branches:

- live issue/source changes cannot amend the frozen Contract;
- live source HEAD drift does not move the recorded B1 or rematerialized pre-run worktree;
- dirty/staged/untracked material is rejected as unsupported B1 before Attempt admission/first dispatch;
- non-empty required effects/effect-dependent evidence and GitDelivery are rejected;
- same-worktree/branch ownership collisions fail closed;
- differing concurrent Attempt admissions leave one current authority and the other conflicts;
- a true public conflicting request or conflicting source under the same submission key blocks rather than correlating or creating a new identity;
- changed request, target/environment, origin, branch, repository/common-Git ownership or enclosure marker cannot be excused by candidate drift;
- dirty files alone do not excuse a public conflict;
- a public conflict with no run ID cannot establish correlation;
- stale/non-current Attempts cannot submit or reconcile; and
- crashes across every durable/side-effect boundary converge without a second current Attempt, worktree, key or run correlation.

No semantic-assurance or Work Unit success claim is made by this scenario. A correlated run means only that this run belongs to this Attempt.

## 6. Disposition of the three #15 blockers

### 6.1 Historical blocker: #14 implementation not reachable — **DISCHARGED**

#15 could not resolve the original `012c32f`, and the product tree available then did not contain #14. That historical finding remains correct.

The successor evidence is different and complete: #14 was rebuilt on the current #13-remediated base. Repository comparison proves `40ca55d` is directly above `7b334bf`, and `9e37148` is directly above `40ca55d` with retained evidence. The current schema/modules/tests are therefore reviewable repository evidence, not an issue-comment claim.

### 6.2 Historical blocker: post-accept/pre-ID crash after candidate mutation — **DISCHARGED**

The current implementation makes the B1 boundary explicit:

```text
prepared / before first dispatch
    → require exclusively owned clean B1

dispatched / accepted-run reconciliation
    → preserve immutable B1 record and source ownership
    → candidate HEAD may have advanced under the qualified run
    → replay exact persisted request
    → if public conflict + owned HEAD drift + nonempty existing_run_id:
         correlate that existing run
    → otherwise fail closed
```

This uses only the public submission/conflict contract already qualified at G1-V1. `zeroshot_sdk.py` imports `SubmissionConflictError.existing_run_id` and maps it directly into the narrow product conflict object. It opens no private Zeroshot database and performs no status/history/completed-occurrence lookup.

The recovery is deliberately narrow. It does **not** claim arbitrary foreign-writer detection, deleted runtime-store reconstruction, hostile key injection recovery, completed-run salvage or cross-Attempt semantic catch-up. Those are not G2-V1 obligations. Under the qualified one-Attempt/exclusive-worktree/exclusive-writer profile, the changed owned HEAD after durable dispatch is the admissible graph-authorized mutation case this gate requires.

### 6.3 Historical blocker: concurrent-identical-Attempt/provisioning ambiguity — **DISCHARGED**

The retained #13 diagnosis separates two properties that the old end-to-end witness mixed:

- **currentness:** the database decides one current Attempt per Work Unit; deterministic identical IDs, `BEGIN IMMEDIATE`, the partial unique current index, and unique worktree ownership prevent competing authority;
- **provisioning convergence:** callers must not inspect or acknowledge a `git worktree add` that another live process is still publishing in stages.

Across 624 retained rounds before and after remediation, no round was classified `competing_authority` or `duplicate_worktree`; the old failures were `inconsistent_result`. The diagnosis also found a serious false-success case that the original one-file fixture barely exposed: after the branch was attached but before checkout completed, a loser could return `provisioned=true` while observing zero of 3000 tracked files. Against the reviewed tree this reproduced in 12/12 bulk rounds.

The remediation is an Attempt-enclosure `flock` around the entire provisioning operation. It is intentionally not durable state and not an authority mechanism; SQLite constraints remain authoritative. Process death releases it, so existing crash-repeat behavior remains intact. The lock is local to one Attempt enclosure, so unrelated Work Units do not queue and no multi-host lease/fencing claim is introduced.

The remediated bulk witness records 12 rounds × 4 concurrent callers × 3000-file B1 with zero violations. Every caller receives the same Attempt, path, branch, B1 and full tracked-file population. Separate tests now witness admission-only currentness and lock exclusion directly, so a future provisioning error cannot again make the currentness fact ambiguous.

## 7. Profile compatibility and scope audit

### 7.1 #13 remediation stays inside the qualified profile

`flock` is a Linux/single-host live-process mutual exclusion primitive around host-local administrative worktree provisioning. It does not:

- change Work Unit or Attempt authority;
- add durable lease/fencing state;
- introduce a scheduler;
- serialize unrelated Work Units;
- change B1 or worktree ownership semantics;
- add a second execution engine; or
- make a multi-host claim.

The kernel drops the lock on process death, preserving the existing repeat-after-crash protocol.

### 7.2 #14 remediation stays inside the qualified profile

The submission adapter:

- uses the official qualified Python SDK `LocalTarget` and matching sidecar;
- calls only public `Client.submit` and consumes the public submission-conflict run ID;
- records only Attempt ID, stable key, canonical request, small administrative state, optional unique run ID and error detail;
- preserves Broodling durable currentness as the authority source;
- requires clean B1 before first dispatch and permits only the already-owned candidate to advance after durable dispatch;
- gives the sidecar only `PATH`; no GitHub/OpenAI/Git credential environment is inherited through the adapter;
- rejects `git_delivery` bindings;
- does not read status/history/logs/usage/completed occurrences or private Zeroshot SQLite/RunLedger; and
- treats correlation as ownership only, not runtime success, semantic acceptance or final disposition.

The current store-boundary tests make these omissions executable constraints. `zeroshot_run_id` is the sole permitted runtime correlation fact in P2; later-phase and RunLedger-mirror vocabulary remains absent from the schema.

### 7.3 Specifically not introduced or required

This gate neither requires nor finds:

- private Zeroshot history access;
- completed-run occurrence recovery or semantic catch-up;
- a candidate seal/hash/manifest/provenance service;
- cross-Attempt source/evidence/decision reuse;
- V1-P4 abandon/restart product behavior;
- delivery/effect intents, receipts or reconciliation;
- GitDelivery, commit/push/publication/PR/issue mutation;
- V1-P3 reviewer/adjudicator/final-assessor product wiring;
- a scheduler, session manager, second router/response validator or runtime-history mirror; or
- distributed/multi-host fencing guarantees.

The qualification fixtures used to discriminate #13/#14 remain qualification evidence; they are not promoted into production assurance machinery.

## 8. Evidence identities and versions

| Item | Reviewed identity |
|---|---|
| Governing target v0.5 | blob `3d9fddbc3ed548e952478bc1c18fbb749604a22a`; governing commit `6a866c5304415e2c03b09f442312adfbccac3c68` |
| Governing plan v0.5 | blob `e7d59b78f86fe7c2c8890ce0b794c7924799f22f`; governing commit `6a866c5304415e2c03b09f442312adfbccac3c68` |
| G1-V1 | PASS record `eebbff192d99797638799f1a624e681dc89bb686` |
| Historical G2-V1 | BLOCKED record `00db4a2e403622bcbf9e8fe5a7a0727737ca29a6`; preserved unchanged |
| #12 implementation | `f7db2eca16077a273ca5fcdec13c927658dcc073` |
| #13 initial implementation | `f42f00d89e662f110254f8779fd3d953b9a125ce` |
| #13 remediation | `38061285bcb3bf35b63b6240d65f606875bfbc4f` |
| #13 retained evidence | `7b334bfedc8bc3bd25a4d61e105670c844310b51` |
| #14 rebuilt implementation | `40ca55d8fd1db2837206f353908b96e81bac66fe` |
| #14 retained evidence / reviewed pre-gate head | `9e37148b6de8de27935815d0f66497f8c066157f` |
| Product | `broodling 0.1.0`; Python requirement `>=3.13`; retained runs use Python 3.13.5 |
| Persistence | SQLite schema v3; retained #14 run SQLite 3.46.1; schema SHA-256 `663acf981b7b5379bde50f9446d12bee922c81bcbdcc1b4e7f15f6c4e5ea007f` |
| #13 Git evidence | Git 2.47.3, Linux x86-64, Python 3.13.5 |
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995` |
| SDK | `zeroshot-rust 0.1.0.dev0`; wheel SHA-256 `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| Installed SDK Python source | SHA-256 `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e` |
| Sidecar | SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |

## 9. Limits of this PASS

G2-V1 PASS establishes only the V1-P2 admission/currentness/worktree/submission-correlation nucleus on the named qualified profile.

It does **not** establish:

- semantic reviewer/adjudicator/final-assessor reliability in product code;
- normal Work Unit final disposition or `SUCCEEDED`;
- completed-run salvage, occurrence scanning, cross-Attempt semantic catch-up or reuse;
- abandoned/replacement product lifecycle behavior;
- effect authorization/execution/reconciliation or publication;
- a general source/candidate provenance platform;
- multi-host/distributed operation or protection from an arbitrary hostile writer on the same filesystem/runtime state; or
- release readiness.

The public-conflict reconciliation after mutation specifically relies on the qualified single-host/exclusive-worktree/exclusive-writer profile: Broodling owns the key, the persisted request is the only request it dispatches under that key, and after dispatch only the accepted run is permitted to graph-authorizedly advance that Attempt's candidate. A different future profile would need its own qualification rather than inheriting this result.

## 10. Gate verdict

**Review status: COMPLETE.**  
**G2-V1 verdict: PASS.**

Every current V1-P2/G2-V1 obligation is supported by the coherent reachable implementation and retained evidence at `9e37148b6de8de27935815d0f66497f8c066157f`.

The historical issue #15 / `00db4a2` **BLOCKED** verdict remains unchanged as provenance. This successor gate records the later state after the named remediations; it does not rewrite history.

G2-V1 PASS clears this gate as a dependency for later separately authorized work. **This review does not create, start or perform V1-P3.**
