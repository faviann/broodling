"""Lose a real product capture caller; the detached runtime is not owned by it."""

import asyncio
import json
import os
import sys
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from zeroshot import Run

from broodling import (
    AbandonmentCoordinator,
    BroodlingStore,
    FinalAssuranceCoordinator,
    QualifiedCodexProfile,
    ZeroshotSubmitter,
)

store_path, attempt_id, state_dir, action = sys.argv[1:5]
with BroodlingStore.open(store_path) as store:
    request = json.loads(
        store.connection.execute(
            "SELECT request_json FROM attempt_submissions WHERE attempt_id = ?",
            (attempt_id,),
        ).fetchone()[0]
    )
    profile = request["target"]["codexProfile"]
    adapter = ZeroshotSubmitter(
        request["target"]["stateDir"],
        codex_profile=QualifiedCodexProfile(
            profile["realCodex"],
            profile["profileHome"],
            profile["isolatedCodexHome"],
        ),
    )
    adapter.target = request["target"]
    if action in {"before-stop", "after-stop"}:
        original_stop = adapter.stop_known

        async def lose_at_stop(*args, **kwargs):
            if action == "before-stop":
                os._exit(77)
            await original_stop(*args, **kwargs)
            os._exit(78)

        adapter.stop_known = lose_at_stop
        asyncio.run(AbandonmentCoordinator(store, adapter).stop(attempt_id, action))
        raise AssertionError("fault injection did not exit")
    original = Run.status

    async def observe_then_lose(run, *args, **kwargs):
        status = await original(run, *args, **kwargs)
        if action == "after-directive":
            assert any(item.node == "repair" for item in status.active_executions)
            os._exit(75)
        (Path(state_dir) / "release").touch()
        return status

    with patch.object(Run, "status", observe_then_lose):
        asyncio.run(FinalAssuranceCoordinator(store, adapter).capture(attempt_id))
    os._exit(76)
