"""One immutable P3 custody row preserves the admission and correlation nucleus."""

import sqlite3
from concurrent.futures import ThreadPoolExecutor
from unittest.mock import patch

from submission_support import SubmissionCase

from broodling import BroodlingStore, SchemaVersionMismatch
from broodling.schema import SCHEMA_SHA256, SCHEMA_VERSION, V3_SCHEMA_SHA256


class AssuranceStorageTests(SubmissionCase):
    def insert(self, run_id="correlated-run"):
        self.store.connection.execute(
            "INSERT INTO final_assurance (attempt_id, zeroshot_run_id, record_json) "
            "VALUES (?, ?, ?)",
            (self.attempt_id, run_id, '{"format":"fixture-custody"}'),
        )

    def correlate(self):
        with patch.object(self.adapter, "submit", return_value="correlated-run"):
            self.submit()

    def test_uncorrelated_or_foreign_run_cannot_acquire_custody(self):
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert()
        self.correlate()
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert("foreign-run")
        self.insert()
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert()

    def test_record_is_immutable_and_survives_reopen_without_runtime_access(self):
        self.correlate()
        self.insert()
        for sql in (
            "UPDATE final_assurance SET record_json = '{}'",
            "DELETE FROM final_assurance",
        ):
            with self.subTest(sql=sql), self.assertRaises(sqlite3.IntegrityError):
                self.store.connection.execute(sql)
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "INSERT OR REPLACE INTO final_assurance VALUES (?, ?, '{}')",
                (self.attempt_id, "correlated-run"),
            )
        self.restart()
        row = self.store.connection.execute("SELECT * FROM final_assurance").fetchone()
        self.assertEqual(
            tuple(row),
            (self.attempt_id, "correlated-run", '{"format":"fixture-custody"}'),
        )

    def make_v3(self):
        self.store.connection.execute("DROP TABLE final_assurance")
        self.store.connection.executemany(
            "UPDATE schema_meta SET value = ? WHERE key = ?",
            [("3", "schema_version"), (V3_SCHEMA_SHA256, "schema_sha256")],
        )

    def test_exact_v3_migration_preserves_existing_request_and_correlation(self):
        self.correlate()
        before = self.coordinator.record(self.attempt_id)
        self.make_v3()
        self.restart()
        self.assertEqual(self.coordinator.record(self.attempt_id), before)
        self.assertEqual(
            self.store.schema_meta()["schema_version"], str(SCHEMA_VERSION)
        )
        self.assertEqual(self.store.schema_meta()["schema_sha256"], SCHEMA_SHA256)
        self.assertEqual(
            self.store.connection.execute(
                "SELECT count(*) FROM final_assurance"
            ).fetchone()[0],
            0,
        )
        self.insert()

    def test_concurrent_v3_openers_migrate_once(self):
        self.make_v3()
        self.store.close()

        def open_store(_):
            with BroodlingStore.open(self.store_path) as store:
                return store.schema_meta()["schema_sha256"]

        with ThreadPoolExecutor(max_workers=2) as pool:
            self.assertEqual(list(pool.map(open_store, range(2))), [SCHEMA_SHA256] * 2)

    def test_unknown_v3_definition_does_not_gain_custody_schema(self):
        self.make_v3()
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'foreign' WHERE key = 'schema_sha256'"
        )
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.assertFalse(
            self.store.connection.execute(
                "SELECT name FROM sqlite_schema WHERE name = 'final_assurance'"
            ).fetchall()
        )
