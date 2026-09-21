"""Schema initialization, versioning and store-location boundary."""

from __future__ import annotations

import shutil
import sqlite3
import tempfile
import unittest
from pathlib import Path

from broodling import (
    BroodlingStore,
    SchemaVersionMismatch,
    StoreLocationError,
    WorkReference,
)
from broodling.schema import SCHEMA_SHA256, SCHEMA_VERSION
from broodling.store import default_store_path
from broodling.workspace import DISPOSABLE_WORKTREE_MARKER
from support import StoreTestCase


class SchemaInitializationTests(StoreTestCase):
    def test_records_version_and_definition_digest(self) -> None:
        meta = self.store.schema_meta()
        self.assertEqual(meta["schema_version"], str(SCHEMA_VERSION))
        self.assertEqual(meta["schema_sha256"], SCHEMA_SHA256)

    def test_reopening_preserves_metadata(self) -> None:
        meta_before = self.store.schema_meta()
        reopened = self.reopen()
        self.assertEqual(reopened.schema_meta(), meta_before)

    def test_tables_are_strict(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "INSERT INTO schema_meta (key, value) VALUES (?, ?)", ("n", b"\x00")
            )

    def test_foreign_keys_are_enforced(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "INSERT INTO work_unit_submissions (submission_id, work_unit_id, "
                "submitted_repository, submitted_issue, received_at) "
                "VALUES ('s1', 'wu-absent', 'r', '1', 'now')"
            )


class SchemaVersionTests(StoreTestCase):
    def test_foreign_schema_version_refuses_to_open(self) -> None:
        self.store.connection.execute(
            "UPDATE schema_meta SET value = '99' WHERE key = 'schema_version'"
        )
        self.store.close()
        original = self.store_path.read_bytes()
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.assertEqual(self.store_path.read_bytes(), original)

    def test_foreign_schema_definition_refuses_to_open(self) -> None:
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'deadbeef' WHERE key = 'schema_sha256'"
        )
        self.store.close()
        original = self.store_path.read_bytes()
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.assertEqual(self.store_path.read_bytes(), original)


class ExplicitStoreLifecycleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="broodling-store-lifecycle-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.store_path = self.root / "state" / "broodling.sqlite3"

    def test_initialize_creates_a_new_store_and_open_preserves_its_identity(self) -> None:
        with BroodlingStore.initialize(self.store_path) as initialized:
            self.assertEqual(
                initialized.schema_meta()["schema_version"], str(SCHEMA_VERSION)
            )
            reference = WorkReference.parse("faviann/broodling", 12)
            work_unit = initialized.resolve_work_unit(reference)

        with BroodlingStore.open(self.store_path) as reopened:
            self.assertEqual(
                reopened.resolve_work_unit(reference),
                work_unit,
            )

    def test_initialize_refuses_to_replace_an_existing_store(self) -> None:
        with BroodlingStore.initialize(self.store_path) as initialized:
            reference = WorkReference.parse("faviann/broodling", 12)
            work_unit = initialized.resolve_work_unit(reference)

        with self.assertRaises(FileExistsError):
            BroodlingStore.initialize(self.store_path)

        with BroodlingStore.open(self.store_path) as reopened:
            self.assertEqual(
                reopened.resolve_work_unit(reference),
                work_unit,
            )

    def test_open_refuses_a_missing_store_without_creating_it(self) -> None:
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)

        self.assertFalse(self.store_path.exists())
        self.assertFalse(self.store_path.parent.exists())

    def test_open_refuses_an_unrecognized_database_without_modifying_it(self) -> None:
        self.store_path.parent.mkdir(parents=True)
        with sqlite3.connect(self.store_path) as foreign:
            foreign.execute("CREATE TABLE unrelated_state (value TEXT NOT NULL)")
            foreign.execute("INSERT INTO unrelated_state VALUES ('retained')")
        original = self.store_path.read_bytes()

        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)

        self.assertEqual(self.store_path.read_bytes(), original)


class StoreLocationTests(unittest.TestCase):
    def test_refuses_a_path_inside_a_disposable_worktree(self) -> None:
        root = Path(tempfile.mkdtemp(prefix="broodling-p2-worktree-"))
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        (root / DISPOSABLE_WORKTREE_MARKER).write_text("at-a1\n", encoding="utf-8")
        nested = root / "nested" / "state"
        nested.mkdir(parents=True)
        with self.assertRaises(StoreLocationError):
            BroodlingStore.open(nested / "broodling.sqlite3")

    def test_default_path_is_user_state_not_a_workspace(self) -> None:
        chosen = default_store_path({"XDG_STATE_HOME": "/var/lib/state"})
        self.assertEqual(chosen, Path("/var/lib/state/broodling/broodling.sqlite3"))
        override = default_store_path({"BROODLING_STORE": "/srv/b.sqlite3"})
        self.assertEqual(override, Path("/srv/b.sqlite3"))


if __name__ == "__main__":
    unittest.main()
