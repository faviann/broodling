"""Spike transport between a non-Python caller and the pinned Zeroshot SDK.

Disposable experiment for issue #130. One JSON request on stdin, one JSON
response on stdout, one process per call. The bridge holds no Broodling state,
makes no lifecycle decision, and never invents a request field: every value it
passes to the SDK comes from the caller's document unchanged.
"""

from __future__ import annotations

import asyncio
import importlib.metadata
import json
import sys

SUPPORTED_SDK_VERSION = "10.3.0.post1"


def _fail(error: str, message: str, **extra) -> dict:
    return {"ok": False, "error": error, "message": message, **extra}


def _result(value) -> dict:
    return {
        "ok": True,
        "result": {
            "runId": value.run_id,
            "succeeded": value.succeeded,
            "output": value.output,
            "failure": value.failure,
        },
    }


def _target(locator: dict, *, workspace: str | None):
    from zeroshot import DirectTarget, LocalTarget

    kind = locator["kind"]
    if kind == "local":
        return LocalTarget(workspace, state_dir=locator["stateDir"])
    if kind == "direct":
        return DirectTarget(locator["origin"], workspace=workspace)
    raise ValueError(f"unsupported target kind {kind!r}")


async def _submit(call: dict) -> dict:
    from zeroshot import Client, Preset, UniformRuntime
    from zeroshot.run_errors import SubmissionConflictError

    environment = dict(call["environment"])
    environment.update(call.get("secrets") or {})
    target = _target(call["target"], workspace=call["workspace"])
    source = call.get("source") or {}
    async with Client(target=target, environment=environment) as client:
        try:
            run = await client.submit(
                call["task"],
                title=call["title"],
                preset=Preset(**call["preset"]),
                runtime=UniformRuntime(**call["runtime"]),
                submission_key=call["submissionKey"],
                **source,
            )
        except SubmissionConflictError as error:
            return _fail(
                "submission_conflict",
                str(error),
                existingRunId=error.existing_run_id,
            )
        return {"ok": True, "runId": run.id}


async def _reconnected(call: dict):
    from zeroshot import Client

    return Client(target=_target(call["target"], workspace=None), environment={})


async def _wait(call: dict) -> dict:
    async with await _reconnected(call) as client:
        return _result(await client.get_run(call["runId"]).wait())


async def _stop(call: dict) -> dict:
    async with await _reconnected(call) as client:
        return _result(await client.get_run(call["runId"]).force_stop())


async def _dispatch(call: dict) -> dict:
    operation = call["op"]
    if operation == "version":
        return {
            "ok": True,
            "sdkVersion": importlib.metadata.version("the-open-engine-zeroshot"),
        }
    if operation == "submit":
        return await _submit(call)
    if operation == "wait":
        return await _wait(call)
    if operation == "stop":
        return await _stop(call)
    return _fail("unsupported_operation", f"unsupported operation {operation!r}")


def main() -> int:
    call = json.loads(sys.stdin.read())
    try:
        response = asyncio.run(_dispatch(call))
    except Exception as error:  # noqa: BLE001 - the transport reports every failure as data
        response = _fail(type(error).__name__, str(error))
    sys.stdout.write(json.dumps(response))
    sys.stdout.flush()
    return 0 if response.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main())
