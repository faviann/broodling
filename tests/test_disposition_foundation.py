"""Storage/migration negatives independent of provider or runtime availability."""

import dataclasses
import json
import sqlite3
from concurrent.futures import ThreadPoolExecutor
from unittest.mock import patch

from schema_support import restore_published_schema
from submission_support import SubmissionCase

from broodling import BroodlingStore, SchemaVersionMismatch
from broodling.disposition import WorkUnitDispositionCoordinator
from broodling.errors import SubmissionNotReady
from broodling.schema import SCHEMA_SHA256, SCHEMA_VERSION


class DispositionFoundationTests(SubmissionCase):
    def correlate(self):
        with patch.object(self.adapter, "submit", return_value="correlated-run"):
            self.submit()

    def marker(self):
        self.store.connection.execute(
            "INSERT INTO attempt_finalizations VALUES (?, 'started')",
            (self.attempt_id,),
        )

    def custody(self, **changes):
        payload = {
            "attemptId": self.attempt_id,
            "contractRevisionId": self.attempt.contract_revision_id,
            "runId": "correlated-run",
        }
        payload.update(changes)
        self.store.connection.execute(
            "INSERT INTO final_assurance VALUES (?, 'correlated-run', ?)",
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
            "INSERT INTO work_unit_dispositions VALUES (?, ?, ?, 'SUCCEEDED', 'completed')",
            (binding["work"], binding["contract"], binding["attempt"]),
        )

    def test_uncorrelated_or_abandoned_attempt_cannot_start_finalization(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.marker()
        self.correlate()
        self.store.abandon_attempt(self.attempt_id, "stop first")
        with self.assertRaises(sqlite3.IntegrityError):
            self.marker()

    def test_preexisting_custody_cannot_acquire_new_finalization_marker(self):
        self.correlate()
        self.custody()
        with self.assertRaises(sqlite3.IntegrityError):
            self.marker()
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()

    def test_marker_cannot_be_reset_or_replaced_after_interruption(self):
        self.correlate()
        self.marker()
        for sql in (
            "DELETE FROM attempt_finalizations",
            "UPDATE attempt_finalizations SET started_at = 'again'",
            "INSERT OR REPLACE INTO attempt_finalizations SELECT * FROM attempt_finalizations",
        ):
            with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                self.store.connection.execute(sql)

    def test_missing_custody_and_conflicting_bound_identity_cannot_complete(self):
        self.correlate()
        self.marker()
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()
        self.custody(runId="foreign-run")
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()
        for changes in (
            {"work": "foreign-work"},
            {"contract": "foreign-contract"},
            {"attempt": "foreign-attempt"},
        ):
            with (
                self.subTest(changes=changes),
                self.assertRaises(sqlite3.IntegrityError),
            ):
                self.insert_disposition(**changes)

    def test_explicit_empty_effect_set_never_defaults_from_missing_or_unsupported(self):
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
                coordinator._require_no_effect_contract(self.attempt_id)
        self.assertEqual(
            coordinator._require_no_effect_contract(self.attempt_id), self.attempt
        )

    def test_exact_v7_migration_preserves_historical_custody_without_eligibility(self):
        self.correlate()
        self.custody()
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
        self.restart()
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
            self.marker()

    def test_concurrent_v7_openers_migrate_once(self):
        restore_published_schema(self.store, 7)

        def open_store(_):
            with BroodlingStore.open(self.store_path) as store:
                return store.schema_meta()

        with ThreadPoolExecutor(max_workers=2) as pool:
            records = list(pool.map(open_store, range(2)))
        self.assertEqual(records[0], records[1])
        self.assertEqual(records[0]["schema_sha256"], SCHEMA_SHA256)

    def test_unknown_v7_definition_cannot_gain_disposition_tables(self):
        restore_published_schema(self.store, 7)
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'foreign' WHERE key = 'schema_sha256'"
        )
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.assertIsNone(
            self.store.connection.execute(
                "SELECT name FROM sqlite_schema WHERE name = 'work_unit_dispositions'"
            ).fetchone()
        )
