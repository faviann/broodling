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
- **W2 is satisfied on the recorded profile** by the existing host-containment evidence; the historical v0.4 W2 FAIL remains unchanged. Missing-evidence sufficiency is covered by W3/W6, model-supplied applicability IDs are non-authoritative, and the delayed-observer requirement is removed.
- **W3 PASS** is recorded by issue #9's [actual assurance-graph qualification](qualification/v1-p1/issue-9-w3.md) on the compatible profile.
- **W4 PASS** and **W6 PASS** are recorded by issue #10's [controlled-reviewer and normal-final-result qualification](qualification/v1-p1/issue-10-w4-w6.md) on the compatible profile.
- **W5 is satisfied** by the existing issue-#8 abandon/restart evidence.
- **W7 is satisfied** by the existing issue-#8 no-effect evidence.

The separate [G1-V1 gate review](qualification/v1-p1/issue-11-g1-v1.md) is **COMPLETE with G1-V1 PASS** for the narrowed single-host, no-effect V1 profile. That gate qualifies the V1-P1 evidence boundary only. Exact effects/reconciliation and completed-run recovery/cross-Attempt reuse remain deferred capabilities.

**V1-P2 is complete with [G2-V1 PASS from #16](qualification/v1-p2/issue-16-g2-v1.md).** The [V1-P2 implementation](docs/implementation/v1-p2-admission-nucleus.md) covers Work Unit identity, source entitlement, immutable Contract revisions, no-effect admission, one current Attempt with B1 and an exclusive worktree, and durable Attempt↔run correlation including acknowledgement loss after graph-authorized mutation.

Issue #17 adds the [product assurance graph](docs/implementation/v1-p3-assurance-graph.md), with structural candidate generations, sticky typed directives, bounded repair and fail-closed required controls. `SubmissionCoordinator.submit_assurance` constructs the product protocol from the frozen Contract and requires the explicit qualified Codex profile. Issue #18 supplies Contract-derived evidence integration and product reviewer controls; final assurance custody remains the #19 boundary. V1-P3 is complete with [G3-V1 PASS](qualification/v1-p3/issue-20-g3-v1.md).

Issue #18 adds [explicit immutable mechanical-evidence declarations and the deterministic read-only evidence leaf](docs/implementation/v1-p3-evidence-review.md). Its [integration record](qualification/v1-p3/issue-18-evidence-review.md) retains actual SDK evidence, containment and real reviewer controls. The earlier blocked and pre-integration records remain historical evidence.

Issue #19 adds [current final-assessment capture and minimal durable custody](docs/implementation/v1-p3-final-assurance.md). Explicit final/B1 material selection is frozen in a new admitted Contract revision and exercised through a fresh Attempt; older revisions and Attempt bindings remain unchanged. Its [completion evidence](qualification/v1-p3/issue-19-final-assurance.md) covers exact bytes/absence, required observations/rationale and refusal of incomplete or lost custody. The earlier selection blocker remains historical. The separate fresh read-only [#20 review records G3-V1 PASS](qualification/v1-p3/issue-20-g3-v1.md) for the integrated current configuration.

Issue #21 is complete with [irreversible abandonment, bounded physical cessation and owned retirement](docs/implementation/v1-p4-abandonment.md). Its [acceptance and requalification record](qualification/v1-p4/issue-21-abandonment.md) retains real-provider controller-loss, live-sibling, mutation/read-only and no-effect controls. Canonical shared Git metadata under the provider's writable `/tmp` root is rejected before dispatch. Earlier cessation and shared-Git blocker records remain historical.

Issue #22 is complete with [explicit original-B1 replacement](docs/implementation/v1-p4-replacement.md), durable retry identity, fresh reserved provider homes, an implementer-only binding for exact frozen entitled instructions, and a narrow refusal of Git checkout transformations. Its [acceptance and requalification record](qualification/v1-p4/issue-22-replacement.md) distinguishes controlled SDK mechanics from real-provider source and boundary evidence, and records the full regression plus corrected affected-suite rerun.

Issue #23 implements the [normal no-effect disposition boundary](docs/implementation/v1-p4-disposition.md), but remains **BLOCKED** on its [required actual-provider repair witness](qualification/v1-p4/issue-23-real-provider-blocker.md). The original two finite real-provider runs and the [two-run natural CSV follow-up](qualification/v1-p4/issue-23-natural-followup.md) all completed clean-only paths; the bounded follow-up has stopped. A [single seeded diagnostic](qualification/v1-p4/issue-23-repair-diagnostic.md) exercised actual downstream repair successfully, but its controlled implementer earns no integrated-witness credit. The [implementation and verification record](qualification/v1-p4/issue-23-disposition.md) does not claim issue completion or G4 PASS. #24 has not begun. Effects remain unimplemented.

The historical [G1-core review](qualification/p1/issue-7-g1-core.md) remains **BLOCKED** under v0.3. Q2 and the bounded Q3 fixture retain their historical scoped passes; Q1/Q4/Q5/Q6 remain blocked, and [Q7/G1-effects](qualification/p1/issue-6-q7.md) remains independently blocked. v0.5 does not relabel those historical results.

## Product code

The Broodling product package is `broodling/`, a Python 3.13 package whose admission/storage code uses the standard library; run submission additionally requires the exact G1-V1 qualified Zeroshot SDK/sidecar. It owns one SQLite database holding the durable admission and Attempt facts. Provisioning an Attempt worktree runs the local `git` binary — host-local administrative setup, not a delivery effect — and the narrow submission adapter invokes the official SDK/matching sidecar. It imports the SDK lazily, uses public submission and forward observation of a current correlated run, and does not mirror the Zeroshot RunLedger.

```bash
python -m pytest tests
python -m unittest discover -s tests
```

The [V1-P2 implementation record](docs/implementation/v1-p2-admission-nucleus.md) states the selected Python/SQLite versions, the qualified Zeroshot SDK/sidecar version boundary the product configuration records, the schema, the B1/worktree policy and the retained implementation evidence.

The Attempt tests provision real Git worktrees and therefore need a durable workspace root, which cannot be `/tmp`. They default to `~/.cache/broodling-tests`; set `BROODLING_TEST_WORKSPACE_ROOT` to choose another durable directory.

## Baseline and qualification provenance

The unchanged [P0/G0 inventory](docs/baseline/p0-g0-inventory.md) links the original v0.3 target and plan, all 25 invariant fixtures, the historical-protection map and recorded input hashes. G0 PASS means specification completeness, not integration or semantic qualification.

The [P1 external SDK/sidecar harness](qualification/p1/README.md), issue-scoped reports/records, the v0.4 governing pair, G0-v0.4, and issue #8 evidence are retained unchanged. Use the originating commits identified by the governing documents for reproduction; bounded historical findings do not establish broader V1 or production readiness.
