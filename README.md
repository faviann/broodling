# Broodling

Broodling implements the single-work-unit execution system.

## Purpose

Broodling is responsible for reliably executing **one admitted software-development work unit**. A work unit arrives already selected and ready to run; Broodling's job is to carry it through to a definite outcome.

## Scope

In scope:

- Executing a single admitted work unit.
- Reliability of that execution.

Explicitly **out of scope**:

- Higher-level scheduling.
- Backlog selection.
- Dependency waiting.
- Multi-project orchestration.

## Status

Documentation baseline established against the accepted v0.3 target. The shared P1 harness is
available. Issue #3 executed Q1, Q2, and Q6 against the pinned real SDK/sidecar path: Q2 passes,
while Q1 and Q6 are blocked on a supported completed-occurrence recovery surface. Issue #4
qualifies Q3 PASS for fail-closed routing and sticky within-run obligations through the same real
integration. G1-core therefore remains blocked by Q1/Q6. Product language, framework, storage,
and product implementation remain deferred. Issue #5 additionally qualifies Q4 and Q5 as blocked:
the selected local profile lacks a trusted synchronous current-candidate/evidence handoff and an
exhaustive reviewer automatic-context control/provenance contract.

See the [P0/G0 baseline and fixture inventory](docs/baseline/p0-g0-inventory.md) for the unchanged governing inputs, all 25 target invariants, P1 witness specifications, historical-protection map, and G0 review. G0 PASS records specification completeness, not executed integration or semantic tests.

The [P1 external SDK/sidecar qualification harness](qualification/p1/README.md) supplies the
shared custom-graph fixture for the later Q1–Q7 witnesses. Its boundary run establishes harness
availability only; both G1 gates and all witness conclusions remain unevaluated.
