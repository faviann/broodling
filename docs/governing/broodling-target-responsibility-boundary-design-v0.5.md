# Broodling
## Target responsibility and boundary design

**Version:** 0.5  
**Date:** 7 September 2026  
**Document type:** Governing target architecture and responsibility design  
**Status:** Targeted V1 candidate-applicability correction after issue #8 qualification. G1-V1 remains incomplete; this revision is not a production-readiness claim.  
**Supersedes:** v0.4 in full as the governing target. v0.4, G0-v0.4, all earlier baselines, and all qualification evidence remain unchanged as provenance.  
**Companion:** [Implementation and dependency plan v0.5](broodling-implementation-dependency-plan-v0.5.md).

> **Broodling owns one Work Unit's Contract, admission, current Attempt authority, candidate/evidence applicability, and final disposition. Zeroshot owns execution of one admitted run. In V1, candidate applicability is established structurally by exclusive Attempt worktree ownership, graph-ordered mutation, read-only assurance, and containment of writers. It is not established by a separate candidate seal, racing observer, or model-supplied identity.**

## 0. Decision basis and narrow correction

Version 0.5 is a narrow correction to v0.4 discovered while qualifying issue #8. It does not reopen the single-Work-Unit boundary, the no-effect V1 scope, abandon-and-restart semantics, independent review requirement, designated authority processors, strict Work Unit success rule, or the Broodling/Zeroshot/Cerebrate ownership split.

The authority/evidence order for this revision is:

1. the complete v0.4 target and implementation/dependency plan;
2. the G0-v0.4 documentation record;
3. issue #8 and its committed qualification evidence through `7931ac9acd70b8670dfcaa48c05982897766367d`;
4. earlier P1 evidence only as historical context.

Current v0.4 files and all qualification evidence are preserved verbatim. This replacement changes the governing requirement; it does not rewrite the result that issue #8 originally recorded against the v0.4 checklist.

### 0.1 The correction

For the deliberately constrained single-host V1 profile, the relationship between an assurance/authority occurrence and the candidate it assesses is established by the execution structure itself:

1. one Attempt exclusively owns one dedicated disposable worktree;
2. other Work Units use different worktrees;
3. only graph-authorized mutating executions may change candidate source;
4. mutating intervals are ordered by the admitted Zeroshot graph;
5. reviewer, adjudicator, final-assessor and other assurance executions are read-only relative to candidate source; and
6. host/runtime containment prevents lingering, concurrent or sibling writers from altering that candidate outside the graph-authorized mutating interval.

Within that profile, an assurance or authority occurrence applies to **the candidate state left by the most recent preceding graph-authorized mutation in that Attempt**. If no mutation has yet occurred, it applies to the Attempt's admitted starting candidate. A later graph-authorized mutation creates a new candidate state. The graph must then require fresh applicable assurance before that later candidate can be accepted.

This relationship is structural. V1 does **not** require an independent candidate seal, content hash, manifest, asynchronous observer, externally racing callback, or processor-supplied Contract/source/evidence/predecessor identifier to establish it. Such model-supplied identifiers may be returned as diagnostic data, but they confer no Contract authority, candidate applicability, evidence applicability, predecessor authority, routing right, or acceptance authority.

### 0.2 Candidate applicability is not evidence sufficiency

The correction does not weaken the evidence obligation.

Candidate applicability asks: **which candidate state did this occurrence assess?** In V1 that answer comes from Attempt/worktree ownership and graph order.

Evidence sufficiency asks: **did the occurrence have the required observations, populations, raw material, environment and falsifying evidence to justify its semantic conclusion?** That remains a separate assurance requirement.

Required evidence must therefore be produced or selected through the admitted graph, remain available when a dependent review/adjudication/final-assessment step relies on it, and be sufficient for the relevant Contract criterion. Missing required raw material, wrong host/mode/population/artifact, unexplained contradictory evidence, or an inadequate check remains blocking. Prefer graph-local validation/evidence production, explicit bindings and fail-closed routing. Do not turn this requirement into a general candidate-sealing or provenance subsystem.

### 0.3 Issue #8 evidence under the corrected rule

Issue #8 was rerun on one final compatible profile in commit `7931ac9`. Its report retains the historical v0.4 verdict **W1 PASS; W2 FAIL; W5 PASS; W7 PASS**. That historical report is not edited or relabeled.

For v0.5 planning, the evidence is consumed as follows:

| Item | v0.5 disposition |
|---|---|
| **W1** | **Satisfied on the recorded profile.** Lost-acknowledgement replay, conflicting request/source rejection, and fresh materialization from immutable B1 after live-HEAD drift passed. |
| **W2 host/profile containment** | **Satisfied on the recorded profile.** Distinct same-repository worktrees, real reviewer/adjudicator/final-assessor read-only behavior, mutator confinement to its candidate, lingering-writer cessation, sibling/shared-path containment, protected shared Git metadata, C1→repair→fresh-review C2 progression, and candidate-source/environment classification controls all passed. |
| **W2 v0.4 missing-evidence failure** | Reclassified as an **evidence-sufficiency obligation**, not a candidate-applicability mechanism. It remains to be qualified in the actual graph/final-result witnesses. |
| **W2 v0.4 forged-ID failure** | No longer a candidate-applicability failure. Processor-supplied Contract/source/evidence/predecessor identifiers are non-authoritative by rule and must not be used to establish applicability. Actual graph qualification must demonstrate that authority/routing derives from admitted runtime/graph state rather than those claims. |
| **W2 v0.4 delayed-observer failure** | The racing-observer requirement is removed from V1. Graph progression must not depend on such an observer. |
| **W5** | **Satisfied on the recorded profile.** Stop/loss/caller windows, safe retirement and fresh zero-carryover restart from B1 passed within the recorded limits. |
| **W7** | **Satisfied on the recorded profile.** No-effect admission/rejection and the narrowed host boundary preventing ambient Git/GitHub mutation authority passed. |

Under the corrected v0.5 W2 scope, **no additional issue-#8 host/profile witness remains to qualify W2 on the recorded profile**. The historical v0.4 W2 FAIL remains provenance because its checklist contained requirements that v0.5 now removes or moves elsewhere. Actual-graph freshness after mutation and required-evidence sufficiency remain obligations of W3/W6 and the final G1-V1 compatibility review. If #9/#10 materially change the qualified host/worktree/session profile, affected issue-#8 controls must be rerun rather than assumed equivalent.

W3, W4 and W6 remain unexecuted as of this governing revision. G1-V1 therefore remains **NOT PASSED**, and V1-P2 product implementation remains blocked.

## 1. System purpose and external boundary

Broodling handles exactly one explicit Work Unit per admission: one primary authoritative GitHub issue and one target repository. Referenced material may be admitted context; independent issues do not silently become additional Work Units. Repeated submission resolves existing identity rather than creating competing authority.

```text
Explicit work reference
  → entitled, frozen Contract and Closability
  → admitted Attempt + immutable starting state + dedicated disposable worktree
  → one Zeroshot run:
       graph-ordered mutation
       read-only review / adjudication / final assessment
       bounded repair when authorized
  → Broodling disposition for the final candidate state
```

Broodling may identify a missing prerequisite but does not wait on dependencies, choose alternate backlog work, schedule across projects or allocate global resources. Those remain Cerebrate territory. A future `/work-on` adapter remains a thin invocation/status surface.

Authoritative Git/GitHub effect authority remains Broodling's general architectural responsibility, but **V1 executes no authoritative external effects, including optional effects**. Contracts requiring commit-as-delivery, push, publication, PR, merge, issue close/comment/label/relationship, deployment or effect-dependent evidence are not admissible in V1. Host-local worktree provisioning is not a delivery effect. Workspace or shell access grants no external mutation authority.

## 2. Responsibility architecture

These are logical ownership boundaries, not required services or modules.

| Responsibility | Owner and boundary |
|---|---|
| Work reference, trusted-source entitlement, Contract construction, Closability | Broodling. Model extraction cannot entitle a source or amend the Contract. |
| Attempt identity, admitted starting state, currentness, exclusive worktree assignment | Broodling policy using the qualified host/runtime profile. |
| Graph topology, role meaning and required assurance ordering | Broodling-authored protocol. |
| Graph admission, typed node I/O, routing, loops, dispatch and runtime occurrence identity | Zeroshot. No second Broodling scheduler or response validator. |
| Provider/session lifecycle, cancellation, terminalization and one-run history | Zeroshot. |
| Candidate applicability inside V1 | Structural consequence of Attempt/worktree ownership + graph mutation order + read-only assurance + writer containment. |
| Evidence sufficiency and applicability to Contract criteria | Broodling assurance policy expressed through graph-local evidence production/selection and final semantic assessment. |
| Review findings | Observations only. |
| Adjudication/final semantic decision | Successful designated authority-processor occurrence for its structurally applicable candidate state and admitted inputs. |
| Work Unit disposition | Broodling; never a bare runtime terminal label. |
| Future external-effect authorization/reconciliation | Broodling, outside V1 execution capability. |

### 2.1 Authority by designated occurrence

An adjudication or final-assessment occurrence can constitute semantic authority when:

- the Work Unit, Contract revision and Attempt were admitted;
- the immutable admitted graph designates the executed role as authority-bearing;
- Zeroshot records the exact successful runtime occurrence and validates its response contract;
- the occurrence executes in the stable assurance interval after the most recent graph-authorized mutation and before any later mutation; and
- the graph supplies the required predecessor/obligation/evidence state from trusted admitted/runtime bindings.

The Contract is fixed by the Attempt. Candidate applicability comes from the structural interval. Predecessor/open-obligation state comes from the graph. Evidence availability comes from admitted graph state and trusted producers. A processor's own asserted identifiers do not establish any of those relationships.

No second Broodling blessing transaction is required merely to re-admit the same processor payload. An authoritative model judgment can still be semantically wrong; quality remains an empirical assurance concern.

## 3. Minimal durable semantic model

```text
Work Unit W
  Contract R1 (immutable) + admitted starting state B1
    Attempt A1 → dedicated worktree T1 → Zeroshot run Z1
       C0 = admitted starting candidate
       M1 completes → C1
       assurance interval for C1
       M2 completes → C2
       fresh assurance interval for C2
    Attempt A2 → fresh worktree T2 from B1 → new run Z2
  Contract R2
    externally authorized new meaning → new admission/Attempt
```

Work Unit, Contract revision, Attempt, Zeroshot run, node occurrence, provider session, candidate state and Git commit remain distinct concepts.

| Conceptual record | V1 meaning |
|---|---|
| **WorkUnit / WorkReference** | Stable issue/repository identity, Contract/Attempt lineage, currentness and disposition. |
| **WorkContractRevision** | Immutable entitled inputs, criteria, finite evidence populations, assurance obligations, prerequisites and an empty required-effect set. |
| **Attempt** | One execution episode permanently bound to one Contract revision, admitted starting state, dedicated worktree, stable submission identity and one Zeroshot run. |
| **CandidateState** | Attempt-local source state established by graph order: admitted starting candidate, then a new state after each graph-authorized mutation. A durable hash/manifest is not required to establish occurrence applicability. |
| **AuthorityOccurrenceRef / OutstandingObligation** | Exact designated runtime occurrence and still-applicable directives/interpretations within the Attempt. Runtime occurrence identity remains canonical in Zeroshot. |
| **EvidenceRecord** | Required observation/material available to the graph for a particular stable candidate interval, with enough check/environment/population/raw-material context to judge sufficiency. It may reference canonical runtime facts rather than duplicate them. |
| **SemanticAcceptance / WorkUnitDisposition** | Applicable final semantic assessment and durable completion justification for the final candidate state; V1's required-effect set is explicitly empty. |

CandidateState may be represented implicitly by graph position/generation, by a local ordinal, or by another minimal trusted representation. **Its role is not to become a content-addressed provenance service.** A hash may be used as test instrumentation or retention integrity data; it is not the authority relationship required by V1.

Delivery intents/receipts, cross-Attempt semantic catch-up and general candidate-sealing infrastructure are later-profile concepts, not mandatory empty V1 subsystems.

## 4. Contract construction, Closability and admission

Broodling constructs a source-attributed Contract from entitled inputs. For each criterion, Closability establishes the concrete production/public boundary, finite evidence population, available validation seam, executable check and falsifying observation, plus prerequisites needed inside the qualified V1 profile.

The Contract must be satisfiable without authoritative external effects. Effect-dependent evidence is unsupported in V1 and causes non-admission rather than requirement deletion.

Before run submission, persist the immutable Contract/Attempt relationship, admitted starting state, exclusive worktree relationship and stable submission identity. Pin the starting content and admitted instructions sufficiently to rematerialize B1; moving issue text, branch tips or HEAD are not the admitted state.

Live issue changes do not modify an executing Contract. Meaning/scope/population/authority changes require external authorization, a new Contract revision and new Attempt. An authority gap or omitted required population member is a handback, not an in-Attempt amendment.

Ambiguous submission remains blocking until the existing run is reconciled. A submission conflict never authorizes another run or Attempt.

## 5. V1 execution and abandon-and-restart

### 5.1 Exclusive worktree and graph-ordered mutation

One current Attempt exclusively owns one dedicated disposable worktree. Other Work Units use different worktrees. An abandoned worktree is retired and never becomes a replacement's starting point.

Only graph-authorized mutating roles may change candidate source. Each mutating execution defines a bounded mutation interval. Zeroshot graph order determines which mutation precedes which assurance step. Reviewers, adjudicators, final assessors and other assurance roles are read-only relative to candidate source.

The supported host/runtime profile must prevent:

- a read-only assurance role from changing candidate source;
- a mutator from changing another Work Unit's worktree or protected shared Git metadata;
- a child/background writer from surviving beyond its authorized interval and later changing the candidate;
- concurrent or out-of-order external writers from changing the candidate during a stable assurance interval; and
- ordinary scratch/environment writes from becoming hidden candidate-source mutation under the admitted source policy.

The issue-#8 `7931ac9` profile provides bounded evidence for these host-containment properties. Those results are profile-specific, not a distributed or hostile-host guarantee.

### 5.2 Structural candidate generations

The graph, not a seal, defines candidate generation changes:

```text
B1 / C0
  → MUTATION M1 completes
  → C1
  → REVIEW / ADJUDICATION applicable to C1
  → MUTATION M2 completes
  → C2
  → fresh REVIEW / ADJUDICATION / FINAL ASSESSMENT applicable to C2
```

A later mutation invalidates prior candidate-specific assurance for acceptance by creating a new candidate state. The actual V1 graph must make fresh assurance unavoidable before the new state can reach SemanticAcceptance. This is a W3/W6 graph/final-boundary requirement.

If a mutating role executes but produces byte-identical source, the protocol may still conservatively treat the post-mutation point as a new candidate state and require renewed assurance. V1 correctness does not depend on proving byte equality to reuse earlier approval.

### 5.3 Abandonment

When an Attempt cannot complete safely, V1 abandons it wholesale. If execution continues, a new Attempt gets a fresh worktree from the original admitted B1.

```text
A1 stops / runtime lost / finalization uncertain
  → durably make A1 ineligible for completion and reuse
  → request/observe supported terminalization or establish safe cessation
  → ensure A1 can no longer affect any current candidate
  → retire T1 after safe cessation
  → admit A2 with new T2 from B1 and a new run
```

No A1 candidate edits, directives, review conclusions, evidence, validation results, acceptance, summaries, caches or sessions become A2 semantic input. Diagnostic history may remain human-readable but does not become replacement authority.

A late old result cannot restore abandoned eligibility. If safe cessation/containment is unknown, replacement remains blocked.

## 6. Durable truth and normal completion

Zeroshot remains authoritative for what ran and what typed outcome it recorded. Broodling remains authoritative for Contract/Attempt currentness, candidate-generation applicability, evidence sufficiency policy and Work Unit disposition.

The normal successful route must expose the designated final-assessment occurrence through the supported public integration with unambiguous admitted provenance. The final assessor applies to the current candidate generation because it occurs after the latest graph-authorized mutation and no later mutation occurs before disposition. A model-supplied candidate/role/occurrence identifier is unnecessary and non-authoritative.

Broodling must retain enough final candidate material, required evidence and criterion-level justification to explain a completed disposition after disposable workspace/session cleanup. This retention requirement is about durable result custody, not the mechanism that establishes in-run candidate applicability. Missing required retained material before completion prevents success.

V1 still has no failed-run completed-occurrence scanner, semantic catch-up watermark, graph replay, parallel runtime-result ledger or cross-Attempt approval migration.

## 7. Execution protocol and information boundaries

### 7.1 Minimal assurance graph

```text
IMPLEMENT (mutating)
  → graph-local validation/evidence production as required
  → REVIEW (independent, read-only)
  → ADJUDICATE (authority, read-only)
      ├─ applicable directives → REPAIR (mutating)
      │                          → renew required evidence
      │                          → fresh REVIEW → ADJUDICATE …
      ├─ authority gap / unusable / exhausted bound → non-success
      └─ no unresolved blockers
           → FINAL SEMANTIC ASSESSOR (authority, read-only)
```

Zeroshot owns the graph order, bindings and bounded convergence. Broodling does not dispatch nodes from an observer callback.

| Role | Allowed semantic inputs | Output / authority |
|---|---|---|
| Implementer | Frozen Contract, admitted instructions, current candidate and permitted validation instructions | Candidate mutation and observations; no semantic authority. |
| Independent reviewer | Frozen Contract, stable current candidate, selected required evidence and governed review instructions | Coverage/findings; no repair or acceptance authority. |
| Adjudicator | Contract, current candidate interval, required evidence, raw findings, scoped prior authority/open obligations | Classifications/directives/resolutions or authority question; designated authority. |
| Repair worker | Contract, current candidate, applicable directives and correction obligations | New candidate state and observations; no authority to discharge its own directive. |
| Final assessor | Complete Contract, final stable candidate state, sufficient available evidence, applicable interpretations and unresolved-obligation state | Criterion-level acceptance/gaps; designated semantic authority, not Work Unit disposition. |

Raw/rejected findings never become repair authority. An accepted obligation remains open until an eligible authority occurrence resolves/supersedes it. Candidate advancement does not discharge it.

### 7.2 Fail-closed evidence and control flow

Crash, timeout, refusal, malformed/missing output, failed required graph-local evidence check, absent required raw material, inconsistent route state or exhausted convergence cannot become initialized empty success.

Where required evidence is known mechanically, produce or verify it in graph-local trusted/controlled operations before the graph relies on it. Where semantic sufficiency is model-judged, deliver the complete required material to the designated reviewer/final assessor and make gaps explicit. A successfully produced expected RED/negative observation is evidence, not an execution crash.

A model may mention identifiers in its response, but such fields cannot repair missing trusted graph state or make a stale occurrence current.

### 7.3 Controlled independent reviewer profile

Every independent reviewer execution uses the qualified fresh execution/session and finite controlled automatic-context profile. Prior worker narrative/private reasoning, prior reviewer conclusions, adjudication deliberation and repair rationale must not automatically enter review. User/provider-home configuration, skills and automatic context paths are disabled, isolated or explicitly admitted under the qualified profile.

Legitimate source, comparison base and required raw evidence remain available. Candidate governing text is data assessed under the frozen Contract; it is not automatically entitled new authority.

This is a finite supported profile, not a claim of complete hostile-agent confinement or exhaustive provenance.

## 8. Candidate applicability and evidence sufficiency

### 8.1 Candidate applicability: structural V1 rule

For any assurance/authority occurrence `E` in Attempt `A`:

```text
candidate(E)
=
source state in A's exclusively owned worktree
left by the latest graph-authorized mutation preceding E,
provided no unauthorized/concurrent writer can alter that state during E.
```

That relationship is established by trusted execution structure. No independent content attestation by the model is needed.

If a later mutation `M` occurs, then:

```text
candidate-after(M) = new candidate state
```

and candidate-specific assurance from before `M` is not sufficient to accept the post-`M` state. The graph must route through fresh required assurance.

### 8.2 What V1 does not require

V1 does not require, for candidate applicability:

- a source hash or Merkle/content manifest;
- a per-node source seal;
- a generic CandidateRef service;
- a racing asynchronous observer;
- a callback that must beat graph completion;
- a model-supplied Contract/source/evidence/predecessor identity;
- an external provenance database that mirrors graph state; or
- selective reuse of pre-mutation approval.

Hashes/manifests may be useful as test instrumentation, retention integrity checks or future-profile mechanisms. They are not a V1 authority prerequisite.

### 8.3 Evidence sufficiency remains mandatory

Evidence must still answer the actual Contract criterion.

Required evidence must be available at the graph point that relies on it. If a criterion requires a raw artifact, that material must be present/recoverable when reviewed and retained if needed for final justification. Missing raw evidence must fail closed. Wrong host/mode/population/artifact does not become sufficient merely because it was produced in the right worktree.

A later candidate mutation requires renewed candidate-specific evidence/assurance by default. Relevant contradictory/failing observations within the current candidate interval remain accounted for until an eligible semantic decision explains their disposition. A later green observation does not silently erase them.

The evidence mechanism should remain local to the actual assurance need: typed results, explicit bindings, a small trusted check, or a retained artifact can be sufficient. Do not introduce a generic sealing/archive/provenance subsystem unless a later capability actually requires it.

### 8.4 Final custody

A completed Work Unit must retain enough final candidate bytes/materialization, required evidence and assessment rationale to justify the disposition after disposable cleanup. This durability is separate from in-run candidate applicability. A digest with no needed bytes or a pathname to deleted evidence is insufficient custody.

## 9. Semantic assurance

Review must challenge criterion sensitivity, including the wrong-stack/count-only counterexample preserved by P0. A clean current execution is not enough if materially wrong behavior could satisfy the evidence.

Adjudication converts raw findings into Contract-backed defects, governed standards violations, rejected suggestions or authority gaps. It may explicitly supersede a prior interpretation within unchanged Contract meaning. It cannot amend the Contract.

Correction safeguards remain mandatory: required negative/population-boundary checks, actual claimed-versus-observed proof and reconsideration of demonstrated-insufficient premises. An unresolved required correction blocks acceptance.

Governing-material edits are assessed against frozen prior authority. Candidate text cannot authorize itself.

Final semantic assessment evaluates every Contract criterion against the final candidate generation, sufficient evidence and applicable interpretations/open obligations. Reviewer silence or clean adjudication does not substitute for it.

## 10. Strict success and future authoritative effects

The general success rule is unchanged:

```text
SUCCEEDED
=
current applicable SemanticAcceptance
AND every Contract-required authoritative effect completed
AND every required effect verified
```

For V1-admissible Contracts, the required-effect set is empty, so the effect terms are vacuously satisfied. Broodling must still establish current applicable SemanticAcceptance for the final candidate state and durably record the justification.

The following remain distinct:

```text
provider completed
≠ node completed
≠ run succeeded
≠ review clean
≠ adjudication clean
≠ SemanticAcceptance
≠ Work Unit SUCCEEDED
```

No `GitDelivery` is used in V1. Agent visibility of Git/`gh` or workspace access confers no external effect authority. The issue-#8 final profile gives bounded evidence that the configured no-effect containment blocks the tested bypasses.

Later effect-capable profiles must separately qualify exact prior authorization, source/payload/target/prerequisite consumption, no unauthorized bundled mutation, verified readback, lost-acknowledgement reconciliation and conflict fencing. v0.5 does not implement or qualify them.

## 11. External collaboration and product interface

The external interface remains semantic:

- submit one explicit work reference;
- observe Contract/Attempt lineage, referenced Zeroshot state and Broodling disposition;
- provide new external authority for a new Contract revision;
- request stop;
- request a fresh retry after safe termination; and
- consume a justified result.

Retry never means resuming an abandoned worktree. A human may inspect diagnostic history without making it semantic input to a replacement Attempt.

Product language/framework, storage implementation, CLI/service packaging and detailed non-success taxonomy remain open.

## 12. Historical R1–R19 responsibility disposition

This preserves protections, not historical packaging.

| Responsibility | v0.5 disposition |
|---|---|
| R1 Admission | One explicit issue/repository, repeat identity, no backlog selector. |
| R2 Authority/trust/mutations | Broodling Contract/disposition; designated in-run semantic authority; graph-only candidate mutation; no second blessing. |
| R3 Snapshot/amendment | Frozen entitled Contract; new meaning requires external revision/new Attempt. |
| R4 Provenance | Runtime occurrence facts by reference plus finite controlled reviewer profile; no model self-attestation of authority/applicability. |
| R5 Closability | Finite populations/falsifiers/prerequisites and V1 no-effect eligibility. |
| R6 Freeze/custody/resume | Immutable admitted starting state and final-result custody; abandon/restart rather than catch-up. |
| R7 Scoped delegation | Explicit role inputs and qualified Zeroshot session mechanics. |
| R8 TDD/coherence | Honor admitted methodology; no universal lifecycle. |
| R9 Evidence/applicability | **Candidate applicability is structural in V1; evidence sufficiency remains explicit.** No abandoned-Attempt reuse or generic sealing requirement. |
| R10 Readiness/same-mechanism | Assurance methodology/fixtures, not another phase. |
| R11 Review-index | Frozen Contract/current stable candidate/selected evidence bindings, not legacy packaging. |
| R12 Convergence | Zeroshot bounded graph; fresh assurance after mutation before acceptance. |
| R13 Adjudication/sticky rulings | Designated authority, sticky obligations and explicit resolution within the Attempt. |
| R14 Correction self-check | Required negative/population/claimed-vs-observed checks block acceptance. |
| R15 Mechanism reset | Reconsider disproven premises without a mandatory reset node. |
| R16 Governing remediation | Actual before/after semantics under frozen authority; no self-authorization. |
| R17 Re-adjudication | Bounded explicit supersession within unchanged Contract/Attempt. |
| R18 Closure/unresolved work | Criterion-level final assessment plus Broodling disposition and durable diagnostic handback. |
| R19 GitHub closeout | General Broodling ownership retained; all effect execution/reconciliation deferred beyond V1. |

## 13. Invariant and qualification map

The original P0 identifiers remain provenance. v0.5 changes only how candidate applicability is established and where evidence-sufficiency failures are qualified.

| Existing invariant(s) | v0.5 V1 requirement |
|---|---|
| I01; H01/H02 | Frozen entitled admission, one issue/repository/current Attempt, immutable B1 and no-effect Closability. |
| I02/I03 | Only designated successful occurrences carry semantic authority; no duplicate blessing or self-attested authority. |
| I04/I05 | Cross-Attempt recovery/carryover remains deferred; wholesale abandonment/restart remains mandatory. |
| I06 | Effect conflict machinery deferred; local old-runtime/currentness containment remains mandatory. |
| I07/I08/I09 | Fail-closed outcomes, sticky obligations and directive-only repair in the actual graph. |
| I10 | Fresh controlled independent reviewer on the actual provider profile. |
| I11/I12 | Separate criterion-level final assessment and semantic sensitivity controls. |
| I13 | Canonical runtime provenance; no parallel runtime truth. |
| I14/I15 | **Structural candidate applicability:** exclusive worktree, graph-only mutation, read-only assurance, writer containment, new candidate generation after mutation and fresh assurance before acceptance. No seal/hash/observer/model-ID requirement. |
| I16/I17/I18 | **Evidence sufficiency:** contradictions, required correction checks, required raw evidence and actual host/mode/population/artifact suitability remain blocking. |
| I19 | Governing edits assessed under frozen prior authority. |
| I20/I21 | Strict acceptance/effect conjunction; V1 effect set empty; run/effect completion never substitutes for acceptance. |
| I22/I24 | Exact effect correspondence/reconciliation deferred. |
| I23/I25 | Workspace access grants no effect authority; no universal publication; effect-bearing Contracts rejected. |
| H03/H04 | Scoped real repair, bounded reconsideration and diagnostic handback; no hidden cross-Attempt continuity. |

### 13.1 Current G1-V1 witness status after the correction

| Witness | Current v0.5 status | Remaining obligation |
|---|---|---|
| **W1** | **PASS / satisfied by issue #8 `7931ac9` on the recorded profile** | Rerun only if later qualification changes its relevant admission/source/profile assumptions. |
| **W2** | **PASS under the corrected v0.5 scope, satisfied by issue #8 host-containment evidence** | **No standalone W2 case remains.** Actual graph freshness after mutation is checked in W3/W6; evidence availability/sufficiency is checked in W3/W6. |
| **W3** | NOT RUN | Qualify the actual V1 graph, including candidate-generation freshness, sticky obligations, fail-closed evidence/control flow and designated authority. |
| **W4** | NOT RUN | Qualify actual independent reviewer context on the compatible profile. |
| **W5** | **PASS / satisfied by issue #8 `7931ac9` on the recorded profile** | Compatibility only unless later changes invalidate the containment/restart profile. |
| **W6** | NOT RUN | Qualify normal final-assessment provenance, final candidate-generation currentness, evidence sufficiency and retained completion material. |
| **W7** | **PASS / satisfied by issue #8 `7931ac9` on the recorded profile** | Compatibility only unless later changes reopen ambient effect paths. |

G1-V1 remains blocked until W3, W4 and W6 pass on a compatible profile and the separate gate review confirms cross-witness consistency. No historical Q/G1 verdict is rewritten.

## 14. Deferred decisions and non-goals

Still open: product language/framework, semantic storage/schema, CLI/service packaging, exact representation of CandidateState, final result retention format, prompts/models/reviewer population and detailed non-success taxonomy.

A V1 CandidateState representation may be an internal graph generation or similarly small trusted fact. Do not select a source hash/manifest service merely because prior qualifications used hashes for instrumentation.

Cross-Attempt semantic/candidate reuse, completed-occurrence recovery/catch-up, shared/concurrent writer profiles, generalized source sealing/provenance, authoritative effects/reconciliation, distributed takeover and stronger hostile-environment guarantees remain later capabilities. They require new qualification before being advertised.

This revision does not implement Broodling or Zeroshot, begin issue #9, create later implementation work, rerun issue #8, or change historical qualification records.

## 15. Conclusion

The V1 correctness boundary is intentionally structural:

> **One Attempt owns one dedicated disposable worktree. Only graph-authorized mutators change candidate source. Zeroshot orders those mutation intervals. Assurance roles are read-only, and containment prevents writers outside the authorized interval. Therefore an assurance/authority occurrence applies to the candidate state produced by the most recent preceding mutation. A later mutation creates a new state and requires fresh assurance before acceptance.**

Evidence sufficiency remains independently mandatory, but it is satisfied through the smallest graph-local checks, bindings and retained material needed by the Contract—not by converting V1 into a candidate-sealing/provenance platform.

Issue #8 already provides bounded W1/W2-containment/W5/W7 evidence on the recorded profile. Under v0.5's corrected W2 scope, no issue-#8 rerun is required. W3, W4 and W6 are the remaining G1-V1 qualification work; G1-V1 itself remains unpassed until their evidence is reviewed together.

## Source and decision register

- **[D05]** Project-owner correction request, 7 September 2026: replace v0.4 candidate-applicability requirement with the structural single-host V1 rule; separate evidence sufficiency; credit issue #8 W1/W5/W7 and host-containment evidence; regenerate only remaining G1-V1 obligations; preserve v0.4/evidence and do not begin #9.
- **[T04]** `docs/governing/broodling-target-responsibility-boundary-design-v0.4.md`, preserved unchanged.
- **[P04]** `docs/governing/broodling-implementation-dependency-plan-v0.4.md`, preserved unchanged.
- **[G04]** `docs/governing/g0-v0.4-review.md`, PASS for documentation completeness only; preserved unchanged.
- **[E08]** `qualification/v1-p1/issue-8-w1-w2-w5-w7.md` at commit `7931ac9acd70b8670dfcaa48c05982897766367d`; historical report verdict W1 PASS, W2 FAIL, W5 PASS, W7 PASS.
- **[M08]** `qualification/v1-p1/evidence/issue-8-run-record.json` at the same commit; retained machine evidence for the recorded profile.
- **[C08]** Issue #8 comments through the requalification comment for `7931ac9`; preserved as provenance and not rewritten by this governing correction.
- Earlier P0/P1 materials remain historical context exactly as referenced by v0.4/G0-v0.4; no old Q/G1 result is reclassified as a pass by this revision.
