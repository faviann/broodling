#!/usr/bin/env python3
"""One racer: issue the admission request and report what this caller saw.

Every racer issues the *semantically identical* request — same admitted Contract
revision, same repository, same ``HEAD`` — so the whole population must converge
on one Attempt and one worktree. The last line of stdout is this caller's own
answer as JSON: which Attempt it was given, which worktree, and what that
worktree's ``HEAD`` was *at the moment it was handed over*.

That last field is the point. A caller that is handed a worktree still being
checked out has been given an inconsistent result even though the durable state
ends up correct, and only the caller can observe it.
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from broodling import AttemptProvisioner, BroodlingStore  # noqa: E402
from broodling import git as product_git  # noqa: E402

GATE_TIMEOUT_SECONDS = 60.0


def wait_for_gate(gate: Path) -> None:
    """Hold until the parent opens the gate, so the racers really do race."""

    deadline = time.monotonic() + GATE_TIMEOUT_SECONDS
    while not gate.exists():
        if time.monotonic() > deadline:
            raise SystemExit("gate never opened")
        time.sleep(0.0005)


def main() -> int:
    store_path, root, repository, revision_id, gate = sys.argv[1:6]
    store = BroodlingStore.open(store_path)
    provisioner = AttemptProvisioner(store, root)
    wait_for_gate(Path(gate))

    try:
        provisioned = provisioner.admit_and_provision(revision_id, repository)
    except Exception as error:
        answer = {
            "ok": False,
            "error": f"{type(error).__name__}: {error}",
            "attempt_id": None,
            "worktree_path": None,
            "observed_head": None,
        }
    else:
        answer = {
            "ok": True,
            "error": None,
            "attempt_id": provisioned.attempt.attempt_id,
            "worktree_path": str(provisioned.path),
            "branch": provisioned.branch,
            "provisioned": provisioned.assignment.provisioned,
            "observed_head": product_git.head_commit(provisioned.path),
            "observed_tracked_files": len(
                product_git.run(provisioned.path, "ls-files").splitlines()
            ),
        }
    store.close()
    print(json.dumps(answer), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
