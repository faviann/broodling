# Broodling implementation and dependency plan

**Version:** 0.5  
**Date:** 7 September 2026  
**Governing target:** [Target responsibility and boundary design v0.5](broodling-target-responsibility-boundary-design-v0.5.md).  
**Status:** Complete replacement plan after the issue-#8 candidate-applicability correction. G1-V1 remains **NOT PASSED**; no product implementation is authorized by this document.  
**Supersedes:** v0.4 planning sequence in full. v0.4, G0-v0.4, all baseline/P1 records, and issue-#8 evidence remain unchanged as provenance.

## 1. Basis and implementation direction

The immediate path remains **qualification before product implementation**, but the remaining qualification is narrower than v0.4 required.

V1 is single-host and no-effect. One current Attempt exclusively owns one dedicated disposable worktree. Other Work Units use different worktrees. Only graph-authorized mutating executions may change candidate source. Zeroshot orders those mutation intervals. Reviewer, adjudicator, final-assessor and other assurance executions are read-only relative to candidate source. The qualified host/runtime profile prevents lingering or concurrent writers from altering the candidate outside their authorized interval.

Under target v0.5, that structure itself establishes candidate applicability. A review/adjudication/final-assessment occurrence applies to the candidate state left by the most recent preceding graph-authorized mutation in the Attempt. A later mutation creates a new candidate state, and the graph must require fresh applicable assurance before acceptance.

Therefore the V1 critical path no longer includes an independent candidate seal/hash/manifest, racing external observer, or model-supplied Contract/source/evidence/predecessor identifier as the mechanism that proves candidate correspondence. Processor-supplied identifiers do not confer authority.

**Evidence sufficiency remains separate and mandatory.** Required evidence must be available and sufficient when the graph relies on it. Prefer graph-local validation/evidence production, explicit state/bindings and fail-closed routing. Do not rebuild a general candidate-sealing/provenance subsystem under the name of evidence handling.

### 1.1 Authority and evidence basis

This plan was regenerated from:

1. target v0.5;
2. preserved v0.4 target/plan;
3. G0-v0.4;
4. issue #8 and qualification evidence through commit `7931ac9acd70b8670dfcaa48c05982897766367d`;
5. earlier P1 evidence only for bounded historical context.

The issue-#8 final profile used:

- Zeroshot source `d0909615d6ba3c179b58bce15a059f40400ec995`;
- SDK wheel SHA-256 `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`;
- matching sidecar SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`;
- `codex-cli 0.153.4`, OpenAI `gpt-5.6-sol`, low effort;
- sidecar-selected `read-only` / `workspace-write` modes;
- ephemeral/ignore-user-config execution, empty agent HOME, network disabled and no extra writable roots;
- dedicated non-temporary worktree root; and
- Bubblewrap 0.12.0 containment for controlled timing/counterexample leaves.

Those details define a bounded qualification profile, not a product-language, deployment or general-host selection.

## 2. Issue #8 evidence and corrected witness disposition

### 2.1 Historical result stays historical

Issue #8's committed report at `7931ac9` records:

```text
W1 PASS
W2 FAIL
W5 PASS
W7 PASS
G1-V1 NOT PASSED
```

That report remains unchanged. The v0.4 W2 FAIL was correct against the v0.4 checklist, which bundled host containment, candidate identity/forged-ID checks, missing raw evidence and a delayed external observer.

v0.5 changes the governing W2 scope rather than rewriting the report.

### 2.2 Reuse of successful bounded evidence

| Witness / portion | Existing evidence retained from `7931ac9` | v0.5 status |
|---|---|---|
| **W1 — admission correlation/original state** | Lost acknowledgement replay recovered the single run; conflicting request/source created no run; live-HEAD drift did not change later B1 rematerialization. | **PASS / satisfied on the recorded profile.** |
| **W2 — host containment / structural applicability substrate** | Distinct dedicated same-repository worktrees; real Codex reviewer/adjudicator/final-assessor remained read-only; authorized mutator changed only its candidate; sibling/shared-path and shared Git metadata stayed protected; lingering writer did not survive; C1→repair→C2 caused fresh review; intentional untracked/relevant generated material versus environment-only state was discriminated. | **PASS under the corrected v0.5 W2 scope.** No issue-#8 rerun required unless later profile assumptions change. |
| **W5 — stop/loss/abandon/restart** | Force-stop/runtime-loss/caller windows, safe retirement and a new B1 worktree/run with zero observed semantic/session carryover. | **PASS / satisfied on the recorded profile.** |
| **W7 — no-effect boundary** | Effect-bearing Contract rejection, GitDelivery exclusion, final-gap control, and real-provider attempted Git bypasses contained without GitHub mutation. | **PASS / satisfied on the recorded profile.** |

### 2.3 What happened to the three old W2 failures

They have different dispositions:

1. **Missing required raw evidence still reached success.** This remains a real defect in the old fixture, but it is an **evidence-sufficiency** failure. It must be fixed/qualified in W3/W6 by graph-local evidence checks and final semantic sufficiency. It is not evidence that candidate-to-occurrence correspondence needs a seal.
2. **Forged Contract/source/evidence/predecessor claims still reached success.** Under v0.5, model-supplied identifiers are simply non-authoritative. W3/W6 must show that graph/routing/final disposition do not rely on those claims. They do not need to reject a semantically irrelevant forged field merely because it exists.
3. **A delayed external observer lost the race to graph completion.** V1 no longer requires such an observer. Candidate applicability is graph-structural, so a racing callback is not on the V1 dependency path.

### 2.4 Exactly what remains for W2

**Nothing remains as a standalone issue-#8 W2 qualification case on the recorded profile.** The corrected W2 is the host/runtime containment needed to make candidate applicability structural, and the `7931ac9` evidence satisfies that bounded profile.

Two related obligations remain, but they are no longer W2:

- **W3:** the actual admitted V1 graph must make every later mutation create a new candidate generation and require fresh assurance before acceptance; model-supplied applicability identifiers cannot influence authority/routing.
- **W6:** the normal final-assessment/disposition path must apply to the last graph candidate generation, have all required evidence available/sufficient, and retain enough final material to justify completion.

If #9 or #10 changes the worktree mode, containment launcher, provider configuration, session policy, writable roots, source policy or other premise that `7931ac9` used, the affected W1/W2/W5/W7 controls must be rerun on the final compatible profile. That is a compatibility rule, not an unexecuted W2 case.

## 3. Minimal implementation selections after the correction

**S1 — Keep external-first qualification.** Use the official Python SDK/matching Rust sidecar and a custom Broodling graph. The qualification harness is not a product-language decision.

**S2 — Structural candidate generations.** Treat the admitted starting state as candidate generation C0 and each graph-authorized mutation as producing the next candidate generation. Do not require a hash/manifest to establish which generation an assurance occurrence assessed.

**S3 — Graph-local evidence.** Required evidence is produced/selected through the graph and is checked at the point of use. Missing required material or insufficient evidence fails closed. Use only the retention needed to justify the final result; no generic archive/sealing service is selected.

**S4 — Abandon, do not recover.** Persist minimal admission/currentness/abandonment facts; use Zeroshot stop/loss mechanics; restart from B1 with a fresh worktree/run. No completed-occurrence scanner, semantic catch-up projection, candidate/approval migration or session restoration.

**S5 — No-effect completion.** Admit only empty-required-effect Contracts, exclude GitDelivery and prevent ambient Git/GitHub mutation authority. Exact effects/reconciliation remain a later capability.

## 4. Regenerated V1 dependency sequence

```text
Preserved v0.4 / G0-v0.4 / issue #8 evidence
  │
  ├─ W1 PASS (existing)
  ├─ W2 PASS under v0.5 corrected scope (existing host-containment evidence)
  ├─ W5 PASS (existing)
  └─ W7 PASS (existing)
  │
  ▼
#9 / W3 — actual assurance graph and candidate-generation/evidence gates
  │
  ▼
#10 / W4 + W6 — controlled reviewer + normal final result/evidence custody
  │
  ▼
#11 / G1-V1 review — cross-witness compatibility and gate verdict
  │
  ▼ only after G1-V1 PASS
V1-P2 admission/product implementation
  → V1-P3 assurance integration
  → V1-P4 abandon/restart + disposition
  → V1-P5 product/release evaluation
```

No new qualification issue is needed for candidate sealing or an external observer. No product implementation or later-phase issue is created by this revision.

## 5. Remaining G1-V1 witness obligations

### 5.1 W1 — satisfied on the recorded profile

No new W1 work is planned. The existing issue-#8 result remains bounded to its exact acknowledgement-loss/conflict/original-state materialization profile.

A rerun is required only if later qualification changes a relevant assumption such as source materialization, submission identity, worktree creation policy or the tested SDK/sidecar profile.

### 5.2 W2 — satisfied under the corrected v0.5 scope

The corrected W2 requirement is:

> The single-host profile must make one Attempt's candidate source writable only by graph-authorized mutators in that Attempt's dedicated worktree; assurance roles are read-only; sibling/shared Git state and other Work Units remain protected; writers cannot survive their authorized interval and later modify the candidate.

Issue #8 at `7931ac9` provides the required bounded evidence. Candidate applicability then follows from the governing structural rule. No seal/hash/manifest, delayed observer or model-supplied applicability identifier is required.

The actual graph still has to use this substrate correctly; that is W3/W6 compatibility, not another W2 case.

### 5.3 W3 — actual V1 assurance graph, structural freshness and evidence gating

**Status:** NOT RUN.  
**Owner:** existing issue #9.  
**Consumers:** G3-V1 and G1-V1.

Qualify the actual intended V1 GraphSpec/RuntimePlan/encoding through the real SDK/sidecar on the issue-#8-compatible profile.

Required demonstrations:

- Record the exact graph, runtime plan, state/input/response contracts, designated authority roles and finite repair bound.
- Every mutating route is explicit. When a mutation completes, the graph advances to a new candidate generation. No path may carry pre-mutation candidate-specific review/adjudication/final-assessment into acceptance of the later generation.
- After repair/mutation, fresh required review/adjudication and, before acceptance, final semantic assessment must run against the new generation. Candidate-generation freshness is derived from graph order, not a digest comparison.
- Reviewer/adjudicator/final-assessor remain read-only under the qualified profile; if the W3 graph changes relevant runtime modes, rerun affected W2 controls.
- Crash, timeout, refusal, malformed/missing output, exhausted bound and any failed required graph-local evidence/precondition check follow explicit non-success handling. Empty initialized state cannot become clean success.
- Required raw evidence must be produced/available before the graph step that relies on it. Remove required evidence: the graph/final path must fail closed. Include a valid-evidence control. Do not solve this by adding a generic candidate seal.
- Route-affecting state is canonical or deterministically consistent. Contradictory `clean` signals cannot bypass an unresolved directive or evidence gap.
- An accepted directive survives candidate mutation and fresh/empty findings until an eligible authority occurrence explicitly resolves/supersedes it. Candidate advancement, omission, worker claims or reviewer state cannot discharge it.
- Ordinary worker/reviewer/repair outputs cannot become adjudication/final-assessment authority. A designated successful occurrence can route directly without a duplicate Broodling blessing.
- Processor-emitted Contract/source/evidence/predecessor IDs are not inputs to authority/applicability. A forged value may be ignored or retained diagnostically, but it cannot retarget the Contract, candidate generation, evidence binding, predecessor state or route.
- Repair receives only applicable adjudicated directives plus Contract/current candidate/correction obligations; raw/rejected findings and private material stay out. Include a deliberate widened-binding canary.
- Persistent unresolved obligations exhaust the finite bound into non-success; authority gaps hand back rather than amend the Contract.
- Final assessment remains distinct from clean review/adjudication. A clean review with inadequate criterion evidence yields a rejecting final assessment.
- Positive controls distinguish a successfully completed empty review from an unexecuted review, an observed expected RED from execution failure, and explicit eligible resolution from omission.

A W3 pass proves structural graph mechanics on the actual V1 encoding. It does not certify broad model judgment quality.

### 5.4 W4 — narrow independent review

**Status:** NOT RUN.  
**Owner:** existing issue #10.  
**Consumers:** G3-V1, G5-V1 and G1-V1.

W4 is unchanged in substance by the candidate-applicability correction.

Required demonstrations:

- Use the actual selected reviewer provider/harness through the real SDK/sidecar and W3 graph; every reviewer occurrence is fresh according to the selected Zeroshot session profile.
- Document and control repository/ancestor instructions, user/provider-home settings, skills, tools and automatic transcript/history paths relevant to reviewer independence.
- Place canaries for worker narrative/private reasoning, prior review conclusions, adjudication/repair deliberation and ambient user-level instructions/skills. They must not automatically enter clean review.
- Include a deliberately contaminated control and a valid source/comparison/raw-evidence control.
- Qualify actual read-only candidate access and candidate governing text as assessment data under the frozen Contract, not live authority.
- Record finite controls/versions/limits; do not claim exhaustive hostile-environment provenance.

W4 must remain compatible with the issue-#8 W2/W7 containment profile. If its real-provider configuration opens new writable roots, ambient credentials/network or shared-state paths, rerun affected controls.

### 5.5 W5 — satisfied on the recorded profile

No new W5 work is planned. Existing evidence establishes wholesale abandon-and-restart within the recorded single-host containment profile, not catch-up/recovery.

W3/W4/W6 must not introduce semantic/session/candidate reuse that violates W5. Material profile changes require affected reruns.

### 5.6 W6 — normal final result, structural candidate currentness and evidence sufficiency

**Status:** NOT RUN.  
**Owner:** existing issue #10.  
**Consumers:** G3-V1, G4-V1 and G1-V1.

Qualify one normal non-abandoned completion through the selected public integration.

Required demonstrations:

- Obtain the actual designated final semantic assessment after convergence with unambiguous runtime/graph occurrence provenance. Do not trust a model-supplied role/occurrence/candidate ID or output shape alone.
- Establish structurally that the final assessor runs in the stable interval after the most recent graph-authorized mutation and that no later mutation occurs before disposition. That occurrence therefore applies to the final candidate generation.
- Exercise C1 → mutation/repair → C2. Final acceptance for C2 must require fresh C2 assurance; C1 review/authority cannot satisfy C2 because of graph structure, not because a hash mismatch was detected.
- Reject ordinary/lookalike authority outputs, unexecuted/default final output, unresolved obligations, semantic gaps and bare runtime `succeeded=true`.
- Required evidence must be present and sufficient at final assessment. Remove required raw evidence, use wrong population/host/mode/artifact or preserve an unexplained relevant contradiction: final acceptance must fail. This is the moved v0.4 W2 evidence-sufficiency obligation.
- Retain enough exact final candidate material, required raw observations and criterion-level rationale to explain completed disposition after disposable cleanup. The retained bytes/material may have integrity metadata, but no seal/hash/manifest is required to establish the in-run candidate relationship.
- Demonstrate a valid complete retained-result control and that missing required completion material blocks success.
- Interrupt finalization, including after final runtime output but before disposition. Either a complete durable disposition already exists and remains readable, or the Attempt is abandoned. No partial-history salvage/catch-up.
- Explicitly use an empty required-effect set and actual current final SemanticAcceptance. No GitDelivery or publication is introduced.

### 5.7 W7 — satisfied on the recorded profile

No new W7 work is planned. The issue-#8 real-provider no-effect containment result remains bounded to the recorded profile.

W4/W6 configuration changes must not reintroduce ambient credentials/network/writable shared paths or GitDelivery. A material change requires affected W7 reruns.

## 6. G1-V1 gate after the correction

**Current status:** NOT PASSED.

G1-V1 can pass when:

1. W1, W2, W5 and W7 remain valid for the final compatible profile using the existing `7931ac9` evidence or explicitly rerun affected controls;
2. W3 passes the actual graph/candidate-generation/evidence-gating obligations;
3. W4 passes the real controlled reviewer boundary;
4. W6 passes normal final-assessment provenance, final candidate-generation currentness, evidence sufficiency and retained completion custody;
5. the gate review confirms that the W3/W4/W6 graph/provider/profile did not invalidate the existing host-containment, restart or no-effect assumptions; and
6. no second scheduler/router/response validator, session manager, private completed-history substitute, duplicate semantic blessing, cross-Attempt recovery projection or generic candidate-sealing subsystem has been introduced to manufacture the pass.

The gate record must explicitly distinguish:

- historical v0.4 issue-#8 W2 FAIL against the old checklist;
- v0.5 W2 PASS/satisfaction from the retained host-containment evidence under the corrected checklist; and
- moved evidence-sufficiency work now covered by W3/W6.

No historical Q1/Q4/Q5/Q6/G1-core/G1-effects verdict is relabeled. Q2/Q3 retain only their original bounded historical passes.

## 7. Later V1 phases after G1-V1

These phases are unchanged in product scope but consume the corrected candidate/evidence model.

### V1-P2 — admission and durable semantic nucleus

**Depends on:** G1-V1 PASS.

Implement Work Unit identity, source entitlement, immutable Contract/Attempt admission, B1 materialization, exclusive current-worktree assignment, submission correlation/currentness and no-effect Closability.

CandidateState need not be persisted as a content digest. The minimal product representation may be graph generation/current stable interval plus final retained material. Do not mirror RunLedger or build cross-Attempt catch-up.

**G2-V1:** duplicate ingress/ack loss preserves one authority/run relationship; live inputs do not alter the frozen Contract/B1; unsupported effects/profile assumptions fail admission; two units do not own one worktree; crashes cannot authorize competing current Attempts.

### V1-P3 — graph-local assurance and evidence

**Depends on:** G2-V1 and qualified W3/W4/W6.

Implement the short graph. Make graph-authorized mutation boundaries explicit; assurance roles read-only; later mutation forces fresh candidate-specific assurance. Integrate the controlled reviewer profile.

Produce/select required evidence graph-locally and fail closed when missing/insufficient. Retain only the final material needed to justify completion. Do not add a generic seal/provenance platform.

**G3-V1:** actual graph negative/positive controls pass for mutation freshness, sticky obligations, raw-finding isolation, unavailable/wrong-population evidence, semantic gaps, reviewer independence and final-currentness.

### V1-P4 — abandon/restart and normal no-effect disposition

**Depends on:** G2-V1/G3-V1 and qualified W5/W6/W7.

Implement minimal durable abandonment/currentness, safe terminalization/containment, worktree retirement and fresh B1 restart. No semantic carryover.

Implement the normal final-result boundary. Current final SemanticAcceptance for the last candidate generation plus the explicitly empty required-effect set yields the strict V1 success terms; Broodling records disposition.

**G4-V1:** stop/mutation/authority/finalization races and idempotent administrative operations pass; no late resurrection/carryover/orphan interference; real-provider no-effect vertical slice includes a real repair path and retained justified final result.

### V1-P5 — product boundary and semantic evaluation

**Depends on:** G2-V1 through G4-V1.

Expose submit/observe/new-authority/stop/fresh-retry/result. Reuse Zeroshot telemetry rather than creating a second runtime authority.

Evaluate reviewer sensitivity, adjudication legitimacy, final-assessor false acceptance, correction effectiveness, convergence and churn using the wrong-stack/count-only case and valid controls; wrong population/host/mode/artifact; inadequate evidence; contradictions; correction checks; disproven-premise repair; governing self-authorization attempts; and bounded reconsideration.

**G5-V1:** deterministic boundary evidence and semantic evaluation pass for the advertised single-host no-effect profile. No claim extends to effects, cross-Attempt reuse, distributed execution or hostile environments.

## 8. P0 traceability after v0.5

The original P0 inventory remains unchanged. Only the applicability/evidence split changes where later proof belongs.

| P0 IDs | v0.5 disposition / witness |
|---|---|
| I01 | Mandatory frozen authority; W1/W7 then G2/G5. |
| I02/I03 | Designated authority/no blessing; W3/W6. |
| I04/I05 | Cross-Attempt catch-up deferred; abandon/restart W5. |
| I06 | External-effect conflict fencing deferred; local runtime isolation W2/W5/W7. |
| I07/I08/I09 | Fail-closed, sticky obligations, repair firewall; W3. |
| I10 | Controlled independent reviewer; W4. |
| I11/I12 | Separate final assessment and sensitivity; W3/W6 + later semantic evaluation. |
| I13 | Canonical runtime provenance; W6. |
| I14/I15 | **Structural candidate applicability:** W2 host containment + W3/W6 graph freshness. No seal/hash/observer/model-ID requirement. |
| I16/I17/I18 | **Evidence sufficiency/correction:** W3/W6 and later semantic evaluation. Missing raw material remains blocking. |
| I19 | Governing change under frozen authority; W3/W4 and later semantic evaluation. |
| I20/I21 | Strict conjunction and acceptance distinction; W6/W7. |
| I22/I24 | Effect receipt/reconciliation deferred. |
| I23/I25 | No workspace-derived effect authority/no universal publication; W7. |
| H01 | Entitlement/finite Closability/no-effect eligibility; W7 then G2/G5. |
| H02 | One issue/repo/current Attempt, B1 and exclusive worktree; W1/W2/W5. |
| H03 | Scoped repair/admitted methodology/no hidden continuity; W3/W4. |
| H04 | Bounded same-Attempt reconsideration + diagnostic handback; W3/W5. |

Historical R1–R19 protections follow target v0.5 §12. No historical mechanism is reinstated merely because the new graph must handle evidence correctly.

## 9. Deferred capabilities and re-entry conditions

**Cross-Attempt reuse/recovery:** still requires explicit authority plus supported exact completed-occurrence/retention/currentness/idempotent catch-up witnesses. V1 abandonment results do not establish it.

**Broader candidate/applicability profiles:** shared/external/concurrent writers require stronger protection. A future profile may choose sealing/hashing or another mechanism, but V1 does not prebuild it.

**General evidence reuse/provenance:** selective cross-generation or cross-Attempt evidence reuse needs an explicit applicability policy and proof. V1 instead renews candidate-specific assurance after mutation and keeps evidence local to the current graph need.

**Authoritative effects:** before any effect-dependent Contract is admitted, qualify exact intent/source/payload/target/prerequisites, no extra mutation, same-run coordination where needed, verified readback, lost-ack reconciliation and conflict fencing. Historical Q7/G1-effects remains BLOCKED.

**Distributed/hostile profiles:** separately qualify takeover/fencing, process/workspace isolation and context controls. The issue-#8 single-host result does not establish them.

## 10. Decisions left unresolved

| Decision | Latest responsible point / fixed boundary |
|---|---|
| Product language/framework and packaging | After G1-V1; the Python harness and Rust sidecar do not dictate product language. |
| Semantic store/schema | V1-P2; store Contract/Attempt/currentness/disposition, not a RunLedger mirror or candidate-seal catalog. |
| CandidateState representation | V1-P2/P3; graph generation/current stable interval is sufficient semantically. Hash/manifest is optional instrumentation, not authority. |
| Required-evidence representation | W3/W6 then V1-P3; use smallest graph-local binding/check/artifact form that satisfies the Contract. |
| Final retained-result format | W6 then V1-P3/P4; enough candidate/evidence/rationale custody to justify disposition. |
| Reviewer model/configuration | W4 and later semantic evaluation; controlled independence is fixed, specific model/prompt is not. |
| Effects/reconciliation implementation | Deferred until an effect-capable profile is authorized. |
| Detailed product interface/non-success taxonomy | V1-P5; strict success and explicit abandonment/non-admission remain fixed. |

## 11. Immediate next dependency

The next execution dependency is **existing issue #9 / W3**, not a W2 remediation issue. Issue #9 must qualify the actual V1 graph's candidate-generation freshness, sticky authority, evidence-sufficiency gates and fail-closed routing on the issue-#8-compatible host profile.

After W3, existing issue #10 qualifies W4 and W6. Existing issue #11 then reviews the compatible evidence and records the G1-V1 verdict.

This revision does **not** begin #9, run any new qualification, implement Broodling or Zeroshot, create V1-P2/later issues, or select a candidate-sealing/provenance subsystem.

## References

[T05]: broodling-target-responsibility-boundary-design-v0.5.md
[T04]: broodling-target-responsibility-boundary-design-v0.4.md
[P04]: broodling-implementation-dependency-plan-v0.4.md
[G04]: g0-v0.4-review.md
[E08]: https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/issue-8-w1-w2-w5-w7.md
[M08]: https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/evidence/issue-8-run-record.json
[R08]: https://github.com/faviann/broodling/blob/7931ac9acd70b8670dfcaa48c05982897766367d/qualification/v1-p1/README.md

The preserved v0.4 source register supplies all earlier P0/P1 references. This plan consumes issue #8 only within the recorded profile limits and does not reinterpret unrelated historical blocked gates.
