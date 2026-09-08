"""Owned retirement is downstream of permanent ineligibility and actual cessation."""

import asyncio
import fcntl
import json
import os
import sqlite3
import subprocess
import sys
import time
from pathlib import Path
from unittest.mock import AsyncMock, patch

from schema_support import restore_published_schema
from submission_support import SubmissionCase
from support import git

from broodling import (
    AbandonmentCoordinator,
    CessationUnconfirmed,
    containment,
    workspace,
)
from broodling.errors import StaleAttempt, WorktreeOwnershipConflict


class RetirementTests(SubmissionCase):
    def setUp(self):
        super().setUp()
        self.administrator = AbandonmentCoordinator(self.store, self.adapter)

    def stop(self):
        return asyncio.run(self.administrator.stop(self.attempt_id, "explicit stop"))

    def test_schema5_migration_preserves_abandonment_and_original_bindings(self):
        self.prepare()
        original = self.coordinator.record(self.attempt_id)
        restore_published_schema(self.store, 5)
        self.store.connection.execute(
            "INSERT INTO attempt_abandonments VALUES (?, 'old reason', 'old time')",
            (self.attempt_id,),
        )
        self.restart()
        self.administrator = AbandonmentCoordinator(self.store, self.adapter)
        self.assertFalse(self.store.get_attempt(self.attempt_id).is_current)
        self.assertEqual(self.store.abandonment(self.attempt_id).reason, "old reason")
        self.assertEqual(self.coordinator.record(self.attempt_id), original)
        self.assertEqual(
            self.store.get_attempt(self.attempt_id).b1_commit_oid,
            self.attempt.b1_commit_oid,
        )
        self.assertIsNone(self.administrator.record(self.attempt_id))
        self.assertIsNotNone(self.stop())

    def test_never_dispatched_needs_no_runtime_and_discards_dirty_owned_tree(self):
        (self.path / "discard-me").write_text("abandoned only")
        sibling = self.root / "sibling"
        git(self.repository, "worktree", "add", "-b", "sibling", str(sibling))
        with patch.object(self.adapter, "stop_known") as native:
            safe = self.stop()
            self.assertEqual(json.loads(safe.proof_json)["basis"], "never_dispatched")
            retired = self.administrator.retire(self.attempt_id)
            native.assert_not_called()
        self.assertIsNotNone(retired.retired_at)
        self.assertFalse(self.path.exists())
        self.assertTrue(sibling.exists())
        self.assertTrue(self.store.path.exists())
        self.assertTrue(self.path.parent.exists())
        self.assertEqual(self.administrator.retire(self.attempt_id), retired)
        self.assertEqual(self.stop(), retired)
        with self.assertRaises(StaleAttempt):
            self.provisioner().provision(self.attempt_id)

    def test_prepared_is_never_dispatched_but_ambiguous_dispatch_stays_blocked(self):
        self.prepare()
        with patch.object(self.adapter, "stop_known") as native:
            self.stop()
            native.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "prepared")

    def test_dispatched_without_id_cannot_replay_or_retire(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("lost")),
            self.assertRaises(OSError),
        ):
            self.submit()
        with (
            patch.object(self.adapter, "submit") as submit,
            patch.object(self.adapter, "stop_known") as stop,
        ):
            with self.assertRaisesRegex(CessationUnconfirmed, "identity"):
                self.stop()
            with self.assertRaises(CessationUnconfirmed):
                self.administrator.retire(self.attempt_id)
            submit.assert_not_called()
            stop.assert_not_called()
        self.assertFalse(self.store.get_attempt(self.attempt_id).is_current)
        self.assertTrue(self.path.exists())

    def test_terminal_legacy_run_cannot_gain_new_containment_authority(self):
        with patch.object(self.adapter, "submit", return_value="legacy-run"):
            self.submit()
        with patch.object(self.adapter, "stop_known", AsyncMock()) as native:
            with self.assertRaisesRegex(CessationUnconfirmed, "containment binding"):
                self.stop()
            native.assert_not_called()
        self.assertIsNone(self.administrator.record(self.attempt_id))

    def test_no_retirement_before_stop_or_when_containment_is_unknown(self):
        with self.assertRaises(CessationUnconfirmed):
            self.administrator.retire(self.attempt_id)
        with (
            patch.object(containment, "confirm_ceased", return_value=False),
            self.assertRaises(CessationUnconfirmed),
        ):
            self.stop()
        self.assertIsNone(self.administrator.record(self.attempt_id))
        self.assertTrue(self.path.exists())

    def test_owned_removal_acknowledgment_loss_converges(self):
        self.stop()
        with (
            patch.object(
                self.administrator, "_acknowledge", side_effect=OSError("ack lost")
            ),
            self.assertRaises(OSError),
        ):
            self.administrator.retire(self.attempt_id)
        self.assertFalse(self.path.exists())
        self.assertIsNone(self.administrator.record(self.attempt_id).retired_at)
        self.assertIsNotNone(self.administrator.retire(self.attempt_id).retired_at)

    def test_missing_marker_or_foreign_branch_cannot_be_retired(self):
        self.stop()
        git(self.path, "checkout", "-b", "foreign")
        with self.assertRaises(WorktreeOwnershipConflict):
            self.administrator.retire(self.attempt_id)
        self.assertTrue(self.path.exists())

    def test_retirement_facts_cannot_be_overwritten_or_deleted(self):
        self.stop()
        self.administrator.retire(self.attempt_id)
        for sql in (
            "UPDATE attempt_retirements SET retired_at = NULL",
            "UPDATE attempt_retirements SET proof_json = '{}'",
            "DELETE FROM attempt_retirements",
            "INSERT OR REPLACE INTO attempt_retirements SELECT * FROM attempt_retirements",
        ):
            with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                self.store.connection.execute(sql)

    def retirement_child(self, action, *arguments):
        return subprocess.Popen(
            [
                sys.executable,
                str(Path(__file__).with_name("retirement_crash_child.py")),
                str(self.store.path),
                self.attempt_id,
                action,
                *arguments,
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )

    def test_process_death_after_removal_before_acknowledgment_converges(self):
        self.stop()
        child = self.retirement_child("after-remove")
        stdout, stderr = child.communicate(timeout=10)
        self.assertEqual(child.returncode, 73, (stdout, stderr))
        self.assertFalse(self.path.exists())
        self.assertIsNone(self.administrator.record(self.attempt_id).retired_at)
        self.assertIsNotNone(self.administrator.retire(self.attempt_id).retired_at)

    def test_orphan_git_retains_exclusion_until_it_finishes(self):
        self.stop()
        ready, gate = self.root / "git-ready", self.root / "release-git"
        shim = self.root / "held-git"
        shim.write_text(
            "#!/usr/bin/python3\nimport os,sys,time\nfrom pathlib import Path\n"
            "if 'remove' in sys.argv:\n"
            f" Path({str(ready)!r}).touch()\n"
            " deadline=time.monotonic()+15\n"
            f" while not Path({str(gate)!r}).exists():\n"
            "  if time.monotonic()>deadline: raise SystemExit(74)\n"
            "  time.sleep(.01)\n"
            "os.execv('/usr/bin/git',['/usr/bin/git',*sys.argv[1:]])\n"
        )
        shim.chmod(0o755)
        caller = self.retirement_child("held-git", str(shim))
        follower = None
        try:
            deadline = time.monotonic() + 10
            while not ready.exists():
                if caller.poll() is not None or time.monotonic() > deadline:
                    self.fail("retirement did not reach its external Git child")
                time.sleep(0.01)
            caller.kill()
            caller.wait(timeout=5)
            lock_fd = os.open(
                self.path.parent / workspace.PROVISIONING_LOCK, os.O_RDONLY
            )
            try:
                with self.assertRaises(BlockingIOError):
                    fcntl.flock(lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            finally:
                os.close(lock_fd)
            follower = self.retirement_child("normal")
            time.sleep(0.15)
            self.assertIsNone(
                follower.poll(), "retirement bypassed the live inherited lock"
            )
            self.assertTrue(self.path.exists())
            self.assertIsNone(self.administrator.record(self.attempt_id).retired_at)
            gate.touch()
            stdout, stderr = follower.communicate(timeout=10)
            self.assertEqual(follower.returncode, 0, stderr)
            self.assertTrue(stdout.strip())
            self.assertFalse(self.path.exists())
        finally:
            gate.touch()
            if caller.poll() is None:
                caller.kill()
            caller.communicate(timeout=10)
            if follower is not None and follower.poll() is None:
                follower.kill()
                follower.communicate(timeout=10)
