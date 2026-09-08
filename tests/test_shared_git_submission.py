"""Unsafe shared metadata is refused before product dispatch or run allocation."""

import shutil
import tempfile
from dataclasses import replace
from pathlib import Path
from unittest.mock import patch

from support import AttemptTestCase, git, make_repository

from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.errors import UnsupportedRuntime
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter


class SharedGitSubmissionTests(AttemptTestCase):
    def setUp(self):
        super().setUp()
        self.repository = self.root / "unsafe-source"
        make_repository(self.repository)
        git(
            self.repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        provisioned = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )
        self.attempt_id = provisioned.attempt.attempt_id
        self.runtime_state = Path(
            tempfile.mkdtemp(prefix="b21-scratch-", dir="/dev/shm")
        )
        self.addCleanup(shutil.rmtree, self.runtime_state, ignore_errors=True)

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

    def test_canonical_scratch_metadata_refusal_has_no_run_or_dispatch(self):
        executable = self.root / "codex"
        executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        executable.chmod(0o755)
        home = self.root / "profile-home"
        home.mkdir()
        codex_home = self.root / "codex-home"
        codex_home.mkdir()
        (codex_home / "auth.json").write_text("{}")
        adapter = ZeroshotSubmitter(
            self.runtime_state,
            codex_profile=QualifiedCodexProfile(executable, home, codex_home),
        )
        coordinator = SubmissionCoordinator(self.store, adapter)
        with patch.object(adapter, "submit") as dispatch:
            with self.assertRaisesRegex(
                UnsupportedRuntime, "shared Git directory.*writable /tmp"
            ):
                coordinator.submit_assurance(self.attempt_id)
            dispatch.assert_not_called()
        record = coordinator.record(self.attempt_id)
        self.assertEqual(record.state, "prepared")
        self.assertIsNone(record.zeroshot_run_id)
        self.assertEqual(list(self.runtime_state.iterdir()), [])
