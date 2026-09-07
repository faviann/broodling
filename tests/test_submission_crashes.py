"""Crash windows retain one key/request/Attempt/run through process death."""

import json
import subprocess
import sys
from pathlib import Path

from submission_support import RealSubmissionCase

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

    def test_second_death_after_public_conflict_before_id(self):
        accepted = self.collect(self.child("after_accept_mutation"), 97)[0][
            "publicRunId"
        ]
        events = self.collect(self.child("after_conflict"), 97)
        self.assertEqual(events, [{"publicRunId": accepted}])
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "dispatched")
        self.restart()
        self.assertEqual(self.coordinator.reconcile(self.attempt_id).run_id, accepted)
        self.assert_single(accepted)

    def race(self):
        gate = self.root / "go"
        children = [self.child("report", gate) for _ in range(4)]
        gate.touch()
        results = [self.collect(child, 0) for child in children]
        correlated = [events[-1]["correlatedRunId"] for events in results]
        self.assertEqual(len(set(correlated)), 1)
        # SQLite serializes the actual SDK call as well as the winning write.
        self.assertEqual(
            sum("publicRunId" in event for events in results for event in events), 1
        )
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

    def test_accepted_graph_mutates_after_broodling_process_dies(self):
        import os
        import time
        from unittest.mock import patch

        from support import git

        from broodling.submission import SubmissionCoordinator
        from broodling.zeroshot_sdk import ZeroshotSubmitter

        fixture_bin = Path(__file__).parent / "fixtures" / "mutator-bin"
        with patch.dict(os.environ, {"PATH": f"{fixture_bin}:{os.environ['PATH']}"}):
            self.adapter = ZeroshotSubmitter(self.runtime_state)
            self.coordinator = SubmissionCoordinator(self.store, self.adapter)
            try:
                events = self.collect(self.child("graph_accept"), 97)
                accepted = events[0]["publicRunId"]
                self.assertEqual(git(self.path, "rev-parse", "HEAD"), self.b1)
                self.assertIsNone(self.coordinator.record(self.attempt_id).run_id)
                original = self.coordinator.record(self.attempt_id)
            finally:
                (self.path.parent / "allow-mutation").touch()
            deadline = time.monotonic() + 20
            while not (self.path / "mutation-finished").exists():
                self.assertLess(
                    time.monotonic(), deadline, "accepted graph did not mutate"
                )
                time.sleep(0.02)
            self.assertNotEqual(git(self.path, "rev-parse", "HEAD"), self.b1)
            self.restart()
            result = self.coordinator.reconcile(self.attempt_id)
            self.assertEqual(result.request_json, original.request_json)
            self.assert_single(accepted)
