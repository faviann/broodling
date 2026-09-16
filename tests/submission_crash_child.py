"""Hard process-death and concurrent replay witnesses; only public SDK submit."""

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from attempt_crash_child import wait_for_gate
from crash_child import crash_when
from submission_support import configured_adapter
from support import move_head

from broodling import BroodlingStore
from broodling.submission import SubmissionCoordinator

store_path, attempt_id, state_dir, mode = sys.argv[1:5]
store = BroodlingStore.open(store_path)
adapter = configured_adapter(state_dir, Path(store_path).parent.parent)
coordinator = SubmissionCoordinator(store, adapter)
if len(sys.argv) > 5:
    wait_for_gate(sys.argv[5])

if mode == "during_prepare":
    crash_when(store, "INSERT INTO attempt_submissions", before=False)
if mode == "during_correlation":
    crash_when(store, "SET state = 'correlated'", before=False)
if mode == "prepared":
    coordinator.prepare(attempt_id)
    os._exit(97)

native = adapter.submit


def dispatch(request):
    if mode == "before_call":
        os._exit(97)
    try:
        run_id = native(request)
    except Exception as error:
        # The conflict itself is also a caller-visible acknowledgement window.
        if mode == "after_conflict" and getattr(error, "existing_run_id", ""):
            print(json.dumps({"publicRunId": error.existing_run_id}), flush=True)
            os._exit(97)
        raise
    print(json.dumps({"publicRunId": run_id}), flush=True)
    if mode == "after_accept_mutation":
        move_head(Path(request["workspace"]), content="accepted run mutation\n")
        os._exit(97)
    if mode == "after_accept":
        os._exit(97)
    return run_id


adapter.submit = dispatch
result = coordinator.submit(attempt_id)
print(json.dumps({"correlatedRunId": result.run_id}), flush=True)
if mode == "after_correlation":
    os._exit(97)
