"""Replacement controls using the product launcher and actual pinned SDK.

Only the external provider is controlled. Observations precede its mutation.
"""

import json
import tempfile
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from final_assurance_support import LEAF, final_case

from broodling import AbandonmentCoordinator
from broodling.codex_profile import QualifiedCodexProfile
from broodling.replacement import RetryCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter

FROZEN_SOURCE_CANARY = "ADMITTED_SOURCE_ONLY_22"
FROZEN_SOURCE_BYTES = (
    b"Frozen source instruction ADMITTED_SOURCE_ONLY_22. Preserve exact bytes.\r\n"
    b"During implementation write frozen-source-visible.txt containing exactly "
    b"ADMITTED_SOURCE_ONLY_22 followed by a newline, and copy desired.json to "
    b"candidate.json. These actions satisfy this disposable fixture.\n"
)

SEMANTIC_CANARIES = (
    "A1_DIRECTIVE_ONLY_22",
    "A1_ACCEPTANCE_ONLY_22",
    "A1_FINDING_ONLY_22",
    "A1_CANDIDATE_GENERATION_ONLY_22",
)


@contextmanager
def abandoned_case(*args, **kwargs):
    """Seed abandoned-only strings in actual typed model outputs and evidence.

    The fixture copy changes only controlled response content. The product graph,
    runtime, evidence checker and original historical fixture remain unchanged.
    """
    with tempfile.TemporaryDirectory(prefix="b22-old-", dir="/dev/shm") as directory:
        old_leaf = Path(directory) / "old-controlled-codex"
        old_leaf.write_text(
            LEAF.read_text()
            .replace(
                "Correct candidate.json", "Correct candidate.json A1_DIRECTIVE_ONLY_22"
            )
            .replace(
                "The current candidate supplies positive",
                "A1_ACCEPTANCE_ONLY_22 The current candidate supplies positive",
            )
            .replace("RAW_REJECTED_FINDING_CANARY", "A1_FINDING_ONLY_22")
            .replace(
                "C1_FROM_IMPLEMENT", "C1_FROM_IMPLEMENT A1_CANDIDATE_GENERATION_ONLY_22"
            )
            .replace("C2_FROM_REPAIR", "C2_FROM_REPAIR A1_CANDIDATE_GENERATION_ONLY_22")
        )
        old_leaf.chmod(0o755)
        with (
            patch("final_assurance_support.LEAF", old_leaf),
            patch("support.ISSUE_BODY", FROZEN_SOURCE_BYTES),
            final_case(*args, **kwargs) as case,
        ):
            yield case


def replacement(case, name="replacement", *, actual_provider=None):
    root = case.run_root / name
    root.mkdir()
    state = root / "observations"
    state.mkdir()
    home, codex_home = root / "home", root / "codex-home"
    home.mkdir()
    codex_home.mkdir()
    (codex_home / "auth.json").write_text("{}")
    executable = root / "controlled-codex"
    executable.write_text(
        "#!/usr/bin/env python3\n"
        "import io,json,os,re,runpy,subprocess,sys\nfrom pathlib import Path\n"
        "if sys.argv[1:] == ['--version']:\n print('codex-cli 0.153.4');sys.exit(0)\n"
        f"state=Path({str(state)!r})\n"
        "prompt=sys.stdin.read()\n"
        "node=re.search(r'BROODLING_NODE=([a-z0-9_]+)',prompt).group(1)\n"
        "snapshot={'node':node,'prompt':prompt,'argv':sys.argv[1:],"
        "'candidateBefore':Path('candidate.json').read_text(),"
        "'files':sorted(str(p) for p in Path('.').rglob('*') if p.is_file()),"
        "'homeEntries':sorted(p.name for p in Path(os.environ['HOME']).iterdir()),"
        "'codexHomeEntries':sorted(p.name for p in Path(os.environ['CODEX_HOME']).iterdir())}\n"
        "with (state/'inputs.jsonl').open('a') as stream: stream.write(json.dumps(snapshot)+'\\n')\n"
        f"actual_provider={str(actual_provider) if actual_provider else None!r}\n"
        "if actual_provider and node=='implement':\n"
        " result=subprocess.run([actual_provider,*sys.argv[1:]],input=prompt,text=True,capture_output=True)\n"
        " (state/'actual.stdout.jsonl').write_text(result.stdout)\n"
        " (state/'actual.stderr.txt').write_text(result.stderr)\n"
        " sys.stdout.write(result.stdout);sys.stderr.write(result.stderr);sys.exit(result.returncode)\n"
        "sys.stdin=io.StringIO(prompt)\n"
        "os.environ['BROODLING_FINAL_TEST_STATE']=str(state)\n"
        "os.environ['BROODLING_FINAL_TEST_SCENARIO']='clean'\n"
        "os.environ['BROODLING_FINAL_TEST_MATERIALS']='False'\n"
        "os.environ['BROODLING_FINAL_TEST_PAUSE']=''\n"
        f"runpy.run_path({str(LEAF)!r},run_name='__main__')\n"
    )
    executable.chmod(0o755)
    adapter = ZeroshotSubmitter(
        case.run_root / "native",
        codex_profile=QualifiedCodexProfile(executable, home, codex_home),
    )
    coordinator = RetryCoordinator(case.store, case.fixture.provisioner(), adapter)
    return SimpleNamespace(
        root=root,
        state=state,
        adapter=adapter,
        coordinator=coordinator,
        profile_home=home,
        codex_home=codex_home,
    )


def inputs(replacement):
    path = replacement.state / "inputs.jsonl"
    return (
        [json.loads(line) for line in path.read_text().splitlines()]
        if path.exists()
        else []
    )


async def retire(case, reason="explicit replacement control"):
    administrator = AbandonmentCoordinator(case.store, case.adapter)
    await administrator.stop(case.attempt_id, reason)
    return administrator.retire(case.attempt_id)


async def wait_replacement(case, replacement, row, *, timeout=60):
    from zeroshot import Client, LocalTarget

    (replacement.state / "release").touch()
    path = case.store.worktree_assignment(row.attempt_id).path
    request = json.loads(row.request_json)
    async with Client(
        target=LocalTarget(path, state_dir=case.run_root / "native"),
        environment=request["target"]["environment"],
    ) as client:
        return await client.get_run(row.run_id).wait(wait_timeout=timeout)


def provider_input(event):
    """The actual public runtime input serialized at the provider boundary."""
    import re

    return json.loads(
        re.search(
            r"Input JSON:\n([^\n]+)\nRuntime-owned response contract:", event["prompt"]
        ).group(1)
    )
