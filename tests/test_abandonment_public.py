"""Current product stop/loss windows on the pinned SDK and new containment.

Semantic leaves are controlled timing fixtures; actual provider confinement is
separately retained by qualification/v1-p4/issue21_provider.py.
"""

import asyncio
import importlib.util
import json
import os
import signal
import subprocess
import sys
import time
import unittest
from pathlib import Path
from typing import ClassVar
from unittest.mock import patch

from final_assurance_support import final_case, observe_released

from broodling import (
    AbandonmentCoordinator,
    BroodlingStore,
    CessationUnconfirmed,
    FinalAssuranceCoordinator,
    containment,
)
from broodling.errors import StaleAttempt


async def paused(case):
    from zeroshot import Client, LocalTarget

    case.release()
    async with Client(
        target=LocalTarget(case.path, state_dir=case.run_root / "native"),
        environment=case.request["target"]["environment"],
    ) as client:
        deadline = time.monotonic() + 40
        while not (case.state / "paused").exists():
            status = await client.get_run(case.row.run_id).status()
            if status.result is not None:
                raise AssertionError(f"run ended before pause: {status.result}")
            if time.monotonic() > deadline:
                raise AssertionError("control did not reach requested pause")
            await asyncio.sleep(0.05)
        return await client.get_run(case.row.run_id).status()


def controller_pid(case):
    # Test-only fault injection from exact bootstrap argv, never product runtime discovery.
    expected = str(
        case.run_root
        / "native"
        / "runs"
        / case.row.run_id
        / "controller.bootstrap.json"
    ).encode()
    found = []
    for path in Path("/proc").iterdir():
        if not path.name.isdigit():
            continue
        try:
            args = (path / "cmdline").read_bytes().split(b"\0")
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
        if (
            args[1:3] == [b"__zeroshot-run-controller", b"--bootstrap"]
            and expected in args
        ):
            found.append(int(path.name))
    if len(found) != 1:
        raise AssertionError(f"expected one exact fixture controller, found {found}")
    return found[0]


@unittest.skipUnless(importlib.util.find_spec("zeroshot"), "install pinned SDK")
class PublicAbandonmentTests(unittest.TestCase):
    control_records: ClassVar[dict] = {}

    def retain(self, case, name, retired, **extra):
        self.control_records[name] = {
            "request": case.request,
            "runId": case.row.run_id,
            "attemptId": case.attempt_id,
            "events": case.events(),
            "abandonment": vars(case.store.abandonment(case.attempt_id))
            if hasattr(case.store.abandonment(case.attempt_id), "__dict__")
            else {
                "reason": case.store.abandonment(case.attempt_id).reason,
                "abandonedAt": case.store.abandonment(case.attempt_id).abandoned_at,
            },
            "cessationProof": json.loads(retired.proof_json),
            "retiredAt": retired.retired_at,
            "worktreeAbsent": not case.path.exists(),
            **extra,
        }

    def test_stop_during_mutation_and_adjudication(self):
        for scenario, node in (("repair", "repair"), ("clean", "adjudicate_authority")):
            with self.subTest(node=node), final_case(scenario, pause_node=node) as case:
                status = asyncio.run(paused(case))
                self.assertTrue(
                    any(item.node == node for item in status.active_executions)
                )
                administrator = AbandonmentCoordinator(case.store, case.adapter)
                safe = asyncio.run(
                    administrator.stop(case.attempt_id, f"stop during {node}")
                )
                self.assertEqual(
                    json.loads(safe.proof_json)["runtimeFailure"], "force_stopped"
                )
                retired = administrator.retire(case.attempt_id)
                self.assertFalse(case.path.exists())
                self.assertEqual(
                    asyncio.run(administrator.stop(case.attempt_id, "late stop")),
                    retired,
                )
                with self.assertRaises(StaleAttempt):
                    case.coordinator.reconcile(case.attempt_id)
                self.retain(case, node, retired)

    def test_controller_death_uses_physical_cessation_before_retirement(self):
        with final_case("repair", pause_node="repair") as case:
            asyncio.run(paused(case))
            pid = controller_pid(case)
            os.kill(pid, signal.SIGKILL)
            administrator = AbandonmentCoordinator(case.store, case.adapter)

            async def stop_after_loss():
                # This is bounded administrative observation, not semantic replay.
                for _ in range(100):
                    try:
                        return await administrator.stop(
                            case.attempt_id, "controller lost"
                        )
                    except CessationUnconfirmed:
                        await asyncio.sleep(0.02)
                raise AssertionError("namespace did not cease")

            safe = asyncio.run(stop_after_loss())
            self.assertEqual(
                json.loads(safe.proof_json)["runtimeFailure"], "runtime_lost"
            )
            self.assertTrue(containment.confirm_ceased(case.path))
            retired = administrator.retire(case.attempt_id)
            self.retain(case, "controller_loss", retired, killedController=pid)

    def test_concurrent_stop_callers_converge_after_public_stop(self):
        from zeroshot.errors import TargetError

        with final_case("repair", pause_node="repair") as case:
            asyncio.run(paused(case))
            with BroodlingStore.open(case.store.path) as other_store:
                first = AbandonmentCoordinator(case.store, case.adapter)
                second = AbandonmentCoordinator(other_store, case.adapter)
                original = case.adapter.stop_known

                async def simultaneous():
                    ready = asyncio.Event()
                    entered = 0

                    async def overlapping(*args):
                        nonlocal entered
                        entered += 1
                        if entered == 2:
                            ready.set()
                        await asyncio.wait_for(ready.wait(), timeout=10)
                        return await original(*args)

                    with patch.object(case.adapter, "stop_known", overlapping):
                        return await asyncio.wait_for(
                            asyncio.gather(
                                first.stop(case.attempt_id, "concurrent first"),
                                second.stop(case.attempt_id, "concurrent second"),
                                return_exceptions=True,
                            ),
                            timeout=45,
                        )

                observations = asyncio.run(simultaneous())
                errors = []
                for observation in observations:
                    if isinstance(observation, BaseException):
                        # The pinned controller exits after terminal publication
                        # without draining all concurrent RPC replies. Preserve
                        # this known acknowledgment loss; do not treat it as proof.
                        self.assertIsInstance(observation, TargetError)
                        self.assertIn("transport disconnected", str(observation))
                        errors.append(str(observation))
                self.assertIsNotNone(case.store.abandonment(case.attempt_id))
                self.assertFalse(case.store.get_attempt(case.attempt_id).is_current)
                if first.record(case.attempt_id) is None:
                    with self.assertRaises(CessationUnconfirmed):
                        first.retire(case.attempt_id)
                records = [
                    asyncio.run(owner.stop(case.attempt_id, "explicit repeat"))
                    for owner in (first, second)
                ]
                self.assertEqual(records[0], records[1])
                for observation in observations:
                    if not isinstance(observation, BaseException):
                        self.assertEqual(observation, records[0])
                retired = first.retire(case.attempt_id)
                self.assertEqual(second.retire(case.attempt_id), retired)
                self.assertFalse(case.path.exists())
                self.retain(
                    case,
                    "concurrent_stop",
                    retired,
                    overlappingCallers=2,
                    publicAcknowledgmentErrors=errors,
                    explicitRepeatsConverged=True,
                )

    def test_caller_process_loss_after_directive_and_after_final_custody(self):
        for action, pause in (("after-directive", "repair"), ("after-final", None)):
            with (
                self.subTest(action=action),
                final_case("repair", final_materials=True, pause_node=pause) as case,
            ):
                if pause:
                    asyncio.run(paused(case))
                completed = subprocess.run(
                    [
                        sys.executable,
                        str(Path(__file__).with_name("abandonment_caller_child.py")),
                        str(case.store.path),
                        case.attempt_id,
                        str(case.state),
                        action,
                    ],
                    capture_output=True,
                    check=False,
                    text=True,
                    timeout=90,
                )
                self.assertEqual(
                    completed.returncode, 75 if pause else 76, completed.stderr
                )
                capture = FinalAssuranceCoordinator(case.store, case.adapter)
                custody = capture.record(case.attempt_id)
                self.assertEqual(custody is None, bool(pause))
                administrator = AbandonmentCoordinator(case.store, case.adapter)
                asyncio.run(
                    administrator.stop(case.attempt_id, action + " caller process died")
                )
                retired = administrator.retire(case.attempt_id)
                self.assertFalse(case.path.exists())
                self.assertEqual(capture.record(case.attempt_id), custody)
                with self.assertRaises(StaleAttempt):
                    asyncio.run(capture.capture(case.attempt_id))
                self.retain(case, action, retired, callerExit=completed.returncode)

    def test_final_runtime_success_and_custody_are_abandoned_without_disposition(self):
        with final_case("repair", final_materials=True) as case:
            capture = FinalAssuranceCoordinator(case.store, case.adapter)
            record = asyncio.run(
                observe_released(case, capture.capture(case.attempt_id))
            )
            administrator = AbandonmentCoordinator(case.store, case.adapter)
            safe = asyncio.run(
                administrator.stop(case.attempt_id, "caller lost before disposition")
            )
            self.assertTrue(json.loads(safe.proof_json)["runtimeSucceeded"])
            retired = administrator.retire(case.attempt_id)
            self.assertEqual(capture.record(case.attempt_id), record)
            with self.assertRaises(StaleAttempt):
                asyncio.run(capture.capture(case.attempt_id))
            self.retain(
                case, "after_final_custody", retired, historicalCustody=record.material
            )

    def test_process_death_before_stop_and_after_terminal_observation(self):
        for action, expected in (("before-stop", 77), ("after-stop", 78)):
            with (
                self.subTest(action=action),
                final_case("clean", pause_node="adjudicate_authority") as case,
            ):
                asyncio.run(paused(case))
                completed = subprocess.run(
                    [
                        sys.executable,
                        str(Path(__file__).with_name("abandonment_caller_child.py")),
                        str(case.store.path),
                        case.attempt_id,
                        str(case.state),
                        action,
                    ],
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=45,
                )
                self.assertEqual(completed.returncode, expected, completed.stderr)
                self.assertIsNotNone(case.store.abandonment(case.attempt_id))
                administrator = AbandonmentCoordinator(case.store, case.adapter)
                self.assertIsNone(administrator.record(case.attempt_id))
                with self.assertRaises(CessationUnconfirmed):
                    administrator.retire(case.attempt_id)
                asyncio.run(
                    administrator.stop(case.attempt_id, "repeat after caller death")
                )
                retired = administrator.retire(case.attempt_id)
                self.assertFalse(case.path.exists())
                self.retain(case, action, retired, callerExit=completed.returncode)
