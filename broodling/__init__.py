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
Zeroshot. Minimal final assurance custody retains declared source material and
normal current-run assessment. Administrative abandonment and owned retirement
require physical cessation. Explicit replacement allocates a fresh Attempt
from the original admitted B1. Normal finalization durably records justified
no-effect Work Unit success; interrupted finalization abandons the Attempt.
"""

from __future__ import annotations

from .abandonment import AbandonmentCoordinator, AttemptRetirement, CessationUnconfirmed
from .assurance import FinalAssuranceCoordinator, FinalAssuranceRecord
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
    FinalAssuranceMaterial,
    MechanicalEvidence,
    Obligation,
    Prerequisite,
    RequiredEffect,
    SourceAttribution,
)
from .disposition import WorkUnitDisposition, WorkUnitDispositionCoordinator
from .entitlement import SourceEntitlement, SourceSubmission
from .errors import (
    AttemptAdmissionError,
    AttemptConflict,
    BroodlingError,
    ContractImmutabilityError,
    FinalizationInterrupted,
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
from .replacement import RetryCoordinator
from .starting_state import StartingState, resolve_starting_state
from .store import (
    AdmissionDecisionRecord,
    AttemptRecord,
    AttemptRetryRecord,
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
    "AbandonmentCoordinator",
    "AdmissionDecisionRecord",
    "AttemptAdmissionError",
    "AttemptConflict",
    "AttemptProvisioner",
    "AttemptRecord",
    "AttemptRetirement",
    "AttemptRetryRecord",
    "AttemptSubmission",
    "BroodlingError",
    "BroodlingStore",
    "CessationUnconfirmed",
    "ClosabilityAssessment",
    "ClosabilityFinding",
    "Contract",
    "ContractImmutabilityError",
    "ContractRevisionRecord",
    "Criterion",
    "EntitledSourceRecord",
    "EvidencePopulation",
    "FinalAssuranceCoordinator",
    "FinalAssuranceMaterial",
    "FinalAssuranceRecord",
    "FinalizationInterrupted",
    "GitCommandError",
    "InvalidWorkReference",
    "MechanicalEvidence",
    "Obligation",
    "Prerequisite",
    "ProvisionedWorktree",
    "QualifiedCodexProfile",
    "RequiredEffect",
    "RetryCoordinator",
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
    "WorkUnitDisposition",
    "WorkUnitDispositionCoordinator",
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
