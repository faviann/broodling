# PR #48: Broodling / Zeroshot ownership audit

Date: 16 September 2026. The initial audit reviewed PR head
d2d34ae30a2e573e23a61379184b623c988944f5; the final witness reassessment reviewed
b3aabc7d3e0e5680e0a925e41a6dae2da2442711. Basis: the owner's instruction to
audit ownership before preserving or replacing qualification assertions. This is
a prospective test-ownership correction and a production-design assessment, not
a retrospective change to G3/G4 evidence.

## 1. Proposed ownership boundary

Broodling admits one Work Unit and owns its immutable Contract, current Attempt,
workspace, invocation identity and genuinely domain-specific policy. It prepares
the input/configuration, submits through Zeroshot's supported interface, consumes
the correlated terminal result and required artifacts, and commits the appropriate
lifecycle action under currentness and effect-authority rules.

Zeroshot owns GraphSpec validation, dispatch, typed response validation, routing,
branch precedence, loop progression, state propagation, retries, occurrence
identity and provider/session execution. Broodling configures policy using that
model; it does not independently certify the model or monitor its intermediate
steps to reconstruct success. Administrative cancellation remains a legitimate
Broodling lifecycle operation, not a reason to supervise every node.

Authorship is not sufficient evidence of ownership. A graph declaration is
worth testing when it expresses a real Broodling decision, such as who can mutate,
which inputs repair receives, or which role can issue a directive. Its incidental
encoding as a named choice after a particular sequence child is not a separate
product requirement. A finite user/Contract execution budget can be owned policy;
proving the engine performs exactly three iterations is not.

Terminal success is not an arbitrary boolean accepted from an arbitrary graph.
It is a result of the immutable admitted invocation with the configured semantic
authority, complete required artifacts and the still-current Attempt. Broodling
must check those boundaries and custody completeness; it need not witness the
internal route used to produce them or independently re-judge model semantics.

## 2. Justified tests and requirements

| Owned behavior | Current coverage retained |
| --- | --- |
| Frozen Contract, admitted instructions/B1/workspace, correct graph/runtime/profile, immutable invocation and one-run correlation | Submission, assurance-submission, frozen-instructions and replay/acknowledgement-loss tests |
| Mutation confined to implementation/repair; read-only assurance; fresh sessions; selected custom evidence leaf | Small declaration tests in test_assurance_policy.py, launcher/profile tests and representative real integration |
| Repair sees the Contract and adjudicated directive, not raw/rejected findings or private rationale | Direct input-binding test plus the actual repair handoff in repair-renewed |
| Only designated roles write obligation/directive/final rationale; worker diagnostics cannot rewrite admission | Direct authority-binding tests; no routing analysis |
| Explicit evidence commands, frozen population, raw bytes/context, availability distinct from sufficiency, no shell interpretation | Mechanical-evidence/Contract tests and opaque-material fidelity tests |
| Correlated terminal artifacts contain the evidence/rationale from the configured run | valid and repair-renewed integration cases; custody completeness tests |
| Failure, missing material or stale/abandoned Attempt cannot authorize success/effects | Terminal adapter, custody, disposition, abandonment/replacement and no-effect tests |
| Broodling's custom evidence collector does not leave its child alive when cancelled by the runtime | One timeout-descendant integration case, with a witnessed live child and no survivor |

The retained real campaign has three cases: valid, repair-renewed and
timeout-descendant. The first two use the unchanged product
graph/runtime/coordinator and the actual deterministic evidence leaf; semantic
and mutation agents are controlled. The last shortens only the evidence node's
timeout and records that deviation. It validates the custom
collector/namespace/child boundary, not Zeroshot's general timer implementation.

The final reassessment removed `wrong-population` and `missing-initial`.
`wrong-population` observed a controlled assessor's semantic judgment and
Zeroshot's route to a failed result. Broodling's owned claims are already direct:
the collector keeps the Contract's frozen population distinct from opaque
candidate bytes, and failed terminal results cannot authorize custody or
disposition. `missing-initial` observed Zeroshot routing the custom leaf's
`missing` signal. The leaf's actual missing-material response is exercised
directly, while the terminal/custody/disposition boundaries independently refuse
failure and incomplete material. Neither complete run protected an additional
Broodling-owned handoff.

## 3. Removed tests and reconsidered requirements

### Tests deliberately removed without replacement

- All per-executable unusable/error-route pairings, routes_authored_after,
  fail/succeed sink inventories, branch ordering, loop-until/error topology,
  exact repair iteration/order traces, and distinct clean/repaired route shapes.
  These freeze or independently reason about engine control flow, not an owned
  boundary. A positional traversal is not a proof of all paths; making it one
  would create exactly the analyzer this project should avoid.
- The eight remaining #17 runs: repeat-labels, sticky-exhaust,
  open-control-crash, missing-initial_review, malformed-initial_review,
  default-initial_review, missing-payload-initial_review, hang-initial_review.
  Their remaining unique claims were loop/error evaluation, response rejection,
  iteration/state propagation or generic timeout mechanics. There is no cheaper
  replacement because those are no longer Broodling test obligations.
- The older all-node crash/default/missing/refusal/hang matrix, authority-lookalike
  response permutations and widened-graph canary are not recreated. Actual
  authority/binding constraints remain covered directly, without asking Zeroshot
  to execute a deliberately changed graph.
- Exact model transcript sequences and assertions that a failure happens before
  a particular review/final node in the evidence campaign. The assertions now
  concern configured handoffs, terminal outcomes and returned material.
- `wrong-population` and `missing-initial` complete runs. Their remaining claims
  split cleanly into direct collector/configuration checks at the beginning and
  terminal/custody/disposition refusal checks at the end; the execution between
  those boundaries belongs to Zeroshot.
- Exact timeout/attempt counts, instruction marker prefixes, diagnostic schema
  option flags and global forbidden-word scans from the new structural suite.
  Keep such checks only when an independent configuration contract requires them,
  not because they appear in the current encoding.

The #17 test class, controlled provider fixture and control/adversarial campaign
executables are deleted. Their original implementations remain available in Git;
no empty or redirected campaign emits a misleading PASS. Shared serialization
helpers remain because other qualification record writers use them. The only
GraphSpec traversal enumerates declared leaves for configuration inspection and
the disclosed timeout fixture adjustment; it infers no order or reachability.

### Historical requirements to revise prospectively

| Source | Ownership drift | Corrected interpretation |
| --- | --- | --- |
| Issue #45 scope and acceptance criteria | Declares every executable's guard, error fall-through, failure sinks and bounded repair to be structural Broodling properties; mandates preserving every old protection and mapping every removal to a replacement | Inventory ownership first; delete dependency conformance assertions without replacements. Keep fail-closed terminal disposition and explicit domain policy, not proof of all internal paths |
| Issue #45 boundary rule | If structural, inspect graph; if runtime behavior, keep a real witness | Neither inspectability nor runtime execution establishes Broodling ownership. Keep a witness only for a named Broodling integration assumption |
| Issue #17 protocol/AC and plan v0.5 section 5.3 (W3) | Preserve qualified topology/semantic state machine, exact third-repair termination, all unusable occurrences and prove C1/C2 separation from graph order | Keep mutation/read-only authority, explicit repair policy and fresh applicable evidence as outcomes/configuration; rely on the runtime's execution model rather than recertify its loops and guards |
| Issue #18 AC | Require every semantic mismatch permutation, before-review placement and unchanged prior topology; require a widened-binding canary | Keep evidence fidelity, population ownership, reviewer independence and repair isolation. A fixture's semantic branches do not prove real judgment; a direct binding test need not be accompanied by a graph mutation campaign |
| Issue #19 output/custody AC and plan section 5.6 (W6) | Require live final and most-recent mutation occurrence capture before consuming a result; reject successful results when that live observation was missed | Specify the trusted terminal-result/artifact contract. Distinguish consuming a correlated terminal result from reconstructing old execution history or reusing another Attempt's semantics |
| Issue #23 and G4 addendum item 3 | Inherit live-occurrence custody and demand integrated repair ordering/sticky/final-currentness controls together with lifecycle races | Preserve current Attempt, complete custody, authorized effects and atomic/idempotent disposition. Separate these from runtime control-flow proof and from the observational mechanism inherited from #19 |

The historical source issues, governing documents, evidence JSON and gate verdicts
remain unchanged. This audit flags their over-specification separately; it does
not claim that the smaller suite satisfies every literal historical checklist or
retroactively changes a recorded PASS/FAIL. Actual semantic quality evaluation is
also separate from dependency conformance.

## 4. Production involvement that still needs simplification

1. **Live occurrence observer.** ZeroshotSubmitter.observe_current in
   broodling/zeroshot_sdk.py tracks implement/repair and named final executables,
   rejects overlapping or later occurrences, requires a forward cursor and
   refuses an already-terminal run. It can reject a successful admitted run solely
   because the observer did not see its intermediate occurrences. This is the
   clearest conflict with beginning/end participation.
2. **Custody depends on that observer.** CurrentRunObservation and
   FinalAssuranceCoordinator store finalOccurrence and candidateGeneration using
   live execution references. _bound also compares against the current graph and
   runtime definitions. WorkUnitDispositionCoordinator requires fresh capture
   under its existing uninterrupted-finalization rule. The steady-state design
   should consume a versioned terminal result/artifact contract bound to the
   persisted invocation, retaining runtime references only when actually needed
   as supplied provenance, not reconstructing them by watching the graph.
3. **A model call for round bookkeeping.** round_complete makes no domain
   judgment: it emits completed. It creates an additional executable, error guard,
   loop-exit dependency and branch-precedence problem just to end a repair round.
   Remove it in a production protocol simplification after checking the pinned
   native loop contract; do not add more tests to certify this bookkeeping.
4. **Graph encoding is treated as a compatibility contract.** Duplicated final
   paths, per-node failure plumbing, current-graph equality and the historical
   supports_contained_stop hash tie integration/stop compatibility to the current
   encoding. Prefer supported native constructs where they preserve domain
   policy. Do not assume an unverified native default has identical behavior or
   remove fail-closed production routes merely because their tests were deleted.

The custom evidence leaf and trusted launcher remain justified: they implement
Contract-derived evidence and actual isolation. Their subprocess/cancellation
seam is not a scheduler or an excuse for supervising Zeroshot's whole run.

## 5. Minimal PR #48 change

Delete the structural proof suite and #17 engine campaign/fixture/entrypoints;
keep seven small policy/configuration tests and the three named real integration
cases. Remove execution-order assertions from those cases and check terminal
material instead. Correct evidence-fidelity claims and current coverage docs.
Do not rewrite historical reports or add a new analyzer/canary framework.

No production behavior, schema, graph, SDK pin or runtime profile changes in this
commit. In particular, the live observer is **identified, not fixed**. Its current
adapter/custody/disposition tests remain temporary regression protection for
production code that still exists, not endorsement of that architecture. Replacing
it is a coordinated terminal-contract/custody/lifecycle change; deleting those
safety tests alone would hide a real mismatch rather than resolve it.

The pre-#45 lane made 52 runs (38 assurance, 12 evidence, 2 product submissions).
Reviewed PR head made 13 (8 assurance, 5 evidence). This revision makes 3, all in
the evidence integration campaign. Running the standalone #18 writer separately
makes the same three runs again; it is a record-producing alternative, not an
additional required campaign. The rest of the opt-in lifecycle/custody lane is
outside this count. Current verification is recorded in tests/README.md.
