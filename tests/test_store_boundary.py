"""The store must stay an admission nucleus: not a RunLedger mirror, not the harness.

The schema retains P2 facts plus one immutable final P3 custody row; the graph
is a public runtime definition. No runtime history, candidate-seal state, recovery projection,
effects or dependency on the qualification harness belongs in the store.
"""

from __future__ import annotations

import ast
import sys
import unittest
from pathlib import Path

from support import StoreTestCase

from broodling.schema import SCHEMA_SQL, TABLES

PACKAGE = Path(__file__).resolve().parents[1] / "broodling"

#: Vocabulary of later V1 phases and of Zeroshot's own record keeping. None of it
#: belongs in the V1-P2 schema.
DEFERRED_VOCABULARY = (
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

#: Modules permitted to start a process. Provisioning a worktree means running
#: `git` locally. The qualified Codex profile checks the installed CLI version
#: before first dispatch; it does not start graph occurrences. Those belong to
#: the public SDK and sidecar.
# #18's explicitly admitted read-only evidence leaf starts only declared local
# checks inside its sandbox; the existing SDK still owns occurrences/lifecycle.
PROCESS_CAPABLE_MODULES = {"git.py", "codex_profile.py", "mechanical_evidence.py"}

#: Modules permitted to take a host-local file lock. Materializing one Attempt's
#: worktree is single-writer on this host, which is what `fcntl` buys; it is
#: mutual exclusion between live processes, never durable authority, so nothing
#: that records a durable fact may reach for it.
LOCK_CAPABLE_MODULES = {"provisioning.py"}

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
    "subprocess",
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
                "attempt_submissions",
                "attempts",
                "contract_revisions",
                "contract_source_attributions",
                "entitled_sources",
                "final_assurance",
                "schema_meta",
                "work_unit_submissions",
                "work_units",
                "worktree_assignments",
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
            if name == "zeroshot_run_id":
                continue  # The sole runtime fact: immutable Attempt correlation.
            for term in DEFERRED_VOCABULARY:
                with self.subTest(name=name, term=term):
                    self.assertNotIn(term, name.lower())

    def test_the_schema_text_declares_no_deferred_machinery(self) -> None:
        lowered = SCHEMA_SQL.lower()
        for term in ("runledger", "candidate_seal", "abandon"):
            with self.subTest(term=term):
                self.assertNotIn(term, lowered)


class RuntimeBoundaryTests(unittest.TestCase):
    def test_the_product_package_imports_only_the_standard_library(self) -> None:
        for module in product_modules():
            with self.subTest(module=module.name):
                allowed = set(STDLIB_ONLY)
                if module.name == "zeroshot_sdk.py":
                    allowed.update({"asyncio", "importlib", "zeroshot"})
                if module.name in LOCK_CAPABLE_MODULES:
                    allowed.add("fcntl")
                if module.name == "mechanical_evidence.py":
                    allowed.update({"platform", "tempfile"})
                roots = imported_roots(module) - {"broodling"}
                self.assertLessEqual(roots, allowed, f"{module.name} grew a dependency")

    def test_only_provisioning_may_take_a_host_lock(self) -> None:
        """A file lock is host-local mutual exclusion, not durable authority.

        The store's constraints decide who owns what; only the module that has
        to keep two live processes out of one half-built worktree may lock.
        """

        for module in product_modules():
            if module.name in LOCK_CAPABLE_MODULES:
                continue
            with self.subTest(module=module.name):
                self.assertNotIn("fcntl", imported_roots(module))

    def test_the_product_package_does_not_import_the_qualification_harness(
        self,
    ) -> None:
        for module in product_modules():
            with self.subTest(module=module.name):
                roots = imported_roots(module)
                self.assertNotIn("qualification", roots)
                if module.name != "zeroshot_sdk.py":
                    self.assertFalse(
                        {root for root in roots if root.startswith("zeroshot")}
                    )

    def test_importing_broodling_loads_no_runtime_client(self) -> None:
        import subprocess

        subprocess.run(
            [
                sys.executable,
                "-c",
                "import broodling, sys; assert not any(n.startswith(('zeroshot', 'qualification')) for n in sys.modules)",
            ],
            check=True,
        )

    def test_the_package_opens_no_socket_or_network_client(self) -> None:
        forbidden = {"socket", "http", "urllib", "ssl", "asyncio"}
        for module in product_modules():
            with self.subTest(module=module.name):
                allowed = {"asyncio"} if module.name == "zeroshot_sdk.py" else set()
                self.assertEqual(imported_roots(module) & forbidden, allowed)

    def test_only_git_profile_and_admitted_evidence_leaf_may_start_a_process(
        self,
    ) -> None:
        for module in product_modules():
            if module.name in PROCESS_CAPABLE_MODULES:
                continue
            with self.subTest(module=module.name):
                self.assertNotIn("subprocess", imported_roots(module))


class ApiBoundaryTests(StoreTestCase):
    def test_the_store_exposes_no_run_or_recovery_operation(self) -> None:
        surface = [name for name in dir(self.store) if not name.startswith("_")]
        for name in surface:
            for term in (
                "run",
                "submit",
                "recover",
                "catchup",
                "catch_up",
                "occurrence",
                "seal",
                "effect",
                "abandon",
                "restart",
                "review",
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


class SubmissionBoundaryTests(StoreTestCase):
    def test_only_minimal_correlation_facts_are_stored(self):
        columns = self.store.connection.execute(
            "PRAGMA table_info(attempt_submissions)"
        ).fetchall()
        self.assertEqual(
            [column["name"] for column in columns],
            [
                "attempt_id",
                "submission_key",
                "request_json",
                "state",
                "zeroshot_run_id",
                "error_detail",
            ],
        )

    def test_sdk_adapter_has_only_current_public_observation_and_no_private_storage(
        self,
    ):
        tree = ast.parse((PACKAGE / "zeroshot_sdk.py").read_text())
        forbidden = {
            "history",
            "wait",
            "logs",
            "list_runs",
            "force_stop",
            "connect",
        }
        calls = {
            node.func.attr
            for node in ast.walk(tree)
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
        }
        self.assertFalse(calls & forbidden)
        watches = [
            node
            for node in ast.walk(tree)
            if isinstance(node, ast.Call)
            and isinstance(node.func, ast.Attribute)
            and node.func.attr == "watch"
        ]
        self.assertTrue(watches)
        self.assertTrue(
            all(
                any(keyword.arg == "after" for keyword in node.keywords)
                for node in watches
            )
        )
        self.assertNotIn("sqlite3", imported_roots(PACKAGE / "zeroshot_sdk.py"))
