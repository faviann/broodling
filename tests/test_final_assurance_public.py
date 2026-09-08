"""#19 normal current-run controls through the actual qualified SDK/sidecar."""

import asyncio
import importlib.util
import json
import unittest

from final_assurance_support import final_case, observe_released

from broodling.assurance_graph import assurance_graph, assurance_runtime
from broodling.errors import UnsupportedRuntime


@unittest.skipUnless(
    importlib.util.find_spec("zeroshot"), "install the G1-V1 qualified SDK/sidecar"
)
class FinalAssurancePublicTests(unittest.TestCase):
    def test_clean_repaired_and_forged_claims_keep_designated_current_authority(self):
        for scenario in ("clean", "repair", "forged-identifiers"):
            with self.subTest(scenario=scenario), final_case(scenario) as case:
                observed = asyncio.run(observe_released(case))
                repaired = scenario != "clean"
                final_node = "final_assessment_authority_" + (
                    "repaired" if repaired else "clean"
                )
                events = case.events()
                final = events[-1]
                expected_nodes = ["implement", "initial_review", "adjudicate_authority"]
                if repaired:
                    expected_nodes += [
                        "repair",
                        "repair_review",
                        "resolution_authority",
                        "round_complete",
                    ]
                self.assertEqual(
                    [event["node"] for event in events], expected_nodes + [final_node]
                )
                self.assertEqual(final["node"], final_node)
                self.assertEqual(observed.run_id, case.row.run_id)
                self.assertEqual(observed.final_node, final_node)
                self.assertEqual(
                    observed.mutation_node, "repair" if repaired else "implement"
                )
                self.assertTrue(observed.final_execution_id)
                self.assertTrue(observed.mutation_execution_id)
                self.assertNotEqual(
                    observed.final_execution_id, observed.mutation_execution_id
                )
                self.assertEqual(case.request["graph"], assurance_graph())
                self.assertEqual(case.request["runtime"], assurance_runtime())
                self.assertEqual(
                    observed.output["contract"], case.revision.canonical_bytes.decode()
                )
                self.assertEqual(
                    observed.output["comparisonBase"], case.attempt.b1_commit_oid
                )
                self.assertEqual(observed.output["findings"], "clean")
                self.assertEqual(
                    observed.output["obligation"], "resolved_d1" if repaired else "none"
                )
                expected_generation = (
                    "C2_FROM_REPAIR" if repaired else "C1_FROM_IMPLEMENT"
                )
                current = final["input"]["evidenceContent"]["observations"][0]
                self.assertEqual(
                    json.loads(current["stdout"])["generationMaterial"],
                    expected_generation,
                )
                self.assertEqual(current["stdout"], final["candidate"])
                self.assertEqual(current["materials"][0]["content"], final["candidate"])
                self.assertEqual(
                    observed.output["evidenceContent"],
                    final["input"]["evidenceContent"],
                )
                self.assertEqual(
                    observed.output["finalRationale"],
                    final["response"]["output"]["finalRationale"],
                )
                self.assertNotIn("FORGED_", json.dumps(observed.output))
                if scenario == "forged-identifiers":
                    self.assertEqual(
                        final["response"]["diagnostic"]["contractId"],
                        "FORGED_CONTRACTID",
                    )
                    self.assertEqual(
                        json.loads(final["response"]["diagnostic"]["note"])[
                            "occurrenceId"
                        ],
                        "FORGED_OCCURRENCEID",
                    )
                if repaired:
                    initial = next(
                        event for event in events if event["node"] == "initial_review"
                    )
                    self.assertEqual(
                        json.loads(
                            initial["input"]["evidenceContent"]["observations"][0][
                                "stdout"
                            ]
                        )["generationMaterial"],
                        "C1_FROM_IMPLEMENT",
                    )
                    self.assertNotEqual(
                        initial["input"]["evidenceContent"],
                        final["input"]["evidenceContent"],
                    )
                    repair = next(
                        event for event in events if event["node"] == "repair"
                    )
                    self.assertEqual(
                        set(repair["input"]),
                        {"contract", "directive", "directiveContent"},
                    )
                    self.assertEqual(final["input"]["obligation"], "resolved_d1")
                replay = case.coordinator.submit_assurance(case.attempt_id)
                self.assertEqual(replay.run_id, observed.run_id)
                self.assertEqual(replay.request_json, case.row.request_json)
                self.assertEqual(
                    case.store.connection.execute(
                        "SELECT count(*) FROM attempts"
                    ).fetchone()[0],
                    1,
                )
                self.assertEqual(
                    case.store.connection.execute(
                        "SELECT count(*) FROM attempt_submissions"
                    ).fetchone()[0],
                    1,
                )
                self.assertTrue(case.path.is_dir())

    def test_invalid_authority_evidence_and_obligations_cannot_return_final_observation(
        self,
    ):
        for scenario in (
            "ordinary-lookalike",
            "missing-final-output",
            "default-final-output",
            "missing-rationale",
            "semantic-gap",
            "sticky",
            "missing-initial",
            "missing-renewed",
        ):
            with self.subTest(scenario=scenario), final_case(scenario) as case:
                with self.assertRaises(UnsupportedRuntime):
                    asyncio.run(observe_released(case))
                self.assertEqual(
                    case.coordinator.record(case.attempt_id).run_id, case.row.run_id
                )
                self.assertTrue(case.path.is_dir())

    def test_observation_transports_empty_rationale_for_custody_to_reject(self):
        with final_case("empty-rationale") as case:
            observed = asyncio.run(observe_released(case))
            self.assertEqual(observed.output["finalRationale"], [])

    def test_completed_run_cannot_be_used_for_late_observation(self):
        from zeroshot import Client, LocalTarget

        with final_case() as case:
            case.release()

            async def finish_then_observe():
                async with Client(
                    target=LocalTarget(case.path, state_dir=case.run_root / "native"),
                    environment=case.request["target"]["environment"],
                ) as client:
                    result = await client.get_run(case.row.run_id).wait(wait_timeout=90)
                    self.assertTrue(result.succeeded)
                await case.adapter.observe_current(case.request, case.row.run_id)

            with self.assertRaisesRegex(UnsupportedRuntime, "nonterminal"):
                asyncio.run(finish_then_observe())
