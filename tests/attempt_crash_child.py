"""Child process that admits/provisions an Attempt and dies at a chosen point.

Run as ``python tests/attempt_crash_child.py <store> <root> <repo> <revision-id>
<crash-point> [<git-revision>] [<gate-file>]``. It uses ``os._exit`` so nothing
unwinds: no ``finally``, no ROLLBACK, no worktree cleanup. Whatever survives is
what SQLite and Git actually committed.

The first line of stdout is always the Attempt id the request derives, so the
parent can assert convergence even when the child died before committing it.
"""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from broodling import AttemptProvisioner, BroodlingStore  # noqa: E402
from broodling import git as product_git  # noqa: E402
from broodling.starting_state import (  # noqa: E402
    admitted_material_digest,
    resolve_starting_state,
)
from broodling.store import derive_attempt_id  # noqa: E402
from crash_child import crash_when  # noqa: E402

GATE_TIMEOUT_SECONDS = 30.0


def expected_attempt_id(store: BroodlingStore, revision_id: str, revision: str) -> str:
    contract = store.get_contract_revision(revision_id).contract
    starting_state = resolve_starting_state(sys.argv[3], revision)
    material = admitted_material_digest(
        (item.source_id, item.content_sha256) for item in contract.source_attribution
    )
    return derive_attempt_id(revision_id, starting_state, material)


def wait_for_gate(gate: str | None) -> None:
    """Hold until the parent opens the gate, so siblings really do race."""

    if not gate:
        return
    deadline = time.monotonic() + GATE_TIMEOUT_SECONDS
    while not Path(gate).exists():
        if time.monotonic() > deadline:
            raise SystemExit("gate never opened")
        time.sleep(0.005)


def die_after_worktree_add() -> None:
    original = product_git.add_worktree

    def killing(*arguments, **keywords):
        original(*arguments, **keywords)
        os._exit(97)

    product_git.add_worktree = killing


def main() -> int:
    store_path, root, repository, revision_id, crash_point = sys.argv[1:6]
    revision = sys.argv[6] if len(sys.argv) > 6 else "HEAD"
    gate = sys.argv[7] if len(sys.argv) > 7 else None

    store = BroodlingStore.open(store_path)
    print(expected_attempt_id(store, revision_id, revision), flush=True)
    provisioner = AttemptProvisioner(store, root)

    if crash_point == "mid_attempt_write":
        # The attempts row is inserted; die before its worktree claim commits.
        crash_when(store, "INSERT INTO worktree_assignments", before=True)
    if crash_point == "after_worktree_add":
        die_after_worktree_add()

    wait_for_gate(gate)
    attempt = provisioner.admit(revision_id, repository, revision)
    if crash_point == "after_attempt_commit":
        os._exit(97)

    provisioner.provision(attempt.attempt_id)
    if crash_point == "after_provision":
        os._exit(97)

    print(attempt.attempt_id, flush=True)
    store.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
