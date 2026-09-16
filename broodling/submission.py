"""Durable Attempt-to-run correlation through the public submission contract.

Only an already-dispatched, byte-identical request may reconcile source drift.
On the exclusive-writer profile that drift belongs to this Attempt's
run. The public conflict then identifies the run already bound to its key. It
is correlation evidence, never execution success or authority to submit anew.
"""

from __future__ import annotations

import json
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path

from . import git, workspace
from .delivery import PULL_REQUEST, authorization
from .errors import (
    StaleAttempt,
    SubmissionConflict,
    SubmissionNotReady,
    UnknownRecord,
    WorktreeOwnershipConflict,
)
from .starting_state import admitted_material_digest
from .store import BroodlingStore
from .zeroshot_sdk import ZeroshotSubmitter, canonical_request


@dataclass(frozen=True)
class AttemptSubmission:
    attempt_id: str
    submission_key: str
    request_json: str
    state: str
    zeroshot_run_id: str | None
    error_detail: str | None

    @property
    def run_id(self) -> str | None:
        return self.zeroshot_run_id


class SubmissionCoordinator:
    """Persist dispatch intent, call Zeroshot, then durably correlate its run.

    No SQLite write transaction spans the external call. Concurrent callers may
    therefore submit the same frozen request; Zeroshot's submission key is the
    duplicate-prevention boundary and every caller converges on its one run. A
    transport failure leaves ``dispatched`` intact for the same idempotent replay.
    """

    def __init__(self, store: BroodlingStore, submitter: ZeroshotSubmitter) -> None:
        self.store = store
        self.submitter = submitter

    def record(self, attempt_id: str) -> AttemptSubmission | None:
        row = self.store.connection.execute(
            "SELECT * FROM attempt_submissions WHERE attempt_id = ?", (attempt_id,)
        ).fetchone()
        return None if row is None else AttemptSubmission(**dict(row))

    @contextmanager
    def _write(self):
        # Share the same transactional boundary as admission/currentness.
        with self.store._write() as connection:
            yield connection

    def _request(
        self, attempt, assignment, *, frozen_execution: dict | None = None
    ) -> dict:
        revision = self.store.get_contract_revision(attempt.contract_revision_id)
        delivery = authorization(revision.contract)
        work_unit = self.store.get_work_unit(attempt.work_unit_id)
        if delivery.mode == PULL_REQUEST and work_unit.host != "github.com":
            raise SubmissionNotReady(
                "pull-request delivery requires a GitHub Work Unit"
            )
        effect_policy = (
            "The required-effect set is empty. Keep changes in this assigned worktree."
            if delivery.mode != PULL_REQUEST
            else (
                "The sole authorized external effect is Zeroshot's native pull-request "
                "delivery. Do not publish, push, create or update a PR, merge, change "
                "issues, deploy, or perform other authoritative effects yourself; the "
                "native delivery node alone owns the authorized PR effect."
            )
        )
        task = (
            "Complete this admitted software-development Work Unit. The frozen Contract "
            "and entitled source material below govern scope and acceptance. Candidate edits "
            "cannot amend that authority. Implement the criteria and run the declared/relevant "
            "checks; independently verify the actual outcome. "
            + effect_policy
            + "\n\n"
            + canonical_request(
                {
                    "contract": json.loads(revision.canonical_bytes),
                    "admittedInstructions": self.store.frozen_instructions(
                        attempt.attempt_id
                    ),
                    "comparisonBase": attempt.b1_commit_oid,
                }
            )
        )
        request = {
            "submissionKey": f"broodling:v1:{attempt.attempt_id}",
            "title": f"Broodling Attempt {attempt.attempt_id}",
            "task": task,
            "preset": {"name": "software-change", "delivery": delivery.mode},
            "runtime": (
                self.submitter.runtime_for(delivery.mode)
                if frozen_execution is None
                else frozen_execution["runtime"]
            ),
            "workspace": assignment.worktree_path,
            "repository": attempt.b1_repository,
            "branch": assignment.branch,
            "startingCommit": attempt.b1_commit_oid,
            "materialSha256": attempt.b1_material_sha256,
            "originUrl": (
                git.origin_url(assignment.path)
                if frozen_execution is None
                else frozen_execution["originUrl"]
            ),
            "target": (
                self.submitter.target
                if frozen_execution is None
                else frozen_execution["target"]
            ),
        }
        if delivery.mode == PULL_REQUEST:
            request["delivery"] = {
                "repository": f"{work_unit.owner}/{work_unit.repository}",
                "targetBranch": delivery.target_branch,
                "baseRevision": attempt.b1_commit_oid,
            }
        return request

    def prepare(self, attempt_id: str) -> AttemptSubmission:
        """Freeze the standard workflow invocation from admitted facts only."""
        with self._write() as connection:
            attempt, assignment = self._current(attempt_id)
            self.store._validate_retry_target(attempt_id, self.submitter.target)
            request = canonical_request(self._request(attempt, assignment))
            previous = self.record(attempt_id)
            if previous is not None:
                if previous.request_json != request:
                    raise SubmissionConflict(
                        "request differs from the persisted submission"
                    )
                return previous
            self._source(attempt, assignment, require_b1=True)
            connection.execute(
                "INSERT INTO attempt_submissions "
                "(attempt_id, submission_key, request_json, state) VALUES (?, ?, ?, 'prepared')",
                (attempt_id, f"broodling:v1:{attempt_id}", request),
            )
            return self.record(attempt_id)

    def submit(self, attempt_id: str) -> AttemptSubmission:
        self.prepare(attempt_id)
        return self.reconcile(attempt_id)

    def reconcile(self, attempt_id: str) -> AttemptSubmission:
        # Commit dispatch intent separately, before ever calling the SDK. A
        # merely prepared request still requires B1, even after a restart.
        with self._write() as connection:
            attempt, assignment = self._current(attempt_id)
            record = self._required(attempt_id)
            if record.state == "correlated":
                return record
            self._target(record)
            if record.state == "prepared":
                self.store._validate_retry_target(attempt_id, self.submitter.target)
                self._source(attempt, assignment, require_b1=True)
                self._origin(record, assignment)
                self.submitter.validate_dispatch(
                    assignment.path, json.loads(record.request_json)
                )
                connection.execute(
                    "UPDATE attempt_submissions SET state = 'dispatched' WHERE attempt_id = ?",
                    (attempt_id,),
                )

            record = self._required(attempt_id)
            if record.state == "blocked":
                raise SubmissionConflict(record.error_detail)
            self._source(attempt, assignment, require_b1=False)
            self._origin(record, assignment)
            request = json.loads(record.request_json)

        # Delivery credentials are intentionally not persisted. Recheck the
        # current credential on every replay, including after a crash left the
        # durable state at dispatched. This check does not hold SQLite's writer.
        self.submitter.validate_dispatch(assignment.path, request)

        # Zeroshot owns duplicate prevention for this immutable submission key.
        # Keeping this call outside BEGIN IMMEDIATE lets unrelated lifecycle
        # writes proceed while native source resolution or startup is slow.
        conflict = None
        try:
            run_id = self.submitter.submit(request)
        except SubmissionConflict as error:
            conflict = error

        stale = False
        with self._write() as connection:
            record = self._required(attempt_id)
            self._target(record)
            if record.state == "correlated":
                if conflict is None and run_id != record.run_id:
                    raise SubmissionConflict(
                        "concurrent submission returned a different run identity"
                    )
                if (
                    conflict is not None
                    and conflict.existing_run_id.strip()
                    and conflict.existing_run_id != record.run_id
                ):
                    raise SubmissionConflict(
                        "concurrent submission conflict named a different run identity"
                    )
                conflict = None
                settled = record
            elif record.state == "blocked":
                raise SubmissionConflict(record.error_detail)
            else:
                attempt, assignment = self._admitted(attempt_id)
                drifted = self._source(attempt, assignment, require_b1=False)
                self._origin(record, assignment)
                if conflict is not None:
                    # A conflict alone is not acceptance of a changed request.
                    # Recheck ownership after the call, since the accepted run can
                    # advance HEAD while the sidecar resolves the replay source.
                    if drifted and conflict.existing_run_id.strip():
                        run_id = conflict.existing_run_id
                        conflict = None
                    else:
                        connection.execute(
                            "UPDATE attempt_submissions SET state = 'blocked', error_detail = ? "
                            "WHERE attempt_id = ?",
                            (str(conflict), attempt_id),
                        )
                if conflict is None:
                    if not isinstance(run_id, str) or not run_id.strip():
                        raise SubmissionConflict(
                            "submission returned no public run identity"
                        )
                    connection.execute(
                        "UPDATE attempt_submissions SET state = 'correlated', zeroshot_run_id = ? "
                        "WHERE attempt_id = ? AND state = 'dispatched'",
                        (run_id, attempt_id),
                    )
                settled = self._required(attempt_id)
            attempt = self.store.get_attempt(attempt_id)
            stale = (
                not attempt.is_current or self.store.abandonment(attempt_id) is not None
            )
        # Commit a genuine public conflict before reporting its refusal.
        if conflict is not None:
            raise conflict
        if stale:
            raise StaleAttempt(
                f"{attempt_id} was abandoned while its Zeroshot run was being correlated"
            )
        return settled

    def _required(self, attempt_id: str) -> AttemptSubmission:
        record = self.record(attempt_id)
        if record is None:
            raise UnknownRecord(f"no prepared submission for {attempt_id}")
        return record

    def _target(self, record: AttemptSubmission) -> None:
        if json.loads(record.request_json)["target"] != self.submitter.target:
            raise SubmissionConflict(
                "runtime target/environment differs from persisted request"
            )

    def _origin(self, record, assignment) -> None:
        if json.loads(record.request_json)["originUrl"] != git.origin_url(
            assignment.path
        ):
            raise SubmissionConflict("repository source configuration changed")

    def _current(self, attempt_id: str):
        attempt = self.store.require_current_attempt(attempt_id)
        current = self.store.current_attempt(attempt.work_unit_id)
        if (
            not attempt.is_current
            or current is None
            or current.attempt_id != attempt_id
        ):
            raise StaleAttempt(f"{attempt_id} is not the durable current Attempt")
        admitted, assignment = self._admitted(attempt_id)
        return admitted, assignment

    def _admitted(self, attempt_id: str):
        """Reload immutable admission/ownership without granting current authority."""
        attempt = self.store.get_attempt(attempt_id)
        self.store.get_contract_revision(attempt.contract_revision_id)
        if not self.store.is_admitted(attempt.contract_revision_id):
            raise SubmissionNotReady("Attempt Contract is not admitted")
        material = self.store._validated_source_material(attempt)
        if (
            admitted_material_digest((s.source_id, s.content_sha256) for s in material)
            != attempt.b1_material_sha256
        ):
            raise SubmissionConflict("admitted B1 material changed")
        return attempt, self.store.worktree_assignment(attempt_id)

    def _source(self, attempt, assignment, *, require_b1: bool) -> bool:
        path = assignment.path
        if not assignment.provisioned:
            raise SubmissionNotReady("Attempt worktree is not provisioned")
        owner = self.store.worktree_owner(path)
        entry = git.find_worktree(attempt.b1_repository, path)
        if (
            owner is None
            or owner.attempt_id != attempt.attempt_id
            or workspace.read_marker(path.parent) != attempt.attempt_id
            or path.resolve() != path
            or not (path / ".git").is_file()
            or entry is None
            or entry.branch != assignment.branch
            or git.common_directory(path) != Path(attempt.b1_repository)
        ):
            raise WorktreeOwnershipConflict("Attempt worktree/source ownership changed")
        if git.current_branch(path) != assignment.branch:
            raise WorktreeOwnershipConflict("Attempt source branch changed")
        if require_b1:
            from .checkout_profile import assert_supported_checkout

            assert_supported_checkout(
                Path(attempt.b1_repository), attempt.b1_commit_oid
            )
            assert_supported_checkout(path, attempt.b1_commit_oid)
        head_changed = git.head_commit(path) != attempt.b1_commit_oid
        if require_b1 and (head_changed or git.uncommitted_entries(path)):
            raise SubmissionNotReady("first dispatch requires the clean admitted B1")
        # Dirty files alone do not change Zeroshot's resolved-source digest.
        # They cannot explain a public conflict and must not excuse one.
        return head_changed
