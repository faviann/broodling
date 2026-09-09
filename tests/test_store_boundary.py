"""Focused dependency checks for the product/qualification boundary."""

from __future__ import annotations

import ast
import sys
import unittest
from pathlib import Path

PACKAGE = Path(__file__).resolve().parents[1] / "broodling"


def product_modules() -> list[Path]:
    """Include nested product modules without constraining their placement."""

    return sorted(PACKAGE.rglob("*.py"))


def imported_roots(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    roots: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            roots.update(alias.name.split(".")[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            roots.add(node.module.split(".")[0])
    return roots


class ProductDependencyBoundaryTests(unittest.TestCase):
    def test_product_modules_do_not_import_qualification_code(self) -> None:
        for module in product_modules():
            with self.subTest(module=module.relative_to(PACKAGE)):
                self.assertNotIn("qualification", imported_roots(module))

    def test_importing_broodling_loads_no_runtime_or_qualification_client(self) -> None:
        import subprocess

        subprocess.run(
            [
                sys.executable,
                "-c",
                "import broodling, sys; assert not any(n.startswith(('zeroshot', 'qualification')) for n in sys.modules)",
            ],
            check=True,
        )


if __name__ == "__main__":
    unittest.main()
