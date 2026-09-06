# Broodling implementation and dependency plan

## 1. Basis and implementation direction

**Start with a blocking Zeroshot integration gate—not a Broodling language, framework, or embedded-runtime decision.** The first milestone should establish that an external-engine implementation can recover exact authority occurrences, preserve trusted applicability context, and execute the required fail-closed protocol without introducing a second execution engine.

The governing architecture is the supplied **target responsibility and boundary design v0.3**. Its responsibilities, authority rules, and success predicate remain fixed throughout this plan. In particular, semantic authority can arise directly from a designated Zeroshot occurrence; Broodling’s subsequent catch-up records cross-run consequences rather than conferring authority again. 

### Inspected repository baselines

| Input     | Inspected baseline                                   | Consequence                                                                                                                                        |
| --------- | ---------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Broodling | `main` at `d93354a8907fff887156af72d67d11d3936d5fe1` | The default branch contains only `README.md` and `.gitignore`. This is a new implementation, not a migration of existing product code.             |
| Zeroshot  | `main` at `d0909615d6ba3c179b58bce15a059f40400ec995` | This matches the revision inspected by v0.3. The plan can investigate the target’s stated integration questions against that same source baseline. |

Both heads were checked during this review. Broodling’s README explicitly describes its status as bootstrap-only.

The distinctions below are deliberate:

* **Established finding:** supported by inspected source or the governing design.
* **Implementation selection:** a proposed choice for carrying out this plan.
* **Unresolved integration question:** requires a demonstrated capability or dependency change before dependent work proceeds.
* **Deferred decision:** need not be settled yet.

This review inspected source; it did not compile Broodling or Zeroshot, execute integration experiments, or run the proposed validation gates. Historical experiment requirements below come from v0.3; the underlying experiment reports were not independently inspected.

## 2. Established source findings

| ID                                                                                                  | Established finding                                                                                                                                                                                                                                                                                | Planning consequence                                                                                                                                                                                                          |
| --------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **F1 — External custom-run submission exists**                                                      | The Python SDK accepts exact `GraphSpec`, `RuntimePlan`, initial input, and a submission key. It delegates preflight and execution to the Rust sidecar rather than implementing graph validation in Python.                                                                                        | Evaluate this route first. Broodling does not need to become a Rust application merely to submit its own protocol.                                                                                                            |
| **F2 — Public observation is narrower than retained execution history**                             | The inspected SDK exposes status, active-execution selectors, terminal results, logs, watch, and force-stop. Its public models do not expose completed occurrences with their exact bound input and outcome.                                                                                       | Completed-authority recovery is a concrete integration dependency, not something satisfied by `RunResult`, logs, or watching active nodes.                                                                                    |
| **F3 — The needed occurrence facts exist internally**                                               | `NodeSnapshot` contains an `ExecutionRef`, structural occurrence, node execution attempt, bound input, start cursor, and completed/voided state. Completion contains a cursor and `WorkerOutcome`. `RunLedger` provides `get`, `get_by_submission_key`, and atomic `snapshot_and_tail`.            | Seek supported access to these facts without duplicating RunLedger. A node’s internal execution-attempt field must not be confused with a Broodling Attempt.                                                                  |
| **F4 — Structural validation does not establish protocol consistency**                              | Graph guards select signals/errors/group controls. The response validator checks output, diagnostic payload, and signal validity separately.                                                                                                                                                       | Test sticky obligations and contradictory clean signals explicitly. Do not assume payload typing enforces cross-field or predecessor-state consistency.                                                                       |
| **F5 — Lifecycle mechanics already belong to Zeroshot**                                             | SDK detachment does not stop execution; force-stop waits for a terminal result. The local observer can reopen a dead controller’s ledger and terminalize a nonterminal run as `runtime_lost` without dispatching replacement nodes. Ledger event application rejects events after terminalization. | Broodling should request/observe terminalization, then catch up semantic state. It should not reconstruct sessions or resume the old execution episode.                                                                       |
| **F6 — Native source and artifact facilities are insufficient by themselves for Broodling custody** | Local execution resolves repository, attached branch, and `HEAD`, then runs in the existing mutable workspace. Native-v2 rejects nonempty `WorkerOutcome.artifacts` fields.                                                                                                                        | Establish candidate-byte identity and raw-artifact custody explicitly. Typed outcomes can carry useful observations or references, but neither a starting `HEAD` nor an outcome reference proves custody of all needed bytes. |
| **F7 — Current trusted delivery is specialized**                                                    | Native runtime bindings are `Agent` and `GitDelivery`. Stock delivery supports PR/merge modes; both commit the workspace, push a deterministic run branch, and create or rediscover a review.                                                                                                      | Reuse only when that complete behavior fits the exact authorized effect. There is no demonstrated generic external-effect binding in this inventory.                                                                          |
| **F8 — Submission deduplication has a source-sensitive boundary**                                   | The local submission path resolves source before computing its submission digest and looking up the submission key. A mismatching digest produces a submission conflict identifying the existing run.                                                                                              | Test lost acknowledgement after source changes. Repeating a request must recover the existing Attempt/run relationship, not accidentally create another run or treat conflict as permission for a new Attempt.                |

Sources: F1—SDK README and client submission path; F2—SDK run models and public run operations; F3—RunLedger records and trait.

F4—graph syntax and response validation; F5—SDK lifecycle, portable observer, and ledger terminal guard.

F6—local composition and artifact restriction; F7—binding inventory and delivery implementation; F8—local submission/deduplication.

## 3. Minimal implementation selections

**Selection S1 — Use the official Python SDK/sidecar for the first feasibility harness.** This selects the investigation route, not Broodling’s eventual language. Use an explicit custom graph and runtime plan rather than adopting a stock software-change workflow as Broodling’s semantics.

**Selection S2 — Qualify one single-host execution profile first.** Start by evaluating local execution in a dedicated test workspace. Qualify its actual context, custody, stop/loss, and credential behavior; do not infer those properties from SDK availability. A different supported target remains possible if the local profile cannot preserve the boundary.

**Selection S3 — Make the first product vertical slice a Contract with no required authoritative effects.** That allows admission, semantic authority, replacement, and disposition to be validated without prematurely building every delivery operation. It is an incremental slice, not a change to the success predicate or a reason to defer investigating effect integration.

**Selection S4 — Keep the application’s durable model limited to v0.3’s semantic responsibilities.** Reference Zeroshot’s canonical execution facts. Do not create parallel node-result, session, graph-progress, or run-status authorities.

These selections preserve the target’s external-integration preference, short assurance graph, and distinction between semantic acceptance and verified required effects. 

## 4. Dependency order

```text
P0  Baseline and invariant fixtures
 │
 ▼
P1  Early Zeroshot integration qualification
 │   ├─ G1-core: required before product execution depends on Zeroshot
 │   └─ G1-effects: required before effect-dependent execution
 ▼
P2  Work Unit, Contract, Closability, and Attempt admission
 ▼
P3  Source/evidence applicability, custody, and context boundary
 ▼
P4  Authority-occurrence catch-up and safe Attempt replacement
 ▼
P5  Minimal assurance graph and no-effect vertical slice
 ▼
P6  Contract-specific effects and reconciliation
 ▼
P7  Product interface, semantic evaluation, and release qualification
```

Fixture preparation and independent source investigation may proceed while an integration dependency is being resolved. A consuming phase must not pass its gate using an unproved substitute for that dependency.

### P0 — Establish the implementation baseline and regression contract

**Depends on:** supplied v0.3 and inspected repositories.

Record v0.3 as the governing implementation basis, along with the exact Zeroshot source/build used for qualification. Establish traceability from every invariant in v0.3 §13 to either a deterministic fixture, a semantic evaluation case, or both.

The initial deliverable is not a module layout. It is a small inventory of invariants, integration questions, test inputs, and expected observations. Separate tests of runtime mechanics from tests of model judgment.

**Gate G0:** Every §13 invariant has an assigned validation method. The baseline contains no implicit requirement to port `work-on`, create nineteen modules, implement a second scheduler, or introduce a universal publication phase. The historical protection map is preserved as tests and role obligations, not historical packaging. 

### P1 — Test the critical Broodling–Zeroshot assumptions early

**Depends on:** G0.

Use a small custom graph containing ordinary and designated authority-bearing nodes, repeated occurrences, a repair route, a final assessment, and controllable failure/stop points. Exercise the real SDK/sidecar boundary; mocks alone cannot establish that the public integration works.

Zeroshot already exposes native runner/driver/session interfaces and ledger test facilities that can support deterministic fixture construction. Using those for qualification does not imply embedding the production engine in Broodling.

#### Integration questions and required witnesses

| ID                                               | Unresolved integration question                                                                                                                          | Required early validation                                                                                                                                                                                                                                                                                      |
| ------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Q1 — Recoverable authority occurrences**       | What supported read interface exposes the retained admitted graph/runtime relationship and exact completed occurrences?                                  | Recover exact identity, bound input, successful/error/voided outcome, and completion ordering after client restart and after successful, failed, and stopped runs. Distinguish repeated executions of the same node. Demonstrate a stable terminal boundary and a retention/custody arrangement.               |
| **Q2 — Admission/run correlation**               | Can Broodling recover an ambiguously submitted run without creating competing authority?                                                                 | Lose the submit acknowledgement; reconnect or resubmit using the same key. Repeat after source/`HEAD` changes. Recover or explicitly reconcile the existing run identity. A conflicting request must not authorize a second run.                                                                               |
| **Q3 — Fail-closed protocol expressiveness**     | Can current GraphSpec/runtime facilities enforce sticky obligations and consistent routing?                                                              | Inject crash, timeout, refusal, malformed/missing output, contradictory payload/signal decisions, and an empty new findings list while an old directive remains open. None may reach the clean/accepting route. A successfully observed expected RED result remains evidence rather than an execution failure. |
| **Q4 — Trusted in-run source/evidence handoff**  | How do trusted candidate identity and evidence selection become bound to authority-node inputs while the workspace changes during the run?               | Change candidate bytes between relevant steps; forge source/predecessor identifiers in model output; remove raw artifacts. The system must refuse stale applicability. Demonstrate that the graph cannot race past a required trusted operation merely because Broodling observes completion asynchronously.   |
| **Q5 — Automatic context and session isolation** | Does the selected execution profile preserve the reviewer’s exhaustive input boundary?                                                                   | Put worker narrative, previous review conclusions, adjudication deliberation, repair rationale, and ambient skills/configuration elsewhere in the environment. Inspect effective reviewer input and relevant provenance. A narrow JSON input must not conceal contaminated inherited context.                  |
| **Q6 — Stop/loss and semantic catch-up**         | Does the qualified transport expose the terminalization and durable history needed for replacement?                                                      | Stop while an adjudicator is in flight; separately kill the controller. Recover every successful authority occurrence committed before terminalization, including one committed before a Broodling-side crash. No post-terminal occurrence may become authority.                                               |
| **Q7 — Exact effects and live-run coordination** | What trusted primitive performs an exact authorized operation, and how can the graph consume its verified result without Broodling becoming a scheduler? | Exercise one effect-before-assessment path and one effect-after-assessment path with controlled effect mechanics. Verify exact intent consumption, no extra bundled mutation, and prerequisite-respecting progression. Keep reconciliation possible after the run ends.                                        |

**Dependency disposition.** Q1 has a demonstrated public-SDK surface gap, although the retained facts exist internally. Prefer a supported SDK/protocol extension or qualified narrow read-only adapter over embedding the engine. The existing Rust `RunLedger` read methods are relevant implementation seams; they are not proof that an external reader is already supported. Do not treat private SQLite layout, redacted logs, or successful final graph output as an adequate recovery contract.

For Q3–Q5 and Q7, a failed witness should produce a concrete dependency: the missing graph/runtime capability, context control, trusted handoff, or exact-effect operation, together with its consuming gate. It must not produce a generic Broodling response validator, node scheduler, or session manager.

**Gate G1-core:** Q1–Q6 are demonstrated through the chosen integration, with explicit supported-version and retention assumptions. Do not pass this gate merely because internal Rust tests can access data that Broodling’s selected integration cannot.

**Gate G1-effects:** Q7 establishes a viable trusted-effect coordination mechanism. If it remains unresolved, the no-effect slice may advance after G1-core, but P6 and all publication-dependent Contracts remain blocked.

### P2 — Implement trusted admission and the durable semantic nucleus

**Depends on:** G1-core.

Implement Work Unit resolution from one primary GitHub issue and one target repository, including repeat-submission identity. Capture entitled authoritative sources and construct source-attributed proposed Contracts. Keep source entitlement separate from model extraction: a model can assist interpretation but cannot decide that a source has authority.

Implement Closability against the frozen criteria: production/public boundary, finite evidence population, validation seam, executable action, falsifying observation, available prerequisites, and a permitted route through required effects and evidence collection. Detect missing capabilities and impossible prerequisite cycles before execution.

Persist the immutable Contract revision and Attempt before authorizing run submission. Correlate the Attempt with its stable submission identity and resulting Zeroshot run. Do not silently amend an Attempt’s Contract or conflate a graph repair iteration with a new Attempt.

Choose the physical semantic store here, after G1’s recovery requirements are known. Store only the Broodling records required by v0.3; retain execution provenance by reference wherever Zeroshot reliably supplies it. 

**Gate G2:**

Repeated submission cannot create competing Work Unit authority. Live issue edits do not alter an executing Contract. Missing entitlement, validation populations, effect permissions, or execution capabilities prevent admission. Crash injection around durable admission and submission preserves one recoverable Attempt/run relationship. A meaning change requires externally authorized revision and a new Attempt.

Replacement operations are not yet exposed for general use; their safety depends on P4.

### P3 — Implement source/evidence applicability, custody, and the qualified context boundary

**Depends on:** G2 and the Q4/Q5 solutions from G1-core.

Define the initial source-selection policy and a recoverable materialization scheme. Distinguish candidate-controlled content from environment state, including intentional untracked source and relevant generated/tracked content. Do not use “all generated files excluded” or starting `HEAD` as an implicit source policy.

Implement EvidenceRecords that reference exact Zeroshot occurrences where sufficient, adding Broodling’s source, environment, external-input, artifact-custody, and applicability relationships. Provide separate custody for raw artifacts not retained by the runtime’s outcome mechanism.

Implement conservative invalidation and contradiction accounting. Changed source does not inherit approval. Changed applicable interpretation may invalidate sufficiency even when bytes are unchanged. A later green observation does not delete a still-relevant failure.

Complete the selected profile’s automatic-context controls and record the additional provenance that Zeroshot does not reliably retain. Use the trusted handoff qualified in P1 to bind applicability-critical context; processor-emitted identifiers cannot establish their own eligibility. These are Broodling source/evidence responsibilities, not a replacement workspace or provider lifecycle manager. 

**Gate G3:**

Demonstrate recoverability after runtime/session cleanup; intentional-untracked-source detection; environment/source separation; wrong-host/mode/population rejection; stale-source and predecessor-state rejection; and preservation of contradictory observations. Show that a reviewer receives the permitted semantic context rather than an accidental history of the run.

A digest without recoverable bytes, or an artifact pathname without custody, does not pass.

### P4 — Implement authority-occurrence catch-up and safe replacement

**Depends on:** G2, G3, and Q1/Q6 qualification.

Implement the Broodling relationships over designated authority occurrences: applicability, supersession, outstanding obligations, and SemanticAcceptance. Resolve authority from admitted role and exact occurrence, successful runtime outcome, trusted bound context, and current applicability—not from output shape or self-asserted identifiers.

A `Completed` runtime record is not automatically successful semantic authority: its `WorkerOutcome` may represent an error. Conversely, an unsuccessful or stopped run may contain earlier successful authority occurrences that must still be accounted for. The retained source model supports these distinctions.

Implement idempotent semantic catch-up before replacement admission, disposition, or effects that rely on current semantic state. Persist the cross-run consequences and recovery checkpoint without replaying Zeroshot’s graph or creating a second decision identity for the same occurrence.

Implement replacement in the target order: request stop, observe durable terminalization, incorporate all relevant committed authority, preserve still-applicable obligations, fence stale undispatched effects, and prevent conflicting mutations while prior effects remain active or uncertain. Only then may a replacement Attempt become current. Non-conflicting work need not wait for unrelated reconciliation.

Use deterministic effect-executor doubles to test fencing policy here; qualify real external behavior in P6. 

**Gate G4:**

Crash after durable adjudication but before semantic catch-up; recover the directive exactly once into replacement state. Exercise stop/completion races, repeated catch-up, stale occurrence references, and explicit supersession. Reviewer-shaped “decisions” never acquire authority. Unresolved directives cannot disappear through omission. Conflicting effect authority remains fenced across replacement.

### P5 — Implement the minimal assurance graph and no-effect vertical slice

**Depends on:** G3 and G4.

Author the short Broodling GraphSpec:

```text
IMPLEMENT → REVIEW → ADJUDICATE
                         ├─ applicable directives → REPAIR → REVIEW …
                         ├─ authority gap → non-success / handback
                         └─ no unresolved blockers → FINAL SEMANTIC ASSESSOR
```

Designate adjudication and final assessment as authority-bearing roles in the immutable admitted profile. Keep ordinary workers and reviewers observational. Repair input contains applicable adjudicated directives, not raw or rejected findings.

Carry outstanding obligations through typed graph state using the mechanism proven in G1. An eligible authority occurrence must explicitly resolve or supersede an accepted obligation; empty new findings and worker claims cannot discharge it. Use explicit unusable-outcome paths and bounded convergence. Keep re-adjudication inside unchanged Contract meaning.

Bind the complete Contract, final source, eligible evidence, applicable interpretations, and unresolved obligations into final assessment. Its criterion-level result is distinct from reviewer silence and clean adjudication. Retain the correction safeguards and governing-text before/after assessment in role contracts and fixtures; do not add dedicated reset/refactoring/specialist nodes without evaluation evidence. 

**Gate G5:**

Pass deterministic tests for error routes, authority designation, sticky obligations, raw-finding isolation, stale applicability, bounded repair, and authority-gap handback.

Then complete an initial real-provider Work Unit under a frozen Contract with no required effects. Broodling—not the run’s terminal label—must determine disposition from applicable final assessment and the Contract’s effect obligations. This demonstrates the no-effect slice, not general semantic reliability or delivery readiness.

### P6 — Implement exact authorized effects and independent reconciliation

**Depends on:** G1-effects, G2–G5.

Add effect capabilities individually. For each operation, document the exact authorized target, payload/source relationship, prerequisites, external preconditions, execution mechanics, and readback that discharges the obligation.

Where stock Zeroshot delivery matches the entire authorized operation, reuse it. Where its commit/push/PR bundle exceeds authority, use a qualified narrower trusted primitive or external executor. Keep any necessary integration change explicit. Current stock delivery is not a branch-only primitive.

Implement durable intent before dispatch, current-authority and prerequisite checks, trusted exact-intent execution, verified receipt, uncertainty handling, and conflict fencing. Workers’ workspace or shell access must not become open-ended effect authority.

Integrate the live-run handoff established by G1-effects. Publication-dependent evidence must be obtainable before semantic assessment when the Contract permits it; other effects may require prior acceptance. Neither case should require a second Broodling scheduler or splitting one Attempt into multiple runs.

Reconciliation must work without a live coding run. It reads back the exact original intent, does not fabricate node execution, does not start a coding Attempt merely to discover remote state, and cannot retrospectively authorize an unauthorized mutation. Receipts remain bound to actual candidate and target; effect-time transformations require explicit source correspondence. 

**Gate G6:**

Test remote success with lost acknowledgement, repeated reconciliation, stale undispatched intents, active/uncertain conflicting effects across replacement, and an unauthorized mutation discovered by readback.

Include all three lifecycle controls: no-publication Contract; prototype publication needed before assessment without an implicit PR/merge requirement; and an effect whose prerequisite is prior semantic acceptance. Verify that a C1 receipt cannot satisfy delivery of C2 through a reused branch or URL.

Only qualified operations may be advertised. Unsupported required effects fail Closability; they are not silently removed from the Contract.

### P7 — Complete the product boundary and release qualification

**Depends on:** G2–G6 for the capabilities being released.

Expose the semantic interactions in v0.3: submit a reference, observe a Work Unit, provide new authority, request stop, retry after terminal Attempt where appropriate, and consume disposition with justification. Reference Zeroshot status/log/usage facilities where available instead of building a second telemetry authority.

A future `/work-on` adapter remains thin. Backlog selection, dependency waiting, global scheduling, and cross-Work-Unit budgeting remain outside this implementation.

Implement and test the exact disposition conjunction:

```text
SUCCEEDED
=
current applicable SemanticAcceptance
AND
every Contract-required effect completed
AND
every required effect verified
```

Do not add “Zeroshot run succeeded” as a substitute for any term, or treat stop, publication, or clean review as success. 

Complete semantic evaluation using counterexamples and valid controls: wrong-stack evidence that still satisfies a count-based test; wrong population/host/mode/artifact; inadequate evidence despite clean review; unjustified corrections; required negative checks; repairs preserving disproven premises; governing edits that attempt self-authorization; and unexplained prior failures.

Exercise actual repair, not only successful initial implementation. Evaluate reviewer sensitivity, adjudication quality, final-assessment false acceptance, correction effectiveness, convergence, and churn. Establish acceptance thresholds before qualification runs; do not choose arbitrary numerical thresholds in this plan. Model behavior remains an empirical property rather than deterministic replay. 

**Gate G7:** The §13 traceability inventory is complete for the advertised profile; deterministic recovery and boundary tests pass; semantic evaluation meets agreed thresholds; every advertised effect has passed reconciliation testing; and disposition is reconstructible from retained authority, applicability, and receipt relationships.

## 5. Decisions to keep deferred

| Decision                                                        | Latest responsible point / constraint                                                                                                                      |
| --------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Broodling language and application framework**                | After G1 establishes the actual integration surface. Python in the feasibility harness is not a product-language commitment.                               |
| **External SDK/process versus Rust embedding**                  | Remains external-first. Embedding requires a demonstrated necessary seam that a narrower supported interface cannot satisfy.                               |
| **Semantic storage technology and physical schema**             | Select in P2 using the proven catch-up, uniqueness, and crash-recovery requirements. Do not mirror RunLedger.                                              |
| **Occurrence-reference encoding and checkpoint representation** | Select with the Q1 read contract/P4 recovery implementation. Exact identity and idempotence are fixed; cursor/watermark/transaction representation is not. |
| **Source manifest and artifact materialization format**         | Select in P3. Exact candidate identity and recoverability are mandatory; format is not prescribed.                                                         |
| **Models, reviewer population, prompts, and specialist nodes**  | Qualify empirically. Keep the normal graph short; add complexity only for measured failures.                                                               |
| **Effect executor and breadth of initial effect support**       | Select operation by operation in P6, subject to exact authority and readback fit. Prototype/no-publication semantics must remain representable.            |
| **CLI/service packaging and detailed non-success taxonomy**     | Settle when implementing the product surface. Durable unresolved facts and strict success are already fixed.                                               |
| **Evidence caching and session reuse optimizations**            | Only after correctness without hidden continuity is demonstrated.                                                                                          |
| **Distributed takeover and hostile-agent confinement**          | Outside the first qualified single-host profile. They must not be implied by that profile’s qualification.                                                 |

These deferred choices follow v0.3’s open-detail boundaries rather than postponing settled correctness obligations. 

## 6. Immediate implementation priorities

The first dependency to resolve is **supported completed-occurrence recovery**: the internal facts exist, but the inspected public SDK does not expose the required recovery contract. The next critical dependencies are **trusted in-run applicability handoff** and **demonstrable sticky-obligation/control consistency**. Exact-effect coordination is a separate early-qualified dependency that blocks effect-bearing Contracts.

Once those gates pass, the implementation can remain narrow: trusted admission, cross-run applicability and obligations, source/evidence custody, effect authority and reconciliation, and strict disposition. Graph execution, typed outcome validation, routing, sessions, stop/loss, and durable execution history remain Zeroshot responsibilities throughout.
