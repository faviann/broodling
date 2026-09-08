#!/usr/bin/env python3
"""Actual SDK #19 final custody controls; no P4 disposition or retirement."""

import hashlib
import json
import sys
import unittest
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tests"))

from test_final_assurance_capture import FinalAssuranceCaptureTests

from broodling.zeroshot_sdk import assert_qualified_integration


def main():
    build = assert_qualified_integration()
    FinalAssuranceCaptureTests.control_records = {}
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(FinalAssuranceCaptureTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sources = (
        Path(__file__),
        ROOT / "tests/test_final_assurance_capture.py",
        ROOT / "tests/final_assurance_support.py",
        ROOT / "tests/fixtures/final-assurance-bin/codex",
        ROOT / "broodling/assurance.py",
        ROOT / "broodling/final_material.py",
        ROOT / "broodling/contract.py",
        ROOT / "broodling/zeroshot_sdk.py",
        ROOT / "broodling/schema.py",
    )
    record = {
        "schema": "broodling.v1-p3.issue19-final-custody/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "scope": (
            "Actual admitted current-run SDK/sidecar path with the controlled model "
            "executable and actual deterministic evidence leaf. Explicit final-material "
            "selection is frozen in a new Contract revision. Custody tests cover exact "
            "binary/source/B1 material and absence, current rationale, incomplete material, "
            "precommit interruption and durable reread. Cleanup is test fixture deletion "
            "only; no product P4 disposition, retirement or replacement."
        ),
        "build": build,
        "cases": FinalAssuranceCaptureTests.control_records,
        "testsRun": result.testsRun,
        "failures": [description for _, description in result.failures],
        "errors": [description for _, description in result.errors],
        "skipped": [(str(test), reason) for test, reason in result.skipped],
        "sources": {
            str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sources
        },
        "verdict": "PASS" if result.wasSuccessful() and not result.skipped else "FAIL",
    }
    target = ROOT / "qualification/v1-p3/evidence/issue-19-final-custody.json"
    target.write_text(json.dumps(record, sort_keys=True, indent=2) + "\n")
    print(json.dumps({"verdict": record["verdict"], "cases": sorted(record["cases"])}))
    if record["verdict"] != "PASS":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
