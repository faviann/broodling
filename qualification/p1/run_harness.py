#!/usr/bin/env python3
"""Run the P1 fixture through the official Zeroshot Python SDK and Rust sidecar."""

from __future__ import annotations

import argparse
import asyncio
import dataclasses
import hashlib
import importlib.metadata
import json
import os
import platform
import subprocess
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
FIXTURES = HERE / "fixtures"
DRIVER_BIN = HERE / "bin"
PINNED_ZEROSHOT_REVISION = "d0909615d6ba3c179b58bce15a059f40400ec995"


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def git(workspace: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", "-C", str(workspace), *arguments],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    ).stdout.strip()


def jsonable(value: Any) -> Any:
    if dataclasses.is_dataclass(value):
        return {
            field.name: jsonable(getattr(value, field.name))
            for field in dataclasses.fields(value)
        }
    if isinstance(value, tuple):
        return [jsonable(item) for item in value]
    return value


async def wait_for_execution(run: Any, node: str, timeout: float) -> None:
    async with asyncio.timeout(timeout):
        while True:
            status = await run.status()
            if any(execution.node == node for execution in status.active_executions):
                return
            if status.result is not None:
                raise RuntimeError(f"run finished before interruption point {node!r}")
            await asyncio.sleep(0.05)


async def command_output(*arguments: str) -> str:
    process = await asyncio.create_subprocess_exec(
        *arguments,
        stdout=asyncio.subprocess.PIPE,
    )
    stdout, _ = await process.communicate()
    if process.returncode != 0:
        raise subprocess.CalledProcessError(process.returncode, arguments)
    return stdout.decode().strip()


async def execute(args: argparse.Namespace) -> dict[str, Any]:
    from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan
    from zeroshot._binary import resolve_binary

    workspace = args.workspace.resolve()
    state_dir = args.state_dir.resolve()
    evidence = args.evidence.resolve()
    driver_state = evidence.parent / f"{evidence.stem}.driver-state"
    graph = read_json(FIXTURES / "graph.json")
    runtime = read_json(FIXTURES / "runtime.json")
    initial_input = read_json(FIXTURES / "initial-input.json")
    roles = read_json(FIXTURES / "roles.json")

    state_dir.mkdir(parents=True, exist_ok=True)
    evidence.parent.mkdir(parents=True, exist_ok=True)
    driver_state.mkdir(parents=True, exist_ok=True)
    git(workspace, "rev-parse", "--is-inside-work-tree")

    scenario = f"hang:{args.interrupt_at}" if args.interrupt_at else args.scenario
    environment = {
        "PATH": f"{DRIVER_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "OPENAI_API_KEY": "broodling-p1-test-only",
        "BROODLING_FIXTURE_SCENARIO": scenario,
        "BROODLING_FIXTURE_STATE": str(driver_state),
    }
    request = RunRequest(
        title="Broodling P1 external qualification fixture",
        graph=GraphSpec.from_dict(graph),
        initial_input=initial_input,
        runtime=RuntimePlan.from_dict(runtime),
        submission_key=args.submission_key,
    )

    interruption_observed = False
    async with Client(
        target=LocalTarget(workspace, state_dir=state_dir),
        environment=environment,
    ) as client:
        run = await client.submit(request)
        if args.interrupt_at:
            await wait_for_execution(run, args.interrupt_at, args.wait_timeout)
            interruption_observed = True
            result = await run.force_stop()
        else:
            result = await run.wait(wait_timeout=args.wait_timeout)
        status = await run.status()

    binary = resolve_binary()
    sidecar_version = await command_output(str(binary), "--version")
    record = {
        "schema": "broodling.p1-harness-run/v1",
        "scope": {
            "issue": 2,
            "qualificationVerdict": "not_evaluated",
            "productArchitectureDecision": "deferred",
            "semanticQuality": "not_evaluated",
            "workUnitSuccess": "not_claimed",
        },
        "recordedAt": datetime.now(UTC).isoformat(),
        "testedSource": {
            "zeroshotRevision": PINNED_ZEROSHOT_REVISION,
            "workspace": str(workspace),
            "workspaceRevision": git(workspace, "rev-parse", "HEAD"),
            "workspaceBranch": git(workspace, "branch", "--show-current"),
        },
        "build": {
            "sdkDistribution": "zeroshot-rust",
            "sdkVersion": importlib.metadata.version("zeroshot-rust"),
            "python": sys.version.split()[0],
            "platform": platform.platform(),
            "sidecarVersion": sidecar_version,
            "sidecarSha256": sha256(binary),
            "controlledLeafSha256": sha256(DRIVER_BIN / "codex"),
        },
        "profile": {
            "target": "LocalTarget",
            "provider": "openai",
            "harness": "codex",
            "credentialAssumption": (
                "A test-only placeholder satisfies the declared provider binding; "
                "no live provider is contacted."
            ),
            "contextAssumption": (
                "The fake Codex executable receives the native-built prompt and returns "
                "controlled protocol output; effective-context qualification belongs to Q5."
            ),
            "doubled": "Only the provider leaf process is controlled by qualification/p1/bin/codex.",
            "real": (
                "Official Python SDK request encoding/process transport, Rust preflight/admission, "
                "local controller, Git source resolution, graph execution/reduction, and durable "
                "status/result."
            ),
        },
        "fixture": {
            "scenario": scenario,
            "roles": roles,
            "interruptionPointObserved": args.interrupt_at if interruption_observed else None,
        },
        "request": {
            "title": request.title,
            "graph": graph,
            "initialInput": initial_input,
            "runtime": runtime,
            "submissionKey": request.submission_key,
        },
        "returnedRunId": run.id,
        "terminalResult": jsonable(result),
        "terminalStatus": jsonable(status),
        "driverTranscript": str(driver_state / "driver.jsonl"),
    }
    evidence.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return record


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--workspace", required=True, type=Path)
    result.add_argument("--state-dir", required=True, type=Path)
    result.add_argument("--evidence", required=True, type=Path)
    result.add_argument("--submission-key", default="broodling-p1-repair-then-accept-v1")
    result.add_argument(
        "--scenario",
        default="repair-then-accept",
        help="repair-then-accept, clean, gap, crash:NODE, or malformed:NODE",
    )
    result.add_argument(
        "--interrupt-at",
        choices=(
            "implement",
            "review",
            "adjudicate_authority",
            "repair",
            "round_complete",
            "final_assessment_authority",
        ),
    )
    result.add_argument("--wait-timeout", type=float, default=60.0)
    return result


def main() -> int:
    args = parser().parse_args()
    record = asyncio.run(execute(args))
    print(
        json.dumps(
            {
                "evidence": str(args.evidence.resolve()),
                "runId": record["returnedRunId"],
                "succeeded": record["terminalResult"]["succeeded"],
                "failure": record["terminalResult"]["failure"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
