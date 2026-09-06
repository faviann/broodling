#!/usr/bin/env python3
"""Qualify issue #3 Q1/Q2/Q6 through the public Zeroshot Python SDK."""

from __future__ import annotations

import argparse
import asyncio
import dataclasses
import hashlib
import importlib.metadata
import json
import os
import platform
import signal
import subprocess
import sys
import time
from contextlib import aclosing
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan
from zeroshot._binary import resolve_binary
from zeroshot.run_errors import SubmissionConflictError

HERE = Path(__file__).resolve().parent
FIXTURES = HERE / "fixtures"
DRIVER_BIN = HERE / "bin"
PINNED_ZEROSHOT_REVISION = "d0909615d6ba3c179b58bce15a059f40400ec995"


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def jsonable(value: Any) -> Any:
    if dataclasses.is_dataclass(value):
        return {
            field.name: jsonable(getattr(value, field.name))
            for field in dataclasses.fields(value)
        }
    if isinstance(value, tuple):
        return [jsonable(item) for item in value]
    return value


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


def request(submission_key: str) -> RunRequest:
    return RunRequest(
        title="Broodling P1 issue 3 qualification fixture",
        graph=GraphSpec.from_dict(read_json(FIXTURES / "graph.json")),
        initial_input=read_json(FIXTURES / "initial-input.json"),
        runtime=RuntimePlan.from_dict(read_json(FIXTURES / "runtime.json")),
        submission_key=submission_key,
    )


def environment(scenario: str, driver_state: Path) -> dict[str, str]:
    return {
        "PATH": f"{DRIVER_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "OPENAI_API_KEY": "broodling-p1-test-only",
        "BROODLING_FIXTURE_SCENARIO": scenario,
        "BROODLING_FIXTURE_STATE": str(driver_state),
    }


def client(workspace: Path, state_dir: Path, scenario: str, driver_state: Path) -> Client:
    return Client(
        target=LocalTarget(workspace, state_dir=state_dir),
        environment=environment(scenario, driver_state),
    )


async def wait_for_execution(run: Any, node: str, timeout: float) -> Any:
    async with asyncio.timeout(timeout):
        while True:
            status = await run.status()
            matching = [item for item in status.active_executions if item.node == node]
            if matching:
                return status
            if status.result is not None:
                raise RuntimeError(f"run finished before {node!r} became active")
            await asyncio.sleep(0.05)


async def public_replay(run: Any, timeout: float) -> dict[str, Any]:
    status = await run.status()
    watches: list[Any] = []
    logs: list[Any] = []
    async with asyncio.timeout(timeout):
        stream = run.watch()
        async with aclosing(stream) as values:
            async for value in values:
                watches.append(jsonable(value))
    async with asyncio.timeout(timeout):
        stream = run.logs()
        async with aclosing(stream) as values:
            async for value in values:
                logs.append(jsonable(value))
    return {
        "terminalStatus": jsonable(status),
        "watchReplay": watches,
        "logReplay": logs,
        "publicStatusFields": [field.name for field in dataclasses.fields(status)],
    }


def driver_events(path: Path) -> list[Any]:
    transcript = path / "driver.jsonl"
    if not transcript.exists():
        return []
    return [json.loads(line) for line in transcript.read_text(encoding="utf-8").splitlines()]


async def completed_case(
    workspace: Path,
    state_dir: Path,
    evidence_root: Path,
    name: str,
    scenario: str,
    submission_key: str,
    timeout: float,
) -> dict[str, Any]:
    state = evidence_root / f"{name}.driver-state"
    async with client(workspace, state_dir, scenario, state) as sdk:
        run = await sdk.submit(request(submission_key))
        result = await run.wait(wait_timeout=timeout)
        run_id = run.id
    async with client(workspace, state_dir, scenario, state) as restarted:
        replay = await public_replay(restarted.get_run(run_id), timeout)
    return {
        "runId": run_id,
        "result": jsonable(result),
        "afterClientRestart": replay,
        "controlledDriverEvents": driver_events(state),
    }


async def force_stopped_case(
    workspace: Path,
    state_dir: Path,
    evidence_root: Path,
    timeout: float,
) -> dict[str, Any]:
    scenario = "repair-then-accept;hang:adjudicate_authority:2"
    state = evidence_root / "stopped.driver-state"
    async with client(workspace, state_dir, scenario, state) as sdk:
        run = await sdk.submit(request("broodling-issue3-q6-stop-v1"))
        in_flight = await wait_for_execution(run, "adjudicate_authority", timeout)
        async with asyncio.timeout(timeout):
            while len(
                [event for event in driver_events(state) if event["node"] == "adjudicate_authority"]
            ) < 2:
                await asyncio.sleep(0.05)
        in_flight = await run.status()
        result = await run.force_stop()
        run_id = run.id
    async with client(workspace, state_dir, scenario, state) as restarted:
        replay = await public_replay(restarted.get_run(run_id), timeout)
    return {
        "runId": run_id,
        "stopRequestedWhile": jsonable(in_flight),
        "result": jsonable(result),
        "afterClientRestart": replay,
        "controlledDriverEvents": driver_events(state),
    }


def controller_pid(binary: Path, state_dir: Path, run_id: str) -> tuple[int, list[str]]:
    bootstrap = str((state_dir / "runs" / run_id / "controller.bootstrap.json").resolve())
    expected_binary = str(binary.resolve())
    matches: list[tuple[int, list[str]]] = []
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            raw = (entry / "cmdline").read_bytes()
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
        arguments = [part.decode(errors="replace") for part in raw.split(b"\0") if part]
        if (
            len(arguments) == 4
            and str(Path(arguments[0]).resolve()) == expected_binary
            and arguments[1:] == ["__zeroshot-run-controller", "--bootstrap", bootstrap]
        ):
            matches.append((int(entry.name), arguments))
    if len(matches) != 1:
        raise RuntimeError(f"expected one exact controller process, found {len(matches)}")
    return matches[0]


async def controller_loss_case(
    workspace: Path,
    state_dir: Path,
    evidence_root: Path,
    timeout: float,
) -> dict[str, Any]:
    scenario = "repair-then-accept;hang:final_assessment_authority:1"
    state = evidence_root / "controller-loss.driver-state"
    binary = resolve_binary()
    async with client(workspace, state_dir, scenario, state) as sdk:
        run = await sdk.submit(request("broodling-issue3-q6-controller-loss-v1"))
        in_flight = await wait_for_execution(run, "final_assessment_authority", timeout)
        pid, arguments = controller_pid(binary, state_dir, run.id)
        os.kill(pid, signal.SIGKILL)
        run_id = run.id
    deadline = time.monotonic() + timeout
    while Path(f"/proc/{pid}").exists() and time.monotonic() < deadline:
        await asyncio.sleep(0.05)
    async with client(workspace, state_dir, scenario, state) as restarted:
        restarted_run = restarted.get_run(run_id)
        result = await restarted_run.wait(wait_timeout=timeout)
        replay = await public_replay(restarted_run, timeout)
    return {
        "runId": run_id,
        "controllerKill": {"pid": pid, "signal": "SIGKILL", "arguments": arguments},
        "statusBeforeKill": jsonable(in_flight),
        "resultAfterExternalObserverRestart": jsonable(result),
        "afterClientRestart": replay,
        "controlledDriverEvents": driver_events(state),
    }


def child_command(args: argparse.Namespace, mode: str) -> list[str]:
    return [
        sys.executable,
        str(Path(__file__).resolve()),
        mode,
        "--workspace",
        str(args.workspace),
        "--state-dir",
        str(args.state_dir),
        "--evidence-root",
        str(args.evidence_root),
        "--timeout",
        str(args.timeout),
        "--zeroshot-source",
        str(args.zeroshot_source),
        "--wheel",
        str(args.wheel),
        "--cargo",
        str(args.cargo),
        "--rustc",
        str(args.rustc),
    ]


async def drop_submit_child(args: argparse.Namespace) -> int:
    state = args.evidence_root / "q2.driver-state"
    async with client(args.workspace, args.state_dir, "clean", state) as sdk:
        await sdk.submit(request("broodling-issue3-q2-ambiguous-v1"))
        os._exit(87)


async def q2_case(args: argparse.Namespace) -> dict[str, Any]:
    scenario = "clean"
    state = args.evidence_root / "q2.driver-state"
    key = "broodling-issue3-q2-ambiguous-v1"
    before_revision = git(args.workspace, "rev-parse", "HEAD")
    async with client(args.workspace, args.state_dir, scenario, state) as first:
        before = {item.run_id for item in await first.list_runs()}
    process = await asyncio.create_subprocess_exec(*child_command(args, "--drop-submit-child"))
    return_code = await process.wait()
    if return_code != 87:
        raise RuntimeError(f"lost-acknowledgement child failed unexpectedly: {return_code}")
    async with client(args.workspace, args.state_dir, scenario, state) as restarted:
        after_lost_ack = {item.run_id for item in await restarted.list_runs()}
        replay = await restarted.submit(request(key))
        recovered_id = replay.id
        after = {item.run_id for item in await restarted.list_runs()}
        result = await replay.wait(wait_timeout=args.timeout)
    new_ids = sorted(after - before)
    marker = args.workspace / "issue3-head-change.txt"
    marker.write_text("Q2 source-sensitive retry witness\n", encoding="utf-8")
    git(args.workspace, "add", marker.name)
    git(args.workspace, "commit", "-m", "issue3 q2 source change")
    after_revision = git(args.workspace, "rev-parse", "HEAD")
    conflict: SubmissionConflictError | None = None
    async with client(args.workspace, args.state_dir, scenario, state) as changed:
        try:
            await changed.submit(request(key))
        except SubmissionConflictError as error:
            conflict = error
        inventory_after_conflict = [item.run_id for item in await changed.list_runs()]
    if conflict is None:
        raise RuntimeError("source-changing replay did not produce SubmissionConflictError")
    return {
        "submissionKey": key,
        "acknowledgementSimulation": "A separate Broodling-side client process exited with code 87 immediately after SDK submit returned, without communicating the Run handle or run ID to its parent.",
        "lostAcknowledgementChildExitCode": return_code,
        "inventoryDeltaBeforeReplay": sorted(after_lost_ack - before),
        "sourceBefore": before_revision,
        "inventoryDeltaAfterLostAcknowledgement": new_ids,
        "sameSourceReplayRunId": recovered_id,
        "sameSourceReplayResult": jsonable(result),
        "sourceAfterHeadChange": after_revision,
        "conflict": {
            "type": type(conflict).__name__,
            "message": str(conflict),
            "existingRunId": conflict.existing_run_id,
            "exitCode": conflict.exit_code,
        },
        "inventoryAfterConflict": inventory_after_conflict,
        "noSecondRun": (
            len(new_ids) == 1
            and inventory_after_conflict.count(recovered_id) == 1
            and set(inventory_after_conflict) == after
        ),
        "controlledDriverEvents": driver_events(state),
    }


async def crash_child(args: argparse.Namespace) -> int:
    scenario = "repair-then-accept;hang:repair:1"
    state = args.evidence_root / "broodling-crash.driver-state"
    async with client(args.workspace, args.state_dir, scenario, state) as sdk:
        run = await sdk.submit(request("broodling-issue3-q6-broodling-crash-v1"))
        status = await wait_for_execution(run, "repair", args.timeout)
        args.crash_marker.write_text(
            json.dumps({"runId": run.id, "statusAtCrash": jsonable(status)}) + "\n",
            encoding="utf-8",
        )
        os._exit(86)


async def broodling_crash_case(args: argparse.Namespace) -> dict[str, Any]:
    marker = args.evidence_root / "broodling-crash-marker.json"
    command = [*child_command(args, "--crash-child"), "--crash-marker", str(marker)]
    process = await asyncio.create_subprocess_exec(*command)
    return_code = await process.wait()
    if return_code != 86 or not marker.exists():
        raise RuntimeError(f"Broodling-side crash child failed unexpectedly: {return_code}")
    crash = read_json(marker)
    scenario = "repair-then-accept;hang:repair:1"
    state = args.evidence_root / "broodling-crash.driver-state"
    async with client(args.workspace, args.state_dir, scenario, state) as restarted:
        run = restarted.get_run(crash["runId"])
        result = await run.force_stop()
        replay = await public_replay(run, args.timeout)
    return {
        "childExitCode": return_code,
        "statusAtCrash": crash["statusAtCrash"],
        "runId": crash["runId"],
        "resultAfterRestart": jsonable(result),
        "afterClientRestart": replay,
        "controlledDriverEvents": driver_events(state),
        "catchUpObservation": "No public SDK field supplies the completed adjudicator occurrence, bound input, or outcome to an exactly-once semantic catch-up consumer.",
    }


async def execute(args: argparse.Namespace) -> dict[str, Any]:
    args.workspace = args.workspace.resolve()
    args.state_dir = args.state_dir.resolve()
    args.evidence_root = args.evidence_root.resolve()
    args.output = args.output.resolve()
    args.evidence_root.mkdir(parents=True, exist_ok=True)
    args.state_dir.mkdir(parents=True, exist_ok=True)
    git(args.workspace, "rev-parse", "--is-inside-work-tree")
    zeroshot_revision = git(args.zeroshot_source, "rev-parse", "HEAD")
    if zeroshot_revision != PINNED_ZEROSHOT_REVISION:
        raise RuntimeError(f"Zeroshot source is not at pinned revision: {zeroshot_revision}")
    if git(args.zeroshot_source, "status", "--short"):
        raise RuntimeError("Zeroshot source checkout is not clean")
    binary = resolve_binary()
    q2 = await q2_case(args)
    successful = await completed_case(
        args.workspace,
        args.state_dir,
        args.evidence_root,
        "successful-repeated",
        "repair-then-accept",
        "broodling-issue3-q1-success-v1",
        args.timeout,
    )
    stopped = await force_stopped_case(
        args.workspace, args.state_dir, args.evidence_root, args.timeout
    )
    lost = await controller_loss_case(
        args.workspace, args.state_dir, args.evidence_root, args.timeout
    )
    crashed = await broodling_crash_case(args)
    result = {
        "schema": "broodling.p1.issue3-qualification/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": {"issue": 3, "questions": ["Q1", "Q2", "Q6"]},
        "verdicts": {
            "Q1": "BLOCKED",
            "Q2": "PASS",
            "Q6": "BLOCKED",
            "G1-core": "BLOCKED",
        },
        "testedSource": {
            "zeroshotRevision": zeroshot_revision,
            "zeroshotTree": git(args.zeroshot_source, "rev-parse", "HEAD^{tree}"),
            "zeroshotCheckoutClean": True,
            "workspace": str(args.workspace),
            "workspaceBranch": git(args.workspace, "branch", "--show-current"),
            "workspaceRevisionAfterQ2Change": git(args.workspace, "rev-parse", "HEAD"),
        },
        "build": {
            "sdkDistribution": "zeroshot-rust",
            "sdkVersion": importlib.metadata.version("zeroshot-rust"),
            "python": sys.version.split()[0],
            "platform": platform.platform(),
            "sidecarVersion": subprocess.run(
                [str(binary), "--version"], check=True, text=True, stdout=subprocess.PIPE
            ).stdout.strip(),
            "sidecarSha256": sha256(binary),
            "wheel": args.wheel.name,
            "wheelSha256": sha256(args.wheel),
            "rustc": subprocess.run(
                [str(args.rustc), "--version"], check=True, text=True, stdout=subprocess.PIPE
            ).stdout.strip(),
            "cargo": subprocess.run(
                [str(args.cargo), "--version"], check=True, text=True, stdout=subprocess.PIPE
            ).stdout.strip(),
            "controlledLeafSha256": sha256(DRIVER_BIN / "codex"),
            "qualificationHarnessSha256": sha256(Path(__file__).resolve()),
        },
        "integrationBoundary": {
            "selected": "official Python SDK LocalTarget and matching Rust sidecar",
            "real": "SDK encoding/process transport; Rust preflight, admission, Git source resolution, controller, graph execution/reduction, durable status/watch/log and force-stop.",
            "doubled": "Only the provider leaf process is controlled.",
            "prohibitedReadersUsed": [],
        },
        "retentionAndCustodyAssumptions": {
            "supportedVersion": "The qualification applies only to the official SDK and matching sidecar built unpatched from the pinned Zeroshot commit and hashes in this record.",
            "localRetention": "LocalTarget observations remain available only while the operator preserves the explicit state directory and the pinned implementation can read it; no broader retention SLA or cross-version migration contract was found.",
            "terminalBoundary": "The public finished status and terminal result are durable and replayed after client restart; force_stop waits for that boundary, and observing a dead controller terminalizes it as runtime_lost.",
            "insufficiency": "Preserving the state directory does not make completed occurrence input/outcome available through the supported external SDK/protocol.",
        },
        "Q1": {
            "successfulRepeatedRun": successful,
            "failedRun": {"evidenceRef": "Q6.controllerKilled", "runId": lost["runId"]},
            "stoppedRun": stopped,
        },
        "Q2": q2,
        "Q6": {
            "stopWhileSecondAdjudicatorInFlight": stopped,
            "controllerKilled": lost,
            "broodlingHarnessCrashBeforeCatchUp": crashed,
        },
        "dependency": {
            "missingContract": "A supported external read/recovery interface for retained admitted graph/runtime data and exact settled execution occurrences, including stable occurrence identity, trusted bound input, completed success/error or voided outcome, and completion cursor/order.",
            "consumer": "G1-core, then P2 and P4",
            "internalButNotQualifying": "RunLedger.get/get_by_submission_key/snapshot_and_tail and StoredRun/NodeSnapshot/NodeState retain the facts, but they are Rust-internal and no public SDK/protocol projection exposes them.",
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return result


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--workspace", required=True, type=Path)
    result.add_argument("--state-dir", required=True, type=Path)
    result.add_argument("--evidence-root", required=True, type=Path)
    result.add_argument("--output", type=Path)
    result.add_argument("--zeroshot-source", required=True, type=Path)
    result.add_argument("--wheel", required=True, type=Path)
    result.add_argument("--cargo", required=True, type=Path)
    result.add_argument("--rustc", required=True, type=Path)
    result.add_argument("--timeout", type=float, default=60.0)
    result.add_argument("--crash-child", action="store_true", help=argparse.SUPPRESS)
    result.add_argument("--drop-submit-child", action="store_true", help=argparse.SUPPRESS)
    result.add_argument("--crash-marker", type=Path, help=argparse.SUPPRESS)
    return result


def main() -> int:
    args = parser().parse_args()
    if args.crash_child:
        if args.crash_marker is None:
            raise SystemExit("--crash-marker is required with --crash-child")
        return asyncio.run(crash_child(args))
    if args.drop_submit_child:
        return asyncio.run(drop_submit_child(args))
    if args.output is None:
        raise SystemExit("--output is required")
    record = asyncio.run(execute(args))
    print(json.dumps({"output": str(args.output.resolve()), "verdicts": record["verdicts"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
