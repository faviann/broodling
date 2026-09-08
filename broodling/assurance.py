"""Minimal final custody from one normally observed, already-correlated run.

This module does not submit, recover a missed result, decide Work Unit disposition
or establish applicability from material bytes. The admitted graph and public
runtime occurrence order supply authority; declared material is retained only for
later justification.
"""

from __future__ import annotations

import json
from dataclasses import dataclass

from .assurance_graph import assurance_graph, assurance_runtime, initial_state
from .contract import validate_final_assurance_materials
from .errors import GitCommandError, SubmissionConflict, SubmissionNotReady
from .final_material import collect_final_material
from .store import BroodlingStore
from .submission import SubmissionCoordinator
from .zeroshot_sdk import ZeroshotSubmitter, canonical_request


@dataclass(frozen=True, slots=True)
class FinalAssuranceRecord:
    attempt_id: str
    zeroshot_run_id: str
    record_json: str

    @property
    def material(self) -> dict:
        """Read retained custody without consulting a workspace or runtime."""
        return json.loads(self.record_json)


class FinalAssuranceCoordinator:
    """Capture once from a live observation; reread already-retained custody."""

    def __init__(self, store: BroodlingStore, submitter: ZeroshotSubmitter):
        self.store = store
        self.submitter = submitter
        self.submission = SubmissionCoordinator(store, submitter)

    def record(self, attempt_id: str) -> FinalAssuranceRecord | None:
        row = self.store.connection.execute(
            "SELECT * FROM final_assurance WHERE attempt_id = ?", (attempt_id,)
        ).fetchone()
        return None if row is None else FinalAssuranceRecord(**dict(row))

    def _bound(self, attempt_id):
        attempt, assignment = self.submission._current(attempt_id)
        submitted = self.submission.record(attempt_id)
        if submitted is None or submitted.state != "correlated":
            raise SubmissionNotReady("final custody requires an already-correlated run")
        self.submission._target(submitted)
        self.submission._source(attempt, assignment, require_b1=False)
        self.submission._origin(submitted, assignment)
        revision = self.store.get_contract_revision(attempt.contract_revision_id)
        request = json.loads(submitted.request_json)
        required = {
            "submissionKey": f"broodling:v1:{attempt_id}",
            "title": "Broodling V1 assurance Attempt",
            "graph": assurance_graph(),
            "runtime": assurance_runtime(),
            "initialInput": initial_state(
                revision.canonical_bytes.decode(),
                attempt.b1_commit_oid,
                self.store.frozen_instructions(attempt_id),
            ),
            "workspace": assignment.worktree_path,
            "repository": attempt.b1_repository,
            "branch": assignment.branch,
            "startingCommit": attempt.b1_commit_oid,
            "materialSha256": attempt.b1_material_sha256,
        }
        if any(request.get(key) != value for key, value in required.items()):
            raise SubmissionConflict(
                "correlated request is not the admitted assurance protocol"
            )
        try:
            validate_final_assurance_materials(revision.contract)
        except ValueError as error:
            raise SubmissionNotReady(str(error)) from error
        return attempt, assignment, submitted, revision, request

    async def capture(self, attempt_id: str) -> FinalAssuranceRecord:
        return await self._capture(attempt_id, fresh=False)

    async def _capture_fresh(self, attempt_id: str) -> FinalAssuranceRecord:
        """One uninterrupted disposition owner may acquire only new custody."""
        return await self._capture(attempt_id, fresh=True)

    async def _capture(self, attempt_id: str, *, fresh: bool) -> FinalAssuranceRecord:
        with self.store._write():
            self.submission._current(attempt_id)
            existing = self.record(attempt_id)
            if existing is not None:
                if fresh:
                    raise SubmissionConflict(
                        "prior P3 custody cannot authorize finalization"
                    )
                return existing
            _, _, submitted, _, request = self._bound(attempt_id)
        # No cursor, partial custody or observation state is persisted. A lost
        # normal observation propagates; another call cannot scan a completed run.
        observed = await self.submitter.observe_current(request, submitted.run_id)
        with self.store._write() as connection:
            self.submission._current(attempt_id)
            existing = self.record(attempt_id)
            if existing is not None:
                if fresh:
                    raise SubmissionConflict(
                        "prior P3 custody cannot authorize finalization"
                    )
                return existing
            attempt, assignment, current, revision, current_request = self._bound(
                attempt_id
            )
            if (
                current != submitted
                or current_request != request
                or observed.run_id != current.run_id
            ):
                raise SubmissionConflict(
                    "final observation lost its immutable run binding"
                )
            _require_custody_output(observed.output, revision.contract)
            try:
                selected = collect_final_material(
                    revision.contract, assignment.path, attempt.b1_commit_oid
                )
            except (OSError, ValueError, GitCommandError) as error:
                raise SubmissionNotReady(
                    f"declared final material is unavailable: {error}"
                ) from error
            output = observed.output
            payload = {
                "format": "broodling.final-assurance/v1",
                "attemptId": attempt_id,
                "contractRevisionId": revision.contract_revision_id,
                "runId": observed.run_id,
                "finalOccurrence": {
                    "node": observed.final_node,
                    "execution": observed.final_execution_id,
                },
                "candidateGeneration": {
                    "mutationNode": observed.mutation_node,
                    "mutationExecution": observed.mutation_execution_id,
                },
                "selectedMaterial": selected,
                "evidenceContent": output["evidenceContent"],
                "finalRationale": output["finalRationale"],
                "assuranceContext": {
                    key: output[key]
                    for key in (
                        "comparisonBase",
                        "findings",
                        "findingContent",
                        "obligation",
                        "directiveContent",
                    )
                },
            }
            connection.execute(
                "INSERT INTO final_assurance (attempt_id, zeroshot_run_id, record_json) VALUES (?, ?, ?)",
                (attempt_id, current.run_id, canonical_request(payload)),
            )
            return self.record(attempt_id)


def _require_custody_output(output: dict, contract) -> None:
    """Check required retained material, never re-judge semantic sufficiency.

    Zeroshot has already validated and routed the typed final response. These
    narrow checks prevent missing criterion rationale or raw material from being
    persisted as complete custody; they do not grant a model output authority.
    Empty raw text is valid material. Required rationale must actually be present.
    """
    try:
        required = [criterion.criterion_id for criterion in contract.criteria]
        rationale = output["finalRationale"]
        if sorted(item["criterionId"] for item in rationale) != sorted(required) or any(
            not item["rationale"].strip() for item in rationale
        ):
            raise ValueError("criterion-level final rationale is missing")
        evidence = output["evidenceContent"]
        observations = evidence["observations"]
        if (
            evidence["error"]
            or output["evidence"] != "valid"
            or len(observations) != len(required)
        ):
            raise ValueError("required final evidence is missing")
        for criterion, observation in zip(contract.criteria, observations, strict=True):
            # Selection/labels come from the frozen deterministic leaf. Require
            # the complete corresponding raw payload, not a path or digest.
            if observation["criterionId"] != criterion.criterion_id:
                raise ValueError("required criterion observation is missing")
            for key in (
                "population",
                "argv",
                "cwd",
                "host",
                "mode",
                "exitCode",
                "stdout",
                "stderr",
            ):
                if key not in observation:
                    raise ValueError(
                        "required observation context/raw material is missing"
                    )
            materials = observation["materials"]
            if [item["path"] for item in materials] != list(
                criterion.mechanical_evidence.materials
            ) or any("content" not in item for item in materials):
                raise ValueError("required raw artifact material is missing")
        if output["obligation"] not in {"none", "resolved_d1"}:
            raise ValueError("final correction state is unresolved")
        for key in ("comparisonBase", "findings", "findingContent", "directiveContent"):
            if key not in output:
                raise ValueError("required final assessment context is missing")
    except (KeyError, TypeError, ValueError, AttributeError) as error:
        raise SubmissionNotReady(f"final custody is incomplete: {error}") from error
