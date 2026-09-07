# V1-P2 qualification and gate records

| File | What it is |
|---|---|
| [`issue-15-g2-v1.md`](issue-15-g2-v1.md) | The G2-V1 gate review and its **BLOCKED** verdict. |
| [`issue-13-concurrency.md`](issue-13-concurrency.md) | Issue #13's answer to that review's §5.3 concurrency blocker. |
| `issue13_concurrency.py`, `issue13_racer.py` | The retained reproducer behind it. |
| `evidence/` | Machine records from the runs the report cites. |

The #13 reproducer needs only Broodling and local Git. The #14 integration
witnesses additionally require the exact qualified Zeroshot SDK/sidecar.

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

To reproduce the flake as the gate saw it rather than the amplified failure, run
the same command against `00db4a2` with `--rounds 300 --racers 2 --files 1`. It
takes a few minutes and violates a handful of rounds; the retained records are
`evidence/issue-13-race-reviewed-flake-{1,2}.json`.

## Product tests

The same facts are asserted in the suite, which needs no arguments:

```bash
.venv/bin/python -m pytest tests/test_attempt_crash_recovery.py
```


## Reproducing the #14 correlation evidence

See [issue-14-correlation.md](issue-14-correlation.md) for scope and retained
results. Use the G1-V1 wheel/build from the [qualified boundary](../v1-p1/issue-11-g1-v1.md).
The adapter rejects any different SDK source, version or sidecar hash. In the
recorded environment `SDK_PYTHON` was
`/home/faviann/.cache/broodling-zeroshot-venv/bin/python`.

```bash
SDK_PYTHON=/path/to/qualified-venv/bin/python
"$SDK_PYTHON" -m pytest tests -q
"$SDK_PYTHON" qualification/v1-p2/issue14_regression.py --legacy-b1-guard
# Expected failure above: replay was improperly required to remain at B1.
"$SDK_PYTHON" qualification/v1-p2/issue14_regression.py
"$SDK_PYTHON" qualification/v1-p2/issue14_evidence.py \
  --output /path/to/issue-14-graph-crash.json
```

Without the SDK, normal `python -m pytest tests` runs admission/storage/control
tests and explicitly skips the real integration cases. The evidence scripts
require the qualified build and cannot report a skipped integration as a pass.
These commands run #14 product tests, not G2 or V1-P3.
