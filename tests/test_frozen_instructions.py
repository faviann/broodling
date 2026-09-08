"""Exact admitted source custody and role-limited graph delivery, without SDK."""

import base64
import hashlib
import json
from dataclasses import replace
from unittest.mock import patch

from submission_support import SubmissionCase
from support import work_reference

from broodling import SourceSubmission
from broodling.assurance_graph import assurance_graph
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter


class FrozenInstructionsTests(SubmissionCase):
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
        self.source_bytes = b"SOURCE_ONLY_22\r\nUnicode: \xc3\xa9\n"
        with patch("support.ISSUE_BODY", self.source_bytes):
            super().setUp()
        executable = self.root / "controlled-codex"
        executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        executable.chmod(0o755)
        home, auth = self.root / "home", self.root / "auth"
        home.mkdir()
        auth.mkdir()
        (auth / "auth.json").write_text("{}")
        self.adapter = ZeroshotSubmitter(
            self.runtime_state,
            codex_profile=QualifiedCodexProfile(executable, home, auth),
        )
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)

    def test_exact_snapshot_survives_newer_issue_snapshot_and_restart(self):
        first = self.coordinator.prepare_assurance(self.attempt_id)
        state = json.loads(first.request_json)["initialInput"]
        self.assertNotIn("SOURCE_ONLY_22", state["contract"])
        material = self.store.contract_source_material(
            self.attempt.contract_revision_id
        )
        expected = [
            {
                "sourceId": source.source_id,
                "kind": source.kind,
                "locator": source.locator,
                "mediaType": source.media_type,
                "contentSha256": hashlib.sha256(source.content).hexdigest(),
                "encoding": "utf-8",
                "content": self.source_bytes.decode(),
            }
            for source in material
        ]
        self.assertEqual(state["admittedInstructions"], expected)
        self.store.entitle_source(
            self.work_unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=self.work_unit.issue_locator,
                content=b"LIVE_ISSUE_DRIFT_22",
                media_type="text/plain",
            ),
        )
        self.reopen()
        self.addCleanup(self.store.close)
        coordinator = SubmissionCoordinator(self.store, self.adapter)
        self.assertEqual(coordinator.prepare_assurance(self.attempt_id), first)
        self.assertEqual(self.store.frozen_instructions(self.attempt_id), expected)

    def test_binary_source_is_explicit_base64_with_exact_roundtrip(self):
        binary = b"binary source\x00\xff\xfe\r\n"
        unit = self.store.resolve_work_unit(work_reference(issue=220002))
        source = self.store.entitle_source(
            unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=unit.issue_locator,
                content=binary,
                media_type="application/octet-stream",
            ),
        )
        revision = self.store.record_contract_revision(self.contract(unit, source))
        self.store.admit(revision.contract_revision_id)
        provisioned = self.provisioner().admit_and_provision(
            revision.contract_revision_id, self.repository
        )
        row = self.coordinator.prepare_assurance(provisioned.attempt.attempt_id)
        frozen = json.loads(row.request_json)["initialInput"]["admittedInstructions"]
        self.assertEqual(len(frozen), 1)
        self.assertEqual(frozen[0]["encoding"], "base64")
        self.assertEqual(base64.b64decode(frozen[0]["content"], validate=True), binary)
        self.assertEqual(frozen[0]["contentSha256"], hashlib.sha256(binary).hexdigest())

    def test_only_implementer_receives_frozen_instructions_and_no_worker_writes_them(
        self,
    ):
        leaves = []

        def visit(value):
            if isinstance(value, dict):
                if "worker" in value:
                    leaves.append(value)
                for child in value.values():
                    visit(child)
            elif isinstance(value, list):
                for child in value:
                    visit(child)

        visit(assurance_graph())
        self.assertTrue(leaves)
        recipients = []
        for leaf in leaves:
            if "admittedInstructions" in leaf["input"]["fields"]:
                recipients.append(leaf["name"])
                self.assertIn(
                    {
                        "target": ["admittedInstructions"],
                        "value": {"source": "state", "path": ["admittedInstructions"]},
                    },
                    leaf["inputBindings"],
                )
            for binding in leaf["writeBindings"]:
                self.assertNotEqual(binding["target"], ["admittedInstructions"])
        self.assertEqual(recipients, ["implement"])
