"""Controlled administrative retry fixture; it executes no provider session."""

import asyncio
from dataclasses import replace

from submission_support import SubmissionCase

from broodling import AbandonmentCoordinator, RetryCoordinator
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.zeroshot_sdk import ZeroshotSubmitter


class RetryCase(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        contract = super().contract(work_unit, source, **overrides)
        return replace(
            contract,
            criteria=tuple(
                replace(
                    item, mechanical_evidence=MechanicalEvidence(("/usr/bin/true",))
                )
                for item in contract.criteria
            ),
        )

    def setUp(self):
        super().setUp()
        self.executable = self.root / "controlled-codex"
        self.executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        self.executable.chmod(0o755)
        self.old_adapter = self.fresh_adapter("old")
        from broodling.submission import SubmissionCoordinator

        SubmissionCoordinator(self.store, self.old_adapter).prepare_assurance(
            self.attempt_id
        )
        admin = AbandonmentCoordinator(self.store, self.old_adapter)
        asyncio.run(admin.stop(self.attempt_id, "fresh retry fixture"))
        admin.retire(self.attempt_id)
        self.new_adapter = self.fresh_adapter("new")

    def fresh_adapter(self, label):
        home, auth = self.root / (label + "-home"), self.root / (label + "-auth")
        home.mkdir()
        auth.mkdir()
        (auth / "auth.json").write_text("{}")
        return self.adapter_for(home, auth)

    def adapter_for(self, home, auth):
        return ZeroshotSubmitter(
            self.runtime_state,
            codex_profile=QualifiedCodexProfile(self.executable, home, auth),
        )

    def retry_coordinator(self, adapter=None):
        return RetryCoordinator(
            self.store, self.provisioner(), adapter or self.new_adapter
        )
