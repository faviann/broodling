# V1-P2 qualification and gate records

| File | What it is |
|---|---|
| [`issue-15-g2-v1.md`](issue-15-g2-v1.md) | The G2-V1 gate review and its **BLOCKED** verdict. |
| [`issue-13-concurrency.md`](issue-13-concurrency.md) | Issue #13's answer to that review's §5.3 concurrency blocker. |
| `issue13_concurrency.py`, `issue13_racer.py` | The retained reproducer behind it. |
| `evidence/` | Machine records from the runs the report cites. |

Unlike the V1-P1 fixtures, nothing here needs the Zeroshot SDK or sidecar. The
reproducer drives only the Broodling product package and the local `git` binary.

## Reproducing the #13 concurrency evidence

The qualified V1 profile refuses a volatile workspace root, so these runs cannot
use `/tmp`. They default to `~/.cache/broodling-evidence`; pass
`--workspace-root` for somewhere else on a durable filesystem.

**What Git publishes, and when.** The mechanism the report turns on. No Broodling
code is involved: it times `git worktree add` against a B1 large enough for the
checkout to be observable.

```bash
.venv/bin/python qualification/v1-p2/issue13_concurrency.py \
  --stages --trials 5 --files 3000 --file-lines 300 \
  --output qualification/v1-p2/evidence/issue-13-stage-order.json
```

**The race.** Four processes issue the semantically identical admission request
behind one gate. Every round records what each caller was handed *and* what the
durable state became, then classifies every divergence. Exit status is 0 only if
every round converged.

```bash
.venv/bin/python qualification/v1-p2/issue13_concurrency.py \
  --race --rounds 12 --racers 4 --files 3000 --file-lines 300 \
  --output qualification/v1-p2/evidence/issue-13-race-remediated.json
```

**Reproducing the failure.** Run the same command against the reviewed tree. The
harness postdates it, so copy the two scripts in:

```bash
git worktree add --detach /path/to/prefix 00db4a2
mkdir -p /path/to/prefix/qualification/v1-p2
cp qualification/v1-p2/issue13_{concurrency,racer}.py /path/to/prefix/qualification/v1-p2/
.venv/bin/python /path/to/prefix/qualification/v1-p2/issue13_concurrency.py \
  --race --rounds 12 --racers 4 --files 3000 --file-lines 300
```

`--files` is the whole trick. It sets how long the checkout takes, and therefore
how wide the window a concurrent caller can land in is. At the suite's default
one-file B1 the window is microseconds and the failure is a rare flake — which is
how it reached the gate as an unexplained one. At 3000 files it is about a
second, and every round fails.

## Product tests

The same facts are asserted in the suite, which needs no arguments:

```bash
.venv/bin/python -m pytest tests/test_attempt_crash_recovery.py
```
