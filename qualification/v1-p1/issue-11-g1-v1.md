# Issue #11 — G1-V1 narrowed-profile gate review

**Review date:** 7 September 2026 (America/Toronto)  
**Review status:** **COMPLETE**  
**G1-V1 verdict:** **PASS**  
**Reviewed Broodling head before this record:** `259a64aa7c06602b6f0fd49fe6af064097211046`  
**Scope:** evidence/gate review only; no qualification rerun, product implementation, architecture redesign, effect work, recovery/catch-up work, or later-issue creation

G1-V1 passes for the deliberately narrowed **single-host, one-Attempt/one-dedicated-worktree, no-effect V1 profile**. All current v0.5 W1–W7 obligations are supported by compatible retained evidence. This verdict does not relabel historical v0.3/v0.4 results and does not begin V1-P2.

## 1. Governing basis and preservation boundary

The governing files reviewed are the current v0.5 pair, whose blobs on `main` are unchanged from the v0.5 governing commit `6a866c5304415e2c03b09f442312adfbccac3c68`:

- [target v0.5](https://github.com/faviann/broodling/blob/6a866c5304415e2c03b09f442312adfbccac3c68/docs/governing/broodling-target-responsibility-boundary-design-v0.5.md), Git blob `3d9fddbc3ed548e952478bc1c18fbb749604a22a`;
- [implementation/dependency plan v0.5](https://github.com/faviann/broodling/blob/6a866c5304415e2c03b09f442312adfbccac3c68/docs/governing/broodling-implementation-dependency-plan-v0.5.md), Git blob `e7d59b78f86fe7c2c8890ce0b794c7924799f22f`.

The review then used the preserved documentation/provenance record and completed V1-P1 qualification evidence in dependency order:

1. [G0-v0.4](https://github.com/faviann/broodling/blob/fa24093f2bb27e88ef1cebcb482b4030fa6f1ebc/docs/governing/g0-v0.4-review.md) — documentation-only PASS;
2. issue #8 evidence at [`7931ac9`](https://github.com/faviann/broodling/commit/7931ac9acd70b8670dfcaa48c05982897766367d);
3. issue #9 / W3 at [`6c5ab62`](https://github.com/faviann/broodling/commit/6c5ab629d5f45a38e3913d706257cfc79644f3cc);
4. issue #10 / W4+W6 at [`259a64a`](https://github.com/faviann/broodling/commit/259a64aa7c06602b6f0fd49fe6af064097211046).

The v0.5 rules applied exactly are:

- **candidate applicability is structural**: exclusive Attempt worktree ownership + graph-authorized ordered mutation + read-only assurance + writer containment;
- a later graph-authorized mutation creates a new candidate generation and requires fresh candidate-specific assurance before acceptance;
- no independent candidate seal/hash/manifest, racing observer/callback, or processor-supplied Contract/source/evidence/predecessor identifier is required to establish candidate applicability;
- processor-supplied applicability identifiers are non-authoritative;
- **evidence sufficiency is separate** from candidate applicability and missing/wrong/contradictory required evidence remains blocking;
- completed-run recovery/catch-up, failed-run occurrence scanning and cross-Attempt semantic reuse remain deferred; V1 abandons and restarts from B1 instead; and
- V1 executes **no authoritative external effects**, including optional effects. The required-effect set is empty rather than waived.

## 2. Compatible identified qualification profile

The witnesses are compatible as one narrowed V1 profile, with controlled substitutions explicitly limited to the mechanics they test.

| Dimension | Compatible reviewed configuration |
|---|---|
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995`, tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d` |
| SDK | `zeroshot-rust 0.1.0.dev0`; wheel SHA-256 `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| Sidecar | `zeroshot-rust 0.1.0`; SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Host/runtime family | Official Python SDK `LocalTarget` → matching Rust sidecar; Linux x86-64 / Python 3.13.5 in the retained records |
| Candidate host policy | Issue #8's qualified single-host profile: one current Attempt exclusively owns one dedicated non-temporary disposable worktree; same-repository worktrees may share Git common metadata only under the qualified containment controls |
| Role access | Sidecar-selected `workspace-write` for implement/repair mutations; `read-only` for evidence/review/adjudication/final-assessment/control occurrences |
| Sessions/context | `sessionScope=execution`; real reviewer additionally uses `--ephemeral`, isolated `HOME`, authentication-only isolated `CODEX_HOME`, `--ignore-user-config`, `--ignore-rules`, no extra writable roots, and forced-off sandbox network |
| Real provider evidence | `codex-cli 0.153.4`, OpenAI `gpt-5.6-sol`, low effort for issue-#8 real containment probes and issue-#10 W4 reviewer controls |
| Controlled leaves | W3 and W6 use controlled Codex-protocol leaves only for deterministic graph/fault/final-boundary mechanics. They are not evidence for broad model judgment, reviewer context isolation, or hostile-host containment; those properties are separately sourced from W4/W2. |
| Graph | W6 uses issue #9's exact canonical graph SHA-256 `f3ffcfced5bab598bc818db65ed985637afa0696a4ff551ee96ed5807788cd2b`; W4 changes only reviewer timeout from the deterministic 250 ms bound to a finite 300,000 ms real-provider bound. Topology, types, bindings, roles, attempts and routing remain unchanged. |
| Effects | Required effects `[]`; no `GitDelivery`, publication or GitHub witness mutation |
| Retention | Normal completion retains exact final candidate material, comparison base, required raw evidence, criterion-level rationale, Contract/run applicability and final occurrence; this is custody, not a candidate-applicability seal. |

### 2.1 Configuration-difference review

Issue #9 and #10 create per-case fixture repositories rather than reproducing issue #8's same-repository/shared-common-Git worktree matrix. This is **fixture scaffolding, not a different advertised worktree policy**. W3/W6 do not use that weaker repository separation to establish sibling/shared-Git containment; issue #8 remains the sole evidence for those host facts. The W3/W6 graph logic is path-local and does not depend on private `.git` layout, while their worker kinds, sidecar sandbox selections, session scope, build and no-effect assumptions match the issue-#8 profile. Therefore this fixture-layout difference does not invalidate W1/W2/W5/W7 and does not require a rerun.

The only deliberate W4 graph/configuration delta is the reviewer timeout needed for an actual provider execution. It does not change authority roles, graph ordering, bindings, sandbox mode, session policy, writable roots, source policy or effect surface. W4's isolated authentication-only `CODEX_HOME` supplies OpenAI provider authentication, not GitHub mutation authority; the agent sandbox remains read-only with forced-off network and no extra writable roots. No W2/W5/W7 premise is reopened.

## 3. Individual witness review

### W1 — admission correlation and immutable original state — **PASS**

**Immutable evidence:** [report](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue-8-w1-w2-w5-w7.md), [machine record](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/evidence/issue-8-run-record.json), [reproduction](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/README.md), [fixture](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue8_qualify.py).

Expected: persist immutable B1 content/instructions, Attempt/worktree ownership and stable submission identity before submission; recover one run after caller-visible acknowledgement loss; reject conflicting request/source without a competing run; rematerialize B1 despite later live-HEAD drift.

Actual: unchanged replay recovered the single run `01a078be-1487-73d2-8f73-1a25edb6b855`; conflicting request and changed-source submissions created no run; a fresh worktree after HEAD drift reproduced admitted B1 bytes and instructions. The result is limited to the tested caller-process acknowledgement-loss window and pinned source/materialization policy.

No #9/#10 change alters admission correlation, submission identity or B1 rematerialization. **Current v0.5 verdict: PASS.**

### W2 — exclusive worktree containment and structural applicability substrate — **PASS under v0.5**

**Immutable evidence:** the same issue-#8 [report](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue-8-w1-w2-w5-w7.md), [machine record](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/evidence/issue-8-run-record.json) and [reproduction](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/README.md), with graph compatibility checked against W3/W6 below.

Expected under v0.5: distinct exclusive same-repository worktrees; only graph-authorized mutators may alter candidate source; assurance roles are read-only; sibling/shared paths and shared Git metadata remain contained; lingering/background writers cease before stable assurance; candidate-source/environment classification is explicit enough for the narrowed profile.

Actual issue-#8 controls established those host facts on the final narrow profile. Reviewer/adjudicator/final-assessor real Codex probes remained read-only; the authorized mutator changed only its own candidate; sibling/shared-path and shared-Git mutations did not persist; the delayed writer was gone after provider completion; C1→repair→C2 renewed review; intentional untracked/relevant generated material was distinguished from environment-only state.

The preserved report still says **W2 FAIL** against the older v0.4 checklist. That historical failure is not relabeled. Its three old failing areas are disposed under v0.5 as follows:

- missing raw evidence is an evidence-sufficiency obligation, now positively/rejectingly qualified by W3/W6;
- forged processor IDs are non-authoritative and W3/W6 show they cannot retarget trusted graph/Contract/candidate/evidence/predecessor state; and
- the delayed external observer requirement is removed from V1 and no witness depends on such an observer.

The result is bounded to the qualified single host and does not certify distributed/shared-writer or hostile-host profiles. **Current v0.5 verdict: PASS.**

### W3 — actual V1 assurance graph and encoding — **PASS**

**Immutable evidence:** [report](https://github.com/faviann/broodling/blob/6c5ab629d5f45a38e3913d706257cfc79644f3cc/qualification/v1-p1/issue-9-w3.md), [machine record](https://github.com/faviann/broodling/blob/6c5ab629d5f45a38e3913d706257cfc79644f3cc/qualification/v1-p1/evidence/issue-9-run-record.json), [reproduction](https://github.com/faviann/broodling/blob/6c5ab629d5f45a38e3913d706257cfc79644f3cc/qualification/v1-p1/README.md), [harness](https://github.com/faviann/broodling/blob/6c5ab629d5f45a38e3913d706257cfc79644f3cc/qualification/v1-p1/issue9_qualify.py), [evidence assertions](https://github.com/faviann/broodling/blob/6c5ab629d5f45a38e3913d706257cfc79644f3cc/qualification/v1-p1/tests/test_issue9.py).

Expected: qualify the actual admitted graph/encoding through the real SDK/sidecar; graph-authorized mutation must create a new structural candidate generation and force renewed evidence/review/authority/final assessment; evidence gaps and unusable results fail closed; directives remain sticky until eligible resolution; ordinary/lookalike outputs cannot gain authority; repair receives only admitted directives; finite exhaustion is non-success; final assessment is distinct from clean review/adjudication.

Actual: the 20-case matrix passed. The C1→repair→C2 path executed fresh C2 evidence, review, resolution and final assessment in order. Missing evidence failed as `required_evidence_missing`; crash/timeout/malformed/missing output failed as `execution_unusable`; refusal produced `authority_gap`; unresolved directives exhausted to `obligations_exhausted`; semantic insufficiency produced `semantic_gap`. Contradictory clean-looking diagnostics could not bypass the canonical authority signal. Forged diagnostics did not retarget the frozen Contract. Repair input was exactly Contract + structural candidate generation + admitted directive. The graph uses only structural generation ordinals, not a seal/hash/manifest/observer.

Controlled leaves establish deterministic integration/routing mechanics, not broad model semantic quality. **Current v0.5 verdict: PASS.**

### W4 — controlled independent reviewer — **PASS**

**Immutable evidence:** [report](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/issue-10-w4-w6.md), [machine record](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/evidence/issue-10-run-record.json), [reproduction](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/README.md), [harness](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/issue10_qualify.py), [mixed real/controlled launcher](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/issue10-bin/codex), [evidence assertions](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/tests/test_issue10.py).

Expected: actual selected fresh reviewer through the real SDK/sidecar; finite automatic-context control; forbidden prior-role/ambient material absent in clean review and detectable in a contaminated control; valid source/comparison/evidence remains visible; actual read-only candidate access; candidate governing text remains assessment data under the frozen Contract.

Actual: two real `gpt-5.6-sol` reviewer occurrences used distinct execution-scoped ephemeral threads and read-only sandboxing. The clean reviewer saw all allowed source/comparison/raw-evidence/candidate-text canaries and no forbidden prior-role/ambient canary; the deliberate contamination control surfaced all six forbidden canaries. The candidate remained unchanged, and instruction-like candidate text was not treated as new authority.

This is a finite two-occurrence control on the recorded host/model/profile, not exhaustive hostile-environment provenance or a broad model-reliability result. **Current v0.5 verdict: PASS.**

### W5 — stop/loss, abandonment and clean restart — **PASS**

**Immutable evidence:** issue #8 [report](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue-8-w1-w2-w5-w7.md), [machine record](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/evidence/issue-8-run-record.json) and [reproduction](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/README.md); W6 interruption compatibility is additionally recorded in issue #10.

Expected: stop/loss/caller windows make the old Attempt ineligible; safe cessation/terminalization precedes retirement/replacement; replacement gets a new Attempt/run/worktree from B1 with no candidate/semantic/evidence/acceptance/session carryover; late old results cannot resurrect eligibility.

Actual: stop during adjudication/mutation produced durable `force_stopped`; controller `SIGKILL` produced `runtime_lost`; caller interruption after directive and after final output was abandoned; replacement run `01a078c0-1185-7163-a5ee-285b5ee1c7ab` started from B1 with no observed semantic/session carryover and no surviving old provider process. Issue #10's post-final-output interruption likewise abandons without result scan, salvage or semantic catch-up.

This is **abandon-and-restart, not completed-run recovery/catch-up**. W3/W4/W6 all retain execution-scoped sessions and introduce no cross-Attempt reuse. **Current v0.5 verdict: PASS.**

### W6 — normal final result, structural currentness, evidence sufficiency and custody — **PASS**

**Immutable evidence:** issue #10 [report](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/issue-10-w4-w6.md), [machine record](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/evidence/issue-10-run-record.json), [reproduction](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/README.md), [harness](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/issue10_qualify.py), [evidence assertions](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/tests/test_issue10.py).

Expected: normal non-abandoned public completion exposes the actual designated final-assessment occurrence; final currentness follows graph order after the latest mutation; C2 requires fresh C2 assurance; lookalikes/defaults/gaps/unresolved obligations/bare runtime success cannot substitute; missing/wrong/contradictory evidence fails; exact final material/evidence/rationale survives cleanup; interruption yields complete durable disposition or abandonment; effects remain empty.

Actual: public run `01a07bef-eb8a-7ed0-a4df-4999f0782d63` completed C1→repair→C2. Live public `RunStatus` correlated designated final execution `nv2-975b8129c4fb2118e8154862cd5f8bfd71329db043eb2896d47e18aa87c12b30` before `RunResult` acceptance; no mutation followed it. Missing raw evidence failed, wrong population/host/mode/artifact and unexplained contradiction produced `semantic_gap`, unresolved obligations exhausted, missing/default final output and ordinary authority lookalikes failed closed, and forged diagnostic IDs could not retarget the frozen Contract or C2.

Valid completion atomically retained exact final candidate bytes/material, comparison base, raw evidence, criterion-level rationale, Contract/run applicability and final occurrence before cleanup. Missing rationale blocked Work Unit completion. Interruption after final runtime output abandoned the Attempt without scanning completed history or salvaging semantic state. Required effects were explicitly `[]`.

The final-assessor/control leaves establish deterministic final-boundary mechanics, not general semantic reliability. **Current v0.5 verdict: PASS.**

### W7 — no-effect admission and execution boundary — **PASS**

**Immutable evidence:** issue #8 [report](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue-8-w1-w2-w5-w7.md), [machine record](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/evidence/issue-8-run-record.json) and [reproduction](https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/README.md), plus the empty-effect W3/W6 configurations.

Expected: admit genuinely empty-effect Contracts; reject required Git/GitHub/publication/effect-dependent evidence without deleting obligations; exclude `GitDelivery`; ambient Git/`gh`/workspace visibility must not become external effect authority; runtime success still requires actual applicable final acceptance.

Actual: empty-effect admission and no-effect completion controls passed; required commit/push/PR/merge/issue/publication/effect-dependent obligations were rejected; bare runtime success with a final semantic gap did not become completion. In the real-provider bypass probe, shared Git config/tag/direct-push attempts did not persist, the sibling bare remote remained unchanged, no readable GitHub hosts file existed and sandbox network was forced off. No GitHub witness mutation occurred.

W3/W6 keep required effects empty and introduce no `GitDelivery`, network/writable-root expansion, publication or effect observer. **Current v0.5 verdict: PASS.**

## 4. Cross-witness agreement and gate criteria

| G1-V1 review condition | Review result |
|---|---|
| W1/W2/W5/W7 remain valid on the final compatible profile | **PASS.** No later qualification changed an assumption requiring rerun. |
| W3 actual graph/freshness/evidence/sticky-authority obligations | **PASS.** Issue #9 records W3 PASS on the compatible build/profile. |
| W4 actual controlled independent reviewer | **PASS.** Issue #10 records W4 PASS with the real selected reviewer. |
| W6 final occurrence/current candidate/evidence/custody | **PASS.** Issue #10 records W6 PASS through the normal public result path. |
| Fresh assurance after mutation | **PASS.** W3 and W6 both exercise C1→repair→C2 with fresh C2 evidence/review/authority/final assessment. |
| W4 preserves W2 containment | **PASS.** Read-only real reviewer, no extra writable roots, controlled context and no GitHub mutation path. |
| W5 zero carryover remains intact | **PASS.** Later witnesses use execution-scoped sessions; W6 interruption abandons without salvage/catch-up. |
| W7 no-effect boundary remains intact | **PASS.** Required effects stay empty; no `GitDelivery` or publication is introduced. |
| Runtime/graph authority, not self-attested IDs | **PASS.** W3/W6 forged diagnostics cannot retarget trusted Contract/candidate/evidence/predecessor state. |
| Evidence sufficiency kept separate from candidate applicability | **PASS.** Structural generation/currentness is graph/worktree based; W3/W6 independently reject missing/wrong/contradictory evidence. |
| No second scheduler/router/response validator/session manager | **PASS.** Zeroshot owns typed validation/routing/sessions; issue #9 records no Broodling validator/router. |
| No private completed-history substitute/cross-Attempt recovery projection | **PASS.** W6 uses normal public live `RunStatus` + `RunResult`; interrupted completion abandons. |
| No duplicate semantic blessing | **PASS.** Designated authority occurrences drive the admitted graph directly; no second payload-admission hop exists. |
| No generic candidate-sealing/provenance subsystem | **PASS.** Structural generation ordinals and local evidence/custody only; no seal/hash/manifest/observer dependency. |

## 5. Historical provenance remains unchanged

This PASS is a **new v0.5 G1-V1 verdict**. It does not rewrite earlier evidence or gates.

| Historical item | Preserved result |
|---|---|
| P0/G0 baseline | **PASS — specification completeness only.** |
| G0-v0.4 | **PASS — documentation completeness only.** |
| Q1 | **BLOCKED** — supported exact settled-occurrence recovery absent under its v0.3 scope. |
| Q2 | **PASS**, bounded to its caller-process acknowledgement-loss/replay/conflict witness. |
| Q3 | **PASS**, bounded to its historical canonical-signal/one-directive/three-round fixture. |
| Q4 | **BLOCKED** under the historical trusted-applicability checklist. |
| Q5 | **BLOCKED** under the historical ambient-context qualification. |
| Q6 | **BLOCKED** under the historical completed-authority catch-up requirement. |
| Q7 / G1-effects | **BLOCKED**; exact effects/reconciliation remain deferred outside V1. |
| G1-core / issue #7 | Review **COMPLETE**, gate **BLOCKED** under v0.3. |
| Issue #8 historical v0.4 report | **W1 PASS; W2 FAIL; W5 PASS; W7 PASS.** The report remains unchanged. |

The v0.5 current W2 PASS is a distinct governing-scope disposition over the retained host-containment evidence; it is not a retrospective edit of issue #8's v0.4 W2 FAIL.

## 6. Gate verdict, limits and stop boundary

**G1-V1 PASS.** All current W1–W7 obligations are supported by compatible evidence for the narrowed V1 profile.

This gate qualifies only the evidence needed to proceed beyond V1-P1. It does **not** claim or perform:

- V1-P2 or any product implementation;
- broad reviewer/adjudicator/final-assessor semantic reliability or release qualification;
- completed-run recovery, semantic catch-up, failed-run occurrence scanning, salvage, cross-Attempt approval/evidence/candidate/session reuse or same-run takeover;
- candidate applicability for shared/concurrent/external writers, distributed execution or hostile hosts;
- a generic candidate seal/hash/manifest/provenance/archive service;
- authoritative Git/GitHub effects, `GitDelivery`, exact effect receipts, lost-acknowledgement effect reconciliation, publication, PR/merge/issue mutation or deployment; or
- a second scheduler, graph reducer/router, generic response validator, session manager or parallel runtime-result authority.

The gate removes **G1-V1** as the dependency blocker for later V1 work, but this review does not start that work and creates no later implementation issue.
