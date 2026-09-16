"""The launcher supplies explicit domain policy without supervising execution."""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from support import durable_test_root, make_repository

from broodling.codex_profile import LAUNCHER, OPERATING_ENVIRONMENT, CodexProfile
from broodling.errors import UnsupportedRuntime


class CodexProfileTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.durable = durable_test_root("broodling-profile-")
        self.addCleanup(shutil.rmtree, self.durable, ignore_errors=True)
        self.workspace = self.durable / "candidate"
        make_repository(self.workspace)
        self.home = self.root / "profile-home"
        self.home.mkdir()
        self.codex_home = self.root / "isolated-codex-home"
        self.codex_home.mkdir()
        (self.codex_home / "auth.json").write_text('{"test":"SECRET_AUTH_CANARY"}')
        self.executable = self.root / "real-codex"
        self.executable.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "if sys.argv[1:] == ['--version']:\n"
            "    print('codex-cli 0.153.4')\n"
            "else:\n"
            "    print(json.dumps({'argv': sys.argv[1:], 'stdin': sys.stdin.read(), "
            "'home': os.environ['HOME'], 'codexHome': os.environ['CODEX_HOME']}))\n"
        )
        self.executable.chmod(0o755)
        self.profile = CodexProfile(self.executable, self.home, self.codex_home)

    def test_existing_configured_paths_validate_without_provisioning(self):
        self.profile.validate(self.workspace, path=os.defpath)
        self.assertEqual(list(self.home.iterdir()), [])
        self.assertEqual(
            [path.name for path in self.codex_home.iterdir()], ["auth.json"]
        )

    def test_ambient_tmpdir_is_not_a_declared_provider_scratch_root(self):
        with patch.dict(os.environ, {"TMPDIR": str(self.durable)}):
            self.profile.validate(self.workspace, path=os.defpath)
        self.assertEqual(self.profile.environment(os.defpath)["TMPDIR"], "")

    def test_profile_request_identity_contains_no_authentication_bytes(self):
        identity = self.profile.identity()
        environment = self.profile.environment(os.defpath)
        self.assertNotIn("SECRET_AUTH_CANARY", json.dumps([identity, environment]))
        self.assertEqual(len(identity["launcherSha256"]), 64)
        self.assertEqual(
            set(environment),
            {
                "PATH",
                "BROODLING_REAL_CODEX",
                "BROODLING_PROFILE_HOME",
                "BROODLING_ISOLATED_CODEX_HOME",
                *OPERATING_ENVIRONMENT,
            },
        )
        self.assertEqual(environment["PATH"].split(os.pathsep)[0], str(LAUNCHER.parent))

    def test_missing_ambient_or_candidate_profile_is_not_admitted(self):
        for bad_home, bad_codex_home in (
            (self.root / "missing", self.codex_home),
            (self.workspace, self.codex_home),
            (self.home, self.workspace),
            (self.home, self.home),
            (self.home, self.root / "missing"),
        ):
            with (
                self.subTest(home=bad_home, codex_home=bad_codex_home),
                self.assertRaises(UnsupportedRuntime),
            ):
                CodexProfile(self.executable, bad_home, bad_codex_home).validate(
                    self.workspace, path=os.defpath
                )

    def test_ambient_settings_and_non_auth_material_fail_initial_validation(self):
        for target in (self.home, self.codex_home):
            with self.subTest(target=target):
                material = target / "config.toml"
                material.write_text("AMBIENT_CANARY")
                with self.assertRaises(UnsupportedRuntime):
                    self.profile.validate(self.workspace, path=os.defpath)
                material.unlink()

    def test_unsupported_cli_version_is_refused(self):
        self.executable.write_text("#!/bin/sh\nprintf 'codex-cli other-version\\n'\n")
        with self.assertRaises(UnsupportedRuntime):
            self.profile.validate(self.workspace, path=os.defpath)

    def test_candidate_cannot_own_trusted_launcher_(self):
        for name in ("LAUNCHER",):
            with (
                self.subTest(name=name),
                patch(f"broodling.codex_profile.{name}", self.workspace / "trusted.py"),
                self.assertRaises(UnsupportedRuntime),
            ):
                self.profile.validate(self.workspace, path=os.defpath)

    def test_invalid_profile_is_refused_before_invoking_the_cli(self):
        invoked = self.root / "cli-invoked"
        self.executable.write_text(
            "#!/usr/bin/env python3\n"
            "from pathlib import Path\n"
            f"Path({str(invoked)!r}).touch()\n"
            "print('codex-cli 0.153.4')\n"
        )
        (self.home / "ambient-config").write_text("unqualified")
        with self.assertRaises(UnsupportedRuntime):
            self.profile.validate(self.workspace, path=os.defpath)
        self.assertFalse(invoked.exists())

    def test_launcher_preserves_role_sandbox_prompt_and_runtime_args(self):
        for sandbox in ("read-only", "workspace-write"):
            with self.subTest(sandbox=sandbox):
                original = [
                    "exec",
                    "--sandbox",
                    sandbox,
                    "--config",
                    "sandbox_workspace_write.network_access=true",
                    "--config",
                    "features.apps=true",
                    "--config",
                    'approval_policy="never"',
                    "-c",
                    "sandbox_workspace_write.network_access=true",
                    "--model",
                    "gpt-5.6-sol",
                    "--json",
                    "-",
                ]
                environment = self.profile.environment(os.defpath)
                environment.update(
                    {"HOME": "/ambient-home", "CODEX_HOME": "/ambient-codex"}
                )
                result = subprocess.run(
                    [
                        sys.executable,
                        str(LAUNCHER),
                        *original,
                    ],
                    input="Unchanged provider prompt\nInput JSON: {}\n",
                    text=True,
                    capture_output=True,
                    check=True,
                    cwd=self.workspace,
                    env=environment,
                )
                observed = json.loads(result.stdout)
                self.assertEqual(observed["home"], str(self.home))
                self.assertEqual(observed["codexHome"], str(self.codex_home))
                self.assertEqual(
                    observed["stdin"], "Unchanged provider prompt\nInput JSON: {}\n"
                )
                self.assertEqual(
                    observed["argv"],
                    [
                        "exec",
                        "--ignore-user-config",
                        "--ignore-rules",
                        "--config",
                        "sandbox_workspace_write.network_access=false",
                        "--config",
                        "sandbox_workspace_write.exclude_slash_tmp=true",
                        "--config",
                        'web_search="disabled"',
                        "--config",
                        'approval_policy="never"',
                        "--config",
                        "features.apps=false",
                        "--config",
                        "features.plugins=false",
                        "--config",
                        "features.hooks=false",
                        "--config",
                        "notify=[]",
                        "--sandbox",
                        sandbox,
                        "--model",
                        "gpt-5.6-sol",
                        "--json",
                        "-",
                    ],
                )

    def test_native_unset_or_bypass_defaults_still_get_workspace_sandbox(self):
        for permission in ([], ["--dangerously-bypass-approvals-and-sandbox"]):
            with self.subTest(permission=permission):
                result = subprocess.run(
                    [str(LAUNCHER), "exec", *permission, "--json", "-"],
                    input="frozen task",
                    text=True,
                    capture_output=True,
                    check=True,
                    env=self.profile.environment(os.defpath),
                    cwd=self.workspace,
                )
                observed = json.loads(result.stdout)
                args = observed["argv"]
                self.assertEqual(args[args.index("--sandbox") + 1], "workspace-write")
                self.assertNotIn("--dangerously-bypass-approvals-and-sandbox", args)
                self.assertIn('approval_policy="never"', args)
                self.assertIn('web_search="disabled"', args)
                self.assertIn("sandbox_workspace_write.exclude_slash_tmp=true", args)
                self.assertEqual(observed["stdin"], "frozen task")

    def test_native_config_probe_is_unavailable_without_loading_host_configuration(
        self,
    ):
        result = subprocess.run(
            [str(LAUNCHER), "app-server"],
            text=True,
            capture_output=True,
            check=False,
            env=self.profile.environment(os.defpath),
            cwd=self.workspace,
        )
        self.assertEqual(result.returncode, 78)
        self.assertEqual(result.stdout, "")
        self.assertIn("explicit execution policy", result.stderr)

    def test_native_same_execution_resume_keeps_session_persistence(self):
        result = subprocess.run(
            [str(LAUNCHER), "exec", "--json", "resume", "native-thread", "-"],
            input="native correction",
            text=True,
            capture_output=True,
            check=True,
            env=self.profile.environment(os.defpath),
            cwd=self.workspace,
        )
        observed = json.loads(result.stdout)
        self.assertEqual(observed["argv"][-3:], ["resume", "native-thread", "-"])
        self.assertNotIn("--ephemeral", observed["argv"])
        self.assertEqual(observed["stdin"], "native correction")

    def test_rebinding_provider_paths_cannot_change_the_admitted_target(self):
        moved = self.workspace / "provider-home"
        self.home.rename(self.root / "original-home")
        moved.mkdir()
        self.home.symlink_to(moved, target_is_directory=True)
        for operation in (
            self.profile.identity,
            lambda: self.profile.validate(self.workspace, path=os.defpath),
        ):
            with self.assertRaisesRegex(UnsupportedRuntime, "canonical"):
                operation()

    def test_launcher_requires_explicit_paths(self):
        result = subprocess.run(
            [str(LAUNCHER), "exec", "-"],
            text=True,
            capture_output=True,
            env={"PATH": os.defpath},
            check=False,
        )
        self.assertEqual(result.returncode, 78)
        self.assertEqual(result.stdout, "")


if __name__ == "__main__":
    unittest.main()
