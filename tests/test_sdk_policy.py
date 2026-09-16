"""Supported SDK calls and the Broodling no-effect configuration seam."""

import asyncio
import copy
import json
import os
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

from submission_support import SubmissionCase

from broodling import UnsupportedRuntime
from broodling.profile import ZEROSHOT_SDK_VERSION
from broodling.zeroshot_sdk import ZeroshotSubmitter, assert_supported_integration


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
                self.adapter.validate_first_dispatch(self.path)
        self.adapter.target["environment"] = original

    def test_runtime_is_a_fixed_selection_not_an_alternate_harness_seam(self):
        runtime = self.adapter.runtime
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

    def test_submit_wait_and_stop_reject_foreign_targets_or_unsupported_workflows(self):
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
            for operation in (
                lambda request=request: self.adapter.submit(request),
                lambda request=request: asyncio.run(
                    self.adapter.wait(request, "known-run")
                ),
                lambda request=request: asyncio.run(
                    self.adapter.stop_known(request, "known-run")
                ),
            ):
                with self.subTest(request=request), patch("zeroshot.Client") as client:
                    with self.assertRaises(UnsupportedRuntime):
                        operation()
                    client.assert_not_called()

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
                adapter.validate_first_dispatch(self.path)

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
            lambda: adapter.validate_first_dispatch(self.path),
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
