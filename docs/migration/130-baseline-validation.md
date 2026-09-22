# Frozen Python baseline validation

This is parity evidence for [#130](https://github.com/faviann/broodling/issues/130),
not a provider-quality verdict or permission for live execution.

## Independently rerun baseline

On 22 September 2026 the full supported suite ran in a detached worktree at:

- Commit: `b3f61a96c40401722ec16fc361958d1690982e02`
- Tree: `7e061c9314d16785c070483799d0e8057a42ba31`
- Working directory: `/home/faviann/repos/broodling-130-baseline`
- Command: `/home/faviann/repos/broodling/.venv/bin/python -m pytest tests`

The interpreter and installed dependencies came from the existing virtual
environment; the application and tests came from the exact baseline checkout.
The worktree was clean before and after the run. No source changes, credentials,
live provider calls, or GitHub delivery were needed.

| Component | Observed version |
| --- | --- |
| Host | Linux `6.17.13-2-pve`, x86-64, glibc 2.41 |
| Python | 3.13.5 |
| SQLite | 3.46.1 |
| Git | 2.47.3 |
| Zeroshot Python SDK | 10.3.0.post1, project's pinned release wheel |
| pytest | 9.1.1 |
| pytest-timeout | 2.4.0 |
| Hypothesis | 6.168.0 |

Relevant output:

```text
rootdir: /home/faviann/repos/broodling-130-baseline
configfile: pyproject.toml
plugins: timeout-2.4.0, hypothesis-6.168.0
collected 404 items
tests/test_gateway_runtime.py .
tests/test_workflow_result.py ............
======================= 404 passed in 128.75s (0:02:08) ========================
```

Exit status was **0**: **404 passed, 0 failed, 0 skipped**. The default suite's
controlled-provider checks exercised the pinned SDK/native seam; those checks
were not disabled or replaced by mocks for this run. Other tests intentionally
control external boundaries as described by the baseline `tests/README.md`.

## Earlier evidence and limits

[PR #131](https://github.com/faviann/broodling/pull/131) separately reports an
exact-baseline run with `404 passed in 132.61s`, zero failures/skips, and the
controlled native checks included. The run above independently confirms that
record; it is not a reclassification of the author's report.

The [Zeroshot spike](https://github.com/faviann/broodling/issues/130#issuecomment-5776348033)
ran against `0a36793` plus disposable spike changes. Its 14 bridge checks and
later Python run establish different evidence and are not the frozen-baseline
run. Twelve bridge scenarios used the released SDK/native engine with a
controlled provider; two used a stub SDK for PR receipt/DirectTarget behavior.
Neither run establishes real DirectTarget PR delivery or semantic quality.

The retained [P5 limitation](../../evaluation/p5/README.md) remains unchanged.
