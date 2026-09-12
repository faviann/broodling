"""Retain explicit retry mechanics on the current product and pinned SDK."""

import argparse
import hashlib
import json
import platform
import subprocess
import sys
import unittest
from pathlib import Path


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "output", type=Path, help="New evidence file; existing paths are refused."
    )
    args = parser.parse_args(argv)
    try:
        output = args.output.open("x", encoding="utf-8")
    except OSError as error:
        parser.error(f"cannot create fresh evidence output: {error}")

    # Reserve before importing fixtures or running a campaign. Exclusive create
    # also refuses symlinks and prevents an existence-check/write race.
    with output:
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
        output.write(json.dumps(record, indent=2, sort_keys=True) + "\n")
    return 0 if record["mechanicsPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
