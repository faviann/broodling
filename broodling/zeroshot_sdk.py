"""Qualified public submission and normal current-run observation boundary."""

from __future__ import annotations

import asyncio
import hashlib
import importlib.metadata
import json
import os
from contextlib import aclosing
from dataclasses import dataclass
from pathlib import Path

from .codex_profile import QualifiedCodexProfile
from .errors import SubmissionConflict, UnsupportedRuntime
from .profile import QUALIFIED_ZEROSHOT_BOUNDARY

QUALIFIED_SDK_SOURCE_SHA256 = (
    "0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e"
)


@dataclass(frozen=True, slots=True)
class CurrentRunObservation:
    """Ephemeral final result and its observed structural occurrence references."""

    run_id: str
    final_node: str
    final_execution_id: str
    mutation_node: str
    mutation_execution_id: str
    output: object


@dataclass(frozen=True, slots=True)
class TerminalRunObservation:
    """Runtime diagnostics only; neither writer cessation nor product success.

    In particular, ``runtime_lost`` can coexist with surviving provider children
    on the pinned local profile. No semantic output is recovered by stopping.
    """

    run_id: str
    runtime_succeeded: bool
    failure: str | None


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
    """Submit or observe one already-correlated current product run.

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

    async def stop_known(self, request: dict, run_id: str) -> TerminalRunObservation:
        """Stop one already-correlated run after durable abandonment.

        The caller owns the persisted request/run binding and must make the
        Attempt ineligible before entering this boundary. This operation cannot
        discover a missing run ID, dispatch work, or authorize retirement. SDK
        errors, including inaccessible runtime state, propagate without a fact.
        Repeating the call observes Zeroshot's existing terminal result.
        """
        if request["target"] != self.target:
            raise UnsupportedRuntime("stop target differs from persisted target")
        self._profile_identity()
        if not isinstance(run_id, str) or not run_id.strip():
            raise UnsupportedRuntime("stop requires an already-correlated run ID")
        assert_qualified_integration()

        from zeroshot import Client, LocalTarget

        async with Client(
            target=LocalTarget(
                request["workspace"], state_dir=request["target"]["stateDir"]
            ),
            environment=request["target"]["environment"],
        ) as client:
            run = client.get_run(run_id)
            result = await run.force_stop()
            status = await run.status()
        if (
            result.run_id != run_id
            or status.run_id != run_id
            or status.phase != "finished"
            or status.active_executions
            or status.result is None
            or status.result.run_id != run_id
            or status.result.succeeded != result.succeeded
            or status.result.failure != result.failure
        ):
            raise UnsupportedRuntime("stop lacks a consistent terminal observation")
        return TerminalRunObservation(run_id, result.succeeded, result.failure)

    async def observe_current(
        self, request: dict, run_id: str
    ) -> CurrentRunObservation:
        """Observe forward from current status, without history or reconnection.

        The caller establishes current Attempt ownership and that ``run_id`` is
        its already-correlated immutable request. This method checks the admitted
        product protocol, then retains only the latest mutation and final assessor
        references in memory. A missed occurrence or interrupted stream cannot be
        repaired here. Runtime response validation and routing remain in Zeroshot;
        custody completeness belongs to the caller.
        """
        from .assurance_graph import assurance_graph, assurance_runtime

        self.require_assurance_profile()
        self._profile_identity()
        if (
            request["target"] != self.target
            or request["graph"] != assurance_graph()
            or request["runtime"] != assurance_runtime()
        ):
            raise UnsupportedRuntime(
                "observation requires the admitted product protocol and target"
            )
        if not isinstance(run_id, str) or not run_id.strip():
            raise UnsupportedRuntime(
                "observation requires an already-correlated run ID"
            )
        assert_qualified_integration()

        from zeroshot import Client, LocalTarget

        final_nodes = {
            "final_assessment_authority_clean": "implement",
            "final_assessment_authority_repaired": "repair",
        }
        mutation = None
        final = None

        def observe(status):
            nonlocal mutation, final
            if status.run_id != run_id:
                raise UnsupportedRuntime("observed status belongs to another run")
            if status.phase == "stopping":
                raise UnsupportedRuntime("normal observation lost to a stopping run")
            selected = [
                item
                for item in status.active_executions
                if item.node in {"implement", "repair"} or item.node in final_nodes
            ]
            if (status.phase == "finished" or status.result is not None) and selected:
                raise UnsupportedRuntime(
                    "terminal status cannot establish live occurrence provenance"
                )
            if len(selected) > 1:
                raise UnsupportedRuntime("ambiguous mutation or final occurrence")
            for item in selected:
                if not item.execution:
                    raise UnsupportedRuntime(
                        "observed occurrence has no runtime identity"
                    )
                reference = (item.node, item.execution)
                if item.node in {"implement", "repair"}:
                    if final is not None:
                        raise UnsupportedRuntime("mutation followed the final assessor")
                    mutation = reference
                else:
                    if mutation is None or mutation[0] != final_nodes[item.node]:
                        raise UnsupportedRuntime(
                            "final structural mutation was not observed"
                        )
                    if final is not None and final != reference:
                        raise UnsupportedRuntime(
                            "ambiguous designated final occurrence"
                        )
                    final = reference
            if status.result is None:
                if status.phase == "finished":
                    raise UnsupportedRuntime("finished run has no public result")
                return None
            result = status.result
            if status.phase != "finished" or result.run_id != run_id:
                raise UnsupportedRuntime("terminal result lacks current-run provenance")
            if not result.succeeded or final is None or mutation is None:
                raise UnsupportedRuntime(
                    "normal successful final occurrence was not observed"
                )
            return CurrentRunObservation(
                run_id, final[0], final[1], mutation[0], mutation[1], result.output
            )

        async with Client(
            target=LocalTarget(
                request["workspace"], state_dir=request["target"]["stateDir"]
            ),
            environment=request["target"]["environment"],
        ) as client:
            run = client.get_run(run_id)
            current = await run.status()
            if (
                current.phase not in {"admitted", "running"}
                or current.result is not None
            ):
                raise UnsupportedRuntime(
                    "normal observation requires a current nonterminal run"
                )
            observe(current)
            if not current.cursor:
                raise UnsupportedRuntime(
                    "current status has no forward-observation cursor"
                )
            async with aclosing(run.watch(after=current.cursor)) as statuses:
                async for status in statuses:
                    completed = observe(status)
                    if completed is not None:
                        return completed
            raise UnsupportedRuntime("normal status stream ended before final capture")

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
