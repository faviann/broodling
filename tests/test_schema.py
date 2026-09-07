"""Schema initialization, versioning and store-location boundary."""

from __future__ import annotations

import shutil
import sqlite3
import tempfile
import unittest
from pathlib import Path

from broodling import BroodlingStore, SchemaVersionMismatch, StoreLocationError
from broodling.schema import SCHEMA_SHA256, SCHEMA_VERSION, TABLES
from broodling.store import default_store_path
from broodling.workspace import DISPOSABLE_WORKTREE_MARKER
from support import StoreTestCase


def schema_objects(connection: sqlite3.Connection) -> list[tuple[str, str, str]]:
    rows = connection.execute(
        "SELECT type, name, COALESCE(sql, '') FROM sqlite_schema ORDER BY type, name"
    ).fetchall()
    return [tuple(row) for row in rows]


class SchemaInitializationTests(StoreTestCase):
    def test_records_version_and_definition_digest(self) -> None:
        meta = self.store.schema_meta()
        self.assertEqual(meta["schema_version"], str(SCHEMA_VERSION))
        self.assertEqual(meta["schema_sha256"], SCHEMA_SHA256)

    def test_initialization_is_deterministic(self) -> None:
        other_root = Path(tempfile.mkdtemp(prefix="broodling-p2-alt-"))
        self.addCleanup(shutil.rmtree, other_root, ignore_errors=True)
        with BroodlingStore.open(other_root / "broodling.sqlite3") as other:
            self.assertEqual(
                schema_objects(self.store.connection),
                schema_objects(other.connection),
            )
            self.assertEqual(
                other.schema_meta()["schema_sha256"],
                self.store.schema_meta()["schema_sha256"],
            )

    def test_reopening_is_idempotent(self) -> None:
        before = schema_objects(self.store.connection)
        meta_before = self.store.schema_meta()
        reopened = self.reopen()
        self.assertEqual(schema_objects(reopened.connection), before)
        self.assertEqual(reopened.schema_meta(), meta_before)

    def test_owns_exactly_the_declared_tables(self) -> None:
        rows = self.store.connection.execute(
            "SELECT name FROM sqlite_schema WHERE type = 'table' "
            "AND name NOT LIKE 'sqlite_%' ORDER BY name"
        ).fetchall()
        self.assertEqual(tuple(row["name"] for row in rows), TABLES)

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
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)

    def test_foreign_schema_definition_refuses_to_open(self) -> None:
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'deadbeef' WHERE key = 'schema_sha256'"
        )
        self.store.close()
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)


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
