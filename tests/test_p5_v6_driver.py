"""V6 refuses unsafe starts and retains the detached/completed boundary proof."""

import asyncio
from contextlib import ExitStack
from dataclasses import replace
import json
import os
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import AsyncMock, MagicMock, patch

from broodling import RequiredEffect, WorkUnitDispositionCoordinator
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL
from evaluation.p5 import verify_direct_target_gh as gh_check
from evaluation.p5.v6 import run_r01 as driver, setup_r01 as setup
from submission_support import SubmissionCase, configured_adapter
from test_direct_target_gh import NEW_VERSION, OLD_VERSION, installed_gh


class P5V6StartTests(unittest.TestCase):
    def prepare(self, stack):
        root = Path(stack.enter_context(tempfile.TemporaryDirectory()))
        state, evidence, retained = (root / name for name in ("state", "evidence", "setup"))
        for name, path in (("STATE", state), ("EVIDENCE", evidence), ("SETUP", retained)):
            path.mkdir()
            stack.enter_context(patch.object(driver, name, path))
        (retained / "SHA256SUMS").write_text("")
        for path in (retained, evidence):
            (path / "slots.json").write_text(json.dumps(setup.slots()))
        (evidence / "pre-admission-verification.json").write_text(json.dumps({
            "ready_for_separate_live_authorization": True, "issue": 83,
            "protocol": driver.PROTOCOL, "freeze_commit": driver.FREEZE,
            "baseline_commit": driver.BASELINE,
        }))
        (evidence / "target-preparation.json").write_text(json.dumps({
            "container_id": "actual-v6-container",
        }))
        for target in ("write", "BroodlingStore.open", "AttemptProvisioner.admit_and_provision",
                       "SubmissionCoordinator.submit"):
            stack.enter_context(patch(f"{driver.__name__}.{target}",
                                      side_effect=AssertionError("admission/effect is forbidden")))
        return root, state, evidence

    def test_actual_start_refuses_incompatible_cli_before_count_or_admission(self):
        with ExitStack() as stack:
            root, state, evidence = self.prepare(stack)
            before = {path: path.read_bytes() for path in root.rglob("*.json")}
            run = stack.enter_context(patch.object(
                gh_check.subprocess, "run", side_effect=installed_gh(OLD_VERSION, slurp=False),
            ))
            with self.assertRaises(gh_check.IncompatibleGitHubCLI):
                driver.start()
            self.assertEqual({path: path.read_bytes() for path in root.rglob("*.json")}, before)
            self.assertTrue(all(call.args[0][:3] == ("docker", "exec", "actual-v6-container")
                                for call in run.call_args_list))
            self.assertFalse((evidence / "R01/execution-start.json").exists())
            self.assertFalse((state / "broodling.sqlite3").exists())

    def test_capable_but_unpinned_cli_is_refused_before_start(self):
        with ExitStack() as stack:
            root, _, _ = self.prepare(stack)
            before = {path: path.read_bytes() for path in root.rglob("*.json")}
            stack.enter_context(patch.object(
                gh_check.subprocess, "run",
                side_effect=installed_gh(NEW_VERSION.replace("2.101.0", "2.102.0"), slurp=True),
            ))
            with self.assertRaisesRegex(driver.Refusal, "pinned image"):
                driver.start()
            self.assertEqual({path: path.read_bytes() for path in root.rglob("*.json")}, before)

    def test_consumed_slot_is_refused_before_target_or_admission(self):
        with ExitStack() as stack:
            _, _, evidence = self.prepare(stack)
            (evidence / "R01").mkdir()
            (evidence / "R01/execution-start.json").write_text("{}")
            stack.enter_context(patch.object(driver, "verify_gh", side_effect=AssertionError))
            with self.assertRaisesRegex(driver.Refusal, "already recorded"):
                driver.start()

    def test_completed_observation_refuses_active_run_and_uses_persisted_origin(self):
        request = {"target": {"deliveryTargetOrigin": "http://127.0.0.1:19999"}}
        with patch("zeroshot.Client") as client:
            client.return_value.__aenter__.return_value = MagicMock()
            handle = client.return_value.__aenter__.return_value.get_run.return_value
            handle.status = AsyncMock(return_value=SimpleNamespace(
                run_id="run", phase="running", result=None,
            ))
            with self.assertRaisesRegex(driver.Refusal, "already-completed"):
                asyncio.run(driver.completed_observation(request, "run"))
            self.assertEqual(client.call_args.kwargs["environment"], {})
            self.assertEqual(client.call_args.kwargs["target"].origin,
                             request["target"]["deliveryTargetOrigin"])
            handle.status.return_value = SimpleNamespace(
                run_id="run", phase="finished", result=object(), cursor="terminal-cursor",
            )
            observed = asyncio.run(driver.completed_observation(request, "run"))
            self.assertTrue(observed["result_already_present"])
            self.assertEqual(observed["cursor"], "terminal-cursor")


class P5V6ReconnectionTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        return replace(
            super().contract(work_unit, source, **overrides),
            required_effects=(RequiredEffect("deliver", "Open the PR.", "pull_request", "main"),),
            host_assumptions=("single_host", "one_attempt_one_dedicated_worktree"),
        )

    def new_adapter(self):
        return configured_adapter(
            self.runtime_state, self.root, delivery_target_origin=driver.TARGET,
            github_token="token", gateway_base_url=V1_GATEWAY_BASE_URL,
            gateway_api_key="provider-key",
        )

    def test_detached_completed_consumption_precedes_atomic_disposition_and_reopened_replay(self):
        from zeroshot import RunResult

        with patch.object(self.adapter, "submit", return_value="retained-run"):
            self.submit()
        evidence = self.root / "v6-evidence"
        (evidence / "R01").mkdir(parents=True)
        (evidence / "slots.json").write_text(json.dumps(setup.slots()))
        (evidence / "R01/execution-start.json").write_text(json.dumps({"caller_pid": -1}))
        receipt = {"version": "v1", "mode": "pr", "outcome": "opened",
                   "repository": "faviann/broodling", "targetBranch": "main",
                   "headRevision": "b" * 40, "pullRequestId": "50"}
        result = RunResult(run_id="retained-run", succeeded=True, output=receipt)
        records = {"store_path": str(self.store_path)}
        events = []

        async def completed(request, run_id):
            self.assertIsNone(WorkUnitDispositionCoordinator(self.store, self.adapter).record(self.attempt_id))
            events.append("already-completed")
            return {"run_id": run_id, "phase": "finished", "result_already_present": True}

        async def consume(request, run_id):
            self.assertIsNone(WorkUnitDispositionCoordinator(self.store, self.adapter).record(self.attempt_id))
            events.append("detached-consumption")
            return result

        with ExitStack() as stack:
            stack.enter_context(patch.object(driver, "EVIDENCE", evidence))
            stack.enter_context(patch.object(driver, "STATE", self.root / "v6-state"))
            stack.enter_context(patch.object(driver, "inputs", return_value=records))
            stack.enter_context(patch.dict(os.environ, {}, clear=True))
            stack.enter_context(patch.object(driver, "completed_observation", side_effect=completed))
            wait = stack.enter_context(patch.object(driver.ZeroshotSubmitter, "wait", side_effect=consume))
            submit = stack.enter_context(patch.object(driver.ZeroshotSubmitter, "submit", side_effect=AssertionError))
            driver.reconnect("finalize")
            driver.replay()
        self.assertEqual(events, ["already-completed", "detached-consumption"])
        self.assertEqual(wait.call_count, 1)
        submit.assert_not_called()
        boundary = json.loads((evidence / "R01/detached-completed-result.json").read_text())
        self.assertTrue(boundary["first_disposition_absent"])
        replay = json.loads((evidence / "R01/reopened-store-replay.json").read_text())
        self.assertTrue(replay["identical_disposition"])
        self.assertTrue(replay["identical_retained_result"])
        self.assertEqual(replay["native_waits"], 0)
        self.assertEqual(replay["disposition"]["result_json"],
                         WorkUnitDispositionCoordinator(self.store, self.adapter).record(self.attempt_id).result_json)
        for table in ("final_assurance", "work_unit_dispositions"):
            self.assertEqual(self.store.connection.execute(f"SELECT count(*) FROM {table}").fetchone()[0], 1)
