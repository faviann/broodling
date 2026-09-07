#!/usr/bin/env python3
"""Read-only W3 boundary probes for issue #17; this is not product code.

Execute the exact committed W3 graph through the qualified public SDK. Derive a
controlled leaf from the W3 fixture, changing only its generation/resolution
responses for the repeated-generation case. No product protocol is modified.
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import importlib.util
import json
import platform
import tempfile
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
EXPECTED_GRAPH_SHA256 = "f3ffcfced5bab598bc818db65ed985637afa0696a4ff551ee96ed5807788cd2b"


def reference():
    spec = importlib.util.spec_from_file_location(
        "w3_qualification_reference", ROOT / "qualification/v1-p1/issue9_qualify.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def environment(w3) -> dict:
    import zeroshot

    source = hashlib.sha256()
    package = Path(zeroshot.__file__).parent
    for path in sorted(package.rglob("*.py")):
        source.update(path.relative_to(package).as_posix().encode() + b"\0")
        source.update(path.read_bytes() + b"\0")
    build = {
        "qualifiedZeroshotRevision": w3.PINNED_ZEROSHOT_REVISION,
        "sdkVersion": w3.importlib.metadata.version("zeroshot-rust"),
        "sdkSourceSha256": source.hexdigest(),
        "sidecarSha256": w3.sha256(Path(w3.resolve_binary())),
        "python": platform.python_version(),
        "platform": platform.platform(),
        "referenceScriptSha256": w3.sha256(ROOT / "qualification/v1-p1/issue9_qualify.py"),
        "referenceLeafSha256": w3.sha256(ROOT / "qualification/v1-p1/w3-bin/codex"),
    }
    assert build["sdkVersion"] == "0.1.0.dev0"
    assert build["sdkSourceSha256"] == "0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e"
    assert build["sidecarSha256"] == "9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86"
    return build


async def execute(output: Path) -> None:
    w3 = reference()
    build = environment(w3)
    graph = w3.graph()
    assert w3.json_sha256(graph) == EXPECTED_GRAPH_SHA256
    run_root = Path(tempfile.mkdtemp(prefix="b17p-", dir="/dev/shm"))
    workspace_root = Path(tempfile.mkdtemp(prefix="b17p-", dir=Path.home() / ".cache"))
    args = SimpleNamespace(run_root=run_root, workspace_root=workspace_root, timeout=60)
    source = (ROOT / "qualification/v1-p1/w3-bin/codex").read_text()
    patches = [
        ('f"c{min(invocation + 1, 4)}"', '"c2"'),
        (
            'base in {"sticky-exhaust", "sticky-omission"} else "resolved_d1"',
            '(base in {"sticky-exhaust", "sticky-omission"} and invocation < 3) else "resolved_d1"',
        ),
    ]
    repeated = source
    for old, new in patches:
        assert repeated.count(old) == 1
        repeated = repeated.replace(old, new)
    bin_dir = run_root / "repeat-bin"
    bin_dir.mkdir()
    leaf = bin_dir / "codex"
    leaf.write_text(repeated)
    leaf.chmod(0o755)
    cases = {}
    for name, scenario, changed_leaf in (
        ("clean_control", "clean", False),
        ("repeated_c2", "sticky-exhaust", True),
        ("round_complete_crash", "repair-resolve;crash:round_complete", False),
        ("round_complete_missing", "repair-resolve;missing:round_complete", False),
    ):
        w3.LEAF_BIN = bin_dir if changed_leaf else ROOT / "qualification/v1-p1/w3-bin"
        cases[name] = await w3.run_case(args, graph, w3.runtime(graph), name, scenario)
    record = {
        "schema": "broodling.v1-p3.issue17-preflight/v1",
        "recordedAt": datetime.now(UTC).isoformat(),
        "purpose": "Discriminating observations of the unmodified qualified graph; no qualification or product PASS verdict",
        "scope": {
            "productImplementation": False,
            "graphMechanicsOnly": True,
            "freshContainmentEvidence": False,
            "realModelJudgmentEvidence": False,
            "runtimeAccess": "public SDK submit, wait and status only",
            "controlledLeafLimit": "Controlled executable records sandbox arguments but does not itself enforce host containment. W2 remains the containment evidence.",
        },
        "graphSha256": w3.json_sha256(graph),
        "build": build,
        "repeatedGenerationLeafPatches": [
            {"old": old, "new": new} for old, new in patches
        ],
        "cases": cases,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(record, indent=2, sort_keys=True) + "\n")
    print(json.dumps({name: case["result"] for name, case in cases.items()}, indent=2))
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output", type=Path,
        default=ROOT / "qualification/v1-p3/evidence/issue-17-preflight.json",
    )
    asyncio.run(execute(parser.parse_args().output))
