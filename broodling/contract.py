"""The immutable Work Contract revision.

A Contract is a source-attributed statement of what the Work Unit must satisfy.
It is a *record*, not a filter: an obligation that V1 cannot support is
representable here verbatim so that Closability can reject it while preserving
it. Nothing in this module removes or weakens a stated obligation.

Canonical bytes are the durable identity of a revision. Two Contracts with the
same meaning produce the same bytes and therefore the same revision; a
meaning-changing replacement produces different bytes and therefore a new
revision.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any

from .identity import digest

CONTRACT_FORMAT = 1

#: How a Contract's structure was produced. Model extraction is permitted to
#: propose structure; it is recorded, and it never entitles a source.
CONSTRUCTED_BY = frozenset({"broodling_policy", "caller", "model_extraction"})


@dataclass(frozen=True, slots=True)
class SourceAttribution:
    """A pinned entitled source this Contract was built from."""

    source_id: str
    content_sha256: str

    def to_mapping(self) -> dict[str, Any]:
        return {"sourceId": self.source_id, "contentSha256": self.content_sha256}


@dataclass(frozen=True, slots=True)
class EvidencePopulation:
    """Optional validation guidance, preserved without a boundedness admission gate."""

    kind: str
    members: tuple[str, ...] = ()
    surface: str = ""

    def to_mapping(self) -> dict[str, Any]:
        return {
            "kind": self.kind,
            "members": list(self.members),
            "surface": self.surface,
        }


@dataclass(frozen=True, slots=True)
class MechanicalEvidence:
    """A frozen check declaration supplied to Zeroshot as task context.

    Retained for Contract compatibility. Broodling does not execute the argv or
    construct an independent evidence record; the standard native workflow owns
    implementation and verification.
    """

    argv: tuple[str, ...]
    cwd: str = "."
    materials: tuple[str, ...] = ()

    def to_mapping(self) -> dict[str, Any]:
        return {
            "argv": list(self.argv),
            "cwd": self.cwd,
            "materials": list(self.materials),
        }


@dataclass(frozen=True, slots=True)
class FinalAssuranceMaterial:
    """Historical repository-relative final-custody request.

    The current stable-result profile refuses new declarations because Zeroshot's
    delivery receipt, not a Broodling worktree read, is the accepted result.
    Keeping the type preserves exact historical Contract meaning.
    """

    path: str
    final_candidate: bool = True
    comparison_base: bool = False

    def to_mapping(self) -> dict[str, Any]:
        return {
            "path": self.path,
            "finalCandidate": self.final_candidate,
            "comparisonBase": self.comparison_base,
        }


@dataclass(frozen=True, slots=True)
class Criterion:
    """A required outcome with optional validation guidance for Zeroshot."""

    criterion_id: str
    statement: str
    evidence_population: EvidencePopulation | None = None
    validation_seam: str = ""
    validation_action: str = ""
    falsifying_observation: str = ""
    evidence_effect_dependencies: tuple[str, ...] = ()
    mechanical_evidence: MechanicalEvidence | None = None

    def to_mapping(self) -> dict[str, Any]:
        result = {
            "criterionId": self.criterion_id,
            "statement": self.statement,
            "validationSeam": self.validation_seam,
            "validationAction": self.validation_action,
            "falsifyingObservation": self.falsifying_observation,
            "evidenceEffectDependencies": list(self.evidence_effect_dependencies),
        }
        if self.evidence_population is not None:
            result["evidencePopulation"] = self.evidence_population.to_mapping()
        # Absence preserves the exact bytes and meaning of historical revisions.
        if self.mechanical_evidence is not None:
            result["mechanicalEvidence"] = self.mechanical_evidence.to_mapping()
        return result


@dataclass(frozen=True, slots=True)
class Obligation:
    """A stated obligation and its kind. Kinds outside the V1 profile are kept."""

    obligation_id: str
    statement: str
    kind: str

    def to_mapping(self) -> dict[str, Any]:
        return {
            "obligationId": self.obligation_id,
            "statement": self.statement,
            "kind": self.kind,
        }


@dataclass(frozen=True, slots=True)
class RequiredEffect:
    """An authoritative external effect the Contract requires.

    A pull-request effect may name its exact target branch. Other effects remain
    representable so admission can preserve and refuse them without weakening the
    Contract.
    """

    effect_id: str
    statement: str
    kind: str
    target_branch: str = ""

    def to_mapping(self) -> dict[str, Any]:
        result = {
            "effectId": self.effect_id,
            "statement": self.statement,
            "kind": self.kind,
        }
        # Omission preserves the exact canonical form of historical Contracts.
        if self.target_branch:
            result["targetBranch"] = self.target_branch
        return result


@dataclass(frozen=True, slots=True)
class Prerequisite:
    """A prerequisite the Contract needs inside the qualified V1 profile."""

    prerequisite_id: str
    statement: str
    satisfied_within_profile: bool

    def to_mapping(self) -> dict[str, Any]:
        return {
            "prerequisiteId": self.prerequisite_id,
            "statement": self.statement,
            "satisfiedWithinProfile": self.satisfied_within_profile,
        }


@dataclass(frozen=True, slots=True)
class Contract:
    """A complete Contract body, ready to be recorded as an immutable revision."""

    work_unit_id: str
    source_attribution: tuple[SourceAttribution, ...]
    criteria: tuple[Criterion, ...]
    obligations: tuple[Obligation, ...] = ()
    prerequisites: tuple[Prerequisite, ...] = ()
    required_effects: tuple[RequiredEffect, ...] = ()
    host_assumptions: tuple[str, ...] = ()
    constructed_by: str = "broodling_policy"
    notes: str = ""
    final_assurance_materials: tuple[FinalAssuranceMaterial, ...] | None = None

    def to_mapping(self) -> dict[str, Any]:
        result = {
            "contractFormat": CONTRACT_FORMAT,
            "workUnitId": self.work_unit_id,
            "constructedBy": self.constructed_by,
            "sourceAttribution": [
                item.to_mapping() for item in self.source_attribution
            ],
            "criteria": [item.to_mapping() for item in self.criteria],
            "obligations": [item.to_mapping() for item in self.obligations],
            "prerequisites": [item.to_mapping() for item in self.prerequisites],
            "requiredEffects": [item.to_mapping() for item in self.required_effects],
            "hostAssumptions": list(self.host_assumptions),
            "notes": self.notes,
        }
        # New selection is new revision meaning, never an amendment of old bytes.
        if self.final_assurance_materials is not None:
            result["finalAssuranceMaterials"] = [
                item.to_mapping() for item in self.final_assurance_materials
            ]
        return result

    def canonical_bytes(self) -> bytes:
        """Deterministic serialization; the durable meaning of the revision."""

        return json.dumps(
            self.to_mapping(),
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=False,
        ).encode("utf-8")

    @property
    def contract_sha256(self) -> str:
        return digest("broodling.contract.v1", self.canonical_bytes().decode("utf-8"))

    @property
    def contract_revision_id(self) -> str:
        """Durable revision id. Identical meaning re-resolves the same revision."""

        return "cr-" + digest(
            "broodling.contract-revision.v1", self.work_unit_id, self.contract_sha256
        )


def contract_from_mapping(mapping: dict[str, Any]) -> Contract:
    """Rebuild a Contract from its canonical mapping.

    Used to read a stored revision back without a second source of truth for the
    field names.
    """

    if mapping.get("contractFormat") != CONTRACT_FORMAT:
        raise ValueError(
            f"unsupported contract format {mapping.get('contractFormat')!r}"
        )
    return Contract(
        work_unit_id=mapping["workUnitId"],
        constructed_by=mapping["constructedBy"],
        source_attribution=tuple(
            SourceAttribution(item["sourceId"], item["contentSha256"])
            for item in mapping["sourceAttribution"]
        ),
        criteria=tuple(
            Criterion(
                criterion_id=item["criterionId"],
                statement=item["statement"],
                evidence_population=(
                    EvidencePopulation(
                        kind=item["evidencePopulation"]["kind"],
                        members=tuple(item["evidencePopulation"]["members"]),
                        surface=item["evidencePopulation"]["surface"],
                    )
                    if "evidencePopulation" in item
                    else None
                ),
                validation_seam=item["validationSeam"],
                validation_action=item["validationAction"],
                falsifying_observation=item["falsifyingObservation"],
                evidence_effect_dependencies=tuple(item["evidenceEffectDependencies"]),
                mechanical_evidence=(
                    _mechanical_from_mapping(item["mechanicalEvidence"])
                    if "mechanicalEvidence" in item
                    else None
                ),
            )
            for item in mapping["criteria"]
        ),
        obligations=tuple(
            Obligation(item["obligationId"], item["statement"], item["kind"])
            for item in mapping["obligations"]
        ),
        prerequisites=tuple(
            Prerequisite(
                item["prerequisiteId"],
                item["statement"],
                item["satisfiedWithinProfile"],
            )
            for item in mapping["prerequisites"]
        ),
        required_effects=tuple(
            RequiredEffect(
                item["effectId"],
                item["statement"],
                item["kind"],
                item.get("targetBranch", ""),
            )
            for item in mapping["requiredEffects"]
        ),
        host_assumptions=tuple(mapping["hostAssumptions"]),
        notes=mapping["notes"],
        final_assurance_materials=(
            _final_materials_from_mapping(mapping["finalAssuranceMaterials"])
            if "finalAssuranceMaterials" in mapping
            else None
        ),
    )


def _final_materials_from_mapping(value: Any) -> tuple[FinalAssuranceMaterial, ...]:
    if not isinstance(value, list):
        raise ValueError("finalAssuranceMaterials must be an array")  # noqa: TRY004
    result = []
    for item in value:
        if not isinstance(item, dict) or set(item) != {
            "path",
            "finalCandidate",
            "comparisonBase",
        }:
            raise ValueError(
                "final assurance material requires exactly path, finalCandidate "
                "and comparisonBase"
            )
        result.append(
            FinalAssuranceMaterial(
                item["path"], item["finalCandidate"], item["comparisonBase"]
            )
        )
    return tuple(result)


def _mechanical_from_mapping(mapping: Any) -> MechanicalEvidence:
    """Refuse malformed structured meaning instead of dropping unknown fields."""

    if not isinstance(mapping, dict) or set(mapping) != {"argv", "cwd", "materials"}:
        raise ValueError("mechanicalEvidence requires exactly argv, cwd and materials")
    if not isinstance(mapping["argv"], list) or not isinstance(
        mapping["materials"], list
    ):
        # Invalid serialized Contract values use one fail-closed error surface.
        raise ValueError("mechanicalEvidence argv and materials must be arrays")  # noqa: TRY004
    return MechanicalEvidence(
        tuple(mapping["argv"]), mapping["cwd"], tuple(mapping["materials"])
    )
