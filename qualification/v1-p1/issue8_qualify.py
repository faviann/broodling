#!/usr/bin/env python3
"""Qualify issue #8 W1/W2/W5/W7 through the real SDK and sidecar."""

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
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan
from zeroshot._binary import resolve_binary
from zeroshot.run_errors import SubmissionConflictError

HERE = Path(__file__).resolve().parent
P1 = HERE.parent / "p1"
LEAF_BIN = HERE / "bin"
PINNED_ZEROSHOT_REVISION = "d0909615d6ba3c179b58bce15a059f40400ec995"


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def jsonable(value: Any) -> Any:
    if dataclasses.is_dataclass(value):
        return {field.name: jsonable(getattr(value, field.name)) for field in dataclasses.fields(value)}
    if isinstance(value, tuple):
        return [jsonable(item) for item in value]
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def json_sha256(value: Any) -> str:
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def command(*arguments: str, cwd: Path | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(arguments, cwd=cwd, check=check, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def git(workspace: Path, *arguments: str) -> str:
    return command("git", "-C", str(workspace), *arguments).stdout.strip()


def events(state: Path) -> list[dict[str, Any]]:
    path = state / "driver.jsonl"
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()] if path.exists() else []


def fixture_runtime() -> dict[str, Any]:
    runtime = read_json(P1 / "fixtures/runtime.json")
    extra = ["BROODLING_V1_CANDIDATE_SHA", "BROODLING_V1_SHARED_PATH"]
    for node in runtime["nodes"].values():
        allowed = node["connections"]["fixture"]
        allowed.extend(name for name in extra if name not in allowed)
    return runtime


GRAPH = read_json(P1 / "fixtures/graph.json")
RUNTIME = fixture_runtime()
INITIAL = read_json(P1 / "fixtures/initial-input.json")


def request(key: str, *, initial: dict[str, Any] | None = None, graph: dict[str, Any] | None = None, runtime: dict[str, Any] | None = None) -> RunRequest:
    return RunRequest(
        title="Broodling issue 8 V1 qualification",
        graph=GraphSpec.from_dict(graph or GRAPH),
        initial_input=initial or INITIAL,
        runtime=RuntimePlan.from_dict(runtime or RUNTIME),
        submission_key=key,
    )


def environment(scenario: str, state: Path, workspace: Path, shared: Path) -> dict[str, str]:
    return {
        "PATH": f"{LEAF_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "OPENAI_API_KEY": "broodling-v1-test-only",
        "BROODLING_FIXTURE_SCENARIO": scenario,
        "BROODLING_FIXTURE_STATE": str(state),
        "BROODLING_V1_CANDIDATE_SHA": sha256(workspace / "candidate.txt"),
        "BROODLING_Q4_EXPECTED_CANDIDATE": sha256(workspace / "candidate.txt"),
        "BROODLING_V1_SHARED_PATH": str(shared),
    }


def client(workspace: Path, state_dir: Path, scenario: str, leaf_state: Path, shared: Path) -> Client:
    return Client(target=LocalTarget(workspace, state_dir=state_dir), environment=environment(scenario, leaf_state, workspace, shared))


async def wait_node(run: Any, node: str, timeout: float) -> Any:
    async with asyncio.timeout(timeout):
        while True:
            status = await run.status()
            if any(item.node == node for item in status.active_executions):
                return status
            if status.result is not None:
                raise RuntimeError(f"run finished before {node} became active")
            await asyncio.sleep(0.03)


def controller_pid(binary: Path, state_dir: Path, run_id: str) -> tuple[int, list[str]]:
    bootstrap = str((state_dir / "runs" / run_id / "controller.bootstrap.json").resolve())
    matches: list[tuple[int, list[str]]] = []
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            arguments = [part.decode(errors="replace") for part in (entry / "cmdline").read_bytes().split(b"\0") if part]
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
        if len(arguments) == 4 and Path(arguments[0]).resolve() == binary.resolve() and arguments[1:] == ["__zeroshot-run-controller", "--bootstrap", bootstrap]:
            matches.append((int(entry.name), arguments))
    if len(matches) != 1:
        raise RuntimeError(f"expected exactly one controller, found {len(matches)}")
    return matches[0]


def prepare_source(root: Path) -> dict[str, Any]:
    source = root / "source"
    remote = root / "controlled-remote.git"
    source.mkdir(parents=True)
    git(source, "init", "-b", "main")
    git(source, "config", "user.name", "Broodling V1 Fixture")
    git(source, "config", "user.email", "broodling-v1@example.invalid")
    git(source, "config", "commit.gpgsign", "false")
    (source / "candidate.txt").write_text("ORIGINAL_B1\n", encoding="utf-8")
    (source / "INSTRUCTIONS.md").write_text("FROZEN_INSTRUCTIONS_V1\n", encoding="utf-8")
    (source / "evidence").mkdir()
    (source / "evidence/raw.txt").write_text("RAW_EVIDENCE_B1\n", encoding="utf-8")
    git(source, "add", ".")
    git(source, "commit", "-m", "original admitted state B1")
    b1 = git(source, "rev-parse", "HEAD")
    command("git", "init", "--bare", str(remote))
    git(source, "remote", "add", "origin", "https://github.com/example/broodling-v1-fixture.git")
    git(source, "remote", "add", "probe", str(remote))
    git(source, "push", "probe", "main")
    admitted = {
        "contractId": "contract-v1",
        "attemptId": "attempt-admission-template",
        "startingRevision": b1,
        "candidateSha256": sha256(source / "candidate.txt"),
        "instructionsSha256": sha256(source / "INSTRUCTIONS.md"),
        "rawEvidenceSha256": sha256(source / "evidence/raw.txt"),
        "instructions": (source / "INSTRUCTIONS.md").read_text(encoding="utf-8"),
        "requiredEffects": [],
    }
    (root / "admitted-contract.json").write_text(json.dumps(admitted, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return {"source": source, "remote": remote, "b1": b1, "admitted": admitted}


def worktree(source: Path, root: Path, name: str, revision: str) -> Path:
    path = root / "worktrees" / name
    path.parent.mkdir(exist_ok=True)
    git(source, "worktree", "add", "-b", f"qualification/{name}", str(path), revision)
    return path


def mutate_live_head(source: Path) -> str:
    (source / "candidate.txt").write_text("LIVE_HEAD_B2\n", encoding="utf-8")
    (source / "INSTRUCTIONS.md").write_text("LIVE_INSTRUCTIONS_B2\n", encoding="utf-8")
    git(source, "add", ".")
    git(source, "commit", "-m", "live head drift B2")
    return git(source, "rev-parse", "HEAD")


def workspace_observation(path: Path) -> dict[str, Any]:
    return {
        "path": str(path), "head": git(path, "rev-parse", "HEAD"),
        "candidateSha256": sha256(path / "candidate.txt"),
        "candidate": (path / "candidate.txt").read_text(encoding="utf-8"),
        "instructionsSha256": sha256(path / "INSTRUCTIONS.md"),
        "rawEvidenceExists": (path / "evidence/raw.txt").exists(),
        "gitCommonDir": git(path, "rev-parse", "--git-common-dir"),
    }


async def child(args: argparse.Namespace) -> int:
    workspace = args.child_workspace
    state = args.run_root / "native-state"
    leaf = args.run_root / "leaf" / args.child_name
    shared = args.run_root / "shared.txt"
    async with client(workspace, state, args.child_scenario, leaf, shared) as sdk:
        run = await sdk.submit(request(args.child_key))
        if args.child_mode == "lost-ack":
            os._exit(87)
        if args.child_mode == "after-directive":
            await wait_node(run, "repair", args.timeout)
            (args.run_root / "child-window.json").write_text(json.dumps({"runId": run.id, "window": "after-directive"}), encoding="utf-8")
            os._exit(86)
        if args.child_mode == "after-final":
            result = await run.wait(wait_timeout=args.timeout)
            (args.run_root / "child-final.json").write_text(json.dumps({"runId": run.id, "result": jsonable(result)}), encoding="utf-8")
            os._exit(85)
    return 1


def child_command(args: argparse.Namespace, mode: str, name: str, workspace: Path, scenario: str, key: str) -> list[str]:
    return [sys.executable, str(Path(__file__).resolve()), "--child-mode", mode, "--child-name", name, "--child-workspace", str(workspace), "--child-scenario", scenario, "--child-key", key, "--run-root", str(args.run_root), "--timeout", str(args.timeout), "--output", str(args.output), "--zeroshot-source", str(args.zeroshot_source), "--wheel", str(args.wheel)]


async def w1(args: argparse.Namespace, source_info: dict[str, Any]) -> dict[str, Any]:
    source, b1 = source_info["source"], source_info["b1"]
    t1 = worktree(source, args.run_root, "w1-a1", b1)
    key = "broodling-issue8-w1-lost-ack-v1"
    admission = {
        "contractId": "contract-v1", "attemptId": "w1-a1", "eligible": True,
        "startingRevision": b1, "worktree": str(t1), "exclusiveOwner": "work-unit-w1",
        "submissionKey": key, "runId": None,
        "candidateSha256": source_info["admitted"]["candidateSha256"],
        "instructionsSha256": source_info["admitted"]["instructionsSha256"],
    }
    (args.run_root / "w1-admission-before-submit.json").write_text(json.dumps(admission, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    before: set[str]
    async with client(t1, args.run_root / "native-state", "clean", args.run_root / "leaf/w1", args.run_root / "shared.txt") as sdk:
        before = {item.run_id for item in await sdk.list_runs()}
    process = await asyncio.create_subprocess_exec(*child_command(args, "lost-ack", "w1", t1, "clean", key))
    exit_code = await process.wait()
    async with client(t1, args.run_root / "native-state", "clean", args.run_root / "leaf/w1", args.run_root / "shared.txt") as sdk:
        inventory_after_loss = {item.run_id for item in await sdk.list_runs()}
        replay = await sdk.submit(request(key))
        result = await replay.wait(wait_timeout=args.timeout)
        inventory_after_replay = {item.run_id for item in await sdk.list_runs()}
    conflict: dict[str, Any] | None = None
    changed_request = request(key, initial={"fixture": "conflicting-request"})
    async with client(t1, args.run_root / "native-state", "clean", args.run_root / "leaf/w1", args.run_root / "shared.txt") as sdk:
        try:
            await sdk.submit(changed_request)
        except SubmissionConflictError as error:
            conflict = {"type": type(error).__name__, "existingRunId": error.existing_run_id, "message": str(error)}
        inventory_after_conflict = {item.run_id for item in await sdk.list_runs()}
    b2 = mutate_live_head(source)
    conflicting_source = worktree(source, args.run_root, "w1-conflicting-source", b2)
    source_conflict: dict[str, Any] | None = None
    async with client(conflicting_source, args.run_root / "native-state", "clean", args.run_root / "leaf/w1-source-conflict", args.run_root / "shared.txt") as sdk:
        try:
            await sdk.submit(request(key))
        except SubmissionConflictError as error:
            source_conflict = {"type": type(error).__name__, "existingRunId": error.existing_run_id, "message": str(error)}
        inventory_after_source_conflict = {item.run_id for item in await sdk.list_runs()}
    fresh = worktree(source, args.run_root, "w1-a2-fresh", b1)
    fresh_observation = workspace_observation(fresh)
    new_ids = inventory_after_replay - before
    checks = {
        "lostAcknowledgementExitedAtControlledPoint": exit_code == 87,
        "oneRunCreated": len(new_ids) == 1,
        "replayRecoveredSameRun": replay.id in new_ids and inventory_after_loss - before == new_ids,
        "conflictIdentifiedSameRun": conflict is not None and conflict["existingRunId"] == replay.id,
        "conflictCreatedNoRun": inventory_after_conflict == inventory_after_replay,
        "changedSourceConflictIdentifiedSameRun": source_conflict is not None and source_conflict["existingRunId"] == replay.id,
        "changedSourceConflictCreatedNoRun": inventory_after_source_conflict == inventory_after_replay,
        "freshWorktreeUsesB1": fresh_observation["head"] == b1,
        "freshContentAndInstructionsMatchAdmission": fresh_observation["candidateSha256"] == source_info["admitted"]["candidateSha256"] and fresh_observation["instructionsSha256"] == source_info["admitted"]["instructionsSha256"],
        "liveHeadActuallyChanged": b2 != b1,
    }
    return {"verdict": "PASS" if all(checks.values()) else "FAIL", "checks": checks, "admissionBeforeSubmission": admission, "runId": replay.id, "result": jsonable(result), "requestConflict": conflict, "sourceConflict": source_conflict, "inventoryDelta": sorted(new_ids), "b1": b1, "liveHeadB2": b2, "attempt1": workspace_observation(t1), "freshAttempt": fresh_observation}


async def run_case(args: argparse.Namespace, source: Path, b1: str, name: str, scenario: str) -> dict[str, Any]:
    workspace = worktree(source, args.run_root, name, b1)
    leaf = args.run_root / "leaf" / name
    async with client(workspace, args.run_root / "native-state", scenario, leaf, args.run_root / "shared.txt") as sdk:
        run = await sdk.submit(request(f"broodling-issue8-{name}-v1"))
        result = await run.wait(wait_timeout=args.timeout)
        status = await run.status()
    await asyncio.sleep(1.0 if "lingering-writer" in scenario else 0)
    return {"runId": run.id, "scenario": scenario, "result": jsonable(result), "status": jsonable(status), "events": events(leaf), "workspace": workspace_observation(workspace), "workspacePath": str(workspace)}


async def q4_case(args: argparse.Namespace, source: Path, b1: str, name: str, scenario: str) -> dict[str, Any]:
    sys.path.insert(0, str(P1))
    import issue5_qualify
    workspace = worktree(source, args.run_root, name, b1)
    leaf = args.run_root / "leaf" / name
    initial = {
        "expectedCandidateSha256": sha256(workspace / "candidate.txt"), "expectedContractId": "contract-v1",
        "expectedEvidenceId": "evidence-v1", "expectedPredecessorId": "predecessor-v1",
        "claimedCandidateSha256": "unset", "claimedContractId": "unset", "claimedEvidenceId": "unset", "claimedPredecessorId": "unset",
    }
    runtime = issue5_qualify.controlled_runtime()
    seen_bindings: set[int] = set()
    for node in runtime["nodes"].values():
        if id(node) in seen_bindings:
            continue
        seen_bindings.add(id(node))
        node["connections"]["fixture"].extend(["BROODLING_V1_CANDIDATE_SHA", "BROODLING_V1_SHARED_PATH"])
    async with client(workspace, args.run_root / "native-state", scenario, leaf, args.run_root / "shared.txt") as sdk:
        run = await sdk.submit(request(f"broodling-issue8-{name}-v1", initial=initial, graph=issue5_qualify.q4_graph(), runtime=runtime))
        result = await run.wait(wait_timeout=args.timeout)
    return {"runId": run.id, "scenario": scenario, "result": jsonable(result), "events": events(leaf), "workspace": workspace_observation(workspace)}


async def w2(args: argparse.Namespace, source_info: dict[str, Any]) -> dict[str, Any]:
    source, b1 = source_info["source"], source_info["b1"]
    first, second = await asyncio.gather(
        run_case(args, source, b1, "w2-work-unit-1", "repair"),
        run_case(args, source, b1, "w2-work-unit-2", "clean"),
    )
    assurance = await run_case(args, source, b1, "w2-assurance-write", "clean;assurance-write")
    lingering = await run_case(args, source, b1, "w2-lingering", "clean;lingering-writer")
    shared = await run_case(args, source, b1, "w2-shared", "clean;shared-path")
    missing = await run_case(args, source, b1, "w2-missing-artifact", "clean;missing-artifact")
    forged = await q4_case(args, source, b1, "w2-forged", "q4-forged")
    positive = await q4_case(args, source, b1, "w2-applicable-control", "q4-unchanged")
    repair_events = first["events"]
    reviews = [event for event in repair_events if event["node"] == "review"]
    checks = {
        "distinctExclusivePaths": first["workspacePath"] != second["workspacePath"],
        "sameTargetRepositorySupported": first["workspace"]["gitCommonDir"] == second["workspace"]["gitCommonDir"],
        "c1C2ReviewsRenewed": len(reviews) == 2 and reviews[0]["candidateBefore"] != reviews[1]["candidateBefore"],
        "assuranceWriteRejected": not any((event.get("action") or {}).get("succeeded") for event in assurance["events"]),
        "lingeringWriterContained": lingering["workspace"]["candidate"] != "LINGERING_WRITER\n",
        "sharedWritablePathProtected": not (args.run_root / "shared.txt").exists(),
        "missingArtifactBlocked": not missing["result"]["succeeded"],
        "forgedIdentifiersBlocked": not forged["result"]["succeeded"],
        "unchangedValidControlAccepted": positive["result"]["succeeded"],
    }
    return {"verdict": "PASS" if all(checks.values()) else "FAIL", "checks": checks, "cases": {"workUnits": [first, second], "assuranceWrite": assurance, "lingeringWriter": lingering, "sharedPath": shared, "missingArtifact": missing, "forgedIdentifiers": forged, "unchangedControl": positive}, "limitations": "The controlled leaf is cooperative evidence instrumentation, not hostile-agent confinement. The failed probes show the selected LocalTarget profile provides no candidate/read-only/shared-path enforcement."}


async def abandon_stop_case(args: argparse.Namespace, source: Path, b1: str, name: str, node: str, scenario: str) -> dict[str, Any]:
    workspace = worktree(source, args.run_root, name, b1)
    leaf = args.run_root / "leaf" / name
    async with client(workspace, args.run_root / "native-state", scenario, leaf, args.run_root / "shared.txt") as sdk:
        run = await sdk.submit(request(f"broodling-issue8-{name}-v1"))
        active = await wait_node(run, node, args.timeout)
        abandonment = {"attemptId": name, "runId": run.id, "eligible": False, "reason": "qualification-stop"}
        (args.run_root / f"{name}.abandoned.json").write_text(json.dumps(abandonment, sort_keys=True), encoding="utf-8")
        result = await run.force_stop()
        repeated = await run.force_stop()
        status = await run.status()
    git(source, "worktree", "remove", "--force", str(workspace))
    return {"runId": run.id, "statusAtAbandonment": jsonable(active), "result": jsonable(result), "repeatedStop": jsonable(repeated), "terminalStatus": jsonable(status), "events": events(leaf), "worktreeRetired": not workspace.exists(), "abandonment": abandonment}


async def controller_loss(args: argparse.Namespace, source: Path, b1: str) -> dict[str, Any]:
    name = "w5-controller-loss"
    workspace = worktree(source, args.run_root, name, b1)
    leaf = args.run_root / "leaf" / name
    binary = resolve_binary()
    async with client(workspace, args.run_root / "native-state", "clean;hang:final_assessment_authority", leaf, args.run_root / "shared.txt") as sdk:
        run = await sdk.submit(request("broodling-issue8-w5-controller-loss-v1"))
        active = await wait_node(run, "final_assessment_authority", args.timeout)
        abandonment = {"attemptId": name, "runId": run.id, "eligible": False, "reason": "controller-loss"}
        (args.run_root / f"{name}.abandoned.json").write_text(json.dumps(abandonment), encoding="utf-8")
        pid, argv = controller_pid(binary, args.run_root / "native-state", run.id)
        os.kill(pid, signal.SIGKILL)
        run_id = run.id
    async with client(workspace, args.run_root / "native-state", "clean", leaf, args.run_root / "shared.txt") as sdk:
        result = await sdk.get_run(run_id).wait(wait_timeout=args.timeout)
        status = await sdk.get_run(run_id).status()
    git(source, "worktree", "remove", "--force", str(workspace))
    return {"runId": run_id, "statusBeforeKill": jsonable(active), "controller": {"pid": pid, "signal": "SIGKILL", "argv": argv}, "result": jsonable(result), "terminalStatus": jsonable(status), "worktreeRetired": not workspace.exists(), "abandonment": abandonment}


async def caller_window(args: argparse.Namespace, source: Path, b1: str, mode: str, name: str, scenario: str, expected: int) -> dict[str, Any]:
    workspace = worktree(source, args.run_root, name, b1)
    key = f"broodling-issue8-{name}-v1"
    process = await asyncio.create_subprocess_exec(*child_command(args, mode, name, workspace, scenario, key))
    exit_code = await process.wait()
    marker = read_json(args.run_root / ("child-window.json" if mode == "after-directive" else "child-final.json"))
    run_id = marker["runId"]
    abandonment = {"attemptId": name, "runId": run_id, "eligible": False, "reason": f"caller-interrupted-{mode}"}
    (args.run_root / f"{name}.abandoned.json").write_text(json.dumps(abandonment), encoding="utf-8")
    async with client(workspace, args.run_root / "native-state", "clean", args.run_root / "leaf" / name, args.run_root / "shared.txt") as sdk:
        run = sdk.get_run(run_id)
        result = await run.force_stop()
        status = await run.status()
    git(source, "worktree", "remove", "--force", str(workspace))
    return {"runId": run_id, "childExitCode": exit_code, "expectedExitCode": expected, "window": mode, "marker": marker, "abandonment": abandonment, "resultAfterReconciliation": jsonable(result), "terminalStatus": jsonable(status), "worktreeRetired": not workspace.exists()}


async def w5(args: argparse.Namespace, source_info: dict[str, Any]) -> dict[str, Any]:
    source, b1 = source_info["source"], source_info["b1"]
    adjudication = await abandon_stop_case(args, source, b1, "w5-stop-adjudication", "adjudicate_authority", "clean;hang:adjudicate_authority")
    mutation = await abandon_stop_case(args, source, b1, "w5-stop-mutation", "repair", "repair;hang:repair")
    loss = await controller_loss(args, source, b1)
    directive = await caller_window(args, source, b1, "after-directive", "w5-caller-directive", "repair;hang:repair", 86)
    final = await caller_window(args, source, b1, "after-final", "w5-caller-final", "clean", 85)
    replacement = await run_case(args, source, b1, "w5-replacement", "clean")
    replacement_events = replacement["events"]
    semantic_canaries = ["C1_FROM_IMPLEMENT", "C2_FROM_REPAIR", "UNAUTHORIZED_", "LINGERING_WRITER"]
    prompt_leak = any(event["carryoverCanariesSeen"] for event in replacement_events)
    cases = [adjudication, mutation, loss, directive, final]
    checks = {
        "allOldAttemptsDurablyIneligible": all(case.get("abandonment", {"eligible": False})["eligible"] is False for case in cases),
        "stopDuringAdjudication": adjudication["result"]["failure"] == "force_stopped",
        "stopDuringMutation": mutation["result"]["failure"] == "force_stopped",
        "repeatedStopIdempotent": adjudication["repeatedStop"] == adjudication["result"] and mutation["repeatedStop"] == mutation["result"],
        "controllerLossObserved": loss["result"]["failure"] == "runtime_lost",
        "callerDirectiveWindow": directive["childExitCode"] == 86,
        "callerFinalWindow": final["childExitCode"] == 85 and final["marker"]["result"]["succeeded"],
        "allOldWorktreesRetiredAfterTerminal": all(case["worktreeRetired"] for case in cases),
        "replacementNewRunAndWorktree": replacement["runId"] not in {case["runId"] for case in cases},
        "replacementFromOriginalB1": replacement["workspace"]["head"] == b1 and replacement["workspace"]["candidate"] == "ORIGINAL_B1\n",
        "zeroAutomaticSemanticCarryoverObserved": not prompt_leak,
        "freshProviderStateAndSessionScope": all(event["invocation"] == 1 for event in replacement_events) and RUNTIME["nodes"]["review"]["sessionScope"] == "execution",
        "lateTerminalResultsCannotRestoreEligibility": all(case.get("abandonment", {"eligible": False})["eligible"] is False for case in cases),
    }
    return {"verdict": "PASS" if all(checks.values()) else "FAIL", "checks": checks, "cases": {"stopAdjudication": adjudication, "stopMutation": mutation, "controllerLoss": loss, "callerAfterDirective": directive, "callerAfterFinal": final, "replacement": replacement}, "limitations": "Wholesale abandonment is proven without reading or carrying completed occurrence semantics. Process containment is limited to this single-host controlled-leaf profile; no same-run takeover, distributed fencing, or completed-occurrence recovery is claimed."}


def admission(effect_requirements: list[str], effect_dependent_evidence: list[str]) -> dict[str, Any]:
    obligations = [*effect_requirements, *effect_dependent_evidence]
    return {"admitted": not obligations, "preservedObligations": obligations, "reason": None if not obligations else "unsupported_required_effect"}


async def w7(args: argparse.Namespace, source_info: dict[str, Any]) -> dict[str, Any]:
    source, b1 = source_info["source"], source_info["b1"]
    no_effect = await run_case(args, source, b1, "w7-no-effect", "clean")
    gap = await run_case(args, source, b1, "w7-final-gap", "clean;final-gap")
    optional = await run_case(args, source, b1, "w7-optional-permission", "clean")
    optional_remote_unchanged = command("git", "--git-dir", str(source_info["remote"]), "show-ref", "--verify", "--quiet", "refs/tags/v1-effect-bypass-probe", check=False).returncode != 0
    bypass = await run_case(args, source, b1, "w7-effect-bypass", "clean;effect-bypass")
    effect_events = events(args.run_root / "leaf/w7-effect-bypass")
    action = next(event["action"] for event in effect_events if event["node"] == "implement")
    contracts = {
        "noEffect": admission([], []),
        "noEffectWithUnusedOptionalPermission": {**admission([], []), "optionalPermissions": ["controlled-local-git-probe"]},
        "commitDelivery": admission(["commit-as-delivery"], []),
        "push": admission(["push"], []),
        "pullRequestMerge": admission(["pull-request", "merge"], []),
        "issueMutation": admission(["issue-comment", "issue-close", "issue-label", "issue-relationship"], []),
        "effectDependentEvidence": admission([], ["evidence-after-publication"]),
    }
    graph_text = json.dumps(GRAPH)
    runtime_text = json.dumps(RUNTIME)
    disposition = {"noEffect": no_effect["result"]["succeeded"] and contracts["noEffect"]["admitted"], "semanticGap": False}
    access = next(event["toolAndCredentialAccess"] for event in bypass["events"] if event["node"] == "implement")
    checks = {
        "genuineNoEffectAdmitted": contracts["noEffect"]["admitted"],
        "effectContractsRejectedWithoutStripping": all(not value["admitted"] and value["preservedObligations"] for key, value in contracts.items() if key not in {"noEffect", "noEffectWithUnusedOptionalPermission"}),
        "gitDeliveryExcluded": "GitDelivery" not in graph_text and "gitDelivery" not in runtime_text and "git_delivery" not in runtime_text,
        "noEffectRunCompleted": no_effect["result"]["succeeded"],
        "unusedOptionalPermissionCausedNoEffect": optional["result"]["succeeded"] and optional_remote_unchanged,
        "bareRuntimeSuccessNotAccepted": gap["result"]["succeeded"] and disposition["semanticGap"] is False,
        "agentEffectBypassRejected": all(item["returnCode"] != 0 for item in action["actions"]),
        "agentCredentialAndToolPathsProtected": access["gitExecutable"] is None and access["ghExecutable"] is None and not access["githubHostsFileReadable"],
        "sharedGitMetadataProtected": command("git", "-C", str(source), "config", "--get", "broodling.probe", check=False).returncode != 0,
        "controlledRemoteUnchanged": command("git", "--git-dir", str(source_info["remote"]), "show-ref", "--verify", "--quiet", "refs/tags/v1-effect-bypass-probe", check=False).returncode != 0,
    }
    return {"verdict": "PASS" if all(checks.values()) else "FAIL", "checks": checks, "contracts": contracts, "cases": {"noEffect": no_effect, "semanticGap": gap, "optionalPermission": optional, "effectBypass": bypass}, "effectProbe": action, "toolAndCredentialAccess": access, "limitations": "All mutation targets were controlled local fixtures. No GitHub write was attempted. The failed boundary demonstrates that excluding GitDelivery and omitting credentials is insufficient when an agent process can invoke Git against shared metadata/remotes; the readable configured GitHub hosts file was detected without reading or recording its contents."}


async def execute(args: argparse.Namespace) -> dict[str, Any]:
    if args.run_root.exists() and any(args.run_root.iterdir()):
        raise RuntimeError("--run-root must be absent or empty")
    args.run_root.mkdir(parents=True, exist_ok=True)
    source_info = prepare_source(args.run_root)
    w1_record = await w1(args, source_info)
    w2_record = await w2(args, source_info)
    w5_record = await w5(args, source_info)
    w7_record = await w7(args, source_info)
    binary = resolve_binary()
    record = {
        "schema": "broodling.issue-8-v1-qualification/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": {"issue": 8, "witnesses": ["W1", "W2", "W5", "W7"], "productCodeImplemented": False, "laterIssueWork": False, "g1V1": "not_evaluated_by_issue_8"},
        "verdicts": {"W1": w1_record["verdict"], "W2": w2_record["verdict"], "W5": w5_record["verdict"], "W7": w7_record["verdict"]},
        "build": {
            "zeroshotRevision": PINNED_ZEROSHOT_REVISION,
            "zeroshotTree": git(args.zeroshot_source, "rev-parse", "HEAD^{tree}"),
            "sdkDistribution": "zeroshot-rust", "sdkVersion": importlib.metadata.version("zeroshot-rust"),
            "wheel": str(args.wheel.resolve()), "wheelSha256": sha256(args.wheel),
            "sidecar": str(binary), "sidecarVersion": command(str(binary), "--version").stdout.strip(), "sidecarSha256": sha256(binary),
            "controlledLeafSha256": sha256(LEAF_BIN / "codex"), "driverSha256": sha256(Path(__file__)),
            "python": sys.version.split()[0], "platform": platform.platform(),
            "cargo": command(str(args.cargo), "--version").stdout.strip(),
        },
        "profile": {
            "target": "LocalTarget", "host": "single Linux x86-64 host", "provider": "openai", "harness": "codex",
            "providerLeaf": "controlled", "sessionScope": "execution", "stateDir": str((args.run_root / "native-state").resolve()),
            "sourcePolicy": "Each Git worktree has a unique qualification branch created at immutable B1 because the pinned sidecar rejects detached HEAD; same-repository worktrees intentionally share Git common metadata for W2/W7 probes.",
            "worktreePolicy": "One path per fixture Attempt; retirement only after terminal force_stopped/runtime_lost observation.",
            "credentialPolicy": "Test-only OPENAI_API_KEY; no live provider or GitHub credential passed. Controlled local bare remote only.",
            "instrumentation": "Controlled Codex JSONL leaf records prompt digests, candidate bytes/digests, raw-artifact presence and probe results; SDK status/result and administrative facts are retained.",
            "retention": "Committed machine record retains observations; disposable LocalTarget state/worktrees are not a production retention promise.",
        },
        "fixture": {"graph": GRAPH, "runtime": RUNTIME, "initialInput": INITIAL, "graphSha256": json_sha256(GRAPH), "runtimeSha256": json_sha256(RUNTIME), "initialInputSha256": json_sha256(INITIAL), "admittedContract": source_info["admitted"], "controlledLeaves": ["all agent/provider occurrences"]},
        "W1": w1_record, "W2": w2_record, "W5": w5_record, "W7": w7_record,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return record


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--run-root", required=True, type=Path)
    result.add_argument("--output", required=True, type=Path)
    result.add_argument("--zeroshot-source", required=True, type=Path)
    result.add_argument("--wheel", required=True, type=Path)
    result.add_argument("--cargo", type=Path, default=Path("/home/faviann/.cargo/bin/cargo"))
    result.add_argument("--timeout", type=float, default=60.0)
    result.add_argument("--child-mode", choices=("lost-ack", "after-directive", "after-final"))
    result.add_argument("--child-name")
    result.add_argument("--child-workspace", type=Path)
    result.add_argument("--child-scenario")
    result.add_argument("--child-key")
    return result


def main() -> int:
    args = parser().parse_args()
    if args.child_mode:
        return asyncio.run(child(args))
    record = asyncio.run(execute(args))
    print(json.dumps({"output": str(args.output.resolve()), "verdicts": record["verdicts"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
