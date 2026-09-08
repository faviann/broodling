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
from pathlib import PurePosixPath
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
    """The finite population, or declared validation surface, for a criterion.

    ``kind`` is deliberately an open string: a Contract that declares an
    unbounded population must be storable so that admission can reject it with
    the declaration intact.
    """

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
    """One explicitly admitted local check and its required raw material.

    ``argv`` is executed literally, without shell-string interpretation. Its
    executable is an absolute path. ``cwd`` and each required material are
    normalized worktree-relative paths; material paths are relative to the
    worktree root, independently of ``cwd``. Standard output, standard error
    and the exit status are always mechanical observations. Required material
    consists of the exact bytes of existing files, not paths inferred from a
    criterion's free-form population or validation descriptions.

    This declaration confers no semantic sufficiency or acceptance judgment.
    Unsupported declarations remain representable, but P3 preparation refuses
    them before any check runs.
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
class Criterion:
    """One Contract criterion and its V1 Closability fields."""

    criterion_id: str
    statement: str
    evidence_population: EvidencePopulation
    validation_seam: str = ""
    validation_action: str = ""
    falsifying_observation: str = ""
    evidence_effect_dependencies: tuple[str, ...] = ()
    mechanical_evidence: MechanicalEvidence | None = None

    def to_mapping(self) -> dict[str, Any]:
        result = {
            "criterionId": self.criterion_id,
            "statement": self.statement,
            "evidencePopulation": self.evidence_population.to_mapping(),
            "validationSeam": self.validation_seam,
            "validationAction": self.validation_action,
            "falsifyingObservation": self.falsifying_observation,
            "evidenceEffectDependencies": list(self.evidence_effect_dependencies),
        }
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

    V1's required-effect set is empty. A Contract that names one is rejected, not
    admitted with the effect dropped.
    """

    effect_id: str
    statement: str
    kind: str

    def to_mapping(self) -> dict[str, Any]:
        return {
            "effectId": self.effect_id,
            "statement": self.statement,
            "kind": self.kind,
        }


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

    def to_mapping(self) -> dict[str, Any]:
        return {
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
                evidence_population=EvidencePopulation(
                    kind=item["evidencePopulation"]["kind"],
                    members=tuple(item["evidencePopulation"]["members"]),
                    surface=item["evidencePopulation"]["surface"],
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
            RequiredEffect(item["effectId"], item["statement"], item["kind"])
            for item in mapping["requiredEffects"]
        ),
        host_assumptions=tuple(mapping["hostAssumptions"]),
        notes=mapping["notes"],
    )


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


def _relative_path(value: Any, *, directory: bool = False) -> bool:
    if not isinstance(value, str) or not value or "\x00" in value:
        return False
    path = PurePosixPath(value)
    return (
        not path.is_absolute()
        and ".." not in path.parts
        and str(path) == value
        and (directory or value != ".")
    )


def validate_mechanical_evidence(contract: Contract) -> None:
    """Require explicit supported evidence inputs for P3, without changing P2.

    No validation action, seam or population string supplies execution semantics.
    Filesystem containment and availability are checked by the evidence leaf at
    the current graph occurrence; this check only validates the frozen shape.
    """

    if not contract.criteria:
        raise ValueError("runnable assurance requires at least one criterion")
    for criterion in contract.criteria:
        prefix = f"criterion {criterion.criterion_id!r} mechanical evidence"
        declaration = criterion.mechanical_evidence
        if not isinstance(declaration, MechanicalEvidence):
            raise ValueError(f"{prefix} requires an explicit declaration")  # noqa: TRY004
        if (
            not isinstance(declaration.argv, tuple)
            or not declaration.argv
            or any(
                not isinstance(arg, str) or "\x00" in arg for arg in declaration.argv
            )
            or not PurePosixPath(declaration.argv[0]).is_absolute()
        ):
            raise ValueError(
                f"{prefix} requires literal argv with an absolute executable"
            )
        if not _relative_path(declaration.cwd, directory=True):
            raise ValueError(
                f"{prefix} cwd must be a normalized worktree-relative path"
            )
        if not isinstance(declaration.materials, tuple) or any(
            not _relative_path(path) for path in declaration.materials
        ):
            raise ValueError(
                f"{prefix} materials must be normalized worktree-relative files"
            )
