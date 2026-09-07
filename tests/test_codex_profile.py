"""The product launcher retains the qualified sidecar/provider boundary."""

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from broodling.codex_profile import LAUNCHER, QualifiedCodexProfile
from broodling.errors import UnsupportedRuntime


class CodexProfileTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.workspace = self.root / "candidate"
        self.workspace.mkdir()
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
        self.profile = QualifiedCodexProfile(
            self.executable, self.home, self.codex_home
        )

    def test_existing_qualified_paths_validate_without_provisioning(self):
        self.profile.validate(self.workspace, path=os.defpath)
        self.assertEqual(list(self.home.iterdir()), [])
        self.assertEqual(
            [path.name for path in self.codex_home.iterdir()], ["auth.json"]
        )

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
                QualifiedCodexProfile(
                    self.executable, bad_home, bad_codex_home
                ).validate(self.workspace, path=os.defpath)

    def test_ambient_settings_and_non_auth_material_fail_initial_validation(self):
        for target in (self.home, self.codex_home):
            with self.subTest(target=target):
                material = target / "config.toml"
                material.write_text("AMBIENT_CANARY")
                with self.assertRaises(UnsupportedRuntime):
                    self.profile.validate(self.workspace, path=os.defpath)
                material.unlink()

    def test_nonqualified_cli_version_is_refused(self):
        self.executable.write_text("#!/bin/sh\nprintf 'codex-cli other-version\\n'\n")
        with self.assertRaises(UnsupportedRuntime):
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
                    [str(LAUNCHER), *original],
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
                        "--ephemeral",
                        "--config",
                        "sandbox_workspace_write.network_access=false",
                        "--sandbox",
                        sandbox,
                        "--config",
                        'approval_policy="never"',
                        "--model",
                        "gpt-5.6-sol",
                        "--json",
                        "-",
                    ],
                )

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
