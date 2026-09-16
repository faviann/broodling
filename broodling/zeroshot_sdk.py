"""Submit one immutable invocation and consume Zeroshot's public result.

Graph admission, execution, observation/reconnection, sessions and cancellation
belong to the SDK and native engine. No execution history is reconstructed here.
"""

from __future__ import annotations

import asyncio
import importlib.metadata
import json
import os
from pathlib import Path

from .codex_profile import OPERATING_ENVIRONMENT, CodexProfile
from .errors import SubmissionConflict, UnsupportedRuntime
from .profile import ZEROSHOT_SDK_VERSION


def canonical_request(value: dict) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def assert_supported_integration() -> None:
    if importlib.metadata.version("the-open-engine-zeroshot") != ZEROSHOT_SDK_VERSION:
        raise UnsupportedRuntime(f"Zeroshot SDK {ZEROSHOT_SDK_VERSION} is required")
    if os.environ.get("ZEROSHOT_PYTHON_NATIVE_BINARY"):
        raise UnsupportedRuntime("use the release SDK's bundled native executable")


class ZeroshotSubmitter:
    def __init__(
        self,
        state_dir: Path | str,
        *,
        codex_profile: CodexProfile | None = None,
    ) -> None:
        self.codex_profile = codex_profile
        self._tool_path = os.environ.get("PATH", "")
        # Client inherits these operating variables even with an explicit
        # environment. Freeze/clear them so ambient homes/config cannot leak in.
        environment = dict(OPERATING_ENVIRONMENT)
        environment["PATH"] = self._tool_path
        self.target = {
            "stateDir": str(Path(state_dir).expanduser().resolve()),
            "environment": environment,
            "sdkVersion": ZEROSHOT_SDK_VERSION,
        }
        if codex_profile is not None:
            environment.update(codex_profile.environment(self._tool_path))
            self.target["codexProfile"] = codex_profile.identity()

    @property
    def runtime(self) -> dict:
        """The supported provider selection, not a caller-authored execution plan."""
        return {
            "harness": "codex",
            "provider": "openai",
            "model": "gpt-5.6-sol",
            "effort": "low",
            "size": "small",
            "session_scope": "execution",
            "connections": {
                "profile": [
                    "BROODLING_REAL_CODEX",
                    "BROODLING_PROFILE_HOME",
                    "BROODLING_ISOLATED_CODEX_HOME",
                ]
            },
        }

    def require_execution_profile(self) -> None:
        if self.codex_profile is None:
            raise UnsupportedRuntime("execution requires the no-effect Codex profile")

    def _validate_policy(self) -> None:
        self.require_execution_profile()
        if (
            self.codex_profile.identity() != self.target.get("codexProfile")
            or self.codex_profile.environment(self._tool_path)
            != self.target.get("environment")
            or self.target.get("sdkVersion") != ZEROSHOT_SDK_VERSION
        ):
            raise UnsupportedRuntime(
                "provider policy/environment differs from the configured no-effect profile"
            )
        state = Path(self.target["stateDir"])
        if not state.is_absolute() or state.resolve() != state:
            raise UnsupportedRuntime("native state directory must remain canonical")

    def validate_first_dispatch(self, workspace: Path) -> None:
        from . import git

        self._validate_policy()
        state = Path(self.target["stateDir"])
        protected = (workspace.resolve(), git.common_directory(workspace))
        if state.resolve() != state or any(
            state.is_relative_to(path) or path.is_relative_to(state)
            for path in protected
        ):
            raise UnsupportedRuntime(
                "native state directory must be canonical and separate from candidate/shared Git"
            )
        self.codex_profile.validate(workspace, path=self.target["environment"]["PATH"])

    def _client(self, request: dict):
        if request.get("target") != self.target:
            raise UnsupportedRuntime("runtime target differs from persisted request")
        self._validate_policy()
        if request.get("preset") != {"name": "software-change", "delivery": "none"}:
            raise UnsupportedRuntime(
                "only the no-delivery software-change workflow is supported"
            )
        if request.get("runtime") != self.runtime:
            raise UnsupportedRuntime(
                "workflow runtime differs from the supported no-effect provider"
            )
        assert_supported_integration()
        from zeroshot import Client, LocalTarget

        return Client(
            target=LocalTarget(
                request["workspace"], state_dir=request["target"]["stateDir"]
            ),
            environment=request["target"]["environment"],
        )

    def submit(self, request: dict) -> str:
        return asyncio.run(self._submit(request))

    async def _submit(self, request: dict) -> str:
        from zeroshot import Preset, UniformRuntime
        from zeroshot.run_errors import SubmissionConflictError

        try:
            async with self._client(request) as client:
                return (
                    await client.submit(
                        request["task"],
                        title=request["title"],
                        preset=Preset(**request["preset"]),
                        runtime=UniformRuntime(**request["runtime"]),
                        submission_key=request["submissionKey"],
                    )
                ).id
        except SubmissionConflictError as error:
            raise SubmissionConflict(
                str(error), existing_run_id=error.existing_run_id
            ) from error

    async def wait(self, request: dict, run_id: str):
        """Return the eventual result, including an already-completed run."""
        async with self._client(request) as client:
            return await client.get_run(run_id).wait()

    async def stop_known(self, request: dict, run_id: str):
        """Request terminalization; RunResult is not a physical cleanup receipt."""
        async with self._client(request) as client:
            return await client.get_run(run_id).force_stop()
