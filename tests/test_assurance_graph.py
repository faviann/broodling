"""Discriminating product graph controls through the pinned public SDK."""

import asyncio
import importlib.util
import shutil
import tempfile
import unittest
from pathlib import Path

from support import durable_test_root

from broodling.zeroshot_sdk import assert_qualified_integration


@unittest.skipUnless(
    importlib.util.find_spec("zeroshot"), "install the G1-V1 qualified SDK/sidecar"
)
class AssuranceGraphTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from assurance_support import acceptance_checks, run_controls

        assert_qualified_integration()
        cls.run_root = Path(tempfile.mkdtemp(prefix="b17t-", dir="/dev/shm"))
        cls.workspace_root = durable_test_root("broodling-assurance-")
        cls.addClassCleanup(shutil.rmtree, cls.run_root, ignore_errors=True)
        cls.addClassCleanup(shutil.rmtree, cls.workspace_root, ignore_errors=True)
        cls.cases = asyncio.run(run_controls(cls.run_root, cls.workspace_root))
        cls.checks = acceptance_checks(cls.cases)

    def test_product_graph_mechanical_acceptance(self):
        for name, passed in self.checks.items():
            with self.subTest(check=name):
                self.assertTrue(passed, name)

    def test_every_unusable_required_occurrence_fails_closed(self):
        from assurance_support import definitions

        for name, scenario, reason in definitions():
            if reason is not None:
                with self.subTest(scenario=scenario):
                    result = self.cases[name]["result"]
                    self.assertFalse(result["succeeded"])
                    self.assertEqual(result["failure"], reason)

    def test_control_error_with_open_obligation_does_not_start_another_repair(self):
        from assurance_support import nodes

        sequence = nodes(self.cases["open-control-crash"])
        self.assertEqual(sequence.count("repair"), 1)
        self.assertEqual(sequence[-1], "round_complete")
