"""Crash windows retain one key/request/Attempt/run through process death."""

import json
import subprocess
import sys
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

from submission_support import RealSubmissionCase, SubmissionCase, configured_adapter

from broodling import RequiredEffect, UnsupportedRuntime
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL

CHILD = Path(__file__).with_name("submission_crash_child.py")


class SubmissionCrashTests(RealSubmissionCase):
    def child(self, mode, gate=None):
        child = subprocess.Popen(
            [
                sys.executable,
                str(CHILD),
                str(self.store_path),
                self.attempt_id,
                str(self.runtime_state),
                mode,
                *([str(gate)] if gate else []),
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        self.addCleanup(lambda: child.poll() is None and child.kill())
        return child

    def collect(self, child, expected):
        out, err = child.communicate(timeout=60)
        self.assertEqual(child.returncode, expected, (out, err))
        return [json.loads(line) for line in out.splitlines()]

    def recover(self, mode, *, expected_state):
        events = self.collect(self.child(mode), 97)
        original = self.coordinator.record(self.attempt_id)
        self.assertEqual(None if original is None else original.state, expected_state)
        self.restart()
        result = self.submit()
        self.assert_single(result.run_id)
        if original is not None:
            self.assertEqual(result.request_json, original.request_json)
            self.assertEqual(result.submission_key, original.submission_key)
        for event in events:
            self.assertEqual(
                event.get("publicRunId", event.get("correlatedRunId")), result.run_id
            )
        return result

    def test_death_during_prepare_rolls_back(self):
        self.recover("during_prepare", expected_state=None)

    def test_death_after_key_before_dispatch(self):
        self.recover("prepared", expected_state="prepared")

    def test_death_after_dispatch_intent_before_sdk_call(self):
        self.recover("before_call", expected_state="dispatched")

    def test_death_after_accept_before_id(self):
        self.recover("after_accept", expected_state="dispatched")

    def test_death_after_accept_and_mutation_before_id(self):
        self.recover("after_accept_mutation", expected_state="dispatched")

    def test_death_during_correlation_write_rolls_back_only_id(self):
        self.recover("during_correlation", expected_state="dispatched")

    def test_death_after_correlation_commit(self):
        self.recover("after_correlation", expected_state="correlated")

    def race(self):
        gate = self.root / "go"
        children = [self.child("report", gate) for _ in range(4)]
        gate.touch()
        results = [self.collect(child, 0) for child in children]
        correlated = [events[-1]["correlatedRunId"] for events in results]
        self.assertEqual(len(set(correlated)), 1)
        # Calls are deliberately not serialized by SQLite. Native submission-key
        # idempotency makes every acknowledgment name the same run.
        public = [
            event["publicRunId"]
            for events in results
            for event in events
            if "publicRunId" in event
        ]
        self.assertTrue(public)
        self.assertEqual(set(public), {correlated[0]})
        self.assert_single(correlated[0])
        return correlated[0]

    def test_concurrent_first_submission(self):
        self.race()

    def test_concurrent_replay_after_mutated_worktree(self):
        accepted = self.collect(self.child("after_accept_mutation"), 97)[0][
            "publicRunId"
        ]
        # Conflict path does not print the normal acknowledgement marker.
        gate = self.root / "go"
        children = [self.child("report", gate) for _ in range(4)]
        gate.touch()
        events = [self.collect(child, 0) for child in children]
        self.assertTrue(
            all(result[-1]["correlatedRunId"] == accepted for result in events)
        )
        self.assert_single(accepted)


class GatewaySubmissionCrashTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        return replace(
            super().contract(work_unit, source, **overrides),
            required_effects=(
                RequiredEffect("deliver", "Open the PR.", "pull_request", "main"),
            ),
            host_assumptions=("single_host", "one_attempt_one_dedicated_worktree"),
        )

    def new_adapter(self):
        return configured_adapter(
            self.runtime_state,
            self.root,
            delivery_target_origin="http://127.0.0.1:8123",
            github_token="RECOVERY_GH_CANARY",
            gateway_base_url=V1_GATEWAY_BASE_URL,
            gateway_api_key="RECOVERY_GATEWAY_CANARY",
        )

    def test_death_after_gateway_accept_replays_with_current_ephemeral_credentials(self):
        child = subprocess.run(
            [
                sys.executable,
                str(CHILD),
                str(self.store_path),
                self.attempt_id,
                str(self.runtime_state),
                "gateway_after_accept",
            ],
            capture_output=True,
            text=True,
            timeout=60,
        )
        self.assertEqual(child.returncode, 97, (child.stdout, child.stderr))
        accepted = json.loads(child.stdout)["publicRunId"]
        original = self.coordinator.record(self.attempt_id)
        self.assertEqual(original.state, "dispatched")
        self.assertIsNone(original.run_id)
        self.restart()
        self.adapter.gateway_api_key = None
        with patch("zeroshot.Client") as construct:
            with self.assertRaisesRegex(UnsupportedRuntime, "GATEWAY_API_KEY"):
                self.coordinator.reconcile(self.attempt_id)
            construct.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id), original)
        self.adapter.gateway_api_key = "RECOVERY_GATEWAY_CANARY"
        client = MagicMock()
        client.submit = AsyncMock(return_value=SimpleNamespace(id=accepted))
        client.__aenter__ = AsyncMock(return_value=client)
        client.__aexit__ = AsyncMock(return_value=None)
        with patch("zeroshot.Client", return_value=client) as construct:
            recovered = self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(recovered.request_json, original.request_json)
        self.assertEqual(recovered.submission_key, original.submission_key)
        self.assert_single(accepted)
        environment = construct.call_args.kwargs["environment"]
        self.assertEqual(environment["GATEWAY_API_KEY"], "RECOVERY_GATEWAY_CANARY")
        self.assertEqual(environment["GH_TOKEN"], "RECOVERY_GH_CANARY")
        stored = "\n".join(self.store.connection.iterdump())
        for canary in (
            "CHILD_GH_CANARY",
            "CHILD_GATEWAY_CANARY",
            "RECOVERY_GH_CANARY",
            "RECOVERY_GATEWAY_CANARY",
            V1_GATEWAY_BASE_URL,
        ):
            self.assertNotIn(canary, stored + child.stdout + child.stderr)
