"""Physical PID namespace cessation, including intentionally uncontained control."""

import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch

from broodling import containment


class ContainmentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.workspace = self.root / "worktree"
        self.workspace.mkdir()

    def test_closed_unused_launch_boundary_is_safe(self):
        self.assertFalse(containment.confirm_ceased(self.workspace))
        containment.close_launches(self.workspace)
        self.assertTrue(containment.confirm_ceased(self.workspace))
        with patch.object(containment, "_guard_parent"), self.assertRaises(ValueError):
            containment.enclose(["/bin/true"], os.environ.copy(), self.workspace)

    def test_live_identity_blocks_and_terminated_identity_confirms(self):
        child = subprocess.Popen(["/bin/sleep", "60"])
        try:
            containment._retain(
                self.root,
                {
                    "profile": containment.CONTAINMENT_PROFILE,
                    "pid": child.pid,
                    "starttime": containment._starttime(child.pid),
                },
            )
            containment.close_launches(self.workspace)
            self.assertFalse(containment.confirm_ceased(self.workspace))
            child.kill()
            child.wait()
            self.assertTrue(containment.confirm_ceased(self.workspace))
        finally:
            child.kill()
            child.wait()

    def test_entry_rejects_eof_without_executing(self):
        read, write = os.pipe()
        os.close(write)
        launcher = os.pidfd_open(os.getpid())
        target = self.workspace / "forbidden"
        result = containment._entry(
            [str(read), str(launcher), "/usr/bin/touch", str(target)]
        )
        self.assertEqual(result, 78)
        self.assertFalse(target.exists())

    def test_durable_receipt_failure_never_releases_provider(self):
        target = self.workspace / "forbidden"
        with (
            patch.object(containment, "_guard_parent"),
            patch.object(
                containment, "_retain", side_effect=OSError("crash before receipt")
            ),
            self.assertRaises(OSError),
        ):
            containment.enclose(
                ["/usr/bin/touch", str(target)], os.environ.copy(), self.workspace
            )
        time.sleep(0.1)
        self.assertFalse(target.exists())
        containment.close_launches(self.workspace)
        self.assertTrue(containment.confirm_ceased(self.workspace))

    def test_gate_rejects_dead_launcher_even_with_token(self):
        child = subprocess.Popen(["/bin/sleep", "60"])
        launcher = os.pidfd_open(child.pid)
        child.kill()
        child.wait()
        read, write = os.pipe()
        os.write(write, b"G")
        os.close(write)
        target = self.workspace / "forbidden"
        self.assertEqual(
            containment._entry(
                [str(read), str(launcher), "/usr/bin/touch", str(target)]
            ),
            78,
        )
        self.assertFalse(target.exists())

    def test_held_launch_lock_blocks_cessation_even_without_receipt(self):
        containment.close_launches(self.workspace)
        with containment._lock(self.root):
            self.assertFalse(containment.confirm_ceased(self.workspace))
        self.assertTrue(containment.confirm_ceased(self.workspace))

    def test_existing_host_shared_memory_provider_path_remains_available(self):
        # The already-qualified provider profile may provision its launcher and
        # auth under /dev/shm. Outer lifecycle containment must preserve that.
        with tempfile.TemporaryDirectory(dir="/dev/shm") as directory:
            executable = Path(directory) / "provider.py"
            executable.write_text("from pathlib import Path; Path('executed').touch()")
            with patch.object(containment, "_guard_parent"):
                result = containment.enclose(
                    [sys.executable, str(executable)], os.environ.copy(), self.workspace
                )
        self.assertEqual(result, 0)
        self.assertTrue((self.workspace / "executed").exists())

    def test_normal_completion_tears_down_detached_writer(self):
        code = "import os,time; p=os.fork(); (os.setsid(),time.sleep(1),open('late','w').write('BAD')) if p==0 else None"
        with patch.object(containment, "_guard_parent"):
            result = containment.enclose(
                [sys.executable, "-c", code], os.environ.copy(), self.workspace
            )
        self.assertEqual(result, 0)
        containment.close_launches(self.workspace)
        self.assertTrue(containment.confirm_ceased(self.workspace))
        time.sleep(1.2)
        self.assertFalse((self.workspace / "late").exists())

    def test_launcher_death_after_release_kills_detached_writer(self):
        code = "import os,time; p=os.fork(); (os.setsid(),open('ready','w').write('READY'),time.sleep(1),open('late','w').write('BAD')) if p==0 else time.sleep(60)"
        launch = "from broodling import containment as c; from pathlib import Path; import os,sys; c._guard_parent=lambda:None; c.enclose([sys.executable,'-c',sys.argv[2]],os.environ.copy(),Path(sys.argv[1]))"
        child = subprocess.Popen(
            [sys.executable, "-c", launch, str(self.workspace), code]
        )
        try:
            deadline = time.monotonic() + 10
            while (
                not (self.workspace / "ready").exists() and time.monotonic() < deadline
            ):
                time.sleep(0.01)
            self.assertTrue((self.workspace / "ready").exists())
            child.kill()
            child.wait()
            containment.close_launches(self.workspace)
            deadline = time.monotonic() + 10
            while (
                not containment.confirm_ceased(self.workspace)
                and time.monotonic() < deadline
            ):
                time.sleep(0.01)
            self.assertTrue(containment.confirm_ceased(self.workspace))
            time.sleep(1.2)
            self.assertFalse((self.workspace / "late").exists())
        finally:
            child.kill()
            child.wait()

    def test_uncontained_negative_control_detects_survivor(self):
        code = "import os,time; p=os.fork(); (os.setsid(),time.sleep(.2),open('late','w').write('SURVIVOR')) if p==0 else None"
        subprocess.run([sys.executable, "-c", code], cwd=self.workspace, check=True)
        deadline = time.monotonic() + 5
        while not (self.workspace / "late").exists() and time.monotonic() < deadline:
            time.sleep(0.01)
        self.assertEqual((self.workspace / "late").read_text(), "SURVIVOR")

    def test_noncontroller_parent_is_rejected(self):
        with self.assertRaises(ValueError):
            containment._guard_parent()
