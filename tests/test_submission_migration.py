"""Upgrade the exact published #13 schema without changing its durable facts."""

from submission_support import SubmissionCase

from broodling import BroodlingStore, SchemaVersionMismatch
from broodling.schema import SCHEMA_SHA256, SCHEMA_VERSION, V2_SCHEMA_SHA256


class SubmissionMigrationTests(SubmissionCase):
    def make_v2(self):
        # Reverse only the new empty schema in this fixture; retain real admitted
        # #12/#13 data and every ownership/currentness trigger.
        self.store.connection.execute("DROP TABLE attempt_submissions")
        self.store.connection.executemany(
            "UPDATE schema_meta SET value = ? WHERE key = ?",
            [("2", "schema_version"), (V2_SCHEMA_SHA256, "schema_sha256")],
        )

    def test_upgrade_preserves_attempt_contract_and_provisioned_worktree(self):
        self.make_v2()
        tables = (
            "work_units",
            "contract_revisions",
            "entitled_sources",
            "admission_decisions",
            "attempts",
            "worktree_assignments",
        )
        before = {
            name: [
                tuple(row)
                for row in self.store.connection.execute(f"SELECT * FROM {name}")
            ]
            for name in tables
        }
        self.restart()
        self.assertEqual(
            self.store.schema_meta()["schema_version"], str(SCHEMA_VERSION)
        )
        self.assertEqual(self.store.schema_meta()["schema_sha256"], SCHEMA_SHA256)
        for name, rows in before.items():
            self.assertEqual(
                [
                    tuple(row)
                    for row in self.store.connection.execute(f"SELECT * FROM {name}")
                ],
                rows,
            )
        self.assertEqual(
            self.provisioner().provision(self.attempt_id).attempt, self.attempt
        )
        self.prepare()
        self.restart()
        self.assertEqual(self.coordinator.record(self.attempt_id).state, "prepared")

    def test_unrecognized_v2_definition_is_not_migrated(self):
        self.make_v2()
        self.store.connection.execute(
            "UPDATE schema_meta SET value = 'foreign' WHERE key = 'schema_sha256'"
        )
        with self.assertRaises(SchemaVersionMismatch):
            BroodlingStore.open(self.store_path)
        self.assertFalse(
            self.store.connection.execute(
                "SELECT name FROM sqlite_schema WHERE name = 'attempt_submissions'"
            ).fetchall()
        )
