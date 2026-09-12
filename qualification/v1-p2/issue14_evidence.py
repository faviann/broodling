#!/usr/bin/env python3
"""Record the graph-owned mutation crash witness using public submit only."""

import argparse
import hashlib
import json
import subprocess
import sys
import unittest
from pathlib import Path


def main(argv=None) -> int:
    ROOT = Path(__file__).resolve().parents[2]
    sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

    from test_submission_crashes import SubmissionCrashTests

    from broodling import git
    from broodling.profile import QUALIFIED_ZEROSHOT_BOUNDARY, runtime_versions
    from broodling.schema import SCHEMA_SHA256, SCHEMA_VERSION
    from broodling.zeroshot_sdk import assert_qualified_integration

    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    case = SubmissionCrashTests("test_accepted_graph_mutates_after_broodling_process_dies")
    record = {
        "implementationCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
        "versions": runtime_versions() | assert_qualified_integration(),
        "qualifiedBoundary": QUALIFIED_ZEROSHOT_BOUNDARY,
        "schemaVersion": SCHEMA_VERSION,
        "schemaSha256": SCHEMA_SHA256,
        "scenario": case.id(),
        "scope": "#14 implementation evidence; no G2 review or V1-P3",
    }
    original_teardown = case.tearDown


    def capture():
        row = case.coordinator.record(case.attempt_id)
        record["lineage"] = {
            "workUnitId": case.work_unit.work_unit_id,
            "contractRevisionId": case.revision.contract_revision_id,
            "attemptId": case.attempt_id,
            "b1": case.b1,
            "headAfterGraphMutation": git.head_commit(case.path),
            "submissionKey": row.submission_key,
            "requestSha256": hashlib.sha256(row.request_json.encode()).hexdigest(),
            "runId": row.run_id,
            "administrativeState": row.state,
            "rowCounts": {
                table: case.store.connection.execute(
                    f"SELECT count(*) FROM {table}"
                ).fetchone()[0]
                for table in (
                    "work_units",
                    "contract_revisions",
                    "attempts",
                    "worktree_assignments",
                    "attempt_submissions",
                )
            },
        }
        original_teardown()


    case.tearDown = capture
    result = unittest.TextTestRunner(verbosity=2).run(unittest.TestSuite([case]))
    record["passed"] = result.wasSuccessful()
    record["trace"] = [
        "persist same key/request and dispatch intent at clean B1",
        "Client.submit accepts R; child prints public R and os._exit(97) before ID persistence",
        "parent verifies HEAD still B1 and correlation absent, then releases graph mutation",
        "accepted graph step commits candidate mutation on the same dedicated branch",
        "restart replays persisted request; public conflict exposes R",
        "one immutable Attempt-to-R correlation; key and request unchanged",
    ]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
