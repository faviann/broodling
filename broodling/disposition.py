"""Commit Broodling's no-effect lifecycle decision from the admitted run result."""

from __future__ import annotations

import json
from dataclasses import dataclass

from .errors import GitCommandError, SubmissionConflict, SubmissionNotReady
from .final_material import collect_final_material
from .store import BroodlingStore, _now
from .submission import SubmissionCoordinator
from .zeroshot_sdk import ZeroshotSubmitter, canonical_request


@dataclass(frozen=True, slots=True)
class WorkUnitDisposition:
    work_unit_id: str
    contract_revision_id: str
    attempt_id: str
    outcome: str
    completed_at: str
    result_json: str

    @property
    def result(self) -> dict:
        return json.loads(self.result_json)


class WorkUnitDispositionCoordinator:
    def __init__(self, store: BroodlingStore, submitter: ZeroshotSubmitter):
        self.store = store
        self.submitter = submitter
        self.submission = SubmissionCoordinator(store, submitter)

    def record(self, attempt_id: str) -> WorkUnitDisposition | None:
        row = self.store.connection.execute(
            "SELECT d.*, f.record_json AS result_json FROM work_unit_dispositions AS d "
            "JOIN final_assurance AS f USING (attempt_id) WHERE d.attempt_id = ?",
            (attempt_id,),
        ).fetchone()
        return None if row is None else WorkUnitDisposition(**dict(row))

    def justification(self, attempt_id: str) -> dict | None:
        record = self.record(attempt_id)
        return None if record is None else record.result

    def _bound(self, attempt_id):
        attempt = self.store.require_current_attempt(attempt_id)
        revision = self.store.get_contract_revision(attempt.contract_revision_id)
        if json.loads(revision.canonical_bytes).get("requiredEffects") != []:
            raise SubmissionNotReady(
                "completion requires an explicit empty required-effect set"
            )
        attempt, assignment = self.submission._current(attempt_id)
        submitted = self.submission._required(attempt_id)
        if submitted.state != "correlated":
            raise SubmissionNotReady("completion requires an already-correlated run")
        self.submission._target(submitted)
        self.submission._source(attempt, assignment, require_b1=False)
        request = json.loads(submitted.request_json)
        if request != self.submission._request(attempt, assignment):
            raise SubmissionConflict(
                "result is not bound to the admitted workflow invocation"
            )
        return attempt, assignment, revision, submitted, request

    async def finalize(self, attempt_id: str) -> WorkUnitDisposition:
        existing = self.record(attempt_id)
        if existing is not None:
            return existing
        with self.store._write():
            _, _, _, submitted, request = self._bound(attempt_id)
        # SDK cancellation/transport loss detaches. Another call can wait for the
        # same result. Explicit abandonment alone removes Attempt authority.
        result = await self.submitter.wait(request, submitted.run_id)
        if result.run_id != submitted.run_id:
            raise SubmissionConflict("result belongs to another run")
        if not result.succeeded:
            reason = f"Zeroshot run failed: {result.failure}"
            self.store.abandon_attempt(attempt_id, reason)
            raise SubmissionNotReady(reason)
        with self.store._write() as connection:
            existing = self.record(attempt_id)
            if existing is not None:
                return existing
            attempt, assignment, revision, current, current_request = self._bound(
                attempt_id
            )
            if (
                current != submitted
                or current_request != request
                or result.run_id != current.run_id
            ):
                raise SubmissionConflict(
                    "result lost its immutable Attempt/run binding"
                )
            # The complete candidate stays in its owned worktree. If the frozen
            # Contract also selected retained bytes, honor that post-run request.
            selected = ()
            if revision.contract.final_assurance_materials is not None:
                try:
                    selected = collect_final_material(
                        revision.contract, assignment.path, attempt.b1_commit_oid
                    )
                except (OSError, ValueError, GitCommandError) as error:
                    raise SubmissionNotReady(
                        f"declared candidate material unavailable: {error}"
                    ) from error
            payload = canonical_request(
                {
                    "format": "broodling.final-assurance/v2",
                    "attemptId": attempt_id,
                    "contractRevisionId": revision.contract_revision_id,
                    "runId": result.run_id,
                    "workflow": request["preset"],
                    "workspace": assignment.worktree_path,
                    "selectedMaterial": selected,
                }
            )
            # Keep the existing custody table so old evidence remains readable.
            # Result retention and disposition now commit in one transaction.
            connection.execute(
                "INSERT INTO final_assurance (attempt_id, zeroshot_run_id, record_json) VALUES (?, ?, ?)",
                (attempt_id, result.run_id, payload),
            )
            connection.execute(
                "INSERT INTO work_unit_dispositions "
                "(work_unit_id, contract_revision_id, attempt_id, outcome, completed_at) "
                "VALUES (?, ?, ?, 'SUCCEEDED', ?)",
                (
                    attempt.work_unit_id,
                    attempt.contract_revision_id,
                    attempt_id,
                    _now(),
                ),
            )
            return self.record(attempt_id)
