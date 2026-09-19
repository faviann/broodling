"""Explicit sources -> semantic proposal -> deterministic immutable admission.

The proposer receives records, never an entitlement or store capability. Its
structured interpretation supplements the complete frozen request; it does not
replace it. This boundary ends at admission and never creates an Attempt.
"""

from __future__ import annotations

import json
from collections.abc import Callable
from dataclasses import dataclass, fields, is_dataclass
from types import UnionType
from typing import get_args, get_origin, get_type_hints

from .contract import CONSTRUCTED_BY, Contract, RequiredEffect, SourceAttribution
from .entitlement import PRIMARY_ISSUE, SourceEntitlement, SourceSubmission
from .errors import BroodlingError, SourceAttributionError, SourceNotEntitled
from .github_source import acquire_github_issue
from .identity import WorkReference
from .store import (
    AdmissionDecisionRecord,
    BroodlingStore,
    ContractRevisionRecord,
    EntitledSourceRecord,
    WorkUnitRecord,
)


class InvalidContractProposal(BroodlingError):
    """A proposal is malformed or attempts to change its input authority."""


@dataclass(frozen=True, slots=True)
class ContractProposalInput:
    """Source-neutral, immutable input to caller or model interpretation."""

    work_unit: WorkUnitRecord
    sources: tuple[EntitledSourceRecord, ...]
    required_effects: tuple[RequiredEffect, ...]
    constructed_by: str

    @property
    def source_attribution(self) -> tuple[SourceAttribution, ...]:
        return tuple(
            SourceAttribution(source.source_id, source.content_sha256)
            for source in self.sources
        )


@dataclass(frozen=True, slots=True)
class ContractIngressResult:
    work_unit: WorkUnitRecord
    sources: tuple[EntitledSourceRecord, ...]
    revision: ContractRevisionRecord
    decision: AdmissionDecisionRecord


class ContractIngress:
    """Compose the existing source, Contract and admission authorities.

    ``required_effects`` is a required caller argument, including when empty.
    It is an exact grant, not a capability wish inferred by the proposer. The
    supported effect kinds and prerequisites are still decided by Closability.
    Callbacks return the existing typed Contract, not arbitrary model JSON.
    """

    def __init__(self, store: BroodlingStore) -> None:
        self.store = store

    def from_github(
        self,
        reference: WorkReference,
        propose: Callable[[ContractProposalInput], Contract],
        *,
        required_effects: tuple[RequiredEffect, ...],
        additional_sources: tuple[SourceSubmission, ...] = (),
        constructed_by: str = "model_extraction",
    ) -> ContractIngressResult:
        """Read just the explicitly named issue, then propose and admit.

        Comments, links and repository guidance are not fetched or entitled.
        Supplementary material needs an explicit caller/policy source grant.
        """
        acquired = acquire_github_issue(reference)
        return self.from_sources(
            acquired.reference,
            (acquired.source, *additional_sources),
            propose,
            required_effects=required_effects,
            constructed_by=constructed_by,
        )

    def from_sources(
        self,
        reference: WorkReference,
        sources: tuple[SourceSubmission, ...],
        propose: Callable[[ContractProposalInput], Contract],
        *,
        required_effects: tuple[RequiredEffect, ...],
        constructed_by: str = "model_extraction",
    ) -> ContractIngressResult:
        """Use explicitly supplied bytes without a GitHub/Markdown dependency.

        A caller with a structured Contract can return it from ``propose``.
        Acquisition/proposal errors raise without an admitted revision; any
        sources already captured remain immutable facts. Supported structure
        with unsupported capabilities is retained as a rejected revision.
        """
        if constructed_by not in CONSTRUCTED_BY:
            raise InvalidContractProposal("unrecognized proposal producer")
        _check_type(sources, tuple[SourceSubmission, ...], "sources")
        _check_type(required_effects, tuple[RequiredEffect, ...], "required_effects")
        if sum(source.kind == PRIMARY_ISSUE for source in sources) != 1:
            raise SourceNotEntitled(
                "ingress requires exactly one primary issue snapshot"
            )
        work_unit = self.store.resolve_work_unit(reference)
        captured = tuple(
            self.store.entitle_source(work_unit.work_unit_id, source)
            for source in sources
        )
        # Record the caller's authority independently of the model's proposal.
        # Pinning this source makes effect changes new revision meaning and keeps
        # the complete request authoritative even when criteria are summarized.
        authority = self.store.entitle_source(
            work_unit.work_unit_id,
            SourceSubmission(
                kind="caller_statement",
                locator=f"{work_unit.issue_locator}#broodling-ingress-authority",
                content=json.dumps(
                    {
                        "format": "broodling.ingress-authority/v1",
                        "sources": [
                            SourceAttribution(
                                s.source_id, s.content_sha256
                            ).to_mapping()
                            for s in captured
                        ],
                        "requiredEffects": [e.to_mapping() for e in required_effects],
                        "scope": (
                            "The complete entitled source snapshots govern this "
                            "Work Unit. Extracted criteria do not replace or "
                            "narrow the work request. Only the exact requiredEffects "
                            "declared here are authorized; "
                            "conflicting requirements must be handed back, not waived."
                        ),
                    },
                    sort_keys=True,
                    separators=(",", ":"),
                    ensure_ascii=False,
                ).encode("utf-8"),
                media_type="application/json",
                entitlement=SourceEntitlement(
                    "caller", "explicit ingress effect authority"
                ),
            ),
        )
        inputs = ContractProposalInput(
            work_unit, (*captured, authority), required_effects, constructed_by
        )
        proposal = propose(inputs)
        _validate_proposal(inputs, proposal)
        revision = self.store.record_contract_revision(proposal)
        decision = self.store.admit(revision.contract_revision_id)
        return ContractIngressResult(work_unit, inputs.sources, revision, decision)


def _validate_proposal(inputs: ContractProposalInput, proposal: Contract) -> None:
    _check_type(proposal, Contract, "proposal")
    if proposal.work_unit_id != inputs.work_unit.work_unit_id:
        raise InvalidContractProposal("proposal changed the Work Unit")
    if proposal.constructed_by != inputs.constructed_by:
        raise InvalidContractProposal("proposal changed its producer attribution")
    expected = set(inputs.source_attribution)
    if set(proposal.source_attribution) != expected or len(
        proposal.source_attribution
    ) != len(expected):
        raise SourceAttributionError(
            "proposal must pin every input snapshot exactly; "
            "it cannot add, omit or replace sources"
        )
    if proposal.required_effects != inputs.required_effects:
        raise InvalidContractProposal(
            "proposal changed the caller's exact effect authority"
        )
    for collection, id_field in (
        (proposal.criteria, "criterion_id"),
        (proposal.obligations, "obligation_id"),
        (proposal.prerequisites, "prerequisite_id"),
        (proposal.required_effects, "effect_id"),
    ):
        identities = [getattr(item, id_field) for item in collection]
        if len(identities) != len(set(identities)) or any(
            not identity.strip() for identity in identities
        ):
            raise InvalidContractProposal(
                f"{id_field} values must be nonempty and unique"
            )
        if any(not item.statement.strip() for item in collection):
            raise InvalidContractProposal("proposal statements must be nonempty")


def _check_type(value: object, expected: object, path: str) -> None:
    """Validate the existing domain dataclasses at the external proposal seam.

    Python annotations alone would let e.g. the string 'false' satisfy a boolean
    prerequisite. No new serialized requirements schema is introduced here.
    """
    origin = get_origin(expected)
    if origin is UnionType:
        for member in get_args(expected):
            try:
                _check_type(value, member, path)
                return
            except InvalidContractProposal:
                pass
    elif origin is tuple:
        if type(value) is tuple:
            for index, item in enumerate(value):
                _check_type(item, get_args(expected)[0], f"{path}[{index}]")
            return
    elif type(value) is expected:
        if is_dataclass(value):
            hints = get_type_hints(expected)
            for field in fields(value):
                _check_type(
                    getattr(value, field.name),
                    hints[field.name],
                    f"{path}.{field.name}",
                )
        return
    raise InvalidContractProposal(f"{path} does not match {expected}")
