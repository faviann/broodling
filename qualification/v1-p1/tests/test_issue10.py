import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "evidence/issue-10-run-record.json"


class Issue10EvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.record = json.loads(EVIDENCE.read_text(encoding="utf-8"))

    def test_separate_verdicts_and_checks(self) -> None:
        self.assertEqual(
            self.record["verdicts"],
            {"W4": "PASS", "W6": "PASS", "G1-V1": "NOT_PASSED"},
        )
        self.assertTrue(all(self.record["w4AcceptanceChecks"].values()))
        self.assertTrue(all(self.record["w6AcceptanceChecks"].values()))

    def test_w4_real_fresh_read_only_reviewer(self) -> None:
        invocations = [
            self.record["w4"]["contaminated"]["realReviewerInvocations"][0],
            self.record["w4"]["clean"]["realReviewerInvocations"][0],
        ]
        self.assertTrue(all(item["returnCode"] == 0 for item in invocations))
        self.assertTrue(all("--ephemeral" in item["argv"] for item in invocations))
        self.assertTrue(all(item["argv"][item["argv"].index("--sandbox") + 1] == "read-only" for item in invocations))
        self.assertNotEqual(invocations[0]["stdout"].splitlines()[0], invocations[1]["stdout"].splitlines()[0])

    def test_w4_canary_controls(self) -> None:
        canaries = self.record["w4"]["canaries"]
        clean = self.record["w4"]["clean"]["realReviewerInvocations"][0]
        contaminated = self.record["w4"]["contaminated"]["realReviewerInvocations"][0]
        self.assertTrue(all(token not in clean["prompt"] for token in canaries["forbidden"]))
        self.assertTrue(all(token in contaminated["stdout"] for token in canaries["forbidden"]))
        self.assertTrue(all(token in clean["stdout"] for token in canaries["allowed"]))

    def test_w6_structural_final_occurrence(self) -> None:
        retained = self.record["w6"]["retainedControl"]["record"]
        self.assertEqual(retained["candidateGeneration"], "c2")
        self.assertEqual(retained["finalOccurrence"]["node"], "final_assessment_authority_repaired")
        self.assertEqual(retained["requiredEffects"], [])
        self.assertEqual(
            set(retained["finalCandidateMaterial"]),
            {"candidate.txt", "CANDIDATE-GOVERNING-TEXT.md"},
        )
        self.assertTrue(retained["comparisonBaseMaterial"])
        self.assertTrue(retained["requiredRawEvidence"])
        self.assertTrue(retained["assessmentRationale"])

    def test_w6_rejections_and_abandonment(self) -> None:
        cases = self.record["w6"]["cases"]
        self.assertEqual(cases["missing_initial"]["result"]["failure"], "required_evidence_missing")
        self.assertEqual(cases["wrong_population"]["result"]["failure"], "semantic_gap")
        self.assertEqual(cases["contradiction"]["result"]["failure"], "semantic_gap")
        self.assertEqual(cases["ordinary_authority"]["result"]["failure"], "execution_unusable")
        self.assertFalse(self.record["w6"]["missingMaterialControl"]["completed"])
        self.assertTrue(self.record["w6"]["interruptionControl"]["abandoned"])

    def test_v05_simplifications_preserved(self) -> None:
        graph = json.dumps(self.record["fixture"]["w6Graph"]).lower()
        for prohibited in ("candidatehash", "candidateseal", "manifest", "observer", "scanner", "recovery"):
            self.assertNotIn(prohibited, graph)
        self.assertTrue(self.record["profileCompatibility"]["w6UsesExactIssue9Graph"])


if __name__ == "__main__":
    unittest.main()
