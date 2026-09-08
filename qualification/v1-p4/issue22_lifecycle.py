"""Retain explicit retry mechanics on the current product and pinned SDK."""

import hashlib
import json
import platform
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from test_replacement_public import PublicReplacementTests

from broodling.profile import runtime_versions
from broodling.zeroshot_sdk import installed_integration

sources = [
    *[
        str(path.relative_to(ROOT))
        for path in sorted((ROOT / "broodling").glob("*.py"))
    ],
    "broodling/codex_bin/codex",
    "tests/replacement_support.py",
    "tests/test_frozen_instructions.py",
    "tests/test_replacement_public.py",
    "tests/replacement_crash_child.py",
    "tests/final_assurance_support.py",
    "tests/fixtures/final-assurance-bin/codex",
    "tests/support.py",
    "tests/test_abandonment_public.py",
    "tests/crash_child.py",
    "qualification/v1-p4/issue22_lifecycle.py",
]
invocation_hashes = {
    path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in sources
}
result = unittest.TextTestRunner(verbosity=2).run(
    unittest.defaultTestLoader.loadTestsFromTestCase(PublicReplacementTests)
)
final_hashes = {
    path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in sources
}
record = {
    "scope": "Actual SDK and unchanged product graph/launcher; external provider is a controlled leaf. No real-provider semantic qualification, disposition or G4 claim.",
    "mechanicsPassed": result.wasSuccessful()
    and not result.skipped
    and invocation_hashes == final_hashes,
    "testsRun": result.testsRun,
    "runtime": runtime_versions(),
    "integration": installed_integration(),
    "host": platform.platform(),
    "productCommit": subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip(),
    "sourceSha256": invocation_hashes,
    "sourceHashesUnchangedThroughout": invocation_hashes == final_hashes,
    "finalSourceSha256": final_hashes,
    "expected": {
        "sameContractAndOriginalB1": True,
        "newAttemptWorktreeSubmissionRun": True,
        "abandonedCandidateEvidenceSessionCarryover": False,
        "oldCustodyAndLateCallsAlterCurrentness": False,
        "sameRetryAfterMutationOrCallerDeathCreatesAnotherAttempt": False,
        "concurrentIdenticalRetries": "one replacement and one public run",
        "frozenSourceOnlyCanary": "exact admitted source bytes reach replacement implementer; absent from Contract and other roles",
    },
    "cases": PublicReplacementTests.control_records,
}
output = (
    Path(sys.argv[1])
    if len(sys.argv) > 1
    else ROOT / "qualification/v1-p4/evidence/issue-22-lifecycle.json"
)
output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
raise SystemExit(0 if record["mechanicsPassed"] else 1)
