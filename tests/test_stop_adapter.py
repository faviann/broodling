"""Administrative public stop never dispatches or establishes writer cessation."""

import copy
import sys
import unittest
from types import ModuleType, SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

from broodling.errors import UnsupportedRuntime
from broodling.zeroshot_sdk import TerminalRunObservation, ZeroshotSubmitter


class StopAdapterTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.profile = Mock()
        self.profile.identity.return_value = {"qualified": True}
        self.profile.environment.return_value = {"PATH": "/qualified/bin"}
        self.adapter = ZeroshotSubmitter("/runtime-state", codex_profile=self.profile)
        self.request = {
            "target": copy.deepcopy(self.adapter.target),
            "workspace": "/abandoned-attempt/worktree",
        }
        self.result = SimpleNamespace(
            run_id="known-run", succeeded=False, failure="force_stopped"
        )
        self.status = SimpleNamespace(
            run_id="known-run",
            phase="finished",
            active_executions=(),
            result=self.result,
        )
        self.run = SimpleNamespace(
            force_stop=AsyncMock(return_value=self.result),
            status=AsyncMock(return_value=self.status),
        )
        run = self.run
        self.selected = []
        selected = self.selected

        class Client:
            async def __aenter__(self):
                return self

            async def __aexit__(self, *args):
                pass

            def get_run(self, run_id):
                selected.append(run_id)
                return run

        self.sdk = ModuleType("zeroshot")
        self.sdk.Client = Mock(return_value=Client())
        self.sdk.LocalTarget = Mock(return_value="target")
        modules = patch.dict(sys.modules, zeroshot=self.sdk)
        modules.start()
        self.addCleanup(modules.stop)
        integration = patch("broodling.zeroshot_sdk.assert_qualified_integration")
        self.integration = integration.start()
        self.addCleanup(integration.stop)

    async def stop(self):
        return await self.adapter.stop_known(self.request, "known-run")

    async def test_repeated_stop_observes_same_run_without_dispatch_or_history(self):
        first = await self.stop()
        self.assertEqual(first, await self.stop())
        self.assertEqual(
            first, TerminalRunObservation("known-run", False, "force_stopped")
        )
        self.assertEqual(self.selected, ["known-run", "known-run"])
        self.assertEqual(self.run.force_stop.await_count, 2)
        self.assertEqual(self.run.status.await_count, 2)
        self.sdk.LocalTarget.assert_called_with(
            self.request["workspace"], state_dir=self.adapter.target["stateDir"]
        )
        self.sdk.Client.assert_called_with(
            target="target", environment=self.request["target"]["environment"]
        )

    async def test_runtime_loss_is_only_a_diagnostic_not_a_cessation_fact(self):
        self.result.failure = "runtime_lost"
        self.assertEqual(
            await self.stop(),
            TerminalRunObservation("known-run", False, "runtime_lost"),
        )

    async def test_existing_runtime_success_does_not_recover_semantic_output(self):
        self.result.succeeded = True
        self.result.failure = None
        self.result.output = {"abandonedAcceptance": "must not cross this boundary"}
        observed = await self.stop()
        self.assertEqual(observed, TerminalRunObservation("known-run", True, None))
        self.assertFalse(hasattr(observed, "output"))
        self.assertFalse(hasattr(observed, "safe_to_retire"))

    async def test_inaccessible_stop_or_status_propagates_without_observation(self):
        for method in (self.run.force_stop, self.run.status):
            with self.subTest(method=method):
                method.side_effect = OSError("runtime inaccessible")
                with self.assertRaisesRegex(OSError, "runtime inaccessible"):
                    await self.stop()
                method.side_effect = None

    async def test_wrong_target_or_changed_profile_cannot_call_runtime(self):
        self.request["target"]["stateDir"] = "/other-runtime"
        with self.assertRaises(UnsupportedRuntime):
            await self.stop()
        self.request["target"] = copy.deepcopy(self.adapter.target)
        self.profile.identity.return_value = {"qualified": "changed"}
        with self.assertRaises(UnsupportedRuntime):
            await self.stop()
        self.sdk.Client.assert_not_called()

    async def test_missing_identity_or_unqualified_sdk_cannot_call_runtime(self):
        for run_id in (None, "", "  ", 3):
            with self.subTest(run_id=run_id), self.assertRaises(UnsupportedRuntime):
                await self.adapter.stop_known(self.request, run_id)
        self.integration.side_effect = UnsupportedRuntime("changed SDK")
        with self.assertRaisesRegex(UnsupportedRuntime, "changed SDK"):
            await self.stop()
        self.sdk.Client.assert_not_called()

    async def test_nonterminal_wrong_run_or_inconsistent_results_fail_closed(self):
        cases = (
            {"phase": "running"},
            {"active_executions": ("surviving-execution",)},
            {"run_id": "other-run"},
            {"result": None},
            {
                "result": SimpleNamespace(
                    run_id="other-run", succeeded=False, failure="force_stopped"
                )
            },
            {
                "result": SimpleNamespace(
                    run_id="known-run", succeeded=False, failure="runtime_lost"
                )
            },
        )
        for overrides in cases:
            with self.subTest(overrides=overrides):
                self.run.status.return_value = SimpleNamespace(
                    **(vars(self.status) | overrides)
                )
                with self.assertRaises(UnsupportedRuntime):
                    await self.stop()

    async def test_profileless_p2_target_can_stop_but_gains_no_safety_authority(self):
        self.adapter = ZeroshotSubmitter("/runtime-state")
        self.request["target"] = copy.deepcopy(self.adapter.target)
        self.assertEqual(
            await self.stop(),
            TerminalRunObservation("known-run", False, "force_stopped"),
        )


if __name__ == "__main__":
    unittest.main()
