"""Admitting an Attempt and materializing its dedicated worktree at B1.

Two operations, deliberately separated by a durable commit:

``admit``
    resolves B1, allocates the Attempt identity and reserves a unique worktree
    path and branch — all in the store, before anything exists on the host.

``provision``
    makes the host match that reservation: one dedicated attached Git worktree,
    on a unique local branch, checked out at exactly B1.

The order is the crash-safety argument. Because the identity is durable *first*,
a crash anywhere in provisioning leaves one recoverable Attempt to converge on:
re-running ``provision`` finds the same path, the same branch and the same
commit, adopts whatever the interrupted run had already created, and never
allocates a replacement. Where the host state cannot be recognized as this
Attempt's, provisioning fails closed rather than taking over somebody's
directory.

That argument only holds against an *interrupted* run, whose leftovers have
stopped changing. It does not hold against a *concurrent* one: ``git worktree
add`` publishes its result in stages — the registration, then the attached
branch, then the checked-out tree — so a second process reading host state
mid-creation sees a worktree that is registered but detached, or a directory
that exists but is not yet registered, or a branch that is attached over a tree
that is not yet B1. None of those intermediate states is distinguishable, by
reading, from foreign or half-built state, so a reader either refuses its own
Attempt's worktree or acknowledges one that has not finished arriving.

Materializing one Attempt is therefore single-writer on this host: ``provision``
holds an exclusive lock on that Attempt's enclosure for the whole operation, so
every observation it makes is of settled state. The lock is host-local mutual
exclusion between live processes, not durable authority — durable authority is
the store's, and the kernel drops the lock when a process dies, which is what
keeps crash recovery converging exactly as before.

Worktree and branch creation here are host-local administrative setup, not
authoritative delivery: the branch is disposable runtime scaffolding, never
pushed and never treated as delivery.
"""

from __future__ import annotations

import fcntl
import os
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path

from . import git, workspace
from .checkout_profile import assert_supported_checkout
from .errors import (
    GitCommandError,
    UnsupportedWorkspaceRoot,
    WorktreeOwnershipConflict,
)
from .starting_state import StartingState, resolve_starting_state
from .store import AttemptRecord, BroodlingStore, WorktreeAssignmentRecord


@dataclass(frozen=True, slots=True)
class ProvisionedWorktree:
    """The materialized result: what exists on the host, and who owns it."""

    attempt: AttemptRecord
    assignment: WorktreeAssignmentRecord

    @property
    def path(self) -> Path:
        return self.assignment.path

    @property
    def branch(self) -> str:
        return self.assignment.branch

    @property
    def commit_oid(self) -> str:
        return self.attempt.b1_commit_oid


class AttemptProvisioner:
    """Admits Attempts into one store and materializes them under one root."""

    def __init__(self, store: BroodlingStore, workspace_root: Path | str) -> None:
        self.store = store
        self.workspace_root = workspace.assert_durable_workspace_root(
            Path(workspace_root)
        )

    # -------------------------------------------------------------------- admit

    def admit(
        self,
        contract_revision_id: str,
        repository: Path | str,
        revision: str = "HEAD",
    ) -> AttemptRecord:
        """Admit the current Attempt for an admitted Contract revision.

        Refuses before writing anything when the requested starting state is
        outside the supported B1 policy, so unsupported starting material never
        becomes a half-admitted Attempt.
        """

        starting_state = resolve_starting_state(repository, revision)
        self._assert_root_outside_repository(repository, starting_state)
        return self.store.admit_attempt(
            contract_revision_id,
            starting_state,
            workspace_root=self.workspace_root,
        )

    def _assert_root_outside_repository(
        self, repository: Path | str, starting_state: StartingState
    ) -> None:
        """A worktree root inside the repository it checks out is not dedicated.

        Nesting a disposable worktree under the source checkout puts Attempt
        scaffolding inside the material B1 is supposed to be, and makes retiring
        the Attempt a deletion inside somebody's repository.
        """

        repository_paths = (
            Path(repository).expanduser().resolve(),
            Path(starting_state.repository),
        )
        for owned in repository_paths:
            if self.workspace_root == owned or owned in self.workspace_root.parents:
                raise UnsupportedWorkspaceRoot(
                    f"workspace root {self.workspace_root} is inside repository "
                    f"{owned}; an Attempt worktree must be dedicated and disposable "
                    "outside the source repository"
                )

    # ---------------------------------------------------------------- provision

    def provision(self, attempt_id: str) -> ProvisionedWorktree:
        """Materialize this Attempt's worktree at B1. Idempotent and converging.

        Repeat it as often as you like, from as many processes as you like: an
        already-materialized current worktree is recognized and left alone, an
        interrupted one is finished, and a worktree that has gone missing is
        rebuilt from the recorded B1 — never from live ``HEAD``. Abandoned
        Attempts are refused before candidate materialization or acknowledgment.

        Concurrent callers converge rather than compete. They queue on this
        Attempt's enclosure, so each one reads host state that some other
        process has finished writing, and every one of them returns the same
        worktree at the same B1 on the same branch.
        """

        # Claim scaffolding only while current. Release SQLite before waiting
        # for the host lock: every host operation orders enclosure -> SQLite.
        with self.store._write():
            self.store.require_current_attempt(attempt_id)
            assignment = self.store.worktree_assignment(attempt_id)
            enclosure = assignment.path.parent
            self._claim_enclosure(enclosure, attempt_id)
        # Serialize abandonment with host mutation, including the external
        # Git call and acknowledgment. A killed caller rolls back the fact;
        # the existing owned leftovers still converge on a subsequent call.
        with _sole_provisioner(enclosure) as lock_fd, self.store._write():
            attempt = self.store.require_current_attempt(attempt_id)
            assignment = self.store.worktree_assignment(attempt_id)
            return self._materialize(attempt, assignment, lock_fd)

    def _materialize(
        self, attempt: AttemptRecord, assignment: WorktreeAssignmentRecord, lock_fd: int
    ) -> ProvisionedWorktree:
        """Bring the host in line with the reservation. Runs single-writer."""

        repository = Path(attempt.b1_repository)
        assert_supported_checkout(repository, attempt.b1_commit_oid)
        path = assignment.path
        entry = git.find_worktree(repository, path)

        if entry is not None:
            self._assert_entry_is_ours(entry, assignment, path)
            if _is_live_worktree(path):
                return self._acknowledge(attempt)
            registered_elsewhere = True
        else:
            self._assert_path_is_free(path)
            registered_elsewhere = False

        try:
            self._create(
                repository,
                attempt,
                assignment,
                force=registered_elsewhere,
                lock_fd=lock_fd,
            )
        except GitCommandError:
            # An interrupted run may have left exactly the worktree this one was
            # about to create — a half-registered path Git now refuses to add
            # over. Converging on it is the whole point; only a failure that did
            # not leave this Attempt's worktree behind propagates.
            if not self._already_materialized(repository, assignment):
                raise
        return self._acknowledge(attempt)

    def admit_and_provision(
        self,
        contract_revision_id: str,
        repository: Path | str,
        revision: str = "HEAD",
    ) -> ProvisionedWorktree:
        """Admit, then provision. Safe to repeat after any interruption."""

        attempt = self.admit(contract_revision_id, repository, revision)
        return self.provision(attempt.attempt_id)

    # ------------------------------------------------------------------ helpers

    def _claim_enclosure(self, enclosure: Path, attempt_id: str) -> None:
        claimed = workspace.read_marker(enclosure)
        if claimed is not None and claimed != attempt_id:
            raise WorktreeOwnershipConflict(
                f"{enclosure} is already marked as the disposable worktree of "
                f"attempt {claimed}; attempt {attempt_id} will not take it over"
            )
        workspace.write_marker(enclosure, attempt_id)

    @staticmethod
    def _assert_entry_is_ours(
        entry: git.WorktreeEntry, assignment: WorktreeAssignmentRecord, path: Path
    ) -> None:
        if entry.branch != assignment.branch:
            raise WorktreeOwnershipConflict(
                f"{path} is already a registered worktree on branch "
                f"{entry.branch!r}, not this Attempt's branch "
                f"{assignment.branch!r}"
            )

    @staticmethod
    def _already_materialized(
        repository: Path, assignment: WorktreeAssignmentRecord
    ) -> bool:
        entry = git.find_worktree(repository, assignment.path)
        return (
            entry is not None
            and entry.branch == assignment.branch
            and _is_live_worktree(assignment.path)
        )

    @staticmethod
    def _assert_path_is_free(path: Path) -> None:
        if path.exists() and any(path.iterdir()):
            raise WorktreeOwnershipConflict(
                f"{path} already contains files but is not a registered worktree "
                "of this repository; provisioning will not overwrite it"
            )

    def _create(
        self,
        repository: Path,
        attempt: AttemptRecord,
        assignment: WorktreeAssignmentRecord,
        *,
        force: bool,
        lock_fd: int,
    ) -> None:
        """Create the worktree, adopting a branch a prior run already made."""

        existing = git.branch_commit(repository, assignment.branch)
        if existing is not None and existing != attempt.b1_commit_oid:
            raise WorktreeOwnershipConflict(
                f"branch {assignment.branch} already exists at {existing}, which "
                f"is not this Attempt's B1 {attempt.b1_commit_oid}; provisioning "
                "will not move or reuse it"
            )
        git.add_worktree(
            repository,
            assignment.path,
            assignment.branch,
            attempt.b1_commit_oid,
            reuse_branch=existing is not None,
            force=force,
            inherited_fds=(lock_fd,),
        )
        self._assert_materialized_at_b1(attempt, assignment)

    @staticmethod
    def _assert_materialized_at_b1(
        attempt: AttemptRecord, assignment: WorktreeAssignmentRecord
    ) -> None:
        branch = git.current_branch(assignment.path)
        if branch != assignment.branch:
            raise WorktreeOwnershipConflict(
                f"{assignment.path} came up on branch {branch!r}, not "
                f"{assignment.branch!r}"
            )
        head = git.head_commit(assignment.path)
        if head != attempt.b1_commit_oid:
            raise WorktreeOwnershipConflict(
                f"{assignment.path} came up at {head}, not B1 {attempt.b1_commit_oid}"
            )

    def _acknowledge(self, attempt: AttemptRecord) -> ProvisionedWorktree:
        return ProvisionedWorktree(
            attempt=attempt,
            assignment=self.store._acknowledge_worktree_provisioned(attempt.attempt_id),
        )


@contextmanager
def _sole_provisioner(enclosure: Path) -> Iterator[int]:
    """Hold an Attempt's enclosure against every other live provisioner.

    Scoped to the enclosure because that is exactly one Attempt's scaffolding:
    two Attempts never share one, so two Work Units never queue on each other.

    The lock lives as long as its descriptors. Git creation inherits it, so a
    killed caller cannot let another provisioner race its surviving Git child.
    Once both exit, repeating the operation converges on the owned leftovers.
    """

    handle = os.open(
        enclosure / workspace.PROVISIONING_LOCK,
        os.O_CREAT | os.O_WRONLY | os.O_CLOEXEC,
        0o600,
    )
    try:
        fcntl.flock(handle, fcntl.LOCK_EX)
        yield handle
    finally:
        os.close(handle)


def _is_live_worktree(path: Path) -> bool:
    """A registered path is live when its ``.git`` link is actually on disk."""

    return path.is_dir() and (path / ".git").exists()
