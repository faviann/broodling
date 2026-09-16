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
        delivery_target_origin: str | None = None,
        github_token: str | None = None,
    ) -> None:
        self.codex_profile = codex_profile
        self.github_token = github_token
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
        if delivery_target_origin is not None:
            self.target["deliveryTargetOrigin"] = delivery_target_origin
            self.target["deliveryCredential"] = "GH_TOKEN"

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

    def runtime_for(self, delivery: str) -> dict:
        if delivery == "none":
            return self.runtime
        if delivery == "pull_request":
            # The named target owns its provider installation and authentication.
            # GH_TOKEN is template-owned delivery authority, never an agent binding.
            return {
                "harness": "codex",
                "provider": "openai",
                "model": "gpt-5.6-sol",
                "effort": "low",
                "size": "small",
                "session_scope": "execution",
            }
        raise UnsupportedRuntime(f"unsupported delivery mode {delivery!r}")

    def require_execution_profile(self) -> None:
        if self.codex_profile is None:
            raise UnsupportedRuntime("execution requires the no-effect Codex profile")

    def _validate_policy(self, delivery: str = "none") -> None:
        if self.target.get("sdkVersion") != ZEROSHOT_SDK_VERSION:
            raise UnsupportedRuntime(
                "provider policy/environment differs from the configured profile"
            )
        if delivery == "none":
            self.require_execution_profile()
            if self.codex_profile.identity() != self.target.get(
                "codexProfile"
            ) or self.codex_profile.environment(self._tool_path) != self.target.get(
                "environment"
            ):
                raise UnsupportedRuntime(
                    "provider policy/environment differs from the configured no-effect profile"
                )
        elif delivery == "pull_request":
            if not str(self.target.get("deliveryTargetOrigin", "")).strip():
                raise UnsupportedRuntime(
                    "pull-request delivery requires a configured Zeroshot direct target"
                )
        else:
            raise UnsupportedRuntime(f"unsupported delivery mode {delivery!r}")
        state = Path(self.target["stateDir"])
        if not state.is_absolute() or state.resolve() != state:
            raise UnsupportedRuntime("native state directory must remain canonical")

    def validate_dispatch(self, workspace: Path, request: dict | None = None) -> None:
        from . import git

        if request is None:
            request = {"preset": {"delivery": "none"}}
        delivery = request["preset"]["delivery"]
        self._validate_policy(delivery)
        state = Path(self.target["stateDir"])
        protected = (workspace.resolve(), git.common_directory(workspace))
        if state.resolve() != state or any(
            state.is_relative_to(path) or path.is_relative_to(state)
            for path in protected
        ):
            raise UnsupportedRuntime(
                "native state directory must be canonical and separate from candidate/shared Git"
            )
        if delivery == "none":
            self.codex_profile.validate(
                workspace, path=self.target["environment"]["PATH"]
            )
            return
        token = self.github_token
        if not isinstance(token, str) or not token.strip() or len(token) > 4096:
            raise UnsupportedRuntime(
                "pull-request delivery requires a current nonempty GH_TOKEN"
            )
        selected = request.get("delivery")
        if not isinstance(selected, dict):
            raise UnsupportedRuntime("pull-request delivery identity is missing")
        if not git.valid_branch_name(workspace, selected.get("targetBranch", "")):
            raise UnsupportedRuntime("authorized pull-request target branch is invalid")
        if git.github_repository(git.origin_url(workspace)) != selected.get(
            "repository"
        ):
            raise UnsupportedRuntime(
                "Attempt source does not match the authorized pull-request repository"
            )

    def _client(self, request: dict):
        if request.get("target") != self.target:
            raise UnsupportedRuntime("runtime target differs from persisted request")
        preset = request.get("preset")
        if not isinstance(preset, dict):
            raise UnsupportedRuntime("workflow preset is missing")
        delivery = preset.get("delivery")
        self._validate_policy(delivery)
        if preset.get("name") != "software-change" or delivery not in {
            "none",
            "pull_request",
        }:
            raise UnsupportedRuntime(
                "only supported software-change delivery modes may run"
            )
        if request.get("runtime") != self.runtime_for(delivery):
            raise UnsupportedRuntime(
                "workflow runtime differs from the supported delivery profile"
            )
        assert_supported_integration()
        from zeroshot import Client, DirectTarget, LocalTarget

        environment = dict(request["target"]["environment"])
        if delivery == "pull_request" and self.github_token is not None:
            environment["GH_TOKEN"] = self.github_token
        target = (
            LocalTarget(request["workspace"], state_dir=request["target"]["stateDir"])
            if delivery == "none"
            else DirectTarget(
                request["target"]["deliveryTargetOrigin"],
                workspace=request["workspace"],
            )
        )
        return Client(
            target=target,
            environment=environment,
        )

    def submit(self, request: dict) -> str:
        return asyncio.run(self._submit(request))

    async def _submit(self, request: dict) -> str:
        from zeroshot import Preset, UniformRuntime
        from zeroshot.run_errors import SubmissionConflictError

        try:
            async with self._client(request) as client:
                delivery = request["preset"]["delivery"]
                source = request.get("delivery") if delivery == "pull_request" else {}
                options = {}
                if delivery == "pull_request":
                    options = {
                        "repository": source["repository"],
                        "branch": source["targetBranch"],
                        "revision": source["baseRevision"],
                    }
                return (
                    await client.submit(
                        request["task"],
                        title=request["title"],
                        preset=Preset(**request["preset"]),
                        runtime=UniformRuntime(**request["runtime"]),
                        submission_key=request["submissionKey"],
                        **options,
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
