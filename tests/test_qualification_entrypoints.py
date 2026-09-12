"""Entry-point safety only; these checks never run qualification campaigns."""

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = (
    "qualification/v1-p2/issue14_evidence.py",
    "qualification/v1-p2/issue14_regression.py",
    "qualification/v1-p3/issue17_adversarial.py",
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

# Run the actual CLI and serialization, substituting only the expensive campaign
# and SDK identity lookup. Records produced here are temporary test doubles.
LIFECYCLE_PROBE = """
import contextlib
import runpy
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

script, outcome, *args = sys.argv[1:]
sys.argv = [script, *args]
def campaign(runner, suite):
    if outcome == "forbid":
        raise AssertionError("invalid output started a campaign")
    print("campaign")
    assert suite.countTestCases() > 0
    return SimpleNamespace(
        wasSuccessful=lambda: outcome != "failure",
        skipped=["controlled skip"] if outcome == "skipped" else [],
        testsRun=suite.countTestCases(),
    )
with contextlib.ExitStack() as stack:
    stack.enter_context(patch.object(unittest.TextTestRunner, "run", campaign))
    if outcome != "forbid":
        sys.path.insert(0, str(Path(script).resolve().parents[2]))
        stack.enter_context(patch("broodling.zeroshot_sdk.installed_integration",
            return_value={"testDouble": "entrypoint check, not qualification"}))
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

    def test_import_is_inert_with_bare_and_execution_arguments(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "must-not-exist.json"
            for script in SCRIPTS:
                execution_args = (output,)
                if script.endswith("issue14_evidence.py"):
                    execution_args = ("--output", output)
                elif script.endswith("issue14_regression.py"):
                    execution_args = ("--legacy-b1-guard",)
                for args in ((), execution_args):
                    with self.subTest(script=script, args=args):
                        result = self.probe(IMPORT_PROBE, script, *args, cwd=directory)
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
                issue = "21" if "issue21" in script else "22"
                retained = (
                    ROOT / f"qualification/v1-p4/evidence/issue-{issue}-lifecycle.json"
                )
                before = retained.read_bytes()
                for args in (
                    (),
                    (occupied,),
                    (alias,),
                    (dangling,),
                    (retained,),
                    (Path(directory) / "missing-parent" / "new.json",),
                ):
                    with self.subTest(script=script, args=args):
                        result = self.probe(
                            LIFECYCLE_PROBE, script, "forbid", *args, cwd=directory
                        )
                        self.assertEqual(result.returncode, 2, result.stderr)
                        self.assertEqual(result.stdout, "")
                        self.assertIn("error:", result.stderr)
                        self.assertEqual(retained.read_bytes(), before)
                        self.assertEqual(occupied.read_bytes(), b"retained evidence\n")
                result = self.probe(
                    LIFECYCLE_PROBE, script, "forbid", "--help", cwd=directory
                )
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("output", result.stdout)

    def test_intentional_lifecycle_execution_retains_results_and_source_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            for script in LIFECYCLES:
                for outcome in ("success", "failure", "skipped"):
                    with self.subTest(script=script, outcome=outcome):
                        output = Path(directory) / f"{Path(script).stem}-{outcome}.json"
                        result = self.probe(
                            LIFECYCLE_PROBE, script, outcome, output, cwd=directory
                        )
                        self.assertEqual(
                            result.returncode,
                            0 if outcome == "success" else 1,
                            result.stderr,
                        )
                        self.assertEqual(result.stdout, "campaign\n")
                        record = json.loads(output.read_text())
                        self.assertEqual(
                            record["mechanicsPassed"], outcome == "success"
                        )
                        self.assertGreater(record["testsRun"], 0)
                        self.assertIn("testDouble", record["integration"])
                        if script.endswith("issue22_lifecycle.py"):
                            self.assertEqual(
                                record["sourceSha256"][script],
                                hashlib.sha256(
                                    (ROOT / script).read_bytes()
                                ).hexdigest(),
                            )
                            self.assertEqual(
                                record["sourceSha256"], record["finalSourceSha256"]
                            )
                            self.assertTrue(record["sourceHashesUnchangedThroughout"])
