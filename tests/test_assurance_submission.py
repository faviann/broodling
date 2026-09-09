"""The product protocol enters the existing durable P2 submission boundary."""

import json
from dataclasses import replace
from unittest.mock import patch

from submission_support import REQUEST, SubmissionCase
from support import move_head

from broodling.assurance_graph import assurance_graph, assurance_runtime
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.errors import StaleAttempt, SubmissionConflict, SubmissionNotReady
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter


class AssuranceSubmissionTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        contract = super().contract(work_unit, source, **overrides)
        return replace(
            contract,
            criteria=tuple(
                replace(
                    item,
                    mechanical_evidence=MechanicalEvidence(argv=("/usr/bin/true",)),
                )
                for item in contract.criteria
            ),
        )

    def setUp(self):
        super().setUp()
        executable = self.root / "controlled-codex"
        executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        executable.chmod(0o755)
        home = self.root / "empty-home"
        home.mkdir()
        codex_home = self.root / "auth-home"
        codex_home.mkdir()
        (codex_home / "auth.json").write_text("{}")
        self.profile = QualifiedCodexProfile(executable, home, codex_home)
        self.configure_adapter()

    def configure_adapter(self):
        self.adapter = ZeroshotSubmitter(self.runtime_state, codex_profile=self.profile)
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)

    def restart(self):
        self.reopen()
        self.configure_adapter()

    def test_product_path_requires_the_qualified_provider_profile(self):
        from broodling.errors import UnsupportedRuntime

        coordinator = SubmissionCoordinator(
            self.store, ZeroshotSubmitter(self.runtime_state)
        )
        with self.assertRaises(UnsupportedRuntime):
            coordinator.prepare_assurance(self.attempt_id)
        self.assertIsNone(coordinator.record(self.attempt_id))

    def test_product_request_derives_only_frozen_contract_and_initial_state(self):
        row = self.coordinator.prepare_assurance(self.attempt_id)
        request = json.loads(row.request_json)
        revision = self.store.get_contract_revision(self.attempt.contract_revision_id)
        self.assertEqual(
            request["initialInput"]["contract"], revision.canonical_bytes.decode()
        )
        self.assertEqual(
            request["initialInput"]["comparisonBase"], self.attempt.b1_commit_oid
        )
        self.assertEqual(
            request["initialInput"]["evidenceContent"],
            {"observations": [], "error": ""},
        )
        self.assertEqual(request["initialInput"]["evidence"], "unchecked")
        self.assertEqual(request["initialInput"]["findings"], "unexecuted")
        self.assertEqual(request["initialInput"]["obligation"], "none")
        self.assertNotIn("candidateGeneration", request["initialInput"])
        self.assertEqual(request["workspace"], str(self.path))
        self.assertEqual(request["graph"], assurance_graph())
        self.assertEqual(request["runtime"], assurance_runtime())
        self.assertEqual(row.state, "prepared")
        self.assertEqual(row.submission_key, f"broodling:v1:{self.attempt_id}")

    def test_product_path_has_no_caller_protocol_or_state_override(self):
        for key, value in (
            ("graph", REQUEST["graph"]),
            ("runtime", REQUEST["runtime"]),
            ("initial_input", {"obligation": "resolved_d1"}),
        ):
            with self.subTest(key=key), self.assertRaises(TypeError):
                self.coordinator.submit_assurance(self.attempt_id, **{key: value})
        self.assertIsNone(self.coordinator.record(self.attempt_id))

    def test_a_prepared_arbitrary_request_cannot_be_adopted_as_product_protocol(self):
        previous = self.prepare()
        with self.assertRaises(SubmissionConflict):
            self.coordinator.prepare_assurance(self.attempt_id)
        self.assertEqual(self.coordinator.record(self.attempt_id), previous)

    def test_a_frozen_product_request_cannot_be_replaced_through_p2_seam(self):
        previous = self.coordinator.prepare_assurance(self.attempt_id)
        with self.assertRaises(SubmissionConflict):
            self.prepare()
        self.assertEqual(self.coordinator.record(self.attempt_id), previous)

    def test_product_submission_replays_same_request_after_owned_mutation(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("ack lost")),
            self.assertRaises(OSError),
        ):
            self.coordinator.submit_assurance(self.attempt_id)
        original = self.coordinator.record(self.attempt_id)
        move_head(self.path)
        self.restart()
        with patch.object(
            self.adapter,
            "submit",
            side_effect=SubmissionConflict(
                "source advanced", existing_run_id="existing-run"
            ),
        ) as submit:
            result = self.coordinator.submit_assurance(self.attempt_id)
        self.assert_single("existing-run")
        self.assertEqual(result.request_json, original.request_json)
        submit.assert_called_once_with(json.loads(original.request_json))

    def test_correlated_product_retry_never_submits_another_run(self):
        with patch.object(self.adapter, "submit", return_value="product-run"):
            original = self.coordinator.submit_assurance(self.attempt_id)
        move_head(self.path)
        self.restart()
        with patch.object(self.adapter, "submit") as submit:
            self.assertEqual(
                self.coordinator.submit_assurance(self.attempt_id), original
            )
            submit.assert_not_called()
        self.assert_single("product-run")

    def test_product_first_dispatch_still_requires_b1(self):
        self.coordinator.prepare_assurance(self.attempt_id)
        move_head(self.path)
        with patch.object(self.adapter, "submit") as submit:
            with self.assertRaises(SubmissionNotReady):
                self.coordinator.submit_assurance(self.attempt_id)
            submit.assert_not_called()

    def test_invalid_provider_profile_cannot_dispatch_a_prepared_request(self):
        from broodling.errors import UnsupportedRuntime

        self.coordinator.prepare_assurance(self.attempt_id)
        (self.profile.profile_home / "ambient-config").write_text("unqualified")
        with patch.object(self.adapter, "submit") as submit:
            with self.assertRaises(UnsupportedRuntime):
                self.coordinator.reconcile(self.attempt_id)
            submit.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "prepared")

    def test_provider_runtime_files_after_dispatch_do_not_block_reconciliation(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("ack lost")),
            self.assertRaises(OSError),
        ):
            self.coordinator.submit_assurance(self.attempt_id)
        # Runtime-owned files appear only after the initially isolated profile
        # was validated and dispatch was durably recorded.
        (self.profile.isolated_codex_home / "runtime-files").mkdir()
        move_head(self.path)
        self.restart()
        with patch.object(
            self.adapter,
            "submit",
            side_effect=SubmissionConflict(
                "owned drift", existing_run_id="original-run"
            ),
        ):
            self.assertEqual(
                self.coordinator.submit_assurance(self.attempt_id).run_id,
                "original-run",
            )

    def test_changed_launcher_identity_is_refused_before_dispatch(self):
        from broodling.errors import UnsupportedRuntime

        self.coordinator.prepare_assurance(self.attempt_id)
        changed = self.profile.identity() | {"launcherSha256": "changed"}
        with (
            patch.object(QualifiedCodexProfile, "identity", return_value=changed),
            self.assertRaises(UnsupportedRuntime),
        ):
            self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "prepared")

    def test_product_path_rejects_stale_attempt(self):
        # Existing P2 fixture convention: no product retirement API is added.
        self.store.connection.execute("DROP TRIGGER attempts_no_update")
        self.store.connection.execute("UPDATE attempts SET is_current = 0")
        with self.assertRaises(StaleAttempt):
            self.coordinator.prepare_assurance(self.attempt_id)
        self.assertIsNone(self.coordinator.record(self.attempt_id))
