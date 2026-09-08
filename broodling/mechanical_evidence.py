"""Deterministic text evidence for the two admitted V1 assurance occurrences.

Zeroshot owns invocation, timeout and graph routing. This leaf only observes the
explicit frozen checks, inside the qualified read-only/no-network namespace.
"""

from __future__ import annotations

import json
import os
import platform
import subprocess
import sys
import tempfile
from pathlib import Path

BWRAP = Path("/usr/bin/bwrap")
EVIDENCE_FLAG = "BROODLING_EVIDENCE_LEAF"
EVIDENCE_PROFILE = "deterministic-v1"
MODE = "read-only/no-network"


def _relative_path(value: object, *, directory: bool = False) -> str:
    if not isinstance(value, str) or not value or "\0" in value:
        raise ValueError("evidence path must be a nonempty string")
    path = Path(value)
    if value == "." and directory:
        return value
    if (
        path.is_absolute()
        or ".." in path.parts
        or path.as_posix() != value
        or value == "."
    ):
        raise ValueError("evidence path must be normalized and worktree-relative")
    return value


def _contained(workspace: Path, value: str, *, directory: bool = False) -> Path:
    path = (workspace / _relative_path(value, directory=directory)).resolve(strict=True)
    if not path.is_relative_to(workspace):
        raise ValueError("evidence path escapes the worktree")
    if (directory and not path.is_dir()) or (not directory and not path.is_file()):
        raise ValueError("required evidence path has the wrong file type")
    return path


def _observe(contract: dict, workspace: Path) -> list[dict]:
    """Run only admitted argv; an observed failing exit remains available evidence."""
    observations = []
    criteria = contract["criteria"]
    if not isinstance(criteria, list) or not criteria:
        raise ValueError("runnable criteria are required")
    for criterion in criteria:
        declaration = criterion["mechanicalEvidence"]
        if set(declaration) != {"argv", "cwd", "materials"}:
            raise ValueError("unsupported mechanical evidence declaration")
        argv = declaration["argv"]
        if (
            not isinstance(argv, list)
            or not argv
            or any(not isinstance(arg, str) or "\0" in arg for arg in argv)
            or not Path(argv[0]).is_absolute()
        ):
            raise ValueError("evidence argv must name an explicit absolute executable")
        cwd = _contained(workspace, declaration["cwd"], directory=True)
        materials = declaration["materials"]
        if not isinstance(materials, list):
            raise TypeError("evidence materials must be an explicit array")
        # Resolve before execution as well as collection: all required material
        # must belong to this candidate, never an ambient sibling or host file.
        for material in materials:
            _contained(workspace, material)
        completed = _sandbox(argv, workspace, cwd)
        observations.append(
            {
                "criterionId": criterion["criterionId"],
                "population": json.dumps(
                    criterion["evidencePopulation"],
                    sort_keys=True,
                    separators=(",", ":"),
                ),
                "argv": argv,
                "cwd": declaration["cwd"],
                "host": json.dumps(
                    dict(
                        zip(
                            (
                                "system",
                                "node",
                                "release",
                                "version",
                                "machine",
                                "processor",
                            ),
                            platform.uname(),
                            strict=True,
                        )
                    ),
                    sort_keys=True,
                ),
                "mode": MODE,
                "exitCode": completed.returncode,
                "stdout": completed.stdout.decode("utf-8", errors="strict"),
                "stderr": completed.stderr.decode("utf-8", errors="strict"),
                "materials": [
                    {
                        "path": material,
                        "content": _contained(workspace, material)
                        .read_bytes()
                        .decode("utf-8", errors="strict"),
                    }
                    for material in materials
                ],
            }
        )
    return observations


def _input(prompt: str) -> dict:
    # The pinned harness serializes one JSON value after this fixed transport
    # marker. Decode that value; delimiters inside Contract text are data.
    prefix = (
        "Execute this graph node using the shared workspace.\nAuthored instructions:\n"
    )
    if not prompt.startswith(prefix):
        raise ValueError("unrecognized pinned evidence input transport")
    marker = "\nInput JSON:\n"
    before, separator, body = prompt.partition(marker)
    if not separator or not before.startswith(prefix):
        raise ValueError("missing evidence graph input")
    value, end = json.JSONDecoder().raw_decode(body)
    if not body[end:].startswith("\nRuntime-owned response contract:\n"):
        raise ValueError("invalid evidence graph input boundary")
    if not isinstance(value, dict) or not isinstance(value.get("contract"), str):
        raise TypeError("frozen Contract graph input is required")
    return json.loads(value["contract"])


def _sandbox(
    argv: list[str], workspace: Path, cwd: Path
) -> subprocess.CompletedProcess:
    command = [
        str(BWRAP),
        "--ro-bind",
        "/",
        "/",
        "--dev",
        "/dev",
        "--proc",
        "/proc",
        "--unshare-pid",
        "--unshare-net",
        "--die-with-parent",
        "--new-session",
        "--tmpfs",
        "/tmp",
        "--dir",
        "/tmp/.broodling-evidence-home",
        "--ro-bind",
        str(workspace),
        str(workspace),
        "--clearenv",
        "--setenv",
        "PATH",
        "/usr/local/bin:/usr/bin:/bin",
        "--setenv",
        "HOME",
        "/tmp/.broodling-evidence-home",
        "--setenv",
        "CODEX_HOME",
        "/tmp/.broodling-evidence-home",
        "--setenv",
        "LANG",
        "C.UTF-8",
        "--setenv",
        "PYTHONDONTWRITEBYTECODE",
        "1",
        "--chdir",
        str(cwd),
    ]
    # The status descriptor belongs to bwrap's outer process. It is closed
    # before the candidate check starts and the sandbox's /proc cannot name
    # the outer collector. Thus check output cannot forge collection metadata.
    with tempfile.TemporaryFile() as status:
        completed = subprocess.run(
            [*command, "--json-status-fd", str(status.fileno()), "--", *argv],
            stdin=subprocess.DEVNULL,
            capture_output=True,
            check=False,
            pass_fds=(status.fileno(),),
        )
        status.seek(0)
        records = [json.loads(line) for line in status if line.strip()]
    exits = [record["exit-code"] for record in records if "exit-code" in record]
    # bwrap encodes signals as 128+signal and cannot distinguish an intentional
    # exit in that range. V1 conservatively marks that entire range unavailable;
    # ordinary 1..127 remain observations for semantic review.
    if (
        len(exits) != 1
        or exits[0] != completed.returncode
        or not 0 <= completed.returncode < 128
    ):
        raise ValueError("evidence containment or declared check could not complete")
    return completed


def _result(observations: list[dict], error: str = "") -> dict:
    return {"observations": observations, "error": error}


def main(arguments: list[str] | None = None) -> int:
    arguments = sys.argv[1:] if arguments is None else arguments
    try:
        if (
            os.environ.get(EVIDENCE_FLAG) != EVIDENCE_PROFILE
            or "--sandbox" not in arguments
            or arguments[arguments.index("--sandbox") + 1] != "read-only"
            or not arguments
            or arguments[0] != "exec"
        ):
            raise ValueError(
                "evidence requires the admitted read-only agent occurrence"
            )
        payload = _result(_observe(_input(sys.stdin.read()), Path.cwd().resolve()))
    except (OSError, ValueError, KeyError, TypeError, IndexError) as error:
        payload = _result([], f"{type(error).__name__}: {error}")
    response = {
        "output": {"evidenceContent": payload},
        "signals": {"availability": "missing" if payload["error"] else "valid"},
        "diagnostic": {},
    }
    print(
        json.dumps(
            {
                "type": "item.completed",
                "item": {
                    "type": "agent_message",
                    "text": json.dumps({"response": response}),
                },
            }
        ),
        flush=True,
    )
    print(
        json.dumps(
            {"type": "turn.completed", "usage": {"input_tokens": 0, "output_tokens": 0}}
        ),
        flush=True,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
