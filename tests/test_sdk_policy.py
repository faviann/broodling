"""Supported SDK calls and the Broodling no-effect configuration seam."""

import asyncio
import copy
import json
import os
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, call, patch

from submission_support import SubmissionCase

from broodling import UnsupportedRuntime
from broodling.profile import ZEROSHOT_SDK_VERSION
from broodling.zeroshot_sdk import (
    V1_GATEWAY_BASE_URL,
    ZeroshotSubmitter,
    assert_supported_integration,
)


class SdkPolicyTests(SubmissionCase):
    def request(self):
        return json.loads(self.prepare().request_json)

    def test_published_sdk_version_and_bundled_binary_are_required(self):
        with (
            patch("importlib.metadata.version", return_value="older-sdk"),
            self.assertRaisesRegex(UnsupportedRuntime, ZEROSHOT_SDK_VERSION),
        ):
            assert_supported_integration()
        with (
            patch("importlib.metadata.version", return_value=ZEROSHOT_SDK_VERSION),
            patch.dict(
                os.environ, {"ZEROSHOT_PYTHON_NATIVE_BINARY": "/unselected/native"}
            ),
            self.assertRaisesRegex(UnsupportedRuntime, "bundled native"),
        ):
            assert_supported_integration()

    def test_ambient_environment_cannot_change_the_selected_provider_policy(self):
        original = copy.deepcopy(self.adapter.target)
        with patch.dict(
            os.environ, {"PATH": "/unselected", "GH_TOKEN": "SECRET_CANARY"}
        ):
            self.adapter._validate_policy()
        self.assertEqual(self.adapter.target, original)
        self.assertNotIn("GH_TOKEN", original["environment"])
        self.assertNotIn("SECRET_CANARY", json.dumps(original))

    def test_environment_cannot_bypass_launcher_or_redirect_profile(self):
        original = copy.deepcopy(self.adapter.target["environment"])
        for name, value in (
            ("PATH", "/usr/bin"),
            ("BROODLING_PROFILE_HOME", "/unselected/home"),
            ("BROODLING_REAL_CODEX", "/unselected/codex"),
            ("GH_TOKEN", "SECRET_CANARY"),
        ):
            self.adapter.target["environment"] = original | {name: value}
            with (
                self.subTest(name=name),
                self.assertRaisesRegex(UnsupportedRuntime, "policy/environment"),
            ):
                self.adapter.validate_dispatch(self.path)
        self.adapter.target["environment"] = original

    def test_runtime_is_a_fixed_selection_not_an_alternate_harness_seam(self):
        runtime = self.adapter.runtime
        expected = {
            "harness": "codex",
            "provider": "openai",
            "model": "gpt-5.6-sol",
            "effort": "medium",
            "size": "small",
            "session_scope": "execution",
        }
        self.assertEqual(
            self.adapter.runtime_for("pull_request"),
            expected | {"provider": "gateway"},
        )
        self.assertEqual(
            runtime,
            expected
            | {
                "connections": {
                    "profile": [
                        "BROODLING_REAL_CODEX",
                        "BROODLING_PROFILE_HOME",
                        "BROODLING_ISOLATED_CODEX_HOME",
                    ]
                }
            },
        )
        runtime["harness"] = "claude"
        runtime["connections"].clear()
        self.assertEqual(self.adapter.runtime["harness"], "codex")
        self.assertTrue(self.adapter.runtime["connections"])
        with self.assertRaises(AttributeError):
            self.adapter.runtime = runtime
        request = self.request()
        request["runtime"] = runtime
        with patch("zeroshot.Client") as client:
            with self.assertRaisesRegex(UnsupportedRuntime, "workflow runtime"):
                self.adapter.submit(request)
            client.assert_not_called()

    def test_submit_rejects_foreign_targets_or_unsupported_workflows(self):
        selected = self.request()
        requests = []
        for preset in (
            {"name": "software-change", "delivery": "pr"},
            {"name": "single-worker", "delivery": "none"},
        ):
            requests.append(selected | {"preset": preset})
        requests.append(
            selected | {"target": selected["target"] | {"stateDir": "/foreign/state"}}
        )
        legacy = copy.deepcopy(selected)
        del legacy["preset"]
        legacy["graph"] = {"historical": "custom-graph"}
        requests.append(legacy)
        for request in requests:
            with self.subTest(request=request), patch("zeroshot.Client") as client:
                with self.assertRaises(UnsupportedRuntime):
                    self.adapter.submit(request)
                client.assert_not_called()

    def test_local_reconnect_uses_only_the_persisted_state_locator(self):
        from zeroshot import LocalTarget

        request = self.request()
        run = SimpleNamespace(
            wait=AsyncMock(return_value="result"),
            force_stop=AsyncMock(return_value="stopped"),
        )
        client = MagicMock()
        client.get_run.return_value = run
        client.__aenter__ = AsyncMock(return_value=client)
        client.__aexit__ = AsyncMock(return_value=None)
        reconnected = ZeroshotSubmitter(self.root / "different-current-state")
        with patch("zeroshot.Client", return_value=client) as construct:
            self.assertEqual(
                asyncio.run(reconnected.wait(request, "known-run")), "result"
            )
            self.assertEqual(
                asyncio.run(reconnected.stop_known(request, "known-run")), "stopped"
            )
        construct.assert_has_calls(
            [
                call(
                    target=LocalTarget(state_dir=request["target"]["stateDir"]),
                    environment={},
                ),
                call(
                    target=LocalTarget(state_dir=request["target"]["stateDir"]),
                    environment={},
                ),
            ],
        )

    def test_direct_reconnect_uses_only_the_persisted_origin(self):
        from zeroshot import DirectTarget

        submitted = ZeroshotSubmitter(
            self.runtime_state,
            delivery_target_origin="http://127.0.0.1:8123",
            github_token="dispatch-only-token",
            gateway_base_url=V1_GATEWAY_BASE_URL,
            gateway_api_key="dispatch-only-provider-key",
        )
        request = self.request()
        request["target"] = submitted.target
        request["preset"] = {"name": "software-change", "delivery": "pull_request"}
        request["runtime"] = submitted.runtime_for("pull_request")
        run = SimpleNamespace(
            wait=AsyncMock(return_value="result"),
            force_stop=AsyncMock(return_value="stopped"),
        )
        client = MagicMock()
        client.get_run.return_value = run
        client.__aenter__ = AsyncMock(return_value=client)
        client.__aexit__ = AsyncMock(return_value=None)
        reconnected = ZeroshotSubmitter(self.root / "different-current-state")
        target = DirectTarget("http://127.0.0.1:8123")
        # Reconnection addresses historical OpenAI runs as well as gateway runs;
        # neither depends on today's execution profile or dispatch credentials.
        for provider in ("gateway", "openai"):
            request["runtime"]["provider"] = provider
            with (
                self.subTest(provider=provider),
                patch("zeroshot.Client", return_value=client) as construct,
            ):
                self.assertEqual(
                    asyncio.run(reconnected.wait(request, "known-run")), "result"
                )
                self.assertEqual(
                    asyncio.run(reconnected.stop_known(request, "known-run")),
                    "stopped",
                )
                self.assertEqual(
                    construct.call_args_list,
                    [
                        call(target=target, environment={}),
                        call(target=target, environment={}),
                    ],
                )

    def test_new_local_dispatch_requires_the_current_execution_profile(self):
        adapter = ZeroshotSubmitter(self.runtime_state)
        request = self.request()
        request["target"] = adapter.target
        with self.assertRaisesRegex(UnsupportedRuntime, "execution requires"):
            adapter.validate_dispatch(self.path, request)

    def test_native_state_cannot_overlap_candidate_or_shared_git(self):
        for state in (
            self.path,
            self.path / "engine",
            Path(self.attempt.b1_repository),
            self.path.parent,
        ):
            adapter = ZeroshotSubmitter(state, codex_profile=self.adapter.codex_profile)
            with (
                self.subTest(state=state),
                self.assertRaisesRegex(UnsupportedRuntime, "separate"),
            ):
                adapter.validate_dispatch(self.path)

    def test_state_directory_rebinding_is_rejected_before_dispatch_wait_or_stop(self):
        parent = self.root / "native-parent"
        parent.mkdir()
        state = parent / "state"
        adapter = ZeroshotSubmitter(state, codex_profile=self.adapter.codex_profile)
        request = self.request() | {"target": adapter.target}
        other = self.root / "other-parent"
        parent.rename(other)
        parent.symlink_to(other, target_is_directory=True)
        for operation in (
            lambda: adapter.validate_dispatch(self.path),
            lambda: asyncio.run(adapter.wait(request, "known-run")),
            lambda: asyncio.run(adapter.stop_known(request, "known-run")),
        ):
            with (
                patch("zeroshot.Client") as client,
                self.assertRaisesRegex(UnsupportedRuntime, "canonical"),
            ):
                operation()
            client.assert_not_called()

    def test_adapter_submits_native_preset_and_delegates_wait_and_stop(self):
        from zeroshot import Preset, UniformRuntime

        request = self.request()
        run = SimpleNamespace(
            id="native-run",
            wait=AsyncMock(return_value="result"),
            force_stop=AsyncMock(return_value="stopped"),
        )
        client = MagicMock()
        client.submit = AsyncMock(return_value=run)
        client.get_run.return_value = run
        client.__aenter__ = AsyncMock(return_value=client)
        client.__aexit__ = AsyncMock(return_value=None)
        with patch("zeroshot.Client", return_value=client):
            self.assertEqual(self.adapter.submit(request), run.id)
            # Provider history is native-owned after dispatch, not a fresh-home
            # precondition on waiting for a completed or still-running result.
            (self.adapter.codex_profile.profile_home / "native-runtime-file").touch()
            self.assertEqual(asyncio.run(self.adapter.wait(request, run.id)), "result")
            self.assertEqual(
                asyncio.run(self.adapter.stop_known(request, run.id)), "stopped"
            )
        client.submit.assert_awaited_once_with(
            request["task"],
            title=request["title"],
            preset=Preset("software-change", delivery="none"),
            runtime=UniformRuntime(**request["runtime"]),
            submission_key=request["submissionKey"],
        )
        run.wait.assert_awaited_once_with()
        run.force_stop.assert_awaited_once_with()
        self.assertEqual(client.get_run.call_args_list[0].args, (run.id,))
        self.assertEqual(client.get_run.call_args_list[1].args, (run.id,))

    def pr_request(self, adapter):
        request = self.request()
        request["target"] = copy.deepcopy(adapter.target)
        request["preset"] = {"name": "software-change", "delivery": "pull_request"}
        request["runtime"] = adapter.runtime_for("pull_request")
        request["delivery"] = {
            "repository": "faviann/broodling",
            "targetBranch": "main",
            "baseRevision": self.attempt.b1_commit_oid,
        }
        return request

    def pr_adapter(self, **overrides):
        return ZeroshotSubmitter(
            self.runtime_state,
            **(
                {
                    "delivery_target_origin": "http://127.0.0.1:8123",
                    "github_token": "secret-delivery-token",
                    "gateway_base_url": V1_GATEWAY_BASE_URL,
                    "gateway_api_key": "secret-provider-key",
                }
                | overrides
            ),
        )

    def test_pr_delivery_uses_gateway_with_only_ephemeral_credentials(self):
        from zeroshot import DirectTarget, Preset, UniformRuntime

        adapter = self.pr_adapter()
        request = self.pr_request(adapter)
        adapter.validate_dispatch(self.path, request)
        run = SimpleNamespace(id="delivered-run")
        client = MagicMock()
        client.submit = AsyncMock(return_value=run)
        client.__aenter__ = AsyncMock(return_value=client)
        client.__aexit__ = AsyncMock(return_value=None)
        with (
            patch.dict(
                os.environ,
                {
                    "OPENAI_API_KEY": "ambient-openai-canary",
                    "CODEX_API_KEY": "ambient-codex-canary",
                    "OPENROUTER_API_KEY": "ambient-openrouter-canary",
                    "AWS_BEARER_TOKEN_BEDROCK": "ambient-bedrock-canary",
                    "GATEWAY_API_KEY": "ambient-gateway-canary",
                    "GATEWAY_BASE_URL": "https://wrong.example/",
                },
            ),
            patch("zeroshot.Client", return_value=client) as construct,
        ):
            self.assertEqual(adapter.submit(request), run.id)
        construct.assert_called_once_with(
            target=DirectTarget(
                "http://127.0.0.1:8123", workspace=request["workspace"]
            ),
            environment=request["target"]["environment"]
            | {
                "GH_TOKEN": "secret-delivery-token",
                "GATEWAY_BASE_URL": "https://cliproxy.local.faviann.com/",
                "GATEWAY_API_KEY": "secret-provider-key",
            },
        )
        client.submit.assert_awaited_once_with(
            request["task"],
            title=request["title"],
            preset=Preset("software-change", delivery="pull_request"),
            runtime=UniformRuntime(**request["runtime"]),
            submission_key=request["submissionKey"],
            repository="faviann/broodling",
            branch="main",
            revision=self.attempt.b1_commit_oid,
        )
        self.assertNotIn("connections", request["runtime"])
        self.assertEqual(request["runtime"]["provider"], "gateway")
        self.assertNotIn("OPENAI_API_KEY", construct.call_args.kwargs["environment"])
        self.assertNotIn("secret-delivery-token", json.dumps(request))
        self.assertNotIn("secret-provider-key", json.dumps(request))
        self.assertNotIn(V1_GATEWAY_BASE_URL, json.dumps(request))

    def test_pr_delivery_requires_current_explicit_credentials_before_dispatch(self):
        for field, environment_name in (
            ("github_token", "GH_TOKEN"),
            ("gateway_api_key", "GATEWAY_API_KEY"),
        ):
            for value in (None, "", " \t", "x" * 4097):
                adapter = self.pr_adapter(**{field: value})
                request = self.pr_request(adapter)
                with (
                    self.subTest(field=field, value_length=len(value or "")),
                    patch.dict(os.environ, {environment_name: "ambient-canary"}),
                    patch("zeroshot.Client") as client,
                ):
                    for action in (
                        lambda: adapter.validate_dispatch(self.path, request),
                        lambda: adapter.submit(request),
                    ):
                        with self.assertRaisesRegex(UnsupportedRuntime, environment_name):
                            action()
                    client.assert_not_called()

    def test_real_sdk_suppresses_ambient_provider_credentials(self):
        adapter = self.pr_adapter()
        request = self.pr_request(adapter)
        ambient = {
            name: "AMBIENT_SECRET_CANARY"
            for name in (
                "OPENAI_API_KEY",
                "CODEX_API_KEY",
                "OPENROUTER_API_KEY",
                "AWS_BEARER_TOKEN_BEDROCK",
                "GITHUB_TOKEN",
                "GATEWAY_API_KEY",
            )
        }
        # Inspect the pinned SDK's process environment without opening a target
        # or starting native work; explicit Client environment is our seam.
        with patch.dict(os.environ, ambient):
            environment, _ = adapter._submission_client(request)._environment()
            observation, _ = adapter._correlated_client(request)._environment()
        self.assertNotIn("AMBIENT_SECRET_CANARY", environment.values())
        self.assertEqual(environment["GATEWAY_API_KEY"], "secret-provider-key")
        self.assertEqual(environment["GATEWAY_BASE_URL"], V1_GATEWAY_BASE_URL)
        self.assertEqual(environment["GH_TOKEN"], "secret-delivery-token")
        for name in ambient.keys() - {"GATEWAY_API_KEY"}:
            self.assertNotIn(name, environment)
        for name in (*ambient, "GH_TOKEN", "GATEWAY_BASE_URL"):
            self.assertNotIn(name, observation)

    def test_historical_openai_request_cannot_dispatch_under_gateway_profile(self):
        adapter = self.pr_adapter()
        request = self.pr_request(adapter)
        request["runtime"]["provider"] = "openai"
        with patch("zeroshot.Client") as client:
            with self.assertRaisesRegex(UnsupportedRuntime, "workflow runtime"):
                adapter.submit(request)
            client.assert_not_called()

    def test_pr_delivery_requires_the_exact_gateway_endpoint(self):
        for value in (
            None,
            "",
            " ",
            "https://cliproxy.local.faviann.com",
            "http://cliproxy.local.faviann.com/",
            "https://cliproxy.local.faviann.com/v1/",
            "https://cliproxy.local.faviann.com/?key=canary",
            "https://foreign.example/",
        ):
            adapter = self.pr_adapter(gateway_base_url=value)
            request = self.pr_request(adapter)
            with (
                self.subTest(value=value),
                patch.dict(os.environ, {"GATEWAY_BASE_URL": V1_GATEWAY_BASE_URL}),
                patch("zeroshot.Client") as client,
            ):
                for action in (
                    lambda: adapter.validate_dispatch(self.path, request),
                    lambda: adapter.submit(request),
                ):
                    with self.assertRaisesRegex(UnsupportedRuntime, "GATEWAY_BASE_URL"):
                        action()
                client.assert_not_called()

    def test_pr_environment_refuses_legacy_credentials_even_if_empty(self):
        adapter = self.pr_adapter()
        original = copy.deepcopy(adapter.target["environment"])
        for name in (
            "OPENAI_API_KEY",
            "CODEX_API_KEY",
            "OPENROUTER_API_KEY",
            "AWS_BEARER_TOKEN_BEDROCK",
            "GATEWAY_BASE_URL",
            "GATEWAY_API_KEY",
            "GH_TOKEN",
        ):
            for value in ("", "INJECTED_SECRET_CANARY"):
                adapter.target["environment"] = original | {name: value}
                request = self.pr_request(adapter)
                with self.subTest(name=name, value=value), patch("zeroshot.Client") as client:
                    for action in (
                        lambda: adapter.validate_dispatch(self.path, request),
                        lambda: adapter.submit(request),
                    ):
                        with self.assertRaisesRegex(UnsupportedRuntime, "policy/environment"):
                            action()
                    client.assert_not_called()

    def test_pr_delivery_refuses_an_invalid_branch_or_foreign_repository(self):
        adapter = self.pr_adapter()
        request = self.pr_request(adapter)
        for field, value, message in (
            ("targetBranch", "bad..branch", "target branch"),
            ("repository", "other/project", "repository"),
        ):
            changed = copy.deepcopy(request)
            changed["delivery"][field] = value
            with (
                self.subTest(field=field),
                self.assertRaisesRegex(UnsupportedRuntime, message),
            ):
                adapter.validate_dispatch(self.path, changed)
