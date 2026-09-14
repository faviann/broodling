"""Actual SDK graph-local mechanical evidence integration controls.

One complete run per distinct integration assumption; `evidence_support.SCENARIOS`
names them. The mismatch dimensions that differ only in candidate bytes are held
against the real deterministic leaf, without Zeroshot, by
`test_evidence_material_fidelity.py`.
"""

import unittest

from zeroshot_lane import qualification_lane


@qualification_lane
class EvidenceGraphTests(unittest.TestCase):
    def test_admitted_evidence_controls(self):
        from evidence_support import SCENARIOS, acceptance_checks, run_case

        cases = {name: run_case(name) for name in SCENARIOS}
        for name, passed in acceptance_checks(cases).items():
            with self.subTest(control=name):
                self.assertTrue(passed, name)
