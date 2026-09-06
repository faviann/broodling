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

    def run_leaf(self, state: Path, node: str) -> object:
        completed = subprocess.run(
            [str(P1 / "bin/codex")],
            input=f"BROODLING_NODE={node}",
            text=True,
            check=True,
            env=self.leaf_environment(state, "repair-then-accept"),
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
