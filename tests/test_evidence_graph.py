"""Actual SDK graph-local mechanical evidence integration controls."""

import importlib.util
import unittest


@unittest.skipUnless(
    importlib.util.find_spec("zeroshot"), "install the G1-V1 qualified SDK/sidecar"
)
class EvidenceGraphTests(unittest.TestCase):
    def test_admitted_evidence_controls(self):
        from evidence_support import SCENARIOS, acceptance_checks, run_case

        cases = {name: run_case(name) for name in SCENARIOS}
        for name, passed in acceptance_checks(cases).items():
            with self.subTest(control=name):
                self.assertTrue(passed, name)
