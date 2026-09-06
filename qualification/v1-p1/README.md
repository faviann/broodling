# V1-P1 issue #8 qualification

This directory contains the qualification-only fixture for Broodling issue #8. It reuses the
official Python SDK and matching Rust sidecar build described by the historical P1 harness, while
keeping the new V1 observations separate from `qualification/p1`.

Only the provider leaf is controlled. SDK request encoding and subprocess transport, Rust
preflight/admission, source resolution, the local controller, graph execution/reduction,
submission-key reconciliation, force-stop/runtime-loss handling, and durable status/result paths
remain real. The fixture creates minimal administrative JSON only to mark an Attempt ineligible
before cessation/restart; it is not Broodling product code or a selected store design.

## Reproduction

Use clean Zeroshot source `d0909615d6ba3c179b58bce15a059f40400ec995` and the official wheel
whose SHA-256 is `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`.
Its matching sidecar SHA-256 is
`9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.
Build and install it using the historical [P1 instructions](../p1/README.md#reproduction), then:

```bash
RUN_ROOT=$(mktemp -d /dev/shm/b8.XXXXXX)
rmdir "$RUN_ROOT"

VENV=/path/to/venv
WHEEL=/path/to/zeroshot_rust-0.1.0.dev0-py3-none-linux_x86_64.whl

"$VENV/bin/python" qualification/v1-p1/issue8_qualify.py \
  --run-root "$RUN_ROOT" \
  --output "$RUN_ROOT/issue-8-run-record.json" \
  --zeroshot-source /path/to/zeroshot \
  --wheel "$WHEEL" \
  --timeout 60
```

Keep `RUN_ROOT` short: the pinned sidecar rejects a local-controller Unix socket path that exceeds
its limit. The run root must be absent or empty. The driver creates attached per-Attempt branches
at immutable B1 because this build rejects detached worktrees.

The committed [report](issue-8-w1-w2-w5-w7.md) explains the verdicts and limits. The
[machine record](evidence/issue-8-run-record.json) retains all run IDs, request fixtures, terminal
observations, controlled-leaf events, administrative facts, and probe results.

## Checks

```bash
python3 -m unittest discover -s qualification/v1-p1/tests -v
python3 -m unittest discover -s qualification/p1/tests -v
python3 -m py_compile qualification/v1-p1/issue8_qualify.py qualification/v1-p1/bin/codex
git diff --check
```
