"""Entry-point safety only; these checks never run qualification campaigns."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = (
    "qualification/v1-p2/issue14_evidence.py",
    "qualification/v1-p2/issue14_regression.py",
    "qualification/v1-p4/issue21_lifecycle.py",
    "qualification/v1-p4/issue22_lifecycle.py",
)
LIFECYCLES = SCRIPTS[-2:]

# A cold interpreter observes imports, including argv and sys.path, without
# exposing the real campaigns or retained evidence to a regressed entry point.
IMPORT_PROBE = """
import asyncio
import importlib.util
import os
import sys
import unittest
from unittest.mock import patch

script, *args = sys.argv[1:]
sys.argv = [script, *args]
original_path = sys.path[:]
def refuse(*args, **kwargs):
    raise AssertionError("import started a qualification campaign")
def no_effects(event, args):
    if event == "open" and args[2] & (os.O_WRONLY | os.O_RDWR | os.O_CREAT):
        raise AssertionError("import opened a file for writing")
    if event in {"subprocess.Popen", "os.mkdir", "os.remove", "os.rename",
                 "os.rmdir", "os.fork", "os.kill", "socket.connect"}:
        raise AssertionError("import side effect: " + event)
sys.addaudithook(no_effects)
with patch.object(unittest.TextTestRunner, "run", refuse), patch.object(asyncio, "run", refuse):
    spec = importlib.util.spec_from_file_location("qualification_import_probe", script)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
assert sys.path == original_path, "import changed sys.path"
"""

# Run the actual output guard, refusing any accidental campaign entry.
LIFECYCLE_PROBE = """
import runpy
import sys
import unittest
from unittest.mock import patch

script, *args = sys.argv[1:]
sys.argv = [script, *args]
def campaign(runner, suite):
    raise AssertionError("invalid output started a campaign")
with patch.object(unittest.TextTestRunner, "run", campaign):
    runpy.run_path(script, run_name="__main__")
"""


class QualificationEntrypointTests(unittest.TestCase):
    def probe(self, source, script, *args, cwd):
        return subprocess.run(
            [sys.executable, "-B", "-c", source, str(ROOT / script), *map(str, args)],
            cwd=cwd,
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )

    def test_import_is_inert_with_execution_arguments(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "must-not-exist.json"
            for script in SCRIPTS:
                execution_args = (output,)
                if script.endswith("issue14_evidence.py"):
                    execution_args = ("--output", output)
                elif script.endswith("issue14_regression.py"):
                    execution_args = ("--legacy-b1-guard",)
                with self.subTest(script=script):
                    result = self.probe(
                        IMPORT_PROBE, script, *execution_args, cwd=directory
                    )
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(result.stdout, "")
                    self.assertEqual(result.stderr, "")
                    self.assertFalse(output.exists())

    def test_lifecycle_rejects_missing_or_occupied_output_before_campaign(self):
        with tempfile.TemporaryDirectory() as directory:
            occupied = Path(directory) / "existing.json"
            occupied.write_bytes(b"retained evidence\n")
            alias = Path(directory) / "alias.json"
            alias.symlink_to(occupied)
            dangling = Path(directory) / "dangling.json"
            dangling.symlink_to(Path(directory) / "absent.json")
            for script in LIFECYCLES:
                for args in (
                    (),
                    (occupied,),
                    (alias,),
                    (dangling,),
                    (Path(directory) / "missing-parent" / "new.json",),
                ):
                    with self.subTest(script=script, args=args):
                        result = self.probe(
                            LIFECYCLE_PROBE, script, *args, cwd=directory
                        )
                        self.assertEqual(result.returncode, 2, result.stderr)
                        self.assertEqual(result.stdout, "")
                        self.assertIn("error:", result.stderr)
                        self.assertEqual(occupied.read_bytes(), b"retained evidence\n")
