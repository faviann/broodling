"""Durable ineligibility only: no terminal label or cessation assertion."""

import multiprocessing
import os
import sqlite3
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from threading import Event
from unittest.mock import patch

from schema_support import restore_published_schema
from support import AttemptTestCase, move_head, work_reference

from broodling import BroodlingStore
from broodling.errors import SchemaVersionMismatch, StaleAttempt
from broodling.provisioning import AttemptProvisioner
from broodling.schema import SCHEMA_VERSION


def abandon_and_die(store_path, attempt_id, after_commit):
    with BroodlingStore.open(store_path) as store:
        original = store._write

        @contextmanager
        def interrupted_write():
            with original() as connection:
                yield connection
                if not after_commit:
                    os._exit(71)
            os._exit(72)

        store._write = interrupted_write
        store.abandon_attempt(attempt_id, "caller lost")


class AbandonmentFoundationTests(AttemptTestCase):
    def setUp(self):
        super().setUp()
        self.attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        self.attempt_id = self.attempt.attempt_id

    def test_abandonment_is_durable_and_first_reason_wins(self):
        first = self.store.abandon_attempt(self.attempt_id, "runtime unavailable")
        self.assertIsNone(self.store.current_attempt(self.work_unit.work_unit_id))
        self.reopen()
        self.assertEqual(self.store.abandonment(self.attempt_id), first)
        self.assertEqual(
            self.store.abandon_attempt(self.attempt_id, "late caller"), first
        )
        self.assertFalse(self.store.get_attempt(self.attempt_id).is_current)
        self.assertEqual(first.reason, "runtime unavailable")

    def test_reason_required_without_losing_currentness(self):
        for reason in ("", " \n", None):
            with self.subTest(reason=reason), self.assertRaises(ValueError):
                self.store.abandon_attempt(self.attempt_id, reason)
        self.assertEqual(
            self.store.require_current_attempt(self.attempt_id), self.attempt
        )

    def test_sql_cannot_remove_authority_without_abandonment_or_rebind(self):
        for assignment in (
            "is_current = 0",
            "b1_repository = 'elsewhere'",
            "b1_requested_revision = 'elsewhere'",
            "admitted_at = 'elsewhere'",
        ):
            with (
                self.subTest(assignment=assignment),
                self.assertRaises(sqlite3.IntegrityError),
            ):
                self.store.connection.execute(f"UPDATE attempts SET {assignment}")
        self.store.abandon_attempt(self.attempt_id, "stop")
        for sql in (
            "UPDATE attempts SET is_current = 1",
            "UPDATE attempt_abandonments SET reason = 'changed'",
            "DELETE FROM attempt_abandonments",
            "INSERT OR REPLACE INTO attempt_abandonments SELECT * FROM attempt_abandonments",
            "UPDATE worktree_assignments SET state = 'provisioned', provisioned_at = 'late'",
        ):
            with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                self.store.connection.execute(sql)

    def test_stale_admission_and_provisioning_do_not_create_scaffolding(self):
        assignment = self.store.worktree_assignment(self.attempt_id)
        self.store.abandon_attempt(self.attempt_id, "cancel before provisioning")
        with self.assertRaises(StaleAttempt):
            self.provisioner().admit(
                self.revision.contract_revision_id, self.repository
            )
        with self.assertRaises(StaleAttempt):
            self.provisioner().provision(self.attempt_id)
        with self.assertRaises(StaleAttempt):
            self.store.acknowledge_worktree_provisioned(self.attempt_id)
        self.assertFalse(assignment.path.parent.exists())

    def test_replace_cannot_rebind_abandoned_attempt_to_another_work_unit(self):
        other = self.store.resolve_work_unit(work_reference(issue=999))
        self.store.abandon_attempt(self.attempt_id, "stop")
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "INSERT OR REPLACE INTO attempts SELECT attempt_id, ?, contract_revision_id, "
                "1, b1_repository, b1_commit_oid, b1_material_sha256, "
                "b1_requested_revision, admitted_at FROM attempts",
                (other.work_unit_id,),
            )
        self.assertEqual(
            self.store.get_attempt(self.attempt_id).work_unit_id,
            self.work_unit.work_unit_id,
        )
        self.assertIsNone(self.store.current_attempt(other.work_unit_id))
        self.assertIsNotNone(self.store.abandonment(self.attempt_id))

    def test_changed_b1_cannot_bypass_empty_current_slot(self):
        self.store.abandon_attempt(self.attempt_id, "stop")
        move_head(self.repository)
        with self.assertRaises(StaleAttempt):
            self.provisioner().admit(
                self.revision.contract_revision_id, self.repository
            )
        self.assertEqual(
            self.store.connection.execute("SELECT count(*) FROM attempts").fetchone()[
                0
            ],
            1,
        )
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "INSERT INTO attempts SELECT 'different', work_unit_id, contract_revision_id, "
                "1, b1_repository, b1_commit_oid, b1_material_sha256, "
                "b1_requested_revision, admitted_at FROM attempts"
            )

    def test_concurrent_abandoners_converge(self):
        def abandon(reason):
            with BroodlingStore.open(self.store_path) as store:
                return store.abandon_attempt(self.attempt_id, reason)

        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(abandon, ("first", "second")))
        self.assertEqual(results[0], results[1])

    def test_process_death_before_commit_leaves_whole_current_fact(self):
        self._crash(False)
        self.assertEqual(
            self.store.require_current_attempt(self.attempt_id), self.attempt
        )
        self.assertIsNone(self.store.abandonment(self.attempt_id))

    def test_process_death_after_commit_leaves_whole_abandonment_fact(self):
        self._crash(True)
        self.assertIsNotNone(self.store.abandonment(self.attempt_id))
        with self.assertRaises(StaleAttempt):
            self.store.require_current_attempt(self.attempt_id)

    def _crash(self, after_commit):
        process = multiprocessing.get_context("fork").Process(
            target=abandon_and_die,
            args=(self.store_path, self.attempt_id, after_commit),
        )
        process.start()
        process.join(10)
        if process.is_alive():
            process.kill()
            process.join()
            self.fail("abandonment child failed to finish")
        self.assertEqual(process.exitcode, 72 if after_commit else 71)
        self.reopen()

    def test_provision_waiter_rechecks_after_abandonment(self):
        from broodling import provisioning

        actual_lock = provisioning._sole_provisioner

        @contextmanager
        def abandon_before_lock(enclosure):
            self.store.abandon_attempt(self.attempt_id, "abandoned while queued")
            with actual_lock(enclosure):
                yield

        with (
            patch.object(provisioning, "_sole_provisioner", abandon_before_lock),
            self.assertRaises(StaleAttempt),
        ):
            self.provisioner().provision(self.attempt_id)
        self.assertFalse(self.store.worktree_assignment(self.attempt_id).path.exists())

    def test_abandonment_cannot_commit_during_git_materialization(self):
        entered, release, abandoning = Event(), Event(), Event()
        original = AttemptProvisioner._create

        def paused_create(provisioner, *args, **kwargs):
            entered.set()
            if not release.wait(5):
                raise RuntimeError("test failed to release materialization")
            return original(provisioner, *args, **kwargs)

        def provision():
            with BroodlingStore.open(self.store_path) as store:
                return AttemptProvisioner(store, self.workspace_root).provision(
                    self.attempt_id
                )

        def abandon():
            with BroodlingStore.open(self.store_path) as store:
                abandoning.set()
                return store.abandon_attempt(self.attempt_id, "stop racing Git")

        with (
            patch.object(AttemptProvisioner, "_create", paused_create),
            ThreadPoolExecutor(max_workers=2) as pool,
        ):
            provisioned = pool.submit(provision)
            try:
                self.assertTrue(entered.wait(5))
                abandoned = pool.submit(abandon)
                self.assertTrue(abandoning.wait(5))
                self.assertIsNone(self.store.abandonment(self.attempt_id))
                self.assertFalse(abandoned.done())
            finally:
                release.set()
            self.assertTrue(provisioned.result(5).assignment.provisioned)
            abandoned.result(5)
        with self.assertRaises(StaleAttempt):
            self.provisioner().provision(self.attempt_id)

    # Migration copies populated original-v4 DDL, not a current-schema database
    # whose metadata merely claims an older version.
    def test_exact_v4_migration_preserves_bindings_correlation_and_custody(self):
        self.provisioner().provision(self.attempt_id)
        connection = self.store.connection
        connection.execute(
            "INSERT INTO attempt_submissions VALUES (?, 'key', '{}', 'prepared', NULL, NULL)",
            (self.attempt_id,),
        )
        connection.execute("UPDATE attempt_submissions SET state = 'dispatched'")
        connection.execute(
            "UPDATE attempt_submissions SET state = 'correlated', zeroshot_run_id = 'run'"
        )
        connection.execute(
            "INSERT INTO final_assurance VALUES (?, 'run', '{}')", (self.attempt_id,)
        )
        before = {}
        for table in (
            "work_units",
            "work_unit_submissions",
            "entitled_sources",
            "contract_revisions",
            "contract_source_attributions",
            "admission_decisions",
            "attempts",
            "worktree_assignments",
            "attempt_submissions",
            "final_assurance",
        ):
            before[table] = [
                tuple(row) for row in connection.execute(f"SELECT * FROM {table}")
            ]
        restore_published_schema(self.store, 4)
        with BroodlingStore.open(self.store_path) as upgraded:
            self.assertEqual(
                upgraded.schema_meta()["schema_version"], str(SCHEMA_VERSION)
            )
            for table, rows in before.items():
                self.assertEqual(
                    [
                        tuple(row)
                        for row in upgraded.connection.execute(f"SELECT * FROM {table}")
                    ],
                    rows,
                )
            upgraded.abandon_attempt(self.attempt_id, "post-migration stop")
            self.assertEqual(
                upgraded.connection.execute(
                    "SELECT record_json FROM final_assurance"
                ).fetchone()[0],
                "{}",
            )

    def test_concurrent_v4_openers_migrate_once(self):
        restore_published_schema(self.store, 4)

        def reopen(_):
            with BroodlingStore.open(self.store_path) as store:
                return store.schema_meta()

        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(reopen, range(2)))
        self.assertEqual(results[0], results[1])
        self.assertEqual(results[0]["schema_version"], str(SCHEMA_VERSION))

    def test_unknown_v4_definition_is_not_migrated(self):
        restore_published_schema(self.store, 4)
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'foreign' WHERE key = 'schema_sha256'"
        )
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.assertIsNone(
            self.store.connection.execute(
                "SELECT name FROM sqlite_schema WHERE name = 'attempt_abandonments'"
            ).fetchone()
        )
