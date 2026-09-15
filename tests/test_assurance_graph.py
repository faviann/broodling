"""Discriminating product graph controls through the pinned public SDK.

The runtime half of the #17 controls: the multi-round routes whose execution
behaviour nothing else witnesses, and the classes of provider misbehaviour
Zeroshot has to turn into a node error. The structural half — every executable
node guarded, no unusable route reaching an accepting sink, repair reading only
the adjudicated directive — is decided against the authored graph by
`test_assurance_graph_structure.py`, which runs in the default regression lane.
The single-round clean and repaired routes are taken through the exact product
graph and runtime by the #18 campaign. `assurance_support.definitions` says why
each run that remains here is not decidable, or already witnessed, elsewhere.
"""

import asyncio
import shutil
import tempfile
import unittest
from pathlib import Path

from support import durable_test_root
from zeroshot_lane import qualification_lane

from broodling.zeroshot_sdk import assert_qualified_integration


@qualification_lane
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
