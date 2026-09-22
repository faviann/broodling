"""Controlled stand-in for the pinned SDK, used only where the real engine cannot reach.

The real engine has no offline path that produces a pull-request delivery receipt.
This stub fabricates one so the C# decoding of a full receipt is exercised end to end.
It reproduces the pinned SDK's public shapes and nothing else.
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path

from . import run_errors

__version__ = "10.3.0.post1"


@dataclass(frozen=True)
class LocalTarget:
    workspace: str | None = None
    state_dir: str | None = None


@dataclass(frozen=True)
class DirectTarget:
    origin: str
    workspace: str | None = None


@dataclass(frozen=True)
class Preset:
    name: str
    delivery: str = "none"


@dataclass(frozen=True)
class UniformRuntime:
    harness: str
    provider: str
    model: str
    effort: str | None = None
    size: str = "medium"
    session_scope: str = "execution"
    connections: dict | None = None


@dataclass(frozen=True)
class RunResult:
    run_id: str
    succeeded: bool
    output: object = None
    failure: str | None = None


class Run:
    def __init__(self, client: Client, run_id: str) -> None:
        self._client = client
        self.id = run_id

    async def wait(self) -> RunResult:
        return self._client._load(self.id)

    async def force_stop(self) -> RunResult:
        return RunResult(run_id=self.id, succeeded=False, failure="force_stopped")


class Client:
    def __init__(self, *, target=None, preset=None, runtime=None, environment=None) -> None:
        self.target = target
        self.environment = environment
        self._store = Path(getattr(target, "state_dir", None) or ".") / "stub-runs"

    async def __aenter__(self) -> Client:
        return self

    async def __aexit__(self, *_) -> None:
        return None

    async def submit(self, task, *, title, preset, runtime, submission_key, **source) -> Run:
        self._store.mkdir(parents=True, exist_ok=True)
        digest = hashlib.sha256(submission_key.encode()).hexdigest()[:16]
        record = self._store / f"{digest}.json"
        document = {
            "task": task,
            "title": title,
            "preset": [preset.name, preset.delivery],
            "source": source,
        }
        if record.exists():
            existing = json.loads(record.read_text())
            if existing["request"] != document:
                raise run_errors.SubmissionConflictError(
                    "submission key already identifies a different admitted run",
                    existing_run_id=existing["runId"],
                )
            return Run(self, existing["runId"])
        run_id = f"stub-{digest}"
        receipt = {
            "version": "v1",
            "mode": "pr",
            "outcome": "opened",
            "repository": source.get("repository"),
            "targetBranch": source.get("branch"),
            "headRevision": "b" * 40,
            "pullRequestId": "50",
        }
        record.write_text(json.dumps({
            "runId": run_id,
            "request": document,
            "result": {
                "run_id": run_id,
                "succeeded": True,
                "output": receipt if preset.delivery == "pull_request" else None,
                "failure": None,
            },
        }))
        return Run(self, run_id)

    def get_run(self, run_id: str) -> Run:
        return Run(self, run_id)

    def _load(self, run_id: str) -> RunResult:
        for record in self._store.glob("*.json"):
            document = json.loads(record.read_text())
            if document["runId"] == run_id:
                return RunResult(**document["result"])
        raise run_errors.RunNotFoundError(f"no run {run_id}")
