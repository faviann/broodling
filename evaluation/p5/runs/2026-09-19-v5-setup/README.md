# P5 v5: #81 implementation and setup completion

**SETUP COMPLETE; R01-R08 NOT_STARTED. No live-boundary or P5-readiness verdict.**

Corrected protocol freeze: `52eb3569b3671baa37426792a67b50058e2d223f`.
Selected endpoint: `https://cliproxy.local.faviann.com/v1`.
Compatible product/dependency/test baseline:
`9c799de1b5cfff16082e65531933e7a249544282`.

This package records #81's completed implementation/setup obligations. Its
closure does not authorize #79's live execution. All earlier P5 protocols and
evidence, including untracked v3/v4 setup records and scripts, remain unchanged.
The v5 protocol itself remains byte-identical to the corrected freeze.

## Baseline and verification

[baseline-verification.json](baseline-verification.json) records the exact
product/test/config Git object identities and frozen protocol SHA-256.
Production and dependency files are unchanged from the corrected freeze.
The baseline adds only v5 setup tooling/documentation and its offline tests.

**Full suite: 351 tests and 267 subtests passed, zero failures/skips, exit 0,
74.51 seconds.** [Retained output](default-suite.txt) comes from
`.venv/bin/python -m pytest tests -q` against the exact committed test tree.
This includes native runtime expansion, controlled provider integration, gateway
policy/replay/crash/reconnection coverage and setup refusal/immutability tests.

[dependency-verification.json](dependency-verification.json) records Python
3.13.5, SQLite 3.46.1, SDK 10.3.0.post1, native Zeroshot 10.3.0 and binary hash.
The exact cached wheel matches the pinned SHA-256; all 15 installed package
files, including the native executable, match byte-for-byte. No upgrade,
installation or fresh download was needed. SDK/native identity is host evidence,
not proof of a currently running DirectTarget.

Before execution, compare the intended checkout with this baseline:

```bash
git diff --exit-code 9c799de1b5cfff16082e65531933e7a249544282 -- broodling tests pyproject.toml conftest.py
git status --short -- broodling tests pyproject.toml conftest.py
sha256sum .venv/lib/python3.13/site-packages/zeroshot/_bin/zeroshot
.venv/bin/python -c 'import importlib.metadata; print(importlib.metadata.version("the-open-engine-zeroshot"))'
```

Any relevant changed or untracked product/test/config file needs compatible
validation. Check the installed package against the recorded pinned wheel again
if that installation changes; do not treat an unchanged version string alone as
a byte identity.

## Prepared inputs, not admission

The [v5 helper](../../v5/setup_r01.py) was run twice with the command in the
[setup guide](../../v5/setup.md). Repeat preparation left every setup evidence
byte unchanged. It used only local Git/SQLite operations and GitHub readbacks;
no GitHub resource was created or changed.

- Existing private repository: `faviann/broodling-p5-v3-20260917`.
- Existing primary issue: [#1](https://github.com/faviann/broodling-p5-v3-20260917/issues/1),
  open, exact frozen `P5 v1 / T1 / R01` text.
- Target branch: `p5-eval`, still at frozen
  B1 `884bd64264df1515bee76a63f548db9cabe25a35`; no PRs exist.
- Fresh v5 source/store/workspace root:
  `/home/faviann/.local/share/broodling-p5-v5/r01-inputs`.
- The [input records](R01/input-records.json) retain real Work Unit, entitled source
  and immutable criteria-only Contract identities. The fixture has only the two
  frozen files; no judge/reference material enters it.
- [Store readback](store-readback.json): one Work Unit/source/Contract; zero
  admission decisions, Attempts, workspace assignments, submissions, result
  records or dispositions.
- [Slots](slots.json): `P=8; S=D=U=A=J_A=0`; all eight are `NOT_STARTED`.
  No trial judgment or outcome class is assigned.

Keep this package immutable. #79 should retain its execution records and mutable
trial accounting in a new v5 live-evidence directory, referencing/copying these
verified input records and zero-start allocation. The prepared durable store,
source and Contract remain the inputs; do not replace them with a new Attempt
after admission or mutate this preserved setup record.

## Exactly what remains before #79

[operational-readback.json](operational-readback.json) records current facts and
their limits. The operator's `/models` 404 and `/v1/models` 200 report is accepted
as endpoint-path evidence; it was not repeated and is not a provider-task result.

1. **Separate authorization and active supervision** for the one R01 trial.
   The operator must have external stop access and sufficient retained/quarantine
   capacity. No extra P5 dollar/time ceiling or signature procedure is required.
2. **A running compatible DirectTarget.** Intended origin:
   `http://127.0.0.1:18767`. The old `broodling-p5-v3-r01-target` is stopped.
   Reuse the recorded pinned image with fresh v5 target state/home, or supply an
   equivalent compatible isolated target, preserving historical state. Verify
   native discovery, empty run inventory, supported runtime and target DNS/TLS
   reachability to the gateway without launching a provider task.
3. **Current dispatch inputs:** exact `GATEWAY_BASE_URL`, nonempty
   `GATEWAY_API_KEY`, and an appropriately authorized GitHub credential as
   `GH_TOKEN`. All are absent from this session's environment. Authenticated
   `gh` read access works and its repository identity reports push/admin
   permission; token availability/scope for delivery still needs checking.
   Supply values ephemerally, never in retained records or transcripts.
4. **Final pre-admission recheck:** the compatible baseline, actual target,
   private repo/unchanged issue/B1/no PRs, zero-admission store, preserved setup
   hashes and all-NOT_STARTED allocation. Prepare a new live-evidence directory.
   Then #79 owns first-admission accounting and normal API orchestration.
   The old v4 driver must not be used.

No R01 admission, dispatch, gateway call, target start/stop, native PR, live
disposition or trial judgment occurred in completing #81. #68 remains gated on
compatible successful R01 evidence; a green suite and issue closure do not open it.
