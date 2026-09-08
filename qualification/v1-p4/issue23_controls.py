"""Retain deterministic disposition/crash controls on the actual pinned SDK."""

import hashlib
import json
import platform
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from test_disposition_public import PublicDispositionTests

from broodling.profile import runtime_versions
from broodling.zeroshot_sdk import assert_qualified_integration, installed_integration


def hashes():
    paths = [*sorted((ROOT / "broodling").glob("*.py"))]
    paths += [ROOT / "broodling/codex_bin/codex", Path(__file__).resolve()]
    # Retain all test helper identities, including transitive fixture imports.
    paths += sorted((ROOT / "tests").glob("*.py"))
    paths += sorted(
        path for path in (ROOT / "tests/fixtures").rglob("*") if path.is_file()
    )
    return {
        str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in paths
    }


def main():
    output = Path(sys.argv[1])
    if output.exists():
        raise FileExistsError("choose a fresh evidence path")
    assert_qualified_integration()
    before = hashes()
    result = unittest.TextTestRunner(verbosity=2).run(
        unittest.defaultTestLoader.loadTestsFromTestCase(PublicDispositionTests)
    )
    after = hashes()
    record = {
        "scope": "Actual SDK/sidecar and product graph/launcher/disposition; model roles are controlled timing fixtures. Separate actual-provider evidence is required.",
        "mechanicsPassed": result.wasSuccessful()
        and not result.skipped
        and before == after,
        "testsRun": result.testsRun,
        "runtime": runtime_versions(),
        "integration": installed_integration(),
        "host": platform.platform(),
        "productCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
        "sourceSha256": before,
        "finalSourceSha256": after,
        "sourceHashesUnchangedThroughout": before == after,
        "cases": PublicDispositionTests.control_records,
        "expected": {
            "normalCleanAndRepaired": "one durable justified SUCCEEDED",
            "duplicateFinalizers": "one normal observation, identical committed disposition",
            "interruptedBeforeDisposition": "abandonment, never retained-custody salvage",
            "interruptedAfterDisposition": "identical durable readback without observation",
            "stopWins": "no disposition",
            "dispositionWins": "immutable success, late stop/retry cannot replace it",
            "lateA1": "cannot alter A2 authority or disposition",
            "cleanup": "complete retained justification readable without disposable state",
        },
    }
    output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    return 0 if record["mechanicsPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
