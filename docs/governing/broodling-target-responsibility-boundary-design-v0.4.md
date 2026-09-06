# Broodling
## Target responsibility and boundary design

**Version:** 0.4  
**Date:** 6 September 2026  
**Document type:** Governing target architecture and responsibility design  
**Status:** Project-owner-directed simplified first execution profile; new profile qualification remains required. This revision is not a G1 pass or a production-readiness claim.  
**Supersedes:** v0.3 in full as the governing target. The v0.3 files, P0/G0 inventory, and P1 evidence remain unchanged as provenance.  
**Companion:** [Implementation and dependency plan v0.4](broodling-implementation-dependency-plan-v0.4.md).

> **Broodling owns one Work Unit's Contract, admission, current Attempt authority, candidate/evidence applicability, and final disposition. Zeroshot owns execution of one admitted run. V1 uses an exclusively owned disposable worktree, graph-ordered candidate mutation, controlled independent review, and no authoritative external effects. An abandoned Attempt contributes no candidate or derived semantic state to its replacement.**

## 0. Decision basis and scope of correction

The project owner's direction of 6 September 2026 deliberately narrows the first supported execution profile after P1 qualification. This document calls that profile **V1**. It is not a language, package, or release-version selection. The direction changes the initial capability boundary; it does not establish that the narrowed profile already works. [D04]

The governing basis is the repository's target v0.3, then its implementation plan v0.3, then the P0/G0 and completed P1 investigations in issues #3–#7. Current Zeroshot source is verification evidence, not authority to alter the product boundary. The inspected Broodling baseline is `a11f2bb085b565234287a7d753ff740a4f9d4285`. The original target and plan remain in `docs/baseline/inputs/`; their recorded hashes and Git blobs are preserved. [T03, P03, B0]

### 0.1 What changes, and what does not

| Classification | v0.4 disposition |
|---|---|
| **Removed from initial-profile requirements** | Salvaging an incomplete Attempt; recovering every completed occurrence before restarting; carrying its directives, evidence, candidate or acceptance forward; qualifying the stronger mutable-workspace source/evidence handoff independently of exclusive ownership; exhaustive automatic-context provenance for an unconstrained ambient profile as the price of independent review. These are not V1 implementation obligations. |
| **Capabilities deferred to later profiles** | Cross-Attempt candidate/semantic reuse and its completed-occurrence recovery/catch-up prerequisites; broader mutable/shared-workspace applicability schemes; exact trusted Git/GitHub effects and reconciliation; distributed takeover/fencing and stronger hostile-environment assurance. Deferral is not qualification or a commitment to a particular mechanism. |
| **Mandatory V1 protections** | Frozen entitled Contract; one current Attempt; exclusive disposable worktree; graph-authorized, ordered mutation; read-only assurance; independent fresh controlled review; no C1-to-C2 transfer of evidence or authority; fail-closed execution; sticky within-Attempt obligations; criterion-level final assessment; strict Broodling disposition; admission refusal for required effects. |
| **Unchanged architecture** | Zeroshot owns graph execution, typed outcomes, routing, sessions, stop/loss and run history. Designated authority occurrences can directly constitute semantic decisions without a second blessing. Broodling owns Work Unit/Contract authority and disposition. Cerebrate owns cross-Work-Unit selection and scheduling. |

“Removed” describes an obligation no longer imposed on V1. “Deferred” describes a possible future capability whose safety requirements must be re-established before that capability is advertised. V1 does not implement substitute recovery machinery under another name. v0.3 did not prescribe a large sealing service or full hostile-agent confinement; v0.4 narrows the handoff/context qualification requirements rather than claiming those mechanisms were mandated.

### 0.2 Evidence boundary

P0/G0 passed specification completeness, not execution qualification. The historical v0.3 G1-core verdict remains **BLOCKED**: Q2 and the bounded Q3 fixture passed; Q1, Q4, Q5 and Q6 did not. Q7/G1-effects remains independently **BLOCKED**. Closing issue #7 completed its review, not the gate. [B0, E3–E7]

P1 tested Zeroshot `d0909615d6ba3c179b58bce15a059f40400ec995`. Current upstream was read at `c5c17626674a53a0ae7e4584654480c2b1be2479`, one successor commit concerning target-admission feedback. The comparison does not modify the inspected SDK run models, local candidate/context composition or delivery implementation. It supplies no new V1 qualification. The plan preserves the exact historical builds and identifies new witnesses. [Z04]

## 1. System purpose and external boundary

Broodling handles exactly one explicit Work Unit per admission: one primary authoritative GitHub issue and one target repository. Referenced material can be admitted context; independent issues do not silently become additional executable units. Repeated submission resolves existing identity rather than creating competing authority.

```text
Explicit work reference
  → entitled, frozen Contract and Closability
  → admitted Attempt and dedicated disposable worktree
  → one Zeroshot run: implement / review / adjudicate / repair / final assessment
  → Broodling disposition for the exact retained candidate
```

Broodling identifies missing prerequisites but does not wait on dependencies, select alternate backlog work, allocate global budgets, or coordinate projects. Those remain Cerebrate territory. A future `/work-on` adapter is thin; it does not retain its own Contract builder, review loop, or publication procedure.

Authoritative Git/GitHub effect authority remains Broodling's general responsibility. **V1 executes no such effects**, including optional effects. Contracts requiring publication, commits as delivery, pushes, PRs, merges, issue closure, comments, labels, or other authoritative external mutation are not admissible in V1. Reading entitled inputs and host-local provisioning of disposable worktrees are not publication or work-reference closeout. Workspace setup does not grant agents authority to mutate shared repository refs, configuration, or remote state.

## 2. Responsibility architecture

These are logical ownership boundaries, not services, modules, databases, or process-count requirements.

| Responsibility | Owner and boundary |
|---|---|
| Work reference, source entitlement, Contract, Closability | Broodling; model extraction cannot entitle a source or amend authority. |
| Attempt admission, original starting state, exclusive worktree assignment | Broodling policy; use qualified host/runtime workspace mechanics, not a new workspace execution engine. |
| Graph and role semantics | Broodling authors the admitted protocol; Zeroshot admits and executes it. |
| Typed response validation, routing, loops, dispatch | Zeroshot; no second Broodling validator or scheduler. |
| Provider/session lifecycle, cancellation, terminalization, run history | Zeroshot; Broodling requests/observes, rather than operating sessions. |
| Candidate/evidence applicability and currentness | Broodling policy, realized by qualified worktree ownership and graph bindings/transitions in V1. |
| Review findings | Observations only. |
| Adjudication and final semantic assessment | Successful designated authority-processor occurrences, for their bound applicable context. |
| Work Unit disposition | Broodling; never a bare Zeroshot terminal label. |
| Future external-effect authorization and reconciliation | Broodling; trusted mechanics may be delegated only when their exact behavior fits. |

### 2.1 Authority by designated occurrence

An adjudication or final-assessment output is authoritative only when the Work Unit/Contract/Attempt were admitted, the immutable graph/profile designates its role, Zeroshot successfully completes that occurrence under its response contract, and the trusted bound Contract/candidate/evidence/predecessor context remains applicable. The runtime owns occurrence identity and execution facts. Output shape, a node name, or processor-asserted identifiers do not confer authority.

No extra Broodling transaction is required merely to bless the same payload. Broodling still determines whether it is current and whether the Work Unit has succeeded. An authoritative model judgment can be wrong; semantic quality remains empirical.

Within V1, adjudication relationships and outstanding directives are Attempt-local. Their graph execution stays in Zeroshot. Abandonment makes the entire Attempt ineligible for subsequent acceptance or reuse; no scan of its completed occurrences is required to make that decision. Historical run facts are not erased or retroactively declared never to have occurred.

## 3. Minimal durable semantic model

```text
Work Unit W
  Contract R1 (immutable) + original admitted starting state B1
    Attempt A1 → worktree T1 → Zeroshot run Z1
    Attempt A2 → new worktree T2 from B1 → new run Z2
  Contract R2 (externally authorized meaning change)
    new admission and starting-state selection → new Attempt/run
```

Work Unit, Contract revision, Attempt, run, node occurrence, provider session, candidate state and Git commit remain distinct identities.

| Conceptual record | V1 meaning |
|---|---|
| **WorkUnit / WorkReference** | Stable issue/repository identity, Contract/Attempt lineage, currentness and disposition. |
| **WorkContractRevision** | Immutable entitled inputs, criteria, scope, finite validation populations, assurance obligations, prerequisites and effect requirements; the required-effect set must be empty. |
| **Attempt** | One execution episode bound permanently to one revision, original admitted starting state, dedicated worktree, stable submission identity and one run. Completion or abandonment is durable. |
| **Candidate / SourceRevisionRef** | The candidate-controlled content of this Attempt at an identified graph-ordered state; not merely the worktree path or starting HEAD. Representation is open. |
| **AuthorityOccurrenceRef / OutstandingObligation** | Exact designated authority and applicable directives within the live Attempt. Canonical execution remains in Zeroshot; V1 needs no cross-Attempt obligation projection. |
| **EvidenceRecord** | Available observation tied to the Attempt, candidate interval, producing check, environment/population and raw material. Reference runtime facts where adequate. |
| **SemanticAcceptance / WorkUnitDisposition** | Applicable final assessment and durable justification for this Contract and final candidate, with the empty required-effect set explicit. |

Delivery intents/receipts and cross-Attempt recovery checkpoints are later-profile concepts, not mandatory empty V1 subsystems. Do not duplicate node results, graph progress, session state, telemetry, or RunLedger. A necessary retention copy preserves provenance; it does not create new authority.

## 4. Contract construction, Closability and admission

Broodling constructs a source-attributed Contract from entitled inputs. For each criterion, Closability establishes the concrete production/public boundary, finite evidence population, authorized validation seam, executable action, falsifying observation and available prerequisites. Model assistance does not decide source entitlement.

V1 additionally requires a feasible route wholly inside its qualified single-host, no-effect profile. A criterion requiring external publication to obtain evidence is unsupported, even when publication would be legitimate in a later profile. Do not delete the effect, narrow the criterion, or silently claim a prototype satisfies a publication obligation.

Before submission, persist the immutable revision and Attempt, the original admitted starting state, the exclusive worktree relationship and stable submission identity. Pin the actual starting content and admitted instructions sufficiently to rematerialize them; a moving issue, branch or HEAD is not that state. Source-selection format and storage remain open.

Live issue edits do not change an executing Contract. Changes to meaning, scope, population or authority require external authorization, a new revision and a new Attempt. Within unchanged meaning, eligible adjudication can resolve or supersede interpretations in the same Attempt. A missing required population member or authority gap requires handback, not silent amendment.

Ambiguous submission remains an identity problem even without recovery/reuse. Resolve the existing run or remain blocked; a submission conflict does not authorize another run or another Attempt.

## 5. V1 execution and abandon-and-restart

### 5.1 Worktree ownership

One Work Unit exclusively owns one dedicated disposable worktree for its current Attempt. Other Work Units use different worktrees. An abandoned worktree is retired, not reassigned or used as a replacement's starting point. A replacement receives a newly materialized worktree from the **original admitted starting state**, not from current repository HEAD or the abandoned candidate.

Only graph-authorized mutating executions may change candidate source. All mutations, including source-mutating validation or code generation, must occur in graph-ordered mutating intervals. Review, adjudication, final assessment and other assurance readers are read-only relative to candidate source. Read-only checks may produce observations or scratch output outside candidate source. Background writers must not outlive their authorized interval.

Separate worktree paths alone do not prove isolation from shared Git metadata, shared writable paths or leftover processes. The selected single-host profile must qualify those boundaries. This is a constrained supported-host contract, not a claim of hostile-agent confinement.

### 5.2 Abandonment

When an Attempt is stopped, loses its runtime, or otherwise cannot complete, V1 abandons that Attempt and its worktree. If execution continues, it is a new Attempt/run from the original admitted starting state.

```text
A1 cannot complete / stop accepted
  → durably make A1 ineligible for completion and reuse
  → request/observe Zeroshot stop or loss terminalization
  → establish that old execution cannot affect the replacement's candidate
  → retire T1; preserve only required diagnostic/administrative history
  → admit A2 with fresh T2 from original B1 and new run identity
```

Do not admit competing current authority while old execution remains unresolved. If the host/runtime cannot establish safe cessation or containment, remain blocked rather than invent takeover, lease, or recovery machinery. Do not delete a worktree while a runtime may still use it.

**Zero carryover:** no candidate edits, patches, semantic decisions, directives, review conclusions, evidence, validation results or acceptance from A1 enter A2. Do not smuggle them through caches, summaries, copied artifacts, automatic context or reused sessions. Frozen externally entitled Contract inputs and the original starting state remain available because they precede A1; they are not salvaged A1 products. New meaning requires a fresh externally authorized admission, not laundering abandoned output into authority.

Earlier A1 directives remain historical facts, not obligations fed to A2. A2 re-establishes all Contract obligations independently. Failure reasons and run references may be retained for human diagnosis but are not semantic inputs or evidence for replacement execution.

### 5.3 Completion and interruption races

A repair round is not a new Attempt. Stop/loss mechanics and durable terminal state remain Zeroshot's responsibility. A stop request's timing need not define the exact last runtime occurrence: once Broodling has abandoned the Attempt, no late result can restore its eligibility or win against the abandonment record.

V1 does not salvage partially recorded semantic completion. If interrupted finalization cannot be established safely from the already complete retained result/disposition, fail closed and abandon; do not reconstruct an incomplete run. An already durably completed Work Unit result remains readable. The plan must test this boundary, including a crash after an authority node or final result but before durable disposition.

## 6. Durable truth and the normal completion boundary

Zeroshot is authoritative for what ran and what it returned. Broodling is authoritative for the Contract, current Attempt, candidate applicability and disposition. The selected integration must support **normal successful completion** with sufficient provenance and retained evidence; removing failed-run recovery does not permit trusting arbitrary JSON marked `accepted`.

A narrow completion route may export the designated final assessor's result through the admitted graph's output bindings. Its provenance must be established from the admitted run and unambiguous graph occurrence, not from a model-supplied role or occurrence ID. The normal graph has a final assessor after convergence; no implementation or review output can substitute for it. Qualifying this route is new work, not a claim that `RunResult` exposes completed-occurrence history.

Broodling must retain enough final candidate material, evidence and assessment justification to explain a completed disposition after disposable workspace/session cleanup. It need not preserve every abandoned candidate or rebuild every intermediate occurrence. Required final material missing before completion prevents success; cleanup must not destroy the only retained basis for an already completed result.

V1 has no completed-occurrence recovery scanner, semantic catch-up watermark, graph replay, parallel result ledger or second authority-admission hop. Future cross-Attempt reuse would require its own supported recovery, currentness and retention contract before use.

## 7. Execution protocol and information boundaries

### 7.1 Minimal assurance graph

```text
IMPLEMENT (mutating)
  → REVIEW (independent, read-only)
  → ADJUDICATE (designated authority, read-only)
      ├─ applicable directives → REPAIR (mutating) → fresh REVIEW → ADJUDICATE …
      ├─ authority gap / unusable / exhausted bound → non-success
      └─ no unresolved blockers → FINAL SEMANTIC ASSESSOR (authority, read-only)
```

Zeroshot owns ordering and bounded convergence. Broodling does not dispatch the next node from an observer callback. No specialist, reset, refactoring, universal TDD or delivery node is required.

| Role | Allowed semantic inputs | Output / authority |
|---|---|---|
| Implementer | Frozen Contract, admitted repository instructions, starting candidate, validation instructions | Candidate changes and observations; no semantic authority. |
| Independent reviewer | Frozen Contract, exact current source/comparison base, selected raw evidence, governed review instructions | Coverage and evidenced findings; no repair/acceptance authority. |
| Adjudicator | Contract, relevant current source/evidence, raw findings, scoped applicable prior decisions and open obligations from this Attempt | Classifications, directives, resolutions/supersessions or authority questions; designated authority. |
| Repair worker | Contract, current source, applicable adjudicated directives and correction obligations | Changes, observations and accounting; no authority to discharge its own directives. |
| Final assessor | Complete Contract, final candidate, eligible evidence, applicable interpretations and unresolved-obligation state | Criterion-level sufficiency/gaps; designated semantic authority, not Work Unit disposition. |

Raw or rejected findings do not become repair authority. Within the same Attempt, an accepted obligation stays open until an eligible authority occurrence explicitly resolves or supersedes it. A fresh empty findings list, worker assertion, inconsistent signal or exhausted loop cannot clear it. Candidate advancement does not itself discharge a directive.

Prefer a canonical route-affecting representation; otherwise qualify deterministic consistency in the graph/runtime. P1's one-directive, three-round enum fixture is evidence of a bounded mechanism, not a required production directive schema or proof for arbitrary directive sets. [E4, E7]

### 7.2 Fail closed

Crash, timeout, refusal, malformed/missing output, failed applicability or missing required raw evidence cannot become initialized empty success. Use explicit unusable routes or admitted bounded recovery inside the graph; an Attempt that cannot complete is abandoned. A successfully produced expected negative/RED observation is evidence, not an execution crash. A clean adjudication route permits final assessment; it does not replace it.

### 7.3 Controlled independent reviewer profile

Every reviewer execution uses a fresh execution/session through Zeroshot's session mechanics. The selected profile deliberately controls automatic prior-role and user-level context: repository/ancestor instructions, user/provider-home configuration, skills, tools and automatic transcript/history inputs that can contaminate review must be disabled, isolated or explicitly admitted in a small documented configuration.

Review must not automatically receive worker narrative/private reasoning, earlier reviewer conclusions, adjudication deliberation or repair rationale. Candidate governing text is assessment data under frozen authority, not newly entitled instructions. Legitimate source, comparison material and raw evidence must remain usable.

Qualify the actual provider/harness with positive and contamination controls. Freshness alone did not prevent P1's repository/skill canaries from entering review. Do not equate read-only filesystem access with independent semantic context. Conversely, V1 does not demand an exhaustive transcript/provenance proof of an arbitrary hostile environment. Record the finite selected controls, relevant versions and tested limits; reject configurations outside that qualified boundary. No Broodling session manager is introduced. [E5]

## 8. Candidate identity, evidence and minimal applicability protection

V1 protects candidates primarily by **exclusive ownership and graph-ordered transitions**, not by recreating v0.3's stronger sealing profile.

A worktree is a location, not an eternal candidate identity. During each read-only assurance interval, the candidate is stable. An authorized mutating interval advances it to a new candidate state; evidence or authority for **C1 cannot establish C2**. After repair, renew the necessary checks/review/assessment. Trusted Contract, Attempt and predecessor bindings cannot be replaced by identifiers emitted by a processor.

Use the smallest graph-visible candidate/evidence relationships sufficient for those rules. A stable starting state, known mutation boundaries, read-only consumers and current-interval evidence may avoid per-node source sealing and an asynchronous external handoff altogether. The actual mechanism remains a qualification question. A claimed digest or delayed observer does not supply missing protection. [E5]

Candidate-controlled source includes intentional untracked source and relevant generated/tracked content according to an explicit selected policy. Do not assume all generated files are irrelevant. Source, execution environment and observations are distinct: equal bytes do not make a wrong host, mode, population or artifact acceptable evidence.

Raw material needed for a criterion must be available when assessed and retained as needed for final disposition. Typed inline observations or a small retained artifact set may suffice; no generic content-addressed archive or sealing service is prescribed. A pathname to missing bytes does not suffice. Any required check must constrain graph progression or final disposition at the point it matters, rather than relying on a racing observer.

Relevant failures and contradictory observations within the Attempt remain accounted for. A later green result does not erase an unexplained relevant failure. Changed interpretations can invalidate sufficiency without changing bytes. Abandonment discards the whole Attempt's semantic/evidence eligibility, not selected inconvenient observations while retaining its favorable candidate or acceptance.

## 9. Semantic assurance

Independent review challenges whether evidence distinguishes a materially incorrect behavior, including the historical wrong-stack/count-only blind spot. Final assessment examines every Contract criterion, not reviewer silence. Evaluate counterexamples and valid controls separately from deterministic runtime mechanics. The P0 synthetic WS-01 case remains a synthetic analogue, not a recovered historical experiment. [B0]

Adjudication filters findings into Contract-backed defects, governed standards violations, rejected suggestions or authority gaps. Bounded reconsideration may explicitly supersede an interpretation under unchanged obligations. It cannot amend the Contract or silently drop directives.

Required correction checks, negative/population-boundary checks and reconsideration of demonstrated-insufficient premises remain mandatory. An unresolved required correction blocks acceptance. Repair must address the obligation rather than preserve a disproven mechanism through additional scaffolding. No historical prose ritual or dedicated reset node is required.

Governing-material changes receive actual before/after semantic assessment against frozen prior authority. Edited text cannot authorize itself. V1 may produce a local candidate containing an authorized governing edit; it does not publish it.

A positive final-assessment occurrence establishes SemanticAcceptance only while its Contract, candidate, evidence and interpretations remain applicable. Retain distinct adjudication and final assessment: they answer correction legitimacy and complete semantic sufficiency respectively.

## 10. Strict success and future authoritative effects

The general success rule is unchanged:

```text
SUCCEEDED
= current applicable SemanticAcceptance
  AND every Contract-required authoritative effect completed
  AND every such required effect verified
```

For admissible V1 Contracts the required-effect set is empty, so the last two terms are **vacuously satisfied**, not waived or approximated. Broodling must still establish current applicable acceptance and a justified disposition for the exact retained candidate. Provider completion, node completion, run success, clean review and clean adjudication remain distinct from Work Unit success.

No Zeroshot `GitDelivery` binding is used in V1. Agent shell/workspace access also grants no commit, push, PR, issue or other authoritative effect authority. The profile must prevent ordinary execution from bypassing its no-effect boundary; an observed unauthorized effect is a boundary failure, not success or a retroactively authorized receipt.

For later effect-capable profiles, the preserved rules are:

- Broodling authorizes the exact source/payload/target and prerequisites under the applicable Contract before dispatch; a trusted executor may not bundle extra mutation.
- Effects may precede acceptance when their own prerequisites permit, including evidence-producing publication, or follow it when acceptance is a prerequisite. There is no universal publication phase or PR/merge requirement.
- Verified readback discharges only the actual authorized obligation. A C1 receipt cannot establish C2 through a reused URL/ref; effect-time transformations require explicit verified source correspondence.
- Lost acknowledgements require exact-intent reconciliation independent of a coding run. Readback cannot retroactively authorize mutation. Conflicting active/uncertain effects and stale undispatched authority require fencing before conflicting replacement work; compensation also requires authority.

These are conditions on enabling later capabilities, not V1 executor, receipt-store or reconciliation implementation work. P1's bundled-delivery counterexample remains relevant and unresolved. [E6]

## 11. External collaboration and product interface

The interface remains semantic: submit a reference; observe Contract/Attempt lineage and disposition with referenced Zeroshot status; provide external authority for a new revision; request stop; request a fresh retry after safe termination; consume the justified result. Retry never means resume an abandoned workspace.

A human may inspect diagnostic history without keeping a model session alive. Such history is not automatically replacement input. Product language, framework, CLI/service packaging, detailed non-success taxonomy and presentation remain unresolved.

## 12. Historical R1–R19 responsibility disposition

This preserves protections, not nineteen modules or a migration plan.

| Responsibility | v0.4 disposition |
|---|---|
| R1 Admission | One reference/repository; repeat identity; no backlog selection. |
| R2 Authority/trust/mutations | Broodling Contract/disposition; designated in-run decisions; graph-only candidate mutation; no second blessing. |
| R3 Snapshot/amendment | Frozen entitled authority; external revision for meaning change. |
| R4 Provenance | Runtime facts by reference; finite controlled reviewer profile, not exhaustive hostile provenance. |
| R5 Closability | Finite populations, falsifiers, prerequisites and V1 capability eligibility. |
| R6 Freeze/custody/resume | Original starting state and final-result custody; V1 abandons instead of resuming or catching up. |
| R7 Scoped delegation | Explicit role inputs and qualified runtime session mechanics. |
| R8 TDD/coherence | Honor admitted methodology; no universal TDD lifecycle. |
| R9 Evidence/reuse | Same-Attempt applicability and contradictions; no abandoned-Attempt reuse. |
| R10 Readiness/same-mechanism | Role methodology and sensitivity fixtures, not another phase. |
| R11 Review-index | Frozen Contract/current candidate/raw-evidence bindings, not legacy packaging. |
| R12 Convergence | Zeroshot bounded graph; no Broodling review scheduler. |
| R13 Adjudication/sticky rulings | Designated authority and explicit resolution within Attempt; input firewalls preserved. |
| R14 Correction self-check | Required negative/population/claimed-versus-observed checks block acceptance. |
| R15 Mechanism reset | Reconsider disproven premises without a mandatory reset node. |
| R16 Governing remediation | Before/after semantics under prior authority; no self-authorization. |
| R17 Re-adjudication | Bounded explicit supersession within unchanged Contract and same Attempt. |
| R18 Closure/unresolved work | Final criterion assessment plus Broodling disposition; durable diagnostic handback. |
| R19 GitHub closeout | General Broodling ownership preserved; all effect execution/reconciliation deferred beyond V1. |

## 13. Invariant and evidence map

The unchanged P0 inventory's I01–I25 identifiers remain provenance references, not automatically passing V1 tests. The companion plan supplies new qualification witnesses and consuming gates. D means deterministic mechanics; S means semantic evaluation.

| Existing invariant(s) | V1 requirement and discriminating test |
|---|---|
| I01; H01/H02 | D+S: entitled frozen admission, correct cardinality and finite Closability; reject live drift, duplicate authority and required effects. |
| I02/I03 | D: only designated successful occurrences route as authority; ordinary lookalikes fail; no blessing callback needed. |
| I04/I05 | **Original carryover/recovery capability deferred.** D replacement test: stop/crash after adjudication; fresh Attempt from original state receives none of the old candidate/directive/evidence/acceptance, including late results. |
| I06 | Effect-conflict machinery deferred; D V1 rejects effect paths and unresolved old-runtime interference. |
| I07/I08/I09 | D: unusable outcomes fail closed; empty findings do not clear open obligations; repair receives only applicable directives. |
| I10 | D with real provider: fresh controlled reviewer excludes prior-role/user-level canaries while legitimate source/evidence remains available. |
| I11/I12 | D+S: clean review cannot hide missing criterion evidence; wrong-stack/count-only counterexample and valid controls. |
| I13 | D: final result retains original provenance; no parallel runtime truth or abandoned-history reconstruction. |
| I14/I15 | D+S: identify candidate versus environment; C1→C2 renews applicability; read-only phase/foreign-worktree writes and forged context cannot establish acceptance. |
| I16/I17/I18 | D+S: preserve same-Attempt contradictions; require correction checks and relevant available raw evidence; reject wrong population/host/mode. |
| I19 | D+S: governing text assessed against frozen prior authority, never self-authorizing. |
| I20/I21 | D+S: general conjunction retained; V1 requires actual final acceptance and empty required effects, never a run label or publication substitute. Effect-bearing executions deferred. |
| I22/I24 | Exact receipt correspondence and reconciliation deferred; no V1 receipt/reconciliation implementation. |
| I23/I25 | D: no workspace-derived effect authority, no GitDelivery or mandatory publication; effect-requiring Contracts fail admission rather than being rewritten. |
| H03/H04 | D+S: real scoped repair, admitted methodology, bounded reconsideration and diagnostic handback; no hidden prior-Attempt continuity. |

Additional V1 witnesses must establish distinct Work Unit worktrees, graph-ordered mutation, restart from the original state despite repository drift, loss/stop isolation, normal final-output provenance, final-material retention and the no-effect boundary. These properties are not established merely by rescoping old fixtures.

## 14. Deferred decisions and capability boundaries

Product language/framework, physical semantic storage/schema, external SDK/process packaging versus justified embedding, candidate/reference/materialization formats, artifact retention format, prompts/models/reviewer population, detailed non-success taxonomy and interface packaging remain open. Evaluate the official external SDK/sidecar first; a Python qualification harness is not a product-language decision.

Fresh reviewer execution and controlled automatic context are **not** deferred in V1. Only their narrow supported configuration remains to be qualified. Specialist roles and session reuse optimizations require evidence rather than anticipatory architecture.

Cross-Attempt reuse, completed-occurrence recovery/catch-up, stronger source/evidence sealing, exact effects/reconciliation, distributed recovery and hostile-environment assurance remain later-profile work. No future profile may advertise reuse or effects by borrowing V1's narrower gate results.

## 15. Conclusion

V1 trades salvage and effect breadth for a small correctness boundary: one immutable Contract, one current Attempt/run, one exclusively owned disposable worktree, graph-ordered candidate transitions, independent controlled review, and a justified no-effect disposition. Failed/stopped work is abandoned wholesale and restarted from original admission, not recovered into new authority.

This correction removes specific v0.3 initial blockers by removing the capabilities that required them. It does not make old failed witnesses pass. The new G1-V1 gate must qualify the remaining narrowed assumptions before dependent product work proceeds.

## Source and decision register

- **[D04]** Project-owner revision direction, 6 September 2026, request timestamp `2026-09-06T20:19:51Z`: deliberate single-host disposable-worktree/no-reuse/no-effect profile, narrow independent review, retained architecture and strict success, complete replacement documents without implementation.
- **[T03]** [Unchanged target v0.3](../baseline/inputs/broodling-target-responsibility-boundary-design-v0.3.md). SHA-256 `58599595020680880c334e85274160421943ca884deb76595ff193aa1af1c1d6`; Git blob `f9ac08f7884862a9200279ea73427bc6bc0061bb`.
- **[P03]** [Unchanged plan v0.3](../baseline/inputs/broodling-implementation-dependency-plan-v0.3.md). SHA-256 `31b35558dc0576d133dc623ed0b5eece3f45ec6ba1b98459d9d13c591f928a04`; Git blob `ebd5455f0ca0b415fff8559fcf9bbbefe10e9acf`.
- **[B0]** [P0/G0 inventory](../baseline/p0-g0-inventory.md), originating commit `5f017e39dfa6a84a4c4f27da90e70174e0817cd6`: I01–I25, H01–H04, R1–R19 and explicitly synthetic WS-01. Historical experiments are inherited evidence, not newly reproduced here.
- **[E3]** [Q1/Q2/Q6 report](../../qualification/p1/issue-3-q1-q2-q6.md), origin `f144e15b15bd85cd788aa21a1c17d973914cb426`, and its linked machine record; issue #3 completion comment.
- **[E4]** [Q3 report](../../qualification/p1/issue-4-q3.md), origin `0b1a3b24d25d1467057593ca614b09a0cbce9766`, and its linked machine record; issue #4 completion comment.
- **[E5]** [Q4/Q5 report](../../qualification/p1/issue-5-q4-q5.md), origin `03bf963eb7d3c59ffa52c4aee388f1470f444f73`, and its linked machine record; issue #5 completion comment.
- **[E6]** [Q7 report](../../qualification/p1/issue-6-q7.md), origin `3a486f270f5fdc1f029d6f255c1ede62b9378f03`, and its linked machine record; issue #6 completion comment.
- **[E7]** [Completed G1-core review](../../qualification/p1/issue-7-g1-core.md), origin `a11f2bb085b565234287a7d753ff740a4f9d4285`, and issue #7's completion record. This is the authoritative consolidation of the historical evidence verdicts, not a V1 pass.
- **[Z04]** [Current upstream comparison](https://github.com/the-open-engine/zeroshot/compare/d0909615d6ba3c179b58bce15a059f40400ec995...c5c17626674a53a0ae7e4584654480c2b1be2479) and [SDK run models at the read revision](https://github.com/the-open-engine/zeroshot/blob/c5c17626674a53a0ae7e4584654480c2b1be2479/sdks/python/src/zeroshot/runs.py): `RunResult` exposes run identity, success/failure and final graph output, not exact settled-occurrence history. Other integration findings are attributed to the pinned P1 reports. No new build or runtime/provider qualification was performed for this revision.
