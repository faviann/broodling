"""Irreversible Attempt stop and owned retirement, never semantic recovery."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from . import containment, git, workspace
from .assurance_graph import assurance_runtime, supports_contained_stop
from .errors import BroodlingError, WorktreeOwnershipConflict
from .provisioning import _sole_provisioner
from .store import BroodlingStore, _now
from .submission import SubmissionCoordinator
from .zeroshot_sdk import ZeroshotSubmitter, canonical_request


class CessationUnconfirmed(BroodlingError):
    """Abandonment is durable, but retirement remains unauthorized."""


@dataclass(frozen=True, slots=True)
class AttemptRetirement:
    attempt_id: str
    ceased_at: str
    proof_json: str
    retired_at: str | None


class AbandonmentCoordinator:
    def __init__(self, store: BroodlingStore, submitter: ZeroshotSubmitter):
        self.store = store
        self.submitter = submitter
        self.submission = SubmissionCoordinator(store, submitter)

    def record(self, attempt_id: str) -> AttemptRetirement | None:
        row = self.store.connection.execute(
            "SELECT * FROM attempt_retirements WHERE attempt_id = ?", (attempt_id,)
        ).fetchone()
        return None if row is None else AttemptRetirement(**dict(row))

    def _owned(self, attempt_id):
        attempt = self.store.get_attempt(attempt_id)
        assignment = self.store.worktree_assignment(attempt_id)
        path = assignment.path
        owner = self.store.worktree_owner(path)
        if (
            owner is None
            or owner.attempt_id != attempt_id
            or assignment.repository != attempt.b1_repository
            or path.resolve() != path
            or path.parent.is_symlink()
        ):
            raise WorktreeOwnershipConflict("retirement ownership changed")
        if path.parent.exists() and workspace.read_marker(path.parent) != attempt_id:
            raise WorktreeOwnershipConflict("retirement enclosure owner changed")
        return attempt, assignment

    async def stop(self, attempt_id: str, reason: str) -> AttemptRetirement:
        """Abandon first, fence launches, request stop and prove physical cessation.

        Inaccessible or ambiguous execution remains abandoned and blocking. This
        operation never calls submission replay to find a missing run identity.
        """
        self.store.abandon_attempt(attempt_id, reason)
        existing = self.record(attempt_id)
        if existing is not None:
            return existing
        _, assignment = self._owned(attempt_id)
        submitted = self.submission.record(attempt_id)
        never_dispatched = submitted is None or submitted.state == "prepared"
        if not assignment.path.parent.exists():
            if not never_dispatched or assignment.provisioned:
                raise CessationUnconfirmed(
                    "dispatched/provisioned enclosure is missing"
                )
            proof = {"basis": "never_materialized", "runId": None}
        else:
            containment.close_launches(assignment.path)
            if never_dispatched:
                # A crash inside Git provisioning can leave a child after the
                # caller releases its lock. Without the committed acknowledgment
                # its cessation is unknown; never guess from a settled-looking tree.
                if not assignment.provisioned:
                    raise CessationUnconfirmed(
                        "interrupted provisioning cessation is unknown"
                    )
                proof = {"basis": "never_dispatched", "runId": None}
            else:
                if submitted.run_id is None:
                    raise CessationUnconfirmed(
                        "existing dispatched run identity is unresolved"
                    )
                request = json.loads(submitted.request_json)
                if (
                    request["target"] != self.submitter.target
                    or request["target"]
                    .get("codexProfile", {})
                    .get("containmentProfile")
                    != containment.CONTAINMENT_PROFILE
                    or not supports_contained_stop(request["graph"])
                    or request["runtime"] != assurance_runtime()
                ):
                    raise CessationUnconfirmed(
                        "run lacks the supported product containment binding"
                    )
                observed = await self.submitter.stop_known(request, submitted.run_id)
                proof = {
                    "basis": containment.CONTAINMENT_PROFILE,
                    "runId": observed.run_id,
                    "runtimeSucceeded": observed.runtime_succeeded,
                    "runtimeFailure": observed.failure,
                    "profile": request["target"]["codexProfile"],
                }
            if not containment.confirm_ceased(assignment.path):
                raise CessationUnconfirmed("contained namespace has not fully ceased")
        with self.store._write() as connection:
            existing = self.record(attempt_id)
            if existing is not None:
                return existing
            # Immutable Attempt and submission bindings survived the external call.
            self._owned(attempt_id)
            if self.submission.record(attempt_id) != submitted:
                raise CessationUnconfirmed(
                    "submission correlation changed during cessation"
                )
            connection.execute(
                "INSERT INTO attempt_retirements (attempt_id, ceased_at, proof_json) "
                "VALUES (?, ?, ?)",
                (attempt_id, _now(), canonical_request(proof)),
            )
            return self.record(attempt_id)

    def retire(self, attempt_id: str) -> AttemptRetirement:
        """Discard only the ceased Attempt's exact owned tree and local branch."""
        record = self.record(attempt_id)
        if record is None:
            raise CessationUnconfirmed("retirement requires retained cessation proof")
        if record.retired_at is not None:
            return record
        attempt, assignment = self._owned(attempt_id)
        if not assignment.path.parent.exists():
            if json.loads(record.proof_json)["basis"] != "never_materialized":
                raise CessationUnconfirmed("retirement enclosure disappeared")
            with self.store._write():
                self._owned(attempt_id)
                return self._acknowledge(attempt_id)
        with (
            _sole_provisioner(assignment.path.parent) as lock_fd,
            self.store._write(),
        ):
            record = self.record(attempt_id)
            if record.retired_at is not None:
                return record
            attempt, assignment = self._owned(attempt_id)
            if not containment.confirm_ceased(assignment.path):
                raise CessationUnconfirmed("physical cessation no longer confirmed")
            self._remove_owned(attempt, assignment, lock_fd)
            return self._acknowledge(attempt_id)

    def _remove_owned(self, attempt, assignment, lock_fd):
        path = assignment.path
        repository = Path(attempt.b1_repository)
        entry = git.find_worktree(repository, path)
        if entry is not None:
            if entry.branch != assignment.branch:
                raise WorktreeOwnershipConflict("retirement worktree branch changed")
            if path.exists() and (
                not (path / ".git").is_file()
                or git.common_directory(path) != repository
                or git.current_branch(path) != assignment.branch
            ):
                raise WorktreeOwnershipConflict("retirement Git ownership changed")
            # The child retains the same flock even if this caller dies. Disable
            # hooks for this exact local administration; no external helper owns
            # the deletion. Repetition waits for that child before examining Git.
            git.run(
                repository,
                "-c",
                "core.hooksPath=/dev/null",
                "worktree",
                "remove",
                "--force",
                str(path),
                inherited_fds=(lock_fd,),
            )
        elif path.exists():
            raise WorktreeOwnershipConflict(
                "unregistered retirement path is not disposable proof"
            )
        if any(
            item.branch == assignment.branch for item in git.list_worktrees(repository)
        ):
            raise WorktreeOwnershipConflict(
                "retirement branch is attached to another worktree"
            )
        if git.branch_commit(repository, assignment.branch) is not None:
            git.run(
                repository,
                "-c",
                "core.hooksPath=/dev/null",
                "branch",
                "-D",
                "--",
                assignment.branch,
                inherited_fds=(lock_fd,),
            )

    def _acknowledge(self, attempt_id):
        self.store.connection.execute(
            "UPDATE attempt_retirements SET retired_at = ? "
            "WHERE attempt_id = ? AND retired_at IS NULL",
            (_now(), attempt_id),
        )
        return self.record(attempt_id)
