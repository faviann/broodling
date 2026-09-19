"""Deployment readiness probes the selected container without dispatching work."""

import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from deployment import check_target


class DeploymentTargetTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.origin = "http://127.0.0.1:18770"
        (self.root / "installation.json").write_text(json.dumps({
            "container_name": "installation-target", "image_id": "sha256:installed",
            "direct_target_origin": self.origin,
        }))
        (self.root / "config.json").write_text(json.dumps({"direct_target_origin": self.origin}))
        self.container = {
            "Id": "selected-container-id", "Image": "sha256:installed",
            "State": {"Running": True},
            "Config": {
                "User": "", "Env": ["HOME=/home/node", "CODEX_HOME=/home/node/.codex"],
                "Entrypoint": ["zeroshot", "target", "serve"],
                "Cmd": ["--listen", "0.0.0.0:18767", "--public-origin", self.origin,
                        "--storage", "/state"],
            },
            "HostConfig": {
                "Privileged": False, "NetworkMode": "bridge", "CapDrop": None,
                "RestartPolicy": {"Name": "no"},
                "PortBindings": {"18767/tcp": [{"HostIp": "127.0.0.1", "HostPort": "18770"}]},
            },
            "Mounts": [{"Type": "bind", "RW": True, "Source": str(self.root / source),
                        "Destination": destination} for source, destination in (
                            ("target-state", "/state"), ("target-home", "/home/node"))],
        }
        self.slurp = True
        self.discovery = {"kind": "zeroshot.native-v2-target/v2", "authentication": "none",
                          "oecpPath": "/native-v2/oecp"}

    def command(self, *args):
        if args == ("docker", "inspect", "installation-target"):
            return json.dumps([self.container])
        self.assertEqual(args[:3], ("docker", "exec", "selected-container-id"))
        args = args[3:]
        versions = {"/usr/local/bin/zeroshot": "zeroshot 10.3.0",
                    "/usr/local/bin/codex": "codex-cli 0.153.4",
                    "/usr/local/bin/node": "v22.23.2",
                    "/usr/bin/gh": "gh version 2.101.0 (2026-09-15)"}
        if len(args) == 2 and args[1] == "--version":
            return versions[args[0]]
        if args[0] == "sha256sum":
            digest = check_target.GH_SHA256 if args[1] == "/usr/bin/gh" else check_target.NATIVE_SHA256
            return digest + "  " + args[1]
        if args == ("/usr/bin/gh", "api", "graphql", "--paginate", "--slurp", "--help"):
            return "FLAGS\n    --slurp Wrap pages" if self.slurp else "FLAGS\n    --paginate Fetch pages"
        if args[:2] == ("python3", "-c") and "os.setuid(10002)" in args[2]:
            return ""
        self.fail(f"unexpected target command: {args!r}")

    def check(self):
        with patch.object(check_target, "command", side_effect=self.command), \
                patch.object(check_target, "urlopen", return_value=io.StringIO(json.dumps(self.discovery))):
            return check_target.check(self.root)

    def test_actual_container_pins_and_capabilities_are_checked_without_provider_work(self):
        facts = self.check()
        self.assertTrue(facts["ready"])
        self.assertTrue(facts["api_paginate_slurp"])
        self.assertTrue(facts["hosted_uid_transition"])
        self.assertEqual(facts["provider_tasks"], 0)
        self.assertEqual(facts["container_id"], "selected-container-id")
        self.assertEqual(facts["image_id"], "sha256:installed")

    def test_stopped_drifted_and_public_targets_refuse_before_readiness(self):
        cases = [
            (self.container["State"], "Running", False, "stopped"),
            (self.container, "Image", "sha256:other", "image differs"),
            (self.container["Config"], "User", "1000:1000", "container root"),
            (self.container["HostConfig"], "CapDrop", ["SETUID"], "capabilities"),
            (self.container["HostConfig"]["PortBindings"]["18767/tcp"][0],
             "HostIp", "0.0.0.0", "loopback"),
            (self.container["Config"], "Env", ["GH_TOKEN=secret sentinel"], "credentials"),
            (self.container["Mounts"][0], "Source", "/other-state", "mounts"),
        ]
        for mapping, key, value, message in cases:
            with self.subTest(message=message):
                prior = mapping[key]
                mapping[key] = value
                try:
                    with self.assertRaisesRegex(check_target.TargetNotReady, message):
                        self.check()
                finally:
                    mapping[key] = prior

    def test_installed_gh_missing_required_flag_refuses(self):
        self.slurp = False
        with self.assertRaisesRegex(check_target.TargetNotReady, "lacks api"):
            self.check()

    def test_changed_or_extended_invocation_configuration_refuses_before_target_access(self):
        for config in ({"direct_target_origin": "http://127.0.0.1:18771"},
                       {"direct_target_origin": self.origin, "extra": "unexpected"}, {}):
            with self.subTest(config=config):
                (self.root / "config.json").write_text(json.dumps(config))
                with patch.object(check_target, "command") as command, \
                        patch.object(check_target, "urlopen") as urlopen:
                    with self.assertRaisesRegex(check_target.TargetNotReady, "invocation configuration"):
                        check_target.check(self.root)
                command.assert_not_called()
                urlopen.assert_not_called()

    def test_discovery_must_be_the_supported_native_target(self):
        self.discovery["kind"] = "another-service"
        with self.assertRaisesRegex(check_target.TargetNotReady, "discovery"):
            self.check()

    def test_subprocess_failure_does_not_expose_environment_or_stderr(self):
        error = subprocess.CalledProcessError(1, "docker", stderr="secret sentinel")
        with patch.object(check_target.subprocess, "run", side_effect=error):
            with self.assertRaises(check_target.TargetNotReady) as raised:
                check_target.command("docker", "inspect", "installation-target")
        self.assertNotIn("secret sentinel", str(raised.exception))
