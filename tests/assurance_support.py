"""Public SDK mechanics runner shared by tests and retained #17 evidence.

Only controlled provider behavior is substituted. There is no qualification
import, product router, runtime storage read, or production observer here.
"""

import dataclasses
import hashlib
import json
import os
from pathlib import Path

from support import git

from broodling.assurance_graph import assurance_graph, assurance_runtime, initial_state

ROOT = Path(__file__).resolve().parents[1]
LEAF_BIN = ROOT / "tests/fixtures/assurance-bin"
CONTRACT = "frozen-contract-v1: replace the old greeting; preserve newline"
DIRECTIVE = {
    "directive": "Replace the old greeting with the Contract greeting.",
    "correction": "Update candidate.txt and preserve its trailing newline.",
}
CLEAN_ORDER = [
    "implement",
    "initial_evidence_check",
    "initial_review",
    "adjudicate_authority",
    "final_assessment_authority_clean",
]
ROUND_ORDER = [
    "repair",
    "repair_evidence_check",
    "repair_review",
    "resolution_authority",
    "round_complete",
]


def canonical_hash(value):
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()


def jsonable(value):
    if dataclasses.is_dataclass(value):
        return {
            field.name: jsonable(getattr(value, field.name))
            for field in dataclasses.fields(value)
        }
    if isinstance(value, tuple):
        return [jsonable(item) for item in value]
    return value


def nodes(case):
    return [event["node"] for event in case["events"]]


def executable_nodes(node):
    if node["kind"] in {"step", "verifier"}:
        yield node
    elif node["kind"] == "seq":
        for child in node["children"]:
            yield from executable_nodes(child)
    elif node["kind"] == "choice":
        for branch in node["branches"]:
            yield from executable_nodes(branch["node"])
        if node.get("otherwise"):
            yield from executable_nodes(node["otherwise"])
    elif node["kind"] == "loop":
        yield from executable_nodes(node["body"])


def controlled_runtime():
    runtime = assurance_runtime()
    for binding in runtime["nodes"].values():
        binding["model"] = "broodling-issue17-controlled-leaf"
        binding["connections"] = {
            "fixture": [
                "OPENAI_API_KEY",
                "BROODLING_FIXTURE_SCENARIO",
                "BROODLING_FIXTURE_STATE",
            ]
        }
    return runtime


async def run_case(run_root, workspace_root, name, scenario, *, graph=None):
    from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan

    workspace = workspace_root / name
    workspace.mkdir(parents=True)
    git(workspace, "init", "-b", "main")
    git(workspace, "config", "user.name", "Broodling Product Controls")
    git(workspace, "config", "user.email", "broodling-controls@example.invalid")
    git(
        workspace,
        "remote",
        "add",
        "origin",
        "https://github.com/example/broodling-controls.git",
    )
    (workspace / "candidate.txt").write_text("C0_ADMITTED\n")
    (workspace / "evidence").mkdir()
    (workspace / "evidence/raw.txt").write_text("REQUIRED_RAW_EVIDENCE\n")
    git(workspace, "add", ".")
    git(workspace, "commit", "-m", "admitted C0 fixture")
    leaf_state = run_root / "leaf" / name
    environment = {
        "PATH": f"{LEAF_BIN}{os.pathsep}{os.environ.get('PATH', '')}",
        "OPENAI_API_KEY": "test-only-not-a-credential",
        "BROODLING_FIXTURE_STATE": str(leaf_state),
        "BROODLING_FIXTURE_SCENARIO": scenario,
    }
    graph = assurance_graph() if graph is None else graph
    runtime = controlled_runtime()
    initial = initial_state(CONTRACT)
    request = RunRequest(
        title=f"Broodling #17 mechanical control: {name}",
        graph=GraphSpec.from_dict(graph),
        runtime=RuntimePlan.from_dict(runtime),
        initial_input=initial,
        submission_key=f"broodling-issue17-{name}",
    )
    async with Client(
        target=LocalTarget(workspace, state_dir=run_root / "native"),
        environment=environment,
    ) as client:
        run = await client.submit(request)
        # The campaign's own deadline, not a product timing guarantee: node
        # timeouts stay at the authored 300000ms. `sticky-exhaust` is the
        # longest case at 19 node executions and measures 34-36s here, and 60s
        # left too little margin on a host whose wall clock for these campaigns
        # has been recorded varying by more than half again on unchanged code --
        # a slow sample truncated the run and failed its terminal assertions.
        # 90s matches what the #18 campaign already allows its own longest runs.
        result = await run.wait(wait_timeout=90)
        status = await run.status()
    transcript = leaf_state / "driver.jsonl"
    return {
        "runId": run.id,
        "scenario": scenario,
        "result": jsonable(result),
        "status": jsonable(status),
        "graphSha256": canonical_hash(graph),
        "runtimeSha256": canonical_hash(runtime),
        "initialInput": initial,
        "events": [json.loads(line) for line in transcript.read_text().splitlines()]
        if transcript.exists()
        else [],
        # Written by the leaf from inside the intentional hang, per node.
        "hangEntered": sorted(
            marker.name.removesuffix(".hang-entered")
            for marker in leaf_state.glob("*.hang-entered")
        )
        if leaf_state.exists()
        else [],
    }


def definitions():
    """The minimal discriminating real-Zeroshot witness set (issue #45).

    Each entry is here because Zeroshot's own execution behaviour is the claim:
    a route actually taken end to end, or one class of provider misbehaviour the
    runtime has to turn into a node error. Which *node* a fault is injected at is
    not a claim — the authored graph catches every executable occurrence on its
    own route, and `test_assurance_graph_structure.py` reads that off the graph —
    so each fault class is witnessed once, at one representative node.

    The clean route and the found -> adjudicated -> repaired -> resolved route
    are not run here. The #18 campaign's `valid` and `repair-renewed` take those
    same two routes through the *exact* product graph and runtime -- this
    campaign substitutes the runtime's binding models -- while additionally
    driving the real deterministic evidence leaf, so they are the stronger
    witness of the same execution. The assertions those two runs carried moved
    with them: route order, the adjudicator receiving the review's actual
    finding, and the repair handoff carrying the directive and nothing else.

    Where a fault is injected is not a claim either, so each fault class is
    witnessed at the earliest occurrence that can carry it: process death at
    `implement`, the first executable node, and every response defect at
    `initial_review`, the first model node declaring both a signal and an output
    payload. That an error at a *later* occurrence is caught on that
    occurrence's own route, and that a control error inside the loop stops the
    loop rather than buying another round, are authored facts asserted against
    the graph -- `UNUSABLE_ROUTES`, and `round_complete`'s error in both the
    loop's `until` and `post_repair_bound_route`.

    Eight W3 demonstrations are named by the v0.5 plan but not run here, because
    each is either established below the seam or already witnessed elsewhere.
    Removing required evidence is exercised by the #18 campaign's own
    `missing-initial`, which deletes the material for real and runs the exact
    product graph through the deterministic leaf rather than a model leaf
    reporting `missing`; the renewed occurrence carries the same authored
    missing->fail branch and is not run again. Resolution-by-omission is the
    omitted-signal rule witnessed by `missing-initial_review`, whose response is
    valid in every other respect so the runtime has to reject it on the absent
    signal; that it is `resolution_authority` omitting the signal is the part
    the graph decides.

    Ordinary output acquiring authority is decided by the graph: routing guards
    name the node whose signal they read, and a review declares only `findings`,
    so a review claiming `decision` or `assessment` routes nothing no matter what
    the runtime does with the claim. Measured, the runtime refuses the response
    outright -- an otherwise valid review response carrying undeclared authority
    fields fails `execution_unusable` -- which makes a run of it a third spelling
    of malformed rather than a witness of the guarantee.

    The widened-binding canary mutates the product graph to bind raw findings
    into `repair`, and the invariant it guards is the authored input of `repair`,
    read directly from the graph. Its one runtime premise -- that a bound state
    path really is delivered -- is witnessed on the *unmodified* graph by the #18
    campaign's `repair-renewed`, where the bytes the review emitted as
    `findingContent` reach `adjudicate_authority`. The original canary evidence
    is retained in `issue-17-controls.json`.

    The final assessor's refusal and gap routes are not run here either. #45
    treats that routing as Broodling-owned structural behaviour, and
    `test_no_unusable_or_unaccepted_route_can_reach_an_accepting_sink` asserts
    both final routes branch for branch: the `gap` and `refused` guards, the
    `semantic_gap` and `authority_gap` sinks they reach, the acceptance that is
    reachable only as the fall-through once both are taken out, and the three
    labels an assessor may emit. What a real run added is that the runtime routes
    a non-accepted signal label to its authored sink rather than falling through
    -- witnessed by `sticky-exhaust` on `resolution_authority`'s `open_d1`, and
    at a final assessor itself by the #18 campaign's `wrong-population`, which
    signals `gap` and fails `semantic_gap` on the exact product graph.
    """
    routes = [
        # Three rounds resolving on the last allowed one: fresh candidate per
        # round, no reuse of an earlier round's assurance, and acceptance.
        ("repeat-labels", "repeat-labels", None),
    ]
    terminals = [
        # The bound is reached with the obligation still open: a signalled
        # non-accepting label reaching its authored failure sink instead of
        # falling through. The final assessor's two other non-accepting labels,
        # `gap` and `refused`, are that same runtime mechanism one node later.
        # Which sink each reaches is authored -- `final_route_clean` and
        # `final_route_repaired` are asserted branch for branch -- and that the
        # runtime really does route a non-accepted assessment off an assessor
        # is witnessed for real by the #18 campaign's `wrong-population`.
        ("sticky-exhaust", "sticky-exhaust", "obligations_exhausted"),
    ]
    faults = [
        # Process death at an executable node becomes a node error, witnessed at
        # the first executable occurrence there is.
        ("crash-implement", "clean;crash:implement", None),
        # The ways the pinned runtime has to reject a response that did arrive.
        # Each isolates one defect: the required signal omitted from an otherwise
        # valid response, an unparseable payload, an empty default, and a
        # declared output payload the model omitted. All four are injected at the
        # first model node that declares both a signal and an output payload.
        ("missing-initial_review", "clean;missing:initial_review", None),
        ("malformed-initial_review", "clean;malformed:initial_review", None),
        ("default-initial_review", "clean;default:initial_review", None),
        (
            "missing-payload-initial_review",
            "clean;missing_payload:initial_review",
            None,
        ),
        # A node that never answers is terminated by the runtime, not waited on.
        ("hang-initial_review", "clean;hang:initial_review", None),
    ]
    return (
        routes
        + terminals
        + [(name, scenario, "execution_unusable") for name, scenario, _ in faults]
    )


async def run_controls(run_root, workspace_root):
    """Every case records its graph deviation, explicitly null when it has none.

    One case submits something other than the product graph, and a reader of the
    retained record has to be able to tell which without recomputing hashes: the
    hang shortens one node's timeout so the fault fits the campaign.
    `graph_deviations_are_declared` below holds this field to the graph that was
    actually submitted: a declaration is present exactly when the submitted hash
    differs from the product graph's.
    """
    cases = {}
    for name, scenario, _ in definitions():
        graph = assurance_graph()
        deviation = None
        if ";hang:" in scenario:
            selected = scenario.split(":")[1]
            for node in executable_nodes(graph["root"]):
                if node["name"] == selected:
                    node["timeoutMs"] = 250
            deviation = {
                "node": selected,
                "change": "timeoutMs shortened to 250 so the hang terminates "
                "inside the campaign; the product node is 300000",
            }
        cases[name] = await run_case(
            run_root, workspace_root, name, scenario, graph=graph
        )
        cases[name]["testOnlyGraphDeviation"] = deviation
    return cases


def acceptance_checks(cases):
    repeated = cases["repeat-labels"]
    exhaustion = cases["sticky-exhaust"]
    mutations = [e for e in repeated["events"] if e["node"] in {"implement", "repair"}]
    return {
        "expected_terminal_routes": all(
            case["result"]["succeeded"]
            if reason is None
            else not case["result"]["succeeded"] and case["result"]["failure"] == reason
            for name, _, reason in definitions()
            for case in [cases[name]]
        ),
        "repeated_labels_cannot_reuse_old_assurance": nodes(repeated)
        == CLEAN_ORDER[:-1] + ROUND_ORDER * 3 + ["final_assessment_authority_repaired"]
        and [e["candidate"] for e in mutations]
        == [
            "C1_FROM_IMPLEMENT\n",
            "C2_FROM_REPAIR\n",
            "C3_FROM_REPAIR\n",
            "C4_FROM_REPAIR\n",
        ]
        and {e["workerGenerationClaim"] for e in mutations} == {"c2"},
        "structural_generation_has_no_worker_ordinal_state": all(
            "candidateGeneration" not in case["initialInput"]
            and all(
                "candidateGeneration" not in (e["input"] or {}) for e in case["events"]
            )
            for case in cases.values()
        ),
        "fresh_candidate_visible_to_each_round": all(
            e["candidate"] == f"C{e['invocation'] + 1}_FROM_REPAIR\n"
            for e in repeated["events"]
            if e["node"] in ROUND_ORDER
        ),
        # Provenance, exactly as far as it goes: a case submitting anything but
        # the product graph carries a declaration, and a case carrying one really
        # did submit a different graph. The declaration's text describes the
        # change for a reader; it is not itself checked against the bytes. What
        # this catches is a future case that modifies the graph and says nothing.
        "graph_deviations_are_declared": all(
            (case["graphSha256"] == canonical_hash(assurance_graph()))
            is (case["testOnlyGraphDeviation"] is None)
            for case in cases.values()
        )
        and sorted(
            name
            for name, case in cases.items()
            if case["testOnlyGraphDeviation"] is not None
        )
        == ["hang-initial_review"],
        # The graph binds no diagnostic channel, so every response's forged
        # identifiers and private note are non-authoritative in every run.
        "private_diagnostics_never_reach_a_bound_input": all(
            token not in json.dumps(e["input"])
            for case in cases.values()
            for e in case["events"]
            if e["input"] is not None
            for token in ("PRIVATE_AUTHORITY_CANARY", "forged-")
        ),
        "sticky_directive_survives_three_clean_reviews": all(
            e["input"]["outstanding"] == "open_d1"
            and e["input"]["directiveContent"] == DIRECTIVE
            and e["input"]["findings"] == "clean"
            for e in exhaustion["events"]
            if e["node"] == "resolution_authority"
        )
        and all(
            e["input"]["directiveContent"] == DIRECTIVE
            for e in exhaustion["events"]
            if e["node"] == "repair"
        ),
        "bound_three_stops_before_final": nodes(exhaustion).count("repair") == 3
        and not any(n.startswith("final_assessment") for n in nodes(exhaustion)),
        # A hang case is only a witness of a *provider* hang if the provider
        # got as far as hanging. The leaf writes this marker from inside the
        # intentional hang; a node timed out during startup would leave it
        # absent, and the run would fail identically from the outside.
        "hang_terminated_a_provider_inside_the_intentional_hang": cases[
            "hang-initial_review"
        ]["hangEntered"]
        == ["initial_review"],
        "control_faults_stop_before_final": all(
            not any(n.startswith("final_assessment") for n in nodes(cases[name]))
            for name, _, reason in definitions()
            if reason == "execution_unusable"
        ),
        "diagnostics_do_not_retarget_contract_or_bypass_repair": repeated["result"][
            "output"
        ]["contract"]
        == CONTRACT
        and "repair" in nodes(repeated),
        "qualified_sandbox_and_session_modes": all(
            e["argv"][e["argv"].index("--sandbox") + 1]
            == (
                "workspace-write"
                if e["node"] in {"implement", "repair"}
                else "read-only"
            )
            and "resume" not in e["argv"]
            for e in repeated["events"]
        ),
    }
