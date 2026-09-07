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

It durably correlates an Attempt to its one Zeroshot run and authors the V1
assurance graph. Execution state, typed validation and routing remain in
Zeroshot. Abandon/restart, final result custody, Work Unit disposition and
effects are outside this implementation boundary.
"""

from __future__ import annotations

from .closability import (
    ADMITTED,
    REJECTED,
    ClosabilityAssessment,
    ClosabilityFinding,
    assess,
)
from .codex_profile import QualifiedCodexProfile
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
    StaleAttempt,
    StoreLocationError,
    SubmissionConflict,
    SubmissionNotReady,
    UnknownRecord,
    UnsupportedRuntime,
    UnsupportedStartingState,
    UnsupportedWorkspaceRoot,
    WorktreeOwnershipConflict,
    WorkUnitIdentityConflict,
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
    WorktreeAssignmentRecord,
    WorkUnitRecord,
    default_store_path,
)
from .submission import AttemptSubmission, SubmissionCoordinator
from .workspace import (
    DISPOSABLE_WORKTREE_MARKER,
    WorktreeAllocation,
    assert_durable_workspace_root,
)
from .zeroshot_sdk import ZeroshotSubmitter

__version__ = "0.1.0"

__all__ = [
    "ADMITTED",
    "DISPOSABLE_WORKTREE_MARKER",
    "REJECTED",
    "AdmissionDecisionRecord",
    "AttemptAdmissionError",
    "AttemptConflict",
    "AttemptProvisioner",
    "AttemptRecord",
    "AttemptSubmission",
    "BroodlingError",
    "BroodlingStore",
    "ClosabilityAssessment",
    "ClosabilityFinding",
    "Contract",
    "ContractImmutabilityError",
    "ContractRevisionRecord",
    "Criterion",
    "EntitledSourceRecord",
    "EvidencePopulation",
    "GitCommandError",
    "InvalidWorkReference",
    "Obligation",
    "Prerequisite",
    "ProvisionedWorktree",
    "QualifiedCodexProfile",
    "RequiredEffect",
    "SchemaVersionMismatch",
    "SourceAttribution",
    "SourceAttributionError",
    "SourceEntitlement",
    "SourceNotEntitled",
    "SourceSubmission",
    "StaleAttempt",
    "StartingState",
    "StoreLocationError",
    "SubmissionConflict",
    "SubmissionCoordinator",
    "SubmissionNotReady",
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
    "ZeroshotSubmitter",
    "__version__",
    "assert_durable_workspace_root",
    "assess",
    "default_store_path",
    "resolve_starting_state",
]
