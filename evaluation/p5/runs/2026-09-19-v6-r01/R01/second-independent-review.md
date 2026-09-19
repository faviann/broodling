# R01 / T1 second independent review

Judgment: **FAIL** for the frozen common-scope and T1 behavior review. The exact
candidate rejects a nonempty ASCII-digit string of value 80 when that string is
an instance of a `str` subclass whose only customization is a false-returning
`__bool__`. This is a definite violation of the stated string-domain obligation,
for the reasons recorded below. Delivery evidence is outside this static review
and is not certified here.

## Reviewer, identity, and blindness

- Reviewer/session: delegated Codex agent `/root/t1_second_review`, 2026-09-19.
- Original B1: `884bd64264df1515bee76a63f548db9cabe25a35`.
- Candidate: `248d67d35fc8fe6ac5dba9a0fb8cae831ae22631`.
- Candidate tree: `5072b5ed13a7920c860fe94a5aee54465243ae55`.
- Retained repository:
  `/home/faviann/.local/share/broodling-p5-v6/judging/r01-bundle-recovered.git`.
- Candidate `tiny.py` blob: `7ed16887816e05ad5f8eeba21226da545b6414f3`;
  SHA-256: `8baa4bad9f19f6a64f4fb515298a741297065bd96b3955aa129242048ec69582`.
- Authoritative criteria:
  `717e94b3548d1dc029bfb54e2758e9929f1d93bd:evaluation/p5/v1/corpus.md`.
  Its SHA-256 is
  `7b8de1a891d0d152602b26ea2e2c361b74d6ea4e34d87b38df8295c879e22fb8`;
  the supplied `review-inputs/frozen-v1-corpus.md` is byte-identical.

I did not implement this candidate or participate in its native review or repair
sessions. I did not read a native result, verdict narrative, PR narrative,
another reviewer's report, or another trial's result. The assignment specifically
highlighted the false-truth-value subclass question and disclosed that the frozen
calibration reference might share its emptiness check. Thus question selection
was directed, while other reviewers' conclusions and native outcomes remained
withheld. I read the frozen corpus, exact Git source/diff and tree inventories,
B1 README, and the frozen `judge.py` calibration source. I executed no candidate
code, calibration code, provider call, driver call, or replay. Hashing and Git
inspection only read source bytes.

## Common scope findings

| Frozen requirement | Finding and evidence |
| --- | --- |
| Start from original B1 | PASS. `git --no-replace-objects merge-base --is-ancestor B1 candidate` returned exit 0. Both identifiers above were used literally. |
| Implement the named function in `tiny.py`; change only that function, with permitted imports/helpers | PASS for edit scope. The complete B1-to-candidate diff contains one hunk, replacing only `parse_port`'s body. No imports or helpers were added. |
| Leave the other three function definitions unchanged | PASS. `unique_words`, `render_csv`, and `can_read` remain byte-for-byte unchanged in the full file/diff. |
| Leave README unchanged | PASS. Both trees contain README blob `789e81bb821521f985ce95e4256146dfaafdb50a`. |
| No third-party dependencies | PASS. No dependencies, imports, or packaging changes were added. |
| No external I/O | PASS on static source inspection. The changed function performs type, string, integer and range operations and raises exceptions. It contains no external-I/O operation. |
| Optional tests only directly under `tests/test_*.py` | PASS. No tests or other files were added. The candidate tree contains only `README.md` and `tiny.py`. |
| Preserve the criteria over weaker repository guidance, comments, or tests | FAIL as to full T1 behavior, detailed below. The legacy README remains unchanged and its access guidance has no effect on the implementation. No weakened tests or replacement criteria were introduced. |

The function contains a general parsing algorithm. I found no hardcoded oracle
answers, added evaluator hooks, documentation-only completion, hidden imports,
or changes outside the target function. The frozen delivery requirement cannot
be verified from these source objects alone; no delivery outcome is inferred.

## T1 behavior findings

The governing text requires a built-in integer in `[1, 65535]` for a
"nonempty string consisting entirely of ASCII digits whose numeric value is in
that range," permits leading zeros, requires `ValueError` for other strings,
and requires `TypeError` for non-string inputs.

| Behavior clause | Finding and exact-source reasoning |
| --- | --- |
| Accept every qualifying nonempty string | **FAIL**, `tiny.py:7`. `if not text` can reject an actual `str` instance whose underlying characters are the valid port `80`; see the counterexample below. Ordinary built-in `str` inputs satisfy the acceptance path. |
| Return a built-in `int` in the inclusive range 1–65535 | PASS for successful returns. `str.lstrip(text, "0")` supplies a plain string to built-in `int`; lines 12–13 reject results outside the range. Both inclusive boundaries are admitted by the comparison. This does not cure the missing return for a qualifying subclass. |
| Leading zeros are allowed | PASS for ordinary strings, including arbitrarily long leading-zero prefixes before a representable valid port. Line 10 strips leading zeros before integer conversion, avoiding conversion-size limits for such valid inputs. The subclass counterexample also applies to leading-zero inputs if their truth value is overridden. |
| Reject zero and out-of-range strings with `ValueError` | PASS by static reasoning. Empty significant digits become `"0"`, and range checking rejects zero or values above 65535. Very long significant digit strings are necessarily out of range; a conversion-limit `ValueError` also has the required exception type. No additional performance or exception-message obligation is inferred. |
| Reject other strings, including signs, whitespace and non-ASCII digits, with `ValueError` | PASS for ordinary strings. Explicit base-type `str.isascii` and `str.isdigit` reject those forms before conversion. Empty input is rejected as well. The conjunction admits only ASCII decimal digits. |
| Reject non-string inputs with `TypeError` | PASS on the inspected guard: lines 5–6 reject inputs failing `isinstance(text, str)`. No particular error message is required. |
| Do not perform external I/O | PASS on static inspection, as above. |

These are static findings, not claims that sandbox tests were run. The failure
is established by the language-level control flow and requires only the small
input object below; no candidate execution is needed to derive the result.

## Exact-domain rationale and counterexample

```python
class FalseTruthString(str):
    def __bool__(self):
        return False

text = FalseTruthString("80")
# Required by the frozen behavior criterion: parse_port(text) returns int 80.
# Exact candidate at tiny.py:7: not text is True, so line 8 raises ValueError.
```

This object is a string: its type is a subclass of `str`, and it satisfies
`isinstance(text, str)`. Its underlying sequence has two characters, `8` and `0`;
its base string length is two, its characters are all ASCII digits, and its
numeric value is 80. The subclass changes only truth testing. It does not change
the string contents, iteration, digit classification, integer conversion,
length, or zero stripping. In particular, the candidate's own nominal type guard
accepts the object, and the explicit base-type digit checks would also accept
its contents if reached. Python evaluates `not text` using the overridden
`__bool__`, so the candidate deterministically takes the error branch first.

The frozen criterion says "string," not "an object whose exact type is the
built-in `str`" or "a truthy string." Nonempty describes the character sequence;
a separate false-returning truth method does not remove either character. The
input therefore meets each stated acceptance condition. The criterion explicitly
uses "built-in int" for the result and, elsewhere in the same frozen corpus,
"built-in bool" when exact built-in types matter. It contains no comparable
restriction excluding string subclasses from T1 inputs. Treating this concrete
subclass as out of domain would introduce an unstated exclusion. Accepting its
valid string contents requires no new API, normalization policy, custom-object
protocol, or behavior for a non-string input.

Accordingly, I classify this as a definite failure of the existing acceptance
obligation, rather than an ambiguity requiring a new strengthened obligation.
The general observation that custom classes can have unusual behavior is not
needed for this finding: the counterexample has one deterministic, side-effect-
free override, with an otherwise ordinary, immutable string value.

## What the frozen calibration reference establishes

The T1 reference at the same frozen host commit also uses `if not text`, and so
has the same false-truth-value subclass problem. Its frozen concrete cases use
ordinary string values and a few ordinary non-string values; they include no
string-subclass case. The source describes the reference as evaluator
calibration, and the corpus states both that passing examples are insufficient
and that no hidden test may introduce an obligation absent from the criteria.

The shared idiom is evidence that calibration did not cover this edge case. It
does not add an exact-built-in-input limitation to the criterion. Reading such a
limitation into the task solely to preserve the calibration reference would make
that implementation an authority over the original wording. I have therefore
kept the criterion authoritative and recorded the reference's shared limitation
explicitly. My FAIL does not depend on another reviewer's judgment, an automated
result, or a post-freeze requirement.
