"""Reproduce ambient smudge carryover through the public retry boundary.

This is an unsupported-profile counterexample, not positive qualification.
Only external adapter.submit is mocked; no SDK run or provider execution occurs.
"""

import hashlib
import json
import platform
import subprocess
import sys
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from retry_test_support import RetryCase
from support import git

from broodling import AbandonmentCoordinator
from broodling.profile import runtime_versions
from broodling.zeroshot_sdk import installed_integration

CANARY = "ABANDONED_SMUDGE_CANARY"
SMUDGE = "cat; printf 'ABANDONED_SMUDGE_CANARY\\n'"
CLEAN = "sed '/ABANDONED_SMUDGE_CANARY/d'"
SYNTHETIC_RUN = "controlled-checkout-counterexample-run"


def hashes():
    paths = [
        *sorted((ROOT / "broodling").glob("*.py")),
        ROOT / "broodling/codex_bin/codex",
        ROOT / "tests/retry_test_support.py",
        ROOT / "tests/submission_support.py",
        ROOT / "tests/support.py",
        Path(__file__).resolve(),
    ]
    return {
        str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in paths
    }


def main():
    before = hashes()
    case = RetryCase(methodName="runTest")
    record = {
        "scope": "Unsupported checkout-context counterexample; not positive qualification, real-provider evidence, disposition or a G4 claim.",
        "controlledBoundary": "Only external ZeroshotSubmitter.submit is mocked, returning a synthetic run ID; QualifiedCodexProfile uses the existing controlled version-reporting executable fixture. No SDK run or provider session is executed.",
        "runtime": runtime_versions(),
        "integration": installed_integration(),
        "host": platform.platform(),
        "gitVersion": subprocess.check_output(["git", "--version"], text=True).strip(),
        "productCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
        "sourceSha256": before,
        "injectedAfterA1Retirement": {
            "infoAttributes": "README.md filter=reviewcarry\n",
            "filter.reviewcarry.smudge": SMUDGE,
            "filter.reviewcarry.clean": CLEAN,
        },
        "expected": {
            "replacementBytesEqualOriginalB1Blob": True,
            "abandonedCanaryInReplacement": False,
            "dispatchAllowedWithChangedB1Bytes": False,
        },
    }
    fixture_paths = []
    try:
        case.setUp()
        fixture_paths = [
            case.root,
            case.workspace_root,
            case.repository.parent,
            case.runtime_state,
        ]
        old_retirement = AbandonmentCoordinator(case.store, case.old_adapter).record(
            case.attempt_id
        )
        assert old_retirement.retired_at is not None and not case.path.exists()
        original = subprocess.check_output(
            [
                "git",
                "--no-replace-objects",
                "-C",
                str(case.repository),
                "show",
                f"{case.attempt.b1_commit_oid}:README.md",
            ]
        )
        info = Path(case.attempt.b1_repository) / "info" / "attributes"
        info.write_text("README.md filter=reviewcarry\n")
        git(case.repository, "config", "filter.reviewcarry.smudge", SMUDGE)
        git(case.repository, "config", "filter.reviewcarry.clean", CLEAN)
        observed_at_dispatch = {}

        def external_submit(_request):
            attempt = case.store.current_attempt(case.attempt.work_unit_id)
            candidate = case.store.worktree_assignment(attempt.attempt_id).path
            observed_at_dispatch.update(
                {
                    "readmeUtf8": (candidate / "README.md").read_text(),
                    "gitStatusPorcelain": git(
                        candidate, "status", "--porcelain=v1", "--untracked-files=all"
                    ),
                }
            )
            return SYNTHETIC_RUN

        with patch.object(
            case.new_adapter, "submit", side_effect=external_submit
        ) as dispatched:
            result = case.retry_coordinator().retry(
                case.attempt_id, "checkout-counterexample-retry"
            )
        replacement = case.store.get_attempt(result.attempt_id)
        candidate = case.store.worktree_assignment(result.attempt_id).path
        actual = (candidate / "README.md").read_bytes()
        record["actual"] = {
            "a1": case.attempt_id,
            "a1RetiredBeforeInjection": old_retirement.retired_at,
            "a2": replacement.attempt_id,
            "b1Commit": replacement.b1_commit_oid,
            "originalB1BlobUtf8": original.decode(),
            "originalB1BlobSha256": hashlib.sha256(original).hexdigest(),
            "replacementReadmeUtf8": actual.decode(),
            "replacementReadmeSha256": hashlib.sha256(actual).hexdigest(),
            "replacementBytesEqualOriginalB1Blob": actual == original,
            "abandonedCanaryInReplacement": CANARY.encode() in actual,
            "gitStatusPorcelain": git(
                candidate, "status", "--porcelain=v1", "--untracked-files=all"
            ),
            "externalAdapterSubmitCallCount": dispatched.call_count,
            "observedAtMockedDispatch": observed_at_dispatch,
            "submissionState": result.state,
            "syntheticRunId": result.run_id,
            "a2Current": replacement.is_current,
            "profileIdentity": case.new_adapter.target["codexProfile"],
        }
        record["counterexampleReproduced"] = (
            actual != original
            and CANARY.encode() in actual
            and CANARY.encode() not in original
            and record["actual"]["gitStatusPorcelain"] == ""
            and CANARY in observed_at_dispatch["readmeUtf8"]
            and dispatched.call_count == 1
            and result.state == "correlated"
            and result.run_id == SYNTHETIC_RUN
            and replacement.is_current
        )
    finally:
        case.doCleanups()
    record["isolatedFixtureCleanupComplete"] = all(
        not path.exists() for path in fixture_paths
    )
    record["finalSourceSha256"] = hashes()
    record["sourceHashesUnchangedThroughout"] = before == record["finalSourceSha256"]
    output = (
        Path(sys.argv[1])
        if len(sys.argv) > 1
        else ROOT / "qualification/v1-p4/evidence/issue-22-checkout-counterexample.json"
    )
    output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    print(
        json.dumps(
            {
                key: record[key]
                for key in (
                    "counterexampleReproduced",
                    "isolatedFixtureCleanupComplete",
                    "sourceHashesUnchangedThroughout",
                )
            },
            sort_keys=True,
        )
    )
    return (
        0
        if record["counterexampleReproduced"]
        and record["isolatedFixtureCleanupComplete"]
        and record["sourceHashesUnchangedThroughout"]
        else 1
    )


if __name__ == "__main__":
    raise SystemExit(main())
