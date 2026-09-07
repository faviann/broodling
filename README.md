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

- [Target responsibility and boundary design v0.5](docs/governing/broodling-target-responsibility-boundary-design-v0.5.md).
- [Implementation and dependency plan v0.5](docs/governing/broodling-implementation-dependency-plan-v0.5.md).

The preserved v0.4 pair and [G0-v0.4 review](docs/governing/g0-v0.4-review.md) remain provenance; v0.5 supersedes v0.4 as the governing target/plan without modifying the earlier documents or qualification evidence.

V1 is deliberately single-host and no-effect. One current Attempt exclusively owns one dedicated disposable worktree. Only graph-authorized mutating executions change candidate source; assurance readers are read-only and the qualified host profile contains lingering/concurrent writers. Under v0.5, that structure establishes candidate applicability: an assurance occurrence applies to the candidate state left by the most recent preceding graph-authorized mutation, and a later mutation requires fresh assurance before acceptance. V1 does not require an independent candidate seal/hash/manifest, racing observer, or model-supplied applicability identifier for that relationship.

Required evidence remains separately mandatory and must be available/sufficient when the graph relies on it. Missing raw material or inadequate evidence must fail closed; this does not imply a general candidate-provenance subsystem.

An incomplete/stopped/lost Attempt and its worktree are abandoned; a replacement starts from the original admitted state without reusing the abandoned candidate, decisions/directives, evidence or acceptance. Reviewer execution is fresh with a narrowly controlled automatic-context profile.

## Status

Issue #8's preserved [V1 qualification report](qualification/v1-p1/issue-8-w1-w2-w5-w7.md) at `7931ac9` historically records **W1 PASS, W2 FAIL, W5 PASS and W7 PASS** against the v0.4 checklist.

Under the corrected v0.5 witness scope:

- **W1 is satisfied** by the existing issue-#8 evidence.
- **W2 is satisfied on the recorded profile** by the existing host-containment evidence; no standalone W2 rerun remains. The old missing-evidence case moves to W3/W6 evidence sufficiency, model-supplied applicability IDs are non-authoritative, and the delayed-observer requirement is removed.
- **W5 is satisfied** by the existing issue-#8 evidence.
- **W7 is satisfied** by the existing issue-#8 evidence.
- **W3 PASS** is recorded by issue #9's [actual assurance-graph qualification](qualification/v1-p1/issue-9-w3.md) on the compatible profile.
- **W4 and W6 remain NOT RUN.**

**G1-V1 is not passed.** Existing issues #10 and #11 remain the qualification/gate path; product implementation remains blocked until the gate passes. Exact effects/reconciliation and cross-Attempt reuse/recovery remain deferred capabilities.

The historical [G1-core review](qualification/p1/issue-7-g1-core.md) remains **BLOCKED** under v0.3. Q2 and the bounded Q3 fixture retain their historical scoped passes; Q1/Q4/Q5/Q6 remain blocked, and [Q7/G1-effects](qualification/p1/issue-6-q7.md) remains independently blocked. v0.5 does not relabel those historical results.

## Baseline and qualification provenance

The unchanged [P0/G0 inventory](docs/baseline/p0-g0-inventory.md) links the original v0.3 target and plan, all 25 invariant fixtures, the historical-protection map and recorded input hashes. G0 PASS means specification completeness, not integration or semantic qualification.

The [P1 external SDK/sidecar harness](qualification/p1/README.md), issue-scoped reports/records, the v0.4 governing pair, G0-v0.4, and issue #8 evidence are retained unchanged. Use the originating commits identified by the governing documents for reproduction; bounded historical findings do not establish broader V1 or production readiness.
