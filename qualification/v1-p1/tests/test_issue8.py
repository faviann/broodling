import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "evidence/issue-8-run-record.json"


class Issue8EvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.record = json.loads(EVIDENCE.read_text(encoding="utf-8"))

    def test_separate_verdicts_match_observations(self) -> None:
        self.assertEqual(
            self.record["verdicts"],
            {"W1": "PASS", "W2": "FAIL", "W5": "PASS", "W7": "PASS"},
        )
        for witness, verdict in self.record["verdicts"].items():
            checks = self.record[witness]["checks"]
            self.assertEqual(verdict == "PASS", all(checks.values()))

    def test_build_and_fixture_identity(self) -> None:
        self.assertEqual(
            self.record["build"]["zeroshotRevision"],
            "d0909615d6ba3c179b58bce15a059f40400ec995",
        )
        self.assertEqual(
            self.record["build"]["wheelSha256"],
            "16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb",
        )
        self.assertEqual(
            self.record["build"]["sidecarSha256"],
            "9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86",
        )
        self.assertFalse(self.record["scope"]["productCodeImplemented"])
        self.assertFalse(self.record["scope"]["laterIssueWork"])

    def test_failures_are_observed_not_unknown(self) -> None:
        w2 = self.record["W2"]["checks"]
        self.assertFalse(w2["missingArtifactBlocked"])
        self.assertFalse(w2["forgedIdentifiersBlocked"])
        self.assertFalse(w2["delayedObserverGatedProgression"])

    def test_narrow_profile_controls_are_observed(self) -> None:
        w2 = self.record["W2"]["checks"]
        self.assertTrue(w2["assuranceWriteRejected"])
        self.assertTrue(w2["lingeringWriterContained"])
        self.assertTrue(w2["sharedWritablePathProtected"])
        self.assertTrue(w2["intentionalUntrackedAndGeneratedDistinguished"])
        self.assertTrue(self.record["W5"]["checks"]["allOldProviderProcessesContained"])
        self.assertTrue(all(self.record["W7"]["checks"].values()))


if __name__ == "__main__":
    unittest.main()
