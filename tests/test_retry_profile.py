"""Fresh automatic-context boundaries across allocation and dispatch."""

import asyncio
from unittest.mock import patch

from retry_test_support import RetryCase
from support import work_reference

from broodling import AbandonmentCoordinator, SourceSubmission
from broodling.errors import UnsupportedRuntime


class RetryProfileTests(RetryCase):
    def test_old_swapped_nested_and_history_homes_are_refused(self):
        old = self.old_adapter.codex_profile
        new = self.new_adapter.codex_profile
        nested = old.profile_home / "nested"
        nested.mkdir()
        cases = (
            (old.profile_home, new.isolated_codex_home),
            (new.profile_home, old.isolated_codex_home),
            (old.isolated_codex_home, old.profile_home),
            (nested, new.isolated_codex_home),
            (self.runtime_state, new.isolated_codex_home),
            (self.path.parent, new.isolated_codex_home),
        )
        for home, auth in cases:
            with (
                self.subTest(home=home, auth=auth),
                self.assertRaises(UnsupportedRuntime),
            ):
                self.retry_coordinator(self.adapter_for(home, auth)).allocate(
                    self.attempt_id, "retry"
                )
        self.assertIsNone(self.store.retry("retry"))

    def test_homes_reserved_without_submission_are_excluded(self):
        coordinator = self.retry_coordinator()
        allocated = coordinator.allocate(self.attempt_id, "retry")
        self.assertIsNone(coordinator.submission.record(allocated.attempt_id))
        with self.assertRaises(UnsupportedRuntime):
            self.store._check_retry_profile(self.new_adapter.target)

    def test_symlink_rebinding_and_new_history_before_dispatch_fail_closed(self):
        coordinator = self.retry_coordinator()
        prepared = coordinator.prepare(self.attempt_id, "retry")
        home = self.new_adapter.codex_profile.profile_home
        home.rmdir()
        home.symlink_to(
            self.old_adapter.codex_profile.profile_home, target_is_directory=True
        )
        with patch.object(self.new_adapter, "submit") as submit:
            with self.assertRaises(UnsupportedRuntime):
                coordinator.retry(self.attempt_id, "retry")
            submit.assert_not_called()
        home.unlink()
        home.mkdir()
        (home / "history.jsonl").write_text("ABANDONED_HISTORY_CANARY")
        with patch.object(self.new_adapter, "submit") as submit:
            with self.assertRaises(UnsupportedRuntime):
                coordinator.retry(self.attempt_id, "retry")
            submit.assert_not_called()
        self.assertEqual(
            coordinator.submission.record(prepared.attempt_id).state, "prepared"
        )

    def test_correlated_replay_keeps_legitimate_runtime_home_files(self):
        coordinator = self.retry_coordinator()
        with patch.object(
            self.new_adapter, "submit", return_value="controlled-new-run"
        ) as submit:
            first = coordinator.retry(self.attempt_id, "retry")
            profile = self.new_adapter.codex_profile
            for home in (profile.profile_home, profile.isolated_codex_home):
                (home / "runtime-history").write_text("NEW_RUN_CONTEXT")
            repeated = coordinator.retry(self.attempt_id, "retry")
            self.assertEqual(first, repeated)
            self.assertEqual(submit.call_count, 1)
            self.assertEqual(
                (profile.profile_home / "runtime-history").read_text(),
                "NEW_RUN_CONTEXT",
            )

    def test_other_work_unit_cannot_claim_reserved_profile_without_submission(self):
        self.retry_coordinator().allocate(self.attempt_id, "retry")
        unit = self.store.resolve_work_unit(work_reference(issue=222))
        source = self.store.entitle_source(
            unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=unit.issue_locator,
                content=b"Independent unit",
            ),
        )
        revision = self.store.record_contract_revision(self.contract(unit, source))
        self.store.admit(revision.contract_revision_id)
        sibling = self.provisioner().admit_and_provision(
            revision.contract_revision_id, self.repository
        )
        admin = AbandonmentCoordinator(self.store, self.adapter)
        asyncio.run(admin.stop(sibling.attempt.attempt_id, "sibling retry"))
        admin.retire(sibling.attempt.attempt_id)
        with self.assertRaises(UnsupportedRuntime):
            self.retry_coordinator().allocate(
                sibling.attempt.attempt_id, "sibling-retry"
            )
        self.assertIsNone(self.store.retry("sibling-retry"))

    def sibling_attempt(self, issue):
        unit = self.store.resolve_work_unit(work_reference(issue=issue))
        source = self.store.entitle_source(
            unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=unit.issue_locator,
                content=b"Independent unit",
            ),
        )
        revision = self.store.record_contract_revision(self.contract(unit, source))
        self.store.admit(revision.contract_revision_id)
        return (
            self.provisioner()
            .admit_and_provision(revision.contract_revision_id, self.repository)
            .attempt
        )

    def overlapping_adapters(self):
        profile = self.new_adapter.codex_profile
        yield "exact", self.new_adapter
        nested = profile.profile_home / "nested"
        nested.mkdir(exist_ok=True)
        yield "nested", self.adapter_for(nested, profile.isolated_codex_home)
        yield (
            "swapped",
            self.adapter_for(profile.isolated_codex_home, profile.profile_home),
        )

    def test_ordinary_prepare_cannot_claim_reserved_retry_homes(self):
        from broodling.submission import SubmissionCoordinator

        self.retry_coordinator().allocate(self.attempt_id, "retry")
        for issue, (name, adapter) in enumerate(self.overlapping_adapters(), start=230):
            sibling = self.sibling_attempt(issue)
            coordinator = SubmissionCoordinator(self.store, adapter)
            with self.subTest(overlap=name):
                with self.assertRaises(UnsupportedRuntime):
                    coordinator.prepare_assurance(sibling.attempt_id)
                self.assertIsNone(coordinator.record(sibling.attempt_id))

    def test_ordinary_first_dispatch_rechecks_reserved_retry_homes(self):
        import json

        from broodling import git
        from broodling.assurance_graph import (
            assurance_graph,
            assurance_runtime,
            initial_state,
        )
        from broodling.submission import SubmissionCoordinator
        from broodling.zeroshot_sdk import canonical_request

        self.retry_coordinator().allocate(self.attempt_id, "retry")
        for issue, (name, adapter) in enumerate(self.overlapping_adapters(), start=240):
            sibling = self.sibling_attempt(issue)
            assignment = self.store.worktree_assignment(sibling.attempt_id)
            revision = self.store.get_contract_revision(sibling.contract_revision_id)
            key = f"broodling:v1:{sibling.attempt_id}"
            # A historical prepared row admitted through the pre-fix ordinary
            # preparation bypass. No invalid target or alternate graph is used.
            request = canonical_request(
                {
                    "submissionKey": key,
                    "title": "Broodling V1 assurance Attempt",
                    "graph": assurance_graph(),
                    "runtime": assurance_runtime(),
                    "initialInput": initial_state(
                        revision.canonical_bytes.decode(), sibling.b1_commit_oid
                    ),
                    "workspace": assignment.worktree_path,
                    "repository": sibling.b1_repository,
                    "branch": assignment.branch,
                    "startingCommit": sibling.b1_commit_oid,
                    "materialSha256": sibling.b1_material_sha256,
                    "originUrl": git.origin_url(assignment.path),
                    "target": adapter.target,
                }
            )
            self.assertEqual(json.loads(request)["target"], adapter.target)
            with self.store._write() as connection:
                connection.execute(
                    "INSERT INTO attempt_submissions (attempt_id, submission_key, request_json, state) "
                    "VALUES (?, ?, ?, 'prepared')",
                    (sibling.attempt_id, key, request),
                )
            coordinator = SubmissionCoordinator(self.store, adapter)
            with (
                self.subTest(overlap=name),
                patch.object(adapter, "submit", return_value="forbidden-run") as submit,
            ):
                with self.assertRaises(UnsupportedRuntime):
                    coordinator.reconcile(sibling.attempt_id)
                submit.assert_not_called()
                self.assertEqual(
                    coordinator.record(sibling.attempt_id).state, "prepared"
                )

    def test_ordinary_prepared_target_blocks_later_retry_reservation(self):
        from broodling.submission import SubmissionCoordinator

        sibling = self.sibling_attempt(250)
        ordinary = SubmissionCoordinator(self.store, self.new_adapter)
        prepared = ordinary.prepare_assurance(sibling.attempt_id)
        with self.assertRaises(UnsupportedRuntime):
            self.retry_coordinator().allocate(self.attempt_id, "retry")
        self.assertIsNone(self.store.retry("retry"))
        self.assertEqual(ordinary.record(sibling.attempt_id), prepared)

    def test_actual_environment_cannot_redirect_ordinary_provider_into_reserved_homes(
        self,
    ):
        from broodling.submission import SubmissionCoordinator

        self.retry_coordinator().allocate(self.attempt_id, "retry")
        reserved = self.new_adapter.codex_profile
        for issue, (variable, destination) in enumerate(
            (
                ("BROODLING_PROFILE_HOME", reserved.profile_home),
                ("BROODLING_ISOLATED_CODEX_HOME", reserved.isolated_codex_home),
            ),
            start=260,
        ):
            sibling = self.sibling_attempt(issue)
            adapter = self.fresh_adapter(f"environment-{issue}")
            adapter.target["environment"][variable] = str(destination)
            coordinator = SubmissionCoordinator(self.store, adapter)
            with self.subTest(variable=variable):
                with self.assertRaises(UnsupportedRuntime):
                    coordinator.prepare_assurance(sibling.attempt_id)
                self.assertIsNone(coordinator.record(sibling.attempt_id))

    def test_ordinary_runtime_state_cannot_write_inside_reserved_homes(self):
        from broodling.submission import SubmissionCoordinator

        self.retry_coordinator().allocate(self.attempt_id, "retry")
        reserved = self.new_adapter.codex_profile
        for issue, home in enumerate(
            (reserved.profile_home, reserved.isolated_codex_home), start=270
        ):
            sibling = self.sibling_attempt(issue)
            adapter = self.fresh_adapter(f"runtime-state-{issue}")
            adapter.target["stateDir"] = str(home / "ordinary-runtime")
            coordinator = SubmissionCoordinator(self.store, adapter)
            with self.subTest(home=home):
                with self.assertRaises(UnsupportedRuntime):
                    coordinator.prepare_assurance(sibling.attempt_id)
                self.assertIsNone(coordinator.record(sibling.attempt_id))
                self.assertFalse((home / "ordinary-runtime").exists())

    def test_ordinary_workspace_allocation_cannot_write_into_reserved_homes(self):
        from broodling import AttemptProvisioner

        # Use supported durable placement, so the reservation must discriminate
        # the conflict rather than the unrelated temporary-root prohibition.
        home = self.workspace_root / "reserved-home"
        auth = self.workspace_root / "reserved-auth"
        home.mkdir()
        auth.mkdir()
        (auth / "auth.json").write_text("{}")
        adapter = self.adapter_for(home, auth)
        self.retry_coordinator(adapter).allocate(self.attempt_id, "retry")
        for issue, root in enumerate((home, auth), start=280):
            unit = self.store.resolve_work_unit(work_reference(issue=issue))
            source = self.store.entitle_source(
                unit.work_unit_id,
                SourceSubmission(
                    kind="primary_issue",
                    locator=unit.issue_locator,
                    content=b"Independent ordinary allocation",
                ),
            )
            revision = self.store.record_contract_revision(self.contract(unit, source))
            self.store.admit(revision.contract_revision_id)
            before = tuple(root.iterdir())
            with self.subTest(root=root):
                with self.assertRaises(UnsupportedRuntime):
                    AttemptProvisioner(self.store, root).admit_and_provision(
                        revision.contract_revision_id, self.repository
                    )
                self.assertIsNone(self.store.current_attempt(unit.work_unit_id))
                self.assertEqual(tuple(root.iterdir()), before)
