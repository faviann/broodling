"""The store must stay an admission nucleus: not a RunLedger mirror, not the harness.

These tests are the stop boundary of issue #12 expressed as assertions. Adding
run/occurrence/attempt/worktree/candidate-seal state, a recovery projection, or a
dependency on the qualification harness fails here first.
"""

from __future__ import annotations

import ast
import sys
import unittest
from pathlib import Path

from broodling.schema import SCHEMA_SQL, TABLES
from support import StoreTestCase

PACKAGE = Path(__file__).resolve().parents[1] / "broodling"

#: Vocabulary of later V1 phases and of Zeroshot's own record keeping. None of it
#: belongs in the V1-P2 schema.
DEFERRED_VOCABULARY = (
    "attempt",
    "worktree",
    "run_",
    "runledger",
    "occurrence",
    "node_",
    "session",
    "candidate",
    "seal",
    "manifest",
    "evidence_record",
    "effect_intent",
    "receipt",
    "watermark",
    "acceptance",
    "disposition",
    "adjudication",
    "review",
    "provider",
)

STDLIB_ONLY = {
    "__future__",
    "collections",
    "contextlib",
    "dataclasses",
    "datetime",
    "hashlib",
    "json",
    "os",
    "pathlib",
    "re",
    "sqlite3",
    "sys",
    "typing",
    "uuid",
}


def product_modules() -> list[Path]:
    return sorted(PACKAGE.glob("*.py"))


def imported_roots(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    roots: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            roots.update(alias.name.split(".")[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            roots.add(node.module.split(".")[0])
    return roots


class SchemaBoundaryTests(StoreTestCase):
    def test_the_schema_holds_only_the_admission_nucleus(self) -> None:
        self.assertEqual(
            TABLES,
            (
                "admission_decisions",
                "contract_revisions",
                "contract_source_attributions",
                "entitled_sources",
                "schema_meta",
                "work_unit_submissions",
                "work_units",
            ),
        )

    def test_no_table_or_column_mirrors_run_or_later_phase_state(self) -> None:
        rows = self.store.connection.execute(
            "SELECT name FROM sqlite_schema WHERE type = 'table' "
            "AND name NOT LIKE 'sqlite_%'"
        ).fetchall()
        names: list[str] = []
        for row in rows:
            names.append(row["name"])
            names.extend(
                column["name"]
                for column in self.store.connection.execute(
                    f"PRAGMA table_info({row['name']})"
                ).fetchall()
            )
        for name in names:
            for term in DEFERRED_VOCABULARY:
                with self.subTest(name=name, term=term):
                    self.assertNotIn(term, name.lower())

    def test_the_schema_text_declares_no_deferred_machinery(self) -> None:
        lowered = SCHEMA_SQL.lower()
        for term in ("runledger", "zeroshot", "worktree_root", "candidate_seal"):
            with self.subTest(term=term):
                self.assertNotIn(term, lowered)


class RuntimeBoundaryTests(unittest.TestCase):
    def test_the_product_package_imports_only_the_standard_library(self) -> None:
        for module in product_modules():
            with self.subTest(module=module.name):
                roots = imported_roots(module) - {"broodling"}
                self.assertLessEqual(
                    roots, STDLIB_ONLY, f"{module.name} grew a dependency"
                )

    def test_the_product_package_does_not_import_the_qualification_harness(
        self,
    ) -> None:
        for module in product_modules():
            with self.subTest(module=module.name):
                roots = imported_roots(module)
                self.assertNotIn("qualification", roots)
                self.assertFalse(
                    {root for root in roots if root.startswith("zeroshot")}
                )

    def test_importing_broodling_loads_no_runtime_client(self) -> None:
        loaded = {
            name
            for name in sys.modules
            if name.startswith(("zeroshot", "qualification"))
        }
        self.assertEqual(loaded, set())

    def test_the_package_opens_no_process_socket_or_network_client(self) -> None:
        forbidden = {"subprocess", "socket", "http", "urllib", "ssl", "asyncio"}
        for module in product_modules():
            with self.subTest(module=module.name):
                self.assertEqual(imported_roots(module) & forbidden, set())


class ApiBoundaryTests(StoreTestCase):
    def test_the_store_exposes_no_attempt_run_or_recovery_operation(self) -> None:
        surface = [name for name in dir(self.store) if not name.startswith("_")]
        for name in surface:
            for term in (
                "attempt",
                "worktree",
                "run",
                "submit",
                "recover",
                "catchup",
                "catch_up",
                "occurrence",
                "seal",
                "effect",
            ):
                with self.subTest(name=name, term=term):
                    self.assertNotIn(term, name.lower())

    def test_the_qualification_harness_is_not_product_code(self) -> None:
        harness = Path(__file__).resolve().parents[1] / "qualification"
        self.assertTrue(harness.is_dir())
        self.assertFalse(
            list(PACKAGE.rglob("*qualify*")),
            "qualification fixtures must not be promoted into the product package",
        )


if __name__ == "__main__":
    unittest.main()
