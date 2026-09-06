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
available, but product implementation and Q1–Q7 qualification have not started; product language,
framework, and storage remain deferred.

See the [P0/G0 baseline and fixture inventory](docs/baseline/p0-g0-inventory.md) for the unchanged governing inputs, all 25 target invariants, P1 witness specifications, historical-protection map, and G0 review. G0 PASS records specification completeness, not executed integration or semantic tests.

The [P1 external SDK/sidecar qualification harness](qualification/p1/README.md) supplies the
shared custom-graph fixture for the later Q1–Q7 witnesses. Its boundary run establishes harness
availability only; both G1 gates and all witness conclusions remain unevaluated.
