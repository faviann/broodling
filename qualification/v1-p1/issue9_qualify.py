#!/usr/bin/env python3
"""Qualify issue #9 W3 through the real Zeroshot SDK and sidecar."""

from __future__ import annotations

import argparse
import asyncio
import dataclasses
import hashlib
import importlib.metadata
import json
import os
import platform
import shutil
import subprocess
import sys
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan
from zeroshot._binary import resolve_binary

HERE = Path(__file__).resolve().parent
LEAF_BIN = HERE / "w3-bin"
PINNED_ZEROSHOT_REVISION = "d0909615d6ba3c179b58bce15a059f40400ec995"
ERROR_LABELS = ["timeout", "crash", "malformed", "refusal"]
REPAIR_BOUND = 3


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


def git(path: Path, *arguments: str) -> str:
    return subprocess.run(["git", "-C", str(path), *arguments], check=True, text=True, stdout=subprocess.PIPE).stdout.strip()


def enum_type(*values: str) -> dict[str, Any]:
    return {"kind": "enum", "values": list(values)}


def field(value_type: dict[str, Any], required: bool = True) -> dict[str, Any]:
    return {"required": required, "type": value_type}


def state_type() -> dict[str, Any]:
    return {
        "kind": "record",
        "fields": {
            "contract": field({"kind": "string"}),
            "candidateGeneration": field(enum_type("c0", "c1", "c2", "c3", "c4")),
            "evidence": field(enum_type("unchecked", "valid", "missing")),
            "findings": field(enum_type("unexecuted", "clean", "found")),
            "obligation": field(enum_type("none", "open_d1", "resolved_d1")),
        },
    }


STATE_PATHS = [[name] for name in state_type()["fields"]]


def fail(name: str, reason: str) -> dict[str, Any]:
    return {"kind": "fail", "name": name, "reason": reason}


def signal_guard(node: str, signal: str, *labels: str) -> dict[str, Any]:
    return {"kind": "in", "value": {"name": node, "source": "signal", "field": signal}, "labels": list(labels)}


def error_guard(node: str) -> dict[str, Any]:
    return {"kind": "in", "value": {"name": node, "source": "error", "field": None}, "labels": ERROR_LABELS}


def seq(name: str, children: list[dict[str, Any]]) -> dict[str, Any]:
    return {"kind": "seq", "name": name, "state": state_type(), "children": children, "promotedStatePaths": STATE_PATHS}


def choice(name: str, branches: list[tuple[dict[str, Any], dict[str, Any]]], otherwise: dict[str, Any] | None) -> dict[str, Any]:
    return {
        "kind": "choice", "name": name, "state": state_type(),
        "branches": [{"when": guard, "node": node} for guard, node in branches],
        "otherwise": otherwise, "promotedStatePaths": STATE_PATHS,
    }


def diagnostic_type() -> dict[str, Any]:
    return {
        "kind": "record",
        "fields": {
            name: field({"kind": "string"}, False)
            for name in ("contractId", "sourceId", "evidenceId", "predecessorId", "note")
        },
    }


def inputs(mapping: dict[str, str]) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    fields = state_type()["fields"]
    return (
        {"kind": "record", "fields": {target: fields[source] for target, source in mapping.items()}},
        [{"target": [target], "value": {"source": "state", "path": [source]}} for target, source in mapping.items()],
    )


def step(name: str, mapping: dict[str, str], output: dict[str, Any], writes: list[dict[str, Any]]) -> dict[str, Any]:
    input_type, bindings = inputs(mapping)
    return {
        "kind": "step", "name": name, "worker": f"agent.broodling-{name.replace('_', '-')}@1",
        "instructions": f"BROODLING_NODE={name}. Execute the graph-designated mutating role.",
        "input": input_type, "output": output, "inputBindings": bindings, "writeBindings": writes,
        "timeoutMs": 250, "attempts": 1,
    }


def verifier(name: str, mapping: dict[str, str], signal: str, labels: list[str], state_target: str | None = None) -> dict[str, Any]:
    input_type, bindings = inputs(mapping)
    writes = [] if state_target is None else [{
        "value": {"node": name, "channel": "signal", "path": [signal]}, "target": [state_target],
    }]
    return {
        "kind": "verifier", "name": name, "worker": f"agent.broodling-{name.replace('_', '-')}@1",
        "instructions": f"BROODLING_NODE={name}. Execute the graph-designated read-only assurance role.",
        "input": input_type, "output": {"kind": "null"}, "inputBindings": bindings,
        "writeBindings": writes, "timeoutMs": 250, "attempts": 1,
        "signals": {signal: labels}, "diagnostic": diagnostic_type(),
    }


def succeed(name: str) -> dict[str, Any]:
    return {
        "kind": "succeed", "name": name, "output": state_type(),
        "bindings": [{"target": path, "value": {"source": "state", "path": path}} for path in STATE_PATHS],
    }


def final_flow(suffix: str) -> dict[str, Any]:
    node_name = f"final_assessment_authority_{suffix}"
    node = verifier(
        node_name,
        {"contract": "contract", "candidateGeneration": "candidateGeneration", "evidence": "evidence", "findings": "findings", "obligation": "obligation"},
        "assessment", ["accepted", "gap", "refused"],
    )
    route = choice(
        f"final_route_{suffix}",
        [
            (error_guard(node_name), fail(f"final_unusable_{suffix}", "execution_unusable")),
            (signal_guard(node_name, "assessment", "gap"), fail(f"final_gap_{suffix}", "semantic_gap")),
            (signal_guard(node_name, "assessment", "refused"), fail(f"final_refused_{suffix}", "authority_gap")),
        ],
        succeed(f"semantic_acceptance_{suffix}"),
    )
    return seq(f"final_semantic_assessment_{suffix}", [node, route])


def evidence_review_resolution() -> dict[str, Any]:
    evidence = verifier(
        "repair_evidence_check", {"contract": "contract", "candidateGeneration": "candidateGeneration"},
        "availability", ["valid", "missing"], "evidence",
    )
    review = verifier(
        "repair_review", {"contract": "contract", "candidateGeneration": "candidateGeneration", "evidence": "evidence"},
        "findings", ["clean", "found"], "findings",
    )
    resolution = verifier(
        "resolution_authority",
        {"contract": "contract", "candidateGeneration": "candidateGeneration", "evidence": "evidence", "findings": "findings", "outstanding": "obligation"},
        "resolution", ["open_d1", "resolved_d1"], "obligation",
    )
    resolution_route = choice(
        "resolution_route",
        [(error_guard("resolution_authority"), fail("resolution_unusable", "execution_unusable"))],
        verifier("round_complete", {}, "status", ["completed"]),
    )
    review_route = choice(
        "repair_review_route",
        [(error_guard("repair_review"), fail("repair_review_unusable", "execution_unusable"))],
        seq("repair_resolution", [resolution, resolution_route]),
    )
    evidence_route = choice(
        "repair_evidence_route",
        [
            (error_guard("repair_evidence_check"), fail("repair_evidence_unusable", "execution_unusable")),
            (signal_guard("repair_evidence_check", "availability", "missing"), fail("repair_evidence_missing", "required_evidence_missing")),
        ],
        seq("fresh_review_and_resolution", [review, review_route]),
    )
    return seq("renewed_evidence_and_assurance", [evidence, evidence_route])


def repair_loop() -> dict[str, Any]:
    output = {"kind": "record", "fields": {"candidateGeneration": field(enum_type("c2", "c3", "c4"))}}
    repair = step(
        "repair", {"contract": "contract", "candidateGeneration": "candidateGeneration", "directive": "obligation"}, output,
        [{"value": {"node": "repair", "channel": "out", "path": ["candidateGeneration"]}, "target": ["candidateGeneration"]}],
    )
    route = choice(
        "repair_execution_route",
        [(error_guard("repair"), fail("repair_unusable", "execution_unusable"))],
        evidence_review_resolution(),
    )
    return {
        "kind": "loop", "name": "bounded_repair", "state": state_type(),
        "body": seq("repair_round", [repair, route]),
        "until": signal_guard("resolution_authority", "resolution", "resolved_d1"),
        "maxIterations": REPAIR_BOUND, "promotedStatePaths": STATE_PATHS,
    }


def initial_assurance() -> dict[str, Any]:
    evidence = verifier(
        "initial_evidence_check", {"contract": "contract", "candidateGeneration": "candidateGeneration"},
        "availability", ["valid", "missing"], "evidence",
    )
    review = verifier(
        "initial_review", {"contract": "contract", "candidateGeneration": "candidateGeneration", "evidence": "evidence"},
        "findings", ["clean", "found"], "findings",
    )
    adjudicate = verifier(
        "adjudicate_authority",
        {"contract": "contract", "candidateGeneration": "candidateGeneration", "evidence": "evidence", "findings": "findings", "outstanding": "obligation"},
        "decision", ["none", "open_d1"], "obligation",
    )
    post_repair_route = choice(
        "post_repair_bound_route",
        [(signal_guard("resolution_authority", "resolution", "open_d1"), fail("obligations_exhausted", "obligations_exhausted"))],
        final_flow("repaired"),
    )
    adjudicate_route = choice(
        "adjudication_route",
        [
            (error_guard("adjudicate_authority"), fail("adjudication_unusable", "execution_unusable")),
            (signal_guard("adjudicate_authority", "decision", "none"), final_flow("clean")),
        ],
        seq("repair_then_final", [repair_loop(), post_repair_route]),
    )
    review_route = choice(
        "initial_review_route",
        [(error_guard("initial_review"), fail("initial_review_unusable", "execution_unusable"))],
        seq("initial_adjudication", [adjudicate, adjudicate_route]),
    )
    evidence_route = choice(
        "initial_evidence_route",
        [
            (error_guard("initial_evidence_check"), fail("initial_evidence_unusable", "execution_unusable")),
            (signal_guard("initial_evidence_check", "availability", "missing"), fail("initial_evidence_missing", "required_evidence_missing")),
        ],
        seq("initial_review_and_adjudication", [review, review_route]),
    )
    return seq("initial_evidence_and_assurance", [evidence, evidence_route])


def graph() -> dict[str, Any]:
    output = {"kind": "record", "fields": {"candidateGeneration": field(enum_type("c1"))}}
    implement = step(
        "implement", {"contract": "contract", "candidateGeneration": "candidateGeneration"}, output,
        [{"value": {"node": "implement", "channel": "out", "path": ["candidateGeneration"]}, "target": ["candidateGeneration"]}],
    )
    route = choice(
        "implementation_route",
        [(error_guard("implement"), fail("implementation_unusable", "execution_unusable"))],
        initial_assurance(),
    )
    return {
        "profile": "openengine.graph.full/v1", "initialInput": state_type(),
        "policy": {"policy": "policy.native-v2@1", "default": "deny"},
        "root": {"kind": "seq", "name": "broodling_v1_assurance", "state": state_type(), "children": [implement, route], "promotedStatePaths": []},
    }


def executable_names(node: dict[str, Any]) -> list[str]:
    if node["kind"] in {"step", "verifier"}:
        return [node["name"]]
    if node["kind"] == "seq":
        return [name for child in node["children"] for name in executable_names(child)]
    if node["kind"] == "choice":
        values = [name for branch in node["branches"] for name in executable_names(branch["node"])]
        return values + (executable_names(node["otherwise"]) if node.get("otherwise") else [])
    if node["kind"] == "loop":
        return executable_names(node["body"])
    return []


def runtime(graph_value: dict[str, Any]) -> dict[str, Any]:
    binding = {
        "kind": "agent", "model": "broodling-w3-controlled-leaf", "effort": "low", "sessionScope": "execution",
        "connections": {"fixture": ["OPENAI_API_KEY", "BROODLING_FIXTURE_SCENARIO", "BROODLING_FIXTURE_STATE"]},
    }
    return {
        "harness": "codex", "provider": "openai", "size": "small",
        "nodes": {name: binding for name in sorted(set(executable_names(graph_value["root"])))},
    }


def prepare_workspace(root: Path, name: str) -> Path:
    workspace = root / name
    workspace.mkdir(parents=True)
    subprocess.run(["git", "init", "-b", "main", str(workspace)], check=True, stdout=subprocess.PIPE)
    git(workspace, "config", "user.name", "Broodling W3 Fixture")
    git(workspace, "config", "user.email", "broodling-w3@example.invalid")
    git(workspace, "remote", "add", "origin", "https://github.com/example/broodling-w3-fixture.git")
    (workspace / "candidate.txt").write_text("C0_ADMITTED\n", encoding="utf-8")
    (workspace / "evidence").mkdir()
    (workspace / "evidence/raw.txt").write_text("REQUIRED_RAW_EVIDENCE\n", encoding="utf-8")
    git(workspace, "add", ".")
    git(workspace, "commit", "-m", "admitted fixture")
    return workspace


def driver_events(path: Path) -> list[dict[str, Any]]:
    transcript = path / "driver.jsonl"
    return [json.loads(line) for line in transcript.read_text(encoding="utf-8").splitlines()] if transcript.exists() else []


async def run_case(args: argparse.Namespace, graph_value: dict[str, Any], runtime_value: dict[str, Any], name: str, scenario: str, credential: bool = True) -> dict[str, Any]:
    workspace = prepare_workspace(args.workspace_root, name)
    leaf_state = args.run_root / "leaf" / name
    environment = {
        "PATH": f"{LEAF_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "BROODLING_FIXTURE_SCENARIO": scenario,
        "BROODLING_FIXTURE_STATE": str(leaf_state),
    }
    if credential:
        environment["OPENAI_API_KEY"] = "broodling-w3-test-only"
    request = RunRequest(
        title=f"Broodling issue 9 W3: {name}", graph=GraphSpec.from_dict(graph_value),
        initial_input={"contract": "frozen-contract-v1", "candidateGeneration": "c0", "evidence": "unchecked", "findings": "unexecuted", "obligation": "none"},
        runtime=RuntimePlan.from_dict(runtime_value), submission_key=f"broodling-issue9-{name}-v1",
    )
    async with Client(target=LocalTarget(workspace, state_dir=args.run_root / "native-state"), environment=environment) as sdk:
        run = await sdk.submit(request)
        result = await run.wait(wait_timeout=args.timeout)
        status = await run.status()
    return {"runId": run.id, "scenario": scenario, "result": jsonable(result), "status": jsonable(status), "events": driver_events(leaf_state)}


def nodes(case: dict[str, Any]) -> list[str]:
    return [event["node"] for event in case["events"]]


def sandbox_mode(event: dict[str, Any]) -> str | None:
    arguments = event["argv"]
    return arguments[arguments.index("--sandbox") + 1] if "--sandbox" in arguments else None


async def execute(args: argparse.Namespace) -> dict[str, Any]:
    args.run_root = args.run_root.resolve()
    args.workspace_root = args.workspace_root.resolve()
    for path in (args.run_root, args.workspace_root):
        if path.exists() and any(path.iterdir()):
            raise RuntimeError(f"path must be absent or empty: {path}")
        path.mkdir(parents=True, exist_ok=True)
    revision = git(args.zeroshot_source, "rev-parse", "HEAD")
    if revision != PINNED_ZEROSHOT_REVISION or git(args.zeroshot_source, "status", "--short"):
        raise RuntimeError("Zeroshot source must be the clean pinned revision")
    graph_value = graph()
    runtime_value = runtime(graph_value)
    definitions = [
        ("clean", "clean", True),
        ("repair_resolve", "repair-resolve", True),
        ("empty_review_control", "empty-review-control", True),
        ("missing_initial_evidence", "missing-initial", True),
        ("missing_repair_evidence", "missing-after-repair", True),
        ("crash", "clean;crash:initial_review", True),
        ("timeout", "clean;hang:initial_review", True),
        ("refusal", "refusal", True),
        ("malformed", "clean;malformed:initial_review", True),
        ("missing_output", "clean;missing:initial_review", True),
        ("sticky_exhaustion", "sticky-exhaust", True),
        ("sticky_omission", "sticky-omission", True),
        ("contradictory_clean", "contradictory-clean", True),
        ("forged_diagnostics", "forged-diagnostics", True),
        ("implementer_authority_claim", "clean;authority_claim:implement", True),
        ("reviewer_authority_claim", "clean;authority_claim:initial_review", True),
        ("repair_authority_claim", "repair-claim;authority_claim:repair", True),
        ("final_gap", "final-gap", True),
        ("final_insufficient_evidence", "final-insufficient-evidence", True),
        ("repair_input_canary", "repair-input-canary", True),
    ]
    cases: dict[str, Any] = {}
    for name, scenario, credential in definitions:
        cases[name] = await run_case(args, graph_value, runtime_value, name, scenario, credential)

    expected = {
        "missing_initial_evidence": "required_evidence_missing",
        "missing_repair_evidence": "required_evidence_missing",
        "crash": "execution_unusable", "timeout": "execution_unusable", "refusal": "authority_gap",
        "malformed": "execution_unusable", "missing_output": "execution_unusable",
        "sticky_exhaustion": "obligations_exhausted", "sticky_omission": "execution_unusable",
        "implementer_authority_claim": "execution_unusable", "reviewer_authority_claim": "execution_unusable",
        "repair_authority_claim": "execution_unusable", "final_gap": "semantic_gap",
        "final_insufficient_evidence": "semantic_gap",
    }
    repair_events = cases["repair_resolve"]["events"]
    canary_repair = next(event for event in cases["repair_input_canary"]["events"] if event["node"] == "repair")
    profile_events = cases["repair_resolve"]["events"]
    checks = {
        "cleanAndEmptyReviewControlsAccepted": all(cases[name]["result"]["succeeded"] for name in ("clean", "empty_review_control")),
        "repairControlAcceptedC2": cases["repair_resolve"]["result"]["succeeded"] and cases["repair_resolve"]["result"]["output"]["candidateGeneration"] == "c2",
        "c2FreshOrder": nodes(cases["repair_resolve"]) == ["implement", "initial_evidence_check", "initial_review", "adjudicate_authority", "repair", "repair_evidence_check", "repair_review", "resolution_authority", "round_complete", "final_assessment_authority_repaired"],
        "freshC2Bindings": any(event["node"] == "repair_review" and event["input"]["candidateGeneration"] == "c2" for event in repair_events),
        "allNegativeRoutes": all(not cases[name]["result"]["succeeded"] and cases[name]["result"]["failure"] == reason for name, reason in expected.items()),
        "stickyOpenSurvivesCleanReviews": all(event["input"]["outstanding"] == "open_d1" for event in cases["sticky_exhaustion"]["events"] if event["node"] == "resolution_authority"),
        "eligibleResolutionPositive": any(event["node"] == "resolution_authority" and event["input"]["outstanding"] == "open_d1" for event in repair_events),
        "contradictoryDiagnosticCannotBypass": "repair" in nodes(cases["contradictory_clean"]) and cases["contradictory_clean"]["result"]["succeeded"],
        "forgedDiagnosticsCannotRetarget": cases["forged_diagnostics"]["result"]["succeeded"] and cases["forged_diagnostics"]["result"]["output"]["contract"] == "frozen-contract-v1",
        "repairInputFirewall": canary_repair["input"] == {"contract": "frozen-contract-v1", "candidateGeneration": "c1", "directive": "open_d1"},
        "runtimeModesMatchIssue8": all(
            sandbox_mode(event) == ("workspace-write" if event["node"] in {"implement", "repair"} else "read-only")
            for event in profile_events
        ),
        "noApplicabilitySubsystem": all(term not in json.dumps(graph_value).lower() for term in ("candidatehash", "candidateseal", "manifest", "observer")),
    }
    verdict = "PASS" if all(checks.values()) else "FAIL"
    binary = resolve_binary()
    record = {
        "schema": "broodling.v1-p1.issue9-w3/v1", "recordedAt": datetime.now(UTC).isoformat(),
        "scope": {"issue": 9, "witness": "W3", "productCodeImplemented": False, "laterIssueWork": False},
        "verdicts": {"W3": verdict, "G1-V1": "NOT_PASSED"},
        "acceptanceChecks": checks,
        "build": {
            "zeroshotRevision": revision, "zeroshotTree": git(args.zeroshot_source, "rev-parse", "HEAD^{tree}"),
            "sdkDistribution": "zeroshot-rust", "sdkVersion": importlib.metadata.version("zeroshot-rust"),
            "wheel": str(args.wheel.resolve()), "wheelSha256": sha256(args.wheel),
            "sidecar": str(binary), "sidecarVersion": subprocess.run([str(binary), "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
            "sidecarSha256": sha256(binary), "controlledLeafSha256": sha256(LEAF_BIN / "codex"),
            "qualificationHarnessSha256": sha256(Path(__file__).resolve()), "python": sys.version.split()[0],
            "platform": platform.platform(), "codexCli": subprocess.run([shutil.which("codex") or "codex", "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
        },
        "profileCompatibility": {
            "issue8Commit": "7931ac9acd70b8670dfcaa48c05982897766367d",
            "sameZeroshotBuild": True, "sameWorkerKindsAndSandboxModes": True, "sessionScope": "execution",
            "requiredEffects": [], "networkAdded": False, "writableRootsAdded": False,
            "affectedW2ControlsNeedRerun": False,
        },
        "integrationBoundary": {
            "real": "Official Python SDK request encoding, sidecar preflight/admission, graph execution, typed response validation, state bindings, routing, bounded loop, runtime occurrence history, and terminal result.",
            "controlled": "Only provider leaf decisions and faults.", "broodlingValidatorOrRouter": False,
        },
        "fixture": {
            "graph": graph_value, "graphCanonicalSha256": json_sha256(graph_value),
            "runtime": runtime_value, "runtimeCanonicalSha256": json_sha256(runtime_value),
            "initialInput": {"contract": "frozen-contract-v1", "candidateGeneration": "c0", "evidence": "unchecked", "findings": "unexecuted", "obligation": "none"},
            "repairBound": REPAIR_BOUND,
            "roles": {name: ("designated_authority" if name in {"adjudicate_authority", "resolution_authority"} or name.startswith("final_assessment_authority") else "graph_local_evidence" if name.endswith("evidence_check") else "ordinary") for name in runtime_value["nodes"]},
            "candidateApplicability": "Structural graph order only: each completed implement/repair mutation writes the next generation; the following evidence/review/authority occurrences apply to that generation. No seal, digest, manifest, or observer participates.",
            "routeAuthority": "Verifier signals are the sole route-affecting processor representation. Only designated authority signal bindings update obligation; diagnostic identifiers and ordinary outputs are not bound.",
        },
        "cases": cases,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if verdict != "PASS":
        raise RuntimeError(f"W3 qualification failed: {checks}")
    return record


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--run-root", required=True, type=Path)
    result.add_argument("--workspace-root", required=True, type=Path)
    result.add_argument("--output", required=True, type=Path)
    result.add_argument("--zeroshot-source", required=True, type=Path)
    result.add_argument("--wheel", required=True, type=Path)
    result.add_argument("--timeout", type=float, default=60.0)
    return result


def main() -> int:
    record = asyncio.run(execute(parser().parse_args()))
    print(json.dumps({"output": record["schema"], "verdicts": record["verdicts"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
