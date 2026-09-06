# Broodling
## Target responsibility and boundary design

**Version:** 0.3  
**Date:** 5 September 2026  
**Document type:** Target architecture and responsibility design  
**Status:** Accepted target after independent architecture and historical-protection regression reviews; ready for implementation/dependency planning, not a claim of production readiness  
**Supersedes:** Version 0.2 in full; this is a complete replacement, not an addendum or implementation plan

> **Broodling owns the meaning, admission, cross-run semantic authority, effect authority, and verified disposition of one software Work Unit. Zeroshot owns the execution of an admitted Attempt, including its graph, typed node I/O, within-run routing, sessions, runtime identities, and durable execution history. Explicitly designated authority-processor occurrences inside that admitted Zeroshot run may directly constitute Broodling semantic decisions. Cerebrate will own decisions across Work Units.**

---

## 0. Decision basis, correction scope, and reading conventions

Version 0.3 is a targeted correction of version 0.2. It does **not** reopen the global Broodling/Cerebrate boundary, the immutable Contract-revision/Attempt relationship, the strict success predicate, or Broodling's ownership of authoritative external effects. It corrects two assumptions that made Broodling unnecessarily duplicate Zeroshot:

1. version 0.2 treated model-backed adjudication and closure output as an execution fact that required a second Broodling validation/admission hop merely to become authoritative; and
2. the implementation direction was beginning to duplicate one-run concerns—typed result validation, routing, execution bookkeeping, session/lifecycle control, and runtime observation—that current Zeroshot Rust v2 already owns.

The corrected rule is:

> **Authority derives from the admitted role and exact runtime occurrence, not from a second ceremonial "blessing" of the same output.**

An ordinary worker or reviewer output remains an observation. A successfully completed output from an explicitly designated **authority processor** in the immutable admitted graph may itself constitute a Broodling semantic decision, provided the occurrence belongs to the current admitted Attempt and satisfies the graph/runtime output contract. The canonical decision payload may remain in Zeroshot's durable execution history; Broodling stores the Work-Unit-level applicability, supersession, outstanding-obligation, and disposition relationships that Zeroshot does not own. [D4; Z2–Z6]

This does **not** mean that a model judgment becomes semantically correct merely because it is authoritative. A model-backed authority processor can still make a bad judgment. Semantic quality remains an empirical assurance problem, just as it was in version 0.2. The change removes duplicated authority plumbing; it does not claim deterministic proof of semantic correctness.

The previously corrected external-effect rule remains unchanged:

> **Broodling may perform an authoritative external effect whenever that effect is authorized by the Attempt's immutable Contract revision and its applicable prerequisites are satisfied.**

Such an effect may produce evidence subsequently consumed by semantic acceptance. Completing an effect never itself establishes semantic acceptance or Work Unit success. No universal execution → acceptance → publication sequence, mandatory publication phase, or preparatory/final effect taxonomy is imposed. [D3]

Three kinds of statement are distinguished:

| Kind | Meaning in this document |
|---|---|
| **Confirmed constraint** | A boundary or policy explicitly settled by the project owner. |
| **Target rule** | The architectural consequence selected by this target, including the v0.3 simplifications. |
| **Evidence / open detail** | A source-backed capability observation, limitation, or deliberately deferred implementation choice. |

### 0.1 Evidence boundary

The historical Zeroshot experiments remain evidence for information-flow, identity, review, and failure-mode requirements. Version 0.3 additionally uses bounded source inspection of current Zeroshot Rust v2 at `d0909615d6ba3c179b58bce15a059f40400ec995`, the same current revision inspected by the v0.2 implementation plan. This inspection establishes available interfaces and explicit ownership statements in that source; it is not a claim that the proposed Broodling integration has been compiled or tested.

Current source inspection establishes, in particular:

- Zeroshot's graph language already supplies typed step/verifier nodes, sequencing, choices, parallelism, bounded loops/maps, output bindings, signals, and explicit success/failure nodes. [Z2]
- native-v2 admission verifies GraphSpec/runtime binding compatibility before execution. [Z3]
- native-v2 validates structured node outcomes against the graph-derived response contract before a successful node completion is accepted. [Z4]
- RunLedger durably records admitted run identity, exact execution occurrences, node inputs/outcomes, terminal state, and usage; it explicitly does **not** own fencing, proofs, effect receipts, retries, or controller takeover. [Z5]
- the supervisor owns within-run reduction/routing, dispatch, timeout/cancellation, force-stop, and terminal runtime-loss behavior. [Z6]
- the official Python SDK can submit exact custom GraphSpec/RuntimePlan values to a bundled Rust sidecar, so use of Zeroshot does not by itself require Broodling to be a Rust application or to link Zeroshot as an in-process crate. [Z1]
- current native runtime bindings expose Agent and GitDelivery lanes, and stock delivery bundles operations more narrowly than Broodling's target effect model. [Z3, Z8]

These observations support the push-down in this version. They do not prove that every desired Broodling trusted effect can already be expressed through the external SDK.

---

## 1. System purpose and external boundary

### 1.1 Broodling is a product, not a renamed skill

Broodling executes **exactly one Work Unit per admission**. Its product-level responsibility is:

```text
Explicit work reference
    → trusted Work Contract construction
    → Closability and Attempt admission
    → one admitted Zeroshot run under one immutable Contract revision
         implementation / validation / review / adjudication / repair
         evidence collection
         final semantic assessment
         Contract-authorized effects wherever their prerequisites permit
    → Broodling Work Unit disposition from
         current semantic acceptance
         + verified required effects
```

The activities inside the admitted run are not a universal chronological sequence. Publication may precede evidence needed for acceptance. An effect may also occur after semantic acceptance when its own authorization requires that ordering. Work Unit success is a conjunction of semantic truth and verified required effects, not the name of the last Zeroshot node. [D3]

The current `skills/personal/work-on` package is not the target execution container. A future `/work-on` command may remain a thin invocation/status adapter. It must not retain a parallel Contract builder, review loop, semantic state machine, or publication procedure.

### 1.2 Broodling–Cerebrate boundary

| Broodling owns, within one Work Unit | Cerebrate territory, outside Broodling |
|---|---|
| Resolve the supplied work reference and repository | Select the next backlog item |
| Construct and admit immutable Contract revisions | Wait for issue dependencies and choose when to dispatch |
| Decide whether the Work Unit is closable now | Schedule work across projects |
| Admit one current Attempt under a Contract revision | Allocate global token/resource budgets |
| Define the allowed assurance/effect semantics for that unit | Decide concurrency across Work Units |
| Correlate the Attempt to its Zeroshot run | Prioritize and coordinate multiple Work Units |
| Preserve cross-Attempt obligations, source/evidence applicability, effects, and disposition | Global dependency resolution |

Broodling may identify a missing prerequisite and return that fact. It does not wait on the backlog or select alternate work.

### 1.3 Work-reference cardinality

A Work Unit has one primary authoritative work reference and one target repository. Initially, the primary reference is a GitHub issue. Referenced specifications or related issues can supply explicitly admitted context; they do not become additional independently executable Work Units inside Broodling.

Repeated submission must resolve existing Work Unit identity rather than accidentally establish competing authority. Broodling neither silently combines independent issues nor decomposes one issue into multiple Work Units.

### 1.4 Delivery remains inside Broodling authority

Broodling owns authorization and verification of authoritative Git/GitHub effects: commits, pushes, branch publication, PR creation/update, merge where supported, issue closure, comments, labels, and work-reference relationships.

A trusted executor may perform those mechanics:
- inside the Zeroshot execution environment,
- through a generic Zeroshot trusted-effect capability if one is suitable,
- or independently of a live Zeroshot run for reconciliation.

Physical placement does not transfer authority. The Contract and Broodling's current Work Unit state decide whether the effect is authorized.

Effect ownership does not mean every Work Unit performs every effect. Required and permitted effects are Contract-specific.

---

## 2. Responsibility architecture

The following are logical ownership boundaries, not requirements for separate services, repositories, processes, or databases.

```text
Human / thin adapter / eventual Cerebrate
                   │
                   ▼
┌──────────────────────── BROODLING ────────────────────────┐
│                                                           │
│ Work Unit + trusted Contract authority                     │
│   ├─ reference/source entitlement                          │
│   ├─ immutable Contract revisions                          │
│   ├─ Closability                                           │
│   └─ Attempt admission / current-authority fencing          │
│                                                           │
│ Cross-run semantic authority                               │
│   ├─ authority-occurrence applicability/supersession       │
│   ├─ outstanding obligations across Attempts               │
│   ├─ source/evidence applicability                         │
│   ├─ effect authorization/intents/receipts                 │
│   └─ Work Unit disposition                                 │
│                         │                                  │
└─────────────────────────┼──────────────────────────────────┘
                          │ admitted Attempt
                          ▼
┌──────────────────────── ZEROSHOT ─────────────────────────┐
│ one admitted run                                           │
│                                                           │
│ GraphSpec + typed bindings + fail-closed control flow       │
│   implementation                                            │
│      ↓                                                     │
│   review(s)                                                │
│      ↓                                                     │
│   designated adjudicator  ──► repair loop when needed      │
│      ↓                                                     │
│   designated final semantic assessor                       │
│                                                           │
│ sessions · provider lifecycle · dispatch · timeout/stop     │
│ exact execution occurrences · typed outcomes · RunLedger    │
└─────────────────────────┬──────────────────────────────────┘
                          │
             Contract-authorized trusted effects
                          │
                          ▼
                     Git / GitHub
```

### 2.1 Ownership table

| Responsibility | Authoritative owner | Delegated mechanics / boundary |
|---|---|---|
| Reference resolution and trusted-source entitlement | Broodling | GitHub reads/source capture |
| Contract construction and Closability | Broodling | Model analysis may assist; cannot entitle sources or amend authority |
| Contract revision / Attempt identity | Broodling | Zeroshot run is correlated, not substituted |
| Graph topology and role semantics | Broodling-authored protocol | Zeroshot admits, verifies, and executes GraphSpec |
| Typed node input/output validation | Zeroshot | Broodling defines the semantic schema/role meaning; no duplicate result-shape validator merely to re-bless output |
| Within-run routing, loops, parallelism, retry structure | Zeroshot | Broodling selects bounded graph semantics; no second scheduler |
| Provider sessions, process lifecycle, timeout/cancellation | Zeroshot | Broodling may request stop; does not manage model sessions itself |
| Exact node execution identity and durable run history | Zeroshot | Broodling references runtime occurrences |
| Review findings | Zeroshot execution outcome | Findings are observations, not correction authority |
| Adjudication decision | Designated authority-processor occurrence | Broodling defines which occurrence/role has authority and retains cross-run applicability/supersession |
| Final semantic assessment | Designated authority-processor occurrence | Broodling determines whether that assessment is current/applicable |
| Physical workspace execution | Zeroshot/runtime host where supported | Broodling still defines semantic source identity and evidence applicability |
| Source identity/evidence applicability | Broodling | Runtime source/execution facts are inputs, not the complete semantic policy |
| Effect authorization and reconciliation | Broodling | Trusted mechanics may be Zeroshot-hosted or external |
| Work Unit `SUCCEEDED` disposition | Broodling | Never identical to a bare Zeroshot terminal result |

### 2.2 The push-down rule

A useful default for later detailed design is:

> **Facts whose lifetime and meaning are confined to one Zeroshot run should remain Zeroshot facts. Facts whose meaning spans Contract revisions, Attempts, source/evidence applicability, or external effects remain Broodling facts.**

Examples:

```text
"execution 17 ran reviewer node with input X and returned Y"
    → Zeroshot

"execution 17 is the authoritative adjudication for Contract R2/source S4"
    → Broodling relationship over a Zeroshot occurrence

"directive from execution 17 remains outstanding in replacement Attempt A5"
    → Broodling

"run Z9 is force-stopped"
    → Zeroshot

"Attempt A4 is no longer current and A5 may now be admitted"
    → Broodling

"push intent P3 was authorized and remote state is uncertain"
    → Broodling
```

This rule is a design heuristic, not a prohibition on caching/copying bytes for retention. Broodling may materialize a referenced outcome for durable custody, but duplication does not confer authority and must not create two competing semantic truths.

### 2.3 Authority by designated processor occurrence

An **authority processor** is a semantic role whose output can change Broodling semantic authority. Initially this includes:

- adjudication: converts raw findings into accepted/rejected findings, repair directives, or authority questions;
- final semantic assessment: decides criterion-level semantic sufficiency/acceptance for the candidate under the frozen Contract.

A processor occurrence is authoritative only when all of the following hold:

1. the Work Unit and Contract revision were admitted by Broodling;
2. the Attempt was current when its Zeroshot run was admitted and has not been replaced while still capable of producing usable authority;
3. the immutable admitted graph/profile designates that exact role/occurrence class as authority-bearing;
4. Zeroshot records the exact execution occurrence and its bound input;
5. the execution completes successfully with an outcome satisfying the admitted graph/runtime response contract;
6. any Broodling cross-run applicability prerequisites—such as current source, relevant prior decision state, or required evidence selection—remain satisfied.

Applicability-critical identities and predecessor state come from trusted admitted inputs or trusted Broodling source/evidence/decision records. An authority processor cannot make its own output applicable to a different source, Contract, evidence set, or predecessor state merely by asserting identifiers in that output.

No second "admit this exact same payload as a decision" hop is required solely to make the result authoritative.

A reviewer, implementer, repair worker, validation worker, or arbitrary node does not gain authority because it can emit a similarly shaped object.

---

## 3. Durable semantic model

### 3.1 Identity hierarchy

```text
Work Unit W
│
├── Contract Revision R1 — immutable
│   ├── Attempt A1 — permanently bound to R1
│   │   └── Zeroshot Run Z1
│   │       ├── ordinary execution occurrences
│   │       └── authority-processor occurrences
│   │
│   └── Attempt A2 — same Contract, replacement execution
│       └── Zeroshot Run Z2
│
├── Contract Revision R2 — externally authorized meaning change
│   └── Attempt A3
│       └── Zeroshot Run Z3
│
└── Broodling cross-run semantic history
    ├── applicability/supersession of authority occurrences
    ├── outstanding directives/obligations
    ├── source/evidence applicability
    ├── delivery intents/receipts
    └── Work Unit disposition
```

**Work Unit, Contract revision, Attempt, Zeroshot run, node execution, provider session, source revision, and Git commit are distinct identities.**

### 3.2 Principal records

These are conceptual contracts, not final tables.

| Record | Meaning |
|---|---|
| **WorkReference / WorkUnit** | Stable unit identity, primary issue/repository identity, revision/Attempt lineage, unresolved obligations, effects, and disposition. |
| **WorkContractRevision** | Immutable admitted meaning: entitled authoritative inputs, criteria, scope/exclusions, finite validation surfaces, assurance obligations, required effects, permitted effect scope, prerequisites, and verification requirements. |
| **Attempt** | One admitted execution episode permanently bound to one Contract revision and correlated to one Zeroshot run. Records starting assumptions and any Broodling-specific execution provenance not already durable in the run. |
| **AuthorityOccurrenceRef** | Reference to an exact Zeroshot execution occurrence whose admitted role is authority-bearing, plus its semantic role, Contract/source/evidence applicability, and supersession/currentness state. The canonical processor payload may remain in RunLedger. |
| **OutstandingObligation** | A still-applicable accepted directive or other semantic obligation that must survive round-local graph containers and, when needed, replacement Attempts. |
| **SourceRevisionRef** | Exact candidate-controlled source identity under a Broodling-owned source-selection scheme, with recoverable bytes or reproducible materialization. |
| **EvidenceRecord** | Recoverable observation with source, producing execution/check identity, environment/external inputs, raw artifacts, and observed result. Where Zeroshot already stores the exact execution outcome, this record may reference rather than duplicate it. |
| **SemanticAcceptance** | Current applicability of a positive final semantic-assessment occurrence to one Contract revision and candidate result, including the evidence and interpretation relationships needed to justify it. It is not Work Unit success. |
| **DeliveryIntent / DeliveryReceipt** | Authorized external effect and verified observation of its completion, including exact target/payload/source relationship and prerequisites. |
| **WorkUnitDisposition** | Durable `SUCCEEDED` or non-success conclusion with complete semantic/effect justification. |

### 3.3 What no longer needs a separate Broodling record merely for duplication

Version 0.3 does not require a second Broodling copy of:

- every node invocation;
- every node result;
- every review report payload;
- every authority-processor payload;
- run progress, logs, usage, or terminal state;
- within-run loop counters or graph state;
- provider session identity merely to operate the run.

Broodling may retain/copy data for custody if RunLedger retention is insufficient, but the copied representation is not a second authority transformation.

### 3.4 Execution profile

The exact GraphSpec, RuntimePlan, resolved native source, node inputs/outcomes, and run status should be referenced from Zeroshot where its durable contract already covers them. Broodling's execution provenance need only add information Zeroshot does not reliably capture but which matters to semantic assurance—for example:

- relevant ambient system/harness instructions;
- loaded skills or uncontrolled automatic context that could affect a blind role;
- Broodling policy/profile version selecting which roles are authority-bearing;
- source-selection/evidence policy version.

This avoids a parallel `ExecutionProfile` that simply serializes Zeroshot's own admitted run again.

---

## 4. Contract construction, Closability, and authority changes

### 4.1 Admission

Broodling accepts an explicit reference and constructs a source-attributed proposed Contract. Trusted-source selection and semantic extraction remain Broodling responsibilities; hashing or model extraction does not establish entitlement.

Before implementation starts, Closability must establish, for every criterion:

- the concrete production/public boundary and finite evidence population;
- an existing or explicitly authorized-to-create validation seam;
- an executable validation action and falsifying observation;
- available prerequisites;
- a credible authorized route to complete semantic and required effect obligations.

For publication-dependent evidence, Closability must establish a permitted route to the relevant publication, observation, and later semantic assessment. Final acceptance need not exist first. Missing effect authority or an impossible prerequisite cycle prevents admission.

Broodling durably records the Contract revision and admitted Attempt **before** authorizing the Zeroshot run.

### 4.2 An Attempt cannot amend its own Contract

Each Attempt is permanently bound to one immutable Contract revision. Workers, reviewers, adjudicators, and final assessors cannot refetch live issue discussion and substitute it for the frozen authority.

| Situation | Boundary rule |
|---|---|
| Reproduced defect violates an existing criterion | Adjudication/repair within the same revision |
| New evidence justifies reconsidering an earlier interpretation without changing the obligation/population | Bounded re-adjudication may supersede the prior authority occurrence |
| Obligation, scope, required population, or effect authority changes | External authority → new Contract revision → new Attempt |
| Runtime/provider failure without meaning change | New Attempt may execute the same revision |
| Frozen validation surface omitted a genuinely required member | Hand back; do not silently append it |
| Candidate edits governing text | Evaluate against frozen prior authority; candidate text cannot authorize itself |
| Required evidence needs an already-authorized effect | Perform when effect prerequisites hold |
| Evidence needs an effect outside admitted authority | Hand back for corrected authority/revision |

### 4.3 Current Attempt, terminalization, and authority fencing

One Work Unit has one current authority capable of producing new semantic decisions or initiating new authoritative external effects.

Version 0.3 pushes the low-level run stop/loss mechanism to Zeroshot and uses terminalization as the run-level cutoff, without requiring the stop request itself to be the exact semantic cutoff:

```text
A1 / Run Z1 is current
        ↓
replacement requested
        ↓
Broodling requests stop / terminalization of Z1
        ↓
Z1 reaches durable terminal state
        ↓
Broodling accounts for all relevant authority occurrences
durably committed by Z1 and carries forward still-applicable obligations
        ↓
old undispatched effect authority is fenced; uncertain/conflicting
external effects remain explicit and block conflicting new mutations
        ↓
A1 ends
        ↓
only then may A2 become current
```

An authority occurrence durably committed before Z1 reaches terminal state remains a historical authoritative occurrence and must be accounted for even if a stop had already been requested. An occurrence that was not durably committed by terminalization cannot later become authority for that run. The target therefore does not depend on a stronger claim that every successful computation racing with a stop request is necessarily converted to failure. [Z5–Z6; D5]

Before admitting a replacement Attempt, deciding Work Unit disposition, or authorizing an operation that depends on current semantic state, Broodling must catch up its cross-run semantic state with all relevant durable authority occurrences not yet incorporated. The mechanism—cursor, watermark, idempotent projection transaction, or equivalent—is implementation detail; incorporation does not confer authority a second time.

Terminalizing the Zeroshot run is not by itself a fence against an already-dispatched or remotely uncertain external mutation. Broodling must prevent stale undispatched intents from starting and must keep conflicting new mutations ineligible while a prior effect remains active or uncertain. This requirement does not force all non-conflicting work to wait and does not select a distributed fencing design for the first single-host profile.

This means Broodling still does not need a fresh semantic "blessing" transaction after every authority node. The integrity boundary is terminalize, account for committed semantic authority, and fence conflicting effect authority before replacement.

Distributed deployment and more elaborate leases/fencing remain deferred. Any future execution profile must preserve these properties before being advertised.

---

## 5. Attempts, runtime loss, sessions, and unit-local control

### 5.1 Zeroshot owns the one-run lifecycle

Within an Attempt, Zeroshot owns:

- node dispatch and exact occurrences;
- graph reduction and routing;
- bounded loops/maps;
- provider/session lifecycle;
- node timeouts;
- cancellation/force-stop;
- durable run progress and terminal state;
- runtime-loss terminalization under the supported profile.

Broodling does not create a second session manager, node retry scheduler, or run-status ledger.

### 5.2 Attempt replacement

A repair round or graph loop iteration is not a new Broodling Attempt. Terminal runtime loss ends the Attempt's execution episode. If work continues under unchanged meaning, Broodling admits a new Attempt against the same Contract revision and starts a new Zeroshot run.

Prior candidate source may be reused only under explicit verified starting assumptions. Prior semantic decisions remain historical facts and may remain applicable if Broodling's cross-run policy establishes that applicability; no prior `PASS` or abandoned workspace silently becomes current approval.

### 5.3 Session continuity is an optimization, not a correctness boundary

Zeroshot may retain or replace provider sessions according to its supported runtime profile. Broodling's semantic correctness must remain reconstructible from explicit graph/run inputs and durable cross-run state.

A session already exposed to forbidden context cannot become blind merely because the next node message is narrower. Broodling therefore still defines information-flow constraints; Zeroshot owns the mechanics of the actual sessions.

### 5.4 Effect reconciliation is not a coding retry

A lost acknowledgement for a Git/GitHub effect is reconciled against external state. It does not create a new coding Attempt merely to discover whether the effect happened.

Reconciliation retains the original Work Unit, Contract revision, Attempt origin, effect intent, and exact target. Further mutation requires current authority.

---

## 6. Durable truth: Broodling and RunLedger

### 6.1 One owner for each kind of fact

| Question | Authoritative answer |
|---|---|
| Which node ran with which bound input and exact occurrence? | Zeroshot RunLedger |
| What outcome did that execution emit? | Zeroshot RunLedger |
| Was the outcome structurally/type valid for the admitted node? | Zeroshot admitted runtime/runner result |
| Which Contract revision does the Attempt execute? | Broodling Attempt admission |
| Is this execution occurrence a designated adjudication/final-assessment authority occurrence? | Broodling profile/Attempt relationship + immutable admitted graph |
| What did the authoritative adjudicator decide? | The referenced Zeroshot authority occurrence |
| Is that decision still applicable or superseded across later source/Attempt/decision changes? | Broodling |
| Which directives remain outstanding across Attempts? | Broodling |
| Does evidence apply to the current Contract/source/result? | Broodling semantic policy, informed by runtime/evidence facts |
| Did an external effect reach the required state? | External readback recorded as Broodling DeliveryReceipt |
| Has the Work Unit succeeded? | Broodling WorkUnitDisposition |

A durable reviewer `approved` outcome proves that the reviewer emitted approval. It does not prove that the candidate satisfies the Contract. Experiment C remains the concrete false-positive counterexample. [E3]

### 6.2 Authority without a duplicate semantic-application hop

The v0.2 path:

```text
model-backed adjudicator
    → Zeroshot outcome
    → Broodling validates/copies/adopts same payload
    → directive/reference returned to Zeroshot
```

is replaced by:

```text
designated adjudicator occurrence
    → Zeroshot validates typed outcome and durably records exact execution
    → that occurrence is the authoritative decision for its bound applicability context
    → Zeroshot graph routes from its admitted output/signals
    → Broodling retains cross-run applicability/supersession/outstanding state
```

The graph/runtime validation is not a proof that the adjudicator's semantic judgment is correct. It is sufficient to establish that the result came from the exact admitted authority processor and has the required structural contract. Semantic correctness is evaluated through the assurance corpus and independent final assessment.

Direct within-run routing is valid only for the applicability context bound to that authority occurrence. Cross-run use, replacement admission, final disposition, and effect decisions that depend on current semantic state require the Broodling catch-up/currentness rules in §§4.3 and 6.4; the processor's own output cannot self-attest that those external prerequisites still hold.

### 6.3 No duplicated execution engine or runtime projection

Broodling does not reproduce:

- Zeroshot graph replay/reduction;
- node scheduler state;
- provider/session lifecycle;
- loop counters;
- exact run occurrence history;
- run status/log streams;
- token-usage accumulation;
- generic response-schema validation.

The official SDK may be used to observe those facts directly where its contract suffices. [Z1]

### 6.4 Reconciliation and semantic catch-up still cross systems

Broodling, Zeroshot RunLedger, and GitHub are not one distributed transaction.

For external effects, Broodling still needs stable intents and exact readback semantics because a remote mutation can complete while the local response is lost.

For semantic authority occurrences, the simplification is stronger because the authoritative payload is already a durable run event/outcome in one store. Broodling must not mint a second semantically distinct decision under the same authority occurrence. If a retention/custody copy is made, it references the same occurrence.

Broodling nevertheless owns cross-run consequences of those decisions. It must be able to recover relevant completed authority occurrences—including from unsuccessful or stopped runs—and idempotently advance its applicability, supersession, outstanding-obligation, acceptance, or other cross-run state before that state is relied on. A crash between durable Zeroshot completion and Broodling semantic catch-up must not make an accepted directive disappear from a replacement Attempt.

The durable checkpoint/catch-up representation is deliberately deferred. It is a semantic recovery boundary, not a second authority-admission step and not a second graph reducer.

---

## 7. Execution protocol and information boundaries

### 7.1 Keep the default assurance graph short

Version 0.3 intentionally does not turn every desirable software quality into another architectural node. The initial semantic protocol should remain small:

```text
IMPLEMENT
   ↓
REVIEW
   ↓
ADJUDICATE  ← designated authority processor
   ├─ accepted corrections → REPAIR → REVIEW → ADJUDICATE ...
   ├─ authority gap        → handback / non-success
   └─ no unresolved blockers
                 ↓
        FINAL SEMANTIC ASSESSOR
        ← designated authority processor
```

The final semantic assessor is the last semantic assurance decision for the candidate convergence path. It is not necessarily the last executable node in the entire Work Unit lifecycle because Contract-authorized effects may occur before or after it.

Additional reviewer axes, simplification reviewers, refactoring stages, or other specialist nodes are **not** target requirements. They should be added only when evaluation demonstrates a concrete failure mode that the simpler protocol does not handle reliably.

### 7.2 Why adjudication remains separate from final assessment

Adjudication reduces review churn:

```text
review findings
    → classify which findings actually establish an obligation
    → repair only admitted directives
```

The final semantic assessor answers a different question:

```text
given the final candidate, complete Contract, eligible evidence,
and applicable authoritative decisions:
    are the semantic obligations satisfied?
```

A single final judge cannot replace mid-loop adjudication without reintroducing raw-review-to-repair churn. Conversely, a clean adjudication round does not itself establish complete criterion-level acceptance.

### 7.3 Role input and output contracts

| Processor | Allowed semantic inputs | Produces | Authority |
|---|---|---|---|
| **Implementation worker** | Frozen Contract, repository instructions, starting source, permitted validation instructions | Source changes, raw validation observations, risks | None |
| **Independent reviewer** | Frozen Contract, exact source/comparison base, selected raw evidence, governed review instructions | Coverage + evidenced findings | None |
| **Adjudicator** | Frozen Contract, current source/evidence needed to investigate, raw findings, scoped applicable prior decisions | Accepted/rejected finding classifications, directives, authority questions | **Yes, by designated occurrence** |
| **Repair worker** | Frozen Contract, current source, currently applicable directives, correction obligations | Corrected source, observations, accounting, unresolved risks | None |
| **Final semantic assessor** | Complete Contract, final source, eligible evidence, applicable authoritative interpretations/directives | Criterion-level acceptance/gaps | **Yes, by designated occurrence** |
| **Trusted effect executor** | One Broodling-authorized intent | External mutation + observation | Mechanical authority only for the exact intent; cannot widen Contract authority |

A role's authority comes from its admitted semantic role/occurrence, not from possessing a particular JSON shape.

The **Allowed semantic inputs** column is exhaustive for automatic semantic context. In particular, an independent reviewer must not automatically receive worker narrative or private reasoning, prior reviewer conclusions, adjudication deliberation/ledger state, or repair rationale. Legitimate additional context must be deliberately selected and identified through the admitted input boundary; it must not arrive merely through inherited session history or ambient graph state. [D6]

### 7.4 Within-run state belongs in the graph; cross-run obligations belong in Broodling

A fresh review round may have a fresh findings container while earlier accepted directives remain open.

Within a live Attempt, the Broodling-authored GraphSpec should carry outstanding directives through typed state/bindings so they cannot vanish merely because the newest findings list is empty. Zeroshot owns execution of that graph state. An accepted obligation remains open until an eligible authority occurrence explicitly resolves or supersedes it; omission from a new findings container or a non-authoritative worker output cannot discharge it.

Where route-affecting meaning is represented in more than one field—for example both a payload and a signal—the admitted protocol must prevent contradictory representations from taking an unauthorized clean route. Prefer one canonical decision representation when practical; otherwise enforce deterministic consistency through the GraphSpec/runtime capability. The exact mechanism is an implementation dependency, not a reason to restore a generic Broodling result validator. [D5]

If the Attempt ends and another Attempt begins, Broodling supplies the still-applicable outstanding obligations explicitly to the new run after performing the semantic catch-up required by §§4.3 and 6.4. This is the cross-run semantic state Zeroshot does not own.

### 7.5 Fail closed using Zeroshot's execution contract

Experiment C showed that failed execution cannot be allowed to masquerade as an initialized empty result.

The target requirement is therefore:

- every executable node has explicit unusable-outcome handling;
- successful model output must satisfy the admitted typed response contract;
- crash, timeout, refusal, malformed output, missing output, and failed trusted identity/applicability checks cannot become a clean semantic result;
- graph routing uses validated output/signals or explicit error routes;
- a clean signal cannot bypass outstanding accepted obligations;
- an expected negative/RED observation that was successfully produced is evidence, not an execution crash, and must remain distinguishable from unusable execution. [D6]

Current Zeroshot already validates graph-derived response shape/types before successful node completion and routes durable history through the graph reducer. [Z4, Z6] Broodling should author the correct fail-closed GraphSpec rather than reimplementing a second validator/router.

### 7.6 Provider context is wider than GraphSpec

Effective model input still includes harness/system instructions, loaded skills, repository instructions, session history, tool configuration, and observed outputs. Experiment C's ambient reviewer skill remains a concrete warning.

Broodling must select or record relevant automatic context when it can alter assurance meaning. Ambient configuration cannot silently become Contract authority.

The exact isolation mechanism is deferred. The requirement is controlled automatic information flow plus observable provenance, not complete hostile-agent confinement.

---

## 8. Source identity, evidence, and custody

### 8.1 Physical workspace mechanics may be Zeroshot-owned; semantic source identity is not

Current local Zeroshot snapshots repository identity, attached branch, and exact `HEAD` before a run, and then executes in the selected workspace. [Z7]

That is useful execution provenance, but it is not sufficient as Broodling's complete candidate-source identity because:

- intentional untracked source may matter;
- generated/tracked source classification matters;
- a mutable workspace can change after the starting `HEAD`;
- evidence may need a stable reviewed source interval;
- a digest is not durable custody without recoverable bytes/materialization.

Therefore:

```text
Zeroshot owns:
    checkout/workspace/run source mechanics

Broodling owns:
    what exact candidate-controlled bytes constitute SourceRevisionRef
    and whether evidence/effects apply to that candidate
```

### 8.2 Source, environment, and evidence are distinct

| Identity | Question |
|---|---|
| **Source revision** | What candidate-controlled content is being assessed/delivered? |
| **Execution/check environment** | Which harness, instructions, command, tools, dependencies, host/mode, and external inputs produced the observation? |
| **Evidence/artifact record** | What was actually observed and where can it be inspected? |

Equal source does not imply equal validation conditions. Wrong host/mode/population/artifact can make evidence insufficient even when code bytes match.

### 8.3 Evidence can reference Zeroshot outcomes instead of copying them

When an observation is already a durable exact Zeroshot node outcome, an EvidenceRecord may reference that execution occurrence plus Broodling-specific source/environment/applicability metadata.

Raw artifacts needed to justify acceptance must remain recoverable after disposable runtime/session cleanup. A run-log pointer that will disappear is not sufficient custody.

### 8.4 Applicability remains Broodling semantic work

A changed source makes prior approval insufficient by default. A superseded interpretation may invalidate evidence sufficiency even with unchanged source bytes. A mutable branch URL does not transfer evidence from candidate C1 to C2.

Relevant failures and contradictions remain observations. A later green rerun does not silently erase an earlier failing or contradictory observation; both remain eligible for consideration until Broodling can justify why the contradiction no longer bears on the criterion. An unexplained relevant prior failure prevents the evidence set from being treated as sufficient. [D6]

These applicability judgments span run history and Contract semantics and therefore remain Broodling-owned.

---

## 9. Semantic assurance

### 9.1 Review must challenge evidence, not merely repeat it

Experiment C's wrong-stack counterexample remains mandatory assurance evidence. A reviewer must challenge whether the claimed evidence would distinguish a materially incorrect behavior that violates the criterion.

The exact reviewer count, axes, prompts, or use of specialist simplicity/complexity reviewers remains open. The default is the smallest reviewer population that achieves measured sensitivity without excessive churn.

### 9.2 Adjudication is authoritative by designated occurrence

Raw findings are observations. The designated adjudicator determines whether each finding establishes:

- a Contract-backed defect;
- a governed standards violation;
- a rejected/nonblocking suggestion;
- a need for external authority.

Its successfully completed admitted occurrence is itself the authoritative semantic decision. Broodling does not need to copy the same classification into a second record merely to confer authority.

Broodling still tracks:
- which adjudication occurrence applies;
- which directive remains outstanding;
- which later occurrence supersedes it;
- whether dependent evidence/acceptance must be reconsidered.

The adjudicator cannot amend the Contract.

### 9.3 Correction safeguards remain

Historical protections against:
- missing negative/population-boundary checks; and
- repairs that preserve a demonstrated-insufficient implementation premise

remain semantic obligations.

They belong in the repair/adjudication/final-assessment role contracts and regression fixtures. Version 0.3 does not require the historical prose rituals or a dedicated reset lifecycle node.

An unresolved correction check that is required by the Contract or applicable correction policy prevents SemanticAcceptance and prevents any effect whose prerequisites require that check. A repair cannot discharge an accepted obligation merely by omitting the negative/population-boundary check or by preserving a premise already shown insufficient. [D6]

A dedicated complexity/refactoring node is not introduced merely to enforce "do not overengineer." Overengineering can be a review finding and adjudication question. A specialist node should be added only if empirical evaluation shows that ordinary review/adjudication reliably misses the failure mode.

### 9.4 Governing-text changes require semantic comparison

When authorized output changes future governing material, the frozen prior Contract remains authority for assessing the actual before/after semantic change.

The assessment may be a bounded model-backed processor inside Zeroshot. If it is designated authority-bearing for this purpose, its occurrence can constitute the governing-change decision. Any commit/publication requiring that decision remains effect-authorized by Broodling.

The edited governing text cannot authorize its own publication.

### 9.5 Final semantic assessment

A final semantic assessor receives:

- the complete frozen Contract obligations;
- the final candidate/source identity;
- eligible evidence;
- applicable authoritative adjudications/interpretations;
- unresolved-obligation state.

It must produce criterion-level sufficiency/gaps. Reviewer silence is not enough. A clean adjudication route is permission to reach final assessment, not an acceptance decision.

A positive final-assessment occurrence becomes current SemanticAcceptance only while its Contract/source/evidence/interpretation applicability remains valid.

This final semantic assessor is the preferred final **semantic** authority node of the normal assurance graph. Work Unit `SUCCEEDED` remains a Broodling disposition because required external effects may still be outstanding, uncertain, or reconciled after the Zeroshot run ends.

---

## 10. Strict success and authoritative delivery

### 10.1 Success predicate

The confirmed success predicate is unchanged:

```text
SUCCEEDED
=
current applicable semantic acceptance
AND
all delivery/publication effects required by the applicable Contract revision completed
AND
those required effects were verified
```

This is a conjunction, not a lifecycle ordering.

The following remain distinct:

```text
provider completed
    ≠ Zeroshot node completed
    ≠ Zeroshot run succeeded
    ≠ review clean
    ≠ adjudication clean
    ≠ semantic acceptance
    ≠ external effect completed
    ≠ Work Unit SUCCEEDED
```

A successful Zeroshot run can carry the authoritative final semantic assessment, but the run terminal is not itself the Work Unit disposition.

### 10.2 Required and permitted effects come from the Contract

The Contract resolves:
- which effects are permitted;
- which are required for success;
- applicable prerequisites;
- target semantics;
- verification requirements.

A useful effect is not automatically permitted. A permitted optional effect is not automatically required.

A prototype-only Contract may require exact branch publication and observations from that prototype, with no PR, merge, issue closure, or deployment.

### 10.3 Authorization, intent, execution, and observation remain separate

Before an effect, Broodling establishes and durably records:
- Work Unit;
- Contract revision;
- originating Attempt where applicable;
- current authority;
- exact target/payload/source relationship;
- satisfied prerequisites and external preconditions.

A trusted executor receives that authorized intent. It does not receive open-ended permission to decide what to mutate. Workspace, repository, shell, or Git write access does not itself confer authority to commit, push, publish, create/update a PR, or perform any other authoritative effect. A local commit does not imply push authority. [D6]

After execution, Broodling verifies the external postcondition and records the receipt. Unknown completion remains uncertain. A receipt discharges a required effect only when its actual source/target/result relationship still satisfies that exact obligation under the applicable Contract revision; a receipt for candidate C1 cannot silently satisfy an obligation to deliver candidate C2 merely because a branch name, URL, or other mutable locator is reused. If effect-time hooks, packaging, or other transformations materially change candidate-controlled bytes, the resulting source relationship must be explicitly identified and verified rather than inheriting the earlier source identity or acceptance by implication. [D6]

Readback during reconciliation establishes what happened; it does not retrospectively grant authority that was missing when the effect occurred. An externally observed unauthorized mutation remains a historical external fact to account for, not an authorized receipt manufactured after the fact. [D6]

### 10.4 Push down mechanics when the Zeroshot capability actually matches

Current Zeroshot has trusted Git delivery mechanics, but its stock native-v2 delivery is organized around PR/merge modes and bundles behavior more narrowly than Broodling's Contract-specific effect model. [Z8]

Therefore v0.3 does **not** require Broodling to reimplement Git/GitHub mechanics, and it also does **not** pretend that the stock adapter already fits every Broodling effect.

The preferred future boundary is:

```text
Broodling:
    authorize exact operation/source/target/prerequisites

generic trusted Zeroshot/external effect primitive:
    perform that exact operation
    return/read back exact observed result

Broodling:
    reconcile and decide applicability/success
```

Where the current Zeroshot API already matches an admitted effect exactly, reuse it. Where it bundles unauthorized extra effects or lacks required readback semantics, do not force the Contract into the adapter merely for reuse.

### 10.5 Reconciliation outlives the run

Git/GitHub is not transactionally coupled to RunLedger or Broodling state. A remote mutation may complete while the local response is lost.

Broodling must reconcile the exact prior intent before retry. It must not fabricate a Zeroshot node execution merely to explain an externally observed effect.

Previously completed effects remain historical facts across Attempts and Contract revisions. Compensation/rollback is itself an effect requiring authority.

---

## 11. External collaboration and product interface

The external interface remains semantic.

| Interaction | Broodling boundary |
|---|---|
| **Submit work reference** | Resolve Work Unit, construct Contract, establish Closability, admit or explain non-admission |
| **Observe Work Unit** | Return Contract/Attempt lineage and Broodling semantic/effect state, plus referenced Zeroshot run status/log/usage information |
| **Provide new authority** | Admit a new immutable revision when meaning changes |
| **Request stop** | Mark/request stop through the current Attempt's Zeroshot run; stop is never success |
| **Request retry after terminal attempt** | Admit a new Attempt under the same revision when appropriate |
| **Consume result** | Return Work Unit disposition with semantic-assessment and required-effect justification |

Broodling should not copy every live Zeroshot status field into a second telemetry system. It may project them for product UX while preserving Zeroshot as the run-status source.

A model session need not remain alive while a human decides. The Work Unit and unresolved authority question remain durable outside the session/run.

---

## 12. Disposition of historical R1–R19 responsibilities

This remains a preservation map, not an instruction to recreate nineteen modules.

| Historical responsibility | v0.3 target disposition |
|---|---|
| **R1 — Issue/repository/run admission** | Broodling admission. One explicit work reference/repository. No backlog selector. |
| **R2 — Primary authority, trust, mutations** | Broodling Contract/effect authority plus designated Zeroshot authority processors for in-run semantic decisions. Remove the requirement for one central agent or a second blessing hop. |
| **R3 — Trusted snapshot/amendment boundary** | Retain in Broodling. Frozen entitled authority; meaning change requires new revision/Attempt. |
| **R4 — Workflow/instruction provenance** | Split: Zeroshot admitted run supplies graph/runtime execution facts; Broodling records only additional semantic/ambient provenance that matters. |
| **R5 — Closability / finite validation surfaces** | Retain in Broodling admission. |
| **R6 — Freeze, custody, resume/invalidation** | Broodling owns Contract/source/evidence custody; Zeroshot owns one-run durability/stop/loss. Replacement run = new Attempt. |
| **R7 — Scoped delegation / retained implementation owner** | Role contract in Broodling-authored GraphSpec; Zeroshot owns invocation/session mechanics. |
| **R8 — TDD slices / bounded coherence** | Optional admitted implementation methodology, not a universal lifecycle. |
| **R9 — Evidence identity/sufficiency/invalidation/reuse** | Retain Broodling semantic policy, conservative invalidation, and contradiction handling; reference Zeroshot outcomes where possible rather than duplicate execution facts. |
| **R10 — Readiness/same-mechanism assessment** | Semantic methodology/fixtures; no mandatory standalone phase. |
| **R11 — Immutable Review-index delivery** | Replace packaging with frozen Contract/source/evidence bindings delivered by the graph/run. |
| **R12 — Review-chain convergence** | Zeroshot graph/loop execution plus Broodling semantic applicability across runs. No Broodling review-loop scheduler. |
| **R13 — Adjudication/blocking thresholds/sticky rulings** | Designated adjudicator occurrence is authoritative; Broodling tracks applicability/supersession/outstanding directives, while raw-findings and blind-review input firewalls remain enforced. |
| **R14 — Correction self-check** | Retain negative/population-boundary and claimed-versus-observed correction protection; unresolved required checks block acceptance and dependent effects; no prose ritual. |
| **R15 — Implementation-mechanism reset** | Retain reconsideration of disproven premises; a repair cannot preserve a demonstrated-insufficient mechanism merely to satisfy the same obligation; no dedicated reset lifecycle node unless evidence later justifies one. |
| **R16 — Normative-remediation checkpoint** | Retain semantic before/after assessment; may be a designated authority processor occurrence. Effect authorization remains Broodling-owned. |
| **R17 — Bounded re-adjudication** | New authority occurrence may explicitly supersede an old one within unchanged Contract bounds; Broodling records supersession/applicability. |
| **R18 — Closure truth/outcomes/unresolved work** | Designated final semantic assessor supplies semantic authority; Broodling combines current acceptance with verified required effects for Work Unit disposition. |
| **R19 — Canonical GitHub closeout/discovery** | Broodling owns Contract-specific effect authorization and verification; reuse generic trusted mechanics where they match. No universal PR path. |

### Consequence

Broodling is **not** nineteen responsibilities turned into nineteen code modules, and it is also **not** a second workflow engine above Zeroshot.

Its irreducible core is narrower:

```text
Work Unit identity
Contract authority / Closability
Attempt admission / current authority
cross-run semantic applicability
source/evidence applicability
effect authorization / reconciliation
strict Work Unit disposition
```

The execution of implementation/review/adjudication/repair/final assessment is primarily a Zeroshot graph concern.

---

## 13. Invariant-to-evidence and test-fixture map

| Target invariant / failure mode | Discriminating fixture |
|---|---|
| **Frozen authority does not drift** | Issue/discussion changes after admission; executing Attempt continues on immutable Contract. Meaning change requires new revision/Attempt. |
| **Only designated processor occurrences carry semantic authority** | Reviewer emits an object shaped like an adjudication; it cannot become a directive. Exact designated adjudicator occurrence with valid contract can. |
| **Authority output does not require duplicate blessing** | Adjudicator completes successfully; graph can route from the durable validated result without a separate Broodling copy/admit operation. |
| **Replacement Attempt accounts for all committed old authority** | Request stop while adjudicator is in flight; after durable terminalization, every authority occurrence committed by the old run is incorporated exactly once before replacement state is constructed. Nothing can become authority after terminalization. |
| **Crash between authority completion and semantic catch-up does not lose obligations** | Adjudicator durably accepts directive D, Broodling crashes before cross-run state advances, and the run then ends; recovery supplies D exactly once to replacement/current semantic state before it is relied on. |
| **Conflicting effects remain fenced across replacement** | Prior effect is active or remotely uncertain when replacement is requested; stale undispatched intent cannot start and a conflicting new mutation remains ineligible until the prior effect is reconciled or otherwise made safe. |
| **Malformed/failed execution cannot masquerade as empty success** | Crash, timeout, refusal, malformed structured output, or missing output cannot take the clean route. |
| **Outstanding directives survive fresh rounds** | New review findings are empty while an earlier accepted directive remains unresolved; graph cannot reach final acceptance. |
| **Raw/rejected findings do not reach repair authority** | Repair input contains only applicable adjudicated directives. |
| **Reviewer blind-input firewall holds** | Private worker reasoning, prior reviewer conclusions, adjudication deliberation, and repair rationale are available elsewhere in the run but are not automatically bound into the independent reviewer input. |
| **Final assessor is not reviewer silence** | Clean/empty review with inadequate criterion evidence must still fail final semantic assessment. |
| **Wrong-stack counterexample is detected** | Count-based test can pass while wrong stack executes; semantic assessment must identify the evidence blind spot. Include valid controls. |
| **Run facts are not duplicated as competing Broodling truth** | Node result/reference is canonical; retention copy cannot diverge into a second decision. |
| **Source identity distinguishes candidate from environment** | Cache/dependency generation does not silently change source identity; intentional/untracked source does. |
| **Stale applicability fails closed** | Review/assessment is bound to candidate C1 or an earlier interpretation, then source/decision applicability changes; the failed identity/applicability check cannot take a clean/accepting route. |
| **Contradictory evidence survives** | Earlier relevant failure and later green rerun both remain accounted for; closure cannot silently drop the failure without explaining why it no longer bears on the criterion. |
| **Correction cannot preserve an invalid premise** | Required negative/population check or causal counterexample defeats the proposed repair mechanism; acceptance and any dependent effect remain blocked until an eligible correction addresses the obligation. |
| **Evidence answers actual obligation** | Wrong host/mode/population/artifact or unavailable raw evidence cannot establish the criterion. |
| **Governing edit cannot self-authorize** | Candidate broadens permissions/precedence; frozen-authority semantic comparison blocks unintended publication. |
| **Semantic acceptance is not Work Unit success** | Positive final assessment with missing/unverified required effect cannot yield `SUCCEEDED`. |
| **Effect completion is not semantic acceptance** | Verified branch/PR publication with inadequate semantic evidence cannot yield `SUCCEEDED`. |
| **Required effect remains bound to its actual candidate/target** | C1 publication is verified, candidate advances to C2/C4, and a required delivery obligation concerns the later candidate; the old receipt cannot satisfy the new/current obligation through a reused branch or URL. |
| **Workspace access is not effect authority** | An agent can edit/commit in the workspace or an unauthorized remote mutation is discovered during reconciliation; neither access nor readback manufactures commit/push/publication authority that was never granted. |
| **Effect reconciliation does not rerun coding** | Lost push/PR response is resolved by exact readback, not a new coding Attempt. |
| **No universal publication** | Contract requiring no publication succeeds without one; prototype-only Contract requires only its exact branch/evidence obligations. |

Deterministic fixtures validate identity, graph routing, type/failure semantics, stop/fencing, and effect reconciliation. Semantic corpora validate adjudicator/reviewer/final-assessor judgment quality against counterexamples and valid controls.

Identical model inputs do **not** guarantee identical model outputs. Zeroshot gives reproducible input/occurrence structure and durable history, not deterministic semantic replay.

---

## 14. Open details and non-goals

| Deferred choice | Boundary already fixed |
|---|---|
| **Broodling language, repository layout, CLI/service packaging** | Language is not dictated by Zeroshot being Rust. |
| **External SDK/process vs Rust library embedding** | Prefer the narrowest supported integration that preserves the target. Implementation planning must evaluate the official external SDK/sidecar path before assuming Rust embedding. Embedding requires a concrete missing necessary seam, not language affinity. |
| **Semantic storage technology** | Broodling needs durable cross-run Work Unit/Contract/applicability/effect/disposition state; it should not mirror RunLedger. |
| **AuthorityOccurrenceRef physical representation** | Exact runtime occurrence identity plus Broodling role/applicability is required; payload may remain canonical in RunLedger. |
| **Reviewer count/axes/models** | Keep the default graph short. Add specialist nodes only on evidence of a measured failure mode. |
| **Fresh vs retained provider sessions** | Zeroshot owns session mechanics; correctness cannot rely on hidden continuity. |
| **Source manifest/materialization format** | Broodling source identity and recoverable custody remain required. |
| **Evidence reuse/caching** | No silent carryover; applicability remains explicit. |
| **Effect executor implementation** | Broodling authorizes exact effects. Reuse Zeroshot or other trusted mechanics when they match without widening/bundling authority. |
| **Generic trusted/deterministic Zeroshot worker seam** | Useful upstream capability, but not a reason to duplicate semantic authority processing in Broodling. Required deterministic operations may remain external until such a seam fits. |
| **Full non-success taxonomy / PARTIAL / cleanup** | Strict `SUCCEEDED` predicate fixed; unresolved facts/effects remain durable. |
| **Distributed takeover / multi-host fencing** | Not required for first single-host profile; any future profile must preserve one current authority. |
| **Hostile-agent OS/network/MCP confinement** | Not immediate target; automatic information flow and authoritative effect routing remain controlled. |
| **Cerebrate global scheduling/budgeting** | Outside Broodling. |

### 14.1 Integration preference after this correction

Current source inspection makes an external-engine Broodling implementation materially plausible:

```text
Broodling application
    → official Zeroshot SDK / sidecar / supported protocol
    → custom Broodling GraphSpec
```

because adjudication and final semantic assessment no longer require custom deterministic Broodling drivers merely to confer authority.

The remaining integration questions are narrower:
- source/evidence custody beyond the native starting source snapshot;
- ambient execution-context provenance;
- recoverable read access to completed designated authority occurrences—including exact occurrence identity, bound input, outcome, and completion state—from unsuccessful or stopped runs;
- exact trusted-effect mechanics required by supported Contracts;
- any deterministic trusted node that truly must execute inside the run rather than before/after it.

The target requires the completed-occurrence recovery capability, not a particular API shape. Current source inspection does not establish that the existing public Python SDK exposes the full retained completed-occurrence data needed for this recovery path; a future Zeroshot SDK/protocol feature, a narrow read-only adapter, or another supported seam may satisfy it. This is an implementation/dependency question, not a reason to assume an in-process Rust host. [Z1, Z5; D5]

The implementation/dependency plan must test these seams rather than assume an in-process Rust host.

---

## 15. Target design conclusion

The v0.3 target is:

> **Broodling is the durable single-Work-Unit semantic and effect authority around one admitted Zeroshot execution Attempt, not a second execution engine. Zeroshot owns the run. Broodling owns why that run is allowed, what its designated authority processors mean, what remains true across runs, what external effects are authorized, and whether the Work Unit ultimately succeeded.**

The central boundaries are:

1. **Across Work Units:** Cerebrate selects/schedules; Broodling handles one explicit unit.
2. **Across authority revisions:** an Attempt executes one immutable Contract revision; meaning change requires external authority, a new revision, and a new Attempt.
3. **Across run vs cross-run state:** Zeroshot owns one-run graph execution, typed outcomes, exact occurrences, sessions, routing, stop/loss, status, and RunLedger; Broodling owns cross-run Contract/applicability/effect/disposition state.
4. **Across observation vs semantic authority:** ordinary worker/reviewer outputs are observations; explicitly designated adjudicator/final-assessor occurrences can directly constitute semantic authority.
5. **Across authority and semantic correctness:** an authoritative model judgment can still be wrong; semantic quality remains an evaluated assurance property.
6. **Across assurance and effects:** Broodling authorizes Contract-permitted effects when their prerequisites hold; effects can produce evidence but do not imply acceptance.
7. **Across acceptance and completion:** current semantic acceptance plus verified completion of every required effect is necessary for `SUCCEEDED`; neither a Zeroshot terminal nor a final assessor alone is the Work Unit result.
8. **Across integration technology:** the architecture does not require Broodling to be Rust or to embed Zeroshot. Use the narrowest supported integration that preserves these boundaries.

**This v0.3 has received both an independent architecture review and a historical-protection regression review against v0.2/R1–R19, and incorporates their targeted corrections without restoring duplicate semantic-admission plumbing.** It is ready for implementation/dependency planning from this accepted target. [D5, D6]

Planning readiness is **not** production readiness. Substantive adjudication quality, fresh repair effectiveness, review sensitivity, delivery reconciliation, and the selected Zeroshot integration/profile behavior remain empirical implementation and release obligations; the target does not claim those properties are already proven.

The implementation/dependency plan should be regenerated from this accepted target rather than patched from v0.2. Current Zeroshot capability gaps discovered during planning should be treated as implementation dependencies unless they demonstrate that a required boundary itself cannot be maintained.

---

## Source and decision register

**[D1] Confirmed Broodling architectural boundaries from the v0.1/v0.2 baseline.** Single-Work-Unit scope; Cerebrate exclusions; no migration-driven transitional architecture; Broodling-owned ingress and authoritative effects; strict success after current semantic acceptance plus verified required effects; immutable Contract revision/Attempt relationship.

**[D2] Confirmed identity/retry assumptions from the v0.1/v0.2 baseline.** One primary work reference and repository per Work Unit; meaning-changing authority requires a new Contract revision; non-semantic runtime failure may produce a new Attempt against unchanged meaning.

**[D3] Project-owner effect-ordering correction, 5 September 2026.** Contract-authorized effects may occur before semantic acceptance when their own prerequisites hold, including prototype publication needed for evidence. Preserves exact source/target correlation and strict success conjunction; no universal PR/merge/closure/deployment.

**[D4] Project-owner simplification direction, 5 September 2026, following review of current Zeroshot seams.** Reconsider the cost of requiring all model/runtime outputs to undergo a second Broodling authority-admission hop; allow explicitly designated Zeroshot processors to carry semantic authority where safe; keep adjudication to filter review churn; prefer a short implementation → review → adjudication/repair → final-assessment process; and look for further opportunities to push generic one-run responsibilities into Zeroshot instead of duplicating them in Broodling.

**[D5] Independent v0.3 architecture review supplied by the project owner, 5 September 2026.** Verdict: `READY WITH TARGETED CORRECTIONS`. The review accepted the central Work-Unit/Zeroshot split, authority by designated occurrence, separate adjudication and final semantic assessment, Contract-specific effect ordering, strict success conjunction, and open integration technology. Its architectural corrections retained here are: semantic catch-up before cross-run state is relied on; trusted applicability context rather than processor self-attestation; explicit sticky-obligation/control-consistency invariants; terminalization rather than stop-request timing as the run cutoff; and fencing of conflicting external effects across replacement. Its observation that the current public SDK does not expose the full completed-occurrence recovery surface is recorded as an implementation dependency, not a target redesign.

**[D6] Independent v0.2 → v0.3 historical-protection regression review supplied by the project owner, 5 September 2026.** Verdict: `TARGETED PROTECTION CORRECTIONS NEEDED`. The review accepted the v0.3 simplifications and identified six protections that had been thinned during mechanism removal: blind reviewer-input isolation; effect-to-current-candidate correspondence; workspace access not implying effect authority and readback not retroactively granting it; preservation of relevant contradictory/failing observations; blocking force for required correction checks and disproven-premise handling; and explicit fail-closed handling of failed applicability/identity checks. These protections are restored here as target invariants and fixtures without restoring the v0.2 duplicate semantic-admission path or prescribing implementation mechanisms.

**[T2] `broodling-target-responsibility-boundary-design-v0.2.md`, version 0.2, 5 September 2026.** Governing predecessor replaced by this document. Its Work Unit/Cerebrate, Contract, source/evidence, assurance, delivery, historical-protection, and success boundaries are retained except where this version explicitly corrects semantic-authority materialization and one-run responsibility placement.

**[V1] `brood-target-architecture-independent-review.md`.** Independent review used by v0.2; retained as critique/evidence, not new authority.

**[P1] `work-on-178-architectural-provenance-audit.md`.** Historical/current-at-audit R1–R19 protection map. Historical packaging is not target architecture.

**[E1] `Zeroshot-Experiments-A.md`, including A.2.** Typed dataflow, bounded convergence, fresh occurrences, round-local state, and signal/data consistency concerns at tested revision `0f4e86ae2b8172c2d571af3f8a253f6862eeb0de`.

**[E2] `Zeroshot-Experiments-B.md`.** Trusted source sealing/verdict eligibility, source-to-verdict binding, stale eligibility, and trusted-node integration awkwardness at the same tested revision.

**[E3] `Zeroshot-Experiments-C.md`.** Real implementation/review sequence, false-positive review, unexercised real repair, generated-state identity drift, explicit error routing, ambient harness instructions, and unresolved assurance/cost questions.

Current Zeroshot Rust v2 source inspected at **`d0909615d6ba3c179b58bce15a059f40400ec995`**:

**[Z1]** `sdks/python/README.md`; `sdks/python/src/zeroshot/runtime.py`; `sdks/python/src/zeroshot/runs.py` — official typed Python SDK, bundled matching Rust sidecar, exact opaque GraphSpec/RuntimePlan submission, durable run observation/status.

**[Z2]** `crates/openengine-cluster-protocol/src/graph.rs` — language-neutral GraphSpec with step/verifier nodes, typed payloads/bindings/signals, seq/choice/par/loop/map, and explicit succeed/fail nodes.

**[Z3]** `zeroshot-rust/src/native_v2_admission.rs`; `zeroshot-rust/src/native_v2_admission/validation.rs`; `crates/openengine-cluster-protocol/src/native_v2_run/runtime.rs` — graph/runtime admission, graph-local worker registry, binding validation, current Agent/GitDelivery binding inventory.

**[Z4]** `zeroshot-rust/src/native_v2_runner.rs`; `zeroshot-rust/src/native_v2_runner/response.rs` — exact NodeInvocation/ExecutionRef handoff, public runner/driver/session ports, graph-derived response contracts, structured outcome validation before successful completion.

**[Z5]** `zeroshot-rust/src/v2_run_ledger.rs`; `zeroshot-rust/src/v2_run_ledger/state.rs` — durable admitted run, execution occurrences, inputs/outcomes, terminal/usage history; explicit statement that fencing, proofs, effect receipts, retries, and takeover do not belong to RunLedger.

**[Z6]** `zeroshot-rust/src/native_v2_supervisor.rs`; `zeroshot-rust/src/native_v2_supervisor/controller.rs`; `zeroshot-rust/src/native_v2_supervisor/runtime.rs` — one-run reduction/routing, durable dispatch-before-start, timeout/cancellation, force-stop, runtime-loss terminalization, completion identity checks.

**[Z7]** `zeroshot-rust/src/native_v2_local.rs` — local repository/branch/exact-HEAD resolution, current workspace execution, local runtime/provider composition.

**[Z8]** `zeroshot-rust/src/native_v2_candidate.rs`; `zeroshot-rust/src/native_v2_delivery.rs` — stock Agent-versus-GitDelivery composition and trusted PR/merge delivery mechanics/receipts; useful transport machinery but narrower than Broodling's Contract-specific effect model.
