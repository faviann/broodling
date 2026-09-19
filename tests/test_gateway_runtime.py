"""Installed-native gateway expansion, without target or provider execution."""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from zeroshot import UniformRuntime
from zeroshot._binary import resolve_binary

from broodling.zeroshot_sdk import (
    V1_GATEWAY_BASE_URL,
    ZeroshotSubmitter,
    assert_supported_integration,
)


class GatewayRuntimeTests(unittest.TestCase):
    def test_native_expansion_keeps_gateway_agents_separate_from_git_delivery(self):
        assert_supported_integration()
        with tempfile.TemporaryDirectory(prefix="broodling-gateway-") as directory:
            scratch = Path(directory)
            provider_key = "non-secret-gateway-sentinel"
            github_token = "non-secret-github-sentinel"
            submitter = ZeroshotSubmitter(
                scratch / "unused-state",
                delivery_target_origin="http://127.0.0.1:8123",
                github_token=github_token,
                gateway_base_url=V1_GATEWAY_BASE_URL,
                gateway_api_key=provider_key,
            )
            runtime = UniformRuntime(
                **submitter.runtime_for("pull_request")
            ).to_dict()
            self.assertEqual(runtime, {
                "harness": "codex",
                "provider": "gateway",
                "model": "gpt-5.6-sol",
                "effort": "medium",
                "size": "small",
                "sessionScope": "execution",
            })
            runtime_file = scratch / "runtime.json"
            runtime_file.write_text(json.dumps(runtime), encoding="utf-8")
            environment = {
                "PATH": "/usr/local/bin:/usr/bin:/bin",
                "HOME": str(scratch / "home"),
                "ZEROSHOT_CONFIG_DIR": str(scratch / "config"),
                "ZEROSHOT_STATE_DIR": str(scratch / "state"),
                "GATEWAY_BASE_URL": V1_GATEWAY_BASE_URL,
                "GATEWAY_API_KEY": provider_key,
                "GH_TOKEN": github_token,
            }

            def profile_command(*arguments: str) -> dict:
                result = subprocess.run(
                    [str(resolve_binary()), "profile", *arguments],
                    cwd=scratch,
                    env=environment,
                    capture_output=True,
                    text=True,
                    check=True,
                    timeout=30,
                )
                return json.loads(result.stdout)

            # These local profile commands only materialize the graph/runtime.
            # No run command, target connection, or provider task is involved.
            profile_command(
                "set", "gateway-test",
                "--template", "software-change", "--delivery", "pull_request",
                "--uniform-runtime-config", str(runtime_file),
            )
            profile = profile_command("show", "gateway-test")
            expanded = profile.get("profile", profile)["runtime"]

        self.assertEqual(expanded["harness"], "codex")
        self.assertEqual(expanded["provider"], "gateway")
        self.assertEqual(expanded["size"], "small")
        agents = []
        deliveries = []
        for name, binding in expanded["nodes"].items():
            with self.subTest(node=name):
                if binding["kind"] == "agent":
                    agents.append(name)
                    self.assertEqual(binding["model"], "gpt-5.6-sol")
                    self.assertEqual(binding["effort"], "medium")
                    # Native serialization omits the default execution scope.
                    self.assertEqual(binding.get("sessionScope", "execution"), "execution")
                    self.assertEqual(binding["connections"], {
                        "gateway": ["GATEWAY_API_KEY", "GATEWAY_BASE_URL"],
                    })
                else:
                    deliveries.append(name)
                    self.assertEqual(binding, {
                        "kind": "git_delivery",
                        "connections": {"github": ["GH_TOKEN"]},
                    })
        self.assertTrue(agents)
        self.assertEqual(len(deliveries), 1)
        serialized = json.dumps({"runtime": runtime, "profile": profile})
        for value in (provider_key, github_token, V1_GATEWAY_BASE_URL):
            self.assertNotIn(value, serialized)


if __name__ == "__main__":
    unittest.main()
