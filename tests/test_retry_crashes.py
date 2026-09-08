"""Hard setup crashes preserve one explicit replacement and original B1."""

import fcntl
import json
import subprocess
import sys
import time
from pathlib import Path

from retry_test_support import RetryCase
from support import git, move_head

from broodling.workspace import PROVISIONING_LOCK

CHILD = Path(__file__).parent / "retry_crash_child.py"


class RetryCrashTests(RetryCase):
    def command(self, mode, *extra):
        config = self.root / "retry-target.json"
        config.write_text(json.dumps(self.new_adapter.target))
        return [
            sys.executable,
            str(CHILD),
            str(self.store_path),
            str(self.workspace_root),
            self.attempt_id,
            str(config),
            mode,
            *map(str, extra),
        ]

    def test_hard_setup_crashes_converge_without_another_attempt(self):
        # Each subsequent fault resumes the very same durable retry identity.
        for mode in (
            "allocation-write",
            "allocated",
            "worktree-add",
            "prepare-write",
            "prepared",
        ):
            with self.subTest(mode=mode):
                completed = subprocess.run(
                    self.command(mode),
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=30,
                )
                self.assertEqual(completed.returncode, 97, completed.stderr)
                if mode == "allocation-write":
                    self.assertIsNone(self.store.retry("retry"))
                else:
                    self.assertIsNotNone(self.store.retry("retry"))
        move_head(self.repository, content="LIVE_HEAD_ONLY_CANARY\n")
        coordinator = self.retry_coordinator()
        first = coordinator.prepare(self.attempt_id, "retry")
        self.assertEqual(first, coordinator.prepare(self.attempt_id, "retry"))
        assignment = self.store.worktree_assignment(first.attempt_id)
        self.assertEqual(
            git(assignment.path, "rev-parse", "HEAD"), self.attempt.b1_commit_oid
        )
        self.assertEqual(
            self.store.connection.execute("SELECT count(*) FROM attempts").fetchone()[
                0
            ],
            2,
        )
        self.assertEqual(
            self.store.connection.execute(
                "SELECT count(*) FROM attempt_retries"
            ).fetchone()[0],
            1,
        )

    def test_orphan_git_child_retains_exclusion_until_materialization_finishes(self):
        pid_file, gate = self.root / "git-pid", self.root / "git-gate"
        self.addCleanup(gate.touch)
        wrapper = self.root / "held-git"
        wrapper.write_text(
            "#!/usr/bin/python3\nimport os, sys, time\nfrom pathlib import Path\n"
            "if 'worktree' in sys.argv and 'add' in sys.argv:\n"
            f"    Path({str(pid_file)!r}).write_text(str(os.getpid()))\n"
            f"    while not Path({str(gate)!r}).exists(): time.sleep(.01)\n"
            "os.execv('/usr/bin/git', ['git', *sys.argv[1:]])\n"
        )
        wrapper.chmod(0o755)
        with (self.root / "caller.log").open("w") as log:
            caller = subprocess.Popen(
                self.command("held-git", wrapper), stdout=log, stderr=log
            )
            self.addCleanup(lambda: caller.poll() is None and caller.kill())
            deadline = time.monotonic() + 20
            while not pid_file.exists() and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertTrue(pid_file.exists())
            caller.kill()
            caller.wait(timeout=10)
            retry = self.store.retry("retry")
            assignment = self.store.worktree_assignment(retry.attempt_id)
            lock_path = assignment.path.parent / PROVISIONING_LOCK
            with lock_path.open("a") as lock, self.assertRaises(BlockingIOError):
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            resumed = subprocess.Popen(self.command("report"), stdout=log, stderr=log)
            try:
                time.sleep(0.15)
                self.assertIsNone(
                    resumed.poll(), "replay bypassed orphan Git exclusion"
                )
                gate.touch()
                self.assertEqual(resumed.wait(timeout=30), 0)
            finally:
                gate.touch()
                if resumed.poll() is None:
                    resumed.kill()
                    resumed.wait()
            self.assertEqual(
                self.retry_coordinator().prepare(self.attempt_id, "retry").attempt_id,
                retry.attempt_id,
            )
            self.assertEqual(
                git(assignment.path, "rev-parse", "HEAD"), self.attempt.b1_commit_oid
            )

    def test_retry_checkout_does_not_execute_repository_hooks(self):
        hook = self.repository / ".git/hooks/post-checkout"
        marker = self.root / "hook-executed"
        hook.write_text(f"#!/bin/sh\ntouch '{marker}'\n")
        hook.chmod(0o755)
        self.retry_coordinator().prepare(self.attempt_id, "retry")
        self.assertFalse(marker.exists())
