# Issue #22 independent review

Fixed baseline: `cb9b9a6e3c80bbe6c5c84a225d8d06601d737138` (`origin/main`).
Separate fresh read-only Codex contexts reviewed standards and specification,
including tracked changes and all new replacement/retry product and test files.
The final review incorporates both user-approved source/input decisions.

## Standards

No hard documented-standard violations found. Product admission/storage additions
use the standard library, preserving README's lazy SDK boundary.

One optional judgement call: the product initial-input construction is repeated
in submission preparation, replacement validation and final-assurance validation.
A shared constructor could prevent future additions from making these paths
disagree, while each caller retains its own authority checks. The parent retained
these explicit boundary comparisons for this issue; regression and integration
checks cover their agreement. An earlier review also noted optional test-fixture
setup duplication between distinct administrative and public-SDK test families.

Final standards total: **0 hard violations; 1 optional smell finding**.

## Spec

The final fresh review found no mandatory product-code fixes against issue #22,
target v0.5 §§4–7.1 and plan v0.5 V1-P4.

- Original Contract/B1 and verified frozen snapshots initialize A2; only the
  implementer receives `admittedInstructions`. No worker writes that binding.
- Retry identity, allocation and profile reservations are transactional. Ordinary
  ingress cannot consume reserved homes through nominal paths, actual launch
  environment, SDK state or prospective worktree allocation.
- Checkout transformation refusal precedes checkout/status and checks configured
  drivers, effective committed/global/info attributes and conditional configuration.
  No normalization or provenance mechanism was added.
- Dispatched repetition preserves public correlation and legitimately mutated
  candidate state; it does not reset B1 or create another run.
- The historical graph exception is an exact digest and is administrative only.
  Runtime/target binding and physical cessation remain mandatory; old semantic
  observation or new assurance is not authorized by it.

The original reservation bypass and its declared-environment variant were
reproduced, fixed and independently re-reviewed. The checkout counterexample and
missing frozen-source delivery prompted explicit user decisions; both approved
narrow solutions are now implemented.

Independent final review verification: checkout 11 passed; frozen instructions
3 passed; retry admission 13 passed; retry profile 11 passed; stop compatibility
6 passed with its native SDK test skipped in that review context. The parent
subsequently ran the actual-SDK compatibility test successfully.

Final spec total: **0 mandatory product-code fixes**. Closure still requires
retained final-profile lifecycle, full regression, real-provider source-only and
affected containment/boundary results, accurate qualification/completion records,
and commit/evidence reachability from origin/main. The passing code review alone
is not acceptance, disposition or G4 PASS.

## Independent evidence audit

A further fresh read-only audit verified all nine parent-audit evidence hashes,
all recorded lifecycle/provider invocation and final source hashes, compatible
SDK/sidecar/CLI/graph/runtime identities, actual command-completion stdout in
the current boundary positives, actual source-directed provider file changes,
and absence of every abandoned-only canary from replacement provider inputs.
It confirmed historical G1/G2/G3/#21 records are unchanged.

The audit identified one literal AC8 coverage gap: serial safety-boundary tests
did not themselves demonstrate every requested concurrent interleaving. Three
focused tests now hold cessation confirmation, owned retirement and ordinary
provisioning while retry/old administrative calls compete on independent store
connections. All three passed; no product change was needed. The initial race
fixture expected an admission exception for stale ordinary ingress; it now
expects the product's existing `StaleAttempt`, with the initial failure retained.

The evidence reviewer subsequently read the new tests and retained log and
confirmed that they close the literal AC8 gap. No unsupported acceptance claim,
profile mismatch or new blocker remained beyond final SDK accounting and
commit/evidence reachability.
