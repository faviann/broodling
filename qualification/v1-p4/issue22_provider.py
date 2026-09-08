"""Bounded real A2 implementer source-delivery witness; other roles controlled.

No prompt append, alternate graph, or semantic acceptance claim. The admitted
source alone instructs a candidate-side canary write observed independently.
"""

import argparse
import asyncio
import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from final_assurance_support import observe_released
from replacement_support import (
    FROZEN_SOURCE_BYTES,
    FROZEN_SOURCE_CANARY,
    SEMANTIC_CANARIES,
    abandoned_case,
    inputs,
    provider_input,
    replacement,
    retire,
    wait_replacement,
)
from support import git, move_head

from broodling.profile import runtime_versions
from broodling.zeroshot_sdk import assert_qualified_integration, installed_integration


def hashes():
    paths = [
        *sorted((ROOT / "broodling").glob("*.py")),
        ROOT / "broodling/codex_bin/codex",
    ]
    paths.extend(
        ROOT / path
        for path in (
            "tests/replacement_support.py",
            "tests/final_assurance_support.py",
            "tests/fixtures/final-assurance-bin/codex",
            "tests/support.py",
            "tests/test_abandonment_public.py",
            "qualification/v1-p4/issue22_provider.py",
        )
    )
    return {
        str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in paths
    }


def run_control():
    assert_qualified_integration()
    actual = shutil.which("codex")
    if not actual:
        raise RuntimeError("actual codex executable missing")
    with abandoned_case("repair") as case:
        asyncio.run(observe_released(case))
        old_events = case.events()
        negatives = [
            "A1_CANDIDATE_ONLY_22",
            "A1_EVIDENCE_ONLY_22",
            "A1_SESSION_ONLY_22",
        ]
        (case.path / "candidate.json").write_text(negatives[0])
        (case.path / "abandoned-evidence.txt").write_text(negatives[1])
        (case.adapter.codex_profile.isolated_codex_home / "history.jsonl").write_text(
            negatives[2]
        )
        asyncio.run(retire(case))
        move_head(case.fixture.repository, content="LIVE_HEAD_NOT_B1_22\n")
        fresh = replacement(case, actual_provider=actual)
        auth = (
            Path(os.environ.get("CODEX_HOME", str(Path.home() / ".codex")))
            / "auth.json"
        )
        shutil.copyfile(auth, fresh.codex_home / "auth.json")
        (fresh.codex_home / "auth.json").chmod(0o600)
        row = fresh.coordinator.retry(case.attempt_id, "actual-source-control")
        try:
            result = asyncio.run(wait_replacement(case, fresh, row, timeout=300))
            observed = inputs(fresh)
            request = json.loads(row.request_json)
            path = case.store.worktree_assignment(row.attempt_id).path
            marker = path / "frozen-source-visible.txt"
            marker_bytes = marker.read_bytes() if marker.exists() else b""
            frozen = request["initialInput"]["admittedInstructions"]
            serialized = json.dumps(observed)
            checks = {
                "sourceOnlyAbsentContract": FROZEN_SOURCE_CANARY
                not in case.revision.canonical_bytes.decode(),
                "sourceOnlyAbsentB1": subprocess.run(
                    [
                        "git",
                        "-C",
                        str(path),
                        "grep",
                        "-F",
                        FROZEN_SOURCE_CANARY,
                        case.attempt.b1_commit_oid,
                    ],
                    capture_output=True,
                    check=False,
                ).returncode
                == 1,
                "exactFrozenSource": frozen[0]["encoding"] == "utf-8"
                and frozen[0]["content"].encode() == FROZEN_SOURCE_BYTES,
                "actualImplementerReceivedFrozenInput": provider_input(observed[0])[
                    "admittedInstructions"
                ]
                == frozen,
                "actualProviderActedOnSourceOnlyInstruction": marker_bytes
                == (FROZEN_SOURCE_CANARY + "\n").encode(),
                "sourceLimitedToImplementer": all(
                    (
                        provider_input(event) is None
                        or "admittedInstructions" not in provider_input(event)
                    )
                    and FROZEN_SOURCE_CANARY not in event["prompt"]
                    for event in observed
                    if event["node"] != "implement"
                ),
                "allAbandonedInputsAbsent": all(
                    canary not in serialized
                    for canary in negatives
                    + list(SEMANTIC_CANARIES)
                    + ["LIVE_HEAD_NOT_B1_22"]
                ),
                "abandonedSemanticControlsActuallySeeded": all(
                    canary in json.dumps(old_events) for canary in SEMANTIC_CANARIES
                ),
                "candidateStartsAtOriginalB1": observed[0]["candidateBefore"]
                == '{"generationMaterial":"B1"}\n'
                and git(path, "rev-parse", "HEAD") == case.attempt.b1_commit_oid,
                "freshRunAndAttempt": row.run_id != case.row.run_id
                and row.attempt_id != case.attempt_id,
                "predecessorRetired": not case.path.exists(),
                "freshProviderSession": all(
                    "--ephemeral" in event["argv"] and "resume" not in event["argv"]
                    for event in observed
                ),
                "freshHomes": observed[0]["homeEntries"] == []
                and observed[0]["codexHomeEntries"] == ["auth.json"],
            }
            return {
                "checks": checks,
                "request": request,
                "providerInputs": observed,
                "actualProvider": actual,
                "actualProviderVersion": subprocess.check_output(
                    [actual, "--version"], text=True
                ).strip(),
                "actualStdout": (fresh.state / "actual.stdout.jsonl").read_text(),
                "actualStderr": (fresh.state / "actual.stderr.txt").read_text(),
                "markerBytesHex": marker_bytes.hex(),
                "runSucceededDiagnosticOnly": result.succeeded,
                "predecessorAttempt": case.attempt_id,
                "predecessorRun": case.row.run_id,
                "replacementAttempt": row.attempt_id,
                "replacementRun": row.run_id,
                "oldSemanticEvents": old_events,
            }
        finally:
            # Ensure an incomplete actual occurrence cannot outlive fixture cleanup.
            from broodling import AbandonmentCoordinator

            administrator = AbandonmentCoordinator(case.store, fresh.adapter)
            asyncio.run(administrator.stop(row.attempt_id, "qualification cleanup"))
            administrator.retire(row.attempt_id)
            (fresh.codex_home / "auth.json").unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    before = hashes()
    record = {
        "scope": "Real replacement implementer receives unchanged product input; A1 and other model roles controlled. Source delivery and isolation only, no semantic assurance or G4 claim.",
        "sourceSha256": before,
        "runtime": runtime_versions(),
        "integration": installed_integration(),
        "host": platform.platform(),
        "productCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
    }
    try:
        record.update(run_control())
    except Exception as error:  # noqa: BLE001 - retain failed qualification evidence
        record["error"] = repr(error)
    record["finalSourceSha256"] = hashes()
    record["sourceHashesUnchangedThroughout"] = before == record["finalSourceSha256"]
    record["passed"] = (
        bool(record.get("checks"))
        and all(record["checks"].values())
        and record["sourceHashesUnchangedThroughout"]
        and "error" not in record
    )
    args.output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    raise SystemExit(0 if record["passed"] else 1)
