"""Commit Broodling's lifecycle decision from Zeroshot's stable result."""

from __future__ import annotations

import json
from dataclasses import dataclass

from .delivery import NONE, PULL_REQUEST, authorization
from .errors import SubmissionConflict, SubmissionNotReady
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
        try:
            declared = json.loads(revision.canonical_bytes).get("requiredEffects")
            if not isinstance(declared, list) or not all(
                isinstance(item, dict) for item in declared
            ):
                raise ValueError("invalid requiredEffects declaration")
            delivery = authorization(revision.contract)
        except (KeyError, TypeError, ValueError) as error:
            raise SubmissionNotReady(
                "completion requires one supported frozen result delivery"
            ) from error
        attempt, assignment = self.submission._current(attempt_id)
        submitted = self.submission._required(attempt_id)
        if submitted.state != "correlated":
            raise SubmissionNotReady("completion requires an already-correlated run")
        request = json.loads(submitted.request_json)
        if request != self.submission._request(
            attempt, assignment, frozen_execution=request
        ):
            raise SubmissionConflict(
                "result is not bound to the admitted workflow invocation"
            )
        if request["preset"]["delivery"] != delivery.mode:
            raise SubmissionConflict("result delivery differs from Contract authority")
        return attempt, assignment, revision, submitted, request

    @staticmethod
    def _accepted_receipt(request: dict, output) -> dict:
        delivery = request["preset"]["delivery"]
        if delivery == NONE:
            raise SubmissionNotReady(
                "Zeroshot 10.3 no-effect runs provide no stable accepted result; "
                "local result handoff is an upstream capability gap"
            )
        if delivery != PULL_REQUEST or not isinstance(output, dict):
            raise SubmissionConflict(
                "successful run returned no authorized delivery receipt"
            )
        expected_fields = {
            "version",
            "mode",
            "outcome",
            "repository",
            "targetBranch",
            "headRevision",
            "pullRequestId",
        }
        selected = request.get("delivery")
        revision = output.get("headRevision")
        review = output.get("pullRequestId")
        if (
            set(output) != expected_fields
            or output.get("version") != "v1"
            or output.get("mode") != "pr"
            or output.get("outcome") != "opened"
            or not isinstance(selected, dict)
            or output.get("repository") != selected.get("repository")
            or output.get("targetBranch") != selected.get("targetBranch")
            or not isinstance(revision, str)
            or len(revision) != 40
            or any(character not in "0123456789abcdef" for character in revision)
            or revision == selected.get("baseRevision")
            or not isinstance(review, str)
            or not review.isascii()
            or not review.isdigit()
        ):
            raise SubmissionConflict(
                "Zeroshot delivery receipt does not match frozen PR authority"
            )
        return output

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
        receipt = self._accepted_receipt(request, result.output)
        with self.store._write() as connection:
            existing = self.record(attempt_id)
            if existing is not None:
                return existing
            attempt, _, revision, current, current_request = self._bound(attempt_id)
            if (
                current != submitted
                or current_request != request
                or result.run_id != current.run_id
            ):
                raise SubmissionConflict(
                    "result lost its immutable Attempt/run binding"
                )
            payload = canonical_request(
                {
                    "format": "broodling.final-assurance/v3",
                    "attemptId": attempt_id,
                    "contractRevisionId": revision.contract_revision_id,
                    "runId": result.run_id,
                    "workflow": request["preset"],
                    "acceptedRevision": receipt["headRevision"],
                    "deliveryReceipt": receipt,
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
