#!/usr/bin/env python3
"""Partial #19 current-run transport evidence; no completed custody claim."""

import asyncio
import dataclasses
import hashlib
import json
import sys
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tests"))

from final_assurance_support import LEAF, final_case, observe_released

from broodling.assurance_graph import assurance_graph, assurance_runtime
from broodling.zeroshot_sdk import assert_qualified_integration


def main():
    cases = {}
    for scenario in ("clean", "repair", "forged-identifiers", "empty-rationale"):
        print(f"Current public observation: {scenario}", flush=True)
        with final_case(scenario) as case:
            observation = asyncio.run(observe_released(case))
            cases[scenario] = {
                "observation": dataclasses.asdict(observation),
                "request": case.request,
                "events": case.events(),
                "completedCustodyRows": case.store.connection.execute(
                    "SELECT count(*) FROM final_assurance"
                ).fetchone()[0],
            }
    checks = {
        "current_clean_and_repaired_designations": all(
            cases[name]["observation"]["final_node"]
            == "final_assessment_authority_" + suffix
            for name, suffix in (("clean", "clean"), ("repair", "repaired"))
        ),
        "exact_product_request": all(
            c["request"]["graph"] == assurance_graph()
            and c["request"]["runtime"] == assurance_runtime()
            for c in cases.values()
        ),
        "final_output_contains_current_raw_material": all(
            c["observation"]["output"]["evidenceContent"]["observations"][0]["stdout"]
            == c["events"][-1]["candidate"]
            for c in cases.values()
        ),
        "fake_identifiers_are_not_exported": "FORGED_"
        not in json.dumps(cases["forged-identifiers"]["observation"]),
        "empty_rationale_is_transported_without_custody_claim": cases[
            "empty-rationale"
        ]["observation"]["output"]["finalRationale"]
        == []
        and all(c["completedCustodyRows"] == 0 for c in cases.values()),
    }
    sources = (
        Path(__file__),
        LEAF,
        ROOT / "tests/final_assurance_support.py",
        ROOT / "broodling/zeroshot_sdk.py",
        ROOT / "broodling/assurance_graph.py",
        ROOT / "broodling/schema.py",
        ROOT / "broodling/store.py",
    )
    record = {
        "schema": "broodling.v1-p3.issue19-current-observation/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": "Partial transport/rationale binding controls only; final candidate selection and complete custody remain blocked pending explicit policy decision. No #19 completion or G3 verdict.",
        "build": assert_qualified_integration(),
        "cases": cases,
        "checks": checks,
        "sources": {
            str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sources
        },
        "verdict": "PASS" if all(checks.values()) else "FAIL",
    }
    path = ROOT / "qualification/v1-p3/evidence/issue-19-current-observation.json"
    path.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    print(json.dumps({"verdict": record["verdict"], "checks": checks}))
    if not all(checks.values()):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
