"""Hard process-death injection around fresh retry setup; no provider calls."""

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from crash_child import crash_when

from broodling import AttemptProvisioner, BroodlingStore, RetryCoordinator, git
from broodling.codex_profile import QualifiedCodexProfile
from broodling.zeroshot_sdk import ZeroshotSubmitter

store_path, workspace_root, predecessor, config_path, mode = sys.argv[1:6]
config = json.loads(Path(config_path).read_text())
profile = config["codexProfile"]
store = BroodlingStore.open(store_path)
adapter = ZeroshotSubmitter(
    config["stateDir"],
    codex_profile=QualifiedCodexProfile(
        profile["realCodex"], profile["profileHome"], profile["isolatedCodexHome"]
    ),
)
coordinator = RetryCoordinator(
    store, AttemptProvisioner(store, workspace_root), adapter
)
if mode == "allocation-write":
    crash_when(store, "INSERT INTO worktree_assignments", before=True)
if mode == "prepare-write":
    crash_when(store, "INSERT INTO attempt_submissions", before=False)
if mode == "held-git":
    git.GIT = sys.argv[6]
if mode == "worktree-add":
    native = git.add_worktree

    def die_after_add(*args, **kwargs):
        native(*args, **kwargs)
        os._exit(97)

    git.add_worktree = die_after_add
attempt = coordinator.allocate(predecessor, "retry")
print(attempt.attempt_id, flush=True)
if mode == "allocated":
    os._exit(97)
prepared = coordinator.prepare(predecessor, "retry")
print(prepared.submission_key, flush=True)
if mode == "prepared":
    os._exit(97)
