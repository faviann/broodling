"""Broodling decisions around the supported native workflow/result seam."""

import asyncio
import json
import sqlite3
from dataclasses import replace
from unittest.mock import AsyncMock, patch

from submission_support import RealSubmissionCase, SubmissionCase
from zeroshot import RunResult

from broodling import (
    AbandonmentCoordinator,
    BroodlingStore,
    CessationUnconfirmed,
    Criterion,
    RequiredEffect,
    StaleAttempt,
    SubmissionConflict,
    SubmissionNotReady,
    WorkUnitDispositionCoordinator,
)


class ResultTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        return replace(
            super().contract(work_unit, source, **overrides),
            required_effects=(
                RequiredEffect(
                    "deliver-pr",
                    "Open a pull request against main.",
                    "pull_request",
                    "main",
                ),
            ),
            host_assumptions=("single_host", "one_attempt_one_dedicated_worktree"),
        )

    def new_adapter(self):
        from submission_support import configured_adapter

        return configured_adapter(
            self.runtime_state,
            self.root,
            delivery_target_origin="http://127.0.0.1:8123",
            github_token="test-github-token",
        )

    def setUp(self):
        super().setUp()
        with patch.object(self.adapter, "submit", return_value="native-run"):
            self.submitted = self.submit()
        self.disposition = WorkUnitDispositionCoordinator(self.store, self.adapter)
        self.receipt = {
            "version": "v1",
            "mode": "pr",
            "outcome": "opened",
            "repository": "faviann/broodling",
            "targetBranch": "main",
            "headRevision": "b" * 40,
            "pullRequestId": "50",
        }
        self.result = RunResult(
            run_id="native-run", succeeded=True, output=self.receipt
        )

    def finalize(self):
        return asyncio.run(self.disposition.finalize(self.attempt_id))

    def test_native_pr_receipt_is_the_stable_successful_result(self):
        with patch.object(
            self.adapter, "wait", AsyncMock(return_value=self.result)
        ) as wait:
            record = self.finalize()
            self.assertEqual(self.finalize(), record)
        self.assertEqual(wait.await_count, 1)
        self.assertEqual(record.outcome, "SUCCEEDED")
        self.assertEqual(record.result["runId"], self.result.run_id)
        self.assertEqual(record.result["acceptedRevision"], "b" * 40)
        self.assertEqual(record.result["deliveryReceipt"], self.receipt)
        self.assertNotIn("workspace", record.result)
        self.assertTrue(self.path.is_dir())
        self.assertIsNone(self.store.current_attempt(self.attempt.work_unit_id))
        self.restart()
        disposition = WorkUnitDispositionCoordinator(self.store, self.adapter)
        with patch.object(
            self.adapter, "wait", side_effect=AssertionError("already retained")
        ):
            self.assertEqual(asyncio.run(disposition.finalize(self.attempt_id)), record)
        self.assertEqual(disposition.justification(self.attempt_id), record.result)

    def test_request_freezes_the_contract_and_selects_authorized_native_delivery(
        self,
    ):
        request = json.loads(self.submitted.request_json)
        task = json.loads(request["task"].split("\n\n", 1)[1])
        self.assertEqual(task["contract"], json.loads(self.revision.canonical_bytes))
        self.assertEqual(
            task["admittedInstructions"],
            self.store.frozen_instructions(self.attempt_id),
        )
        self.assertEqual(task["comparisonBase"], self.attempt.b1_commit_oid)
        self.assertEqual(
            request["preset"],
            {"name": "software-change", "delivery": "pull_request"},
        )
        self.assertEqual(
            request["delivery"],
            {
                "repository": "faviann/broodling",
                "targetBranch": "main",
                "baseRevision": self.attempt.b1_commit_oid,
            },
        )
        self.assertNotIn("test-github-token", self.submitted.request_json)
        self.assertEqual(request["target"]["deliveryCredential"], "GH_TOKEN")
        self.assertEqual(request["runtime"]["session_scope"], "execution")
        self.assertNotIn("connections", request["runtime"])

    def test_malformed_or_mismatched_delivery_receipt_fails_closed(self):
        variants = (
            None,
            {**self.receipt, "mode": "merge"},
            {**self.receipt, "repository": "other/project"},
            {**self.receipt, "targetBranch": "release"},
            {**self.receipt, "headRevision": self.attempt.b1_commit_oid},
            {**self.receipt, "extra": "field"},
        )
        for output in variants:
            with (
                self.subTest(output=output),
                patch.object(
                    self.adapter,
                    "wait",
                    AsyncMock(return_value=replace(self.result, output=output)),
                ),
                self.assertRaises(SubmissionConflict),
            ):
                self.finalize()
        self.assertIsNone(self.disposition.record(self.attempt_id))

    def test_foreign_run_result_cannot_complete(self):
        with (
            patch.object(
                self.adapter,
                "wait",
                AsyncMock(return_value=replace(self.result, run_id="foreign")),
            ),
            self.assertRaises(SubmissionConflict),
        ):
            self.finalize()
        self.assertIsNone(self.disposition.record(self.attempt_id))
        self.store.require_current_attempt(self.attempt_id)

    def test_native_failure_abandons_without_claiming_cleanup(self):
        with (
            patch.object(
                self.adapter,
                "wait",
                AsyncMock(
                    return_value=RunResult(
                        run_id="native-run",
                        succeeded=False,
                        failure="runtime_lost",
                    )
                ),
            ),
            self.assertRaisesRegex(SubmissionNotReady, "runtime_lost"),
        ):
            self.finalize()
        self.assertIsNotNone(self.store.abandonment(self.attempt_id))
        self.assertIsNone(self.disposition.record(self.attempt_id))
        self.assertIsNone(
            AbandonmentCoordinator(self.store, self.adapter).record(self.attempt_id)
        )
        self.assertTrue(self.path.is_dir())

    def test_abandonment_while_waiting_refuses_late_success(self):
        async def late(*args):
            self.store.abandon_attempt(self.attempt_id, "explicit stop")
            return self.result

        with (
            patch.object(self.adapter, "wait", side_effect=late),
            self.assertRaises(StaleAttempt),
        ):
            self.finalize()
        self.assertIsNone(self.disposition.record(self.attempt_id))

    def test_cancelled_or_unavailable_wait_can_be_repeated_for_the_same_attempt(self):
        for error in (asyncio.CancelledError(), OSError("transport unavailable")):
            with self.subTest(error=type(error).__name__):
                with (
                    patch.object(self.adapter, "wait", side_effect=error),
                    self.assertRaises(type(error)),
                ):
                    self.finalize()
                self.store.require_current_attempt(self.attempt_id)
                self.assertIsNone(self.disposition.record(self.attempt_id))
        with patch.object(self.adapter, "wait", AsyncMock(return_value=self.result)):
            self.assertEqual(self.finalize().outcome, "SUCCEEDED")

    def test_result_and_disposition_are_atomic_and_retryable_after_write_failure(self):
        self.store.connection.execute(
            "CREATE TRIGGER fixture_failure BEFORE INSERT ON work_unit_dispositions "
            "BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END"
        )
        with patch.object(self.adapter, "wait", AsyncMock(return_value=self.result)):
            with self.assertRaises(sqlite3.IntegrityError):
                self.finalize()
            self.assertEqual(
                self.store.connection.execute(
                    "SELECT count(*) FROM final_assurance"
                ).fetchone()[0],
                0,
            )
            self.store.require_current_attempt(self.attempt_id)
            self.store.connection.execute("DROP TRIGGER fixture_failure")
            self.assertEqual(self.finalize().outcome, "SUCCEEDED")

    def test_concurrent_finalizers_converge_without_execution_observation_state(self):
        async def result(*args):
            await asyncio.sleep(0)
            return self.result

        async def complete():
            with BroodlingStore.open(self.store_path) as other:
                second = WorkUnitDispositionCoordinator(other, self.adapter)
                return await asyncio.gather(
                    self.disposition.finalize(self.attempt_id),
                    second.finalize(self.attempt_id),
                )

        with patch.object(self.adapter, "wait", side_effect=result):
            first, second = asyncio.run(complete())
        self.assertEqual(first, second)
        self.assertEqual(
            self.store.connection.execute(
                "SELECT count(*) FROM final_assurance"
            ).fetchone()[0],
            1,
        )

    def test_native_stop_never_promotes_terminal_labels_to_cleanup_authority(self):
        admin = AbandonmentCoordinator(self.store, self.adapter)
        for terminal in (
            self.result,
            RunResult(run_id="native-run", succeeded=False, failure="force_stopped"),
            RunResult(run_id="native-run", succeeded=False, failure="runtime_lost"),
        ):
            with self.subTest(result=terminal):
                with patch.object(
                    self.adapter, "stop_known", AsyncMock(return_value=terminal)
                ) as stop:
                    with self.assertRaisesRegex(
                        CessationUnconfirmed, "physical cessation"
                    ):
                        asyncio.run(admin.stop(self.attempt_id, "explicit stop"))
                    stop.assert_awaited_once()
                with self.assertRaises(CessationUnconfirmed):
                    admin.retire(self.attempt_id)
        self.assertIsNone(admin.record(self.attempt_id))
        self.assertTrue(self.path.is_dir())


class NativeWorkflowTests(RealSubmissionCase):
    def contract(self, work_unit, source, **overrides):
        return replace(
            super().contract(work_unit, source, **overrides),
            criteria=(Criterion("candidate", "Make the requested README change."),),
        )

    def setUp(self):
        with patch("support.ISSUE_BODY", b"BROODLING_TEST_WRITE"):
            super().setUp()

    def test_no_effect_success_exposes_the_stable_result_capability_gap(
        self,
    ):
        row = self.submit()
        # The real released SDK/native engine executes its standard workflow.
        # Only the provider is controlled; no graph or engine trace is inspected.
        result = asyncio.run(
            self.adapter.wait(json.loads(row.request_json), row.run_id)
        )
        self.assertTrue(result.succeeded, result.failure)
        self.assertIsNone(result.output)
        self.restart()
        disposition = WorkUnitDispositionCoordinator(self.store, self.adapter)
        with self.assertRaisesRegex(SubmissionNotReady, "capability gap"):
            asyncio.run(disposition.finalize(self.attempt_id))
        self.assertIsNone(disposition.record(self.attempt_id))
        self.store.require_current_attempt(self.attempt_id)
