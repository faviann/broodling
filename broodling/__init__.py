"""Broodling V1-P2 admission and Attempt/worktree nucleus.

This package implements the durable facts for one Work Unit:

* stable Work Unit identity for one target repository plus one primary
  authoritative GitHub issue;
* explicitly entitled source snapshots, with the exact bytes a Contract was
  built from;
* immutable Contract revisions;
* the V1 no-effect Closability/admission decision for each revision; and
* one immutable current Attempt, its original starting state B1, and the one
  dedicated disposable worktree it exclusively owns.

It submits no Zeroshot run, holds no runtime execution state, and implements no
abandon/restart, assurance, candidate-provenance or effect machinery. Those
belong to later V1 phases.
"""

from __future__ import annotations

from .closability import (
    ADMITTED,
    REJECTED,
    ClosabilityAssessment,
    ClosabilityFinding,
    assess,
)
from .contract import (
    Contract,
    Criterion,
    EvidencePopulation,
    Obligation,
    Prerequisite,
    RequiredEffect,
    SourceAttribution,
)
from .entitlement import SourceEntitlement, SourceSubmission
from .errors import (
    AttemptAdmissionError,
    AttemptConflict,
    BroodlingError,
    ContractImmutabilityError,
    GitCommandError,
    InvalidWorkReference,
    SchemaVersionMismatch,
    SourceAttributionError,
    SourceNotEntitled,
    StoreLocationError,
    UnknownRecord,
    UnsupportedRuntime,
    UnsupportedStartingState,
    UnsupportedWorkspaceRoot,
    WorkUnitIdentityConflict,
    WorktreeOwnershipConflict,
)
from .identity import WorkReference
from .provisioning import AttemptProvisioner, ProvisionedWorktree
from .starting_state import StartingState, resolve_starting_state
from .store import (
    AdmissionDecisionRecord,
    AttemptRecord,
    BroodlingStore,
    ContractRevisionRecord,
    EntitledSourceRecord,
    WorkUnitRecord,
    WorktreeAssignmentRecord,
    default_store_path,
)
from .workspace import (
    DISPOSABLE_WORKTREE_MARKER,
    WorktreeAllocation,
    assert_durable_workspace_root,
)

__version__ = "0.1.0"

__all__ = [
    "ADMITTED",
    "AdmissionDecisionRecord",
    "AttemptAdmissionError",
    "AttemptConflict",
    "AttemptProvisioner",
    "AttemptRecord",
    "BroodlingError",
    "BroodlingStore",
    "ClosabilityAssessment",
    "ClosabilityFinding",
    "Contract",
    "ContractImmutabilityError",
    "ContractRevisionRecord",
    "Criterion",
    "DISPOSABLE_WORKTREE_MARKER",
    "EntitledSourceRecord",
    "EvidencePopulation",
    "GitCommandError",
    "InvalidWorkReference",
    "Obligation",
    "Prerequisite",
    "ProvisionedWorktree",
    "REJECTED",
    "RequiredEffect",
    "SchemaVersionMismatch",
    "SourceAttribution",
    "SourceAttributionError",
    "SourceEntitlement",
    "SourceNotEntitled",
    "SourceSubmission",
    "StartingState",
    "StoreLocationError",
    "UnknownRecord",
    "UnsupportedRuntime",
    "UnsupportedStartingState",
    "UnsupportedWorkspaceRoot",
    "WorkReference",
    "WorkUnitIdentityConflict",
    "WorkUnitRecord",
    "WorktreeAllocation",
    "WorktreeAssignmentRecord",
    "WorktreeOwnershipConflict",
    "__version__",
    "assert_durable_workspace_root",
    "assess",
    "default_store_path",
    "resolve_starting_state",
]
