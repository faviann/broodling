"""DirectTarget CLI compatibility is checked without a provider or GitHub call."""

from contextlib import ExitStack
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from evaluation.p5 import verify_direct_target_gh as check
from evaluation.p5.v5 import run_r01 as driver


OLD_VERSION = "gh version 2.23.0 (2023-02-27 Debian 2.23.0+dfsg1-1)"
NEW_VERSION = "gh version 2.101.0 (2026-09-15)"


def installed_gh(version, *, slurp):
    def run(arguments, **kwargs):
        if tuple(arguments[-2:]) == ("/usr/bin/gh", "--version"):
            return subprocess.CompletedProcess(arguments, 0, version + "\nrelease URL\n", "")
        if tuple(arguments[-6:]) == (
            "/usr/bin/gh", "api", "graphql", "--paginate", "--slurp", "--help",
        ):
            if slurp:
                return subprocess.CompletedProcess(arguments, 0, "FLAGS\n      --slurp  Wrap pages in an array\n", "")
            raise subprocess.CalledProcessError(1, arguments, stderr="unknown flag: --slurp")
        raise AssertionError(f"unexpected command: {arguments}")
    return run


class DirectTargetGitHubCLITests(unittest.TestCase):
    def test_installed_absolute_binary_version_and_capability_are_recorded(self):
        with patch.object(check.subprocess, "run", side_effect=installed_gh(NEW_VERSION, slurp=True)) as run:
            facts = check.verify("docker", "exec", "selected-container")
        self.assertEqual(facts, {
            "gh_program": "/usr/bin/gh", "gh_version": NEW_VERSION,
            "api_paginate_slurp": True,
        })
        self.assertTrue(all(call.args[0][:3] == ("docker", "exec", "selected-container")
                            for call in run.call_args_list))

    def test_incompatible_cli_retains_version_in_refusal(self):
        with patch.object(check.subprocess, "run", side_effect=installed_gh(OLD_VERSION, slurp=False)):
            with self.assertRaises(check.IncompatibleGitHubCLI) as raised:
                check.verify("docker", "exec", "selected-container")
        self.assertEqual(raised.exception.facts["gh_version"], OLD_VERSION)
        self.assertFalse(raised.exception.facts["api_paginate_slurp"])

    def test_missing_binary_or_failed_probe_refuses_without_leaking_stderr(self):
        for error in (FileNotFoundError("secret sentinel"),
                      subprocess.CalledProcessError(1, "gh", stderr="secret sentinel"),
                      subprocess.TimeoutExpired("gh", 30, stderr="secret sentinel")):
            with self.subTest(error=type(error).__name__):
                with patch.object(check.subprocess, "run", side_effect=error):
                    with self.assertRaises(check.IncompatibleGitHubCLI) as raised:
                        check.verify()
                self.assertIsNone(raised.exception.facts["gh_version"])
                self.assertNotIn("secret sentinel", str(raised.exception))

    def test_successful_help_without_flag_is_not_accepted(self):
        responses = [subprocess.CompletedProcess("gh", 0, NEW_VERSION, ""),
                     subprocess.CompletedProcess("gh", 0, "FLAGS\n      --paginate  Fetch pages\n", "")]
        with patch.object(check.subprocess, "run", side_effect=responses):
            with self.assertRaises(check.IncompatibleGitHubCLI):
                check.verify()

    def test_start_refuses_old_target_before_counting_or_admission(self):
        with tempfile.TemporaryDirectory() as directory, ExitStack() as stack:
            root = Path(directory)
            state, evidence, setup = (root / name for name in ("state", "evidence", "setup"))
            for path in (state, evidence, setup):
                path.mkdir()
            for name, path in (("STATE", state), ("EVIDENCE", evidence), ("SETUP", setup)):
                stack.enter_context(patch.object(driver, name, path))
            (setup / "SHA256SUMS").write_text("")
            allocation = {"counts": {"P": 8, "S": 0, "D": 0, "U": 0, "A": 0, "J_A": 0},
                          "slots": [{"status": "NOT_STARTED"} for _ in range(8)]}
            for path in (setup, evidence):
                (path / "slots.json").write_text(json.dumps(allocation))
            (evidence / "pre-admission-verification.json").write_text(json.dumps({
                "ready_for_separate_live_authorization": True, "issue": 79,
                "protocol": driver.PROTOCOL, "freeze_commit": driver.FREEZE,
                "baseline_commit": driver.BASELINE,
            }))
            (evidence / "target-preparation.json").write_text(json.dumps({
                "container_id": "incompatible-target",
            }))
            before = {path: path.read_bytes() for path in root.rglob("*.json")}
            for target in ("write", "BroodlingStore.open", "AttemptProvisioner.admit_and_provision",
                           "SubmissionCoordinator.submit"):
                stack.enter_context(patch(f"{driver.__name__}.{target}",
                                          side_effect=AssertionError("admission/effect is forbidden")))
            stack.enter_context(patch.object(check.subprocess, "run",
                                             side_effect=installed_gh(OLD_VERSION, slurp=False)))
            with self.assertRaises(check.IncompatibleGitHubCLI) as raised:
                driver.start()
            self.assertEqual(raised.exception.facts["gh_version"], OLD_VERSION)
            self.assertEqual({path: path.read_bytes() for path in root.rglob("*.json")}, before)
            self.assertFalse((evidence / "R01/execution-start.json").exists())
            self.assertFalse((state / "broodling.sqlite3").exists())
