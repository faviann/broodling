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
            "testGraphDeviations": "One of the eight cases submits a modified graph and declares it in its own testOnlyGraphDeviation field: hang-initial_review shortens that node's timeoutMs to 250 so the hang terminates inside the campaign. Every other case carries an explicit null and submits the exact product graph; the product graph itself is never mutated. The acceptanceChecks entry graph_deviations_are_declared enforces only that a declaration is present exactly when the submitted graphSha256 differs from the product graph's, so an undeclared future deviation fails the campaign. The wording of a declaration is descriptive and is not machine-compared against the submitted bytes; the recorded per-case graphSha256 is the authoritative record of what ran.",
            "witnessScope": "Issue #45 reduced this campaign to a minimal discriminating set of real Zeroshot runs: 38 became 8. Fail-closed topology -- every executable occurrence caught on its own authored route -- plus repair-input isolation, sticky-obligation ownership, authority ownership and diagnostic non-authority are asserted directly against the authored graph by tests/test_assurance_graph_structure.py, which runs in the default regression lane. Eight W3 demonstrations are no longer run here. The clean route and the found/adjudicated/repaired/resolved route are no longer run here either: the #18 campaign's valid and repair-renewed take the same two routes through the exact product graph AND runtime -- this campaign substitutes every runtime binding's model and connections -- while additionally driving the real deterministic evidence leaf at both occurrences, so they are the stronger witness of the same execution. Route order, the adjudicator receiving the bytes the review emitted as findingContent, and the repair handoff carrying contract/directive/directiveContent and no raw finding are asserted there. Fault location is likewise not a claim -- the authored graph catches every executable occurrence on its own route -- so process death is now witnessed at implement, the first executable node, and every response defect at initial_review, the first model node declaring both a signal and an output payload; that a control error inside the loop stops the loop rather than buying another round is round_complete's error authored in both the loop's until and post_repair_bound_route, asserted against the graph. Required-evidence removal and resolution-by-omission have stronger or equal witnesses in the same lane: the #18 campaign's missing-initial and missing-renewed against the real deterministic leaf, and missing-initial_review, whose response is now valid apart from the omitted signal. The other two were synthetic: the widened-binding canary mutated the graph to create the very binding the authored graph is asserted not to have, and its runtime premise -- that a bound state path is delivered -- is witnessed on the unmodified graph by repair-resolve, where findingContent reaches adjudicate_authority; an authority-claiming review response is refused by the runtime as a contract violation, measured, so a run of it duplicated malformed-response rejection while the guarantee itself is that guards name the node whose signal they read. Refusal and final-assessment failure routing is Broodling-owned structural behaviour under #45, and tests/test_assurance_graph_structure.py asserts both final routes branch for branch: the gap and refused guards, the semantic_gap and authority_gap sinks they reach, and acceptance reachable only as the fall-through once both are taken out. The single runtime premise those two runs added -- that a non-accepted signal label reaches the authored sink rather than falling through -- is witnessed by the retained sticky-exhaust on resolution_authority's open_d1, and at a final assessor itself by the #18 campaign's wrong-population, which signals gap and fails semantic_gap on the exact product graph. The retained hang case records reaching the intentional hang from inside the leaf, so the witness distinguishes a provider terminated inside its hang from one the runtime timed out before the execution got that far. The original canary and authority-claim evidence is retained unchanged in the preserved issue-17-controls.json. This field describes the current run only.",
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
