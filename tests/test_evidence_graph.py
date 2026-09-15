"""Product submission, evidence/authority handoffs and terminal artifacts.

These five real-SDK cases validate Broodling's integration seams. They do not
assert route placement, branch ordering, loop semantics or execution traces.
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
