# Broodling implementation and dependency plan

**Version:** 0.4  
**Date:** 6 September 2026  
**Governing target:** [Target responsibility and boundary design v0.4](broodling-target-responsibility-boundary-design-v0.4.md).  
**Status:** Complete replacement plan for the deliberate simplified V1 profile. New execution witnesses are **NOT RUN**; G1-V1 is not passed.  
**Supersedes:** v0.3 planning sequence in full. The unchanged v0.3 baseline, P0/G0 inventory, P1 reports/records and historical gate verdicts remain provenance.

## 1. Basis and implementation direction

**Qualify the narrowed profile before implementing the product.** V1 is single-host, with one Work Unit exclusively owning one dedicated disposable worktree for its current Attempt. Other Work Units use different worktrees. Zeroshot orders graph-authorized mutations; assurance readers are read-only relative to candidate source. Incomplete/stopped/lost Attempts and their worktrees are abandoned; a replacement starts from original admission without reusing abandoned candidate, decisions/directives, evidence or acceptance. Only Contracts with no required authoritative external effects are supported, and no GitDelivery is used.

This is a capability reduction, not a claim that the old G1-core blockers were fixed. The old gate remains BLOCKED under v0.3. The new target removes completed-occurrence catch-up and stronger source-sealing requirements from V1's critical path, narrows context qualification, and excludes effects. It still requires new witnesses for the reduced boundary. Do not bypass those witnesses with a Broodling scheduler, response validator, session manager, history reader or sealing subsystem.

### 1.1 Authority, evidence and inspected baseline

Authority order for the revision was current target v0.3, current plan v0.3, then P0/G0 and completed P1 evidence from issues #3–#7, with the project owner's explicit v0.4 simplification governing the changed scope. The new v0.4 target now governs this plan.

| Input | Recorded basis |
|---|---|
| Broodling current baseline read | `a11f2bb085b565234287a7d753ff740a4f9d4285`; documentation and qualification code/evidence, not a completed product. |
| v0.3 provenance | `docs/baseline/inputs/` target and plan; hashes and blob identities in the companion target. Preserve these files and `docs/baseline/SHA256SUMS` unchanged. |
| P0/G0 | [Inventory][B0] at `5f017e39dfa6a84a4c4f27da90e70174e0817cd6`: specification-completeness PASS; I01–I25, H01–H04, R1–R19 and synthetic WS-01. |
| P1 tested upstream | Clean/unpatched Zeroshot `d0909615d6ba3c179b58bce15a059f40400ec995`, tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`; official async Python SDK → matching Rust sidecar → local one-run controller. |
| Current upstream source read | `c5c17626674a53a0ae7e4584654480c2b1be2479`, one successor commit. Comparison concerns target-admission feedback; relevant run-model/local-context/delivery files are not changed by it. No new build or qualification is inferred. [Z04] |

**Established finding** means supported by the read source or retained P1 reports. **Implementation selection** means a deliberately small proposed route. **Unresolved qualification question** requires a witness before its consumer proceeds. **Deferred capability/decision** is not a V1 implementation requirement. These categories must remain separate in later gate records.

This revision read governing inputs, P0/G0, issue requirements/completion records, P1 reports and the consolidated gate review. P1 execution claims below are attributed retained evidence; the revision did not rerun harnesses, rebuild binaries, recompute every machine-record digest, or perform a new real-provider experiment. Historical experiment material not recovered by P0 is not silently treated as available.

## 2. Existing P1 findings and their V1 disposition

### 2.1 Reuse does not change historical verdicts

| Existing question | Historical evidence/verdict | What remains useful | New V1 consequence |
|---|---|---|---|
| **Q1: completed occurrences** | **BLOCKED**, #3/#7. Public restart observation lacks exact settled input/outcome/order and admitted graph/runtime recovery. | Boundary of the public SDK and warning against private SQLite/log-derived authority. | Failed/stopped-run recovery is deferred. V1 does not need it for abandonment. Normal final-assessment export/provenance still needs **W6**; Q1 is not relabeled PASS. |
| **Q2: submission correlation** | **PASS within the witness**, #3. Caller process lost the acknowledgement; replay returned the same run. Changed HEAD caused `SubmissionConflictError.existing_run_id`; inventory showed no second run. | Stable-key replay and conflict reconciliation through the real transport. | Reuse as a bounded building block. **W1** must cover original-state/worktree admission and ambiguous submission in the actual V1 profile; no claim about every transport fault or a production store. |
| **Q3: fail-closed graph** | **PASS for the fixture**, #4/#7. Canonical enum signal also writes typed state; one directive, three repair rounds, null adjudication payload. | Explicit unusable routes, sticky directive, direct authority routing, directive-only repair, separate final assessment and RED/empty controls. | Reuse mechanism/counterexamples, not an arbitrary production schema. **W3** qualifies the actual V1 encoding, including any richer directive payload or state. |
| **Q4: applicability** | **BLOCKED**, #5. Stale starting digest, forged IDs and missing raw artifact reached fixture acceptance; graph outran delayed observer. | Negative evidence against claimed hashes, model self-attestation and asynchronous gating; starting HEAD is not current candidate identity. | General trusted sealing handoff removed from V1 requirements. **W2/W6** must establish exclusive ownership, graph-ordered candidate transitions, same-candidate evidence and minimal final custody. The negative cases still matter. |
| **Q5: reviewer context** | **BLOCKED**, #5. Real fresh Codex reviewer reproduced repository/skill canaries outside graph JSON. | Execution-scoped freshness and the limited private-narrative `NOT_VISIBLE` control; actual ambient contamination counterexample. | Exhaustive hostile-environment provenance not required. **W4** must positively qualify a narrow controlled real-provider profile; freshness or read-only access alone is insufficient. |
| **Q6: stop/loss/catch-up** | **BLOCKED**, #3/#7. Force-stop and killed-controller runtime-loss terminalization were observed; exact pre-terminal semantic catch-up was unavailable. | Real stop/loss and crash-window mechanics, subject to original build/state-dir limits. | Catch-up is deferred, not repaired. **W5** tests wholesale abandonment, old-runtime isolation and fresh restart with zero semantic carryover. |
| **Q7: exact effects** | **BLOCKED**, #6. Stock PR/merge delivery created extra local commits and attempted fixed push; generic effect binding rejected. | Failure ordering and concrete evidence that delivery bundles do not fit narrow intents. | Exact effects/reconciliation deferred. **W7** excludes GitDelivery and effect-bearing Contracts/access from V1. No effect fixture is a V1 release prerequisite. |
| **G1-core review** | **BLOCKED**, #7; review COMPLETE. | Consolidated limitations and exact provenance. | Leave record and issue state unchanged. New **G1-V1** is a separate gate, currently NOT RUN and blocking dependent product work. |

Q3's injected applicability-failure case proves routing **after a failure is supplied**, not trusted detection of stale bytes. Its refusal witness was force-stop/forced-refusal terminalization, not an independently observed provider refusal. Controlled leaf captures do not prove real provider context or semantic quality. These limits carry into every reuse decision. [R3–R7]

### 2.2 Reproducibility and artifact limits

| Evidence | Immutable originating Broodling snapshot | Reproduction and retained observations |
|---|---|---|
| Shared harness #2 | `500fdb401edd83b36d791d02074920e607a5c98d` | [Instructions][H2]; [build/run record][E2]. |
| #3, Q1/Q2/Q6 | `f144e15b15bd85cd788aa21a1c17d973914cb426` | [Report][R3]; [machine record][E3]. |
| #4, Q3 | `0b1a3b24d25d1467057593ca614b09a0cbce9766` | [Report][R4]; [machine record][E4]. |
| #5, Q4/Q5 | `03bf963eb7d3c59ffa52c4aee388f1470f444f73` | [Report][R5]; [machine record][E5]. |
| #6, Q7 | `3a486f270f5fdc1f029d6f255c1ede62b9378f03` | [Report][R6]; [machine record][E6]. |
| #7, consolidated review | `a11f2bb085b565234287a7d753ff740a4f9d4285` | [Gate review][R7], including immutable fixture links and limitations. |

All reports identify the same upstream revision, SDK distribution `zeroshot-rust 0.1.0.dev0` and sidecar SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`. However, #3/#4 record wheel hash `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`, whereas #2/#5/#6 record `153d19c2ea1b5f6a58cd06a20d6c4003071ced0669c3155a94b476a3bb2dd9ae`. Their equivalence is not established. The controlled `bin/codex` also evolved between originating commits. Same filename or source SHA does not make different artifacts the original tested fixture. [R7, R6]

Q5 used real `codex-cli 0.153.4` / OpenAI `gpt-5.6-sol`, low effort, `sessionScope: execution`. Most mechanics witnesses controlled only the provider leaf while retaining real SDK/sidecar/runtime behavior. Preserve that distinction. Runtime replay was conditioned on preserving readable `LocalTarget.state_dir` under the pinned build; no general retention SLA or cross-version compatibility was qualified.

For G1-V1, identify one compatible chosen build/profile, exact graph/runtime/input, provider configuration, instrumentation and retained observations. Reuse old evidence only for unchanged, explicitly bounded claims. Rerun affected witnesses when the build, controls, graph, source policy or profile differs; do not assemble an asserted capability from incompatible positives.

## 3. Minimal implementation selections and scope cuts

**S1 — External-first qualification.** Extend or reuse the official Python SDK/sidecar feasibility harness, using a custom Broodling graph. This is not a product-language choice. No embedding or private ledger reader is selected.

**S2 — Ownership rather than a general sealing system.** Qualify dedicated worktrees, graph-ordered mutators, read-only assurance intervals and explicit same-Attempt candidate/evidence bindings. Keep only enough starting/final materialization and evidence retention to support restart and explain completion. The representation is not selected here.

**S3 — Abandon, do not recover.** Persist minimal admission/currentness/abandonment facts; use Zeroshot stop/loss mechanics; restart from original admitted state. No completed-occurrence scanner, semantic catch-up projection, cross-Attempt directive migration, candidate cache or session restoration.

**S4 — No-effect completion.** Admit only empty-required-effect Contracts. Execute no authoritative effects, including optional ones; exclude GitDelivery. A product result can be a justified retained local candidate without commit/push/PR/issue closeout.

| v0.3 requirement/phase | Removed from V1's implementation path | What is deferred versus mandatory |
|---|---|---|
| P1 Q1/Q6; P4 catch-up before replacement | Recovery and incorporation of every pre-terminal decision | Cross-Attempt reuse/recovery deferred; correlation, safe stop/loss and zero-carryover replacement mandatory. |
| P1 Q4; broad P3 custody/handoff | Stronger mutable-workspace handoff/custody qualification as an initial prerequisite | Stronger profiles deferred; C1/C2 separation, available evidence and final-result retention mandatory. |
| P1 Q5 exhaustive boundary | Exhaustive automatic-context provenance for the unconstrained ambient profile as an initial prerequisite | Stronger assurance deferred; narrow actual reviewer freshness/context controls mandatory. |
| P1 G1-effects; P6 effect suite | Exact-effect integration and reconciliation as initial prerequisites | Entire effect capability deferred; no-effect admission/access boundary mandatory. |
| P7 depending on P6 | Delivery readiness on V1 release path | V1 semantic evaluation and justified disposition mandatory; future effect readiness separately gated. |

No omitted recovery mechanism is to be rebuilt in admission, source custody or disposition. An unsupported narrowed requirement remains an explicit integration/profile dependency, not an invitation to broaden Broodling.

## 4. Regenerated dependency sequence and gates

```text
V1-P0  Rebaseline target/invariant applicability
  │ G0-v0.4: documentation traceability
  ▼
V1-P1  Qualify narrowed integration/profile, W1–W7
  │ G1-V1: BLOCKING; new witnesses NOT RUN
  ▼
V1-P2  Minimal admission, original state and worktree ownership
  │ G2-V1
  ▼
V1-P3  Assurance graph, candidate/evidence and controlled review
  │ G3-V1
  ▼
V1-P4  Abandon/restart and normal no-effect disposition
  │ G4-V1
  ▼
V1-P5  Product boundary, semantic evaluation and release
    G5-V1

Later capability work (not on V1's dependency path):
  cross-Attempt reuse/recovery; stronger applicability profiles;
  exact effects/reconciliation; distributed/hostile-environment profiles
```

Phases are dependency boundaries, not modules or calendar estimates. Fixture preparation may proceed independently; a consuming phase cannot pass using an unproved substitute. No product implementation or new implementation issues are part of this documentation revision.

### V1-P0 — Rebaseline without rewriting history

**Depends on:** the read v0.3 baseline, P0/G0 and P1 evidence, and the corrected v0.4 target.

Preserve all old evidence. Use §5's traceability dispositions and the companion target's R1–R19 map. Record the new scope, original-state/no-carryover rules and independent-review/no-effect requirements. Mark each future witness as unexecuted until evidence exists.

**G0-v0.4:** every old invariant/supplement has a mandatory, rescoped or deferred disposition; every new V1 assumption has a witness/consumer; historical G1-core/G1-effects remain BLOCKED rather than rewritten. This gate is documentation completeness only. This pair delivers the new inventory; it supplies no runtime gate pass.

### V1-P1 — Early narrowed qualification

**Depends on:** G0-v0.4. **Before:** product execution depends on the selected integration.

Use the real SDK/sidecar and selected host profile. Controlled leaves can expose mechanics; W4 requires the actual reviewer provider. Record expected/actual outcomes, rejecting cases and valid controls. No witness below is asserted passed by this revision.

| Witness | Required new demonstration and control | Reused building block / consumers |
|---|---|---|
| **W1 — Admission correlation and original state** | Freeze original starting content/instructions, admit one Attempt/worktree and lose the submit acknowledgement. Replay/reconcile to the same run; conflicting source/request must not create competing authority. Change live repository HEAD and prove later fresh materialization still uses the original admitted state. Unknown run identity blocks replacement. | #3 Q2, bounded as recorded. G2-V1, W5. |
| **W2 — Exclusive ownership and candidate applicability** | Run two Work Units in different worktrees, including the same target repository where supported. Only graph-authorized mutators can change their respective candidates. Exercise read-only reviewer/assessor writes, out-of-order or lingering writes and shared-path/Git-metadata interference. Prove C1 evidence/approval cannot establish C2 after repair; forged Contract/source/evidence/predecessor output cannot retarget trusted bindings. Remove a required raw artifact: no justified acceptance. An unchanged candidate with valid evidence is the positive control. Show graph progression does not depend on a racing external observer. | #5 negative cases are regressions, not a passing mechanism. G2-V1/G3-V1/G4-V1. No general sealing service required. |
| **W3 — Actual V1 assurance protocol** | Exercise unusable outcomes, contradictory clean decisions, authority impersonation, missing resolution, raw/rejected finding isolation, bounded exhaustion and separate final assessment in the intended V1 graph/encoding. Valid empty-review, observed RED and explicit eligible resolution controls must succeed appropriately. A retained open directive survives source repair and fresh findings until authority explicitly resolves/supersedes it. | #4 canonical-state mechanism and matrix. Any richer directive encoding is new qualification, not covered by the one-directive fixture. G3-V1. |
| **W4 — Narrow independent review** | Real reviewer uses fresh execution/session and a documented controlled configuration. Put prior-role, prior-review, adjudication/repair and user-level/ambient instruction/skill canaries in the contamination paths addressed by that configuration. They must not automatically enter clean review. Include a deliberately contaminated control proving detection and a valid-source/raw-evidence control. Qualify read-only candidate access and candidate governing text as data, not live authority. Record finite controls/versions/limits, not an exhaustive hostile-environment transcript. | #5 freshness observation and real context counterexample. No mock-only or prompt-only pass. G3-V1/G5-V1. |
| **W5 — Stop/loss, abandonment and clean restart** | Stop during adjudication and during mutation; separately kill the controller and interrupt the Broodling-side caller after a directive or final output but before disposition. Observe supported cessation/loss handling and retire the worktree. A new Attempt/run/worktree starts from original admission, with none of the old candidate, decisions/directives, evidence, acceptance or automatic context. Late old results cannot reverse abandonment; surviving execution cannot alter replacement/other Work Units. Unknown cessation/interference blocks progression. Repeat restart/stop requests to test durable identity/currentness without semantic history replay. | #3 stop/runtime-loss/crash-window mechanics; no Q1/Q6 catch-up claim. G4-V1. |
| **W6 — Normal final-result provenance and retention** | Through the selected public integration, complete one non-abandoned run and obtain the actual designated final assessment for its current candidate. Qualify explicit admitted final-output bindings and unambiguous occurrence provenance; reject ordinary/forged outputs, stale candidate results, semantic gaps and bare runtime success. Retain the candidate and required raw observations/rationale through disposable cleanup. Missing material blocks completion. Interrupt finalization: either an already durable complete disposition is readable or the Attempt is abandoned, never partial-history salvage. | SDK `RunResult` is a possible normal export carrier, not a completed-history API. #4 supplies structural final-assessor separation only. G3-V1/G4-V1. |
| **W7 — No-effect admission and execution boundary** | Admit a genuinely no-effect Contract; reject commit/publication/PR/merge/issue-effect requirements and effect-dependent evidence. Do not silently strip them. No GitDelivery binding, agent credential/tool path or shared repository mutation may bypass the boundary; optional permissions do not cause effects to execute. Use controlled rejecting probes, not real unauthorized GitHub mutation. Verify no-effect success requires actual final acceptance. | #6 bundling counterexample; #4 no-effect graph controls. G2-V1/G4-V1/G5-V1. |

**G1-V1:** all remaining V1 integration obligations above are demonstrated on one compatible identified profile with recorded limits and controls. Existing narrow positives may be cited where unchanged; all changed/new assumptions need actual witnesses. Current status: **NOT RUN / NOT PASSED; product progression blocked pending qualification.** Old G1-core remains BLOCKED under its original scope, and G1-effects stays deferred and unqualified.

A failed witness must name the smallest missing runtime/profile contract and its consuming gate. For example, inability to make assurance readers read-only is a W2/W4 profile blocker; inability to export an unambiguously designated final result is a W6 normal-completion blocker. Neither licenses a generic Broodling scheduler, session manager, completed-history substitute or large seal store. Qualification may change a narrow configuration or require a supported integration change; this plan does not assume one exists.

### V1-P2 — Admission and the durable semantic nucleus

**Depends on:** G1-V1, especially W1/W2/W7.

Implement Work Unit resolution, entitlement, frozen source-attributed Contracts and Closability. Enforce the empty required-effect set and the actual qualified profile limits. Persist admission before submission, original starting-state materialization information, exclusive worktree/current-Attempt relationships, stable submission identity and run correlation.

Select storage only when implementing these proven requirements. Keep administrative lineage and currentness separate from derived Attempt-local graph state. Do not mirror RunLedger or build abandoned-result migration. Candidate selection must account for intentional source and relevant generated/tracked content without prescribing a universal manifest format.

**G2-V1:** duplicate ingress/acknowledgement loss preserves one authority/run relationship; live edits cannot amend the Contract or starting state; unsupported effects/context/workspace profiles fail admission; two units cannot own the same worktree; crashes around admission do not authorize a competing Attempt. New meaning requires external revision. General retry exposure waits for G4-V1.

### V1-P3 — Graph-local assurance, applicability and controlled review

**Depends on:** G2-V1 and W2/W3/W4/W6.

Implement the short implement → review → adjudicate/repair → final-assessment graph. Encode mutator/read-only role boundaries and candidate transitions in the qualified profile. Only designated authority changes obligation state; repair receives applicable directives. Keep sticky obligations within the graph and within the Attempt, with explicit resolution/supersession and bounded exhaustion.

Integrate the narrow controlled reviewer configuration. Track only the same-Attempt candidate/evidence relationships needed for stable assurance intervals and conservative invalidation. Make missing relevant raw material and unexplained contradictory observations block justified acceptance. Starting HEAD, model IDs and worktree path alone are insufficient. Preserve final candidate/evidence material using the minimal retention route established by W6.

**G3-V1:** run the intended graph's negative/valid controls for read-only assurance, C1→C2 invalidation, forged bindings, open directives, raw-finding isolation, unavailable/wrong-population evidence and clean-review/semantic-gap separation. Demonstrate actual controlled reviewer input. A delayed observer is neither a graph dependency nor the source of candidate authority. No cross-Attempt projection or general source/evidence sealing subsystem is required to pass.

### V1-P4 — Abandon-and-restart plus normal disposition

**Depends on:** G2-V1/G3-V1 and W5/W6/W7.

Implement the target's replacement order with minimal durable administrative state: make abandoned authority ineligible, request/observe Zeroshot termination, establish safe isolation, retire the old worktree and admit a fresh Attempt/run from original admission. Do not read and incorporate old decisions. Do not reuse candidate bytes, summaries, caches, evidence, directives or acceptance. A failure reason may remain diagnostic history but cannot become automatic replacement context.

Implement the normal final-result boundary proven by W6. Current final semantic acceptance, the exact retained candidate and required justification establish the semantic term; the required-effect set is explicitly empty. Broodling records disposition, not a second semantic blessing of each authority output. An ambiguous interrupted finalization is non-success/abandonment unless a complete durable disposition is already established; no partial semantic catch-up is introduced.

**G4-V1:** integrate stop/mutation/authority/completion races and crash points. Prove original-state rematerialization despite live HEAD drift, no late resurrection or carryover, no orphan interference, and idempotent administrative operations. Demonstrate a real-provider no-effect vertical slice, including an actual repair path, and retention of its justified final result through disposable cleanup. Runtime `succeeded=true` without applicable final assessment cannot establish Work Unit success. This gate does not certify broad semantic reliability.

### V1-P5 — Product boundary, semantic evaluation and release

**Depends on:** G2-V1 through G4-V1. **Does not depend on:** effect/recovery capability implementation.

Expose submission, observation, external revision, stop, fresh retry and justified disposition. Reuse Zeroshot status/log/usage observation rather than a second telemetry authority. No backlog selection, dependency waiting, global budgeting or multi-project scheduling is added.

Evaluate reviewer sensitivity, adjudication legitimacy, final-assessor false acceptance, actual correction effectiveness, convergence and churn. Use wrong-stack/count-only counterexamples and valid controls; wrong host/mode/population/artifact; insufficient evidence despite clean review; same-Attempt contradictory failures; unjustified findings; required negative/correction checks; repairs preserving disproven premises; governing edits attempting self-authorization; bounded re-adjudication and scoped methodology. Set empirical thresholds before qualification runs, not after results. Deterministic scripted results do not establish model judgment quality.

**G5-V1:** every applicable row in §5 has evidence at the advertised profile; new qualification, admission, isolation, restart, final retention and semantic evaluation gates pass; strict disposition is reconstructible from retained final justification without failed-run recovery. Claims are limited to single-host, exclusive disposable worktrees, fresh controlled review and no authoritative effects. A synthetic WS-01 result must not be advertised as historical Experiment C reproduction. General language/framework or deployment ambitions do not widen the qualified capability set.

## 5. Baseline traceability and later capability gates

### 5.1 Complete P0 invariant/supplement disposition

The original inventory remains unchanged. “Rescoped” is a new witness obligation, not a retroactive alteration of its old expected result.

| P0 IDs | v0.4 disposition | V1 witness / consuming gate |
|---|---|---|
| I01 | Mandatory frozen authority | W1/W7; G2-V1/G5-V1. |
| I02, I03 | Mandatory designated authority/no blessing; no abandoned authority reuse | W3/W6; G3-V1/G4-V1. |
| I04, I05 | Old committed-authority carryover/catch-up deferred; replacement rescoped to abandon/zero reuse | W5; G4-V1. |
| I06 | External-effect conflict fencing deferred; local old-runtime/currentness safety remains | W5/W7; G2-V1/G4-V1. |
| I07, I08, I09 | Mandatory unusable routing, sticky within-Attempt obligations and repair firewall | W3/W4; G3-V1. |
| I10 | Rescoped to narrow controlled independent reviewer, not exhaustive hostile provenance | W4; G3-V1/G5-V1. |
| I11, I12 | Mandatory separate final assessment and evidence sensitivity | W3/W6; G3-V1/G5-V1. |
| I13 | Mandatory canonical provenance/no duplicate runtime truth; failed-history reconstruction not required | W6; G4-V1. |
| I14, I15 | Mandatory candidate/environment separation and C1/C2 protection, implemented by narrowed ownership/transition profile | W2/W6; G2-V1/G3-V1/G4-V1. |
| I16 | Mandatory contradictions within Attempt; wholesale abandonment never selectively retains favorable results | W2/W5/W6; G3-V1/G5-V1. |
| I17, I18 | Mandatory correction sufficiency and actual relevant available evidence | W2/W3/W6; G3-V1/G5-V1. |
| I19 | Mandatory frozen-authority governing comparison; effect prerequisite mechanics deferred | W3/W4; G3-V1/G5-V1. |
| I20 | Strict conjunction retained; required-effect set empty, not waived | W6/W7; G4-V1/G5-V1. |
| I21 | Effect completion never acceptance; effect-bearing execution tests deferred | W7 rejection and W6 actual acceptance; G2-V1/G4-V1. |
| I22 | Exact effect receipt/source/target correspondence deferred | Future effect gate; C1/C2 semantic protection still W2/W6. |
| I23 | Mandatory no authority from workspace access; future receipt/readback mechanics deferred | W7; G2-V1/G4-V1. |
| I24 | Exact-intent post-run reconciliation deferred | Future effect gate; no V1 reconciliation subsystem. |
| I25 | No universal publication mandatory; publication-dependent/prototype effect paths unsupported in V1 | W7; G2-V1/G4-V1. |
| H01 | Entitlement and finite Closability mandatory, with no-effect eligibility | W7; G2-V1/G5-V1. |
| H02 | One issue/repository/current Attempt and unique worktree mandatory | W1/W2/W5; G2-V1/G4-V1. |
| H03 | Scoped real repair/admitted methodology mandatory; no hidden continuity | W3/W4; G3-V1/G5-V1. |
| H04 | Bounded same-Attempt reconsideration and diagnostic handback mandatory; semantic carryover deferred | W3/W5; G3-V1/G4-V1/G5-V1. |
| New V1 ownership/restart/completion rules | Distinct worktrees, graph-only mutation, immutable original restart, zero reuse, controlled review and justified retained no-effect completion | W1–W7; G1-V1 through G5-V1 as above. |

D/S separation and the R1–R19 protections are preserved by the companion target §§9, 12–13. No legacy packaging, nineteen-module structure or extra specialist role follows from traceability.

### 5.2 Deferred capabilities and their future re-entry conditions

**Cross-Attempt reuse/recovery:** re-enter only with explicit capability authority and supported exact completed-occurrence, retention, currentness and idempotent catch-up witnesses, including failed/stopped runs and crash windows. Existing #3/#7 negatives remain evidence. Do not prebuild cursor/checkpoint stores or salvage channels for V1.

**Stronger candidate/applicability profiles:** shared/external/concurrent writers or broader reusable source/evidence schemes need new protection and trusted handoff witnesses. Preserve #5's stale/forged/missing-artifact/delayed-observer cases. Do not claim exclusive-worktree qualification establishes a general sealing solution.

**Exact authoritative effects:** before any effect-dependent Contract is admitted, qualify exact intent/source/payload/target/prerequisite consumption, no extra bundled mutations, same-run trusted result coordination where needed, verified readback, lost-acknowledgement reconciliation and conflict fencing. Preserve both effect-before-assessment and acceptance-before-effect controls; no universal PR/merge path. #6/G1-effects remains independently BLOCKED, not waived by V1. Choose operations and implementation only then.

**Distributed or stronger hostile-environment profiles:** separately establish takeover/fencing, workspace/process isolation and context/confinement guarantees. Single-host V1 is not evidence for them. These are not extra stages of the initial release sequence.

## 6. Decisions left unresolved and immediate next dependency

| Decision | Latest responsible point / fixed boundary |
|---|---|
| Product language/framework and external packaging | After narrowed integration is understood; Python harness and Rust upstream dictate neither. |
| Semantic storage technology/schema | V1-P2; durable admission/currentness/final disposition, not a mirror of RunLedger or catch-up store. |
| Starting/final materialization and candidate/reference representation | W1/W2/W6 feasibility, then V1-P2/P3; exact applicability and original restart are fixed, serialization/hash scheme is not. |
| Reviewer configuration, models/prompts/population | W4 and semantic evaluation; fresh controlled independent review is fixed, exhaustive hostile provenance and session management are not required. |
| Final-result export/custody format | W6 then V1-P3/P4; qualify provenance and needed retention, not completed-history recovery. |
| Effect implementation, supported operations, reconciliation storage | Deferred with effect capability; no V1 executor selection or GitDelivery adoption. |
| Distributed recovery, reuse/caching optimizations | Deferred with their capabilities; never necessary to make V1 correct. |
| Product interface/CLI/service, detailed non-success taxonomy | V1-P5; strict success and explicit abandonment/non-admission facts remain fixed. |

The immediate dependency is **G1-V1's narrowed qualification**, especially actual worktree/candidate control, controlled reviewer context, no-carryover stop/loss and normal final-result provenance. Completed-occurrence recovery is no longer V1's first implementation task. This plan introduces no Broodling/Zeroshot implementation, later implementation issues or calendar estimates.

## References

[T04]: broodling-target-responsibility-boundary-design-v0.4.md
[B0]: https://github.com/faviann/broodling/blob/5f017e39dfa6a84a4c4f27da90e70174e0817cd6/docs/baseline/p0-g0-inventory.md
[H2]: https://github.com/faviann/broodling/blob/500fdb401edd83b36d791d02074920e607a5c98d/qualification/p1/README.md
[E2]: https://github.com/faviann/broodling/blob/500fdb401edd83b36d791d02074920e607a5c98d/qualification/p1/evidence/run-record.json
[R3]: https://github.com/faviann/broodling/blob/f144e15b15bd85cd788aa21a1c17d973914cb426/qualification/p1/issue-3-q1-q2-q6.md
[E3]: https://github.com/faviann/broodling/blob/f144e15b15bd85cd788aa21a1c17d973914cb426/qualification/p1/evidence/issue-3-run-record.json
[R4]: https://github.com/faviann/broodling/blob/0b1a3b24d25d1467057593ca614b09a0cbce9766/qualification/p1/issue-4-q3.md
[E4]: https://github.com/faviann/broodling/blob/0b1a3b24d25d1467057593ca614b09a0cbce9766/qualification/p1/evidence/issue-4-run-record.json
[R5]: https://github.com/faviann/broodling/blob/03bf963eb7d3c59ffa52c4aee388f1470f444f73/qualification/p1/issue-5-q4-q5.md
[E5]: https://github.com/faviann/broodling/blob/03bf963eb7d3c59ffa52c4aee388f1470f444f73/qualification/p1/evidence/issue-5-run-record.json
[R6]: https://github.com/faviann/broodling/blob/3a486f270f5fdc1f029d6f255c1ede62b9378f03/qualification/p1/issue-6-q7.md
[E6]: https://github.com/faviann/broodling/blob/3a486f270f5fdc1f029d6f255c1ede62b9378f03/qualification/p1/evidence/issue-6-run-record.json
[R7]: https://github.com/faviann/broodling/blob/a11f2bb085b565234287a7d753ff740a4f9d4285/qualification/p1/issue-7-g1-core.md
[Z04]: https://github.com/the-open-engine/zeroshot/compare/d0909615d6ba3c179b58bce15a059f40400ec995...c5c17626674a53a0ae7e4584654480c2b1be2479

The companion target's source register supplies the unchanged baseline hashes, owner decision and current SDK model pointer. Source comparisons are not execution witnesses; retained report/machine links are not a production occurrence API or proof of present custody of every original runtime directory.
