"""One caller boundary, real Broodling services, controlled GitHub/SDK edges."""

import asyncio
import json
import shutil
import subprocess
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

from support import StoreTestCase, durable_test_root, git, make_repository, move_head
from zeroshot import RunResult

from broodling import (
    AttemptConflict,
    Broodling,
    BroodlingStore,
    CessationUnconfirmed,
    Contract,
    Criterion,
    GitCommandError,
    Prerequisite,
    RequiredEffect,
    WorkReference,
    WorkUnitIdentityConflict,
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
        make_repository(self.repository)
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
        self.assertEqual(
            submitted.sources[0].work_unit_id, submitted.work_unit.work_unit_id
        )
        self.assertEqual(
            submitted.attempt.contract_revision_id,
            submitted.revision.contract_revision_id,
        )
        self.assertEqual(submitted.worktree.attempt_id, submitted.attempt.attempt_id)
        self.assertEqual(submitted.submission.run_id, "native-run")
        self.assertIsNone(submitted.disposition)
        result = asyncio.run(self.app.wait(submitted.attempt.attempt_id))
        self.assertEqual(result.outcome, "SUCCEEDED")
        self.assertEqual(result.attempt_id, submitted.attempt.attempt_id)
        status = self.app.status(submitted.revision.contract_revision_id)
        self.assertEqual(status.disposition, result)

    def test_observation_does_not_wait_for_the_provisioning_writer(self):
        real_run = subprocess.run
        observed = []
        with BroodlingStore.open(self.store_path) as reader:
            # Make writer-slot contention fail immediately, without timing races.
            reader.connection.execute("PRAGMA busy_timeout = 0")
            observer = Broodling(reader, self.engine, self.host / "attempts")

            def observe_during_worktree_creation(command, *args, **kwargs):
                if "worktree" in command and "add" in command:
                    # Real provisioning holds its lifecycle write transaction
                    # across this Git call. Read through the public facade.
                    observed.extend(observer.history(REFERENCE))
                return real_run(command, *args, **kwargs)

            with patch("subprocess.run", side_effect=observe_during_worktree_creation):
                submitted = self.submit()

        (snapshot,) = observed
        self.assertEqual(snapshot.revision, submitted.revision)
        self.assertEqual(snapshot.attempt, submitted.attempt)
        self.assertFalse(snapshot.worktree.provisioned)
        self.assertIsNone(snapshot.submission)
        self.assertTrue(submitted.worktree.provisioned)
        self.assertEqual(submitted.submission.run_id, "native-run")

    def test_history_checks_upstream_identity_without_becoming_ingress(self):
        reference = replace(REFERENCE, repository_identity="R_original")
        submitted = self.app.submit(
            reference, propose, repository=self.repository, required_effects=(PR,)
        )
        pinned = replace(reference, issue_identity=submitted.work_unit.issue_identity)
        unpinned = WorkReference.parse("faviann/broodling", 83)
        self.store.resolve_work_unit(unpinned)
        self.reopen()
        self.app = self.application()
        self.sdk.reset_mock()
        # Observation must not record submissions or pin previously unknown IDs.
        self.store.connection.execute("PRAGMA query_only = ON")
        with patch("subprocess.run", side_effect=AssertionError("observation only")):
            self.assertEqual(self.app.history(pinned), (submitted,))
            self.assertEqual(self.app.history(REFERENCE), (submitted,))
            for field in ("repository_identity", "issue_identity"):
                with (
                    self.subTest(field=field),
                    self.assertRaises(WorkUnitIdentityConflict),
                ):
                    self.app.history(replace(pinned, **{field: "recreated"}))
            self.assertEqual(
                self.app.history(
                    replace(
                        unpinned,
                        repository_identity="R_supplied",
                        issue_identity="I_supplied",
                    )
                ),
                (),
            )
            self.assertEqual(
                self.app.history(WorkReference.parse("other/repo", 82)), ()
            )
        self.sdk.assert_not_called()

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
        self.assertEqual(self.submit().disposition, result)
        self.client.submit.assert_awaited_once()
        self.run.wait.assert_awaited_once()

    def test_lost_submission_response_is_discoverable_and_resumable(self):
        self.client.submit.side_effect = OSError("acknowledgment lost")
        with self.assertRaises(OSError):
            self.submit()
        self.reopen()
        self.app = self.application()
        with patch("subprocess.run", side_effect=AssertionError("read only")):
            (pending,) = self.app.history(REFERENCE)
        self.assertEqual(pending.submission.state, "dispatched")
        move_head(self.repository)
        self.client.submit.side_effect = None
        resumed = self.app.resume(pending.revision.contract_revision_id)
        self.assertEqual(resumed.attempt, pending.attempt)
        self.assertEqual(resumed.submission.run_id, "native-run")

    def test_stopped_invocation_is_handed_back_by_resume_and_submit(self):
        submitted = self.submit()
        revision_id = submitted.revision.contract_revision_id
        with self.assertRaises(CessationUnconfirmed):
            asyncio.run(self.app.stop(submitted.attempt.attempt_id, "operator stop"))
        self.run.force_stop.assert_awaited_once()
        # Guard the facade's abandonment handback, not native cessation policy.
        status = self.app.status(revision_id)
        self.assertEqual(status.abandonment.reason, "operator stop")
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
        self.assertTrue(rejected.decision.findings)
        self.assertIsNone(rejected.attempt)
        self.assertIsNone(rejected.submission)
        self.assertEqual(
            self.app.resume(rejected.revision.contract_revision_id), rejected
        )
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

    def test_interrupted_provisioning_resumes_the_recorded_attempt(self):
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
        self.assertTrue(resumed.worktree.provisioned)
        self.assertEqual(resumed.submission.run_id, "native-run")
