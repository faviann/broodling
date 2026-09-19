# T1 acceptance/calibration gap — offline follow-up to #66

**Analysis COMPLETE, 19 September 2026. Prospective evaluator correction recommended; not activated or frozen here. V5 IF and v6 FA are unchanged. P5's scoped verdict remains FAIL; no broader release decision is made.**

This is the separately scoped follow-up requested by completed [#66](https://github.com/faviann/broodling/issues/66), for [#62](https://github.com/faviann/broodling/issues/62). It diagnoses the retained T1 failure, distinguishes native attribution from evaluator calibration, and specifies the smallest prospective correction. It is an unblinded post-outcome analysis, not another independent discovery or cohort.

## Authority and exact material

| Input | Immutable identity |
| --- | --- |
| Original criteria, judge and calibration references | [v1 corpus][corpus] and [judge][judge], freeze `717e94b3548d1dc029bfb54e2758e9929f1d93bd` |
| Claim-level gate | [v2 protocol][v2], `1d581717509de182410c3ab2f54d3f65768d8d7f`; rule 4 permits zero FA |
| Operational policy / v6 profile freeze | `de1da18479fdf846d04994190132357fcb0a87ef` / `4a2b0b8a0340b747e805f51da218a77ad81278f9` |
| V5 settlement | `445c6d773ab772cb00f98a274d4bb8fce3e21b84`, NOT_DELIVERED / IF |
| V6 settlement and retained source/reviews | [R01 package][r01], `fcc8439f7827068185bb533a2b48b3ae5bb9c90d` |
| Completed skeptical review | [#66 record][review66], `c5632ddd8896bfe8ffd2defafc7777d68dafbc5a` (PR #84; not modified by this analysis) |
| Original B1 / exact accepted revision | `884bd64264df1515bee76a63f548db9cabe25a35` / `248d67d35fc8fe6ac5dba9a0fb8cae831ae22631` |
| Accepted `tiny.py` SHA-256 | `8baa4bad9f19f6a64f4fb515298a741297065bd96b3955aa129242048ec69582` |
| Frozen judge SHA-256 | `b6dbf57a606e95377eadb89dea72ad2979b84147705be384c361f883483ddb95` |
| Frozen corpus SHA-256 | `7b8de1a891d0d152602b26ea2e2c361b74d6ea4e34d87b38df8295c879e22fb8` |

The original criterion says **string**, not exact built-in `str` or truthy string. It explicitly requires a built-in `int` result, allows leading zeros, requires ValueError for invalid strings and TypeError for non-strings. The criterion remains authoritative over the reference implementation. The frozen invocation retained that wording and supplied no hidden judge/reference or validation examples.

## 1. Exactly why the automated judge and reference missed the case

```python
class FalseyString(str):
    def __bool__(self):
        return False

parse_port(FalseyString("80"))  # Required: built-in int 80.
```

The object remains a nonempty string containing the two ASCII digits `8` and `0`. The sole override changes truth testing, not content, length, digit classification or numeric value. Python's [truth-testing semantics](https://docs.python.org/3.13/reference/datamodel.html#object.__bool__) explain the mechanism; the frozen criterion supplies the obligation.

The [exact accepted source][candidate] admits it through `isinstance(text, str)`, then evaluates `if not text or not str.isascii(text) or not str.isdigit(text)`. `not text` is True and short-circuits the content checks; the function raises ValueError before normalization/conversion. The frozen T1 reference likewise uses `if not text or any(...)` and rejects the same valid input. Neither behavior makes the input out of domain.

There are three distinct facts about the evaluator:

1. **Coverage omission.** `cases("T1")` contains 16 behavioral checks: three valid built-in strings, ten invalid built-in strings, and three non-string values. There is no string subclass or overridden-truth-value case. The other two observations in the recorded **18/18** are README equality and allowed file paths; they do not enlarge behavioral coverage.
2. **Calibration is not an independent semantic oracle.** `self_test()` runs the same `check_source()`/`cases()` against each B1 implementation (expected to fail overall) and each reference (expected to pass). T1's B1 fails ordinary rejection examples, while the flawed reference passes all selected examples. No targeted negative control distinguishes a correct content-based implementation from the truthiness shortcut. The live judge does **not** compare candidate answers to the reference: it compares them to the expected values in `cases()`. The reference is used for calibration, not runtime judging of R01.
3. **No reporting malfunction or full-protocol false pass.** The recorded automated PASS correctly describes those checks. Exit 0 explicitly leaves independent review REQUIRED. The [first independent review][first] found the defect; the [second][second] corroborated it with a directed subclass/calibration question. The complete frozen evaluation procedure therefore did catch the violation and recorded FA. The automated component and its positive calibration reference have a shared blind spot; the whole protocol did not silently classify R01 CO.

The first review was independent of native implementation/review and blinded to automated/native outcomes. The second was independent assessment, not independent discovery. Neither is a statistically independent model-diversity observation. Their findings and the retained sandbox probes are unchanged.

## 2. What the retained native evidence does and does not establish

The [frozen invocation][invocation] records standard `software-change`, Codex, gateway, `gpt-5.6-sol`, medium effort, small size and execution-scoped sessions. This identifies the requested profile, not a controlled comparison between models or independent verification of the gateway's underlying model identity.

A new **read-only** extraction of the retained native SQLite event store strengthens the accessible evidence beyond the terminal receipt. [observations.json](observations.json) retains a stage projection, its source path, run ID, database hashes before/after, and a digest of all 64 ordered event rows. SQLite was opened with `mode=ro&immutable=1`; the database hash was unchanged. No target was started and no native/product API was invoked.

| Native stage | Start / completion sequence | Retained outcome |
| --- | --- | --- |
| worker, execution 1 | 2 / 27 | attempt 1; status `verified` |
| acceptance, execution 2 | 28 / 57 | attempt 1; verifier verdict `accepted` |
| code, execution 3 | 29 / 45 | attempt 1; verifier verdict `accepted` |
| deliver, execution 4 | 58 / 63 | attempt 1; `opened`, matching the exact accepted revision |

Both reviewers made full-correctness claims contradicted by the retained counterexample. Code review reported boundary, invalid-input, exhaustive valid-range, long-leading-zero and compilation checks. Acceptance reported custom checks including every value 1–65535, plus unittest discovery reporting **Ran 0 tests / OK**. These are retained native diagnostics, not independently verified test transcripts in this analysis. Zero discovered tests alone does not show no validation: the same record reports custom checks, and optional committed tests were not required.

**Supported attribution:** this native workflow instance produced/delivered a wrong T1 implementation and both native verifier stages accepted it. This is a native outcome/verification escape under the recorded profile, not merely a mistake made by the later external judge. Broodling's receipt-backed SUCCEEDED disposition remains consistent with its boundary; it is not a semantic guarantee. Correcting the offline evaluator would expose the miss earlier in evaluation, not change that native result.

**Attribution limit:** stage diagnostics do not establish the exact complete test inputs, prompts, internal reasoning, or why each reviewer missed the case. A preliminary safe-log inventory showed logged commands, worker update-event pairs, and a `PortText(str)` test prefix. Thus it would also be wrong to claim that the worker never considered subclasses or that native logs do not exist. Full raw-command reading and a proposed AST-only inventory were blocked by the tool safety layer in this session; the blocked contents were not obtained. The exported projection deliberately excludes raw commands. No claim is made that the decisive case was absent from every native check, noticed and ignored, or lost during repair.

A numeric-value sweep alone cannot establish coverage of every allowed string representation/subclass. That is a coverage argument, not proof of which tests the native sessions actually ran. Likewise, one run cannot identify a stable model weakness, inadequate effort, correlated reviewers, a workflow-routing bug, or the effect of an alternative prompt/model. Shared use of `if not text` is not evidence of reference leakage: the reference was excluded from the disclosed worker inputs, and no contamination evidence was found. There is no demonstrated causal path from this hidden, post-delivery evaluator to the native acceptance decision.

## 3. Smallest prospective evaluator correction

**Yes: correct the evaluation-only judge and calibration, without narrowing T1.** Proposed ID: **`p5-judge-v2`**, separate from the already-existing `p5-native-pr-v2` protocol ID. This analysis does not create, activate or freeze that evaluator release.

The minimal change is:

- Add one named T1 case, `valid-falsey-str-80`, constructed as `FalseyString("80")`, expecting exact built-in `int` 80. Keep every existing case, scope check, outcome class and full-criteria review requirement unchanged. Assert that case construction/deepcopy retains the subclass and false truth value; flattening it into a JSON/plain string would erase the regression.
- In the **new calibration reference only**, preserve the string-membership guard and insert `text = str.__str__(text)` immediately after it. Subsequent checks then operate on the underlying built-in string value rather than overridable truth/method behavior. Ordinary `str(text)` is not the same deliberate base-method call; replacing `isinstance` with an exact-type exclusion would instead narrow the criterion. Do not patch the accepted revision or product.
- Add a targeted calibration negative control: the immutable old T1 reference must fail the new case, while the corrected reference passes. Retain the existing B1-negative/reference-positive checks for T1–T4. Use the exact v6 accepted source as an additional offline regression witness, not a new provider observation. No general mutation-testing framework is needed.

Before any future use, retain the new evaluator under a new path/version with its source hashes and this impact statement. Existing v5/v6 profiles bind the old frozen judge and cannot silently adopt it. Any later authorized cohort would need a **separate prospective binding** to the new evaluator before dispatch; no successor protocol/cohort is created or authorized here.

## 4. New offline diagnostic and historical impact

[diagnostic.py](diagnostic.py) was supplied on stdin to Python in the existing Bubblewrap-style credential-free, network-unshared sandbox, mounting only system tools and the retained review inputs read-only. The exact source/diff had been inspected before execution. The script verifies the judge, B1 and accepted-source hashes, loads the frozen code unchanged, and creates the proposed case/reference **in a separate in-memory diagnostic namespace only**. It does not write files, change frozen rules, call a provider, or replay a Broodling/native run.

[observations.json](observations.json) retains the command, script hash, exit status, empty stderr, environment keys, network namespace/routes and output. Python was 3.13.5; only HOME/LANG/PATH/PWD were present and the isolated namespace had no IPv4 routes. Input hashes matched before and after. All eight original B1/reference calibration-control expectations reproduced.

| Source | Frozen 16 behavioral checks | One-case prospective simulation |
| --- | --- | --- |
| Exact accepted v6 revision | 16/16 | 16/17; only `valid-falsey-str-80` fails with ValueError |
| Frozen T1 reference | 16/16 | 16/17; the same case fails with ValueError |
| Proposed reference, one normalization line | 16/16 | 17/17 |

The script also verifies that deepcopy preserves the regression subtype/false truth value. Its exit 0 means the diagnostic's expected positive **and negative** observations were confirmed, not that the historical candidate passed. The two historical scope checks were not rerun here: the retained full CLI result remains **18/18 PASS, independent review REQUIRED**. The hypothetical new CLI would contain 17 behavioral plus two scope observations; no new full-CLI score is recorded for P5.

This is an outcome-informed regression check, not fresh confirmatory evidence. It establishes sensitivity to this demonstrated defect, not exhaustive correctness of the reference or broad evaluator/model reliability. The complete historical judging procedure already recorded the failure; no retrospective rescore, denominator adjustment or classification substitution is warranted.

| Cohort | P | S | D | U | A | J_A | Unchanged result |
| --- | --- | --- | --- | --- | --- | --- | --- |
| v5 | 8 | 1 | 1 | 1 | 0 | 0 | NOT_DELIVERED / IF; separately BLOCKED |
| v6 | 8 | 1 | 1 | 1 | 1 | 1 | Native/Broodling SUCCEEDED; independent FAIL / FA |

Each retains seven NOT_STARTED slots. No pooling, resets or additional observations occur. V6 FA still defeats frozen v2 rule 4; #66 remains COMPLETE, the scoped P5 verdict remains FAIL, and #62 remains open without release authorization. An evaluator defect does not convert this determinate semantic FA into IF or erase v5's separate installation failure. Successful delivery/disposition/replay evidence is unaffected.

## Conclusion and stop

The smallest justified next step is **separately scoped offline implementation and verification of the new evaluator version described above**, retaining the original evaluator and all historical results. This is not authority to resume v6, start a cohort, change workflow/model settings, fix the product, add a runtime assessor, or clean up quarantined resources. More native trace access could refine attribution, but is not required to establish this evaluator correction and is not a prerequisite for declaring the present analysis complete.

Only this new analysis record and #62's follow-up navigation/conclusion are changed. Frozen protocols, historical evidence, PR #84's review, accepted/partial task PRs, product behavior and quarantined native resources are not modified. No provider work, cohort, workflow tuning, live admission/dispatch, target restart, product replay or cleanup was performed.

[corpus]: https://github.com/faviann/broodling/blob/717e94b3548d1dc029bfb54e2758e9929f1d93bd/evaluation/p5/v1/corpus.md
[judge]: https://github.com/faviann/broodling/blob/717e94b3548d1dc029bfb54e2758e9929f1d93bd/evaluation/p5/v1/judge.py
[v2]: https://github.com/faviann/broodling/blob/1d581717509de182410c3ab2f54d3f65768d8d7f/evaluation/p5/v2/protocol.md
[r01]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/README.md
[review66]: https://github.com/faviann/broodling/blob/c5632ddd8896bfe8ffd2defafc7777d68dafbc5a/evaluation/p5/reviews/2026-09-19-issue-66/README.md
[candidate]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/review-inputs/receipt-tiny.py
[first]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/independent-review.md
[second]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/second-independent-review.md
[invocation]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/invocation.json
