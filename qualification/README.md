# Qualification entry-point safety and reproduction

## PR #48 ownership correction (15 September 2026)

The current #17 control and adversarial entrypoints and their controlled provider
fixture are retired. They tested Zeroshot control-flow/response mechanics rather
than a Broodling-owned integration boundary. No replacement graph analyzer or
empty PASS record is provided. Use the original recorded Git tree for historical
reproduction; the reviewed pre-audit PR tree is d2d34ae30a2e573e23a61379184b623c988944f5.
Historical records, reports and hashes remain unchanged.

The current #18 writer retains five explicitly scoped integration witnesses and
can be invoked with an explicit fresh output path. See the
[ownership audit](../docs/implementation/pr48-ownership-audit.md). The issue #36
section below records the earlier safety change; its statements about #17's
continued executable behavior describe that historical revision, not current code.

## Issue #36 decision (12 September 2026)

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

## Intentional fresh reproduction on current code

Use the exact SDK/sidecar and host profile required by the relevant record.
From the repository root, choose a fresh output directory whose parent exists:

```bash
SDK_PYTHON=/path/to/qualified-venv/bin/python
FRESH_DIR="$(mktemp -d)"

"$SDK_PYTHON" qualification/v1-p4/issue21_lifecycle.py "$FRESH_DIR/issue21.json"
"$SDK_PYTHON" qualification/v1-p4/issue22_lifecycle.py "$FRESH_DIR/issue22.json"

"$SDK_PYTHON" qualification/v1-p2/issue14_regression.py
"$SDK_PYTHON" qualification/v1-p2/issue14_regression.py --legacy-b1-guard
# The legacy guard is a negative control: expect exit 1.
"$SDK_PYTHON" qualification/v1-p2/issue14_evidence.py --output "$FRESH_DIR/issue14.json"

"$SDK_PYTHON" qualification/v1-p3/issue18_evidence.py --output "$FRESH_DIR/issue18.json"
# Choose a fresh explicit output; never overwrite retained historical records.
```

These commands intentionally launch the supported current campaigns. The #18
writer now uses the integration-only scope described above.
The P4 scripts retain their existing verdict logic: failures or skipped tests
cannot report `mechanicsPassed: true`; #22 also requires invocation/final source
hashes to match. Fresh results are separate observations, not replacements for
retained G1–G4 evidence or a new gate verdict.

## Focused safety validation

`tests/test_qualification_entrypoints.py` checks cold imports with bare and
execution-like arguments, unchanged import search paths, and no campaign/file
writes. It exercises missing, occupied, symlinked and unusable P4 output paths,
including the actual committed evidence paths, before any campaign can run.
Fresh P4 CLI checks use explicit campaign/SDK-identity doubles to verify record
writing, success/failure/skip exit behavior, and #22's actual current self-hash.
They are entry-point tests, **not SDK qualification evidence**; their temporary
records are labeled as doubles and are not retained as gate artifacts.

```bash
python -m pytest tests/test_qualification_entrypoints.py -q
```

No P5 work, product requalification claim, or G1–G4 guarantee change is included.

## Lane selection added to four entry points (issue #39)

`issue19_capture.py`, `issue21_lifecycle.py`, `issue22_lifecycle.py` and
`issue23_controls.py` each gained one `os.environ.setdefault(
"BROODLING_ZEROSHOT_LANE", "1")` before they import the test class they run.
Broodling regression now excludes the real-Zeroshot witnesses by default
([the lane notes](../tests/README.md#two-test-lanes-issue-39)); these campaigns
are that lane, so they select it rather than requiring the operator to.

This changes no campaign, fixture, assertion or retained record, and no
documented command. As with the #36 guards, the original bytes remain in Git at
the commits those reports name; a current checkout's executable is not offered
as the historical source identity.
