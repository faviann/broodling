"""Published #21 protocol may cease, but cannot resume as current assurance.

The JSON fixture is the canonical graph from cb9b9a6:broodling/assurance_graph.py.
It is retained test data, with no production import of historical qualification.
"""

import asyncio
import copy
import hashlib
import importlib.util
import json
import os
import signal
import unittest
from dataclasses import replace
from pathlib import Path
from unittest.mock import AsyncMock, patch

from retry_test_support import RetryCase
from submission_support import SubmissionCase

from broodling import AbandonmentCoordinator, CessationUnconfirmed, containment
from broodling.assurance_graph import assurance_graph, assurance_runtime, initial_state
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.errors import StaleAttempt, SubmissionConflict, UnsupportedRuntime
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import TerminalRunObservation, ZeroshotSubmitter


def published_graph():
    raw = (
        Path(__file__)
        .with_name("fixtures")
        .joinpath("issue21-assurance-graph.json")
        .read_bytes()
        .rstrip(b"\n")
    )
    assert hashlib.sha256(raw).hexdigest() == (
        "6619b045f12637e82f0127033c71851eb92718df530e99e99cb16ad0accbb034"
    )
    return json.loads(raw)


class StopProtocolCompatibilityTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        contract = super().contract(work_unit, source, **overrides)
        return replace(
            contract,
            criteria=tuple(
                replace(
                    item, mechanical_evidence=MechanicalEvidence(("/usr/bin/true",))
                )
                for item in contract.criteria
            ),
        )

    def setUp(self):
        super().setUp()
        executable = self.root / "controlled-codex"
        executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        executable.chmod(0o755)
        home, auth = self.root / "empty-home", self.root / "auth-only"
        home.mkdir()
        auth.mkdir()
        (auth / "auth.json").write_text("{}")
        self.adapter = ZeroshotSubmitter(
            self.runtime_state,
            codex_profile=QualifiedCodexProfile(executable, home, auth),
        )
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)
        self.admin = AbandonmentCoordinator(self.store, self.adapter)

    def legacy_submission(self, graph=None, runtime=None):
        # Generic P2 is a fixture admission seam, not new product authorization.
        with patch.object(self.adapter, "submit", return_value="published-run"):
            return self.coordinator.submit(
                self.attempt_id,
                graph=published_graph() if graph is None else graph,
                runtime=assurance_runtime() if runtime is None else runtime,
            )

    def stop(self):
        return asyncio.run(self.admin.stop(self.attempt_id, "old caller lost"))

    def test_published_graph_stop_retirement_preserves_request_and_no_semantics(self):
        original = self.legacy_submission()
        with (
            patch.object(
                self.adapter,
                "stop_known",
                AsyncMock(
                    return_value=TerminalRunObservation(
                        "published-run", False, "runtime_lost"
                    )
                ),
            ) as stop,
            patch.object(self.adapter, "submit") as dispatch,
        ):
            safe = self.stop()
            stop.assert_awaited_once_with(
                json.loads(original.request_json), "published-run"
            )
            self.assertTrue(containment.confirm_ceased(self.path))
            retired = self.admin.retire(self.attempt_id)
            self.assertIsNotNone(retired.retired_at)
            self.assertFalse(self.path.exists())
            self.assertEqual(self.coordinator.record(self.attempt_id), original)
            self.assertEqual(
                json.loads(safe.proof_json)["runtimeFailure"], "runtime_lost"
            )
            for operation in (
                lambda: self.coordinator.prepare_assurance(self.attempt_id),
                lambda: self.coordinator.reconcile(self.attempt_id),
            ):
                with self.assertRaises(StaleAttempt):
                    operation()
            dispatch.assert_not_called()
        self.assertEqual(
            self.store.connection.execute(
                "SELECT count(*) FROM final_assurance"
            ).fetchone()[0],
            0,
        )

    def test_old_graph_cannot_be_observed_or_reprepared_as_current_assurance(self):
        original = self.legacy_submission()
        with self.assertRaisesRegex(UnsupportedRuntime, "product protocol"):
            asyncio.run(
                self.adapter.observe_current(
                    json.loads(original.request_json), original.run_id
                )
            )
        with self.assertRaises(SubmissionConflict):
            self.coordinator.prepare_assurance(self.attempt_id)
        self.assertEqual(self.coordinator.record(self.attempt_id), original)

    def test_modified_published_graph_cannot_call_stop_or_retire(self):
        graph = published_graph()
        graph["root"]["children"][0]["timeoutMs"] += 1
        self.legacy_submission(graph=graph)
        with patch.object(self.adapter, "stop_known", AsyncMock()) as stop:
            with self.assertRaisesRegex(CessationUnconfirmed, "containment binding"):
                self.stop()
            stop.assert_not_called()
        self.assertIsNone(self.admin.record(self.attempt_id))
        with self.assertRaises(CessationUnconfirmed):
            self.admin.retire(self.attempt_id)

    def test_published_graph_still_requires_current_runtime_and_physical_cessation(
        self,
    ):
        self.legacy_submission()
        observed = TerminalRunObservation("published-run", False, "runtime_lost")
        with (
            patch.object(self.adapter, "stop_known", AsyncMock(return_value=observed)),
            patch.object(containment, "confirm_ceased", return_value=False),
            self.assertRaisesRegex(CessationUnconfirmed, "not fully ceased"),
        ):
            self.stop()
        self.assertIsNone(self.admin.record(self.attempt_id))

    def test_changed_runtime_blocks_published_graph_stop(self):
        runtime = copy.deepcopy(assurance_runtime())
        runtime["nodes"]["implement"]["model"] = "foreign-model"
        self.legacy_submission(runtime=runtime)
        with patch.object(self.adapter, "stop_known", AsyncMock()) as stop:
            with self.assertRaisesRegex(CessationUnconfirmed, "containment binding"):
                self.stop()
            stop.assert_not_called()


class RetryProtocolCompatibilityTests(RetryCase):
    def test_replacement_rejects_legacy_graph_and_legacy_initial_state(self):
        coordinator = self.retry_coordinator()
        attempt = coordinator.allocate(self.attempt_id, "fresh-retry")
        self.provisioner().provision(attempt.attempt_id)
        state = initial_state(
            self.revision.canonical_bytes.decode("utf-8"),
            attempt.b1_commit_oid,
            self.store.frozen_instructions(attempt.attempt_id),
        )
        old_state = {
            key: value for key, value in state.items() if key != "admittedInstructions"
        }
        for graph, value in (
            (published_graph(), state),
            (assurance_graph(), old_state),
        ):
            with (
                self.subTest(legacy_graph=graph != assurance_graph()),
                self.assertRaisesRegex(SubmissionConflict, "fresh product"),
            ):
                coordinator.submission.prepare(
                    attempt.attempt_id,
                    graph=graph,
                    runtime=assurance_runtime(),
                    initial_input=value,
                )
        self.assertIsNone(coordinator.submission.record(attempt.attempt_id))


@unittest.skipUnless(importlib.util.find_spec("zeroshot"), "install pinned SDK")
class PublicStopProtocolCompatibilityTests(unittest.TestCase):
    def test_published_graph_controller_loss_can_stop_and_retire_under_current_launcher(
        self,
    ):
        from final_assurance_support import final_case, paused
        from test_abandonment_public import controller_pid

        def legacy_state(*args, **kwargs):
            state = initial_state(*args, **kwargs)
            del state["admittedInstructions"]
            return state

        # Simulate a request admitted under #21, then restore the current product
        # before its controller is lost. Runtime/profile/launcher remain current.
        with (
            patch("broodling.assurance_graph.assurance_graph", published_graph),
            patch("broodling.assurance_graph.initial_state", legacy_state),
        ):
            context = final_case("clean", pause_node="implement")
            case = context.__enter__()
        try:
            self.assertEqual(case.request["graph"], published_graph())
            asyncio.run(paused(case))
            os.kill(controller_pid(case), signal.SIGKILL)
            admin = AbandonmentCoordinator(case.store, case.adapter)

            async def stop_after_loss():
                for _ in range(100):
                    try:
                        return await admin.stop(
                            case.attempt_id, "published controller lost"
                        )
                    except CessationUnconfirmed:
                        await asyncio.sleep(0.02)
                self.fail("published run did not cease")

            safe = asyncio.run(stop_after_loss())
            self.assertEqual(
                json.loads(safe.proof_json)["runtimeFailure"], "runtime_lost"
            )
            self.assertTrue(containment.confirm_ceased(case.path))
            self.assertIsNotNone(admin.retire(case.attempt_id).retired_at)
            self.assertFalse(case.path.exists())
        finally:
            context.__exit__(None, None, None)
