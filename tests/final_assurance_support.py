"""Real admitted #19 controls; only the external model executable is substituted.

Candidate fixture paths are data, not a product material-selection policy. The
optional Contract transform lets admission tests declare that policy explicitly.
"""

import asyncio
import dataclasses
import json
import shutil
import tempfile
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from evidence_support import raw_material
from support import AttemptTestCase, criterion, git

from broodling import EvidencePopulation
from broodling.codex_profile import QualifiedCodexProfile
from broodling.contract import MechanicalEvidence
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter

LEAF = Path(__file__).parent / "fixtures/final-assurance-bin/codex"


@contextmanager
def final_case(scenario="clean", *, contract_transform=None):
    fixture = AttemptTestCase()
    fixture.setUp()
    run_root = Path(tempfile.mkdtemp(prefix="b19-", dir="/dev/shm"))
    try:
        repository = fixture.repository
        git(
            repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        (repository / "candidate.json").write_text('{"generationMaterial":"B1"}\n')
        raw = raw_material("valid")
        raw["needsCorrection"] = scenario in {
            "repair",
            "sticky",
            "missing-renewed",
            "forged-identifiers",
        }
        if scenario == "semantic-gap":
            raw["population"] = ["unrelated"]
        (repository / "desired.json").write_text(json.dumps(raw))
        (repository / "check.py").write_text(
            "from pathlib import Path\nprint(Path('candidate.json').read_text(), end='')\n"
        )
        git(repository, "add", ".")
        git(repository, "commit", "-m", "final assurance control at B1")
        required = criterion(
            statement=(
                "Require positive, negative and boundary PASS observations for candidate.json "
                "on qualified-single-host in actual mode without contradictions. Correct "
                "needsCorrection and retain the directive until correctionSatisfied is true."
            ),
            evidence_population=EvidencePopulation(
                "enumerated", ("positive", "negative", "boundary")
            ),
            mechanical_evidence=MechanicalEvidence(
                argv=("/usr/bin/python3", "check.py"),
                cwd=".",
                materials=("candidate.json",),
            ),
        )
        contract = dataclasses.replace(fixture.revision.contract, criteria=(required,))
        if contract_transform is not None:
            contract = contract_transform(contract)
        revision = fixture.store.record_contract_revision(contract)
        fixture.store.admit(revision.contract_revision_id)
        provisioned = fixture.provisioner().admit_and_provision(
            revision.contract_revision_id, repository
        )
        state = run_root / "model-leaf"
        state.mkdir()
        executable = run_root / "controlled-codex"
        executable.write_text(
            "#!/usr/bin/env python3\nimport os,sys\n"
            f"os.environ['BROODLING_FINAL_TEST_STATE'] = {str(state)!r}\n"
            f"os.environ['BROODLING_FINAL_TEST_SCENARIO'] = {scenario!r}\n"
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
        row = coordinator.submit_assurance(provisioned.attempt.attempt_id)

        def release():
            (state / "release").touch()

        def events():
            transcript = state / "driver.jsonl"
            return (
                [json.loads(line) for line in transcript.read_text().splitlines()]
                if transcript.exists()
                else []
            )

        yield SimpleNamespace(
            fixture=fixture,
            store=fixture.store,
            revision=revision,
            attempt=provisioned.attempt,
            attempt_id=provisioned.attempt.attempt_id,
            path=provisioned.path,
            adapter=adapter,
            coordinator=coordinator,
            row=row,
            request=json.loads(row.request_json),
            run_root=run_root,
            state=state,
            release=release,
            events=events,
        )
    finally:
        shutil.rmtree(run_root, ignore_errors=True)
        fixture.tearDown()
        fixture.doCleanups()


async def observe_released(case, operation=None):
    """Release the model after the adapter reads its real initial public status.

    Instrumentation forwards the unchanged status and cursor. Every watch event
    and final result still comes from the actual public SDK and product adapter.
    """
    from zeroshot import Client, LocalTarget

    async with Client(
        target=LocalTarget(case.path, state_dir=case.run_root / "native"),
        environment=case.request["target"]["environment"],
    ) as client:
        handle_type = type(client.get_run(case.row.run_id))
    original = handle_type.status

    async def release_after_status(handle, *args, **kwargs):
        status = await original(handle, *args, **kwargs)
        case.release()
        return status

    with patch.object(handle_type, "status", release_after_status):
        return await asyncio.wait_for(
            operation
            if operation is not None
            else case.adapter.observe_current(case.request, case.row.run_id),
            timeout=90,
        )
