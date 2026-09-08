"""Required custody and immutable admission checks around the public observer."""

import asyncio
import copy
from dataclasses import replace
from unittest.mock import AsyncMock, patch

from submission_support import SubmissionCase
from support import criterion

from broodling import (
    FinalAssuranceCoordinator,
    FinalAssuranceMaterial,
    MechanicalEvidence,
    QualifiedCodexProfile,
)
from broodling.assurance_graph import initial_state
from broodling.errors import (
    AttemptConflict,
    StaleAttempt,
    SubmissionConflict,
    SubmissionNotReady,
)
from broodling.zeroshot_sdk import CurrentRunObservation, ZeroshotSubmitter


class FinalCompletenessTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        contract = super().contract(work_unit, source, **overrides)
        return replace(
            contract,
            criteria=tuple(
                criterion(
                    name,
                    mechanical_evidence=MechanicalEvidence(
                        ("/usr/bin/true",), materials=("README.md",)
                    ),
                )
                for name in ("c1", "c2")
            ),
            final_assurance_materials=(
                FinalAssuranceMaterial("README.md", True, True),
            ),
        )

    def setUp(self):
        super().setUp()
        binary = self.root / "codex"
        binary.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        binary.chmod(0o755)
        home, auth = self.root / "empty-home", self.root / "auth-home"
        home.mkdir()
        auth.mkdir()
        (auth / "auth.json").write_text("{}")
        self.adapter = ZeroshotSubmitter(
            self.runtime_state, codex_profile=QualifiedCodexProfile(binary, home, auth)
        )
        from broodling.submission import SubmissionCoordinator

        self.coordinator = SubmissionCoordinator(self.store, self.adapter)
        with patch.object(self.adapter, "submit", return_value="correlated"):
            self.coordinator.submit_assurance(self.attempt_id)
        self.capture = FinalAssuranceCoordinator(self.store, self.adapter)
        self.output = initial_state(
            self.revision.canonical_bytes.decode(), self.attempt.b1_commit_oid
        )
        self.output.update(
            evidence="valid",
            findings="clean",
            finalRationale=[
                {"criterionId": name, "rationale": "Complete assessment"}
                for name in ("c1", "c2")
            ],
        )
        self.output["evidenceContent"]["observations"] = [
            {
                "criterionId": name,
                "population": "declared",
                "argv": ["/usr/bin/true"],
                "cwd": ".",
                "host": "actual host",
                "mode": "read-only/no-network",
                "exitCode": 0,
                "stdout": "",
                "stderr": "",
                "materials": [{"path": "README.md", "content": ""}],
            }
            for name in ("c1", "c2")
        ]

    def observed(self, output=None, run_id="correlated"):
        return CurrentRunObservation(
            run_id,
            "final_assessment_authority_clean",
            "runtime-final",
            "implement",
            "runtime-mutation",
            self.output if output is None else output,
        )

    def test_complete_multiple_criteria_and_empty_raw_text_are_retained(self):
        with patch.object(
            self.adapter, "observe_current", AsyncMock(return_value=self.observed())
        ) as observe:
            record = asyncio.run(self.capture.capture(self.attempt_id))
        self.assertEqual(len(record.material["finalRationale"]), 2)
        self.assertEqual(
            record.material["evidenceContent"], self.output["evidenceContent"]
        )
        self.assertEqual(observe.await_args.args[1], "correlated")
        with patch.object(
            self.adapter, "observe_current", side_effect=AssertionError("no recovery")
        ):
            self.assertEqual(asyncio.run(self.capture.capture(self.attempt_id)), record)

    def test_abandonment_during_observation_rejects_late_capture(self):
        from broodling import BroodlingStore

        async def late_result(*args):
            with BroodlingStore.open(self.store_path) as administrator:
                administrator.abandon_attempt(self.attempt_id, "caller interrupted")
            return self.observed()

        with (
            patch.object(self.adapter, "observe_current", side_effect=late_result),
            self.assertRaises(StaleAttempt),
        ):
            asyncio.run(self.capture.capture(self.attempt_id))
        self.assertIsNone(self.capture.record(self.attempt_id))
        self.assertIsNone(self.store.current_attempt(self.attempt.work_unit_id))

    def test_abandoned_custody_is_readable_but_cannot_be_recaptured(self):
        with patch.object(
            self.adapter, "observe_current", AsyncMock(return_value=self.observed())
        ):
            historical = asyncio.run(self.capture.capture(self.attempt_id))
        self.store.abandon_attempt(self.attempt_id, "finalization interrupted")
        with patch.object(self.adapter, "observe_current") as observe:
            with self.assertRaises(StaleAttempt):
                asyncio.run(self.capture.capture(self.attempt_id))
            observe.assert_not_called()
        self.assertEqual(self.capture.record(self.attempt_id), historical)

    def test_missing_duplicate_or_foreign_criterion_rationale_leaves_no_record(self):
        variants = [
            [],
            [self.output["finalRationale"][0]],
            [self.output["finalRationale"][0]] * 2,
            [
                {"criterionId": "c1", "rationale": "yes"},
                {"criterionId": "foreign", "rationale": "yes"},
            ],
            [
                {"criterionId": "c1", "rationale": "yes"},
                {"criterionId": "c2", "rationale": " \n"},
            ],
        ]
        for rationale in variants:
            output = copy.deepcopy(self.output)
            output["finalRationale"] = rationale
            with (
                self.subTest(rationale=rationale),
                patch.object(
                    self.adapter,
                    "observe_current",
                    AsyncMock(return_value=self.observed(output)),
                ),
                self.assertRaises(SubmissionNotReady),
            ):
                asyncio.run(self.capture.capture(self.attempt_id))
            self.assertIsNone(self.capture.record(self.attempt_id))

    def test_required_raw_material_context_and_open_obligation_cannot_be_lost(self):
        for missing in ("stdout", "host", "materials", "criterion", "open"):
            output = copy.deepcopy(self.output)
            observation = output["evidenceContent"]["observations"][0]
            if missing == "criterion":
                output["evidenceContent"]["observations"].pop()
            elif missing == "open":
                output["obligation"] = "open_d1"
            elif missing == "materials":
                observation["materials"][0].pop("content")
            else:
                observation.pop(missing)
            with (
                self.subTest(missing=missing),
                patch.object(
                    self.adapter,
                    "observe_current",
                    AsyncMock(return_value=self.observed(output)),
                ),
                self.assertRaises(SubmissionNotReady),
            ):
                asyncio.run(self.capture.capture(self.attempt_id))
            self.assertIsNone(self.capture.record(self.attempt_id))

    def test_foreign_run_and_nonproduct_initial_state_cannot_acquire_custody(self):
        with (
            patch.object(
                self.adapter,
                "observe_current",
                AsyncMock(return_value=self.observed(run_id="foreign")),
            ),
            self.assertRaises(SubmissionConflict),
        ):
            asyncio.run(self.capture.capture(self.attempt_id))
        self.assertIsNone(self.capture.record(self.attempt_id))
        # Test-only corruption of immutable request, not a product mutation seam.
        import json

        self.store.connection.execute("DROP TRIGGER attempt_submissions_stable")
        row = self.coordinator.record(self.attempt_id)
        request = json.loads(row.request_json)
        request["initialInput"]["obligation"] = "resolved_d1"
        self.store.connection.execute(
            "UPDATE attempt_submissions SET request_json = ?", (json.dumps(request),)
        )
        with (
            patch.object(self.adapter, "observe_current", AsyncMock()) as observe,
            self.assertRaises(SubmissionConflict),
        ):
            asyncio.run(self.capture.capture(self.attempt_id))
        observe.assert_not_awaited()

    def test_new_declaration_cannot_rebind_an_existing_current_attempt(self):
        revised = replace(
            self.revision.contract,
            final_assurance_materials=(FinalAssuranceMaterial("new.txt"),),
        )
        revision = self.store.record_contract_revision(revised)
        self.store.admit(revision.contract_revision_id)
        with self.assertRaises(AttemptConflict):
            self.provisioner().admit_and_provision(
                revision.contract_revision_id, self.repository
            )
        self.assertEqual(self.store.get_attempt(self.attempt_id), self.attempt)
        self.assertEqual(
            self.store.get_contract_revision(
                self.revision.contract_revision_id
            ).canonical_bytes,
            self.revision.canonical_bytes,
        )
