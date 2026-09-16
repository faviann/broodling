"""Small caller-owned P2 fixture; no assurance graph or result interpretation."""

import importlib.util
import shutil
import tempfile
import unittest
from pathlib import Path

from support import AttemptTestCase, git

from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter

LEAF = Path(__file__).parent / "fixtures" / "software-change-codex"


def configured_adapter(state_dir, root):
    from broodling import CodexProfile

    home, auth = root / "submission-home", root / "submission-auth"
    home.mkdir(exist_ok=True)
    auth.mkdir(exist_ok=True)
    (auth / "auth.json").write_text("{}")
    return ZeroshotSubmitter(state_dir, codex_profile=CodexProfile(LEAF, home, auth))


class SubmissionCase(AttemptTestCase):
    def setUp(self):
        super().setUp()
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
        self.attempt = provisioned.attempt
        self.attempt_id = self.attempt.attempt_id
        self.path = provisioned.path
        # Only sockets/controller state use shm; the dedicated worktree is durable.
        self.runtime_state = Path(tempfile.mkdtemp(prefix="b14-", dir="/dev/shm"))
        self.addCleanup(shutil.rmtree, self.runtime_state, ignore_errors=True)
        self.adapter = configured_adapter(self.runtime_state, self.root)
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)

    def submit(self, **overrides):
        return self.coordinator.submit(self.attempt_id, **overrides)

    def prepare(self):
        return self.coordinator.prepare(self.attempt_id)

    def assert_single(self, run_id):
        row = self.coordinator.record(self.attempt_id)
        self.assertEqual(row.run_id, run_id)
        self.assertEqual(row.state, "correlated")
        for table in (
            "work_units",
            "contract_revisions",
            "attempts",
            "worktree_assignments",
            "attempt_submissions",
        ):
            self.assertEqual(
                self.store.connection.execute(
                    f"SELECT count(*) FROM {table}"
                ).fetchone()[0],
                1,
            )
        return row

    def restart(self):
        self.reopen()
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)


class RealSubmissionCase(SubmissionCase):
    def setUp(self):
        if importlib.util.find_spec("zeroshot") is None:
            raise unittest.SkipTest("install the release SDK/native engine")
        super().setUp()
