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

## Governing documents

The current governing pair is:

- [Target responsibility and boundary design v0.4](docs/governing/broodling-target-responsibility-boundary-design-v0.4.md).
- [Implementation and dependency plan v0.4](docs/governing/broodling-implementation-dependency-plan-v0.4.md).

V1 is deliberately single-host and no-effect: one Work Unit exclusively owns a dedicated disposable
worktree, candidate mutations are graph-ordered, and independent assurance readers are read-only.
An incomplete/stopped/lost Attempt and its worktree are abandoned; a replacement starts from the
original admitted state without reusing the abandoned candidate, decisions/directives, evidence or
acceptance. Reviewer execution is fresh with a narrowly controlled automatic-context profile.

## Status

The v0.4 target and dependency sequence are documented. **New G1-V1 qualification witnesses are
NOT RUN; the narrowed profile is not yet qualified.** Product language, framework, storage and
product implementation remain unresolved. Exact effects/reconciliation and cross-Attempt
reuse/recovery are deferred capabilities, not V1 prerequisites.

The historical [G1-core review](qualification/p1/issue-7-g1-core.md) remains **BLOCKED** under v0.3:
Q2 and the bounded Q3 fixture passed; Q1/Q4/Q5/Q6 did not. [Q7/G1-effects](qualification/p1/issue-6-q7.md)
remains independently **BLOCKED**. Completion of issue #7's review was not a gate pass. The new
plan distinguishes reusable findings from the fresh witnesses required for V1.

## Baseline and qualification provenance

The unchanged [P0/G0 inventory](docs/baseline/p0-g0-inventory.md) links the original v0.3 target and
plan, all 25 invariant fixtures, historical-protection map and recorded input hashes. G0 PASS means
specification completeness, not integration or semantic qualification. The v0.3 input files and
historical evidence are preserved; v0.4 supersedes them as governing target/plan, not as evidence.

The [P1 external SDK/sidecar harness](qualification/p1/README.md) and issue-scoped reports/records
retain the original experiments. Use the originating commits identified in the v0.4 plan for
reproduction; the controlled provider leaf changed between investigations. Neither those bounded
findings nor this documentation revision establish V1 readiness.
