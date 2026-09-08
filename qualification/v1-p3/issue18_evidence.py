#!/usr/bin/env python3
"""Retain #18 exact-product graph evidence controls; no qualification product imports."""

import argparse
import hashlib
import json
import sys
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tests"))

from evidence_support import LEAF, SCENARIOS, acceptance_checks, run_case

from broodling.zeroshot_sdk import assert_qualified_integration


def main(output):
    cases = {}
    for name in SCENARIOS:
        print(f"Running admitted deterministic evidence control: {name}", flush=True)
        cases[name] = run_case(name)
    checks = acceptance_checks(cases)
    record = {
        "schema": "broodling.v1-p3.issue18-evidence/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "build": assert_qualified_integration(),
        "cases": cases,
        "acceptanceChecks": checks,
        "sources": {
            str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in (
                Path(__file__),
                LEAF,
                ROOT / "tests/evidence_support.py",
                ROOT / "broodling/mechanical_evidence.py",
                ROOT / "broodling/codex_bin/codex",
                ROOT / "broodling/codex_profile.py",
                ROOT / "broodling/assurance_graph.py",
                ROOT / "broodling/contract.py",
                ROOT / "broodling/submission.py",
            )
        },
        "testOnlySubstitution": "Only semantic/mutation agents use a controlled executable. Both evidence occurrences execute the actual deterministic product leaf through the exact admitted graph/runtime/coordinator/profile. Semantic assessors evaluate bound raw content rather than scenario names.",
        "limits": "Mechanics and handoff evidence, not broad model semantic reliability; real reviewer/containment evidence is retained separately. No G3 or Work Unit disposition verdict.",
        "verdict": "PASS" if all(checks.values()) else "FAIL",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(record, sort_keys=True, indent=2) + "\n")
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
        default=ROOT / "qualification/v1-p3/evidence/issue-18-evidence.json",
    )
    main(parser.parse_args().output)
