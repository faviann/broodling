# p5-judge-v2 — prospective evaluator correction

This separately versioned, evaluation-only evaluator implements the
[retained T1 calibration-gap analysis](../../analyses/2026-09-19-t1-calibration-gap/README.md)
from commit `24855ea8c59c852558553ce1bd6b0a72f992aeef` (PR #85).
It is distinct from the existing `p5-native-pr-v2` protocol. No cohort is created
or bound to this evaluator. Existing profiles retain the frozen v1 judge.

## Exact change and use

[judge.py](judge.py) copies the frozen v1 evaluator with these bounded changes:

- Append `valid-falsey-str-80` to T1: `FalseyString("80")`, overriding only
  `__bool__`, must return built-in `int` 80. Calibration checks construction and
  deepcopy preserve its subtype, false truth value and underlying content.
- Insert `text = str.__str__(text)` after the new T1 reference's string-membership
  guard. This uses the base string content without narrowing T1 to exact `str`.
- Keep the eight B1-negative/reference-positive controls and add the exact old
  T1 reference as a negative control. It must fail **only** the new case; the
  corrected reference must pass every case.
- Report `evaluator: p5-judge-v2` in JSON. Reuse the unchanged v1 B1 fixture for
  calibration; the versioned evaluator and its evidence are separate files.

All original cases, their order and expected results, exact-receipt Git loading,
README/file-scope checks and exit meanings are preserved. T1 now has 17 behavioral
checks; T2/T3/T4 retain 4/6/18. No product code, workflow, protocol or historical
evidence is edited. The references remain calibration material, never task input
or provider evidence.

**Independent full-criteria review remains required**, including after exit 0.
The complete procedure in the [frozen corpus](../../v1/corpus.md#independent-judging-fixed-before-outputs)
still applies: an independent reviewer assesses every original criterion on the
exact receipt revision, including unchanged non-target functions, imports,
helpers, tests and side effects. Passing examples do not establish full-domain
correctness. The helper is not a sandbox; inspect candidate code before executing
it in the credential-free, network-disabled judging sandbox.

Inside that sandbox, with the repository's `evaluation/p5` tree at `/p5`:

```bash
python3 -I -B /p5/judges/v2/judge.py --self-test
python3 -I -B /p5/judges/v2/judge.py T1 /repository /receipt.json
```

Exit 0 means automated PASS with independent review REQUIRED; 1 means automated
FAIL; 2 means material/identity could not be checked. This release's source and
evidence identities are sealed by [SHA256SUMS](SHA256SUMS); the commit containing
that manifest identifies the version. Any future dispatch would need a separate
prospective protocol/evaluator binding and authorization. This correction supplies
neither.

## Offline verification

[verify.py](verify.py) checks immutable input hashes, preservation of every old
case and the source-checking function, deepcopy semantics, reference identities,
both self-tests and both exact-receipt CLIs. It uses only Python's standard
library and Git. It imports no Broodling/native runtime and calls no provider.
The retained exact candidate source and diff were inspected before execution.

The new regression was first run before correcting the reference: the v2
`--self-test` returned 1 with `calibration_pass: false`; only T1's positive
reference expectation failed. After correction, [verification.json](verification.json)
retains the full reproducible Bubblewrap command, source hashes, timestamps,
exit status, empty stderr, environment/network metadata and verification output.
Inputs were read-only; credentials were cleared and all network namespaces
unshared. To reproduce, run the recorded command with the corresponding local
P5 tree and retained Git archive paths. Inside the sandbox the verifier is:

```bash
python3 -I -B /p5/judges/v2/verify.py /repository
```

| Check | Result |
| --- | --- |
| Frozen calibration | All 8 original control expectations reproduced |
| New calibration | All 9 control expectations met |
| Old T1 reference | 16/17; only `valid-falsey-str-80` fails |
| Corrected T1 reference | 17/17 |
| Exact retained v6 source, frozen CLI | 18/18 PASS, exit 0, independent review REQUIRED |
| Same source, v2 CLI diagnostic | 18/19 FAIL, exit 1; only the added case fails; independent review REQUIRED |

The new CLI observation is an outcome-informed regression witness, not a
retrospective score or fresh confirmatory provider evidence. No new candidate
revision or task receipt was manufactured. This verifies sensitivity to the
demonstrated defect, not exhaustive reference correctness or model reliability.
Product/dependency/test trees are unchanged; no product-suite/provider run was
needed for this evaluation-only change.

## Historical impact and remaining decision

The original automated **18/18 PASS** remains evidence of the frozen examples.
Independent full-criteria review already caught the violation and settled v6
as FA. The correction changes neither the historical procedure nor its verdict.

| Cohort | P | S | D | U | A | J_A | Unchanged result |
| --- | --- | --- | --- | --- | --- | --- | --- |
| v5 | 8 | 1 | 1 | 1 | 0 | 0 | NOT_DELIVERED / IF; separately BLOCKED |
| v6 | 8 | 1 | 1 | 1 | 1 | 1 | Native/Broodling SUCCEEDED; independent FAIL / FA |

Each retains seven NOT_STARTED slots; no observations or denominators are added
or pooled. V5 #68 remains BLOCKED / NOT RUN. V6 FA still disqualifies the cohort
under frozen v2 rule 4. #66 remains COMPLETE and scoped **P5 FAIL** remains
unchanged; broader release readiness is unassessed. Frozen protocols, v1 judge,
v5/v6 evidence and the prior analysis remain byte-for-byte unchanged.

The offline correction is complete. The remaining decision for
[#62](https://github.com/faviann/broodling/issues/62) is whether to stop with the
retained FAIL or explicitly scope and authorize further work. Any future cohort
would require its own prospective binding and authorization; it cannot resume or
rescore v5/v6. No provider work, new cohort, product behavior change, workflow
tuning, target restart, product replay or cleanup is performed or authorized here.
Native-cause attribution limits from the analysis remain unresolved and unchanged.
