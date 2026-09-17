#!/usr/bin/env python3
"""Probe the pinned LocalTarget + explicit PR-source combination without a run."""

from __future__ import annotations

import asyncio
import importlib.metadata
import json
import os
import subprocess
import tempfile
from pathlib import Path

from zeroshot import Client, InvalidRequestError, LocalTarget, Preset, UniformRuntime


ROOT = Path(__file__).resolve().parents[2]
EXPECTED_SDK = "10.3.0.post1"


def head_revision() -> str:
    return subprocess.run(
        ["git", "-C", str(ROOT), "rev-parse", "HEAD"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


async def probe(state_dir: Path) -> dict[str, object]:
    requested = {
        "repository": "faviann/broodling",
        "branch": "main",
        "revision": head_revision(),
    }
    runtime = UniformRuntime(
        harness="codex",
        provider="openai",
        model="gpt-5.6-sol",
        effort="low",
        size="small",
        session_scope="execution",
        connections={},
    )
    async with Client(
        target=LocalTarget(ROOT, state_dir=state_dir),
        # The sentinel can satisfy PR preflight but is never used: explicit
        # source authority is rejected before controller or provider startup.
        environment={"GH_TOKEN": "issue-72-probe-not-used"},
    ) as client:
        try:
            await client.submit(
                "Do not execute; this request must fail native source preflight.",
                title="Issue 72 node-local source authority probe",
                preset=Preset("software-change", delivery="pull_request"),
                runtime=runtime,
                repository=requested["repository"],
                branch=requested["branch"],
                revision=requested["revision"],
                submission_key="broodling:issue-72:node-local-source-authority",
            )
        except InvalidRequestError as error:
            blocked = "require --target" in str(error)
            return {
                "schema": "broodling.node-local-source-authority-probe/v1",
                "outcome": "BLOCKED" if blocked else "UNEXPECTED",
                "zeroshotSdk": importlib.metadata.version(
                    "the-open-engine-zeroshot"
                ),
                "preset": {
                    "name": "software-change",
                    "delivery": "pull_request",
                },
                "runtimeConnections": {},
                "requestedSource": requested,
                "nativeError": {"code": error.code, "message": str(error)},
                "runStarted": False,
                "providerCalls": 0,
                "githubEffects": 0,
            }
    return {
        "schema": "broodling.node-local-source-authority-probe/v1",
        "outcome": "UNEXPECTED",
        "reason": "pinned LocalTarget accepted explicit source authority",
    }


def main() -> int:
    installed = importlib.metadata.version("the-open-engine-zeroshot")
    if installed != EXPECTED_SDK:
        raise SystemExit(f"requires the pinned SDK {EXPECTED_SDK}; found {installed}")
    if os.environ.get("ZEROSHOT_PYTHON_NATIVE_BINARY"):
        raise SystemExit("use the pinned SDK's bundled native executable")
    with tempfile.TemporaryDirectory(prefix="broodling-issue-72-") as directory:
        result = asyncio.run(probe(Path(directory)))
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0 if result["outcome"] == "BLOCKED" else 1


if __name__ == "__main__":
    raise SystemExit(main())
