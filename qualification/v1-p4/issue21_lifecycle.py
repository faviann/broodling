"""Retain bounded product lifecycle mechanics; semantic roles are controlled."""

import argparse
import hashlib
import json
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
            "tests/support.py",
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
        output.write(json.dumps(record, indent=2, sort_keys=True) + "\n")
    return 0 if record["mechanicsPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
