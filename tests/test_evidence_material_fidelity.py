"""Every mismatch dimension survives the deterministic leaf byte for byte.

Issue #45. The #18 campaign used to run six complete Zeroshot scenarios —
population, host, mode, artifact, contradiction, insufficiency — that differ only
in the bytes inside the candidate the check prints. Each one traverses the same
graph on the same route and ends the same way, so what they jointly established
about *Broodling* is this: whatever the mismatch, the product's deterministic
evidence leaf carries the raw material and the frozen population into the
assessor's input unaltered, and never judges it.

That is decidable here, against the real leaf, without Zeroshot. The campaign
keeps one real run to witness that available-but-insufficient evidence still
fails at the final assessment rather than short-circuiting the review.
"""

from __future__ import annotations

import json
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from evidence_support import SEMANTIC_GAPS, raw_material

from broodling.mechanical_evidence import MODE, _observe

#: The frozen declaration the #18 Contract admits, in its serialized form.
CONTRACT = {
    "criteria": [
        {
            "criterionId": "c1",
            "evidencePopulation": {
                "kind": "enumerated",
                "members": ["positive", "negative", "boundary"],
            },
            "validationAction": "Descriptive action; never shell $(touch forbidden)",
            "mechanicalEvidence": {
                "argv": ["/usr/bin/python3", "check.py"],
                "cwd": ".",
                "materials": ["candidate.json"],
            },
        }
    ]
}


class DeterministicMaterialFidelityTests(unittest.TestCase):
    def observe(self, candidate: str) -> dict:
        with TemporaryDirectory(prefix="broodling-material-") as directory:
            workspace = Path(directory)
            (workspace / "candidate.json").write_text(candidate)
            (workspace / "check.py").write_text(
                "from pathlib import Path\nprint(Path('candidate.json').read_text())\n"
            )
            observations = _observe(CONTRACT, workspace)
        self.assertEqual(len(observations), 1)
        return observations[0]

    def test_each_mismatch_dimension_reaches_the_assessor_unaltered(self):
        for scenario in ("valid", *SEMANTIC_GAPS):
            with self.subTest(scenario=scenario):
                raw = raw_material(scenario)
                raw["generationMaterial"] = "AFTER_IMPLEMENT"
                candidate = json.dumps(raw)
                observed = self.observe(candidate)
                # stdout is what the assessor reads and materials[0] is the
                # candidate it was read from; both must be the exact bytes.
                self.assertEqual(json.loads(observed["stdout"]), raw)
                self.assertEqual(observed["stdout"], candidate + "\n")
                self.assertEqual(
                    observed["materials"],
                    [{"path": "candidate.json", "content": candidate}],
                )

    def test_the_leaf_reports_production_and_never_judges_sufficiency(self):
        # "insufficient" is a one-result population against a three-member
        # frozen requirement. The leaf still reports exit 0 with the frozen
        # population beside it: the gap is the assessor's to find, not the
        # collector's to pre-empt.
        raw = raw_material("insufficient")
        observed = self.observe(json.dumps(raw))
        self.assertEqual(observed["exitCode"], 0)
        self.assertEqual(observed["stderr"], "")
        self.assertEqual(json.loads(observed["stdout"])["results"], ["PASS"])
        self.assertEqual(observed["criterionId"], "c1")
        self.assertEqual(
            json.loads(observed["population"]),
            CONTRACT["criteria"][0]["evidencePopulation"],
        )
        self.assertEqual(observed["argv"], ["/usr/bin/python3", "check.py"])
        self.assertEqual(observed["cwd"], ".")
        self.assertEqual(observed["mode"], MODE)
        self.assertEqual(json.loads(observed["host"])["system"], "Linux")

    def test_a_model_supplied_population_cannot_displace_the_frozen_one(self):
        # wrong-population claims a population inside the candidate's own bytes.
        # The observation's population field comes from the frozen Contract, so
        # the claim arrives as data beside it rather than in place of it.
        observed = self.observe(json.dumps(raw_material("wrong-population")))
        self.assertEqual(json.loads(observed["stdout"])["population"], ["unrelated"])
        self.assertEqual(
            json.loads(observed["population"]),
            {"kind": "enumerated", "members": ["positive", "negative", "boundary"]},
        )


if __name__ == "__main__":
    unittest.main()
