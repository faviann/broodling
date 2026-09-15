"""Fail-closed topology read directly from the authored assurance GraphSpec.

Issue #45. These are the Broodling-owned half of the #17 controls: the graph is
this repository's own authored artifact, so "every executable node is guarded",
"no unusable route reaches an accepting sink" and "repair sees only the
adjudicated directive" are decidable by reading it. The real-Zeroshot campaign in
`test_assurance_graph.py` keeps the runtime half — that Zeroshot turns a crash,
a malformed response or a hang into the node error these guards name, and that it
honours the routes once it does.

Nothing here executes, interprets or simulates a graph. Every assertion is a
statement about the authored definition — including the placement ones, which
read the order children are declared in and never decide which branch a run
would take.
"""

from __future__ import annotations

import json
import unittest

from broodling.assurance_graph import (
    EXECUTABLE_NODES,
    NODE_TIMEOUT_MS,
    REPAIR_BOUND,
    assurance_graph,
    initial_state,
)

#: Zeroshot's unusable-execution labels, as every product error guard names them.
ERROR_LABELS = ["timeout", "crash", "malformed", "refusal"]
#: The route each executable occurrence's unusable execution is caught on, as
#: the graph authors it. The route's identity is stated rather than derived:
#: deriving it would mean deciding where Zeroshot goes after a node finishes,
#: which is the dependency's control flow. Where each route *sits* is read off
#: the tree below, because that is authored position, not Zeroshot semantics.
UNUSABLE_ROUTES = {
    "implement": "implementation_route",
    "initial_evidence_check": "initial_evidence_route",
    "initial_review": "initial_review_route",
    "adjudicate_authority": "adjudication_route",
    "repair": "repair_execution_route",
    "repair_evidence_check": "repair_evidence_route",
    "repair_review": "repair_review_route",
    "resolution_authority": "resolution_route",
    "round_complete": "post_repair_bound_route",
    "final_assessment_authority_clean": "final_route_clean",
    "final_assessment_authority_repaired": "final_route_repaired",
}
#: The only state paths any worker response is allowed to write.
WRITABLE_PATHS = {
    "evidence",
    "evidenceContent",
    "findings",
    "findingContent",
    "obligation",
    "directiveContent",
    "finalRationale",
}


#: The one executable not authored as the pair below. `round_complete` ends the
#: loop body, so what the graph runs after it is the loop's `until` rather than a
#: sibling. Handled explicitly, not exempted.
LOOP_TAIL = "round_complete"


def walk(node):
    """Yield every node of the authored tree, parents before children."""
    yield node
    kind = node["kind"]
    if kind == "seq":
        for child in node["children"]:
            yield from walk(child)
    elif kind == "choice":
        for branch in node["branches"]:
            yield from walk(branch["node"])
        yield from walk(node["otherwise"])
    elif kind == "loop":
        yield from walk(node["body"])


def authored_pairs(root):
    """Map each executable authored as `seq(occurrence, route)` to that route.

    The second child of a `seq` is what the graph runs when the first finishes,
    so this reads each occurrence's authored continuation as a local shape. No
    traversal decides which branch a run would take.
    """
    return {
        node["children"][0]["name"]: node["children"][1]
        for node in walk(root)
        if node["kind"] == "seq"
        and len(node["children"]) == 2
        and node["children"][0]["kind"] in {"step", "verifier"}
    }


def last_authored(node):
    """Descend to the position the graph authors last within `node`."""
    while True:
        if node["kind"] == "seq":
            node = node["children"][-1]
        elif node["kind"] == "choice":
            node = node["otherwise"]
        else:
            return node


def executables(root):
    return {n["name"]: n for n in walk(root) if n["kind"] in {"step", "verifier"}}


def groups(root, kind):
    return {n["name"]: n for n in walk(root) if n["kind"] == kind}


def error_guard(node):
    return {
        "kind": "in",
        "value": {"name": node, "source": "error", "field": None},
        "labels": ERROR_LABELS,
    }


def signal_guard(node, field, *labels):
    return {
        "kind": "in",
        "value": {"name": node, "source": "signal", "field": field},
        "labels": list(labels),
    }


class AuthoredTopologyTests(unittest.TestCase):
    def setUp(self):
        self.graph = assurance_graph()
        self.root = self.graph["root"]
        self.nodes = executables(self.root)
        self.choices = groups(self.root, "choice")
        self.branches = [
            (choice["name"], branch["when"], branch["node"])
            for choice in self.choices.values()
            for branch in choice["branches"]
        ]

    def guarded_branch(self, guard):
        """Return the single branch node selected by an exact guard."""
        selected = [node for _, when, node in self.branches if when == guard]
        self.assertEqual(len(selected), 1, guard)
        return selected[0]

    def test_authored_executable_nodes_are_exactly_the_declared_set(self):
        # The runtime plan binds one agent per declared name; a node added to the
        # graph without its entry would run unbound, and an entry without a node
        # would leave a guard below asserting over nothing.
        self.assertEqual(sorted(self.nodes), sorted(EXECUTABLE_NODES))
        self.assertEqual(len(EXECUTABLE_NODES), len(set(EXECUTABLE_NODES)))
        # Occurrences, not just names: `executables` is keyed by name, so a node
        # authored twice would collapse into one entry and the per-occurrence
        # placement below would silently cover only whichever copy walked last.
        occurrences = [
            n["name"] for n in walk(self.root) if n["kind"] in {"step", "verifier"}
        ]
        self.assertEqual(sorted(occurrences), sorted(EXECUTABLE_NODES))

    def test_each_executable_occurrence_is_caught_on_its_own_authored_route(self):
        # Not "the graph mentions every node in some error guard somewhere": a
        # guard naming the wrong node, or the right node from a route belonging
        # to a different occurrence, leaves the occurrence it was meant to catch
        # with no handling of its own. The pairing is asserted both ways, so a
        # swapped pair of guards fails on both halves.
        self.assertEqual(sorted(UNUSABLE_ROUTES), sorted(EXECUTABLE_NODES))
        for name, route in UNUSABLE_ROUTES.items():
            with self.subTest(node=name, route=route):
                caught = [
                    branch
                    for branch in self.choices[route]["branches"]
                    if branch["when"] == error_guard(name)
                ]
                self.assertEqual(len(caught), 1, route)
                self.assertEqual(caught[0]["node"]["kind"], "fail")
                self.assertEqual(caught[0]["node"]["reason"], "execution_unusable")
        self.assertEqual(
            sorted(
                (choice, when["value"]["name"])
                for choice, when, _ in self.branches
                if when["value"]["source"] == "error"
            ),
            sorted((route, name) for name, route in UNUSABLE_ROUTES.items()),
        )
        # Everything above reads guards by name, and a name is not a position:
        # a correctly named guard on a choice reached before the occurrence runs
        # catches nothing. So the placement is read too. Ten of the eleven
        # executables are authored as `seq(occurrence, its route)`, and the
        # second child of a seq is what runs when the first finishes.
        pairs = authored_pairs(self.root)
        self.assertEqual(
            sorted(pairs), sorted(n for n in EXECUTABLE_NODES if n != LOOP_TAIL)
        )
        for name, route in pairs.items():
            with self.subTest(node=name):
                self.assertEqual(route["kind"], "choice")
                self.assertEqual(route["name"], UNUSABLE_ROUTES[name])
        # round_complete is the eleventh, and its position is read directly: it
        # is the last thing the loop body authors, so nothing inside the loop
        # follows it and the outcome lands on the loop's own `until` and on the
        # choice authored immediately after the loop. Both carry its guard —
        # asserted exactly in the bound test below.
        loop = groups(self.root, "loop")["bounded_repair"]
        self.assertIs(last_authored(loop["body"]), self.nodes[LOOP_TAIL])
        self.assertIn(error_guard(LOOP_TAIL), loop["until"]["guards"])
        self.assertEqual(
            [
                child["name"]
                for child in groups(self.root, "seq")["repair_then_final"]["children"]
            ],
            ["bounded_repair", UNUSABLE_ROUTES[LOOP_TAIL]],
        )

    def test_ordinary_output_cannot_acquire_adjudication_or_final_authority(self):
        # Every routing guard names the node whose signal it reads, and a node
        # can only emit the signals it declares. So a review claiming `decision`
        # or `assessment` satisfies no guard: the adjudication route reads
        # adjudicate_authority's `decision`, and initial_review declares only
        # `findings`. The claim routes nothing whatever the runtime does with it.
        declared = {}
        for name, node in self.nodes.items():
            for field in node.get("signals", {}):
                declared.setdefault(field, set()).add(name)
        self.assertEqual(
            {field: sorted(owners) for field, owners in declared.items()},
            {
                "availability": ["initial_evidence_check", "repair_evidence_check"],
                "findings": ["initial_review", "repair_review"],
                "decision": ["adjudicate_authority"],
                "resolution": ["resolution_authority"],
                "status": ["round_complete"],
                "assessment": [
                    "final_assessment_authority_clean",
                    "final_assessment_authority_repaired",
                ],
            },
        )
        loop = groups(self.root, "loop")["bounded_repair"]
        guards = [when for _, when, _ in self.branches] + loop["until"]["guards"]
        signalled = 0
        for guard in guards:
            value = guard["value"]
            if value["source"] != "signal":
                continue
            signalled += 1
            with self.subTest(guard=json.dumps(guard, sort_keys=True)):
                emitter = self.nodes[value["name"]]
                self.assertIn(value["field"], emitter.get("signals", {}))
                self.assertLessEqual(
                    set(guard["labels"]), set(emitter["signals"][value["field"]])
                )
        # Each route's branches are asserted exactly elsewhere, so this only
        # has to refuse a vacuous sweep.
        self.assertTrue(signalled, "no signal guards found")
        # Neither mutation nor review may write the state the authorities own.
        for name in ("implement", "repair", "initial_review", "repair_review"):
            targets = [b["target"] for b in self.nodes[name]["writeBindings"]]
            with self.subTest(node=name):
                self.assertNotIn(["obligation"], targets)
                self.assertNotIn(["directiveContent"], targets)
                self.assertNotIn(["finalRationale"], targets)

    def test_no_unusable_or_unaccepted_route_can_reach_an_accepting_sink(self):
        # An accepting sink is reachable only as the fall-through of a final
        # assessment whose error, gap and refusal branches have already been
        # taken out. Nothing else in the graph may succeed.
        accepting = groups(self.root, "succeed")
        self.assertEqual(
            sorted(accepting),
            ["semantic_acceptance_clean", "semantic_acceptance_repaired"],
        )
        for choice, when, node in self.branches:
            if when["value"]["source"] == "error" or when["labels"] in (
                ["gap"],
                ["refused"],
                ["missing"],
            ):
                with self.subTest(
                    choice=choice, guard=json.dumps(when, sort_keys=True)
                ):
                    self.assertEqual([child["kind"] for child in walk(node)], ["fail"])
        for suffix in ("clean", "repaired"):
            assessor = f"final_assessment_authority_{suffix}"
            route = self.choices[f"final_route_{suffix}"]
            self.assertEqual(
                [branch["when"] for branch in route["branches"]],
                [
                    error_guard(assessor),
                    signal_guard(assessor, "assessment", "gap"),
                    signal_guard(assessor, "assessment", "refused"),
                ],
            )
            self.assertEqual(
                [branch["node"]["reason"] for branch in route["branches"]],
                ["execution_unusable", "semantic_gap", "authority_gap"],
            )
            self.assertEqual(
                route["otherwise"]["name"], f"semantic_acceptance_{suffix}"
            )
            self.assertEqual(
                self.nodes[assessor]["signals"],
                {"assessment": ["accepted", "gap", "refused"]},
            )

    def test_missing_required_evidence_fails_closed_at_both_occurrences(self):
        for prefix in ("initial", "repair"):
            check = f"{prefix}_evidence_check"
            route = self.choices[f"{prefix}_evidence_route"]
            with self.subTest(occurrence=prefix):
                self.assertEqual(
                    [branch["when"] for branch in route["branches"]],
                    [
                        error_guard(check),
                        signal_guard(check, "availability", "missing"),
                    ],
                )
                self.assertEqual(
                    [branch["node"]["reason"] for branch in route["branches"]],
                    ["execution_unusable", "required_evidence_missing"],
                )
                # Availability only reports production: the valid fall-through
                # leads to the fresh review, never straight to an assessment.
                following = [
                    n["name"] for n in walk(route["otherwise"]) if "worker" in n
                ]
                self.assertEqual(following[0], f"{prefix}_review")
                self.assertNotIn(check, following)

    def test_repair_receives_only_the_contract_and_the_adjudicated_directive(self):
        repair = self.nodes["repair"]
        self.assertEqual(
            sorted(repair["input"]["fields"]),
            ["contract", "directive", "directiveContent"],
        )
        self.assertEqual(
            repair["inputBindings"],
            [
                {"target": [target], "value": {"source": "state", "path": [source]}}
                for target, source in (
                    ("contract", "contract"),
                    ("directive", "obligation"),
                    ("directiveContent", "directiveContent"),
                )
            ],
        )
        # Raw findings and raw evidence are the two things a repair must never
        # be able to read as a directive, at any binding depth.
        sourced = {
            tuple(binding["value"]["path"]) for binding in repair["inputBindings"]
        }
        self.assertNotIn(("findingContent",), sourced)
        self.assertNotIn(("evidenceContent",), sourced)
        readers = sorted(
            name
            for name, node in self.nodes.items()
            if "findingContent" in node["input"]["fields"]
        )
        self.assertEqual(
            readers,
            [
                "adjudicate_authority",
                "final_assessment_authority_clean",
                "final_assessment_authority_repaired",
                "resolution_authority",
            ],
        )

    def test_mutation_nodes_cannot_write_any_graph_state(self):
        for name in ("implement", "repair"):
            with self.subTest(node=name):
                node = self.nodes[name]
                self.assertEqual(node["kind"], "step")
                self.assertEqual(node["output"], {"kind": "null"})
                self.assertEqual(node["writeBindings"], [])
                self.assertNotIn("signals", node)

    def test_only_declared_signal_and_output_channels_write_state(self):
        targets = set()
        for name, node in self.nodes.items():
            for binding in node["writeBindings"]:
                value = binding["value"]
                with self.subTest(node=name, target=binding["target"]):
                    self.assertEqual(value["node"], name)
                    # "diagnostic" is the model's own free channel. It is
                    # declared so a response carrying it stays well formed, and
                    # it is bound nowhere, so its identifiers cannot retarget
                    # the frozen Contract or any applicability state.
                    self.assertIn(value["channel"], {"signal", "out"})
                    self.assertEqual(len(binding["target"]), 1)
                    targets.add(binding["target"][0])
            for key, field in node.get("diagnostic", {}).get("fields", {}).items():
                with self.subTest(node=name, diagnostic=key):
                    self.assertFalse(field["required"])
        self.assertEqual(targets, WRITABLE_PATHS)
        frozen = set(initial_state("contract-text")) - WRITABLE_PATHS
        self.assertEqual(frozen, {"contract", "admittedInstructions", "comparisonBase"})

    def test_the_obligation_is_written_only_by_the_two_designated_authorities(self):
        writers = {
            (name, binding["value"]["path"][0])
            for name, node in self.nodes.items()
            for binding in node["writeBindings"]
            if binding["target"] == ["obligation"]
        }
        self.assertEqual(
            writers,
            {
                ("adjudicate_authority", "decision"),
                ("resolution_authority", "resolution"),
            },
        )
        # A fresh clean round writes "findings", which is a different path, so
        # an empty or clean round cannot clear an open obligation. The round
        # control reads nothing and writes nothing at all.
        for name in ("initial_review", "repair_review"):
            self.assertEqual(
                [b["target"] for b in self.nodes[name]["writeBindings"]],
                [["findings"], ["findingContent"]],
            )
        control = self.nodes["round_complete"]
        self.assertEqual(control["input"]["fields"], {})
        self.assertEqual(control["writeBindings"], [])
        self.assertEqual(control["signals"], {"status": ["completed"]})
        self.assertEqual(initial_state("contract-text")["obligation"], "none")

    def test_repair_is_bounded_and_an_open_obligation_cannot_reach_a_final_assessment(
        self,
    ):
        loop = groups(self.root, "loop")["bounded_repair"]
        self.assertEqual(loop["maxIterations"], REPAIR_BOUND)
        self.assertEqual(REPAIR_BOUND, 3)
        # The graph authors round_complete's unusable execution in two places:
        # here, so the loop stops, and on post_repair_bound_route, so the stop
        # is a failure. Both are asserted as authored facts; neither says
        # anything about the order Zeroshot evaluates them in.
        self.assertEqual(
            loop["until"],
            {
                "kind": "any",
                "guards": [
                    signal_guard("resolution_authority", "resolution", "resolved_d1"),
                    error_guard("round_complete"),
                ],
            },
        )
        # The obligation and its directive payload are carried across rounds
        # rather than re-derived, which is what makes the directive sticky.
        for path in (["obligation"], ["directiveContent"], ["findings"]):
            self.assertIn(path, loop["promotedStatePaths"])
        route = self.choices["post_repair_bound_route"]
        self.assertEqual(
            [branch["when"] for branch in route["branches"]],
            [
                error_guard("round_complete"),
                signal_guard("resolution_authority", "resolution", "open_d1"),
            ],
        )
        self.assertEqual(
            [branch["node"]["reason"] for branch in route["branches"]],
            ["execution_unusable", "obligations_exhausted"],
        )
        self.assertEqual(
            route["otherwise"]["name"], "final_semantic_assessment_repaired"
        )

    def test_every_repaired_route_renews_evidence_and_assurance_before_the_final(self):
        repaired = self.choices["post_repair_bound_route"]["otherwise"]
        loop = groups(self.root, "loop")["bounded_repair"]
        rounds = [n["name"] for n in walk(loop["body"]) if "worker" in n]
        self.assertEqual(
            rounds,
            [
                "repair",
                "repair_evidence_check",
                "repair_review",
                "resolution_authority",
                "round_complete",
            ],
        )
        self.assertEqual(
            [n["name"] for n in walk(repaired) if "worker" in n],
            ["final_assessment_authority_repaired"],
        )
        # The two assessors are distinct nodes on distinct routes, so a clean
        # adjudication and a repaired round can never share an assessment.
        clean = self.guarded_branch(
            signal_guard("adjudicate_authority", "decision", "none")
        )
        self.assertEqual(
            [n["name"] for n in walk(clean) if "worker" in n],
            ["final_assessment_authority_clean"],
        )

    def test_every_executable_node_declares_the_product_bound_and_one_attempt(self):
        for name, node in self.nodes.items():
            with self.subTest(node=name):
                self.assertEqual(node["timeoutMs"], NODE_TIMEOUT_MS)
                self.assertEqual(node["attempts"], 1)
                self.assertTrue(
                    node["instructions"].startswith(f"BROODLING_NODE={name}.")
                )

    def test_no_worker_supplied_candidate_ordinal_exists_anywhere(self):
        encoded = json.dumps(self.graph)
        self.assertNotIn("candidateGeneration", encoded)
        self.assertNotIn("candidateGeneration", initial_state("contract-text"))
        self.assertEqual(self.graph["policy"]["default"], "deny")


if __name__ == "__main__":
    unittest.main()
