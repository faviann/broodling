"""One caller boundary, real Broodling services, controlled GitHub/SDK edges."""

import asyncio
import json
import shutil
import subprocess
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

from submission_support import configured_adapter
from support import StoreTestCase, durable_test_root, git, make_repository, move_head
from zeroshot import RunResult

from broodling import (
    AttemptConflict,
    Broodling,
    CessationUnconfirmed,
    Contract,
    Criterion,
    GitCommandError,
    Prerequisite,
    RequiredEffect,
    StaleAttempt,
    SubmissionConflict,
    SubmissionNotReady,
    WorkReference,
)
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL, ZeroshotSubmitter

PR = RequiredEffect("deliver", "Open a PR targeting main.", "pull_request", "main")
REFERENCE = WorkReference.parse("faviann/broodling", 82)
ISSUE = (Path(__file__).parent / "fixtures/ingress/issue-82.json").read_bytes()


def propose(inputs):
    return Contract(
        work_unit_id=inputs.work_unit.work_unit_id,
        source_attribution=inputs.source_attribution,
        criteria=(Criterion("request", json.loads(inputs.sources[0].content)["body"]),),
        required_effects=inputs.required_effects,
        constructed_by=inputs.constructed_by,
    )


class InvocationTests(StoreTestCase):
    def setUp(self):
        super().setUp()
        self.host = durable_test_root(prefix="broodling-invocation-")
        self.addCleanup(shutil.rmtree, self.host, ignore_errors=True)
        self.repository = self.host / "source"
        self.b1 = make_repository(self.repository)
        git(
            self.repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        self.engine = ZeroshotSubmitter(
            self.host / "runtime",
            delivery_target_origin="http://127.0.0.1:8123",
            github_token="fixture-github-token",
            gateway_base_url=V1_GATEWAY_BASE_URL,
            gateway_api_key="fixture-gateway-key",
        )
        self.app = self.application()
        self.receipt = {
            "version": "v1",
            "mode": "pr",
            "outcome": "opened",
            "repository": "faviann/broodling",
            "targetBranch": "main",
            "headRevision": "b" * 40,
            "pullRequestId": "50",
        }
        self.run = SimpleNamespace(
            id="native-run",
            wait=AsyncMock(
                return_value=RunResult(
                    run_id="native-run",
                    succeeded=True,
                    output=self.receipt,
                )
            ),
            force_stop=AsyncMock(),
        )
        self.client = MagicMock()
        self.client.__aenter__ = AsyncMock(return_value=self.client)
        self.client.__aexit__ = AsyncMock(return_value=None)
        self.client.submit = AsyncMock(return_value=self.run)
        self.client.get_run.return_value = self.run
        self.sdk = self.enterContext(patch("zeroshot.Client", return_value=self.client))
        real_run = subprocess.run

        def external_run(command, *args, **kwargs):
            if command[0] == "gh":
                return subprocess.CompletedProcess(command, 0, stdout=ISSUE, stderr=b"")
            return real_run(command, *args, **kwargs)

        self.enterContext(patch("subprocess.run", side_effect=external_run))

    def application(self):
        return Broodling(self.store, self.engine, self.host / "attempts")

    def submit(self, proposer=propose, **overrides):
        arguments = {"repository": self.repository, "required_effects": (PR,)}
        arguments.update(overrides)
        return self.app.submit(REFERENCE, proposer, **arguments)

    def test_reference_reaches_receipt_backed_disposition_and_observable_lineage(self):
        submitted = self.submit()
        self.assertTrue(submitted.decision.admitted)
        self.assertEqual(submitted.work_unit.work_unit_id, REFERENCE.work_unit_id)
        self.assertEqual(submitted.sources[0].content, ISSUE)
        self.assertEqual(
            submitted.attempt.contract_revision_id,
            submitted.revision.contract_revision_id,
        )
        self.assertEqual(submitted.attempt.b1_commit_oid, self.b1)
        self.assertTrue(submitted.worktree.path.is_dir())
        self.assertEqual(submitted.submission.run_id, "native-run")
        self.assertIsNone(submitted.disposition)
        result = asyncio.run(self.app.wait(submitted.attempt.attempt_id))
        self.assertEqual(result.outcome, "SUCCEEDED")
        self.assertEqual(result.result["deliveryReceipt"], self.receipt)
        self.assertEqual(result.result["acceptedRevision"], "b" * 40)
        self.assertEqual(result.result["runId"], "native-run")
        status = self.app.status(submitted.revision.contract_revision_id)
        self.assertEqual(status.disposition, result)
        self.assertFalse(status.attempt.is_current)
        self.assertTrue(status.worktree.path.is_dir())
        self.assertIsNone(status.abandonment)
        dispatch = self.client.submit.call_args.kwargs
        self.assertEqual(dispatch["repository"], "faviann/broodling")
        self.assertEqual(dispatch["branch"], "main")
        self.assertEqual(dispatch["revision"], self.b1)
        self.assertEqual(dispatch["preset"].delivery, "pull_request")

    def test_repeated_submission_and_reopened_resume_preserve_original_authority(self):
        first = self.submit()
        revision_id = first.revision.contract_revision_id
        move_head(self.repository)
        repeated = self.submit()
        self.assertEqual(repeated, first)
        self.reopen()
        self.engine = ZeroshotSubmitter(self.host / "unavailable-current-runtime")
        self.app = self.application()
        # Reconnection needs neither acquisition/proposal nor dispatch credentials,
        # a valid current profile, live HEAD, or even the old worktree on disk.
        first.worktree.path.rename(first.worktree.path.with_name("unavailable"))
        with patch("subprocess.run", side_effect=AssertionError("no reacquisition")):
            resumed = self.app.resume(revision_id)
            self.assertEqual(resumed, first)
            result = asyncio.run(self.app.wait(first.attempt.attempt_id))
            self.assertEqual(self.app.resume(revision_id).disposition, result)
        self.assertEqual(self.client.submit.await_count, 1)
        self.assertEqual(self.run.wait.await_count, 1)
        self.assertEqual(self.sdk.call_args.kwargs["environment"], {})
        self.reopen()
        self.app = self.application()
        self.run.wait.side_effect = AssertionError("result already retained")
        self.assertEqual(asyncio.run(self.app.wait(first.attempt.attempt_id)), result)
        self.assertEqual(self.submit().disposition, result)

    def test_lost_dispatch_acknowledgment_is_discoverable_and_replays_frozen_request(
        self,
    ):
        self.client.submit.side_effect = OSError("acknowledgment lost")
        with self.assertRaises(OSError):
            self.submit()
        self.reopen()
        self.app = self.application()
        with patch("subprocess.run", side_effect=AssertionError("read only")):
            (pending,) = self.app.history(REFERENCE)
        self.assertEqual(pending.submission.state, "dispatched")
        original_dispatch = self.client.submit.call_args
        move_head(self.repository)
        self.engine.github_token = "rotated-fixture-token"
        self.client.submit.side_effect = None
        resumed = self.app.resume(pending.revision.contract_revision_id)
        self.assertEqual(resumed.attempt, pending.attempt)
        self.assertEqual(
            resumed.submission.request_json, pending.submission.request_json
        )
        self.assertEqual(self.client.submit.call_args, original_dispatch)
        self.assertEqual(resumed.submission.run_id, "native-run")
        result = asyncio.run(self.app.wait(resumed.attempt.attempt_id))
        self.assertEqual(result.outcome, "SUCCEEDED")

    def test_stop_abandons_and_retains_dispatched_worktree_without_replacement(self):
        submitted = self.submit()
        revision_id = submitted.revision.contract_revision_id
        attempt_id = submitted.attempt.attempt_id
        with self.assertRaises(CessationUnconfirmed):
            asyncio.run(self.app.stop(attempt_id, "operator stop"))
        self.run.force_stop.assert_awaited_once()
        status = self.app.status(revision_id)
        self.assertEqual(status.abandonment.reason, "operator stop")
        self.assertFalse(status.attempt.is_current)
        self.assertTrue(status.worktree.path.is_dir())
        self.assertIsNone(status.disposition)
        with self.assertRaises(StaleAttempt):
            asyncio.run(self.app.wait(attempt_id))
        self.assertEqual(self.app.resume(revision_id), status)
        self.assertEqual(self.submit(), status)
        self.client.submit.assert_awaited_once()

    def test_rejected_request_hands_back_findings_without_attempt_or_dispatch(self):
        def unsupported(inputs):
            return replace(
                propose(inputs),
                prerequisites=(
                    Prerequisite(
                        "approval", "Await explicit upstream readiness.", False
                    ),
                ),
            )

        rejected = self.submit(proposer=unsupported)
        self.assertFalse(rejected.decision.admitted)
        self.assertEqual(rejected.decision.findings[0].code, "unsatisfied_prerequisite")
        self.assertEqual(
            rejected.decision.findings[0].preserved_obligation,
            "Await explicit upstream readiness.",
        )
        self.assertIsNone(rejected.attempt)
        self.assertIsNone(rejected.submission)
        self.assertEqual(
            self.app.resume(rejected.revision.contract_revision_id), rejected
        )
        self.assertFalse((self.host / "attempts").exists())
        self.sdk.assert_not_called()

    def test_changed_effect_authority_cannot_replace_the_current_attempt(self):
        first = self.submit()
        with self.assertRaises(AttemptConflict):
            self.submit(required_effects=(replace(PR, target_branch="release"),))
        old, new = self.app.history(REFERENCE)
        self.assertEqual(old, first)
        self.assertTrue(new.decision.admitted)
        self.assertIsNone(new.attempt)
        self.assertEqual(
            new.revision.contract.required_effects[0].target_branch, "release"
        )
        self.assertEqual(self.app.resume(first.revision.contract_revision_id), first)
        self.client.submit.assert_awaited_once()

    def test_native_failure_exposes_abandonment_instead_of_a_success_disposition(self):
        submitted = self.submit()
        self.run.wait.return_value = RunResult(
            run_id="native-run",
            succeeded=False,
            failure="runtime_lost",
        )
        with self.assertRaisesRegex(SubmissionNotReady, "runtime_lost"):
            asyncio.run(self.app.wait(submitted.attempt.attempt_id))
        self.reopen()
        self.app = self.application()
        status = self.app.resume(submitted.revision.contract_revision_id)
        self.assertIn("runtime_lost", status.abandonment.reason)
        self.assertEqual(status.submission.run_id, "native-run")
        self.assertIsNone(status.disposition)
        self.assertFalse(status.attempt.is_current)
        self.assertTrue(status.worktree.path.is_dir())
        self.client.submit.assert_awaited_once()

    def test_cancelled_wait_can_reconnect_without_implicitly_stopping(self):
        submitted = self.submit()
        self.run.wait.side_effect = asyncio.CancelledError()
        with self.assertRaises(asyncio.CancelledError):
            asyncio.run(self.app.wait(submitted.attempt.attempt_id))
        self.assertEqual(
            self.app.status(submitted.revision.contract_revision_id), submitted
        )
        self.run.force_stop.assert_not_awaited()
        self.reopen()
        self.app = self.application()
        self.run.wait.side_effect = None
        self.assertEqual(
            asyncio.run(self.app.wait(submitted.attempt.attempt_id)).outcome,
            "SUCCEEDED",
        )
        self.client.submit.assert_awaited_once()

    def test_interrupted_provisioning_resumes_the_recorded_attempt_and_b1(self):
        real_run = subprocess.run

        def refuse_worktree(command, *args, **kwargs):
            if "worktree" in command and "add" in command:
                return subprocess.CompletedProcess(
                    command, 1, stdout="", stderr="fixture refusal"
                )
            return real_run(command, *args, **kwargs)

        with (
            patch("subprocess.run", side_effect=refuse_worktree),
            self.assertRaises(GitCommandError),
        ):
            self.submit()
        (pending,) = self.app.history(REFERENCE)
        self.assertFalse(pending.worktree.provisioned)
        self.assertIsNone(pending.submission)
        move_head(self.repository)
        self.reopen()
        self.app = self.application()
        resumed = self.app.resume(pending.revision.contract_revision_id)
        self.assertEqual(resumed.attempt, pending.attempt)
        self.assertEqual(git(resumed.worktree.path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(resumed.submission.run_id, "native-run")

    def test_unrelated_run_result_cannot_grant_work_unit_success(self):
        submitted = self.submit()
        self.run.wait.return_value = RunResult(
            run_id="unrelated-run",
            succeeded=True,
            output=self.receipt,
        )
        with self.assertRaises(SubmissionConflict):
            asyncio.run(self.app.wait(submitted.attempt.attempt_id))
        status = self.app.status(submitted.revision.contract_revision_id)
        self.assertIsNone(status.disposition)
        self.assertTrue(status.attempt.is_current)

    def test_no_effect_native_success_keeps_the_existing_stable_result_gap(self):
        self.engine = configured_adapter(self.host / "runtime", self.root)
        self.app = self.application()
        submitted = self.submit(required_effects=())
        self.run.wait.return_value = RunResult(
            run_id="native-run",
            succeeded=True,
            output=None,
        )
        with self.assertRaisesRegex(SubmissionNotReady, "capability gap"):
            asyncio.run(self.app.wait(submitted.attempt.attempt_id))
        status = self.app.status(submitted.revision.contract_revision_id)
        self.assertIsNone(status.disposition)
        self.assertTrue(status.attempt.is_current)
        self.assertEqual(self.client.submit.call_args.kwargs["preset"].delivery, "none")
