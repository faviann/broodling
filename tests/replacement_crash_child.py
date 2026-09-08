"""Hard caller-death windows for explicit replacement, using actual public SDK."""

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from crash_child import crash_when

from broodling import BroodlingStore
from broodling import git as product_git
from broodling.codex_profile import QualifiedCodexProfile
from broodling.provisioning import AttemptProvisioner
from broodling.replacement import RetryCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter

config = json.loads(Path(sys.argv[1]).read_text())
mode = sys.argv[2]
store = BroodlingStore.open(config["store"])
adapter = ZeroshotSubmitter(
    config["stateDir"],
    codex_profile=QualifiedCodexProfile(
        config["executable"], config["home"], config["codexHome"]
    ),
)
coordinator = RetryCoordinator(
    store, AttemptProvisioner(store, config["workspaceRoot"]), adapter
)
args = (config["predecessor"], config["retryId"])
if mode == "after-allocation":
    attempt = coordinator.allocate(*args)
    print(json.dumps({"allocatedAttempt": attempt.attempt_id}), flush=True)
    os._exit(97)
if mode == "after-worktree-add":
    original_add = product_git.add_worktree

    def die_after_add(*arguments, **keywords):
        original_add(*arguments, **keywords)
        os._exit(97)

    product_git.add_worktree = die_after_add
if mode == "during-key-persistence":
    crash_when(store, "INSERT INTO attempt_submissions", before=False)
if mode == "after-key-persistence":
    coordinator.prepare(*args)
    os._exit(97)
if mode == "after-acceptance":
    native = adapter.submit

    def die_after_acceptance(request):
        run_id = native(request)
        print(json.dumps({"publicAcceptedRun": run_id}), flush=True)
        os._exit(97)

    adapter.submit = die_after_acceptance
coordinator.retry(*args)
raise AssertionError("requested crash window was not reached")
