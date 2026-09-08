"""Product correlation controls, including the exact G2 mutation crash window."""

import json
import sqlite3
from concurrent.futures import ThreadPoolExecutor
from threading import Event
from unittest.mock import patch

from submission_support import REQUEST, RealSubmissionCase, SubmissionCase
from support import git, move_head, work_reference

from broodling.errors import (
    StaleAttempt,
    SubmissionConflict,
    SubmissionNotReady,
    WorktreeOwnershipConflict,
)
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter


class SubmissionControls(SubmissionCase):
    def test_abandonment_serializes_with_external_submission_acknowledgment(self):
        from broodling import BroodlingStore

        entered, release, abandoning = Event(), Event(), Event()

        def accept(request):
            entered.set()
            if not release.wait(5):
                raise RuntimeError("test did not release the SDK acknowledgment")
            return "original-run"

        def submit():
            with BroodlingStore.open(self.store_path) as store:
                return SubmissionCoordinator(store, self.adapter).submit(
                    self.attempt_id, **REQUEST
                )

        def abandon():
            with BroodlingStore.open(self.store_path) as store:
                abandoning.set()
                return store.abandon_attempt(self.attempt_id, "stop during dispatch")

        with (
            patch.object(self.adapter, "submit", side_effect=accept) as native,
            ThreadPoolExecutor(max_workers=2) as pool,
        ):
            submitted = pool.submit(submit)
            try:
                self.assertTrue(entered.wait(5))
                abandoned = pool.submit(abandon)
                self.assertTrue(abandoning.wait(5))
                self.assertFalse(abandoned.done())
                self.assertIsNone(self.store.abandonment(self.attempt_id))
            finally:
                release.set()
            self.assertEqual(submitted.result(5).run_id, "original-run")
            abandoned.result(5)
            native.assert_called_once()
        self.assertFalse(self.store.get_attempt(self.attempt_id).is_current)
        with patch.object(self.adapter, "submit") as native:
            with self.assertRaises(StaleAttempt):
                self.coordinator.reconcile(self.attempt_id)
            native.assert_not_called()
        self.assertEqual(
            self.coordinator.record(self.attempt_id).run_id, "original-run"
        )

    def test_abandonment_fences_prepared_and_ambiguous_dispatch_without_sdk_replay(
        self,
    ):
        self.prepare()
        self.store.abandon_attempt(self.attempt_id, "explicit stop")
        with patch.object(self.adapter, "submit") as native:
            for operation in (
                self.prepare,
                self.submit,
                lambda: self.coordinator.reconcile(self.attempt_id),
            ):
                with self.subTest(operation=operation), self.assertRaises(StaleAttempt):
                    operation()
            native.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "prepared")

    def test_lost_ack_abandonment_cannot_launch_work_to_discover_old_run(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("ack lost")),
            self.assertRaises(OSError),
        ):
            self.submit()
        original = self.coordinator.record(self.attempt_id)
        self.assertEqual(original.state, "dispatched")
        self.store.abandon_attempt(self.attempt_id, "ambiguous dispatch")
        self.restart()
        with patch.object(self.adapter, "submit") as native:
            with self.assertRaises(StaleAttempt):
                self.coordinator.reconcile(self.attempt_id)
            native.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id), original)

    def test_prepared_identity_commits_before_dispatch(self):
        def accept(request):
            from broodling import BroodlingStore

            with BroodlingStore.open(self.store_path) as reader:
                row = reader.connection.execute(
                    "SELECT * FROM attempt_submissions"
                ).fetchone()
                self.assertEqual(row["state"], "dispatched")
                self.assertEqual(json.loads(row["request_json"]), request)
            return "r-one"

        with patch.object(self.adapter, "submit", side_effect=accept):
            self.assert_single(self.submit().run_id)

    def test_prepared_restart_rejects_drift_without_calling_sdk(self):
        original = self.prepare()
        move_head(self.path)
        self.restart()
        with patch.object(self.adapter, "submit") as native:
            with self.assertRaises(SubmissionNotReady):
                self.coordinator.reconcile(self.attempt_id)
            native.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id), original)

    def test_first_submit_rejects_dirty_b1(self):
        (self.path / "untracked").write_text("foreign material")
        with self.assertRaises(SubmissionNotReady):
            self.submit()
        self.assertIsNone(self.coordinator.record(self.attempt_id))

    def test_changed_request_is_rejected_before_sdk_even_after_mutation(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("lost")),
            self.assertRaises(OSError),
        ):
            self.submit()
        original = self.coordinator.record(self.attempt_id)
        move_head(self.path)
        with patch.object(self.adapter, "submit") as native:
            with self.assertRaises(SubmissionConflict):
                self.submit(title="competing request")
            native.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id), original)

    def test_runtime_target_cannot_change_on_restart(self):
        self.prepare()
        other = SubmissionCoordinator(
            self.store, ZeroshotSubmitter(self.root / "other-runtime")
        )
        with self.assertRaises(SubmissionConflict):
            other.reconcile(self.attempt_id)

    def test_source_ownership_change_is_not_excused_by_head_drift(self):
        for change in ("origin", "branch", "marker", "repository"):
            with self.subTest(change=change):
                with (
                    patch.object(self.adapter, "submit", side_effect=OSError("lost")),
                    self.assertRaises(OSError),
                ):
                    self.submit()
                move_head(self.path, content=change)
                origin = git(self.path, "config", "--get", "remote.origin.url")
                branch = git(self.path, "branch", "--show-current")
                from broodling.workspace import DISPOSABLE_WORKTREE_MARKER

                marker = self.path.parent / DISPOSABLE_WORKTREE_MARKER
                old_marker = marker.read_text()
                git_link = (self.path / ".git").read_text()
                try:
                    if change == "origin":
                        git(
                            self.path,
                            "remote",
                            "set-url",
                            "origin",
                            "https://github.com/other/source.git",
                        )
                    elif change == "branch":
                        git(self.path, "checkout", "-b", "foreign")
                    elif change == "marker":
                        marker.write_text("other-attempt\n")
                    else:
                        (self.path / ".git").write_text(
                            f"gitdir: {self.repository / '.git'}\n"
                        )
                    with patch.object(self.adapter, "submit") as native:
                        with self.assertRaises(
                            (SubmissionConflict, WorktreeOwnershipConflict)
                        ):
                            self.coordinator.reconcile(self.attempt_id)
                        native.assert_not_called()
                finally:
                    (self.path / ".git").write_text(git_link)
                    marker.write_text(old_marker)
                    git(self.path, "checkout", branch)
                    git(self.path, "remote", "set-url", "origin", origin)

    def test_conflict_without_public_id_blocks_after_mutation(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("lost")),
            self.assertRaises(OSError),
        ):
            self.submit()
        move_head(self.path)
        with (
            patch.object(
                self.adapter, "submit", side_effect=SubmissionConflict("no ID")
            ),
            self.assertRaises(SubmissionConflict),
        ):
            self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "blocked")

    def test_dirty_files_do_not_excuse_true_conflict(self):
        with (
            patch.object(self.adapter, "submit", side_effect=OSError("lost")),
            self.assertRaises(OSError),
        ):
            self.submit()
        (self.path / "README.md").write_text("dirty only")
        with (
            patch.object(
                self.adapter,
                "submit",
                side_effect=SubmissionConflict(
                    "different graph", existing_run_id="foreign"
                ),
            ),
            self.assertRaises(SubmissionConflict),
        ):
            self.coordinator.reconcile(self.attempt_id)
        self.assertIsNone(self.coordinator.record(self.attempt_id).run_id)

    def test_correlation_is_immutable_at_database_boundary(self):
        with patch.object(self.adapter, "submit", return_value="r-one"):
            row = self.submit()
        for sql in (
            "UPDATE attempt_submissions SET zeroshot_run_id = 'r-two'",
            "UPDATE attempt_submissions SET submission_key = 'new-key'",
            "UPDATE attempt_submissions SET request_json = '{}'",
            "UPDATE attempt_submissions SET state = 'dispatched', zeroshot_run_id = NULL",
            "DELETE FROM attempt_submissions",
        ):
            with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                self.store.connection.execute(sql)
        self.assertEqual(self.coordinator.record(self.attempt_id), row)

    def test_stale_attempt_cannot_prepare_dispatch_or_read_back_correlation(self):
        for state in ("absent", "prepared", "dispatched", "correlated"):
            with self.subTest(state=state):
                self.store.connection.execute("DROP TRIGGER attempts_no_update")
                self.store.connection.execute("UPDATE attempts SET is_current = 1")
                if state == "prepared":
                    self.prepare()
                if state == "dispatched":
                    with (
                        patch.object(
                            self.adapter, "submit", side_effect=OSError("lost")
                        ),
                        self.assertRaises(OSError),
                    ):
                        self.submit()
                if state == "correlated":
                    with patch.object(self.adapter, "submit", return_value="r-one"):
                        self.submit()
                # No product retirement API: fixture creates a historical stale row.
                self.store.connection.execute("UPDATE attempts SET is_current = 0")
                self.store.connection.execute(
                    "CREATE TRIGGER attempts_no_update BEFORE UPDATE ON attempts BEGIN SELECT RAISE(ABORT, 'immutable'); END"
                )
                with patch.object(self.adapter, "submit") as native:
                    for action in (
                        self.submit,
                        lambda: self.coordinator.reconcile(self.attempt_id),
                    ):
                        with self.assertRaises(StaleAttempt):
                            action()
                    native.assert_not_called()


class PublicSubmissionTests(RealSubmissionCase):
    def lose_ack(self):
        native = self.adapter.submit
        accepted = []

        def lose(request):
            accepted.append(native(request))
            raise OSError("caller lost acknowledgement")

        with (
            patch.object(self.adapter, "submit", side_effect=lose),
            self.assertRaises(OSError),
        ):
            self.submit()
        self.assertEqual(len(accepted), 1)
        self.assertIsNone(self.coordinator.record(self.attempt_id).run_id)
        return accepted[0]

    def test_normal_and_repeated_ingress_preserve_entire_lineage(self):
        result = self.submit()
        self.assertEqual(
            self.store.resolve_work_unit(work_reference()).work_unit_id,
            self.work_unit.work_unit_id,
        )
        self.assertEqual(
            self.provisioner()
            .admit_and_provision(self.revision.contract_revision_id, self.repository)
            .attempt,
            self.attempt,
        )
        self.restart()
        self.assertEqual(self.submit(), result)
        self.assert_single(result.run_id)

    def test_acknowledgement_loss_before_worktree_mutation(self):
        run_id = self.lose_ack()
        original = self.coordinator.record(self.attempt_id)
        self.restart()
        result = self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(result.request_json, original.request_json)
        self.assert_single(run_id)

    def test_acknowledgement_loss_after_worktree_mutation_uses_public_conflict(self):
        run_id = self.lose_ack()
        original = self.coordinator.record(self.attempt_id)
        move_head(self.path)
        # Discriminates the public conflict path from ordinary successful replay.
        with self.assertRaises(SubmissionConflict) as conflict:
            self.adapter.submit(json.loads(original.request_json))
        self.assertEqual(conflict.exception.existing_run_id, run_id)
        self.restart()
        result = self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(result.request_json, original.request_json)
        self.assertEqual(result.submission_key, original.submission_key)
        self.assert_single(run_id)

    def test_acknowledgement_loss_with_uncommitted_mutation_replays_normally(self):
        run_id = self.lose_ack()
        (self.path / "README.md").write_text("runtime edit")
        self.restart()
        self.assertEqual(self.coordinator.reconcile(self.attempt_id).run_id, run_id)

    def test_true_conflicting_request_at_public_boundary_blocks(self):
        original = self.prepare()
        different = json.loads(original.request_json)
        different["title"] = "foreign request under the same key"
        existing = self.adapter.submit(different)
        with self.assertRaises(SubmissionConflict) as conflict:
            self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(conflict.exception.existing_run_id, existing)
        row = self.coordinator.record(self.attempt_id)
        self.assertEqual(row.state, "blocked")
        self.assertIsNone(row.run_id)
        with self.assertRaises(SubmissionConflict):
            self.submit()
        self.assertEqual(self.coordinator.record(self.attempt_id), row)


class QualifiedAdapterTests(SubmissionCase):
    def test_unqualified_build_is_refused_before_dispatch(self):
        from broodling.errors import UnsupportedRuntime
        from broodling.profile import QUALIFIED_ZEROSHOT_BOUNDARY
        from broodling.zeroshot_sdk import (
            QUALIFIED_SDK_SOURCE_SHA256,
            assert_qualified_integration,
        )

        correct = {
            "sdkVersion": "0.1.0.dev0",
            "sdkSourceSha256": QUALIFIED_SDK_SOURCE_SHA256,
            "sidecarSha256": QUALIFIED_ZEROSHOT_BOUNDARY["sidecarSha256"],
        }
        for field in correct:
            with (
                self.subTest(field=field),
                patch(
                    "broodling.zeroshot_sdk.installed_integration",
                    return_value=correct | {field: "different"},
                ),
                self.assertRaises(UnsupportedRuntime),
            ):
                assert_qualified_integration()

    def test_no_delivery_or_ambient_credentials(self):
        from broodling.errors import UnsupportedRuntime

        with patch.dict(
            "os.environ",
            {
                "GH_TOKEN": "canary",
                "GITHUB_TOKEN": "canary",
                "OPENAI_API_KEY": "canary",
            },
        ):
            adapter = ZeroshotSubmitter(self.runtime_state)
        self.assertEqual(set(adapter.target["environment"]), {"PATH"})
        with self.assertRaises(UnsupportedRuntime):
            self.submit(runtime={"nodes": {"deliver": {"kind": "git_delivery"}}})
        self.assertIsNone(self.coordinator.record(self.attempt_id))


class PublicSourceConflictTests(RealSubmissionCase):
    def test_true_conflicting_source_at_public_boundary_blocks(self):
        original = self.prepare()
        branch = git(self.path, "branch", "--show-current")
        git(self.path, "checkout", "-b", "foreign-source")
        try:
            existing = self.adapter.submit(json.loads(original.request_json))
        finally:
            git(self.path, "checkout", branch)
        with self.assertRaises(SubmissionConflict) as conflict:
            self.coordinator.reconcile(self.attempt_id)
        self.assertEqual(conflict.exception.existing_run_id, existing)
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "blocked")
        self.assertIsNone(self.coordinator.record(self.attempt_id).run_id)


class AdditionalSubmissionControls(SubmissionCase):
    def test_explicit_credentials_are_refused_at_adapter_boundary(self):
        from broodling.errors import UnsupportedRuntime

        request = json.loads(self.prepare().request_json)
        for name in ("GH_TOKEN", "GITHUB_TOKEN", "OPENAI_API_KEY", "GIT_DIR"):
            with self.subTest(name=name):
                request["target"]["environment"][name] = "canary"
                with self.assertRaises(UnsupportedRuntime):
                    self.adapter.submit(request)
                del request["target"]["environment"][name]

    def test_changed_origin_before_dispatch_leaves_prepared(self):
        original = self.prepare()
        git(
            self.path,
            "remote",
            "set-url",
            "origin",
            "https://github.com/foreign/source.git",
        )
        with patch.object(self.adapter, "submit") as native:
            with self.assertRaises(SubmissionConflict):
                self.coordinator.reconcile(self.attempt_id)
            native.assert_not_called()
        self.assertEqual(self.coordinator.record(self.attempt_id), original)

    def test_a_second_attempt_cannot_claim_the_same_run_id(self):
        from broodling import SourceSubmission

        first_id = self.submit_with_fake()
        other = self.store.resolve_work_unit(work_reference(issue=99))
        source = self.store.entitle_source(
            other.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=other.issue_locator,
                content=b"another unit",
                retrieved_at="2026-09-07T00:00:00+00:00",
            ),
        )
        contract = self.store.record_contract_revision(self.contract(other, source))
        self.store.admit(contract.contract_revision_id)
        attempt = (
            self.provisioner()
            .admit_and_provision(contract.contract_revision_id, self.repository)
            .attempt
        )
        with (
            patch.object(self.adapter, "submit", return_value=first_id),
            self.assertRaises(sqlite3.IntegrityError),
        ):
            self.coordinator.submit(attempt.attempt_id, **REQUEST)
        self.assertEqual(self.coordinator.record(self.attempt_id).run_id, first_id)
        self.assertIsNone(self.coordinator.record(attempt.attempt_id).run_id)

    def submit_with_fake(self):
        with patch.object(self.adapter, "submit", return_value="unique-public-run"):
            return self.submit().run_id
