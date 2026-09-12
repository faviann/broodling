#!/usr/bin/env python3
"""Run the G2 #14 regression with optional injection of the rejected B1 guard.

The injection reproduces the previous reported policy, without importing or
publishing the non-authoritative WIP branch. Exit 1 is expected in legacy mode.
"""

import argparse
import sys
import unittest
from pathlib import Path


def main(argv=None) -> int:
    ROOT = Path(__file__).resolve().parents[2]
    sys.path[:0] = [str(ROOT), str(ROOT / "tests")]
    from broodling.submission import SubmissionCoordinator
    from broodling.zeroshot_sdk import assert_qualified_integration

    parser = argparse.ArgumentParser()
    parser.add_argument("--legacy-b1-guard", action="store_true")
    args = parser.parse_args(argv)
    assert_qualified_integration()
    if args.legacy_b1_guard:
        original = SubmissionCoordinator._source

        def source(self, attempt, assignment, *, require_b1):
            return original(self, attempt, assignment, require_b1=True)

        SubmissionCoordinator._source = source
    suite = unittest.defaultTestLoader.loadTestsFromName(
        "test_submission.PublicSubmissionTests.test_acknowledgement_loss_after_worktree_mutation_uses_public_conflict"
    )
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
