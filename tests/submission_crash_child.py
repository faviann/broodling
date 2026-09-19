"""Hard process-death and concurrent replay witnesses; only public SDK submit."""

import json
import os
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from attempt_crash_child import wait_for_gate
from crash_child import crash_when
from submission_support import configured_adapter
from support import move_head

from broodling import BroodlingStore
from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL

store_path, attempt_id, state_dir, mode = sys.argv[1:5]
store = BroodlingStore.open(store_path)
gateway = mode == "gateway_after_accept"
adapter = configured_adapter(
    state_dir,
    Path(store_path).parent.parent,
    **(
        {
            "delivery_target_origin": "http://127.0.0.1:8123",
            "github_token": "CHILD_GH_CANARY",
            "gateway_base_url": V1_GATEWAY_BASE_URL,
            "gateway_api_key": "CHILD_GATEWAY_CANARY",
        }
        if gateway
        else {}
    ),
)
coordinator = SubmissionCoordinator(store, adapter)
if gateway:
    # A hard process-death witness at the public SDK boundary; this lane must
    # never connect to a gateway, DirectTarget server, provider, or GitHub.
    def gateway_client(*, target, environment):
        from zeroshot import DirectTarget

        assert isinstance(target, DirectTarget)
        assert environment["GATEWAY_BASE_URL"] == V1_GATEWAY_BASE_URL
        assert environment["GATEWAY_API_KEY"] == "CHILD_GATEWAY_CANARY"
        assert environment["GH_TOKEN"] == "CHILD_GH_CANARY"
        assert "OPENAI_API_KEY" not in environment
        client = MagicMock()
        client.submit = AsyncMock(return_value=SimpleNamespace(id="gateway-run"))
        client.__aenter__ = AsyncMock(return_value=client)
        client.__aexit__ = AsyncMock(return_value=None)
        return client

    patch("zeroshot.Client", side_effect=gateway_client).start()
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
    run_id = native(request)
    print(json.dumps({"publicRunId": run_id}), flush=True)
    if mode == "after_accept_mutation":
        move_head(Path(request["workspace"]), content="accepted run mutation\n")
        os._exit(97)
    if mode in {"after_accept", "gateway_after_accept"}:
        os._exit(97)
    return run_id


adapter.submit = dispatch
result = coordinator.submit(attempt_id)
print(json.dumps({"correlatedRunId": result.run_id}), flush=True)
if mode == "after_correlation":
    os._exit(97)
