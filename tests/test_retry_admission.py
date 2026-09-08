"""Explicit retry allocation is durable, isolated and conditional on safe retirement."""

import asyncio
import concurrent.futures
import sqlite3
from dataclasses import replace
from pathlib import Path
from unittest.mock import patch

from schema_support import restore_published_schema
from submission_support import SubmissionCase
from support import move_head

from broodling import (
    AbandonmentCoordinator,
    AttemptAdmissionError,
    AttemptConflict,
    BroodlingStore,
)
from broodling.codex_profile import QualifiedCodexProfile
from broodling.errors import SourceAttributionError, StaleAttempt
from broodling.zeroshot_sdk import ZeroshotSubmitter


class RetryAdmissionTests(SubmissionCase):
    def setUp(self):
        super().setUp()
        executable = self.root / "controlled-codex"
        executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        executable.chmod(0o755)
        self.executable = executable
        self.target = self.fresh_target("second")

    def fresh_target(self, label):
        home = self.root / (label + "-home")
        home.mkdir()
        auth = self.root / (label + "-auth")
        auth.mkdir()
        (auth / "auth.json").write_text("{}")
        return ZeroshotSubmitter(
            self.root / (label + "-runtime"),
            codex_profile=QualifiedCodexProfile(self.executable, home, auth),
        ).target

    def attempt_count(self):
        return self.store.connection.execute(
            "SELECT count(*) FROM attempts"
        ).fetchone()[0]

    def retire(self, attempt_id=None):
        attempt_id = attempt_id or self.attempt_id
        admin = AbandonmentCoordinator(self.store, self.adapter)
        asyncio.run(admin.stop(attempt_id, "explicit retry"))
        return admin.retire(attempt_id)

    def retry(self, retry_id="request-1", predecessor=None, **changes):
        return self.store.admit_retry(
            predecessor or self.attempt_id,
            retry_id,
            **(
                {"workspace_root": self.workspace_root, "target": self.target} | changes
            ),
        )

    def test_allocation_waits_for_abandonment_cessation_and_retirement(self):
        with self.assertRaises(AttemptAdmissionError):
            self.retry()
        self.store.abandon_attempt(self.attempt_id, "not safe yet")
        with self.assertRaises(AttemptAdmissionError):
            self.retry()
        admin = AbandonmentCoordinator(self.store, self.adapter)
        asyncio.run(admin.stop(self.attempt_id, "stop"))
        with self.assertRaises(AttemptAdmissionError):
            self.retry()
        admin.retire(self.attempt_id)
        self.assertTrue(self.retry().is_current)

    def test_new_identity_preserves_original_bindings_before_any_host_change(self):
        original_source = self.store.contract_source_material(
            self.attempt.contract_revision_id
        )
        self.retire()
        move_head(self.repository)
        replacement = self.retry()
        self.assertNotEqual(replacement.attempt_id, self.attempt_id)
        for binding in (
            "work_unit_id",
            "contract_revision_id",
            "b1_repository",
            "b1_commit_oid",
            "b1_material_sha256",
            "b1_requested_revision",
        ):
            self.assertEqual(
                getattr(replacement, binding), getattr(self.attempt, binding)
            )
        allocation = self.store.worktree_assignment(replacement.attempt_id)
        self.assertNotEqual(
            allocation.branch, self.store.worktree_assignment(self.attempt_id).branch
        )
        self.assertFalse(allocation.path.exists())
        self.assertEqual(allocation.state, "allocated")
        self.assertIsNone(self.coordinator.record(replacement.attempt_id))
        self.assertEqual(
            self.store.contract_source_material(self.attempt.contract_revision_id),
            original_source,
        )
        self.reopen()
        self.assertEqual(self.retry(), replacement)
        self.assertEqual(self.store.retry("request-1").target, self.target)

    def test_conflicting_identity_target_and_stale_predecessor_cannot_allocate(self):
        self.retire()
        replacement = self.retry()
        for changes in (
            {"retry_id": "other"},
            {"target": {"profile": "changed"}},
            {"workspace_root": self.workspace_root / "other"},
            {"predecessor": replacement.attempt_id},
        ):
            with self.assertRaises(AttemptConflict):
                self.retry(**changes)
        self.retire(replacement.attempt_id)
        third = self.retry(
            "request-2", replacement.attempt_id, target=self.fresh_target("third")
        )
        self.assertEqual(self.retry().attempt_id, replacement.attempt_id)
        self.assertEqual(self.store.current_attempt(third.work_unit_id), third)
        with self.assertRaises(AttemptConflict):
            self.retry("late-other")
        with self.assertRaises(StaleAttempt):
            self.provisioner().admit(
                self.revision.contract_revision_id, self.repository
            )

    def test_concurrent_identical_requests_converge(self):
        self.retire()

        def invoke(_):
            with BroodlingStore.open(self.store_path) as store:
                return store.admit_retry(
                    self.attempt_id,
                    "request-1",
                    workspace_root=self.workspace_root,
                    target=self.target,
                )

        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
            attempts = list(executor.map(invoke, range(8)))
        self.assertEqual(len({item.attempt_id for item in attempts}), 1)
        self.assertEqual(self.attempt_count(), 2)

    def test_allocation_failure_rolls_back_lineage_and_attempt(self):
        self.retire()
        with (
            patch.object(
                self.store, "_claim_worktree", side_effect=RuntimeError("crash")
            ),
            self.assertRaises(RuntimeError),
        ):
            self.retry()
        self.assertIsNone(self.store.retry("request-1"))
        self.assertEqual(self.attempt_count(), 1)
        self.assertTrue(self.retry().is_current)

    def test_missing_original_object_and_corrupt_frozen_bytes_fail_before_allocation(
        self,
    ):
        self.retire()
        with (
            patch(
                "broodling.store.git.read_object",
                side_effect=ValueError("missing original"),
            ),
            self.assertRaises(ValueError),
        ):
            self.retry()
        material = self.store.contract_source_material(
            self.revision.contract_revision_id
        )
        with (
            patch.object(
                self.store,
                "contract_source_material",
                return_value=(replace(material[0], content=b"abandoned canary"),),
            ),
            self.assertRaises(SourceAttributionError),
        ):
            self.retry()
        self.assertIsNone(self.store.retry("request-1"))
        self.assertEqual(self.attempt_count(), 1)

    def test_schema6_migration_preserves_all_previous_rows(self):
        restore_published_schema(self.store, 6)
        self.retire()
        tables = [
            row[0]
            for row in self.store.connection.execute(
                "SELECT name FROM sqlite_schema WHERE type='table' AND name <> 'schema_meta'"
            )
        ]
        before = {
            name: list(
                map(tuple, self.store.connection.execute(f"SELECT * FROM {name}"))
            )
            for name in tables
        }
        self.reopen()
        for name, rows in before.items():
            self.assertEqual(
                list(
                    map(tuple, self.store.connection.execute(f"SELECT * FROM {name}"))
                ),
                rows,
            )
        self.assertTrue(self.retry().is_current)

    def test_sql_lineage_cannot_be_rewritten_or_deleted(self):
        self.retire()
        self.retry()
        for statement in (
            "DELETE FROM attempt_retries",
            "UPDATE attempt_retries SET target_json = '{}'",
            "INSERT OR REPLACE INTO attempt_retries SELECT * FROM attempt_retries",
        ):
            with self.assertRaises(sqlite3.IntegrityError):
                self.store.connection.execute(statement)

    def test_first_dispatch_rechecks_profile_and_rejects_changed_target(self):
        from broodling.errors import UnsupportedRuntime

        self.retire()
        replacement = self.retry()
        with self.store._write():
            self.store._validate_retry_profile(replacement.attempt_id, self.target)
        with self.assertRaises(AttemptConflict):
            self.store._validate_retry_profile(replacement.attempt_id, {})
        profile_home = self.target["codexProfile"]["profileHome"]
        (Path(profile_home) / "old-session-canary").write_text("old provider context")
        with self.assertRaises(UnsupportedRuntime):
            self.store._validate_retry_profile(replacement.attempt_id, self.target)
        # Administrative identity remains convergent despite later host drift.
        self.assertEqual(self.retry(), replacement)

    def test_missing_attribution_set_fails_before_allocation(self):
        self.retire()
        with (
            patch.object(self.store, "contract_source_material", return_value=()),
            self.assertRaises(SourceAttributionError),
        ):
            self.retry()
        self.assertIsNone(self.store.retry("request-1"))

    def test_sql_cannot_bypass_retirement_or_change_retry_b1(self):
        self.retire()
        replacement = self.retry()
        self.store.abandon_attempt(replacement.attempt_id, "not ceased")
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "INSERT INTO attempt_retries VALUES ('unsafe', ?, 'unsafe-id', '/', '{}', 'now')",
                (replacement.attempt_id,),
            )
        self.retire(replacement.attempt_id)
        with self.assertRaises(sqlite3.IntegrityError), self.store._write():
            self.store.connection.execute(
                "INSERT INTO attempt_retries VALUES ('forged', ?, 'forged-id', '/', '{}', 'now')",
                (replacement.attempt_id,),
            )
            self.store.connection.execute(
                "INSERT INTO attempts SELECT 'forged-id', work_unit_id, contract_revision_id, 1, "
                "b1_repository, ?, b1_material_sha256, b1_requested_revision, 'now' "
                "FROM attempts WHERE attempt_id = ?",
                ("f" * 40, replacement.attempt_id),
            )
        self.assertIsNone(self.store.retry("forged"))

    def test_retry_root_cannot_be_inside_original_repository_or_common_git(self):
        self.retire()
        for root in (
            self.repository,
            self.repository / "nested-retry-workspaces",
            Path(self.attempt.b1_repository) / "nested-retry-workspaces",
        ):
            with self.subTest(root=root), self.assertRaises(AttemptAdmissionError):
                self.retry(workspace_root=root)
            self.assertIsNone(self.store.retry("request-1"))
            self.assertEqual(self.attempt_count(), 1)

    def test_retry_root_cannot_be_inside_another_registered_checkout(self):
        from support import git

        self.retire()
        sibling = self.workspace_root / "registered-sibling"
        git(
            self.repository, "worktree", "add", "-b", "retry-root-sibling", str(sibling)
        )
        with self.assertRaises(AttemptAdmissionError):
            self.retry(workspace_root=sibling / "nested-retry-workspaces")
        self.assertIsNone(self.store.retry("request-1"))
        self.assertEqual(self.attempt_count(), 1)
