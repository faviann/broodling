"""Run actual admitted P3 Attempts through the public coordinator and SDK."""

import importlib.util
import unittest


@unittest.skipUnless(
    importlib.util.find_spec("zeroshot"), "install the G1-V1 qualified SDK/sidecar"
)
class AdmittedAssuranceTests(unittest.TestCase):
    def test_clean_and_repaired_attempts_use_product_request_and_replay_after_mutation(
        self,
    ):
        from assurance_support import (
            CLEAN_ORDER,
            ROUND_ORDER,
            admitted_case,
            canonical_hash,
            nodes,
        )

        from broodling.assurance_graph import assurance_graph, assurance_runtime

        for scenario in ("clean", "repair-resolve"):
            with self.subTest(scenario=scenario):
                case = admitted_case(scenario)
                self.assertTrue(case["result"]["succeeded"], case["result"])
                self.assertEqual(case["replayedRunId"], case["runId"])
                self.assertTrue(case["samePersistedRequest"])
                self.assertEqual(case["graphSha256"], canonical_hash(assurance_graph()))
                self.assertEqual(
                    case["runtimeSha256"], canonical_hash(assurance_runtime())
                )
                expected = (
                    CLEAN_ORDER
                    if scenario == "clean"
                    else CLEAN_ORDER[:-1]
                    + ROUND_ORDER
                    + ["final_assessment_authority_repaired"]
                )
                # Evidence runs in the real product deterministic leaf, so it is
                # absent from the controlled model provider's event log.
                self.assertEqual(
                    nodes(case),
                    [n for n in expected if not n.endswith("evidence_check")],
                )
                for event in case["events"]:
                    if event["node"].endswith("review"):
                        observation = event["input"]["evidenceContent"]["observations"][
                            0
                        ]
                        self.assertEqual(observation["stdout"], event["candidate"])
                        self.assertEqual(
                            observation["materials"][0]["content"],
                            "REQUIRED_RAW_EVIDENCE\n",
                        )
                for event in case["events"]:
                    self.assertIn("--ignore-user-config", event["argv"])
                    self.assertIn("--ignore-rules", event["argv"])
                    self.assertIn("--ephemeral", event["argv"])
                    self.assertIn(
                        "sandbox_workspace_write.network_access=false", event["argv"]
                    )
