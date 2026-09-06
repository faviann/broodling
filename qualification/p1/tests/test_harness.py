from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

P1 = Path(__file__).resolve().parents[1]


def executable_nodes(node: dict[str, object]) -> list[str]:
    kind = node["kind"]
    if kind in ("step", "verifier"):
        return [str(node["name"])]
    if kind == "seq":
        return [name for child in node["children"] for name in executable_nodes(child)]
    if kind == "loop":
        return executable_nodes(node["body"])
    if kind == "choice":
        names = [
            name
            for branch in node["branches"]
            for name in executable_nodes(branch["node"])
        ]
        if node.get("otherwise"):
            names.extend(executable_nodes(node["otherwise"]))
        return names
    return []


class HarnessFixtureTests(unittest.TestCase):
    def test_graph_runtime_and_role_inventory_agree(self) -> None:
        graph = json.loads((P1 / "fixtures/graph.json").read_text())
        runtime = json.loads((P1 / "fixtures/runtime.json").read_text())
        roles = json.loads((P1 / "fixtures/roles.json").read_text())
        nodes = set(executable_nodes(graph["root"]))
        self.assertEqual(nodes, set(runtime["nodes"]))
        self.assertEqual(nodes, set(roles["roles"]))
        self.assertEqual(
            {name for name, role in roles["roles"].items() if role == "designated_authority"},
            {"adjudicate_authority", "final_assessment_authority"},
        )
        loop = graph["root"]["children"][1]
        self.assertEqual(loop["kind"], "loop")
        self.assertEqual(loop["maxIterations"], 3)
        self.assertIn("repair", nodes)

    def test_controlled_leaf_drives_repair_then_accept(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory)
            review = self.run_leaf(state, "review")
            adjudicate = self.run_leaf(state, "adjudicate_authority")
            repair = self.run_leaf(state, "repair")
            review_again = self.run_leaf(state, "review")
            adjudicate_again = self.run_leaf(state, "adjudicate_authority")
            final = self.run_leaf(state, "final_assessment_authority")
        self.assertEqual(review["signals"]["findings"], "found")
        self.assertEqual(adjudicate["signals"]["directive"], "repair")
        self.assertIsNone(repair)
        self.assertEqual(review_again["signals"]["findings"], "clean")
        self.assertEqual(adjudicate_again["signals"]["directive"], "advance")
        self.assertEqual(final["signals"]["assessment"], "accepted")

    def test_controlled_leaf_exposes_crash_point(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            completed = subprocess.run(
                [str(P1 / "bin/codex")],
                input="BROODLING_NODE=review",
                text=True,
                env=self.leaf_environment(Path(directory), "crash:review"),
                check=False,
                capture_output=True,
            )
        self.assertEqual(completed.returncode, 70)
        self.assertIn("controlled crash at review", completed.stderr)

    def test_issue3_evidence_matches_qualified_verdicts(self) -> None:
        record = json.loads((P1 / "evidence/issue-3-run-record.json").read_text())
        self.assertEqual(
            record["verdicts"],
            {"G1-core": "BLOCKED", "Q1": "BLOCKED", "Q2": "PASS", "Q6": "BLOCKED"},
        )
        q2 = record["Q2"]
        self.assertEqual(q2["lostAcknowledgementChildExitCode"], 87)
        self.assertEqual(q2["sameSourceReplayRunId"], q2["conflict"]["existingRunId"])
        self.assertTrue(q2["noSecondRun"])

        watches = record["Q1"]["successfulRepeatedRun"]["afterClientRestart"]["watchReplay"]
        adjudicators = {
            execution["execution"]
            for status in watches
            for execution in status["active_executions"]
            if execution["node"] == "adjudicate_authority"
        }
        self.assertEqual(len(adjudicators), 2)
        self.assertEqual(
            record["Q1"]["stoppedRun"]["result"]["failure"], "force_stopped"
        )
        self.assertEqual(
            record["Q6"]["controllerKilled"]["resultAfterExternalObserverRestart"]["failure"],
            "runtime_lost",
        )
        self.assertEqual(
            record["Q6"]["broodlingHarnessCrashBeforeCatchUp"]["childExitCode"], 86
        )
        self.assertEqual(record["integrationBoundary"]["prohibitedReadersUsed"], [])

    def test_issue4_evidence_matches_qualified_verdict(self) -> None:
        record = json.loads((P1 / "evidence/issue-4-run-record.json").read_text())
        self.assertEqual(record["verdicts"], {"G1-core": "BLOCKED", "Q3": "PASS"})
        self.assertTrue(all(record["acceptanceChecks"].values()))
        self.assertFalse(record["integrationBoundary"]["broodlingValidationOrScheduling"])
        self.assertEqual(record["integrationBoundary"]["prohibitedReadersUsed"], [])

        cases = record["cases"]
        self.assertTrue(cases["clean_control"]["result"]["succeeded"])
        self.assertTrue(cases["red_repair_control"]["result"]["succeeded"])
        self.assertEqual(
            cases["sticky_omission"]["result"]["failure"], "obligations_exhausted"
        )
        self.assertEqual(
            cases["final_gap_after_clean_adjudication"]["result"]["failure"],
            "semantic_gap",
        )
        self.assertEqual(cases["refusal"]["result"]["failure"], "force_stopped")

        repair_inputs = [
            event["input"]
            for event in cases["red_repair_control"]["controlledDriverEvents"]
            if event["node"] == "repair_1"
        ]
        self.assertEqual(repair_inputs, [{"directive": "open_d1"}])
        resolution_inputs = [
            event["input"]
            for event in cases["red_repair_control"]["controlledDriverEvents"]
            if event["node"] == "resolution_authority_1"
        ]
        self.assertEqual(
            resolution_inputs,
            [{"findings": "clean", "outstanding": "open_d1"}],
        )

    def test_controlled_leaf_can_target_a_repeated_occurrence(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            state = Path(directory)
            first = self.run_leaf(
                state,
                "adjudicate_authority",
                "repair-then-accept;hang:adjudicate_authority:2",
            )
            with self.assertRaises(subprocess.TimeoutExpired):
                subprocess.run(
                    [str(P1 / "bin/codex")],
                    input="BROODLING_NODE=adjudicate_authority",
                    text=True,
                    env=self.leaf_environment(
                        state,
                        "repair-then-accept;hang:adjudicate_authority:2",
                    ),
                    timeout=0.2,
                    check=False,
                    capture_output=True,
                )
        self.assertEqual(first["signals"]["directive"], "repair")

    def run_leaf(
        self,
        state: Path,
        node: str,
        scenario: str = "repair-then-accept",
    ) -> object:
        completed = subprocess.run(
            [str(P1 / "bin/codex")],
            input=f"BROODLING_NODE={node}",
            text=True,
            check=True,
            env=self.leaf_environment(state, scenario),
            stdout=subprocess.PIPE,
        )
        events = [json.loads(line) for line in completed.stdout.splitlines()]
        message = next(event for event in events if event["type"] == "item.completed")
        return json.loads(message["item"]["text"])["response"]

    @staticmethod
    def leaf_environment(state: Path, scenario: str) -> dict[str, str]:
        environment = dict(os.environ)
        environment.update({
            "BROODLING_FIXTURE_STATE": str(state),
            "BROODLING_FIXTURE_SCENARIO": scenario,
        })
        return environment


if __name__ == "__main__":
    unittest.main()
