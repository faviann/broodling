"""Broodling V1-P2 admission nucleus.

This package implements exactly four durable facts for one Work Unit:

* stable Work Unit identity for one target repository plus one primary
  authoritative GitHub issue;
* explicitly entitled source snapshots, with the exact bytes a Contract was
  built from;
* immutable Contract revisions; and
* the V1 no-effect Closability/admission decision for each revision.

It creates no Attempt, no worktree and no Zeroshot run, and it holds no runtime
execution state. Those belong to later V1 phases.
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
    BroodlingError,
    ContractImmutabilityError,
    InvalidWorkReference,
    SchemaVersionMismatch,
    SourceAttributionError,
    SourceNotEntitled,
    StoreLocationError,
    UnknownRecord,
    UnsupportedRuntime,
    WorkUnitIdentityConflict,
)
from .identity import WorkReference
from .store import (
    AdmissionDecisionRecord,
    BroodlingStore,
    ContractRevisionRecord,
    EntitledSourceRecord,
    WorkUnitRecord,
    default_store_path,
)

__version__ = "0.1.0"

__all__ = [
    "ADMITTED",
    "REJECTED",
    "AdmissionDecisionRecord",
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
    "InvalidWorkReference",
    "Obligation",
    "Prerequisite",
    "RequiredEffect",
    "SchemaVersionMismatch",
    "SourceAttribution",
    "SourceAttributionError",
    "SourceEntitlement",
    "SourceNotEntitled",
    "SourceSubmission",
    "StoreLocationError",
    "UnknownRecord",
    "UnsupportedRuntime",
    "WorkReference",
    "WorkUnitIdentityConflict",
    "WorkUnitRecord",
    "assess",
    "default_store_path",
    "__version__",
]
