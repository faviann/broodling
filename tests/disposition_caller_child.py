"""Fresh-process #23 caller faults; all fault hooks remain test-only."""

import asyncio
import dataclasses
import fcntl
import json
import os
import signal
import sys
from contextlib import ExitStack, contextmanager
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from zeroshot import Run

from broodling import (
    BroodlingStore,
    QualifiedCodexProfile,
    ZeroshotSubmitter,
    assurance,
)
from broodling.disposition import WorkUnitDispositionCoordinator


def kill():
    os.kill(os.getpid(), signal.SIGKILL)


store_path, attempt_id, state_directory, action, output_path = sys.argv[1:]
state = Path(state_directory)
with BroodlingStore.open(store_path) as store, ExitStack() as patches:
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
    coordinator = WorkUnitDispositionCoordinator(store, adapter)
    original_flock = fcntl.flock

    def witnessed_flock(*args):
        try:
            return original_flock(*args)
        except BlockingIOError:
            (state / "waiting-lock").touch()
            raise

    patches.enter_context(patch.object(fcntl, "flock", witnessed_flock))
    original_status = Run.status

    async def release_status(run, *args, **kwargs):
        status = await original_status(run, *args, **kwargs)
        (state / "status-observed").touch()
        if action == "duplicate":
            while not (state / "allow-observation").exists():
                await asyncio.sleep(0.01)
        (state / "release").touch()
        return status

    patches.enter_context(patch.object(Run, "status", release_status))
    original_observe = adapter.observe_current

    async def counted_observe(*args, **kwargs):
        with (state / "observers.jsonl").open("a") as stream:
            stream.write(json.dumps({"pid": os.getpid()}) + "\n")
        result = await original_observe(*args, **kwargs)
        if action == "after-output":
            kill()
        return result

    adapter.observe_current = counted_observe
    if action == "during-custody":
        patches.enter_context(
            patch.object(
                assurance, "collect_final_material", side_effect=lambda *a, **k: kill()
            )
        )
    if action == "after-custody":
        patches.enter_context(
            patch.object(coordinator, "_commit", side_effect=lambda *a, **k: kill())
        )
    if action == "waiting-owner":

        def hold_completed_custody(*args, **kwargs):
            (state / "custody-ready").touch()
            signal.pause()
            raise AssertionError("paused owner unexpectedly resumed")

        patches.enter_context(
            patch.object(coordinator, "_commit", hold_completed_custody)
        )
    if action in {"before-commit", "after-commit"}:
        original_write = store._write

        @contextmanager
        def interrupted_write():
            before = coordinator.record(attempt_id)
            with original_write() as connection:
                yield connection
                inserted = before is None and coordinator.record(attempt_id) is not None
                if inserted and action == "before-commit":
                    kill()
            if inserted and action == "after-commit":
                kill()

        store._write = interrupted_write
    (state / ("started-" + Path(output_path).name)).touch()
    result = asyncio.run(coordinator.finalize(attempt_id))
    Path(output_path).write_text(json.dumps(dataclasses.asdict(result), sort_keys=True))
