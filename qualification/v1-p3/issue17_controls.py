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
            "testGraphDeviations": "One of the eight cases submits a modified graph and declares it in its own testOnlyGraphDeviation field: hang-initial_review shortens that node's timeoutMs to 250 so the hang terminates inside the campaign. Every other case carries an explicit null and submits the exact product graph; the product graph itself is never mutated. The acceptanceChecks entry graph_deviations_are_declared enforces only that a declaration is present exactly when the submitted graphSha256 differs from the product graph's, so an undeclared future deviation fails the campaign. The wording of a declaration is descriptive and is not machine-compared against the submitted bytes; the recorded per-case graphSha256 is the authoritative record of what ran.",
            "witnessScope": "Issue #45 reduced this campaign to a minimal discriminating set of real Zeroshot runs: 38 cases became 8, and this entrypoint no longer makes the two admitted product submissions of the clean and repair-resolve routes that earlier records show under admittedProductSubmissions -- the #18 evidence campaign's valid and repair-renewed take those two routes through the same P3 coordinator on the exact product graph and runtime, where this campaign substitutes every runtime binding's model and connections. Retained here: repeat-labels, sticky-exhaust, open-control-crash, missing-initial_review, malformed-initial_review, default-initial_review, missing-payload-initial_review, hang-initial_review. Claims decidable from Broodling's own authored GraphSpec -- fail-closed topology with every executable occurrence caught on the authored route that continues it, repair-input isolation, sticky-obligation and authority ownership, diagnostic non-authority, and final-assessment failure routing -- are asserted against the graph by tests/test_assurance_graph_structure.py in the default regression lane. The mapping from each removed run to the structural test or stronger retained runtime witness that now holds its claim is tests/README.md, section 'Minimal real-Zeroshot witnesses (issue #45)'; it is not restated here, so this field does not go stale when the witness set changes. This record describes only the run that produced it: the cases below are exactly what ran.",
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
    }
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
