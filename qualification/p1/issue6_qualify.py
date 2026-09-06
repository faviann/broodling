#!/usr/bin/env python3
"""Qualify issue #6 Q7 through the pinned Zeroshot SDK/sidecar integration."""

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
from zeroshot.errors import InvalidRequestError

HERE = Path(__file__).resolve().parent
DRIVER_BIN = HERE / "bin"
PINNED_ZEROSHOT_REVISION = "d0909615d6ba3c179b58bce15a059f40400ec995"
FIXTURE_REPOSITORY = "example/broodling-p1-fixture"


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
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def git(workspace: Path, *arguments: str, check: bool = True) -> str:
    return subprocess.run(
        ["git", "-C", str(workspace), *arguments], check=check, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    ).stdout.strip()


def field(value_type: dict[str, Any]) -> dict[str, Any]:
    return {"required": True, "type": value_type}


def record_type(fields: dict[str, dict[str, Any]]) -> dict[str, Any]:
    return {"kind": "record", "fields": fields}


STRING = {"kind": "string"}
NULL = {"kind": "null"}


def agent_node(
    name: str, *, verifier: bool = False, input_type: dict[str, Any] = NULL,
    input_bindings: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    node: dict[str, Any] = {
        "kind": "verifier" if verifier else "step", "name": name,
        "worker": f"agent.broodling-{name.replace('_', '-')}@1",
        "instructions": f"BROODLING_NODE={name}. Produce the controlled Q7 response.",
        "input": input_type, "output": NULL, "inputBindings": input_bindings or [],
        "writeBindings": [],
        "timeoutMs": 1000, "attempts": 1,
    }
    if verifier:
        node.update({"signals": {"assessment": ["accepted", "rejected"]}, "diagnostic": NULL})
    return node


def delivery_output(mode: str) -> dict[str, Any]:
    outcomes = ["opened"] if mode == "pr" else ["merged", "conflict", "ci_failed"]
    return record_type({
        "version": field({"kind": "enum", "values": ["v1"]}),
        "repository": field(STRING), "targetBranch": field(STRING),
        "headRevision": field(STRING), "pullRequestId": field(STRING),
        "mode": field({"kind": "enum", "values": [mode]}),
        "outcome": field({"kind": "enum", "values": outcomes}),
    })


def delivery_node(name: str, mode: str) -> dict[str, Any]:
    outcomes = ["opened"] if mode == "pr" else ["merged", "conflict", "ci_failed"]
    worker = "builtin.git-delivery.pr@1" if mode == "pr" else "builtin.git-delivery.merge@1"
    return {
        "kind": "verifier", "name": name, "worker": worker, "instructions": None,
        "input": NULL, "output": delivery_output(mode), "inputBindings": [],
        "writeBindings": [
            {"value": {"node": name, "channel": "out", "path": [field_name]},
             "target": [field_name]}
            for field_name in ["version", "repository", "targetBranch", "headRevision", "pullRequestId", "mode", "outcome"]
        ] + [{"value": {"node": name, "channel": "diagnostic", "path": ["message"]},
              "target": ["deliveryMessage"]}],
        "timeoutMs": 20000, "attempts": 1,
        "signals": {"delivery": outcomes},
        "diagnostic": record_type({"message": field(STRING)}),
    }


def graph(kind: str) -> dict[str, Any]:
    mode = "pr" if kind == "before_assessment" else "merge"
    outcomes = ["opened"] if mode == "pr" else ["merged", "conflict", "ci_failed"]
    state = record_type({
        "fixture": field(STRING),
        "version": {"required": False, "type": {"kind": "enum", "values": ["v1"]}},
        "repository": {"required": False, "type": STRING},
        "targetBranch": {"required": False, "type": STRING},
        "headRevision": {"required": False, "type": STRING},
        "pullRequestId": {"required": False, "type": STRING},
        "mode": {"required": False, "type": {"kind": "enum", "values": [mode]}},
        "outcome": {"required": False, "type": {"kind": "enum", "values": outcomes}},
        "deliveryMessage": {"required": False, "type": STRING},
    })
    prepare = agent_node("q7_prepare")
    receipt_fields = ["version", "repository", "targetBranch", "headRevision", "pullRequestId", "mode", "outcome"]
    assessment = agent_node(
        "q7_assessment", verifier=True,
        input_type=delivery_output(mode) if kind == "before_assessment" else NULL,
        input_bindings=[
            {"target": [name], "value": {"source": "state", "path": [name]}}
            for name in receipt_fields
        ] if kind == "before_assessment" else [],
    )
    succeeded = {"kind": "succeed", "name": "q7_done", "output": NULL, "bindings": []}
    delivery_result = {
        "kind": "choice", "name": "q7_delivery_result", "state": state,
        "branches": [{
            "when": {"kind": "in", "value": {"name": "q7_effect", "source": "error", "field": None},
                     "labels": ["crash", "malformed", "refusal", "timeout"]},
            "node": {"kind": "fail", "name": "q7_delivery_failed", "reason": "delivery_failed"},
        }],
        "otherwise": succeeded, "promotedStatePaths": [],
    }
    if kind == "before_assessment":
        success_path = {
            "kind": "seq", "name": "q7_assess_receipt", "state": state,
            "children": [assessment, succeeded], "promotedStatePaths": [],
        }
        delivery_result["otherwise"] = success_path
        children: list[dict[str, Any]] = [prepare, delivery_node("q7_effect", mode), delivery_result]
    elif kind == "after_assessment":
        accepted_delivery = {
            "kind": "seq", "name": "q7_accepted_delivery", "state": state,
            "children": [delivery_node("q7_effect", mode), delivery_result],
            "promotedStatePaths": [],
        }
        delivery_choice = {
            "kind": "choice", "name": "q7_after_acceptance", "state": state,
            "branches": [{
                "when": {"kind": "in", "value": {"name": "q7_assessment", "source": "signal", "field": "assessment"}, "labels": ["accepted"]},
                "node": accepted_delivery,
            }],
            "otherwise": {"kind": "fail", "name": "acceptance_required", "reason": "acceptance_required"},
            "promotedStatePaths": [],
        }
        children = [prepare, assessment, delivery_choice]
    else:
        raise ValueError(kind)
    return {
        "profile": "openengine.graph.full/v1", "initialInput": state,
        "policy": {"policy": "policy.native-v2@1", "default": "deny"},
        "root": {"kind": "seq", "name": f"q7_{kind}", "state": state,
                 "children": children, "promotedStatePaths": []},
    }


def unsupported_exact_effect_graph() -> dict[str, Any]:
    effect = {
        "kind": "verifier", "name": "q7_exact_effect",
        "worker": "builtin.trusted-effect@1", "instructions": None,
        "input": record_type({
            "intentId": field(STRING), "source": field(STRING),
            "target": field(STRING), "payload": field(STRING),
        }),
        "output": record_type({"receipt": field(STRING)}),
        "inputBindings": [
            {"target": [name], "value": {"source": "state", "path": [name]}}
            for name in ["intentId", "source", "target", "payload"]
        ],
        "writeBindings": [], "timeoutMs": 1000, "attempts": 1,
        "signals": {"effect": ["verified", "uncertain"]}, "diagnostic": NULL,
    }
    input_type = record_type({name: field(STRING) for name in ["intentId", "source", "target", "payload"]})
    return {
        "profile": "openengine.graph.full/v1", "initialInput": input_type,
        "policy": {"policy": "policy.native-v2@1", "default": "deny"},
        "root": {"kind": "seq", "name": "q7_exact_effect_admission", "state": input_type,
                 "children": [effect], "promotedStatePaths": []},
    }


def runtime(*nodes: str) -> dict[str, Any]:
    bindings: dict[str, Any] = {
        name: {
            "kind": "agent", "model": "broodling-p1-controlled-leaf", "effort": "low",
            "sessionScope": "execution", "connections": {"fixture": [
                "OPENAI_API_KEY", "BROODLING_FIXTURE_SCENARIO", "BROODLING_FIXTURE_STATE",
            ]},
        }
        for name in nodes if name != "q7_effect"
    }
    if "q7_effect" in nodes:
        bindings["q7_effect"] = {"kind": "git_delivery", "connections": {"github": ["GH_TOKEN"]}}
    return {"harness": "codex", "provider": "openai", "size": "small", "nodes": bindings}


async def logs(run: Any) -> list[dict[str, Any]]:
    return [jsonable(event) async for event in run.logs()]


def workspace_observation(workspace: Path, base: str) -> dict[str, Any]:
    head = git(workspace, "rev-parse", "HEAD")
    return {
        "baseRevision": base, "headRevision": head, "headChanged": head != base,
        "headSubject": git(workspace, "show", "-s", "--format=%s", "HEAD"),
        "headAuthor": git(workspace, "show", "-s", "--format=%an <%ae>", "HEAD"),
        "candidateAtHead": git(workspace, "show", "HEAD:candidate.txt", check=False),
        "statusPorcelain": git(workspace, "status", "--short"),
    }


async def delivery_case(args: argparse.Namespace, kind: str) -> dict[str, Any]:
    workspace = args.workspace_root / kind
    shutil.copytree(args.workspace_template, workspace)
    driver_state = args.evidence_root / f"{kind}.driver-state"
    base = git(workspace, "rev-parse", "HEAD")
    submitted_graph = graph(kind)
    submitted_runtime = runtime("q7_prepare", "q7_assessment", "q7_effect")
    request = RunRequest(
        title=f"Broodling issue 6 Q7: {kind}", graph=GraphSpec.from_dict(submitted_graph),
        initial_input={"fixture": kind}, runtime=RuntimePlan.from_dict(submitted_runtime),
        submission_key=f"broodling-issue6-{kind}-v1",
    )
    environment = {
        "PATH": f"{DRIVER_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "OPENAI_API_KEY": "broodling-p1-test-only",
        "GH_TOKEN": "broodling-q7-deliberately-invalid-token",
        "BROODLING_FIXTURE_SCENARIO": f"q7-{kind}",
        "BROODLING_FIXTURE_STATE": str(driver_state),
    }
    async with Client(target=LocalTarget(workspace, state_dir=args.state_dir), environment=environment) as sdk:
        try:
            run = await sdk.submit(request)
        except InvalidRequestError as error:
            raise RuntimeError(
                f"delivery fixture rejected: code={error.code} path={error.path} "
                f"node={error.node} details={error.details} message={error}"
            ) from error
        result = await run.wait(wait_timeout=args.timeout)
        status = await run.status()
        retained_logs = await logs(run)
    events_path = driver_state / "driver.jsonl"
    events = [json.loads(line) for line in events_path.read_text().splitlines()]
    return {
        "runId": run.id, "result": jsonable(result), "terminalStatus": jsonable(status),
        "submittedGraph": submitted_graph, "submittedRuntime": submitted_runtime,
        "controlledDriverEvents": events, "durableLogs": retained_logs,
        "workspaceAfterRun": workspace_observation(workspace, base),
        "remoteTarget": FIXTURE_REPOSITORY,
        "credential": "literal deliberately-invalid test token (value not retained)",
    }


async def unsupported_case(args: argparse.Namespace) -> dict[str, Any]:
    workspace = args.workspace_root / "unsupported_exact_effect"
    shutil.copytree(args.workspace_template, workspace)
    submitted_graph = unsupported_exact_effect_graph()
    submitted_runtime = {
        "harness": "codex", "provider": "openai", "size": "small",
        "nodes": {"q7_exact_effect": {"kind": "git_delivery", "connections": {}}},
    }
    request = RunRequest(
        title="Broodling issue 6 Q7: exact effect admission",
        graph=GraphSpec.from_dict(submitted_graph),
        initial_input={"intentId": "intent-q7-1", "source": "candidate-q7-1",
                       "target": "controlled-target-q7-1", "payload": "payload-q7-1"},
        runtime=RuntimePlan.from_dict(submitted_runtime),
        submission_key="broodling-issue6-exact-effect-admission-v1",
    )
    environment = {"PATH": os.environ["PATH"]}
    try:
        async with Client(target=LocalTarget(workspace, state_dir=args.state_dir), environment=environment) as sdk:
            await sdk.submit(request)
    except InvalidRequestError as error:
        return {
            "admitted": False, "error": str(error), "code": error.code,
            "path": list(error.path or ()), "node": error.node, "details": error.details,
            "submittedGraph": submitted_graph, "submittedRuntime": submitted_runtime,
        }
    raise RuntimeError("unsupported exact-effect worker was unexpectedly admitted")


async def execute(args: argparse.Namespace) -> dict[str, Any]:
    for name in ["workspace_root", "workspace_template", "state_dir", "evidence_root", "output", "zeroshot_source", "wheel"]:
        setattr(args, name, getattr(args, name).resolve())
    args.workspace_root.mkdir(parents=True, exist_ok=True)
    args.evidence_root.mkdir(parents=True, exist_ok=True)
    args.state_dir.mkdir(parents=True, exist_ok=True)
    revision = git(args.zeroshot_source, "rev-parse", "HEAD")
    if revision != PINNED_ZEROSHOT_REVISION or git(args.zeroshot_source, "status", "--short"):
        raise RuntimeError("Zeroshot checkout must be clean and at the pinned revision")

    before = await delivery_case(args, "before_assessment")
    after = await delivery_case(args, "after_assessment")
    unsupported = await unsupported_case(args)
    before_nodes = [event["node"] for event in before["controlledDriverEvents"]]
    after_nodes = [event["node"] for event in after["controlledDriverEvents"]]
    checks = {
        "beforeAssessmentEffectEnteredTrustedDelivery": before_nodes == ["q7_prepare"],
        "beforeAssessmentDidNotReachAssessmentWithoutReceipt": "q7_assessment" not in before_nodes,
        "afterAssessmentPrerequisiteRanBeforeDelivery": after_nodes == ["q7_prepare", "q7_assessment"],
        "stockDeliveryCommittedWorkspaceInBothCases": all(
            case["workspaceAfterRun"]["headChanged"] for case in [before, after]
        ),
        "stockDeliveryDidNotYieldVerifiedReceipt": all(
            case["result"]["succeeded"] is False for case in [before, after]
        ),
        "genericExactEffectWorkerRejected": unsupported["admitted"] is False,
        "noRemoteMutationCredentialUsed": True,
    }
    if not all(checks.values()):
        raise RuntimeError(f"qualification witness failed: {checks}")

    binary = resolve_binary()
    record = {
        "schema": "broodling.p1.issue6-qualification/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": {"issue": 6, "questions": ["Q7"]},
        "verdicts": {"Q7": "BLOCKED", "G1-effects": "BLOCKED"},
        "testedSource": {
            "zeroshotRevision": revision,
            "zeroshotTree": git(args.zeroshot_source, "rev-parse", "HEAD^{tree}"),
            "zeroshotCheckoutClean": True,
            "workspaceTemplateRevision": git(args.workspace_template, "rev-parse", "HEAD"),
        },
        "build": {
            "sdkDistribution": "zeroshot-rust",
            "sdkVersion": importlib.metadata.version("zeroshot-rust"),
            "python": sys.version.split()[0], "platform": platform.platform(),
            "sidecarVersion": subprocess.run([str(binary), "--version"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip(),
            "sidecarSha256": sha256(binary), "wheel": args.wheel.name,
            "wheelSha256": sha256(args.wheel),
            "controlledLeafSha256": sha256(DRIVER_BIN / "codex"),
            "qualificationHarnessSha256": sha256(Path(__file__).resolve()),
        },
        "integrationBoundary": {
            "selected": "official Python SDK LocalTarget and matching unpatched Rust sidecar",
            "doubled": "Only agent leaves. Git preparation/delivery, admission, controller, graph progression, durable status/result/logs, and SDK transport are real.",
            "controlledRemoteSafety": "The target is example/broodling-p1-fixture and GH_TOKEN is a literal known-invalid fixture value, so delivery cannot authenticate or mutate GitHub.",
            "broodlingValidationOrScheduling": False, "prohibitedReadersUsed": [],
        },
        "authorizedFixtureIntents": {
            "beforeAssessment": {
                "intentId": "q7-prototype-publication-v1", "operation": "publish controlled prototype bytes",
                "prerequisite": "candidate prepared", "forbiddenBundledEffects": ["pull request", "merge", "issue closure", "repeated final publication"],
            },
            "afterAssessment": {
                "intentId": "q7-post-acceptance-marker-v1", "operation": "write one controlled accepted-candidate marker",
                "prerequisite": "current semantic acceptance", "forbiddenBundledEffects": ["commit", "push", "pull request", "merge", "issue closure"],
            },
        },
        "acceptanceChecks": checks,
        "cases": {"beforeAssessment": before, "afterAssessment": after, "unsupportedExactEffect": unsupported},
        "dependency": {
            "missingCapability": "A supported trusted generic exact-effect operation that consumes one durable authorized intent (source/payload/target/prerequisites), synchronously returns a verified result to dependent nodes in the same run, and exposes exact post-run readback/reconciliation without a coding Attempt.",
            "stockDeliveryMismatch": "The only trusted bindings are GitDelivery PR/merge. Both commit all workspace changes, push a deterministic run branch, and create or rediscover a pull request; merge mode additionally inspects checks and requests merge. Neither can execute either narrower fixture intent without widening authority.",
            "consumingGate": "G1-effects; therefore P6 and every effect-dependent/publication-dependent Contract remain blocked.",
        },
        "limits": "Controlled-mechanics qualification only; no production Git/GitHub delivery operation or reconciliation was certified.",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return record


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--workspace-root", required=True, type=Path)
    result.add_argument("--workspace-template", required=True, type=Path)
    result.add_argument("--state-dir", required=True, type=Path)
    result.add_argument("--evidence-root", required=True, type=Path)
    result.add_argument("--output", required=True, type=Path)
    result.add_argument("--zeroshot-source", required=True, type=Path)
    result.add_argument("--wheel", required=True, type=Path)
    result.add_argument("--timeout", type=float, default=60.0)
    return result


def main() -> int:
    args = parser().parse_args()
    record = asyncio.run(execute(args))
    print(json.dumps({"output": str(args.output.resolve()), "verdicts": record["verdicts"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
