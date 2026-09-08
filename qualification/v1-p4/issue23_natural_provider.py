"""Bounded natural CSV import task through the unchanged actual-provider product.

All model roles use actual qualified Codex. This ordinary engineering task has
fully visible correctness requirements and a fixed smoke-check population. No
initial defect or repair is prescribed. A clean result is valid task completion
but does not witness the issue-23 repair path. No graph, prompt, verifier response,
candidate write, or provider output is substituted after admission. Public
diagnostic collection confers no authority. Freeze this fixture before launch;
at most two independent runs of this same fixture are authorized.
"""

import argparse
import ast
import asyncio
import dataclasses
import hashlib
import json
import os
import platform
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import time
import traceback
from contextlib import aclosing
from datetime import UTC, datetime
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from assurance_support import jsonable
from support import AttemptTestCase, criterion, durable_test_root, git, work_reference

from broodling import EvidencePopulation, FinalAssuranceMaterial, SourceSubmission
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.profile import runtime_versions
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import (
    ZeroshotSubmitter,
    assert_qualified_integration,
    installed_integration,
)

SOURCE = """Finish the CSV import helper in csv_records.py for a small contacts importer.
Expose read_records(text), returning a list of dictionaries with normalized
column names and original cell strings. Use Python's standard CSV quoting rules
(including quoted commas, doubled quotes, and quoted newlines). Trim surrounding
Unicode whitespace from each header; do not trim or coerce cell values. Ignore
empty physical CSV rows (csv.reader rows equal to []), both before the header
and between records. The first nonempty row is the header. An empty document or
a document containing only empty rows returns []. A header without records also
returns [], but the header must still be validated. Reject an empty normalized
header name or duplicate normalized header name with ValueError. Reject any data
row whose field count differs from the header count with ValueError; never
silently lose fields or invent missing values. Malformed CSV syntax, Unicode BOM
handling, and non-string inputs are outside scope. Keep check_csv_records.py
unchanged. Its fixed valid-input smoke cases are necessary evidence, not an
exhaustive proof: all the requirements above remain part of acceptance.
"""
CSV_RECORDS = """import csv
import io


def read_records(text):
    return list(csv.DictReader(io.StringIO(text)))
"""
CHECK = r'''import json
import sys
from csv_records import read_records

cases = [
    ('ordinary', 'name,city\nAda,London\nGrace,New York\n',
     [{'name': 'Ada', 'city': 'London'}, {'name': 'Grace', 'city': 'New York'}]),
    ('quoted', 'name,note\n"Doe, Jane","said ""hi"""\n',
     [{'name': 'Doe, Jane', 'note': 'said "hi"'}]),
    ('empty', '', []),
]
observations = []
for name, source, expected in cases:
    try:
        actual = read_records(source)
        observation = dict(case=name, source=source, expected=expected,
                           actual=actual, passed=actual == expected)
    except Exception as error:
        observation = dict(case=name, source=source, expected=expected,
                           error=type(error).__name__ + ': ' + str(error), passed=False)
    observations.append(observation)
print(json.dumps(observations, sort_keys=True))
sys.exit(0 if all(item['passed'] for item in observations) else 1)
'''
NOTES = (
    "Implement the complete visible CSV import Contract in the first occurrence. "
    "The B1 implementation is editable legacy code; no part must be preserved. "
    "The immutable checker covers only the stated ordinary, quoted, and empty "
    "valid-input population. Its success does not waive other Contract behavior. "
    "Independent review and final assessment must justify the remaining behavior "
    "from the actual source and retained context; absence of rejection smoke cases "
    "alone is not a defect or reason to require repair. No particular initial outcome or "
    "assurance route is required. Do not change check_csv_records.py, do not "
    "commit or publish, and do not introduce effects."
)


def hashes():
    paths = [*sorted((ROOT / "broodling").glob("*.py"))]
    paths += [ROOT / "broodling/codex_bin/codex", Path(__file__).resolve()]
    paths += [ROOT / "tests/support.py", ROOT / "tests/assurance_support.py"]
    return {
        str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in paths
    }


def save(path, record):
    temporary = path.with_suffix(path.suffix + ".pending")
    temporary.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    temporary.replace(path)


def responses(record):
    """Index diagnostic response messages by observed public execution identity."""
    executions = {}
    for status in record.get("publicStatuses", []):
        for active in status.get("active_executions", []):
            executions[active["execution"]] = active["node"]
    result = []
    for event in record.get("publicLogs", []):
        try:
            value = json.loads(event["message"])
        except (ValueError, KeyError, TypeError):
            continue
        if isinstance(value, dict) and "response" in value:
            result.append(
                {
                    "node": executions.get(event.get("execution")),
                    "execution": event.get("execution"),
                    "cursor": event.get("cursor"),
                    "response": value["response"],
                }
            )
    return result


def witness_checks(record):
    indexed = record["roleResponses"]

    def role(name):
        return [item for item in indexed if item["node"] == name]

    def last(name):
        values = role(name)
        return values[-1]["response"] if values else {}

    def signal(name, key):
        return last(name).get("signals", {}).get(key)

    initial = (
        last("initial_evidence_check").get("output", {}).get("evidenceContent", {})
    )
    renewed = last("repair_evidence_check").get("output", {}).get("evidenceContent", {})
    initial_observations = initial.get("observations", [])
    renewed_observations = renewed.get("observations", [])
    initial_raw = initial_observations[0] if initial_observations else {}
    renewed_raw = renewed_observations[0] if renewed_observations else {}
    initial_materials = {
        item["path"]: item["content"] for item in initial_raw.get("materials", [])
    }
    renewed_materials = {
        item["path"]: item["content"] for item in renewed_raw.get("materials", [])
    }

    def source_tree(materials):
        try:
            return ast.dump(ast.parse(materials.get("csv_records.py", "")))
        except SyntaxError:
            return None

    try:
        initial_cases = json.loads(initial_raw.get("stdout", ""))
        renewed_cases = json.loads(renewed_raw.get("stdout", ""))
    except ValueError:
        initial_cases, renewed_cases = [], []
    custody = record.get("custody", {})
    directive = (
        last("adjudicate_authority").get("output", {}).get("directiveContent", {})
    )
    required_nodes = (
        "implement",
        "initial_evidence_check",
        "initial_review",
        "adjudicate_authority",
        "repair",
        "repair_evidence_check",
        "repair_review",
        "resolution_authority",
        "round_complete",
        "final_assessment_authority_repaired",
    )
    return {
        "controlStoreOutsideCandidateAndAmbientScratch": record.get(
            "controlStoreOutsideAmbientTmp"
        )
        is True
        and record.get("controlStoreOutsideCandidate") is True,
        "allRequiredActualProductRolesObserved": all(
            role(node) for node in required_nodes
        ),
        "initialRawPopulationPresent": initial_raw.get("exitCode") in (0, 1)
        and [item.get("case") for item in initial_cases]
        == ["ordinary", "quoted", "empty"],
        "independentInitialFinding": signal("initial_review", "findings") == "found"
        and bool(
            last("initial_review").get("output", {}).get("findingContent", "").strip()
        ),
        "adjudicatedSubstantiveDirective": signal("adjudicate_authority", "decision")
        == "open_d1"
        and bool(directive.get("directive", "").strip())
        and bool(directive.get("correction", "").strip()),
        "realSourceCorrection": bool(role("repair"))
        and bool(initial_materials.get("csv_records.py"))
        and bool(renewed_materials.get("csv_records.py"))
        and source_tree(renewed_materials) is not None
        and source_tree(initial_materials) != source_tree(renewed_materials),
        "checkerUnchanged": initial_materials.get("check_csv_records.py") == CHECK
        and renewed_materials.get("check_csv_records.py") == CHECK,
        "renewedRequiredEvidencePasses": renewed_raw.get("exitCode") == 0
        and [item.get("case") for item in renewed_cases]
        == ["ordinary", "quoted", "empty"]
        and all(item["passed"] for item in renewed_cases),
        "freshIndependentReview": bool(role("repair_review"))
        and role("initial_review")[0]["execution"]
        != role("repair_review")[-1]["execution"]
        if role("initial_review")
        else False,
        "explicitEligibleResolution": signal("resolution_authority", "resolution")
        == "resolved_d1",
        "currentFinalAssessment": signal(
            "final_assessment_authority_repaired", "assessment"
        )
        == "accepted"
        and custody.get("finalOccurrence", {}).get("node")
        == "final_assessment_authority_repaired",
        "lastRepairIsRetainedGeneration": bool(role("repair"))
        and custody.get("candidateGeneration", {}).get("mutationExecution")
        == role("repair")[-1]["execution"],
        "sameRenewedEvidenceRetained": custody.get("evidenceContent") == renewed,
        "directiveRetainedThroughResolution": custody.get("assuranceContext", {}).get(
            "directiveContent"
        )
        == directive
        and custody.get("assuranceContext", {}).get("obligation") == "resolved_d1",
        "explicitNoEffects": record["contract"]["requiredEffects"] == [],
        "normalDurableSuccess": record.get("disposition", {}).get("outcome")
        == "SUCCEEDED",
        "oneBoundedRepairPath": 1 <= len(role("repair")) <= 3
        and not role("final_assessment_authority_clean"),
        "justificationReadableAfterFixtureCleanup": record.get("rereadCustody")
        == custody
        and bool(custody)
        and record.get("rereadDisposition") == record.get("disposition"),
    }


async def observe(case, coordinator, record, checkpoint, timeout):
    """Forward-only normal finalization plus independent public diagnostics."""
    from zeroshot import Client, LocalTarget

    record["publicStatuses"] = []
    record["publicLogs"] = []
    async with Client(
        target=LocalTarget(case.path, state_dir=case.runtime_root / "native"),
        environment=case.request["target"]["environment"],
    ) as client:
        run = client.get_run(case.row.run_id)

        async def collect():
            current = await run.status()
            record["publicStatuses"].append(jsonable(current))
            checkpoint()
            if current.phase != "finished":
                async with aclosing(run.watch(after=current.cursor)) as statuses:
                    async for status in statuses:
                        record["publicStatuses"].append(jsonable(status))
                        checkpoint()
                        if status.phase == "finished":
                            return

        # Finalization starts before diagnostic collection; only the product's
        # own live observation can establish current final semantic authority.
        finalize = asyncio.create_task(coordinator.finalize(case.attempt_id))
        diagnostics = asyncio.create_task(collect())
        try:
            disposition = await asyncio.wait_for(finalize, timeout=timeout)
            record["disposition"] = dataclasses.asdict(disposition)
            record["custody"] = coordinator.justification(case.attempt_id).material
            checkpoint()
            await asyncio.wait_for(diagnostics, timeout=30)
        finally:
            for task in (finalize, diagnostics):
                if not task.done():
                    task.cancel()
            await asyncio.gather(finalize, diagnostics, return_exceptions=True)
            # Public logs are diagnostic evidence only, including on failure;
            # they never restore missed final authority or start another assessor.
            try:
                async with asyncio.timeout(30):
                    record["publicLogs"] = [
                        jsonable(event) async for event in run.logs()
                    ]
            except Exception as error:  # noqa: BLE001 - retain diagnostic/cleanup failure evidence
                record["diagnosticLogError"] = repr(error)
            checkpoint()


def close_completed_fixture(workspace, *, timeout=10):
    """Fixture-only launch fence and physical proof after committed disposition."""
    from broodling.containment import close_launches, confirm_ceased

    close_launches(workspace)
    deadline = time.monotonic() + timeout
    while not confirm_ceased(workspace):
        if time.monotonic() >= deadline:
            raise RuntimeError("completed fixture has not confirmed physical cessation")
        time.sleep(0.05)
    return {
        "launchesClosed": True,
        "physicalCessationConfirmed": True,
        "scope": "Fixture-only cleanup after normal committed disposition; no stop or semantic recovery.",
    }


def run(record, checkpoint, output, timeout):
    from types import SimpleNamespace

    from broodling import AbandonmentCoordinator, WorkUnitDispositionCoordinator

    assert_qualified_integration()
    actual = shutil.which("codex")
    if actual is None:
        raise RuntimeError("actual qualified Codex is unavailable")
    fixture = AttemptTestCase()
    # The inexpensive storage fixtures normally use /tmp. Actual mutators may
    # write there, so this witness keeps its control store and adjacent lock in
    # a separate durable location outside both candidate and ambient scratch.
    store_parent = durable_test_root("issue23-natural-control-store-")
    with patch.object(tempfile, "tempdir", str(store_parent)):
        fixture.setUp()
    fixture.addCleanup(shutil.rmtree, store_parent, ignore_errors=True)
    runtime_root = Path(
        tempfile.mkdtemp(prefix="issue23-natural-provider-", dir="/dev/shm")
    )
    case = None
    safe_cleanup = True
    try:
        repository = fixture.repository
        git(
            repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        (repository / "csv_records.py").write_text(CSV_RECORDS)
        (repository / "check_csv_records.py").write_text(CHECK)
        (repository / "README.md").write_text(
            "CSV contacts importer. The frozen Contract governs.\n"
        )
        git(repository, "add", ".")
        git(
            repository,
            "commit",
            "-m",
            "B1 CSV import helper and fixed valid-input smoke population",
        )
        unit = fixture.store.resolve_work_unit(work_reference(issue=23))
        source = fixture.store.entitle_source(
            unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=unit.issue_locator,
                content=SOURCE.encode(),
                media_type="text/markdown; charset=utf-8",
                retrieved_at=datetime.now(UTC).isoformat(),
            ),
        )
        required = criterion(
            criterion_id="csv-records",
            statement=SOURCE,
            evidence_population=EvidencePopulation(
                "enumerated", ("ordinary", "quoted", "empty")
            ),
            validation_seam="csv_records.read_records",
            validation_action="/usr/bin/python3 -B check_csv_records.py",
            falsifying_observation=(
                "Any stated read_records behavior is violated, including empty or "
                "duplicate normalized headers, header-only validation, or row-width "
                "rejection; any smoke case fails; or the checker changes."
            ),
            mechanical_evidence=MechanicalEvidence(
                argv=("/usr/bin/python3", "-B", "check_csv_records.py"),
                cwd=".",
                materials=("csv_records.py", "check_csv_records.py"),
            ),
        )
        contract = fixture.contract(
            unit,
            source,
            criteria=(required,),
            notes=NOTES,
            required_effects=(),
            final_assurance_materials=tuple(
                FinalAssuranceMaterial(path, True, True)
                for path in ("csv_records.py", "check_csv_records.py", "README.md")
            ),
        )
        revision = fixture.store.record_contract_revision(contract)
        fixture.store.admit(revision.contract_revision_id)
        provisioned = fixture.provisioner().admit_and_provision(
            revision.contract_revision_id, repository
        )
        home, codex_home = runtime_root / "empty-home", runtime_root / "auth-only-home"
        home.mkdir()
        codex_home.mkdir(mode=0o700)
        auth_source = (
            Path(os.environ.get("CODEX_HOME", str(Path.home() / ".codex")))
            / "auth.json"
        )
        shutil.copyfile(auth_source, codex_home / "auth.json")
        (codex_home / "auth.json").chmod(0o600)
        profile = QualifiedCodexProfile(Path(actual), home, codex_home)
        adapter = ZeroshotSubmitter(runtime_root / "native", codex_profile=profile)
        record.update(
            controlStorePath=str(fixture.store_path),
            controlStoreOutsideAmbientTmp=not fixture.store_path.resolve().is_relative_to(
                Path("/tmp").resolve()
            ),
            controlStoreOutsideCandidate=not fixture.store_path.resolve().is_relative_to(
                provisioned.path.parent
            ),
            actualProvider=actual,
            actualProviderVersion=subprocess.check_output(
                [actual, "--version"], text=True
            ).strip(),
            contract=json.loads(revision.canonical_bytes),
            contractCanonicalHex=revision.canonical_bytes.hex(),
            contractRevisionId=revision.contract_revision_id,
            entitledSource=SOURCE,
            attempt=dataclasses.asdict(provisioned.attempt),
            b1Files={
                name: subprocess.check_output(
                    [
                        "git",
                        "-C",
                        str(repository),
                        "show",
                        f"{provisioned.attempt.b1_commit_oid}:{name}",
                    ]
                ).decode("utf-8")
                for name in ("csv_records.py", "check_csv_records.py", "README.md")
            },
            profile=profile.identity(),
            initialHomeEntries=[],
            initialCodexHomeEntries=["auth.json"],
        )
        checkpoint()
        case = SimpleNamespace(
            path=provisioned.path,
            attempt_id=provisioned.attempt.attempt_id,
            adapter=adapter,
            runtime_root=runtime_root,
            row=None,
        )
        # Once dispatch begins even a lost acknowledgment requires cessation.
        safe_cleanup = False
        row = SubmissionCoordinator(fixture.store, adapter).submit_assurance(
            case.attempt_id
        )
        case.row, case.request = row, json.loads(row.request_json)
        record.update(request=case.request, runId=row.run_id)
        checkpoint()
        coordinator = WorkUnitDispositionCoordinator(fixture.store, adapter)
        asyncio.run(observe(case, coordinator, record, checkpoint, timeout))
        record["completedFixtureCessation"] = close_completed_fixture(case.path)
        safe_cleanup = True
        record["finalFiles"] = {
            name: (case.path / name).read_text()
            for name in ("csv_records.py", "check_csv_records.py", "README.md")
        }
        record["roleResponses"] = responses(record)
        checkpoint()
        # Fixture-only cleanup, after normal disposition and physical cessation.
        # The store remains open; no product worktree cleanup policy is implied.
        shutil.rmtree(case.path)
        shutil.rmtree(runtime_root)
        fixture.reopen()
        rereader = WorkUnitDispositionCoordinator(fixture.store, None)
        record["rereadDisposition"] = dataclasses.asdict(
            rereader.record(case.attempt_id)
        )
        record["rereadCustody"] = rereader.justification(case.attempt_id).material
        record["checks"] = witness_checks(record)
        checkpoint()
    finally:
        if case is not None and not safe_cleanup:
            try:
                completed = WorkUnitDispositionCoordinator(
                    fixture.store, case.adapter
                ).record(case.attempt_id)
                if completed is not None:
                    record["completedFixtureCessation"] = close_completed_fixture(
                        case.path
                    )
                else:
                    administrator = AbandonmentCoordinator(fixture.store, case.adapter)
                    asyncio.run(
                        administrator.stop(
                            case.attempt_id,
                            "issue23 qualification incomplete; no semantic salvage",
                        )
                    )
                    record["failureCessation"] = dataclasses.asdict(
                        administrator.record(case.attempt_id)
                    )
                safe_cleanup = True
            except Exception as error:  # noqa: BLE001 - retain diagnostic/cleanup failure evidence
                record["cleanupError"] = repr(error)
                record["preservedUnsafeFixture"] = str(fixture.root)
                record["preservedUnsafeWorkspaceRoot"] = str(fixture.workspace_root)
        # Credentials are never exported, including failed observations.
        (runtime_root / "auth-only-home/auth.json").unlink(missing_ok=True)
        database = output.with_suffix(".sqlite3")
        with sqlite3.connect(database) as backup:
            fixture.store.connection.backup(backup)
        record["retainedDatabase"] = str(database)
        record["roleResponses"] = responses(record)
        checkpoint()
        if safe_cleanup:
            shutil.rmtree(runtime_root, ignore_errors=True)
            fixture.store.close()
            fixture.tearDown()
            fixture.doCleanups()
        else:
            fixture.store.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=2400)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.with_suffix(".sqlite3").exists():
        parser.error("retained database already exists; choose a fresh output basename")
    # Never overwrite a historical qualification observation.
    with output.open("x"):
        pass
    record = {
        "scope": __doc__,
        "fixtureNotes": NOTES,
        "startedAt": datetime.now(UTC).isoformat(),
        "sourceSha256": hashes(),
        "host": platform.platform(),
        "passed": False,
        "productCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
        ).strip(),
        "productWorkingTree": subprocess.check_output(
            ["git", "status", "--porcelain"], cwd=ROOT, text=True
        ),
    }
    checkpoint = lambda: save(output, record)
    checkpoint()
    try:
        record["runtime"] = runtime_versions()
        record["integration"] = installed_integration()
        run(record, checkpoint, output, args.timeout)
    except BaseException as error:  # noqa: BLE001 - retain interrupted qualification evidence
        record["error"] = repr(error)
        record["traceback"] = traceback.format_exc()
    finally:
        record["finalSourceSha256"] = hashes()
        record["sourceHashesUnchangedThroughout"] = (
            record["sourceSha256"] == record["finalSourceSha256"]
        )
        record["finishedAt"] = datetime.now(UTC).isoformat()
        record["passed"] = (
            bool(record.get("checks"))
            and all(record["checks"].values())
            and record["sourceHashesUnchangedThroughout"]
            and not any(
                key in record for key in ("error", "cleanupError", "diagnosticLogError")
            )
        )
        checkpoint()
    return 0 if record["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
