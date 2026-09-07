import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "evidence/issue-9-run-record.json"


class Issue9EvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.record = json.loads(EVIDENCE.read_text(encoding="utf-8"))

    def test_verdict_and_acceptance_checks(self) -> None:
        self.assertEqual(self.record["verdicts"], {"W3": "PASS", "G1-V1": "NOT_PASSED"})
        self.assertTrue(all(self.record["acceptanceChecks"].values()))

    def test_pinned_real_integration(self) -> None:
        build = self.record["build"]
        self.assertEqual(build["zeroshotRevision"], "d0909615d6ba3c179b58bce15a059f40400ec995")
        self.assertEqual(build["wheelSha256"], "16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb")
        self.assertEqual(build["sidecarSha256"], "9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86")
        self.assertFalse(self.record["integrationBoundary"]["broodlingValidatorOrRouter"])

    def test_c2_requires_fresh_assurance(self) -> None:
        events = self.record["cases"]["repair_resolve"]["events"]
        self.assertEqual(
            [event["node"] for event in events],
            ["implement", "initial_evidence_check", "initial_review", "adjudicate_authority",
             "repair", "repair_evidence_check", "repair_review", "resolution_authority",
             "round_complete", "final_assessment_authority_repaired"],
        )
        review = next(event for event in events if event["node"] == "repair_review")
        self.assertEqual(review["input"]["candidateGeneration"], "c2")

    def test_evidence_and_obligations_fail_closed(self) -> None:
        cases = self.record["cases"]
        self.assertEqual(cases["missing_initial_evidence"]["result"]["failure"], "required_evidence_missing")
        self.assertEqual(cases["missing_repair_evidence"]["result"]["failure"], "required_evidence_missing")
        self.assertEqual(cases["sticky_exhaustion"]["result"]["failure"], "obligations_exhausted")
        nodes = [event["node"] for event in cases["sticky_exhaustion"]["events"]]
        self.assertNotIn("final_assessment_authority_repaired", nodes)

    def test_authority_and_repair_firewalls(self) -> None:
        cases = self.record["cases"]
        for name in ("implementer_authority_claim", "reviewer_authority_claim", "repair_authority_claim"):
            self.assertEqual(cases[name]["result"]["failure"], "execution_unusable")
        repair = next(event for event in cases["repair_input_canary"]["events"] if event["node"] == "repair")
        self.assertEqual(repair["input"], {"contract": "frozen-contract-v1", "candidateGeneration": "c1", "directive": "open_d1"})
        self.assertEqual(cases["forged_diagnostics"]["result"]["output"]["contract"], "frozen-contract-v1")

    def test_no_candidate_applicability_subsystem(self) -> None:
        graph = json.dumps(self.record["fixture"]["graph"]).lower()
        for prohibited in ("candidatehash", "candidateseal", "manifest", "observer"):
            self.assertNotIn(prohibited, graph)


if __name__ == "__main__":
    unittest.main()
