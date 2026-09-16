"""Domain errors for the V1-P2 admission nucleus.

Every error here is a fail-closed refusal. None of them is recoverable by
weakening a Contract obligation, aliasing a Work Unit identity or entitling a
payload that no entitling authority granted.
"""

from __future__ import annotations


class BroodlingError(Exception):
    """Base class for every Broodling domain error."""


class InvalidWorkReference(BroodlingError):
    """The submitted repository/issue reference cannot be canonicalized."""


class WorkUnitIdentityConflict(BroodlingError):
    """Ingress would alias or overwrite an existing Work Unit identity."""


class SourceNotEntitled(BroodlingError):
    """No entitling authority granted this payload as a Contract source."""


class SourceAttributionError(BroodlingError):
    """A Contract revision attributes material that is not an entitled source."""


class ContractImmutabilityError(BroodlingError):
    """An admitted Contract revision cannot be amended in place."""


class UnknownRecord(BroodlingError):
    """A referenced durable record does not exist."""


class SchemaVersionMismatch(BroodlingError):
    """The opened database was not written by this schema version."""


class StoreLocationError(BroodlingError):
    """The Broodling store would live inside a disposable Attempt worktree."""


class UnsupportedRuntime(BroodlingError):
    """The host runtime is outside the selected product configuration."""


class UnsupportedStartingState(BroodlingError):
    """The requested starting state is not representable by the V1 B1 policy."""


class UnsupportedWorkspaceRoot(BroodlingError):
    """The configured worktree root is not a durable non-temporary location."""


class AttemptAdmissionError(BroodlingError):
    """An Attempt cannot be admitted for this Contract revision."""


class AttemptConflict(BroodlingError):
    """Admission would create a second current Attempt for one Work Unit."""


class WorktreeOwnershipConflict(BroodlingError):
    """A worktree path or branch is already owned by different Broodling state."""


class GitCommandError(BroodlingError):
    """A local Git command Broodling needs for administrative setup failed."""


class StaleAttempt(BroodlingError):
    """Only the durable current Attempt may acquire or exercise authority."""


class SubmissionNotReady(BroodlingError):
    """The admitted source/worktree is not ready for first dispatch."""


class SubmissionConflict(BroodlingError):
    """Competing submission authority; never permission to mint another key."""

    def __init__(self, message: str, *, existing_run_id: str = "") -> None:
        super().__init__(message)
        self.existing_run_id = existing_run_id
