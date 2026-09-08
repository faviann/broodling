"""Admitted #18 controls with real deterministic evidence and controlled semantics."""

import asyncio
import dataclasses
import json
import shutil
import tempfile
import uuid
from pathlib import Path
from unittest.mock import patch

from assurance_support import canonical_hash, jsonable
from support import AttemptTestCase, criterion, git

from broodling import EvidencePopulation
from broodling.assurance_graph import assurance_graph, assurance_runtime
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter

ROOT = Path(__file__).resolve().parents[1]
LEAF = ROOT / "tests/fixtures/evidence-bin/codex"
SCENARIOS = (
    "valid",
    "wrong-population",
    "wrong-host",
    "wrong-mode",
    "wrong-artifact",
    "contradiction",
    "insufficient",
    "missing-initial",
    "missing-renewed",
    "repair-renewed",
    "sticky",
    "timeout-descendant",
)
SEMANTIC_GAPS = SCENARIOS[1:7]


def raw_material(scenario):
    raw = {
        "population": ["positive", "negative", "boundary"],
        "host": "qualified-single-host",
        "mode": "actual",
        "artifact": "candidate.json",
        "results": ["PASS", "PASS", "PASS"],
        "contradictions": [],
        "needsCorrection": scenario in {"missing-renewed", "repair-renewed", "sticky"},
        "correctionSatisfied": False,
    }
    mutations = {
        "wrong-population": ("population", ["unrelated"]),
        "wrong-host": ("host", "different-host"),
        "wrong-mode": ("mode", "simulated"),
        "wrong-artifact": ("artifact", "unrelated.json"),
        "contradiction": ("contradictions", ["FAIL_UNEXPLAINED despite PASS"]),
        "insufficient": ("results", ["PASS"]),
    }
    if scenario in mutations:
        key, value = mutations[scenario]
        raw[key] = value
    return raw


def run_case(scenario):
    fixture = AttemptTestCase()
    fixture.setUp()
    run_root = Path(tempfile.mkdtemp(prefix="b18e-", dir="/dev/shm"))
    marker = "broodling-evidence-child-" + uuid.uuid4().hex

    def matching_processes():
        matches = []
        for entry in Path("/proc").iterdir():
            if not entry.name.isdecimal():
                continue
            try:
                arguments = (entry / "cmdline").read_bytes().split(b"\0")
            except (OSError, PermissionError):
                continue
            if marker.encode() in arguments:
                matches.append(int(entry.name))
        return matches

    try:
        repository = fixture.repository
        git(
            repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        (repository / "candidate.json").write_text('{"generationMaterial":"B1"}')
        (repository / "desired.json").write_text(json.dumps(raw_material(scenario)))
        (repository / "check.py").write_text(
            "from pathlib import Path\nprint(Path('candidate.json').read_text())\n"
        )
        if scenario == "timeout-descendant":
            (repository / "check.py").write_text(
                "import subprocess,time\n"
                f"subprocess.Popen(['/usr/bin/python3','-c','import time; time.sleep(60)',{marker!r}])\n"
                "time.sleep(60)\n"
            )
        git(repository, "add", ".")
        git(repository, "commit", "-m", "explicit evidence fixture at comparison base")
        b1 = git(repository, "rev-parse", "HEAD")
        _, _, contract = fixture.admissible_contract()
        required = criterion(
            statement=(
                "Require positive, negative and boundary PASS results for candidate.json "
                "on qualified-single-host in actual mode without unexplained contradictions. "
                "An observation claiming another population/host/mode/artifact is insufficient. "
                "When needsCorrection is true, correct candidate.json and retain the directive "
                "until correctionSatisfied is explicitly true."
            ),
            evidence_population=EvidencePopulation(
                "enumerated", ("positive", "negative", "boundary")
            ),
            validation_seam="Descriptive seam; never a path $(touch forbidden-seam)",
            validation_action="Descriptive action; never shell $(touch forbidden-action)",
            mechanical_evidence=MechanicalEvidence(
                argv=("/usr/bin/python3", "check.py"),
                cwd=".",
                materials=("candidate.json",),
            ),
        )
        contract = dataclasses.replace(contract, criteria=(required,))
        revision = fixture.store.record_contract_revision(contract)
        fixture.store.admit(revision.contract_revision_id)
        provisioned = fixture.provisioner().admit_and_provision(
            revision.contract_revision_id, repository
        )
        state = run_root / "model-leaf"
        executable = run_root / "controlled-codex"
        executable.write_text(
            "#!/usr/bin/env python3\nimport os,sys\n"
            f"os.environ['BROODLING_EVIDENCE_TEST_STATE'] = {str(state)!r}\n"
            f"os.environ['BROODLING_EVIDENCE_TEST_SCENARIO'] = {scenario!r}\n"
            f"os.execv({str(LEAF)!r}, [{str(LEAF)!r}, *sys.argv[1:]])\n"
        )
        executable.chmod(0o755)
        home, codex_home = run_root / "empty-home", run_root / "auth-only-home"
        home.mkdir()
        codex_home.mkdir()
        (codex_home / "auth.json").write_text("{}")
        adapter = ZeroshotSubmitter(
            run_root / "native",
            codex_profile=QualifiedCodexProfile(executable, home, codex_home),
        )
        coordinator = SubmissionCoordinator(fixture.store, adapter)
        graph = assurance_graph()
        deviation = None
        if scenario == "timeout-descendant":
            from assurance_support import executable_nodes

            for node in executable_nodes(graph["root"]):
                if node["name"] == "initial_evidence_check":
                    node["timeoutMs"] = 1_000
            deviation = {"node": "initial_evidence_check", "timeoutMs": 1_000}
        with patch("broodling.assurance_graph.assurance_graph", return_value=graph):
            row = coordinator.submit_assurance(provisioned.attempt.attempt_id)
        request = json.loads(row.request_json)

        async def wait():
            from zeroshot import Client, LocalTarget

            async with Client(
                target=LocalTarget(provisioned.path, state_dir=run_root / "native"),
                environment=request["target"]["environment"],
            ) as client:
                run = client.get_run(row.run_id)
                task = asyncio.create_task(run.wait(wait_timeout=90))
                seen = []
                if scenario == "timeout-descendant":
                    while not task.done():
                        seen.extend(matching_processes())
                        await asyncio.sleep(0.02)
                result = await task
                return jsonable(result), jsonable(await run.status()), sorted(set(seen))

        result, status, seen = asyncio.run(wait())
        with patch("broodling.assurance_graph.assurance_graph", return_value=graph):
            replay = coordinator.submit_assurance(provisioned.attempt.attempt_id)
        transcript = state / "driver.jsonl"
        return {
            "scenario": scenario,
            "runId": row.run_id,
            "result": result,
            "status": status,
            "events": [
                json.loads(line) for line in transcript.read_text().splitlines()
            ],
            "initialInput": request["initialInput"],
            "b1": b1,
            "frozenContract": json.loads(revision.canonical_bytes),
            "graphSha256": canonical_hash(request["graph"]),
            "runtimeSha256": canonical_hash(request["runtime"]),
            "profileIdentity": request["target"]["codexProfile"],
            "testOnlyGraphDeviation": deviation,
            "observedDescendantPidsDuringCheck": seen,
            "descendantPidsAfterTerminal": matching_processes(),
            "exactProductGraphAndRuntime": request["graph"] == assurance_graph()
            and request["runtime"] == assurance_runtime(),
            "sameReplayedRunAndRequest": replay.run_id == row.run_id
            and replay.request_json == row.request_json,
            "freeformStringsNotExecuted": not any(
                (provisioned.path / name).exists()
                for name in ("forbidden-action", "forbidden-seam")
            ),
        }
    finally:
        shutil.rmtree(run_root, ignore_errors=True)
        fixture.tearDown()
        fixture.doCleanups()


def acceptance_checks(cases):
    def events(name, node):
        return [e for e in cases[name]["events"] if e["node"] == node]

    def observed(event):
        return event["input"]["evidenceContent"]["observations"][0]

    def raw(event):
        return json.loads(observed(event)["stdout"])

    valid = cases["valid"]
    initial = events("repair-renewed", "initial_review")[0]
    renewed = events("repair-renewed", "repair_review")[0]
    repair = events("repair-renewed", "repair")[0]
    stable = events("sticky", "resolution_authority")
    return {
        "valid_complete_raw_evidence_reaches_acceptance": valid["result"]["succeeded"]
        and raw(events("valid", "initial_review")[0])["results"] == ["PASS"] * 3,
        "all_semantic_mismatches_fail_at_final_despite_available_evidence": all(
            not cases[name]["result"]["succeeded"]
            and cases[name]["result"]["failure"] == "semantic_gap"
            and events(name, "initial_review")[0]["input"]["evidence"] == "valid"
            and events(name, "final_assessment_authority_clean")[0]["response"][
                "signals"
            ]["assessment"]
            == "gap"
            for name in SEMANTIC_GAPS
        ),
        "initial_missing_stops_before_review": cases["missing-initial"]["result"][
            "failure"
        ]
        == "required_evidence_missing"
        and [e["node"] for e in cases["missing-initial"]["events"]] == ["implement"],
        "renewed_missing_stops_before_fresh_review_or_final": cases["missing-renewed"][
            "result"
        ]["failure"]
        == "required_evidence_missing"
        and not events("missing-renewed", "repair_review")
        and not any(
            e["node"].startswith("final_assessment")
            for e in cases["missing-renewed"]["events"]
        ),
        "renewed_evidence_observes_structurally_current_candidate": cases[
            "repair-renewed"
        ]["result"]["succeeded"]
        and raw(initial)["generationMaterial"] == "AFTER_IMPLEMENT"
        and raw(renewed)["generationMaterial"] == "AFTER_IMPLEMENT:AFTER_REPAIR"
        and json.loads(observed(renewed)["materials"][0]["content"]) == raw(renewed),
        "frozen_contract_population_and_comparison_cannot_be_retargeted": all(
            event["input"]["contract"]
            == json.dumps(case["frozenContract"], sort_keys=True, separators=(",", ":"))
            and event["input"]["comparisonBase"] == case["b1"]
            and json.loads(observed(event)["population"])
            == case["frozenContract"]["criteria"][0]["evidencePopulation"]
            and observed(event)["criterionId"] == "c1"
            for case in cases.values()
            for event in case["events"]
            if event["node"].endswith("review")
        ),
        "repair_handoff_excludes_evidence_raw_findings_and_private_rationale": set(
            repair["input"]
        )
        == {"contract", "directive", "directiveContent"}
        and repair["input"]["directiveContent"]
        == {
            "directive": "Correct candidate.json",
            "correction": "Set correctionSatisfied true",
        }
        and all(
            token not in json.dumps(repair["input"])
            for token in (
                "RAW_REJECTED_FINDING_CANARY",
                "PRIVATE_AUTHORITY_CANARY",
                "FORGED_",
            )
        ),
        "sticky_directive_survives_clean_review_until_explicit_resolution": len(stable)
        == 3
        and cases["sticky"]["result"]["failure"] == "obligations_exhausted"
        and all(
            e["input"]["findings"] == "clean"
            and e["input"]["outstanding"] == "open_d1"
            and e["input"]["directiveContent"] == repair["input"]["directiveContent"]
            for e in stable
        ),
        "deterministic_evidence_never_reaches_model_executable": all(
            not e["node"].endswith("evidence_check")
            for case in cases.values()
            for e in case["events"]
        ),
        "observation_carries_actual_check_profile_and_required_raw_material": all(
            observed(event)["argv"] == ["/usr/bin/python3", "check.py"]
            and observed(event)["cwd"] == "."
            and observed(event)["mode"] == "read-only/no-network"
            and json.loads(observed(event)["host"])["system"] == "Linux"
            and observed(event)["exitCode"] == 0
            and observed(event)["stderr"] == ""
            and observed(event)["materials"][0]["path"] == "candidate.json"
            and json.loads(observed(event)["materials"][0]["content"]) == raw(event)
            for case in cases.values()
            for event in case["events"]
            if event["node"].endswith("review")
        ),
        "exact_product_graph_runtime_and_replay": all(
            (
                case["exactProductGraphAndRuntime"]
                or case["testOnlyGraphDeviation"]
                == {"node": "initial_evidence_check", "timeoutMs": 1_000}
            )
            and case["sameReplayedRunAndRequest"]
            for case in cases.values()
        ),
        "native_timeout_terminates_check_descendants_before_terminal": (
            cases["timeout-descendant"]["result"]["failure"] == "execution_unusable"
            and bool(cases["timeout-descendant"]["observedDescendantPidsDuringCheck"])
            and not cases["timeout-descendant"]["descendantPidsAfterTerminal"]
            and [e["node"] for e in cases["timeout-descendant"]["events"]]
            == ["implement"]
        ),
        "freeform_contract_strings_never_interpreted_as_commands": all(
            case["freeformStringsNotExecuted"] for case in cases.values()
        ),
        "all_review_occurrences_keep_qualified_argv": all(
            event["argv"][event["argv"].index("--sandbox") + 1] == "read-only"
            and all(
                flag in event["argv"]
                for flag in ("--ignore-user-config", "--ignore-rules", "--ephemeral")
            )
            and "sandbox_workspace_write.network_access=false" in event["argv"]
            and "resume" not in event["argv"]
            and "--add-dir" not in event["argv"]
            for case in cases.values()
            for event in case["events"]
            if event["node"].endswith("review")
        ),
    }
