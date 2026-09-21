"""Storage/migration negatives independent of provider or runtime availability."""

import dataclasses
import hashlib
import json
import sqlite3
from concurrent.futures import ThreadPoolExecutor
from dataclasses import replace
from unittest.mock import patch

from schema_support import published_schema, restore_published_schema
from submission_support import SubmissionCase, configured_adapter
from support import work_reference

from broodling import Broodling, BroodlingStore, RequiredEffect, SchemaVersionMismatch
from broodling.disposition import WorkUnitDispositionCoordinator
from broodling.errors import SubmissionNotReady
from broodling.schema import (
    SCHEMA_SHA256,
    SCHEMA_VERSION,
    V9_SCHEMA_SHA256,
    V10_SCHEMA_SHA256,
)
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL


class DispositionFoundationTests(SubmissionCase):
    def correlate(self):
        with patch.object(self.adapter, "submit", return_value="correlated-run"):
            self.submit()

    def custody(self, **changes):
        payload = {
            "format": "broodling.final-assurance/v2",
            "attemptId": self.attempt_id,
            "contractRevisionId": self.attempt.contract_revision_id,
            "runId": "correlated-run",
        }
        payload.update(changes)
        self.store.connection.execute(
            "INSERT INTO final_assurance VALUES (?, 'correlated-run', ?)",
            (self.attempt_id, json.dumps(payload)),
        )

    def insert_disposition(self):
        self.store.connection.execute(
            "INSERT INTO work_unit_dispositions VALUES (?, ?, ?, 'SUCCEEDED', 'completed')",
            (
                self.attempt.work_unit_id,
                self.attempt.contract_revision_id,
                self.attempt_id,
            ),
        )

    def test_explicit_empty_effect_set_never_defaults_from_missing_or_unsupported(self):
        self.correlate()
        coordinator = WorkUnitDispositionCoordinator(self.store, self.adapter)
        original = self.store.get_contract_revision(self.attempt.contract_revision_id)
        for declaration in (None, "missing", {}, ["push"], "[]", False):
            mapping = json.loads(original.canonical_bytes)
            if declaration == "missing":
                del mapping["requiredEffects"]
            else:
                mapping["requiredEffects"] = declaration
            revision = dataclasses.replace(
                original, canonical_bytes=json.dumps(mapping).encode()
            )
            with (
                self.subTest(declaration=declaration),
                patch.object(
                    self.store, "get_contract_revision", return_value=revision
                ),
                self.assertRaises(SubmissionNotReady),
            ):
                coordinator._bound(self.attempt_id)
        self.assertEqual(coordinator._bound(self.attempt_id)[0], self.attempt)

    def test_exact_v7_migration_preserves_historical_custody_without_eligibility(self):
        self.correlate()
        self.custody(format="broodling.final-assurance/v1")
        before = {
            table: [
                tuple(row)
                for row in self.store.connection.execute(f"SELECT * FROM {table}")
            ]
            for table in (
                "attempts",
                "attempt_submissions",
                "final_assurance",
                "contract_revisions",
            )
        }
        restore_published_schema(self.store, 7)
        self.upgrade()
        self.assertEqual(
            self.store.schema_meta()["schema_version"], str(SCHEMA_VERSION)
        )
        self.assertEqual(self.store.schema_meta()["schema_sha256"], SCHEMA_SHA256)
        for table, rows in before.items():
            self.assertEqual(
                [
                    tuple(row)
                    for row in self.store.connection.execute(f"SELECT * FROM {table}")
                ],
                rows,
            )
        self.assertEqual(
            self.store.connection.execute(
                "SELECT count(*) FROM work_unit_dispositions"
            ).fetchone()[0],
            0,
        )
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()

    def test_concurrent_v7_upgraders_migrate_once(self):
        restore_published_schema(self.store, 7)

        def upgrade_store(_):
            with BroodlingStore.upgrade(self.store_path) as store:
                return store.schema_meta()

        with ThreadPoolExecutor(max_workers=2) as pool:
            records = list(pool.map(upgrade_store, range(2)))
        self.assertEqual(records[0], records[1])
        self.assertEqual(records[0]["schema_sha256"], SCHEMA_SHA256)

    def test_exact_v8_migration_preserves_completed_historical_evidence(self):
        self.correlate()
        restore_published_schema(self.store, 8)
        self.store.connection.execute(
            "INSERT INTO attempt_finalizations VALUES (?, 'historical-start')",
            (self.attempt_id,),
        )
        self.custody(format="broodling.final-assurance/v1")
        self.insert_disposition()
        before = WorkUnitDispositionCoordinator(self.store, self.adapter).record(
            self.attempt_id
        )
        self.upgrade()
        self.assertEqual(self.store.schema_meta()["schema_sha256"], SCHEMA_SHA256)
        self.assertEqual(
            WorkUnitDispositionCoordinator(self.store, self.adapter).record(
                self.attempt_id
            ),
            before,
        )
        self.assertEqual(before.result["format"], "broodling.final-assurance/v1")
        self.assertIsNone(self.store.current_attempt(self.attempt.work_unit_id))
        self.assertIsNone(
            self.store.connection.execute(
                "SELECT 1 FROM sqlite_schema WHERE name = 'attempt_finalizations'"
            ).fetchone()
        )

    def test_v8_interrupted_finalization_marker_confers_no_disposition(self):
        self.correlate()
        restore_published_schema(self.store, 8)
        self.store.connection.execute(
            "INSERT INTO attempt_finalizations VALUES (?, 'interrupted')",
            (self.attempt_id,),
        )
        self.custody(format="broodling.final-assurance/v1")
        self.upgrade()
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()
        self.assertIsNone(
            WorkUnitDispositionCoordinator(self.store, self.adapter).record(
                self.attempt_id
            )
        )

    def test_exact_v9_migration_preserves_completed_historical_result(self):
        self.correlate()
        restore_published_schema(self.store, 9)
        self.custody()
        self.insert_disposition()
        before = WorkUnitDispositionCoordinator(self.store, self.adapter).record(
            self.attempt_id
        )
        self.upgrade()
        self.assertEqual(
            self.store.schema_meta()["schema_version"], str(SCHEMA_VERSION)
        )
        self.assertEqual(self.store.schema_meta()["schema_sha256"], SCHEMA_SHA256)
        self.assertEqual(
            WorkUnitDispositionCoordinator(self.store, self.adapter).record(
                self.attempt_id
            ),
            before,
        )

    def test_interrupted_v9_upgrade_rolls_back_and_remains_explicit(self):
        restore_published_schema(self.store, 9)
        self.store.close()
        original = self.store_path.read_bytes()
        connect = sqlite3.connect

        def deny_trigger_creation(*args, **kwargs):
            connection = connect(*args, **kwargs)
            connection.set_authorizer(
                lambda action, _arg1, _arg2, _database, _trigger: (
                    sqlite3.SQLITE_DENY
                    if action == sqlite3.SQLITE_CREATE_TRIGGER
                    else sqlite3.SQLITE_OK
                )
            )
            return connection

        with (
            patch("broodling.store.sqlite3.connect", side_effect=deny_trigger_creation),
            self.assertRaises(sqlite3.DatabaseError),
        ):
            BroodlingStore.upgrade(self.store_path)

        self.assertEqual(self.store_path.read_bytes(), original)
        database = sqlite3.connect(self.store_path)
        try:
            metadata = dict(database.execute("SELECT key, value FROM schema_meta"))
            self.assertEqual(metadata["schema_version"], "9")
            self.assertEqual(metadata["schema_sha256"], V9_SCHEMA_SHA256)
            self.assertIsNotNone(
                database.execute(
                    "SELECT 1 FROM sqlite_schema "
                    "WHERE type = 'trigger' AND name = 'work_unit_dispositions_bound'"
                ).fetchone()
            )
        finally:
            database.close()

        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.store = BroodlingStore.upgrade(self.store_path)
        self.assertEqual(
            self.store.schema_meta()["schema_version"], str(SCHEMA_VERSION)
        )

    def test_unknown_v7_definition_cannot_gain_disposition_tables(self):
        restore_published_schema(self.store, 7)
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'foreign' WHERE key = 'schema_sha256'"
        )
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.upgrade(self.store_path)
        self.assertIsNone(
            self.store.connection.execute(
                "SELECT name FROM sqlite_schema WHERE name = 'work_unit_dispositions'"
            ).fetchone()
        )


class StableReceiptDatabaseTests(SubmissionCase):
    def contract(self, work_unit, source, **overrides):
        return replace(
            super().contract(work_unit, source, **overrides),
            required_effects=(
                RequiredEffect("deliver", "Open the PR.", "pull_request", "main"),
            ),
            host_assumptions=("single_host", "one_attempt_one_dedicated_worktree"),
        )

    def new_adapter(self):
        return configured_adapter(
            self.runtime_state,
            self.root,
            delivery_target_origin="http://127.0.0.1:8123",
            github_token="token",
            gateway_base_url=V1_GATEWAY_BASE_URL,
            gateway_api_key="provider-key",
        )

    def setUp(self):
        super().setUp()
        with patch.object(self.adapter, "submit", return_value="run"):
            self.submit()

    def payload(self, **receipt_changes):
        receipt = {
            "version": "v1",
            "mode": "pr",
            "outcome": "opened",
            "repository": "faviann/broodling",
            "targetBranch": "main",
            "headRevision": "b" * 40,
            "pullRequestId": "50",
        }
        receipt.update(receipt_changes)
        return {
            "format": "broodling.final-assurance/v3",
            "attemptId": self.attempt_id,
            "contractRevisionId": self.attempt.contract_revision_id,
            "runId": "run",
            "workflow": {"name": "software-change", "delivery": "pull_request"},
            "acceptedRevision": receipt["headRevision"],
            "deliveryReceipt": receipt,
        }

    def insert_custody(self, payload):
        self.store.connection.execute(
            "INSERT INTO final_assurance VALUES (?, 'run', ?)",
            (self.attempt_id, json.dumps(payload)),
        )

    def insert_disposition(self, **changes):
        binding = {
            "work": self.attempt.work_unit_id,
            "contract": self.attempt.contract_revision_id,
            "attempt": self.attempt_id,
        }
        binding.update(changes)
        self.store.connection.execute(
            "INSERT INTO work_unit_dispositions VALUES (?, ?, ?, 'SUCCEEDED', 'now')",
            (binding["work"], binding["contract"], binding["attempt"]),
        )

    def insert_result(self, payload):
        self.insert_custody(payload)
        self.insert_disposition()

    def test_matching_native_receipt_can_complete_at_database_boundary(self):
        payload = self.payload()
        self.insert_result(payload)
        self.assertEqual(
            WorkUnitDispositionCoordinator(self.store, self.adapter).record(
                self.attempt_id
            ).result,
            payload,
        )
        self.assertIsNone(self.store.current_attempt(self.attempt.work_unit_id))

    def test_v10_migration_preserves_result_by_exact_attempt_and_history(self):
        metadata_before = self.store.schema_meta()
        with (
            patch("schema_support.V10_SCHEMA_SHA256", "incorrect"),
            self.assertRaises(AssertionError),
        ):
            restore_published_schema(self.store, 10)
        self.assertEqual(self.store.schema_meta(), metadata_before)

        v10_schema, v10_digest = published_schema(10)
        self.assertEqual(v10_digest, V10_SCHEMA_SHA256)
        self.assertEqual(
            hashlib.sha256(v10_schema.encode("utf-8")).hexdigest(),
            V10_SCHEMA_SHA256,
        )

        payload = self.payload()
        self.insert_result(payload)
        disposition = WorkUnitDispositionCoordinator(self.store, self.adapter)
        before = disposition.record(self.attempt_id)
        before_history = Broodling(
            self.store, self.adapter, self.workspace_root
        ).history(work_reference())
        self.assertEqual(len(before_history), 1)
        self.assertEqual(before_history[0].disposition, before)

        tables = (
            "contract_revisions",
            "attempts",
            "final_assurance",
            "work_unit_dispositions",
        )
        rows_before = {
            table: [
                tuple(row)
                for row in self.store.connection.execute(f"SELECT * FROM {table}")
            ]
            for table in tables
        }
        restore_published_schema(self.store, 10)
        self.assertEqual(self.store.schema_meta()["schema_version"], "10")
        self.assertEqual(
            self.store.schema_meta()["schema_sha256"], V10_SCHEMA_SHA256
        )
        self.upgrade()

        self.assertEqual(self.store.schema_meta()["schema_version"], "11")
        rows_after = {
            table: [
                tuple(row)
                for row in self.store.connection.execute(f"SELECT * FROM {table}")
            ]
            for table in tables
        }
        self.assertEqual(rows_after, rows_before)
        migrated = WorkUnitDispositionCoordinator(self.store, self.adapter)
        self.assertEqual(migrated.record(self.attempt_id), before)
        self.assertEqual(migrated.justification(self.attempt_id), payload)
        after_history = Broodling(
            self.store, self.adapter, self.workspace_root
        ).history(work_reference())
        self.assertEqual(after_history, before_history)
        self.assertEqual(after_history[0].attempt.attempt_id, self.attempt_id)

    def test_missing_custody_cannot_complete_at_database_boundary(self):
        with self.assertRaisesRegex(sqlite3.IntegrityError, "disposition requires"):
            self.insert_disposition()
        self.insert_result(self.payload())

    def test_historical_custody_cannot_gain_new_success(self):
        for version in ("v1", "v2"):
            payload = {**self.payload(), "format": f"broodling.final-assurance/{version}"}
            with (
                self.subTest(version=version),
                self.assertRaisesRegex(sqlite3.IntegrityError, "disposition requires"),
                self.store._write(),
            ):
                self.insert_result(payload)
        self.insert_result(self.payload())

    def test_custody_must_name_the_bound_attempt_contract_and_run(self):
        for field in ("attemptId", "contractRevisionId", "runId"):
            payload = {**self.payload(), field: "foreign-identity"}
            # Roll back rejected custody so each case changes only one binding
            # of the same current, PR-authorized successful control.
            with (
                self.subTest(field=field),
                self.assertRaisesRegex(sqlite3.IntegrityError, "disposition requires"),
                self.store._write(),
            ):
                self.insert_result(payload)
        self.insert_result(self.payload())

    def test_disposition_cannot_complete_another_work_unit_or_contract(self):
        other_work = self.store.resolve_work_unit(work_reference(issue=13))
        contract = self.revision.contract
        other_contract = self.store.record_contract_revision(
            replace(
                contract,
                criteria=(replace(contract.criteria[0], statement="Another task."),),
            )
        )
        self.assertTrue(self.store.admit(other_contract.contract_revision_id).admitted)
        self.insert_custody(self.payload())
        # Both references exist: foreign-key failure cannot explain refusal.
        for changes in (
            {"work": other_work.work_unit_id},
            {"contract": other_contract.contract_revision_id},
        ):
            with (
                self.subTest(changes=changes),
                self.assertRaisesRegex(sqlite3.IntegrityError, "disposition requires"),
            ):
                self.insert_disposition(**changes)
        self.insert_disposition()

    def test_foreign_native_receipt_cannot_complete_at_database_boundary(self):
        with (
            self.assertRaisesRegex(sqlite3.IntegrityError, "disposition requires"),
            self.store._write(),
        ):
            self.insert_result(self.payload(repository="other/project"))
        self.insert_result(self.payload())
