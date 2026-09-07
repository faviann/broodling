#!/usr/bin/env python3
"""Retain product-graph #17 controls; no G3 verdict or containment requalification."""

import argparse
import asyncio
import hashlib
import json
import platform
import sys
import tempfile
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tests"))

from assurance_support import (
    CONTRACT,
    LEAF_BIN,
    acceptance_checks,
    admitted_case,
    canonical_hash,
    controlled_runtime,
    run_controls,
)
from support import durable_test_root

from broodling.assurance_graph import assurance_graph, assurance_runtime, initial_state
from broodling.zeroshot_sdk import assert_qualified_integration


async def execute(output):
    build = assert_qualified_integration()
    run_root = Path(tempfile.mkdtemp(prefix="b17e-", dir="/dev/shm"))
    workspace_root = durable_test_root("broodling-assurance-evidence-")
    cases = await run_controls(run_root, workspace_root)
    checks = acceptance_checks(cases)
    admitted = {
        scenario: await asyncio.to_thread(admitted_case, scenario)
        for scenario in ("clean", "repair-resolve")
    }
    graph = assurance_graph()
    runtime = assurance_runtime()
    record = {
        "schema": "broodling.v1-p3.issue17-controls/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": {
            "issue": 17,
            "graphMechanicsOnly": True,
            "gateG3": "NOT_REVIEWED",
            "runtimeAccess": "public SDK submit/wait/status only",
            "controlledLeafLimit": "The fixture records sandbox arguments but does not enforce containment or establish model judgment; historical W2/W4 supplies those qualified boundaries.",
            "testRuntimeDeviation": "Product binding models and connections replaced by controlled provider model and fixture connection; same agent kinds, execution sessions, graph worker roles and sandbox selection.",
            "testGraphDeviations": "Only two hang cases shorten the selected node timeout to250ms, and the explicitly named widened-binding-canary adds a forbidden raw-finding repair input to test detector sensitivity. Other cases execute the exact product graph.",
        },
        "build": build
        | {"python": platform.python_version(), "platform": platform.platform()},
        "sources": {
            str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in (
                ROOT / "broodling/assurance_graph.py",
                ROOT / "tests/assurance_support.py",
                LEAF_BIN / "codex",
                Path(__file__),
            )
        },
        "fixture": {
            "graph": graph,
            "graphSha256": canonical_hash(graph),
            "runtime": runtime,
            "runtimeSha256": canonical_hash(runtime),
            "controlledRuntime": controlled_runtime(),
            "controlledRuntimeSha256": canonical_hash(controlled_runtime()),
            "initialInput": initial_state(CONTRACT),
            "historicalW3GraphSha256": "f3ffcfced5bab598bc818db65ed985637afa0696a4ff551ee96ed5807788cd2b",
        },
        "acceptanceChecks": checks,
        "verdict": "PASS" if all(checks.values()) else "FAIL",
        "cases": cases,
        "admittedProductSubmissions": admitted,
    }
    checks["admitted_product_clean_and_repaired"] = all(
        case["result"]["succeeded"]
        and case["samePersistedRequest"]
        and case["runId"] == case["replayedRunId"]
        and case["graphSha256"] == canonical_hash(graph)
        and case["runtimeSha256"] == canonical_hash(runtime)
        for case in admitted.values()
    )
    record["verdict"] = "PASS" if all(checks.values()) else "FAIL"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    print(
        json.dumps(
            {"verdict": record["verdict"], "checks": checks, "output": str(output)},
            indent=2,
        )
    )
    if not all(checks.values()):
        raise SystemExit(1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=ROOT / "qualification/v1-p3/evidence/issue-17-controls.json",
    )
    asyncio.run(execute(parser.parse_args().output))
