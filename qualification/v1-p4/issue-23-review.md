# Issue #23 independent review

Fixed baseline: `854555f4be0b5cfe925cc2df53a123737823fb79`.
Fresh separate read-only Codex contexts reviewed the complete product diff and
new untracked implementation, test, harness and documentation files.

## Standards

**0 hard violations; 0 actionable smell findings.** The review found the minimal
store and responsibility split compatible with target v0.5 §§3, 5–6 and 10 and
the implementation plan's V1-P4 boundary. Disposition references P3 custody;
the start marker contains no runtime history; cessation remains with #21.
Checks in Python and SQL serve separate enforcement responsibilities.

The existing README status must be updated when issue acceptance is known.
Standards review is not evidence-completeness approval.

## Spec

**0 confirmed product-spec defects or scope additions.** The durable start marker
and host lock distinguish live ownership from interruption. Arbitrary retained
P3 custody cannot authorize success. Currentness and abandonment exclusion share
the disposition transaction. Python and SQL require the same immutable admitted
Contract's explicit empty effect set. Completed disposition removes current
authority and prevents later abandonment or new Attempt allocation.

The review identified two pending evidence items: explicit missing/unsupported
effect declarations and retry directly against a completed Attempt. Foundation
tests now reject missing, null, object, nonempty, string and boolean declarations;
the public completion test directly rejects retry of its successful Attempt.
Executed final controls must still substantiate those additions.

The reviewer found the disclosed staged provider fixture within the finite
integration scope. Source inspection alone cannot establish that it takes the
required real repair path. Actual execution, current regression, retained evidence
and origin/main reachability remain necessary before completion.

## Independent evidence audit

A further fresh read-only context checked the raw actual-provider records,
preserved cleanup observations, current product hashes and development logs.
It confirmed byte-identical Contracts and only clean-role occurrences in both
actual runs; no repair or renewed repair assurance is present. All recorded
product hashes match current bytes, and graph/profile files match the baseline.
It verified unchanged original JSON/SQLite hashes after the separate cleanup.

The audit confirmed the corrected negative source-change predicate and noted
that its read-only audit predates the later durable-store harness correction.
Neither actual execution qualifies that latest harness configuration. The
developmental control record explicitly fails its stable-source requirement and
cannot substitute for current controls. SDK-free verification passed 373 tests;
56 SDK tests were skipped. Final SDK controls/regression were still running at
the time of this review. No new product counterexample was found.

The reviewer subsequently audited the final controlled result: 10 tests, 19
retained cases, 519.383 seconds, with all source hashes stable and current.
It verified all five SIGKILL outcomes, one observer for duplicate callers, actual
lock contention and abandonment after owner death, both stop orders, A1/A2
isolation and exact justification after cleanup. No additional evidence
contradiction or product-spec defect was found. The broader SDK regression is
recorded separately when complete.

Parent verification subsequently completed the broader SDK regression: 419 tests
and 2,521 subtests passed in 2,125.24 seconds. Together with the independently
retained 10-test disposition suite, every current test is covered by passing
SDK evidence. Product/test source hashes remain unchanged. This closes the
mechanical verification work; it does not close the actual-provider blocker.

The evidence supports keeping #23 blocked. Neither code review nor controlled
mechanical tests can establish the missing real repair witness or G4-V1 PASS.
