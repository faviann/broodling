"""Retain bounded product lifecycle mechanics; semantic roles are controlled."""

import hashlib
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from test_abandonment_public import PublicAbandonmentTests

from broodling.profile import runtime_versions
from broodling.zeroshot_sdk import installed_integration

result = unittest.TextTestRunner(verbosity=2).run(
    unittest.defaultTestLoader.loadTestsFromTestCase(PublicAbandonmentTests)
)
sources = [
    "broodling/abandonment.py",
    "broodling/containment.py",
    "broodling/codex_bin/codex",
    "broodling/codex_profile.py",
    "broodling/assurance_graph.py",
    "broodling/assurance.py",
    "broodling/zeroshot_sdk.py",
    "broodling/schema.py",
    "broodling/provisioning.py",
    "broodling/git.py",
    "tests/test_abandonment_public.py",
    "tests/final_assurance_support.py",
    "tests/fixtures/final-assurance-bin/codex",
    "tests/abandonment_caller_child.py",
]
record = {
    "scope": "controlled product lifecycle mechanics; not actual-provider qualification or #21 completion",
    "mechanicsPassed": result.wasSuccessful() and not result.skipped,
    "testsRun": result.testsRun,
    "runtime": runtime_versions(),
    "integration": installed_integration(),
    "sourceSha256": {
        path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in sources
    },
    "cases": PublicAbandonmentTests.control_records,
}
output = ROOT / "qualification/v1-p4/evidence/issue-21-lifecycle.json"
output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
raise SystemExit(0 if record["mechanicsPassed"] else 1)
