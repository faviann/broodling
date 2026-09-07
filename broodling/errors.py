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
