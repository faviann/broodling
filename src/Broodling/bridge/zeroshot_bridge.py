"""One-call SDK transport. No Broodling policy, state, recovery or lifecycle."""

import asyncio
import importlib.metadata
import importlib.resources
import json
import subprocess
import sys


def target(value, workspace=None):
    from zeroshot import LocalTarget

    return LocalTarget(workspace, state_dir=value["address"])


async def call(value):
    if value["op"] == "version":
        binary = importlib.resources.files("zeroshot").joinpath("_bin", "zeroshot")
        version = subprocess.run([str(binary), "--version"], capture_output=True, check=True, text=True)
        return {"ok": True, "sdkVersion": importlib.metadata.version("the-open-engine-zeroshot"),
                "nativeVersion": version.stdout.strip()}

    from zeroshot import Client, Preset, UniformRuntime
    from zeroshot.run_errors import SubmissionConflictError

    if value["op"] == "submit":
        request = value["request"]
        async with Client(target=target(request["target"]["locator"], request["workspace"]),
                          environment=dict(request["target"]["environment"])) as client:
            try:
                run = await client.submit(request["task"], title=request["title"],
                    preset=Preset(**request["preset"]), runtime=UniformRuntime(**request["runtime"]),
                    submission_key=request["submissionKey"])
                return {"ok": True, "runId": run.id}
            except SubmissionConflictError as error:
                return {"ok": False, "error": "submission_conflict", "existingRunId": error.existing_run_id}

    # Cancelling inside the SDK stops its native command; killing this bridge would not.
    async with asyncio.timeout(value["timeout"]):
        async with Client(target=target(value["locator"]), environment={}) as client:
            run = client.get_run(value["runId"])
            if value["op"] == "status":
                status = await run.status()
                return {"ok": True, "result": {"runId": status.run_id, "phase": status.phase,
                        "activeNodes": [active.node for active in status.active_executions]}}
            result = await {"wait": run.wait, "stop": run.force_stop}[value["op"]]()
            return {"ok": True, "result": {"runId": result.run_id, "succeeded": result.succeeded,
                    "output": result.output, "failure": result.failure}}


def main():
    try:
        response = asyncio.run(call(json.load(sys.stdin)))
    except Exception as error:
        # Never return exception text: SDK/native diagnostics may contain dispatch secrets.
        response = {"ok": False, "error": type(error).__name__}
    print(json.dumps(response, allow_nan=False), flush=True)


if __name__ == "__main__":
    main()
