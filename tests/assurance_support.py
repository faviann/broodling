"""Public SDK mechanics runner shared by tests and retained #17 evidence.

Only controlled provider behavior is substituted. There is no qualification
import, product router, runtime storage read, or production observer here.
"""

import asyncio
import dataclasses
import hashlib
import json
import os
from pathlib import Path

from support import AttemptTestCase, git

from broodling.assurance_graph import assurance_graph, assurance_runtime, initial_state

ROOT = Path(__file__).resolve().parents[1]
LEAF_BIN = ROOT / "tests/fixtures/assurance-bin"
CONTRACT = "frozen-contract-v1: replace the old greeting; preserve newline"
FINDING = "Candidate still prints the old greeting. RAW_REJECTED_FINDING_CANARY"
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
        result = await run.wait(wait_timeout=60)
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
    }


def definitions():
    cases = [
        (name, name, None)
        for name in (
            "clean",
            "repair-resolve",
            "repeat-labels",
            "contradictory-clean",
            "forged-diagnostics",
            "repair-input-canary",
        )
    ]
    cases += [
        (name, name, reason)
        for name, reason in (
            ("missing-initial", "required_evidence_missing"),
            ("missing-after-repair", "required_evidence_missing"),
            ("sticky-exhaust", "obligations_exhausted"),
            ("sticky-omission", "execution_unusable"),
            ("refusal", "authority_gap"),
            ("final-gap", "semantic_gap"),
        )
    ]
    for node in [*CLEAN_ORDER, *ROUND_ORDER, "final_assessment_authority_repaired"]:
        base = "clean" if node in CLEAN_ORDER else "repair-resolve"
        cases.append((f"crash-{node}", f"{base};crash:{node}", "execution_unusable"))
    for fault in ("missing", "malformed", "default", "hang"):
        for node in ("initial_review", "round_complete"):
            base = "clean" if node == "initial_review" else "repair-resolve"
            cases.append(
                (f"{fault}-{node}", f"{base};{fault}:{node}", "execution_unusable")
            )
    for node in ("implement", "initial_review", "repair"):
        cases.append(
            (
                f"authority-claim-{node}",
                f"repair-resolve;authority_claim:{node}",
                "execution_unusable",
            )
        )
    for node in ("initial_review", "adjudicate_authority"):
        cases.append(
            (
                f"missing-payload-{node}",
                f"repair-resolve;missing_payload:{node}",
                "execution_unusable",
            )
        )
    cases.append(
        (
            "open-control-crash",
            "sticky-exhaust;crash:round_complete",
            "execution_unusable",
        )
    )
    return cases


async def run_controls(run_root, workspace_root):
    cases = {}
    for name, scenario, _ in definitions():
        graph = assurance_graph()
        if ";hang:" in scenario:
            selected = scenario.split(":")[1]
            for node in executable_nodes(graph["root"]):
                if node["name"] == selected:
                    node["timeoutMs"] = 250
        cases[name] = await run_case(
            run_root, workspace_root, name, scenario, graph=graph
        )
        if ";hang:" in scenario:
            cases[name]["testOnlyGraphDeviation"] = {"node": selected, "timeoutMs": 250}
    # Sensitivity control: deliberately widen repair input to include raw findings.
    # This modified graph is evidence-only; product graph remains untouched.
    widened = assurance_graph()
    repair = next(
        node for node in executable_nodes(widened["root"]) if node["name"] == "repair"
    )
    repair["input"]["fields"]["findingContent"] = {
        "required": True,
        "type": {"kind": "string"},
    }
    repair["inputBindings"].append(
        {
            "target": ["findingContent"],
            "value": {"source": "state", "path": ["findingContent"]},
        }
    )
    cases["widened-binding-canary"] = await run_case(
        run_root,
        workspace_root,
        "widened-binding-canary",
        "repair-input-canary",
        graph=widened,
    )
    return cases


def acceptance_checks(cases):
    repair = cases["repair-resolve"]
    repeated = cases["repeat-labels"]
    exhaustion = cases["sticky-exhaust"]
    canary = next(
        e for e in cases["repair-input-canary"]["events"] if e["node"] == "repair"
    )
    widened = next(
        e for e in cases["widened-binding-canary"]["events"] if e["node"] == "repair"
    )
    authority = next(e for e in repair["events"] if e["node"] == "adjudicate_authority")
    mutations = [e for e in repeated["events"] if e["node"] in {"implement", "repair"}]
    return {
        "expected_terminal_routes": all(
            case["result"]["succeeded"]
            if reason is None
            else not case["result"]["succeeded"] and case["result"]["failure"] == reason
            for name, _, reason in definitions()
            for case in [cases[name]]
        ),
        "clean_requires_distinct_final": nodes(cases["clean"]) == CLEAN_ORDER,
        "repair_requires_fresh_evidence_review_authority": nodes(repair)
        == CLEAN_ORDER[:-1] + ROUND_ORDER + ["final_assessment_authority_repaired"],
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
        "actual_findings_reach_adjudicator": authority["input"]["findingContent"]
        == FINDING,
        "actual_directive_reaches_repair_without_raw_findings": canary["input"]
        == {
            "contract": CONTRACT,
            "directive": "open_d1",
            "directiveContent": DIRECTIVE,
        },
        "widened_binding_control_detects_canary": widened["input"]["findingContent"]
        == FINDING
        and "RAW_REJECTED_FINDING_CANARY" not in json.dumps(canary["input"]),
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
        "control_faults_stop_before_final": all(
            not any(
                n.startswith("final_assessment")
                for n in nodes(cases[f"{fault}-round_complete"])
            )
            for fault in ("crash", "missing", "malformed", "default", "hang")
        ),
        "diagnostics_do_not_retarget_contract_or_bypass_repair": all(
            cases[name]["result"]["output"]["contract"] == CONTRACT
            and "repair" in nodes(cases[name])
            for name in ("forged-diagnostics", "contradictory-clean")
        ),
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


def admitted_case(scenario):
    """Exercise the unmodified P3 submission API with a controlled executable.

    The real product launcher, environment, graph and runtime are all retained.
    Only the profile's external Codex executable is a deterministic test leaf.
    """
    import shutil
    import tempfile

    from broodling.codex_profile import QualifiedCodexProfile
    from broodling.submission import SubmissionCoordinator
    from broodling.zeroshot_sdk import ZeroshotSubmitter

    fixture = AttemptTestCase()
    fixture.setUp()
    try:
        repository = fixture.repository
        git(
            repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        (repository / "candidate.txt").write_text("C0_ADMITTED\n")
        (repository / "evidence").mkdir()
        (repository / "evidence/raw.txt").write_text("REQUIRED_RAW_EVIDENCE\n")
        git(repository, "add", ".")
        git(repository, "commit", "-m", "assurance fixture inputs at B1")
        provisioned = fixture.provisioner().admit_and_provision(
            fixture.revision.contract_revision_id, repository
        )
        run_root = Path(tempfile.mkdtemp(prefix="b17a-", dir="/dev/shm"))
        fixture.addCleanup(shutil.rmtree, run_root, ignore_errors=True)
        leaf_state = run_root / "leaf"
        executable = fixture.root / "controlled-codex"
        executable.write_text(
            "#!/usr/bin/env python3\nimport os, sys\n"
            "if sys.argv[1:] == ['--version']:\n print('codex-cli 0.153.4')\n sys.exit(0)\n"
            f"os.environ['BROODLING_FIXTURE_SCENARIO'] = {scenario!r}\n"
            f"os.environ['BROODLING_FIXTURE_STATE'] = {str(leaf_state)!r}\n"
            f"os.execv({str(LEAF_BIN / 'codex')!r}, [{str(LEAF_BIN / 'codex')!r}, *sys.argv[1:]])\n"
        )
        executable.chmod(0o755)
        home = fixture.root / "profile-home"
        home.mkdir()
        codex_home = fixture.root / "codex-home"
        codex_home.mkdir()
        (codex_home / "auth.json").write_text("{}")
        profile = QualifiedCodexProfile(executable, home, codex_home)
        adapter = ZeroshotSubmitter(run_root / "native", codex_profile=profile)
        coordinator = SubmissionCoordinator(fixture.store, adapter)
        row = coordinator.submit_assurance(provisioned.attempt.attempt_id)
        request = json.loads(row.request_json)

        async def wait():
            from zeroshot import Client, LocalTarget

            async with Client(
                target=LocalTarget(provisioned.path, state_dir=run_root / "native"),
                environment=request["target"]["environment"],
            ) as client:
                result = await client.get_run(row.run_id).wait(wait_timeout=60)
                return jsonable(result)

        result = asyncio.run(wait())
        original_request = row.request_json
        # Actual graph-owned mutation has happened; retry must keep the run ID.
        fixture.reopen()
        replay = SubmissionCoordinator(fixture.store, adapter).submit_assurance(
            provisioned.attempt.attempt_id
        )
        events = [
            json.loads(line)
            for line in (leaf_state / "driver.jsonl").read_text().splitlines()
        ]
        return {
            "scenario": scenario,
            "result": result,
            "runId": row.run_id,
            "replayedRunId": replay.run_id,
            "samePersistedRequest": replay.request_json == original_request,
            "workUnitId": fixture.work_unit.work_unit_id,
            "contractRevisionId": fixture.revision.contract_revision_id,
            "attemptId": provisioned.attempt.attempt_id,
            "graphSha256": canonical_hash(request["graph"]),
            "runtimeSha256": canonical_hash(request["runtime"]),
            "initialInput": request["initialInput"],
            "events": events,
            "testOnlySubstitution": "QualifiedCodexProfile real_codex points to a controlled test executable that reports the qualified CLI version; product launcher/graph/runtime/environment and P3 coordinator are unchanged. This proves integration, not real-provider containment.",
        }
    finally:
        fixture.tearDown()
        fixture.doCleanups()
