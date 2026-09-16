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

    def test_historical_custody_cannot_gain_new_success(self):
        self.correlate()
        self.custody(format="broodling.final-assurance/v1")
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()

    def test_missing_custody_and_conflicting_bound_identity_cannot_complete(self):
        self.correlate()
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
            self.insert_disposition()

    def test_concurrent_v7_openers_migrate_once(self):
        restore_published_schema(self.store, 7)

        def open_store(_):
            with BroodlingStore.open(self.store_path) as store:
                return store.schema_meta()

        with ThreadPoolExecutor(max_workers=2) as pool:
            records = list(pool.map(open_store, range(2)))
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
        self.restart()
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
        self.restart()
        with self.assertRaises(sqlite3.IntegrityError):
            self.insert_disposition()
        self.assertIsNone(
            WorkUnitDispositionCoordinator(self.store, self.adapter).record(
                self.attempt_id
            )
        )

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
