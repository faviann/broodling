"""Trusted-source entitlement.

The primary GitHub issue is the primary authoritative work reference. Referenced
material is governing input only when an entitling authority explicitly entitles
it. Model extraction may propose Contract structure from entitled bytes; it can
never add an entitled source.

Entitlement is decided from the *presentation* of a payload — who produced it and
who granted it — never from anything the payload says about itself. This module
therefore never parses submitted content.
"""

from __future__ import annotations

from dataclasses import dataclass

from .errors import SourceNotEntitled
from .identity import content_digest

#: Authorities that can entitle a source. Anything else, including a model or the
#: payload itself, cannot.
ENTITLING_AUTHORITIES: frozenset[str] = frozenset({"caller", "broodling_policy"})

#: Origins that may present a payload for entitlement.
TRUSTED_ORIGINS: frozenset[str] = frozenset({"caller", "broodling_policy"})

#: Origins that are recognized but can never produce an entitled source. Naming
#: them explicitly keeps the refusal a stated rule rather than an unknown-origin
#: accident.
NON_ENTITLING_ORIGINS: frozenset[str] = frozenset(
    {"model_extraction", "candidate_output", "referenced_material"}
)

#: Source kinds. ``primary_issue`` is entitled by Broodling policy because it is
#: the primary authoritative work reference; every other kind needs a grant.
PRIMARY_ISSUE = "primary_issue"
SOURCE_KINDS: frozenset[str] = frozenset(
    {PRIMARY_ISSUE, "referenced_document", "repository_file", "caller_statement"}
)

PRIMARY_ISSUE_BASIS = "primary_authoritative_work_reference"


@dataclass(frozen=True, slots=True)
class SourceEntitlement:
    """An explicit grant naming the authority that entitled a source."""

    granted_by: str
    basis: str


@dataclass(frozen=True, slots=True)
class SourceSubmission:
    """A payload presented for entitlement, with its exact bytes."""

    kind: str
    locator: str
    content: bytes
    media_type: str = "text/plain; charset=utf-8"
    retrieved_at: str = ""
    origin: str = "caller"
    entitlement: SourceEntitlement | None = None

    @property
    def content_sha256(self) -> str:
        return content_digest(self.content)


@dataclass(frozen=True, slots=True)
class EntitlementGrant:
    """The resolved entitlement Broodling recorded for a submission."""

    entitled_by: str
    basis: str


def evaluate_entitlement(submission: SourceSubmission) -> EntitlementGrant:
    """Decide whether a submission may become an entitled Contract source.

    Fails closed: an unrecognized kind, an unrecognized origin, a non-entitling
    origin, a missing grant or a grant from a non-entitling authority all refuse.
    """

    if submission.kind not in SOURCE_KINDS:
        raise SourceNotEntitled(f"unrecognized source kind {submission.kind!r}")
    if not isinstance(submission.content, bytes):
        raise SourceNotEntitled("source content must be exact bytes")
    if not submission.locator.strip():
        raise SourceNotEntitled("source locator is empty")

    if submission.origin in NON_ENTITLING_ORIGINS:
        raise SourceNotEntitled(
            f"a payload of origin {submission.origin!r} cannot become an entitled "
            "source; model extraction may propose Contract structure from already "
            "entitled bytes but cannot add a source"
        )
    if submission.origin not in TRUSTED_ORIGINS:
        raise SourceNotEntitled(f"unrecognized source origin {submission.origin!r}")

    grant = submission.entitlement
    if grant is not None:
        if grant.granted_by not in ENTITLING_AUTHORITIES:
            raise SourceNotEntitled(
                f"{grant.granted_by!r} is not an entitling authority; only "
                f"{sorted(ENTITLING_AUTHORITIES)} may entitle a source"
            )
        if not grant.basis.strip():
            raise SourceNotEntitled("an entitlement grant must state its basis")
        return EntitlementGrant(entitled_by=grant.granted_by, basis=grant.basis)

    if submission.kind == PRIMARY_ISSUE:
        return EntitlementGrant(
            entitled_by="broodling_policy", basis=PRIMARY_ISSUE_BASIS
        )
    raise SourceNotEntitled(
        f"referenced material {submission.locator!r} was not explicitly entitled"
    )
