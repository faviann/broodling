"""Actual SDK replacement witnesses; external provider responses are controlled."""

import asyncio
import importlib.util
import json
import sqlite3
import subprocess
import sys
import unittest
from concurrent.futures import ThreadPoolExecutor
from dataclasses import replace
from pathlib import Path
from threading import Event
from typing import ClassVar
from unittest.mock import patch

from final_assurance_support import final_case, observe_released
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
from test_abandonment_public import paused

from broodling import AbandonmentCoordinator, BroodlingStore, FinalAssuranceCoordinator
from broodling.errors import StaleAttempt
from broodling.provisioning import AttemptProvisioner
from broodling.replacement import RetryCoordinator


@unittest.skipUnless(importlib.util.find_spec("zeroshot"), "install pinned SDK")
class PublicReplacementTests(unittest.TestCase):
    control_records: ClassVar[dict] = {}

    def retain(self, name, case, fresh, row, **extra):
        self.control_records[name] = {
            "predecessorAttempt": case.attempt_id,
            "predecessorRun": case.row.run_id,
            "replacementAttempt": row.attempt_id,
            "replacementRun": row.run_id,
            "request": json.loads(row.request_json),
            "providerInputs": inputs(fresh),
            "originalB1": case.attempt.b1_commit_oid,
            "predecessorTreeAbsent": not case.path.exists(),
            "controlledProvider": True,
            "frozenSourceOnlyCanary": {
                "canary": FROZEN_SOURCE_CANARY,
                "providerRoles": [
                    event["node"]
                    for event in inputs(fresh)
                    if FROZEN_SOURCE_CANARY in event["prompt"]
                ],
                "absentFromContract": FROZEN_SOURCE_CANARY
                not in case.revision.canonical_bytes.decode(),
            },
            **extra,
        }

    def test_original_b1_contract_and_provider_inputs_exclude_abandoned_state(self):
        with abandoned_case("repair", pause_node="repair") as case:
            asyncio.run(paused(case))
            old_inputs = json.dumps(case.events())
            self.assertIn("A1_DIRECTIVE_ONLY_22", old_inputs)
            self.assertIn("A1_FINDING_ONLY_22", old_inputs)
            self.assertIn("A1_CANDIDATE_GENERATION_ONLY_22", old_inputs)
            canaries = [
                "A1_CANDIDATE_ONLY_22",
                "A1_EVIDENCE_ONLY_22",
                "A1_SESSION_ONLY_22",
            ]
            (case.path / "candidate.json").write_text(canaries[0])
            (case.path / "abandoned-evidence.txt").write_text(canaries[1])
            old_profile = case.adapter.codex_profile
            (old_profile.isolated_codex_home / "history.jsonl").write_text(canaries[2])
            asyncio.run(retire(case))
            move_head(case.fixture.repository, content="LIVE_HEAD_NOT_B1_22\n")
            fresh = replacement(case)
            prepared = fresh.coordinator.prepare(case.attempt_id, "fresh-control")
            path = case.store.worktree_assignment(prepared.attempt_id).path
            self.assertEqual(git(path, "rev-parse", "HEAD"), case.attempt.b1_commit_oid)
            self.assertEqual(
                (path / "candidate.json").read_text(), '{"generationMaterial":"B1"}\n'
            )
            request = json.loads(prepared.request_json)
            self.assertEqual(
                request["initialInput"]["contract"],
                case.revision.canonical_bytes.decode(),
            )
            self.assertEqual(request["initialInput"]["evidence"], "unchecked")
            self.assertEqual(request["initialInput"]["findings"], "unexecuted")
            self.assertEqual(request["initialInput"]["obligation"], "none")
            self.assertEqual(request["initialInput"]["finalRationale"], [])
            self.assertEqual(
                request["initialInput"]["directiveContent"],
                {"directive": "", "correction": ""},
            )
            self.assertNotIn(
                "Correct candidate.json", json.dumps(request["initialInput"])
            )
            row = fresh.coordinator.retry(case.attempt_id, "fresh-control")
            asyncio.run(wait_replacement(case, fresh, row))
            observed = inputs(fresh)
            self.assertEqual(
                observed[0]["candidateBefore"], '{"generationMaterial":"B1"}\n'
            )
            self.assertIn(
                case.revision.contract.criteria[0].statement, observed[0]["prompt"]
            )
            self.assertNotIn(
                FROZEN_SOURCE_CANARY, case.revision.canonical_bytes.decode()
            )
            self.assertNotIn(
                FROZEN_SOURCE_CANARY, git(path, "show", "HEAD:candidate.json")
            )
            frozen = request["initialInput"]["admittedInstructions"]
            self.assertEqual(len(frozen), 1)
            self.assertEqual(frozen[0]["encoding"], "utf-8")
            self.assertEqual(frozen[0]["content"].encode(), FROZEN_SOURCE_BYTES)
            self.assertEqual(
                provider_input(observed[0])["admittedInstructions"], frozen
            )
            for event in observed:
                if event["node"] != "implement":
                    value = provider_input(event)
                    if value is not None:
                        self.assertNotIn("admittedInstructions", value)
                    self.assertNotIn(FROZEN_SOURCE_CANARY, event["prompt"])
            for canary in canaries + list(SEMANTIC_CANARIES) + ["LIVE_HEAD_NOT_B1_22"]:
                self.assertNotIn(canary, json.dumps(observed))
            self.assertNotIn("abandoned-evidence.txt", observed[0]["files"])
            self.assertEqual(observed[0]["homeEntries"], [])
            self.assertEqual(observed[0]["codexHomeEntries"], ["auth.json"])
            for event in observed:
                self.assertNotIn("resume", event["argv"])
                self.assertIn("--ephemeral", event["argv"])
            self.assertNotEqual(row.run_id, case.row.run_id)
            self.assertNotEqual(row.submission_key, case.row.submission_key)
            self.assertNotEqual(path, case.path)
            self.assertNotEqual(
                case.store.worktree_assignment(row.attempt_id).branch,
                case.store.worktree_assignment(case.attempt_id).branch,
            )
            self.assertEqual(
                fresh.coordinator.retry(case.attempt_id, "fresh-control"), row
            )
            self.retain(
                "fresh-b1-and-inputs", case, fresh, row, abandonedCanaries=canaries
            )

    def test_historical_custody_and_late_old_calls_do_not_touch_current_replacement(
        self,
    ):
        with abandoned_case("repair", final_materials=True) as case:
            capture = FinalAssuranceCoordinator(case.store, case.adapter)
            custody = asyncio.run(
                observe_released(case, capture.capture(case.attempt_id))
            )
            asyncio.run(retire(case))
            fresh = replacement(case)
            row = fresh.coordinator.retry(case.attempt_id, "after-custody")
            before = json.loads(row.request_json)["initialInput"]
            self.assertIn("A1_ACCEPTANCE_ONLY_22", json.dumps(custody.material))
            self.assertNotIn("C2_FROM_REPAIR", json.dumps(before))
            for rationale in custody.material["finalRationale"]:
                self.assertNotIn(rationale["rationale"], json.dumps(before))
            for operation in (
                lambda: case.coordinator.reconcile(case.attempt_id),
                lambda: case.fixture.provisioner().provision(case.attempt_id),
                lambda: asyncio.run(capture.capture(case.attempt_id)),
            ):
                with self.assertRaises(StaleAttempt):
                    operation()
            administrator = AbandonmentCoordinator(case.store, case.adapter)
            asyncio.run(administrator.stop(case.attempt_id, "delayed old stop"))
            administrator.retire(case.attempt_id)
            # The public submit call serializes its acknowledgement with abandonment.
            # A delayed external correlation write still cannot alter the stale row.
            with self.assertRaises(sqlite3.IntegrityError):
                case.store.connection.execute(
                    "UPDATE attempt_submissions SET state = 'correlated', zeroshot_run_id = ? WHERE attempt_id = ?",
                    ("late-a1-acknowledgement", case.attempt_id),
                )
            self.assertEqual(capture.record(case.attempt_id), custody)
            self.assertEqual(
                case.store.current_attempt(case.attempt.work_unit_id).attempt_id,
                row.attempt_id,
            )
            self.assertFalse(case.path.exists())
            asyncio.run(wait_replacement(case, fresh, row))
            for canary in SEMANTIC_CANARIES:
                self.assertNotIn(canary, json.dumps(inputs(fresh)))
            self.retain(
                "late-old-calls-and-custody",
                case,
                fresh,
                row,
                historicalCustody=custody.material,
            )

    def test_delayed_actual_a1_final_observation_cannot_capture_after_a2_is_current(
        self,
    ):
        with final_case("repair", final_materials=True) as case:
            observed, release = Event(), Event()
            native = case.adapter.observe_current

            async def delay_final(*args):
                result = await native(*args)
                observed.set()
                while not release.is_set():
                    await asyncio.sleep(0.01)
                return result

            def capture_in_caller():
                with BroodlingStore.open(case.store.path) as store:
                    coordinator = FinalAssuranceCoordinator(store, case.adapter)
                    return asyncio.run(
                        observe_released(case, coordinator.capture(case.attempt_id))
                    )

            with (
                patch.object(case.adapter, "observe_current", delay_final),
                ThreadPoolExecutor(max_workers=1) as pool,
            ):
                pending = pool.submit(capture_in_caller)
                try:
                    self.assertTrue(
                        observed.wait(85),
                        "A1 never reached its actual final observation",
                    )
                    self.assertIsNone(
                        FinalAssuranceCoordinator(case.store, case.adapter).record(
                            case.attempt_id
                        )
                    )
                    asyncio.run(retire(case))
                    fresh = replacement(case)
                    row = fresh.coordinator.retry(case.attempt_id, "delayed-final")
                    release.set()
                    with self.assertRaises(StaleAttempt):
                        pending.result(timeout=10)
                    self.assertEqual(
                        case.store.current_attempt(
                            case.attempt.work_unit_id
                        ).attempt_id,
                        row.attempt_id,
                    )
                    self.assertIsNone(
                        FinalAssuranceCoordinator(case.store, case.adapter).record(
                            case.attempt_id
                        )
                    )
                    self.assertFalse(case.path.exists())
                    asyncio.run(wait_replacement(case, fresh, row))
                    self.retain(
                        "delayed-actual-final",
                        case,
                        fresh,
                        row,
                        rejectedLateCapture=True,
                    )
                finally:
                    release.set()

    def test_actual_mutation_after_acceptance_ack_loss_reconciles_same_replacement(
        self,
    ):
        with final_case() as case:
            asyncio.run(retire(case))
            fresh = replacement(case)
            native = fresh.adapter.submit
            accepted = []

            def lose_ack(request):
                accepted.append(native(request))
                raise OSError("replacement acceptance acknowledgment lost")

            with (
                patch.object(fresh.adapter, "submit", lose_ack),
                self.assertRaises(OSError),
            ):
                fresh.coordinator.retry(case.attempt_id, "ack-loss")
            attempt_id = case.store.retry("ack-loss").attempt_id
            row = fresh.coordinator.submission.record(attempt_id)
            self.assertIsNone(row.run_id)
            # Observe only this known public run, then repeat the persisted request.
            result = asyncio.run(
                wait_replacement(case, fresh, replace(row, zeroshot_run_id=accepted[0]))
            )
            self.assertTrue(result.succeeded)
            path = case.store.worktree_assignment(attempt_id).path
            self.assertIn("C1_FROM_IMPLEMENT", (path / "candidate.json").read_text())
            recovered = fresh.coordinator.retry(case.attempt_id, "ack-loss")
            self.assertEqual(recovered.run_id, accepted[0])
            self.assertEqual(recovered.request_json, row.request_json)
            self.retain(
                "mutation-ack-loss",
                case,
                fresh,
                recovered,
                publicAcceptedRun=accepted[0],
            )

    def test_concurrent_identical_retry_submits_one_actual_run(self):
        with final_case() as case:
            asyncio.run(retire(case))
            fresh = replacement(case)

            def invoke(_):
                with BroodlingStore.open(case.store.path) as store:
                    coordinator = RetryCoordinator(
                        store,
                        AttemptProvisioner(store, case.fixture.workspace_root),
                        fresh.adapter,
                    )
                    return coordinator.retry(case.attempt_id, "concurrent")

            with ThreadPoolExecutor(max_workers=4) as pool:
                rows = list(pool.map(invoke, range(4)))
            self.assertEqual(len({row.run_id for row in rows}), 1)
            self.assertEqual(len({row.attempt_id for row in rows}), 1)
            self.assertEqual(
                case.store.connection.execute(
                    "SELECT count(*) FROM attempts"
                ).fetchone()[0],
                2,
            )
            asyncio.run(wait_replacement(case, fresh, rows[0]))
            self.assertEqual(
                sum(event["node"] == "implement" for event in inputs(fresh)), 1
            )
            self.retain("concurrent-identical", case, fresh, rows[0], callers=4)

    def test_process_death_windows_converge_on_allocated_replacement(self):
        for mode in (
            "after-allocation",
            "after-worktree-add",
            "during-key-persistence",
            "after-key-persistence",
            "after-acceptance",
        ):
            with self.subTest(mode=mode), final_case() as case:
                asyncio.run(retire(case))
                fresh = replacement(case)
                profile = fresh.adapter.codex_profile
                config = {
                    "store": str(case.store.path),
                    "stateDir": str(case.run_root / "native"),
                    "executable": str(profile.real_codex),
                    "home": str(profile.profile_home),
                    "codexHome": str(profile.isolated_codex_home),
                    "workspaceRoot": str(case.fixture.workspace_root),
                    "predecessor": case.attempt_id,
                    "retryId": mode,
                }
                config_path = fresh.root / "child.json"
                config_path.write_text(json.dumps(config))
                child = subprocess.run(
                    [
                        sys.executable,
                        str(Path(__file__).with_name("replacement_crash_child.py")),
                        str(config_path),
                        mode,
                    ],
                    capture_output=True,
                    text=True,
                    timeout=60,
                    check=False,
                )
                self.assertEqual(child.returncode, 97, child.stderr)
                lineage = case.store.retry(mode)
                self.assertIsNotNone(lineage)
                original = fresh.coordinator.submission.record(lineage.attempt_id)
                child_events = [json.loads(line) for line in child.stdout.splitlines()]
                if mode == "after-acceptance":
                    accepted = child_events[0]["publicAcceptedRun"]
                    self.assertEqual(original.state, "dispatched")
                    self.assertIsNone(original.run_id)
                    # The caller is dead before the actual graph mutates the tree.
                    result = asyncio.run(
                        wait_replacement(
                            case, fresh, replace(original, zeroshot_run_id=accepted)
                        )
                    )
                    self.assertTrue(result.succeeded)
                    candidate = (
                        case.store.worktree_assignment(lineage.attempt_id).path
                        / "candidate.json"
                    )
                    self.assertIn("C1_FROM_IMPLEMENT", candidate.read_text())
                row = fresh.coordinator.retry(case.attempt_id, mode)
                self.assertEqual(row.attempt_id, lineage.attempt_id)
                if original is not None:
                    self.assertEqual(row.request_json, original.request_json)
                if mode == "after-acceptance":
                    self.assertEqual(row.run_id, accepted)
                else:
                    asyncio.run(wait_replacement(case, fresh, row))
                self.assertEqual(
                    case.store.connection.execute(
                        "SELECT count(*) FROM attempts"
                    ).fetchone()[0],
                    2,
                )
                self.retain(
                    "crash-" + mode,
                    case,
                    fresh,
                    row,
                    childExit=97,
                    childObservations=child_events,
                )
