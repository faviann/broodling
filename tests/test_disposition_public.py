"""Normal disposition and caller-loss controls on the actual admitted SDK graph."""

import asyncio
import dataclasses
import importlib.util
import json
import shutil
import signal
import sqlite3
import subprocess
import sys
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from typing import ClassVar
from unittest.mock import patch

from final_assurance_support import final_case, observe_released
from replacement_support import abandoned_case, replacement, retire

from broodling import (
    AbandonmentCoordinator,
    BroodlingStore,
    FinalAssuranceCoordinator,
    FinalAssuranceMaterial,
)
from broodling.disposition import WorkUnitDispositionCoordinator
from broodling.errors import BroodlingError, StaleAttempt
from broodling.provisioning import AttemptProvisioner
from broodling.replacement import RetryCoordinator


@unittest.skipUnless(importlib.util.find_spec("zeroshot"), "install pinned SDK")
class PublicDispositionTests(unittest.TestCase):
    control_records: ClassVar[dict] = {}

    def retain(self, case, name, **extra):
        coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
        disposition = coordinator.record(case.attempt_id)
        custody = coordinator.justification(case.attempt_id)
        abandoned = case.store.abandonment(case.attempt_id)
        self.control_records[name] = {
            "attemptId": case.attempt_id,
            "runId": case.row.run_id,
            "request": case.request,
            "events": case.events(),
            "disposition": None
            if disposition is None
            else dataclasses.asdict(disposition),
            "justification": None if custody is None else custody.material,
            "abandonment": None if abandoned is None else dataclasses.asdict(abandoned),
            "controlledProvider": True,
            **extra,
        }

    def child(self, case, action, output):
        return subprocess.Popen(
            [
                sys.executable,
                str(Path(__file__).with_name("disposition_caller_child.py")),
                str(case.store.path),
                case.attempt_id,
                str(case.state),
                action,
                str(output),
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

    def finish(self, child, expected=0):
        try:
            stdout, stderr = child.communicate(timeout=100)
        except subprocess.TimeoutExpired:
            child.kill()
            stdout, stderr = child.communicate()
            self.fail(f"disposition child timed out: {stdout}\n{stderr}")
        self.assertEqual(child.returncode, expected, stdout + stderr)

    def wait_file(self, path, child):
        deadline = time.monotonic() + 30
        while not path.exists():
            if child.poll() is not None:
                self.finish(child)
                self.fail(f"caller exited before {path}")
            if time.monotonic() > deadline:
                child.kill()
                self.fail(f"caller did not reach {path}")
            time.sleep(0.01)

    def test_clean_repair_and_forged_ids_retain_strict_disposition(self):
        for scenario in ("clean", "repair", "forged-identifiers"):
            with (
                self.subTest(scenario=scenario),
                final_case(scenario, final_materials=True) as case,
            ):
                coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
                result = asyncio.run(
                    observe_released(case, coordinator.finalize(case.attempt_id))
                )
                self.assertEqual(result.outcome, "SUCCEEDED")
                self.assertEqual(result.attempt_id, case.attempt_id)
                self.assertEqual(
                    result.contract_revision_id, case.revision.contract_revision_id
                )
                self.assertEqual(result.work_unit_id, case.attempt.work_unit_id)
                self.assertEqual(
                    json.loads(case.revision.canonical_bytes)["requiredEffects"], []
                )
                custody = coordinator.justification(case.attempt_id).material
                self.assertEqual(
                    custody["candidateGeneration"]["mutationNode"],
                    "implement" if scenario == "clean" else "repair",
                )
                self.assertNotIn("FORGED_", json.dumps(custody))
                with patch.object(
                    case.adapter,
                    "observe_current",
                    side_effect=AssertionError("readback observed runtime"),
                ):
                    self.assertEqual(
                        asyncio.run(coordinator.finalize(case.attempt_id)), result
                    )
                self.retain(case, scenario)

    def test_two_process_duplicates_observe_once_and_return_identical_commit(self):
        with final_case(final_materials=True) as case:
            first_output, second_output = (
                case.run_root / "first.json",
                case.run_root / "second.json",
            )
            first = self.child(case, "duplicate", first_output)
            second = None
            try:
                self.wait_file(case.state / "status-observed", first)
                second = self.child(case, "duplicate", second_output)
                self.wait_file(case.state / "started-second.json", second)
                (case.state / "allow-observation").touch()
                self.finish(first)
                self.finish(second)
                self.assertEqual(first_output.read_text(), second_output.read_text())
                observers = (case.state / "observers.jsonl").read_text().splitlines()
                self.assertEqual(len(observers), 1)
                self.assertEqual(
                    case.store.connection.execute(
                        "SELECT count(*) FROM work_unit_dispositions"
                    ).fetchone()[0],
                    1,
                )
                self.retain(
                    case,
                    "two-process-duplicate",
                    observers=[json.loads(row) for row in observers],
                )
            finally:
                for child in (first, second):
                    if child is not None and child.poll() is None:
                        child.kill()
                        child.communicate()

    def test_sigkill_at_five_finalization_windows_never_salvages_custody(self):
        for action in (
            "after-output",
            "during-custody",
            "after-custody",
            "before-commit",
            "after-commit",
        ):
            with self.subTest(action=action), final_case(final_materials=True) as case:
                child = self.child(case, action, case.run_root / "lost-ack.json")
                self.finish(child, -signal.SIGKILL)
                coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
                before = coordinator.record(case.attempt_id)
                custody = FinalAssuranceCoordinator(case.store, case.adapter).record(
                    case.attempt_id
                )
                self.assertEqual(
                    custody is not None,
                    action in {"after-custody", "before-commit", "after-commit"},
                )
                with patch.object(
                    case.adapter,
                    "observe_current",
                    side_effect=AssertionError("restart observed runtime"),
                ):
                    if action == "after-commit":
                        self.assertIsNotNone(before)
                        self.assertEqual(
                            asyncio.run(coordinator.finalize(case.attempt_id)), before
                        )
                        self.assertIsNone(case.store.abandonment(case.attempt_id))
                    else:
                        self.assertIsNone(before)
                        with self.assertRaises(BroodlingError):
                            asyncio.run(coordinator.finalize(case.attempt_id))
                        self.assertIsNotNone(case.store.abandonment(case.attempt_id))
                        self.assertIsNone(coordinator.record(case.attempt_id))
                self.retain(
                    case,
                    action,
                    childReturnCode=child.returncode,
                    retainedP3BeforeRestart=custody is not None,
                )

    def test_waiting_duplicate_abandons_when_live_custody_owner_dies(self):
        with final_case(final_materials=True) as case:
            first = self.child(case, "waiting-owner", case.run_root / "owner.json")
            second = None
            try:
                self.wait_file(case.state / "custody-ready", first)
                (case.state / "allow-observation").touch()
                second = self.child(case, "duplicate", case.run_root / "waiter.json")
                self.wait_file(case.state / "waiting-lock", second)
                first.kill()
                self.finish(first, -signal.SIGKILL)
                self.finish(second, 1)
                coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
                self.assertIsNone(coordinator.record(case.attempt_id))
                self.assertIsNotNone(case.store.abandonment(case.attempt_id))
                self.assertIsNotNone(
                    FinalAssuranceCoordinator(case.store, case.adapter).record(
                        case.attempt_id
                    )
                )
                self.assertEqual(
                    len((case.state / "observers.jsonl").read_text().splitlines()), 1
                )
                self.retain(
                    case,
                    "waiting-duplicate-owner-died",
                    ownerReturnCode=first.returncode,
                    waiterReturnCode=second.returncode,
                    observedLockContention=True,
                )
            finally:
                for child in (first, second):
                    if child is not None and child.poll() is None:
                        child.kill()
                        child.communicate()

    def test_standalone_p3_custody_does_not_authorize_later_finalization(self):
        with final_case(final_materials=True) as case:
            custody = asyncio.run(
                observe_released(
                    case,
                    FinalAssuranceCoordinator(case.store, case.adapter).capture(
                        case.attempt_id
                    ),
                )
            )
            coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
            with (
                patch.object(
                    case.adapter,
                    "observe_current",
                    side_effect=AssertionError("salvage tried observation"),
                ),
                self.assertRaises(BroodlingError),
            ):
                asyncio.run(coordinator.finalize(case.attempt_id))
            self.assertIsNone(coordinator.record(case.attempt_id))
            self.assertIsNotNone(case.store.abandonment(case.attempt_id))
            self.assertEqual(
                FinalAssuranceCoordinator(case.store, case.adapter).record(
                    case.attempt_id
                ),
                custody,
            )
            self.retain(case, "standalone-custody-no-salvage")

    def test_disposition_is_immutable_and_late_stop_cannot_remove_completion(self):
        with final_case(final_materials=True) as case:
            coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
            result = asyncio.run(
                observe_released(case, coordinator.finalize(case.attempt_id))
            )
            for sql in (
                "DELETE FROM work_unit_dispositions",
                "UPDATE work_unit_dispositions SET outcome = 'ABANDONED'",
                "UPDATE work_unit_dispositions SET attempt_id = 'other'",
                "INSERT OR REPLACE INTO work_unit_dispositions SELECT * FROM work_unit_dispositions",
                "INSERT INTO attempt_abandonments VALUES (?, 'late SQL stop', 'now')",
            ):
                with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                    case.store.connection.execute(
                        sql, (case.attempt_id,) if "?" in sql else ()
                    )
            with patch.object(
                case.adapter,
                "stop_known",
                side_effect=AssertionError("late stop contacted runtime"),
            ):
                try:
                    asyncio.run(
                        AbandonmentCoordinator(case.store, case.adapter).stop(
                            case.attempt_id, "late stop"
                        )
                    )
                except BroodlingError:
                    pass
            self.assertIsNone(case.store.abandonment(case.attempt_id))
            self.assertEqual(coordinator.record(case.attempt_id), result)
            with self.assertRaises(StaleAttempt):
                case.store.require_current_attempt(case.attempt_id)
            with self.assertRaises(BroodlingError):
                case.store.admit_retry(
                    case.attempt_id,
                    "retry-completed-attempt",
                    workspace_root=case.fixture.workspace_root,
                    target=case.adapter.target,
                )
            self.assertIsNone(case.store.retry("retry-completed-attempt"))
            self.assertEqual(
                case.store.connection.execute(
                    "SELECT count(*) FROM attempts"
                ).fetchone()[0],
                1,
            )
            self.retain(case, "success-wins-stop")

    def test_stop_wins_between_custody_and_disposition(self):
        with final_case(final_materials=True) as case:
            coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
            original = coordinator._commit

            def stop_before_commit(*args, **kwargs):
                case.store.abandon_attempt(
                    case.attempt_id, "stop won before disposition"
                )
                return original(*args, **kwargs)

            with (
                patch.object(coordinator, "_commit", stop_before_commit),
                self.assertRaises(BroodlingError),
            ):
                asyncio.run(
                    observe_released(case, coordinator.finalize(case.attempt_id))
                )
            self.assertIsNone(coordinator.record(case.attempt_id))
            self.assertIsNotNone(case.store.abandonment(case.attempt_id))
            self.assertIsNotNone(
                FinalAssuranceCoordinator(case.store, case.adapter).record(
                    case.attempt_id
                )
            )
            self.retain(case, "stop-wins-after-custody")

    def test_full_justification_survives_runtime_and_worktree_cleanup(self):
        with final_case("repair", final_materials=True) as case:
            coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
            result = asyncio.run(
                observe_released(case, coordinator.finalize(case.attempt_id))
            )
            before = coordinator.justification(case.attempt_id)
            self.retain(case, "cleanup-durable-justification")
            shutil.rmtree(case.path)
            shutil.rmtree(case.run_root)
            with patch.object(
                case.adapter,
                "observe_current",
                side_effect=AssertionError("cleanup readback contacted runtime"),
            ):
                self.assertEqual(
                    asyncio.run(coordinator.finalize(case.attempt_id)), result
                )
                self.assertEqual(coordinator.justification(case.attempt_id), before)
            self.assertTrue(before.material["selectedMaterial"])
            self.assertTrue(before.material["evidenceContent"]["observations"])
            self.assertTrue(before.material["finalRationale"])

    def test_missing_material_and_semantic_gap_cannot_complete(self):
        for scenario, materials in (
            ("clean", False),
            ("semantic-gap", True),
            ("missing-rationale", True),
        ):
            with (
                self.subTest(scenario=scenario, materials=materials),
                final_case(scenario, final_materials=materials) as case,
            ):
                coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
                with self.assertRaises(BroodlingError):
                    asyncio.run(
                        observe_released(case, coordinator.finalize(case.attempt_id))
                    )
                self.assertIsNone(coordinator.record(case.attempt_id))
                self.assertIsNotNone(case.store.abandonment(case.attempt_id))
                self.retain(case, f"rejected-{scenario}-{materials}")

    def test_late_a1_capture_and_finalization_cannot_compete_with_a2_disposition(self):
        def candidate_only(contract):
            return dataclasses.replace(
                contract,
                final_assurance_materials=(
                    FinalAssuranceMaterial("candidate.json", True, True),
                ),
            )

        for boundary in ("observation", "custody"):
            with (
                self.subTest(boundary=boundary),
                abandoned_case("repair", contract_transform=candidate_only) as case,
            ):
                coordinator = WorkUnitDispositionCoordinator(case.store, case.adapter)
                successor = {}

                async def replace_and_complete(boundary=boundary, successor=successor):
                    await retire(case, "A1 lost before disposition")
                    fresh = replacement(case)

                    def submit_replacement():
                        with BroodlingStore.open(case.store.path) as store:
                            return RetryCoordinator(
                                store,
                                AttemptProvisioner(store, case.fixture.workspace_root),
                                fresh.adapter,
                            ).retry(case.attempt_id, "late-final-" + boundary)

                    row = await asyncio.to_thread(submit_replacement)
                    fresh_case = SimpleNamespace(**vars(case))
                    fresh_case.path = case.store.worktree_assignment(
                        row.attempt_id
                    ).path
                    fresh_case.state = fresh.state
                    fresh_case.row = row
                    fresh_case.request = json.loads(row.request_json)
                    fresh_case.release = lambda: (fresh.state / "release").touch()
                    finalizer = WorkUnitDispositionCoordinator(
                        case.store, fresh.adapter
                    )
                    result = await observe_released(
                        fresh_case, finalizer.finalize(row.attempt_id)
                    )
                    successor.update(
                        result=result, row=row, finalizer=finalizer, fresh=fresh
                    )

                if boundary == "custody":
                    original = coordinator.assurance._capture_fresh

                    async def delayed(*args, original=original, **kwargs):
                        result = await original(*args, **kwargs)
                        await replace_and_complete()
                        return result

                    target, method = coordinator.assurance, "_capture_fresh"
                else:
                    original = case.adapter.observe_current

                    async def delayed(*args, original=original, **kwargs):
                        result = await original(*args, **kwargs)
                        await replace_and_complete()
                        return result

                    target, method = case.adapter, "observe_current"
                with (
                    patch.object(target, method, delayed),
                    self.assertRaises(BroodlingError),
                ):
                    asyncio.run(
                        observe_released(case, coordinator.finalize(case.attempt_id))
                    )
                self.assertIsNone(coordinator.record(case.attempt_id))
                self.assertEqual(
                    successor["finalizer"].record(successor["row"].attempt_id),
                    successor["result"],
                )
                self.assertEqual(successor["result"].outcome, "SUCCEEDED")
                self.assertIsNone(case.store.abandonment(successor["row"].attempt_id))
                self.assertNotIn(
                    "A1_ACCEPTANCE_ONLY_22",
                    successor["finalizer"]
                    .justification(successor["row"].attempt_id)
                    .record_json,
                )
                asyncio.run(
                    AbandonmentCoordinator(case.store, case.adapter).stop(
                        case.attempt_id, "repeated A1 stop"
                    )
                )
                self.assertEqual(
                    successor["fresh"]
                    .coordinator.allocate(case.attempt_id, "late-final-" + boundary)
                    .attempt_id,
                    successor["row"].attempt_id,
                )
                self.assertEqual(
                    successor["finalizer"].record(successor["row"].attempt_id),
                    successor["result"],
                )
                self.retain(
                    case,
                    "late-a1-" + boundary,
                    successorDisposition=dataclasses.asdict(successor["result"]),
                    successorJustification=successor["finalizer"]
                    .justification(successor["row"].attempt_id)
                    .material,
                )
