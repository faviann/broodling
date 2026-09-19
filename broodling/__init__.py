"""One admitted Work Unit, executed by Zeroshot.

Broodling freezes domain authority and the invocation, correlates one Attempt
with one native software-change run, and commits a lifecycle decision only from
an authorized stable Zeroshot result. Zeroshot owns execution, review, repair,
delivery, sessions, observation and stopping. Dispatched worktrees remain
quarantined when physical cessation cannot be established by the supported target.
"""

from __future__ import annotations

from .abandonment import AbandonmentCoordinator, AttemptRetirement, CessationUnconfirmed
from .closability import (
    ADMITTED,
    REJECTED,
    ClosabilityAssessment,
    ClosabilityFinding,
    assess,
)
from .codex_profile import CodexProfile
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
from .github_source import AcquiredIssue, GitHubSourceError, acquire_github_issue
from .identity import WorkReference
from .ingress import (
    ContractIngress,
    ContractIngressResult,
    ContractProposalInput,
    InvalidContractProposal,
)
from .invocation import Broodling, InvocationStatus
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
    "AcquiredIssue",
    "AdmissionDecisionRecord",
    "AttemptAdmissionError",
    "AttemptConflict",
    "AttemptProvisioner",
    "AttemptRecord",
    "AttemptRetirement",
    "AttemptRetryRecord",
    "AttemptSubmission",
    "Broodling",
    "BroodlingError",
    "BroodlingStore",
    "CessationUnconfirmed",
    "ClosabilityAssessment",
    "ClosabilityFinding",
    "CodexProfile",
    "Contract",
    "ContractIngress",
    "ContractIngressResult",
    "ContractImmutabilityError",
    "ContractProposalInput",
    "ContractRevisionRecord",
    "Criterion",
    "EntitledSourceRecord",
    "EvidencePopulation",
    "FinalAssuranceMaterial",
    "GitCommandError",
    "GitHubSourceError",
    "InvalidContractProposal",
    "InvalidWorkReference",
    "InvocationStatus",
    "MechanicalEvidence",
    "Obligation",
    "Prerequisite",
    "ProvisionedWorktree",
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
    "acquire_github_issue",
    "assess",
    "default_store_path",
    "resolve_starting_state",
]
