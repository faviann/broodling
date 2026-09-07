"""Qualified public submit boundary. No runtime observation or storage access."""

from __future__ import annotations

import asyncio
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path

from .codex_profile import QualifiedCodexProfile
from .errors import SubmissionConflict, UnsupportedRuntime
from .profile import QUALIFIED_ZEROSHOT_BOUNDARY

QUALIFIED_SDK_SOURCE_SHA256 = (
    "0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e"
)


def canonical_request(value: dict) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def assert_no_effect_runtime(runtime: dict) -> None:
    for binding in runtime.get("nodes", {}).values():
        if binding.get("kind") == "git_delivery":
            raise UnsupportedRuntime("V1 does not admit GitDelivery")


def installed_integration() -> dict[str, str]:
    # Package resource resolution only; never controller state or RunLedger.
    import zeroshot
    from zeroshot._binary import resolve_binary

    binary = Path(resolve_binary())
    root = Path(zeroshot.__file__).parent
    source = hashlib.sha256()
    for path in sorted(root.rglob("*.py")):
        source.update(path.relative_to(root).as_posix().encode() + b"\0")
        source.update(path.read_bytes() + b"\0")
    return {
        "sdkVersion": importlib.metadata.version("zeroshot-rust"),
        "sidecarSha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
        "sdkSourceSha256": source.hexdigest(),
    }


def assert_qualified_integration() -> dict[str, str]:
    installed = installed_integration()
    expected = QUALIFIED_ZEROSHOT_BOUNDARY
    if (
        installed["sdkVersion"] != "0.1.0.dev0"
        or installed["sdkSourceSha256"] != QUALIFIED_SDK_SOURCE_SHA256
        or installed["sidecarSha256"] != expected["sidecarSha256"]
    ):
        raise UnsupportedRuntime("SDK/sidecar differs from the G1-V1 qualified build")
    return installed


class ZeroshotSubmitter:
    """Only submit; return an ID or preserve the public conflict's existing ID.

    The target and complete environment are persisted as request identity, so a
    restart cannot silently switch runtime stores or bootstrap configuration.
    No credentials are inherited. Caller-supplied graphs remain opaque; their
    no-effect execution/containment profile remains the caller's responsibility.
    """

    def __init__(
        self,
        state_dir: Path | str,
        *,
        codex_profile: QualifiedCodexProfile | None = None,
    ) -> None:
        self.codex_profile = codex_profile
        self.target = {
            "stateDir": str(Path(state_dir).expanduser().resolve()),
            "environment": {"PATH": os.environ.get("PATH", "")},
        }
        if codex_profile is not None:
            self.target["environment"] = codex_profile.environment(
                os.environ.get("PATH", "")
            )
            self.target["codexProfile"] = codex_profile.identity()

    def require_assurance_profile(self) -> None:
        if self.codex_profile is None:
            raise UnsupportedRuntime(
                "product assurance requires the qualified Codex profile"
            )

    def _profile_identity(self) -> None:
        if (
            self.codex_profile is not None
            and self.codex_profile.identity() != self.target["codexProfile"]
        ):
            raise UnsupportedRuntime(
                "qualified launcher changed after target configuration"
            )

    def validate_first_dispatch(self, workspace: Path) -> None:
        if self.codex_profile is not None:
            self._profile_identity()
            self.codex_profile.validate(
                workspace, path=self.target["environment"]["PATH"]
            )

    def submit(self, request: dict) -> str:
        assert_no_effect_runtime(request["runtime"])
        if self.codex_profile is None:
            if set(request["target"]["environment"]) != {"PATH"}:
                raise UnsupportedRuntime("P2 passes only PATH to the sidecar")
        elif request["target"] != self.target:
            raise UnsupportedRuntime(
                "qualified provider profile differs from persisted target"
            )
        self._profile_identity()
        assert_qualified_integration()
        return asyncio.run(self._submit(request))

    async def _submit(self, request: dict) -> str:
        from zeroshot import Client, GraphSpec, LocalTarget, RunRequest, RuntimePlan
        from zeroshot.run_errors import SubmissionConflictError

        native = RunRequest(
            title=request["title"],
            graph=GraphSpec.from_dict(request["graph"]),
            runtime=RuntimePlan.from_dict(request["runtime"]),
            initial_input=request["initialInput"],
            submission_key=request["submissionKey"],
        )
        try:
            async with Client(
                target=LocalTarget(
                    request["workspace"], state_dir=request["target"]["stateDir"]
                ),
                environment=request["target"]["environment"],
            ) as client:
                return (await client.submit(native)).id
        except SubmissionConflictError as error:
            raise SubmissionConflict(
                str(error), existing_run_id=error.existing_run_id
            ) from error
