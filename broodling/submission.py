"""Durable Attempt-to-run correlation through the public submission contract.

Only an already-dispatched, byte-identical request may reconcile source drift.
On the qualified exclusive-writer profile that drift belongs to this Attempt's
run. The public conflict then identifies the run already bound to its key. It
is correlation evidence, never execution success or authority to submit anew.
"""

from __future__ import annotations

import json
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path

from . import git, workspace
from .errors import (
    StaleAttempt,
    SubmissionConflict,
    SubmissionNotReady,
    UnknownRecord,
    WorktreeOwnershipConflict,
)
from .starting_state import admitted_material_digest
from .store import BroodlingStore
from .zeroshot_sdk import ZeroshotSubmitter, assert_no_effect_runtime, canonical_request


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
    """Serialize dispatch/correlation using the Broodling store's write boundary.

    The durable prepared -> dispatched transition precedes the external call.
    Transport errors leave dispatched intact, including process death. The
    subsequent call and correlation run under BEGIN IMMEDIATE; concurrent callers
    reread the winner's row, and a killed caller releases SQLite's lock. This is
    intentionally a bounded single-host P2 operation, not a scheduler or lease.
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

    def prepare(
        self,
        attempt_id: str,
        *,
        graph: dict,
        runtime: dict,
        initial_input=None,
        title: str = "Broodling V1-P2 Attempt",
    ) -> AttemptSubmission:
        assert_no_effect_runtime(runtime)
        with self._write() as connection:
            attempt, assignment = self._current(attempt_id)
            request = canonical_request(
                {
                    "submissionKey": f"broodling:v1:{attempt_id}",
                    "title": title,
                    "graph": graph,
                    "runtime": runtime,
                    "initialInput": initial_input,
                    "workspace": assignment.worktree_path,
                    "repository": attempt.b1_repository,
                    "branch": assignment.branch,
                    "startingCommit": attempt.b1_commit_oid,
                    "materialSha256": attempt.b1_material_sha256,
                    "originUrl": git.origin_url(assignment.path),
                    "target": self.submitter.target,
                }
            )
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

    def submit(self, attempt_id: str, **request) -> AttemptSubmission:
        self.prepare(attempt_id, **request)
        return self.reconcile(attempt_id)

    def prepare_assurance(self, attempt_id: str) -> AttemptSubmission:
        """Freeze the product protocol and the Attempt's admitted Contract.

        This path accepts no caller graph, runtime, or initialized assurance
        state. The ordinary P2 request boundary still enforces the same durable
        identity and rejects a different protocol already frozen for the Attempt.
        """
        from .assurance_graph import assurance_graph, assurance_runtime, initial_state
        from .contract import validate_mechanical_evidence

        self.submitter.require_assurance_profile()
        attempt = self.store.get_attempt(attempt_id)
        revision = self.store.get_contract_revision(attempt.contract_revision_id)
        try:
            validate_mechanical_evidence(revision.contract)
        except ValueError as error:
            raise SubmissionNotReady(str(error)) from error
        return self.prepare(
            attempt_id,
            graph=assurance_graph(),
            runtime=assurance_runtime(),
            initial_input=initial_state(
                revision.canonical_bytes.decode("utf-8"), attempt.b1_commit_oid
            ),
            title="Broodling V1 assurance Attempt",
        )

    def submit_assurance(self, attempt_id: str) -> AttemptSubmission:
        """Submit the product graph using the existing one-run correlation."""
        self.prepare_assurance(attempt_id)
        return self.reconcile(attempt_id)

    def reconcile(self, attempt_id: str) -> AttemptSubmission:
        # Commit dispatch intent separately, before ever calling the SDK. A
        # merely prepared request still requires B1, even after a restart.
        with self._write() as connection:
            attempt, assignment = self._current(attempt_id)
            record = self._required(attempt_id)
            self._target(record)
            if record.state == "prepared":
                self._source(attempt, assignment, require_b1=True)
                self._origin(record, assignment)
                self.submitter.validate_first_dispatch(assignment.path)
                connection.execute(
                    "UPDATE attempt_submissions SET state = 'dispatched' WHERE attempt_id = ?",
                    (attempt_id,),
                )
        conflict = None
        with self._write() as connection:
            attempt, assignment = self._current(attempt_id)
            record = self._required(attempt_id)
            self._target(record)
            if record.state == "correlated":
                return record
            if record.state == "blocked":
                raise SubmissionConflict(record.error_detail)
            self._source(attempt, assignment, require_b1=False)
            self._origin(record, assignment)
            try:
                run_id = self.submitter.submit(json.loads(record.request_json))
            except SubmissionConflict as error:
                # A conflict alone is not acceptance of a changed request.
                # Recheck ownership after the call, since the accepted run can
                # advance HEAD while the sidecar resolves the replay source.
                drifted = self._source(attempt, assignment, require_b1=False)
                self._origin(record, assignment)
                if drifted and error.existing_run_id.strip():
                    run_id = error.existing_run_id
                else:
                    conflict = error
                    connection.execute(
                        "UPDATE attempt_submissions SET state = 'blocked', error_detail = ? "
                        "WHERE attempt_id = ?",
                        (str(error), attempt_id),
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
        # Commit a genuine public conflict before reporting its refusal.
        if conflict is not None:
            raise conflict
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
        attempt = self.store.get_attempt(attempt_id)
        current = self.store.current_attempt(attempt.work_unit_id)
        if (
            not attempt.is_current
            or current is None
            or current.attempt_id != attempt_id
        ):
            raise StaleAttempt(f"{attempt_id} is not the durable current Attempt")
        self.store.get_contract_revision(attempt.contract_revision_id)
        if not self.store.is_admitted(attempt.contract_revision_id):
            raise SubmissionNotReady("Attempt Contract is not admitted")
        material = self.store.contract_source_material(attempt.contract_revision_id)
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
        head_changed = git.head_commit(path) != attempt.b1_commit_oid
        if require_b1 and (head_changed or git.uncommitted_entries(path)):
            raise SubmissionNotReady("first dispatch requires the clean admitted B1")
        # Dirty files alone do not change Zeroshot's resolved-source digest.
        # They cannot explain a public conflict and must not excuse one.
        return head_changed
