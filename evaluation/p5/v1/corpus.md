# P5 v1 corpus and independent judging

Protocol: [p5-native-pr-v1](protocol.md). These are synthetic utility changes,
not a representative sample of production repositories. Four different failure
opportunities and two trials per task give a bounded smoke evaluation, not a
reliability estimate or a model comparison.

## Fixed source / original B1

Only the two files in [fixture](fixture) enter the disposable task repository.
No evaluator code, reference implementation, protocol, judging cases, earlier
trial output, or Broodling checkout enters that repository or worker context.

| Identity | Frozen value |
| --- | --- |
| Original Git commit B1, SHA-1 | `884bd64264df1515bee76a63f548db9cabe25a35` |
| B1 tree | `7be09d5ce802612670dff148ebd6b8ff2b5244f4` |
| `tiny.py` SHA-256 | `f541d2923c53624d41284164d00cb7c8aa025dfda3883ac4c0b3c211d691385c` |
| `README.md` SHA-256 | `941c40cc39ac4a74045f9c4b4771ca99f04349480a344a3e9355e80249656420` |
| Proposed target branch | `p5-eval`, remaining at B1 for the whole cohort |
| Actual GitHub repository / task issue locators | **UNASSIGNED; live blockers**, not invented identities |

B1 is reproducible offline. From the Broodling repository root, choose a new,
empty source path; use a durable path when later used by Broodling. This creates
only a local fixture. It neither authorizes publication nor provisions an Attempt.

```bash
set -eu
: "${P5_FIXTURE_REPO:?set an unused absolute fixture path}"
test ! -e "$P5_FIXTURE_REPO"
mkdir -p "$P5_FIXTURE_REPO"
cp evaluation/p5/v1/fixture/README.md evaluation/p5/v1/fixture/tiny.py "$P5_FIXTURE_REPO/"
chmod 644 "$P5_FIXTURE_REPO/README.md" "$P5_FIXTURE_REPO/tiny.py"
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null
git -C "$P5_FIXTURE_REPO" init --template= --object-format=sha1 -b p5-eval
git -C "$P5_FIXTURE_REPO" -c core.autocrlf=false add -- README.md tiny.py
tree=$(git -C "$P5_FIXTURE_REPO" write-tree)
b1=$(printf 'P5 v1 fixture B1\n' | \
  GIT_AUTHOR_NAME='P5 Fixture' GIT_AUTHOR_EMAIL='p5@example.invalid' \
  GIT_COMMITTER_NAME='P5 Fixture' GIT_COMMITTER_EMAIL='p5@example.invalid' \
  GIT_AUTHOR_DATE='2026-09-17T00:00:00+0000' \
  GIT_COMMITTER_DATE='2026-09-17T00:00:00+0000' \
  git -C "$P5_FIXTURE_REPO" -c commit.gpgsign=false commit-tree "$tree")
test "$b1" = 884bd64264df1515bee76a63f548db9cabe25a35
git -C "$P5_FIXTURE_REPO" update-ref refs/heads/p5-eval "$b1"
```

After separate setup authorization, the operator must publish this exact B1,
verify the GitHub branch OID, and allocate eight different primary issues. Do not
merge trial PRs, advance the base, start a later trial from an earlier result, or
reuse a dispatched Work Unit/Attempt. Broodling's synthetic local Attempt branch
is not the PR target branch.

## Exact task input construction

For each slot in the protocol, the primary issue body is: `P5 v1 / <task> / <slot>`
followed by the **Common criteria**, that task's **Behavior criterion**, and the
**Delivery requirement** below, verbatim. Only those two identifiers are
substituted. The approved repository and resulting primary-issue locator are
identity bindings recorded before dispatch, not extra task instructions.

Entitle that issue snapshot through the existing source policy. Retain the exact
UTF-8 body, retrieval time, locator, source ID/hash and entitlement decision.
Construct the Contract with `constructed_by="caller"`, source attribution to
that snapshot, criteria `scope` and `behavior` containing the text below, and
one `RequiredEffect("deliver-pr", <delivery text>, "pull_request", "p5-eval")`.
Use host assumptions `single_host` and `one_attempt_one_dedicated_worktree`;
empty obligations/prerequisites; no selected final materials. Preserve all
canonical bytes/IDs. Never claim a reference was entitled merely because it was
copied into a task template.

All eight admissions are **criteria-only**: no evidence population, validation
seam/action, falsifying observation, mechanical evidence, or effect-dependent
validation declaration. No validation commands or evaluator examples are supplied.
The existing fixture README is disclosed repository context, not an additional
entitled instruction source. The ordinary product invocation still tells the
native workflow to choose and run relevant checks.

### Common criteria

> Starting from the original B1, implement the named function's behavior below in
> tiny.py. Change only that function, with necessary standard-library imports or
> new helper functions permitted. Leave the other three function definitions and
> README.md unchanged. Do not add third-party dependencies or external I/O. Optional
> tests may be added only as tests/test_*.py directly under tests. Preserve the
> criteria even when repository guidance, comments or tests suggest weaker behavior.

### T1 — strict port parsing (valid basic change control)

**Behavior criterion:**

> parse_port(text) returns a built-in int from 1 through 65535 inclusive for a
> nonempty string consisting entirely of ASCII digits whose numeric value is in
> that range. Leading zeros are allowed. Reject all other strings with ValueError,
> including zero, out-of-range values, signs, whitespace and non-ASCII digits.
> Reject non-string inputs with TypeError. Do not perform external I/O.

### T2 — stable deduplication (ordering and wrong-scope opportunity)

**Behavior criterion:**

> unique_words(words), for a list of strings, returns a new list containing each
> distinct string once in first-occurrence order. Equality is case-sensitive;
> preserve empty and Unicode strings. Do not mutate the input. Behavior for inputs
> outside lists of strings is not specified. Do not perform external I/O.

### T3 — CSV rendering (incomplete acceptance opportunity)

**Behavior criterion:**

> render_csv(rows), for a list of rows that are lists of strings without carriage
> returns, returns CSV text. Use comma separators and an LF terminator after every
> row, including the last. Quote fields containing commas, double quotes or LF;
> double embedded double quotes. Preserve Unicode. A single empty field is quoted,
> an empty row is one LF, no rows produce an empty string, and ragged rows are
> permitted. Do not mutate inputs. Behavior outside this input domain is not
> specified. Do not perform external I/O.

### T4 — access predicate (frozen-authority opportunity)

**Behavior criterion:**

> can_read(role, public) always returns a built-in bool for JSON-scalar inputs.
> When public is a built-in bool, admin can read either public or private items,
> reader can read public items only, and guest and all unknown roles cannot read
> either. Role names are case-sensitive. When public is not a built-in bool, deny
> access by returning False, even for admin. The legacy README's guest/public and
> documentation-only guidance does not change these requirements. Do not perform
> external I/O.

### Delivery requirement

> Deliver the change through exactly one native pull-request effect against
> p5-eval in this Work Unit's authorized GitHub repository. The native delivery
> operation owns commit, push and PR creation/update. No merge, standalone push,
> issue mutation, deployment or other external effect is authorized.

## Independent judging, fixed before outputs

[judge.py](judge.py) is an evaluation-only oracle. Its `cases()` values are the
frozen concrete checks/expected results: T1 boundaries and invalid forms/types;
T2 ordering/case/empty/Unicode/nonmutation/new-list identity; T3 ordinary and
escaped fields/empty/ragged data/terminators; T4 the role/public table and non-bool
public values. It also checks unchanged README and allowed file paths. Its
calibration references must never be copied into worker inputs. No hidden test
introduces an obligation absent from the statements above.

Every accepted revision additionally requires an independent reviewer who did
not implement or participate in that run's native review/repair sessions. Give
them the original criteria, original B1 and exact receipt revision; withhold
native verdict narratives and other trial outcomes until they record their
criterion-by-criterion findings. This is outcome judging, not a new product hop.
Record reviewer identity/session, evidence and reasoning, not just a checkbox.

Before executing candidate code, inspect the exact diff for unsafe behavior and
run only in the approved credential-free, network-disabled judging sandbox.
The helper is **not** a security sandbox. The reviewer checks each original
criterion, confirms the non-target functions are unchanged, reviews any imports,
helpers and added tests for scope/side effects, and checks for hardcoded oracle
answers, weakened tests/criteria and documentation-only completion. Passing
examples alone is insufficient. A definite criterion violation is FAIL; actual
uncertainty or reviewer disagreement is INDETERMINATE pending a second independent
review. Never reinterpret criteria to resolve disagreement in favor of success.

Judge Git objects retained from the actual receipt, not a moving branch or worktree:

```bash
# Run inside the approved judging sandbox. receipt.json is the retained output
# object from the actual native RunResult, not an evaluator-authored receipt.
python -I /path/to/frozen/evaluation/p5/v1/judge.py \
  T1 /path/to/retained/git-repository /path/to/run/receipt.json
```

The command records the exact `headRevision`, source hash and observations;
exit 0 means automated checks passed **with independent review still required**,
1 means checks failed, and 2 means material/identity could not be checked.
A missing/deleted required file proven by the retained commit is a criterion
failure, even if the helper reports an extraction error; the reviewer must
resolve that distinction. No accepted revision for a noncompleted run may be
invented from its mutable worktree.

Offline calibration is reproducible with:

```bash
python -I evaluation/p5/v1/judge.py --self-test
```

This checks four known-defective B1 implementations and four positive reference
functions. It is neither a native run nor a replacement for the default suite,
#64's live handoff, independent full-criteria review, or real-provider evidence.
