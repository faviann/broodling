# Migration-wide parity and simplicity review

This is the independent review gate required by [#130](https://github.com/faviann/broodling/issues/130)
before [#140](https://github.com/faviann/broodling/issues/140) starts. It is not
deployment, a live-provider qualification or authority to switch existing state.

The fixed Python comparison is commit
`b3f61a96c40401722ec16fc361958d1690982e02`, tree
`7e061c9314d16785c070483799d0e8057a42ba31`. The
[baseline record](130-baseline-validation.md) distinguishes the exact-baseline
404-test runs from later runs. The [parity map](130-parity-map.md) connects all
eight behavioral slices to source, owning witnesses and preserved invariants.

## Initial independent review

All eight behavioral issues #132–#139 were closed before two fresh independent
reviewers inspected the complete migration at
`d7239041d88d0bb05f704266fc40c4b21e9d16ba`. Neither relied on earlier slice PASS
reports. They inspected the frozen Python, actual issue requirements, .NET
production code and witnesses, and cross-slice composition. Full suites were
not rerun by the reviewers. Their separate reports are retained in
[the umbrella review comment](https://github.com/faviann/broodling/issues/130#issuecomment-5782505891).

### Standards / simplicity

**COMPLETE / PASS**, no hard violation or blocking design defect. Three P3
duplicated-code observations were explicitly judgments, not demonstrated defects:

1. Repeated delivery authorization in dispatch and replacement. Resolved by
   calling existing `Closability.AuthorizeDelivery`, preserving each boundary's
   refusal classification and the separate GitHub-host check.
2. Repeated retained-material inspection in dispatch and replacement. Deliberately
   retained locally: the common material fingerprint is already shared, while
   dispatch constructs instruction JSON and replacement verifies retry authority;
   their failures also have different meanings. A new reader plus translated
   errors would add indirection to these short loops without simplifying the
   caller operations. Both still validate retained ownership/digests. This is a
   disposition of a nonblocking design preference, not a claim it was extracted.
3. Repeated unmanaged UTF-8 vector ownership in Git spawn and the Codex launcher.
   Resolved with one internal `Utf8Vector`, including constructor-failure cleanup.
   Git retains explicit NUL refusal before allocation; spawn/exec, PID, file
   descriptors and child-surviving exclusion are unchanged.

### Spec / parity

**COMPLETE / FAIL**, two P2 behavioral gaps. The combined gate therefore failed;
#140 remained unstarted while repairs were made:

1. The existing-target readiness operation was still Python-only. The callable
   .NET operation and thin `check-target` command now preserve the existing
   selected-container, pins/hashes, credentials, isolation, mounts, loopback,
   hosted UID/GID and discovery checks. See
   [the readiness reference](../implementation/dotnet-target-readiness.md).
   Controlled command/HTTP witnesses do not establish a real target's readiness.
2. Replacement encoding allowed a lone surrogate and U+FFFD to alias source
   identity and rewrote provenance. A real SQLite source-custody test failed twice
   with `refused=False; retainedAfterMalformed=1; aliased=True;
   returnedLocatorRewritten=True`. The frozen Python refused before retention.
   An isolated encoder probe produced `EFBFBD` for both inputs; strict encoding
   refused the malformed input. This directly established the cause without
   speculative hypotheses or broad instrumentation. Strict validation now guards
   source metadata, raw references/pins, identity hashing and SQL string binding.
   Three further RED cases proved the same SQL-boundary cause in retry lookup,
   historical handback and abandonment reasons. Well-formed identities, binary
   payloads, schema definitions and historical .NET fixtures remain unchanged.
   See [identity custody](../implementation/dotnet-identity-custody.md).

A final same-cause public store-path probe failed in all three lifecycle cases:
malformed `InitializeStore` created the legitimate U+FFFD file, `OpenStore`
accepted it, and `UpgradeStore` changed its historical-schema bytes. SQL string
validation was too late for filesystem paths. `PhysicalPaths.Resolve` now refuses
malformed UTF-16 before traversal; the existing store boundary maps that refusal
to `invalid_store_path`. The three regressions also require unchanged bytes and
directory entries on refusal and successful valid U+FFFD/supplementary-character
paths. This adds no restriction on well-formed paths or new schema requirement.
The focused `MalformedStorePathCannotOperateOnReplacementCharacterFile` command
failed 3/3 before production changes (2.764s), then again without rebuilding
(3.016s).

## Validation and failure history

Before integration, encoding repairs passed 270 .NET tests (25.924s), Release
with zero warnings/errors (30.28s), and the unchanged Python reference's 404 tests
(106.77s). Simplicity changes passed 259 .NET tests (56.639s) after removing an
unnecessary forged-state test; its production code had already passed Release
with zero warnings/errors. Readiness passed 66 focused tests (2.486s), 323 full
.NET tests (27.102s), clean Release (23.77s), and unchanged Python 404 (85.81s).
These are separate candidates, not the combined-candidate result.

The integrated repair candidate, including the store-path correction, passed:

- Focused store lifecycle: **22 passed**, zero failed/skipped, **3.854s**.
- `dotnet test --solution Broodling.sln`: **341 passed**, zero failed/skipped,
  **26.530s** reported test duration.
- `dotnet build Broodling.sln --configuration Release`: **0 warnings/errors**,
  **26.33s**.
- Unchanged Python reference, `python -m pytest tests`: **404 passed in 113.37s**.
  This is the integrated worktree's reference run, not a new exact-baseline run.

The .NET commands use `MSBUILDDISABLENODEREUSE=1`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0`, `UseSharedCompilation=false`, and
`BROODLING_TEST_PYTHON=/home/faviann/repos/broodling/.venv/bin/python`.

The first encoding full run had **266 passes and one failure** (27.307s):
`InvocationTests.ThinStopHandsBackQuarantineAndSubmitResumeStatusHistoryRetainAbandonment`
raised `GitHubSourceError` during controlled primary-issue acquisition. The
subsequent encoding pass followed the independent SQL text repair and establishes
neither cause nor repair of this acquisition failure. The original HTML report
and bounded diagnostic artifacts are retained locally under
`/home/faviann/.cache/broodling-tests/130-encoding-repair/evidence` and
`/home/faviann/.cache/broodling-tests/diag-130-acquisition-8rpSusGw`.

A fresh bounded diagnosis used H's already-built, unchanged Debug test/application
binaries and launcher, checking their hashes before and after. The acquisition
source and invocation witness are byte-identical to the failing repair's source.
The initial focused case passed (2.124s); all 100 repetitions passed (389.598s
wall total); the unfiltered H suite passed 257/257 (26.572s). This is the same
acquisition path in an earlier stable build, **not the exact failing binary or
267-test suite**. A read-only 5ms watcher retained controlled gh/response inputs
before fixture disposal and may affect timing. No reproduction rate, underlying
exception, cause or repair was established; no production fix or test suppression
was made. A recurrence should preserve the exact build/report and fixture before
minimization. This bounded nonrecurrence does not erase the original failure.

G's earlier one-off Git-config and provider executable-format failures remain
separate observations in [its validation record](../implementation/dotnet-receipt-completion.md).
No common cause, retry policy or suppression is inferred from subsequent passes.

## Final gate status

**PASS on 22 September 2026**, at reviewed implementation candidate
`5a1f01cd15ad32d5eec028be19c5b9b97d9af903`. Two new independent reviewers compared
the whole migration, including all eight slices and integrated repairs, against
the fixed Python baseline. Both completed their assigned scope without relying
on earlier PASS reports. The candidate was unchanged throughout their reviews.

### Final Standards / simplicity

**COMPLETE / PASS**. No blocking standards violation. The reviewer found the
single SQLite session, explicit transactions, callable lifecycle operations,
thin host, SDK bridge and selected-child native shim proportionate to the scope.
The two remaining P3 observations are nonblocking duplication preferences:

- Retained-material validation remains local for the reasons recorded above;
  the reviewer explicitly judged retaining these short loops defensible.
- Canonical loopback validation repeats three conditions in the host config
  parser and independently callable readiness operation. Both currently agree
  and have owning refusal witnesses. Sharing solely this small predicate would
  extend the core/host API while still requiring distinct boundary errors. Keep
  it local unless an actual policy change or additional caller justifies a shared
  abstraction. No current defect or documented-standard violation was found.

### Final Spec / parity

**COMPLETE / PASS, zero actionable findings**. The reviewer inspected every
parity-map row, frozen production and specific baseline witnesses, .NET witnesses
and cross-slice composition. No missing requirement, incorrect migrated behavior
or unrequested behavioral expansion requiring repair was found. Independent
in-memory SQLite probes confirmed currentness/Attempt replacement guards,
receipt refusal and atomic completion, completed-work refusal, and v1–v6
definition/manifest hashes with preservation of every retained row through
upgrades and valid foreign keys. Standards independently checked those fixture
hashes and row preservation too; these probes supplement managed upgrade tests.

Both reviewers fetched actual issue bodies but could not reach GitHub dependency
API endpoints. The root independently rechecked the actual #130 subissue list
(#132–#139 closed, #140 open) and #140's closed G/H dependencies. Neither reviewer
reran the full suite/build or treated existing pass counts as review proof.

**Combined decision:** the all-eight-slices and fresh parity/simplicity start
gate is satisfied. #140 may now perform its focused source retirement and
release/guidance adaptation. It does not inherit authorization for deployment,
operational state switching, import, takeover or deletion of existing state.

Throughout the migration, controlled released-SDK/native tests, stub PR receipts,
historical deployment validation and P5 semantic-quality evidence remain distinct.
No-effect stable success is still refused. Every dispatched Attempt remains
quarantined; terminal status is not physical cessation. P5 remains scoped FAIL,
with independent human/operator review of the exact accepted revision required.
No live provider run, target deployment, old-state import, in-flight takeover or
operational cutover is part of this review.
