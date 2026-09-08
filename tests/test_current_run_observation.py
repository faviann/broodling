"""The public observation seam never recovers a missed current occurrence."""

import asyncio
import copy
import sys
import unittest
from types import ModuleType, SimpleNamespace
from unittest.mock import Mock, patch

from broodling.assurance_graph import assurance_graph, assurance_runtime
from broodling.errors import UnsupportedRuntime
from broodling.zeroshot_sdk import ZeroshotSubmitter

RUN_ID = "already-correlated-run"
FINAL_CLEAN = "final_assessment_authority_clean"
FINAL_REPAIRED = "final_assessment_authority_repaired"


def status(*active, phase="running", result=None, run_id=RUN_ID, cursor="current"):
    return SimpleNamespace(
        run_id=run_id,
        phase=phase,
        cursor=cursor,
        active_executions=tuple(
            SimpleNamespace(node=node, execution=execution)
            for node, execution in active
        ),
        result=result,
    )


def finished(*, succeeded=True, run_id=RUN_ID):
    return status(
        phase="finished",
        result=SimpleNamespace(
            run_id=run_id,
            succeeded=succeeded,
            output={"finalRationale": [{"criterionId": "c1", "rationale": "met"}]},
        ),
    )


class PublicRun:
    """Only the supported current status and forward watch methods exist."""

    def __init__(self, current, following):
        self.current = current
        self.following = following
        self.after = None
        self.closed = False
        self.status_calls = 0

    async def status(self):
        self.status_calls += 1
        return self.current

    async def watch(self, *, after):
        self.after = after
        try:
            for item in self.following:
                if isinstance(item, BaseException):
                    raise item
                yield item
        finally:
            self.closed = True


class CurrentRunObservationTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        profile = Mock()
        profile.identity.return_value = {"qualified": True}
        profile.environment.return_value = {"PATH": "/qualified/bin"}
        self.adapter = ZeroshotSubmitter("/runtime-state", codex_profile=profile)
        self.request = {
            "target": copy.deepcopy(self.adapter.target),
            "workspace": "/current-attempt/worktree",
            "graph": assurance_graph(),
            "runtime": assurance_runtime(),
        }
        qualified = patch("broodling.zeroshot_sdk.assert_qualified_integration")
        self.qualified = qualified.start()
        self.addCleanup(qualified.stop)

    async def observe(self, current, following, *, request=None, run_id=RUN_ID):
        self.run = PublicRun(current, following)
        run = self.run

        class PublicClient:
            async def __aenter__(self):
                return self

            async def __aexit__(self, *args):
                pass

            def get_run(self, selected):
                self.selected = selected
                return run

        sdk = ModuleType("zeroshot")
        sdk.Client = Mock(return_value=PublicClient())
        sdk.LocalTarget = Mock(return_value="qualified-local-target")
        with patch.dict(sys.modules, zeroshot=sdk):
            result = await self.adapter.observe_current(
                self.request if request is None else request, run_id
            )
        sdk.LocalTarget.assert_called_once_with(
            "/current-attempt/worktree", state_dir=self.adapter.target["stateDir"]
        )
        sdk.Client.assert_called_once_with(
            target="qualified-local-target",
            environment=self.request["target"]["environment"],
        )
        return result

    async def test_clean_observation_binds_runtime_final_and_mutator_then_detaches(
        self,
    ):
        result = await self.observe(
            status(("implement", "mutation-c1")),
            [
                status((FINAL_CLEAN, "final-c1")),
                finished(),
                AssertionError("must detach"),
            ],
        )
        self.assertEqual(result.run_id, RUN_ID)
        self.assertEqual(result.final_node, FINAL_CLEAN)
        self.assertEqual(result.final_execution_id, "final-c1")
        self.assertEqual(result.mutation_node, "implement")
        self.assertEqual(result.mutation_execution_id, "mutation-c1")
        self.assertEqual(result.output, finished().result.output)
        self.assertEqual(self.run.after, "current")
        self.assertEqual(self.run.status_calls, 1)
        self.assertTrue(self.run.closed)

    async def test_repair_replaces_previous_generation_without_counting_or_history(
        self,
    ):
        result = await self.observe(
            status(("implement", "mutation-c1")),
            [
                status(("repair", "mutation-c2")),
                status(("repair", "mutation-c2")),
                status(("repair", "mutation-c3")),
                status((FINAL_REPAIRED, "final-c3")),
                status((FINAL_REPAIRED, "final-c3")),
                finished(),
            ],
        )
        self.assertEqual(result.mutation_execution_id, "mutation-c3")
        self.assertEqual(result.final_execution_id, "final-c3")

    async def test_initial_terminal_or_stopping_run_never_opens_history(self):
        for current in (
            finished(),
            finished(succeeded=False),
            status(phase="stopping"),
        ):
            with (
                self.subTest(phase=current.phase),
                self.assertRaises(UnsupportedRuntime),
            ):
                await self.observe(
                    current, [status((FINAL_CLEAN, "old-final")), finished()]
                )
            self.assertIsNone(self.run.after)

    async def test_missing_mutation_final_or_exact_designation_cannot_capture(self):
        cases = (
            (status(), [status((FINAL_CLEAN, "final")), finished()]),
            (status(("implement", "mutation")), [finished()]),
            (
                status(("implement", "mutation")),
                [status((FINAL_CLEAN + "_lookalike", "forged")), finished()],
            ),
            (
                status(("implement", "mutation")),
                [status((FINAL_REPAIRED, "final")), finished()],
            ),
        )
        for current, following in cases:
            with (
                self.subTest(following=following),
                self.assertRaises(UnsupportedRuntime),
            ):
                await self.observe(current, following)

    async def test_failed_interrupted_or_ambiguous_final_has_no_result(self):
        cases = (
            [status((FINAL_CLEAN, "final")), finished(succeeded=False)],
            [status((FINAL_CLEAN, "final"))],
            [status((FINAL_CLEAN, "final")), status(phase="stopping")],
            [
                status((FINAL_CLEAN, "final")),
                status(("repair", "late-mutation")),
                finished(),
            ],
            [
                status((FINAL_CLEAN, "final")),
                status((FINAL_CLEAN, "other-final")),
                finished(),
            ],
            [status(("repair", "mutation"), (FINAL_REPAIRED, "final")), finished()],
            [status((FINAL_CLEAN, "")), finished()],
            [status((FINAL_CLEAN, "final")), status(phase="finished")],
            [
                status(
                    (FINAL_CLEAN, "terminal-only-final"),
                    phase="finished",
                    result=finished().result,
                )
            ],
        )
        for following in cases:
            with (
                self.subTest(following=following),
                self.assertRaises(UnsupportedRuntime),
            ):
                await self.observe(status(("implement", "mutation")), following)
            self.assertTrue(self.run.closed)

    async def test_cancellation_and_transport_loss_detach_without_status_fallback(self):
        for error in (asyncio.CancelledError(), OSError("transport interrupted")):
            with self.subTest(error=error), self.assertRaises(type(error)):
                await self.observe(
                    status(("implement", "mutation")),
                    [status((FINAL_CLEAN, "final")), error],
                )
            self.assertTrue(self.run.closed)
            self.assertEqual(self.run.status_calls, 1)

    async def test_status_and_result_must_belong_to_correlated_run(self):
        for following in (
            [status((FINAL_CLEAN, "final"), run_id="another-run"), finished()],
            [status((FINAL_CLEAN, "final")), finished(run_id="another-run")],
        ):
            with (
                self.subTest(following=following),
                self.assertRaises(UnsupportedRuntime),
            ):
                await self.observe(status(("implement", "mutation")), following)

    async def test_nonproduct_graph_runtime_target_or_missing_run_id_are_refused(self):
        for key in ("graph", "runtime", "target"):
            request = copy.deepcopy(self.request)
            request[key] = {}
            with self.subTest(key=key), self.assertRaises(UnsupportedRuntime):
                await self.observe(status(), [], request=request)
        with self.assertRaises(UnsupportedRuntime):
            await self.observe(status(), [], run_id="")
        self.qualified.assert_not_called()

    async def test_missing_cursor_never_falls_back_to_unbounded_watch(self):
        with self.assertRaises(UnsupportedRuntime):
            await self.observe(status(cursor=""), [])
        self.assertIsNone(self.run.after)
