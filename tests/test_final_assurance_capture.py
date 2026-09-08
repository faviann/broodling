"""Completed #19 custody against the actual admitted SDK/sidecar path."""

import asyncio
import base64
import importlib.util
import json
import shutil
import sqlite3
import unittest
from typing import ClassVar
from unittest.mock import patch

from final_assurance_support import final_case, observe_released

from broodling.assurance import FinalAssuranceCoordinator
from broodling.errors import SubmissionNotReady, UnsupportedRuntime


async def native_succeeded(case):
    """Test inspection of native outcome; never product custody recovery."""
    from zeroshot import Client, LocalTarget

    async with Client(
        target=LocalTarget(case.path, state_dir=case.run_root / "native"),
        environment=case.request["target"]["environment"],
    ) as client:
        return (await client.get_run(case.row.run_id).wait(wait_timeout=90)).succeeded


@unittest.skipUnless(
    importlib.util.find_spec("zeroshot"), "install the G1-V1 qualified SDK/sidecar"
)
class FinalAssuranceCaptureTests(unittest.TestCase):
    # The qualification entry point serializes these raw observations. Ordinary
    # regression execution writes no evidence files.
    control_records: ClassVar[dict] = {}

    def assert_single_attempt_run(self, case):
        for table in ("attempts", "attempt_submissions"):
            self.assertEqual(
                case.store.connection.execute(
                    f"SELECT count(*) FROM {table}"
                ).fetchone()[0],
                1,
            )
        self.assertEqual(
            case.coordinator.record(case.attempt_id).run_id, case.row.run_id
        )

    def report(self, case, name, *, record=None, **extra):
        self.control_records[name] = {
            "request": case.request,
            "events": case.events(),
            "record": None if record is None else record.material,
            "recordJson": None if record is None else record.record_json,
            "completedCustodyRows": case.store.connection.execute(
                "SELECT count(*) FROM final_assurance"
            ).fetchone()[0],
            "attemptRows": case.store.connection.execute(
                "SELECT count(*) FROM attempts"
            ).fetchone()[0],
            "submissionRows": case.store.connection.execute(
                "SELECT count(*) FROM attempt_submissions"
            ).fetchone()[0],
            **extra,
        }

    def test_clean_repaired_and_forged_current_custody(self):
        for scenario in ("clean", "repair", "forged-identifiers"):
            with (
                self.subTest(scenario=scenario),
                final_case(scenario, final_materials=True) as case,
            ):
                coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
                record = asyncio.run(
                    observe_released(case, coordinator.capture(case.attempt_id))
                )
                material = record.material
                final = case.events()[-1]
                repaired = scenario != "clean"
                self.assertEqual(record.attempt_id, case.attempt_id)
                self.assertEqual(record.zeroshot_run_id, case.row.run_id)
                self.assertEqual(material["attemptId"], case.attempt_id)
                self.assertEqual(material["runId"], case.row.run_id)
                self.assertEqual(
                    material["contractRevisionId"], case.revision.contract_revision_id
                )
                self.assertEqual(
                    material["finalOccurrence"]["node"],
                    "final_assessment_authority_"
                    + ("repaired" if repaired else "clean"),
                )
                self.assertEqual(
                    material["candidateGeneration"]["mutationNode"],
                    "repair" if repaired else "implement",
                )
                self.assertTrue(material["finalOccurrence"]["execution"])
                self.assertTrue(material["candidateGeneration"]["mutationExecution"])
                self.assertEqual(
                    material["evidenceContent"], final["input"]["evidenceContent"]
                )
                self.assertEqual(
                    material["finalRationale"],
                    final["response"]["output"]["finalRationale"],
                )
                self.assertEqual(
                    {entry["criterionId"] for entry in material["finalRationale"]},
                    {"c1"},
                )
                self.assertEqual(
                    material["assuranceContext"]["obligation"],
                    "resolved_d1" if repaired else "none",
                )
                self.assertNotIn("FORGED_", record.record_json)
                self.assertNotIn("RAW_REJECTED_FINDING_CANARY", record.record_json)
                selected = {
                    (entry["path"], entry["state"]): entry
                    for entry in material["selectedMaterial"]
                }
                expected = {
                    ("candidate.json", "final_candidate"): final["candidate"].encode(),
                    (
                        "candidate.json",
                        "comparison_base",
                    ): b'{"generationMaterial":"B1"}\n',
                    ("source.bin", "final_candidate"): b"final source\x00\xff\xfe\n",
                    ("source.bin", "comparison_base"): None,
                    ("removed.bin", "final_candidate"): None,
                    ("removed.bin", "comparison_base"): b"B1 deleted\x00\xff\n",
                    (
                        "unchanged.txt",
                        "final_candidate",
                    ): b"unchanged governing source\r\n",
                    (
                        "unchanged.txt",
                        "comparison_base",
                    ): b"unchanged governing source\r\n",
                }
                self.assertEqual(set(selected), set(expected))
                for key, content in expected.items():
                    entry = selected[key]
                    if content is None:
                        self.assertEqual(entry["kind"], "absent")
                        self.assertIsNone(entry["contentBase64"])
                        self.assertIsNone(entry["mode"])
                    else:
                        self.assertEqual(entry["kind"], "file")
                        self.assertEqual(
                            base64.b64decode(entry["contentBase64"]), content
                        )
                self.assert_single_attempt_run(case)
                self.assertTrue(case.path.is_dir())
                self.report(case, scenario, record=record)

    def test_declared_revision_is_new_and_old_contract_is_unchanged(self):
        with final_case(final_materials=True) as case:
            old = case.fixture.revision
            self.assertNotEqual(
                old.contract_revision_id, case.revision.contract_revision_id
            )
            self.assertNotIn("finalAssuranceMaterials", json.loads(old.canonical_bytes))
            self.assertEqual(
                case.store.get_contract_revision(
                    old.contract_revision_id
                ).canonical_bytes,
                old.canonical_bytes,
            )
            self.assertEqual(
                case.attempt.contract_revision_id, case.revision.contract_revision_id
            )
            coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
            record = asyncio.run(
                observe_released(case, coordinator.capture(case.attempt_id))
            )
            self.assert_single_attempt_run(case)
            self.report(
                case,
                "new-declared-revision-fresh-attempt",
                record=record,
                oldContractCanonical=old.canonical_bytes.decode(),
            )

    def test_empty_rationale_native_success_cannot_complete_custody(self):
        with final_case("empty-rationale", final_materials=True) as case:
            coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
            with self.assertRaises(SubmissionNotReady):
                asyncio.run(
                    observe_released(case, coordinator.capture(case.attempt_id))
                )
            self.assertTrue(asyncio.run(native_succeeded(case)))
            self.assertIsNone(coordinator.record(case.attempt_id))
            self.assert_single_attempt_run(case)
            self.report(case, "empty-rationale", nativeSucceeded=True)

    def test_old_undeclared_attempt_is_refused_without_amending_its_contract(self):
        with final_case() as case:
            coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
            original = case.revision.canonical_bytes
            with self.assertRaises(SubmissionNotReady):
                asyncio.run(coordinator.capture(case.attempt_id))
            self.assertEqual(
                case.store.get_contract_revision(
                    case.attempt.contract_revision_id
                ).canonical_bytes,
                original,
            )
            self.assertIsNone(coordinator.record(case.attempt_id))
            # Complete this fixture's already-submitted run through its normal
            # public observer; no declaration or custody is retrofitted to it.
            asyncio.run(observe_released(case))
            self.assert_single_attempt_run(case)
            self.report(
                case,
                "old-undeclared-attempt-refused",
                oldContractCanonical=original.decode(),
            )

    def test_material_loss_and_unreadable_nonregular_material_refuse_custody(self):
        for scenario in (
            "missing-initial",
            "missing-renewed",
            "nonregular-material",
            "unreadable-material",
        ):
            with (
                self.subTest(scenario=scenario),
                final_case(scenario, final_materials=True) as case,
            ):
                coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
                with self.assertRaises((SubmissionNotReady, UnsupportedRuntime)):
                    asyncio.run(
                        observe_released(case, coordinator.capture(case.attempt_id))
                    )
                self.assertIsNone(coordinator.record(case.attempt_id))
                self.assert_single_attempt_run(case)
                self.assertTrue(case.path.is_dir())
                native_success = None
                if scenario in {"nonregular-material", "unreadable-material"}:
                    native_success = asyncio.run(native_succeeded(case))
                    self.assertTrue(native_success)
                self.report(case, scenario, nativeSucceeded=native_success)

    def test_loss_or_cancellation_before_commit_leaves_no_late_recovery(self):
        for boundary in ("cancellation", "observer-loss"):
            with (
                self.subTest(boundary=boundary),
                final_case(final_materials=True) as case,
            ):
                coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
                original = case.adapter.observe_current

                async def interrupt_after_native_observation(
                    *args, observe=original, interruption=boundary, **kwargs
                ):
                    await observe(*args, **kwargs)
                    if interruption == "cancellation":
                        asyncio.current_task().cancel()
                        await asyncio.sleep(0)
                    raise UnsupportedRuntime(
                        "injected observer loss before custody commit"
                    )

                with (
                    patch.object(
                        case.adapter,
                        "observe_current",
                        interrupt_after_native_observation,
                    ),
                    self.assertRaises((asyncio.CancelledError, UnsupportedRuntime)),
                ):
                    asyncio.run(
                        observe_released(case, coordinator.capture(case.attempt_id))
                    )
                self.assertIsNone(coordinator.record(case.attempt_id))
                self.assertTrue(asyncio.run(native_succeeded(case)))
                with self.assertRaisesRegex(UnsupportedRuntime, "nonterminal"):
                    asyncio.run(coordinator.capture(case.attempt_id))
                self.assertIsNone(coordinator.record(case.attempt_id))
                self.assert_single_attempt_run(case)
                self.report(
                    case,
                    boundary,
                    nativeSucceeded=True,
                    interruptionBoundary="after actual current observation, before custody commit",
                    lateObservationRefused=True,
                )

    def test_immutable_record_survives_test_only_disposable_cleanup(self):
        with final_case(final_materials=True) as case:
            coordinator = FinalAssuranceCoordinator(case.store, case.adapter)
            record = asyncio.run(
                observe_released(case, coordinator.capture(case.attempt_id))
            )
            for sql, args in (
                (
                    "UPDATE final_assurance SET record_json = '{}' WHERE attempt_id = ?",
                    (case.attempt_id,),
                ),
                (
                    "DELETE FROM final_assurance WHERE attempt_id = ?",
                    (case.attempt_id,),
                ),
                (
                    "INSERT OR REPLACE INTO final_assurance VALUES (?, ?, '{}')",
                    (case.attempt_id, case.row.run_id),
                ),
            ):
                with self.assertRaises(sqlite3.IntegrityError):
                    case.store.connection.execute(sql, args)
            self.report(case, "cleanup-reread", record=record)
            # Qualification fixture deletion only: no product retirement or P4.
            shutil.rmtree(case.path)
            shutil.rmtree(case.run_root / "native")
            reopened = case.fixture.reopen()
            coordinator = FinalAssuranceCoordinator(reopened, case.adapter)
            with patch.object(
                case.adapter,
                "observe_current",
                side_effect=AssertionError("no native recovery"),
            ):
                self.assertEqual(
                    coordinator.record(case.attempt_id).record_json, record.record_json
                )
                self.assertEqual(
                    asyncio.run(coordinator.capture(case.attempt_id)).record_json,
                    record.record_json,
                )
            self.control_records["cleanup-reread"]["rereadAfterCleanup"] = True
            self.control_records["cleanup-reread"]["cleanupScope"] = (
                "test fixture only; no product retirement"
            )
