#!/usr/bin/env python3
"""Qualify issue #4 Q3 through the public Zeroshot Python SDK."""

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
from contextlib import aclosing
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan
from zeroshot._binary import resolve_binary

HERE = Path(__file__).resolve().parent
DRIVER_BIN = HERE / "bin"
PINNED_ZEROSHOT_REVISION = "d0909615d6ba3c179b58bce15a059f40400ec995"
STATE_PATHS = [["fixture"], ["obligation"], ["latestFindings"]]
ERROR_LABELS = ["timeout", "crash", "malformed", "refusal"]


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


def json_sha256(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def git(workspace: Path, *arguments: str) -> str:
    return subprocess.run(
        ["git", "-C", str(workspace), *arguments],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    ).stdout.strip()


def enum_type(*labels: str) -> dict[str, Any]:
    return {"kind": "enum", "values": list(labels)}


def field(value_type: dict[str, Any]) -> dict[str, Any]:
    return {"required": True, "type": value_type}


def state_type() -> dict[str, Any]:
    return {
        "kind": "record",
        "fields": {
            "fixture": field({"kind": "string"}),
            "obligation": field(enum_type("none", "open_d1", "resolve_d1")),
            "latestFindings": field(enum_type("clean", "found")),
        },
    }


def fail(name: str, reason: str) -> dict[str, Any]:
    return {"kind": "fail", "name": name, "reason": reason}


def error_guard(node: str) -> dict[str, Any]:
    return {
        "kind": "in",
        "value": {"name": node, "source": "error", "field": None},
        "labels": ERROR_LABELS,
    }


def signal_guard(node: str, field_name: str, *labels: str) -> dict[str, Any]:
    return {
        "kind": "in",
        "value": {"name": node, "source": "signal", "field": field_name},
        "labels": list(labels),
    }


def choice(
    name: str,
    branches: list[tuple[dict[str, Any], dict[str, Any]]],
    otherwise: dict[str, Any] | None,
) -> dict[str, Any]:
    return {
        "kind": "choice",
        "name": name,
        "state": state_type(),
        "branches": [{"when": guard, "node": node} for guard, node in branches],
        "otherwise": otherwise,
        "promotedStatePaths": STATE_PATHS,
    }


def worker(name: str, input_fields: dict[str, Any] | None = None) -> dict[str, Any]:
    bindings = []
    if input_fields:
        bindings = [
            {"target": [target], "value": {"source": "state", "path": [source]}}
            for target, source in input_fields.items()
        ]
    return {
        "kind": "step",
        "name": name,
        "worker": f"agent.broodling-{name.replace('_', '-')}@1",
        "instructions": f"BROODLING_NODE={name}. Produce the controlled ordinary-worker outcome.",
        "input": {
            "kind": "record",
            "fields": {
                target: field(state_type()["fields"][source]["type"])
                for target, source in (input_fields or {}).items()
            },
        } if input_fields else {"kind": "null"},
        "output": {"kind": "null"},
        "inputBindings": bindings,
        "writeBindings": [],
        "timeoutMs": 250,
        "attempts": 1,
    }


def verifier(
    name: str,
    signal_name: str,
    labels: list[str],
    input_fields: dict[str, str] | None = None,
    state_target: str | None = None,
) -> dict[str, Any]:
    result = worker(name, input_fields)
    result["kind"] = "verifier"
    result["signals"] = {signal_name: labels}
    result["diagnostic"] = {"kind": "null"}
    if state_target:
        result["writeBindings"] = [{
            "value": {"node": name, "channel": "signal", "path": [signal_name]},
            "target": [state_target],
        }]
    return result


def success(suffix: str) -> dict[str, Any]:
    return {
        "kind": "succeed",
        "name": f"fixture_complete_{suffix}",
        "output": state_type(),
        "bindings": [
            {"target": path, "value": {"source": "state", "path": path}}
            for path in STATE_PATHS
        ],
    }


def final_flow(suffix: str) -> dict[str, Any]:
    authority_name = f"final_assessment_authority_{suffix}"
    node = verifier(
        authority_name, "assessment", ["accepted", "gap"],
        {"outstanding": "obligation", "findings": "latestFindings"},
    )
    route = choice(
        f"final_assessment_route_{suffix}",
        [
            (error_guard(node["name"]), fail(f"final_execution_unusable_{suffix}", "execution_unusable")),
            (signal_guard(node["name"], "assessment", "gap"), fail(f"semantic_gap_{suffix}", "semantic_gap")),
        ],
        success(suffix),
    )
    return {
        "kind": "seq", "name": f"final_semantic_assessment_{suffix}", "state": state_type(),
        "children": [node, route], "promotedStatePaths": STATE_PATHS,
    }


def repair_round(index: int, maximum: int) -> dict[str, Any]:
    repair_name = f"repair_{index}"
    review_name = f"review_{index}"
    authority_name = f"resolution_authority_{index}"
    exhausted = fail("obligations_exhausted", "obligations_exhausted")
    next_round = exhausted if index == maximum else repair_round(index + 1, maximum)
    authority = verifier(
        authority_name,
        "resolution",
        ["open_d1", "resolve_d1"],
        {"outstanding": "obligation", "findings": "latestFindings"},
        "obligation",
    )
    authority_route = choice(
        f"resolution_route_{index}",
        [
            (error_guard(authority_name), fail(f"resolution_unusable_{index}", "execution_unusable")),
            (signal_guard(authority_name, "resolution", "resolve_d1"), final_flow(str(index))),
        ],
        next_round,
    )
    review = verifier(
        review_name, "findings", ["found", "clean"], state_target="latestFindings"
    )
    review_route = choice(
        f"review_route_{index}",
        [(error_guard(review_name), fail(f"review_unusable_{index}", "execution_unusable"))],
        {
            "kind": "seq", "name": f"resolution_sequence_{index}", "state": state_type(),
            "children": [authority, authority_route], "promotedStatePaths": STATE_PATHS,
        },
    )
    repair = worker(repair_name, {"directive": "obligation"})
    repair_route = choice(
        f"repair_route_{index}",
        [(error_guard(repair_name), fail(f"repair_unusable_{index}", "execution_unusable"))],
        {
            "kind": "seq", "name": f"rereview_sequence_{index}", "state": state_type(),
            "children": [review, review_route], "promotedStatePaths": STATE_PATHS,
        },
    )
    return {
        "kind": "seq", "name": f"repair_round_{index}", "state": state_type(),
        "children": [repair, repair_route], "promotedStatePaths": STATE_PATHS,
    }


def graph() -> dict[str, Any]:
    adjudicate = verifier(
        "adjudicate_authority",
        "decision",
        ["none", "open_d1"],
        {"findings": "latestFindings", "outstanding": "obligation"},
        "obligation",
    )
    adjudicate_route = choice(
        "adjudication_route",
        [
            (error_guard("adjudicate_authority"), fail("adjudication_unusable", "execution_unusable")),
            (signal_guard("adjudicate_authority", "decision", "none"), final_flow("clean")),
        ],
        repair_round(1, 3),
    )
    review = verifier("review", "findings", ["found", "clean"], state_target="latestFindings")
    review_route = choice(
        "initial_review_route",
        [(error_guard("review"), fail("initial_review_unusable", "execution_unusable"))],
        {
            "kind": "seq", "name": "initial_adjudication", "state": state_type(),
            "children": [adjudicate, adjudicate_route], "promotedStatePaths": STATE_PATHS,
        },
    )
    identity = verifier("identity_check", "applicability", ["applicable", "stale"])
    identity_route = choice(
        "identity_route",
        [
            (error_guard("identity_check"), fail("identity_unusable", "execution_unusable")),
            (signal_guard("identity_check", "applicability", "stale"), fail("identity_stale", "identity_or_applicability_failed")),
        ],
        {
            "kind": "seq", "name": "review_and_adjudicate", "state": state_type(),
            "children": [review, review_route], "promotedStatePaths": STATE_PATHS,
        },
    )
    implement = worker("implement")
    implement_route = choice(
        "implementation_route",
        [(error_guard("implement"), fail("implementation_unusable", "execution_unusable"))],
        {
            "kind": "seq", "name": "applicability_and_assurance", "state": state_type(),
            "children": [identity, identity_route], "promotedStatePaths": STATE_PATHS,
        },
    )
    return {
        "profile": "openengine.graph.full/v1",
        "initialInput": state_type(),
        "policy": {"policy": "policy.native-v2@1", "default": "deny"},
        "root": {
            "kind": "seq", "name": "q3_qualification", "state": state_type(),
            "children": [implement, implement_route], "promotedStatePaths": [],
        },
    }


def executable_names(node: dict[str, Any]) -> list[str]:
    kind = node["kind"]
    if kind in ("step", "verifier"):
        return [node["name"]]
    if kind == "seq":
        return [name for child in node["children"] for name in executable_names(child)]
    if kind == "choice":
        names = [name for branch in node["branches"] for name in executable_names(branch["node"])]
        if node.get("otherwise"):
            names.extend(executable_names(node["otherwise"]))
        return names
    return []


def runtime(graph_value: dict[str, Any]) -> dict[str, Any]:
    binding = {
        "kind": "agent", "model": "broodling-p1-controlled-leaf", "effort": "low",
        "sessionScope": "execution",
        "connections": {"fixture": ["OPENAI_API_KEY", "BROODLING_FIXTURE_SCENARIO", "BROODLING_FIXTURE_STATE"]},
    }
    return {
        "harness": "codex", "provider": "openai", "size": "small",
        "nodes": {name: binding for name in sorted(set(executable_names(graph_value["root"])))},
    }


def environment(scenario: str, driver_state: Path, credential: bool = True) -> dict[str, str]:
    result = {
        "PATH": f"{DRIVER_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "BROODLING_FIXTURE_SCENARIO": scenario,
        "BROODLING_FIXTURE_STATE": str(driver_state),
    }
    result["OPENAI_API_KEY"] = "broodling-p1-test-only" if credential else ""
    return result


def driver_events(path: Path) -> list[Any]:
    transcript = path / "driver.jsonl"
    if not transcript.exists():
        return []
    return [json.loads(line) for line in transcript.read_text(encoding="utf-8").splitlines()]


async def replay(run: Any, timeout: float) -> dict[str, Any]:
    transitions: list[dict[str, Any]] = []
    last: list[str] | None = None
    async with asyncio.timeout(timeout):
        stream = run.watch()
        async with aclosing(stream) as values:
            async for status in values:
                active = [execution.node for execution in status.active_executions]
                if active != last:
                    transitions.append({"cursor": status.cursor, "active": active, "phase": status.phase})
                    last = active
    status = await run.status()
    return {"transitions": transitions, "terminalStatus": jsonable(status)}


async def run_case(
    args: argparse.Namespace,
    graph_value: dict[str, Any],
    runtime_value: dict[str, Any],
    name: str,
    scenario: str,
    credential: bool = True,
    force_stop_at: str | None = None,
) -> dict[str, Any]:
    state = args.evidence_root / f"{name}.driver-state"
    request = RunRequest(
        title=f"Broodling issue 4 Q3: {name}",
        graph=GraphSpec.from_dict(graph_value),
        initial_input={"fixture": "broodling-q3-v1", "obligation": "none", "latestFindings": "clean"},
        runtime=RuntimePlan.from_dict(runtime_value),
        submission_key=f"broodling-issue4-{name}-v1",
    )
    async with Client(
        target=LocalTarget(args.workspace, state_dir=args.state_dir),
        environment=environment(scenario, state, credential),
    ) as sdk:
        run = await sdk.submit(request)
        interruption = None
        if force_stop_at:
            async with asyncio.timeout(args.timeout):
                while True:
                    status = await run.status()
                    if any(item.node == force_stop_at for item in status.active_executions):
                        interruption = jsonable(status)
                        break
                    if status.result is not None:
                        raise RuntimeError(f"run finished before force-stop point {force_stop_at}")
                    await asyncio.sleep(0.05)
            result = await run.force_stop()
        else:
            result = await run.wait(wait_timeout=args.timeout)
        observed = await replay(run, args.timeout)
    return {
        "runId": run.id,
        "scenario": scenario,
        "credentialProvided": credential,
        "forceStopAt": force_stop_at,
        "statusAtForceStop": interruption,
        "result": jsonable(result),
        "publicReplay": observed,
        "controlledDriverEvents": driver_events(state),
    }


async def execute(args: argparse.Namespace) -> dict[str, Any]:
    args.workspace = args.workspace.resolve()
    args.state_dir = args.state_dir.resolve()
    args.evidence_root = args.evidence_root.resolve()
    args.output = args.output.resolve()
    args.evidence_root.mkdir(parents=True, exist_ok=True)
    args.state_dir.mkdir(parents=True, exist_ok=True)
    git(args.workspace, "rev-parse", "--is-inside-work-tree")
    revision = git(args.zeroshot_source, "rev-parse", "HEAD")
    if revision != PINNED_ZEROSHOT_REVISION:
        raise RuntimeError(f"Zeroshot source is not at pinned revision: {revision}")
    if git(args.zeroshot_source, "status", "--short"):
        raise RuntimeError("Zeroshot source checkout is not clean")

    graph_value = graph()
    runtime_value = runtime(graph_value)
    cases = {}
    definitions = [
        ("clean_control", "q3-clean", True),
        ("red_repair_control", "q3-repair-resolve", True),
        ("crash", "crash:review", True),
        ("timeout", "hang:review", True),
        ("refusal", "hang:review", True, "review"),
        ("malformed", "malformed:review", True),
        ("missing_output", "missing:review", True),
        ("failed_applicability", "q3-stale-applicability", True),
        ("contradictory_payload_signal", "q3-contradictory-payload-signal", True),
        ("reviewer_authority_claim", "reviewer_claim:review", True),
        ("sticky_omission", "q3-sticky-omission", True),
        ("final_gap_after_clean_adjudication", "q3-final-gap-after-repair", True),
    ]
    definitions = [item if len(item) == 4 else (*item, None) for item in definitions]
    for name, scenario, credential, force_stop_at in definitions:
        cases[name] = await run_case(
            args, graph_value, runtime_value, name, scenario, credential, force_stop_at
        )

    expected_failures = {
        "crash": "execution_unusable",
        "timeout": "execution_unusable",
        "refusal": "force_stopped",
        "malformed": "execution_unusable",
        "missing_output": "execution_unusable",
        "failed_applicability": "identity_or_applicability_failed",
        "contradictory_payload_signal": "execution_unusable",
        "reviewer_authority_claim": "execution_unusable",
        "sticky_omission": "obligations_exhausted",
        "final_gap_after_clean_adjudication": "semantic_gap",
    }
    checks: dict[str, bool] = {
        "cleanControlSucceeded": cases["clean_control"]["result"]["succeeded"] is True,
        "redRepairControlSucceeded": cases["red_repair_control"]["result"]["succeeded"] is True,
        "repairReceivedOnlyCanonicalDirective": any(
            event["node"] == "repair_1" and event.get("input") == {"directive": "open_d1"}
            for event in cases["red_repair_control"]["controlledDriverEvents"]
        ),
        "resolutionReceivedStickyStateAndEmptyReview": any(
            event["node"] == "resolution_authority_1"
            and event.get("input") == {"findings": "clean", "outstanding": "open_d1"}
            for event in cases["red_repair_control"]["controlledDriverEvents"]
        ),
        "allNegativeCasesFailedAsExpected": all(
            cases[name]["result"]["succeeded"] is False
            and cases[name]["result"]["failure"] == failure
            for name, failure in expected_failures.items()
        ),
    }
    if not all(checks.values()):
        raise RuntimeError(f"Q3 acceptance checks failed: {checks}")

    binary = resolve_binary()
    result = {
        "schema": "broodling.p1.issue4-qualification/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": {"issue": 4, "questions": ["Q3"]},
        "verdicts": {"Q3": "PASS", "G1-core": "BLOCKED"},
        "gateNote": "Q3 passes. G1-core remains blocked by issue #3 Q1/Q6; this run does not attempt to resolve that dependency.",
        "testedSource": {
            "zeroshotRevision": revision,
            "zeroshotTree": git(args.zeroshot_source, "rev-parse", "HEAD^{tree}"),
            "zeroshotCheckoutClean": True,
            "workspace": str(args.workspace),
            "workspaceBranch": git(args.workspace, "branch", "--show-current"),
            "workspaceRevision": git(args.workspace, "rev-parse", "HEAD"),
        },
        "build": {
            "sdkDistribution": "zeroshot-rust",
            "sdkVersion": importlib.metadata.version("zeroshot-rust"),
            "python": sys.version.split()[0],
            "platform": platform.platform(),
            "sidecarVersion": subprocess.run([str(binary), "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
            "sidecarSha256": sha256(binary),
            "wheel": args.wheel.name,
            "wheelSha256": sha256(args.wheel),
            "rustc": subprocess.run([str(args.rustc), "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
            "cargo": subprocess.run([str(args.cargo), "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
            "controlledLeafSha256": sha256(DRIVER_BIN / "codex"),
            "qualificationHarnessSha256": sha256(Path(__file__).resolve()),
        },
        "integrationBoundary": {
            "selected": "official Python SDK LocalTarget and matching Rust sidecar",
            "real": "SDK encoding/process transport; Rust preflight and admission; Git source resolution; controller; graph response validation, state dataflow, routing, bounded execution, and durable terminal result.",
            "doubled": "Only the provider leaf process is controlled.",
            "broodlingValidationOrScheduling": False,
            "prohibitedReadersUsed": [],
        },
        "fixture": {
            "graph": graph_value,
            "graphCanonicalSha256": json_sha256(graph_value),
            "runtime": runtime_value,
            "runtimeCanonicalSha256": json_sha256(runtime_value),
            "roles": {
                name: ("designated_authority" if name == "adjudicate_authority" or name.startswith("resolution_authority_") or name.startswith("final_assessment_authority_") else "ordinary")
                for name in runtime_value["nodes"]
            },
            "canonicalDecision": "The obligation token is the adjudicator signal itself. Only designated adjudicator occurrences have write bindings to obligation; repair and review have none. Once open_d1 is entered, resolution occurrences cannot emit none, and only resolve_d1 reaches final assessment.",
            "repairBound": 3,
        },
        "cases": cases,
        "acceptanceChecks": checks,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return result


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--workspace", required=True, type=Path)
    result.add_argument("--state-dir", required=True, type=Path)
    result.add_argument("--evidence-root", required=True, type=Path)
    result.add_argument("--output", required=True, type=Path)
    result.add_argument("--zeroshot-source", required=True, type=Path)
    result.add_argument("--wheel", required=True, type=Path)
    result.add_argument("--cargo", required=True, type=Path)
    result.add_argument("--rustc", required=True, type=Path)
    result.add_argument("--timeout", type=float, default=60.0)
    return result


def main() -> int:
    args = parser().parse_args()
    record = asyncio.run(execute(args))
    print(json.dumps({"output": str(args.output.resolve()), "verdicts": record["verdicts"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
