"""Broodling-owned authority, input isolation and invocation configuration.

Read declarations only. Zeroshot owns validation, routing, state propagation,
loop execution and failure handling; none of those is reconstructed here.
"""

import unittest

from assurance_support import executable_nodes

from broodling.assurance_graph import assurance_graph, assurance_runtime


class AssurancePolicyTests(unittest.TestCase):
    def setUp(self):
        self.graph = assurance_graph()
        self.nodes = {n["name"]: n for n in executable_nodes(self.graph["root"])}
        self.runtime = assurance_runtime()

    def inputs(self, name):
        node = self.nodes[name]
        return {tuple(b["target"]): b["value"] for b in node["inputBindings"]}

    def test_every_declared_role_has_a_runtime_binding(self):
        # Submission must provide a runtime for the roles it actually authors.
        # SDK validation owns duplicate names and other GraphSpec validity.
        self.assertEqual(set(self.nodes), set(self.runtime["nodes"]))

    def test_only_implementation_and_repair_receive_mutation_capability(self):
        # The selected SDK profile maps step/verifier to write/read-only mode.
        self.assertEqual(
            {name for name, node in self.nodes.items() if node["kind"] == "step"},
            {"implement", "repair"},
        )
        self.assertEqual(self.graph["policy"]["default"], "deny")

    def test_repair_receives_contract_and_admitted_directive_not_raw_findings(self):
        self.assertEqual(
            set(self.nodes["repair"]["input"]["fields"]),
            {"contract", "directive", "directiveContent"},
        )
        self.assertEqual(
            self.inputs("repair"),
            {
                (target,): {"source": "state", "path": [source]}
                for target, source in (
                    ("contract", "contract"),
                    ("directive", "obligation"),
                    ("directiveContent", "directiveContent"),
                )
            },
        )

    def test_review_automatic_inputs_exclude_prior_role_narratives(self):
        permitted = {"contract", "comparisonBase", "evidence", "evidenceContent"}
        for name in ("initial_review", "repair_review"):
            with self.subTest(role=name):
                self.assertEqual(set(self.nodes[name]["input"]["fields"]), permitted)
                self.assertEqual(
                    self.inputs(name),
                    {(key,): {"source": "state", "path": [key]} for key in permitted},
                )

    def test_only_designated_authorities_can_write_decisions_and_final_rationale(self):
        expected = {
            "obligation": {
                ("adjudicate_authority", "signal", ("decision",)),
                ("resolution_authority", "signal", ("resolution",)),
            },
            "directiveContent": {
                ("adjudicate_authority", "out", ("directiveContent",)),
            },
            "finalRationale": {
                ("final_assessment_authority_clean", "out", ("finalRationale",)),
                ("final_assessment_authority_repaired", "out", ("finalRationale",)),
            },
        }
        for target, permitted in expected.items():
            with self.subTest(authority=target):
                self.assertEqual(
                    {
                        (
                            name,
                            binding["value"]["channel"],
                            tuple(binding["value"]["path"]),
                        )
                        for name, node in self.nodes.items()
                        for binding in node["writeBindings"]
                        if binding["target"][0] == target
                    },
                    permitted,
                )

    def test_worker_output_cannot_rewrite_admission_or_bind_private_diagnostics(self):
        frozen = {"contract", "admittedInstructions", "comparisonBase"}
        for name, node in self.nodes.items():
            for binding in node["writeBindings"]:
                with self.subTest(role=name, target=binding["target"]):
                    self.assertNotIn(binding["target"][0], frozen)
                    self.assertNotEqual(binding["value"]["channel"], "diagnostic")
                    self.assertEqual(binding["value"]["node"], name)

    def test_runtime_selects_fresh_sessions_and_the_product_evidence_leaf(self):
        for name, binding in self.runtime["nodes"].items():
            with self.subTest(role=name):
                self.assertEqual(binding["sessionScope"], "execution")
                profile = binding["connections"]["profile"]
                self.assertLessEqual(
                    {
                        "BROODLING_REAL_CODEX",
                        "BROODLING_PROFILE_HOME",
                        "BROODLING_ISOLATED_CODEX_HOME",
                    },
                    set(profile),
                )
                self.assertEqual(
                    "BROODLING_EVIDENCE_LEAF" in profile,
                    name in {"initial_evidence_check", "repair_evidence_check"},
                )


if __name__ == "__main__":
    unittest.main()
