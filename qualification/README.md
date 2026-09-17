# Qualification archive and current validation

**Historical evidence only; not qualification of the current native integration.**
Read the [current architecture and P5 plan](../docs/governing/current.md) and
[current test guidance](../tests/README.md) first. Reports, records, hashes and
verdicts below retain their original source/profile meaning. This README is
mutable navigation; it cannot transfer an old pass to current code.

Archived campaign scripts are not supported current qualification commands.
Some still reference deleted fixtures or APIs, including the #18 evidence writer.
Do not import or run them as a current campaign merely because a file survives.
A guarded writer may reserve an empty output before a missing dependency fails;
that is not evidence. Only the default suite and focused safety test below are
current validation commands. Dated PR/issue sections describe historical changes,
not present campaign availability.

## PR #48 ownership correction (15 September 2026)

PR #48 retired the #17 control/adversarial entrypoints and their controlled
provider fixture because they tested Zeroshot control-flow/response mechanics.
The intermediate #18 writer retained three integration witnesses at that time;
PR #50 subsequently removed the custom evidence fixtures and qualified-integration
API it depended on. It is now historical tooling, not a supported native campaign.
See the [dated ownership audit](../docs/implementation/pr48-ownership-audit.md)
and [adopted native boundary](../docs/implementation/zeroshot-native-integration.md).
No replacement analyzer or empty PASS record is supplied. The reviewed pre-audit
PR tree is d2d34ae30a2e573e23a61379184b623c988944f5; historical reproduction needs
the actual tree and dependencies named by the relevant record.

## Issue #36 decision (12 September 2026)

The following describes the safety change at that historical revision, before
native integration. The current retained safety checks are described separately
below; the old campaign behaviors are not current support claims.

The five entry points listed below are now inert on import. Path setup, fixture
imports, argument parsing and campaigns run only through their guarded main
entry points. This is prospective maintenance of executable copies, not a new
qualification framework or a change to any product guarantee.

The two P4 lifecycle commands now require an explicit positional output path.
They exclusively create that file **before** importing fixtures or starting the
campaign. Missing arguments, existing files (including committed evidence),
symlinks and unusable parent directories fail before campaign execution. There
is no overwrite flag or committed-evidence default. Exclusive creation, rather
than a separate existence check, also closes the check/write race. An interrupted
or failed invocation can leave an empty or incomplete new output; that is not
qualification evidence and a subsequent run must use a fresh filename.

The #14 regression/evidence CLI and #17 adversarial campaign retain their
intentional-execution behavior, including #14 evidence's required `--output` and
#17's fixed volatile output. Their import-time work has moved behind guards.
No historical evidence JSON, logs, reports, governing documents, verdicts, or
product/fixture implementations are rewritten by this change.

### Why change executable source?

The issue owner explicitly authorized a minimal safety fix with disclosed
provenance consequences. Documentation, test-collection exclusions, or an outer
wrapper would leave the original `.py` files directly importable and P4's direct
commands unsafe. Renaming or moving originals to inert files breaks existing
paths and #22's explicit self-source inventory unless extra loader/manifest
machinery is added. Those alternatives do not give a smaller complete fix.

Instead, current executable copies receive guards and the P4 output preflight.
The exact original bytes remain in Git at the immutable source commits below.
This is consistent with retaining the meaning and bytes of historical evidence,
not a claim that edited executables still have the same source identity.

## Historical provenance is not current-source equivalence

The [v0.5 target](../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md)
preserves earlier evidence verbatim; the
[G4 addendum](../docs/governing/v0.5-g4-evidence-addendum.md) preserves earlier
records and verdicts rather than relabeling them. The
[#17 report](v1-p3/issue-17-assurance-graph.md) additionally retains its original
reproducer byte-for-byte. Those reports remain unchanged and describe their
recorded trees. Use the original commit for the promised historical source, not
the edited executable linked from a current checkout.

These source identities were verified before the fix against current main
`ebeb31c` and the listed originating commits:

| Script | Original source commit | Original SHA-256 |
| --- | --- | --- |
| `v1-p2/issue14_evidence.py` | `40ca55d8fd1db2837206f353908b96e81bac66fe` | `5af9044ff67e2b2c657d566333889ce91739a1e3c78d607328762c190e5c0068` |
| `v1-p2/issue14_regression.py` | `40ca55d8fd1db2837206f353908b96e81bac66fe` | `049d60379351fce7bfe632b563be4031601112b3acdea01b0a93cee771483b96` |
| `v1-p3/issue17_adversarial.py` | `25c496df9ed33715d3cbbff18d77f07a581ba565` | `11ff02b0060dfd118f84f22ca94d3238f4de7670cbfd9d34ba27dd18ed09ec6b` |
| `v1-p4/issue21_lifecycle.py` | `cb9b9a6e3c80bbe6c5c84a225d8d06601d737138` | `4615710bc272e9cfcfa0d60c2490159a28c0325afe3ff6bdcc36ffdd2cad9050` |
| `v1-p4/issue22_lifecycle.py` | `854555f4be0b5cfe925cc2df53a123737823fb79` | `198f2c823f7ac04bacd29d26a71fac56377d924fbf544fa0ddb5651d607952c8` |

In particular, #17's retained `probeSourceSha256` matches its original script
above. #22's retained `sourceSha256` and `finalSourceSha256` match its original
script, **not** the new guarded one. Fresh executions record the new script
identities through the same existing hashing logic. Do not update the historical
hashes or cite an old record as having been generated by the edited source.
The retained #22 lifecycle record's `productCommit` is the pre-commit parent
`cb9b9a6`; its matching reproducer was published in `854555f`, as verified by the
source hash. Do not assume that `productCommit` alone locates the harness.

For example, read the exact original without executing or importing it:

```bash
git show 25c496df9ed33715d3cbbff18d77f07a581ba565:qualification/v1-p3/issue17_adversarial.py
```

An exact historical reproduction requires that historical product/test tree
and its recorded qualified dependencies, not just an old script copied onto
current code. Old trees retain the old hazards: never import those unguarded
scripts, and give old P4 commands an explicit fresh output. This fix cannot
retroactively change an immutable historical checkout.

## Current supported validation

From the current repository root, install the pinned release SDK and run the
default tests, as described in the [test README](../tests/README.md):

    python -m pip install -e '.[test]'
    python -m pytest tests

The default suite includes controlled-provider checks through the published
SDK/native seam. There is no old opt-in campaign lane to select. These checks do
not qualify real-provider semantic quality, a live DirectTarget PR delivery,
sandbox escape resistance or physical cessation. Missing dependencies or skipped
coverage are not a passing integration result.

For historical reproduction, use a separate checkout of the relevant recorded
product/test tree and its exact dependencies/profile, not an old script copied
onto main. Inspect its commands first, use disposable resources and fresh outputs,
and account for the original unsafe import/overwrite behavior described above.
Do not overwrite retained evidence or use a fresh run to relabel an old verdict.
No historical campaign is required for this documentation alignment.

## Focused safety validation

The current tests/test_qualification_entrypoints.py retains import-inertness
checks for the #14 evidence/regression and #21/#22 lifecycle script copies, with
execution-like arguments, unchanged import search paths and no campaign/file
writes. It also checks missing, occupied, symlinked and unusable P4 output paths
using temporary stand-ins before any campaign can run. It does not invoke an
archived campaign, fabricate a successful campaign record or qualify its self-hash.

    python -m pytest tests/test_qualification_entrypoints.py -q

This is entrypoint/record-overwrite safety only, not SDK or product qualification.
Historical evidence JSON, reports and recorded verdicts are unchanged.

## Historical lane selection (issue #39)

At that revision, issue19_capture.py, issue21_lifecycle.py, issue22_lifecycle.py
and issue23_controls.py selected BROODLING_ZEROSHOT_LANE before importing their
campaign test classes. The split belonged to the former SDK/custom-assurance
suite, not the native integration. The
[pre-native test notes](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/tests/README.md)
retain that historical context; the current default suite has no such lane.

As with the #36 guards, original executable bytes remain in Git at the commits
named by their records. Current executable copies are not offered as those
historical source identities. No campaign, fixture or retained record is changed
by this navigation correction.
