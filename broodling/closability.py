"""V1 Closability and admission decision.

Closability asks whether this Contract can be carried to a definite outcome
*inside the qualified V1 profile*: single-host, one Attempt owning one dedicated
disposable worktree, read-only assurance, and no authoritative external effects.

Every refusal carries the obligation that caused it, verbatim. Admission never
edits a Contract to make it admissible; an unsupported obligation is a
non-admission and a handback, not a deletion.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .contract import Contract, Criterion
from .profile import SUPPORTED_HOST_ASSUMPTIONS

# Rejection codes.
NO_CRITERIA = "no_criteria"
NO_SOURCE_ATTRIBUTION = "no_entitled_source_attribution"
REQUIRED_EFFECT_PRESENT = "required_effect_present"
UNSUPPORTED_EXTERNAL_OBLIGATION = "unsupported_external_obligation"
UNRECOGNIZED_OBLIGATION_KIND = "unrecognized_obligation_kind"
EFFECT_DEPENDENT_EVIDENCE = "effect_dependent_evidence"
MISSING_EVIDENCE_POPULATION = "missing_finite_evidence_population"
MISSING_VALIDATION_SEAM = "missing_validation_seam"
MISSING_VALIDATION_ACTION = "missing_validation_action"
MISSING_FALSIFYING_OBSERVATION = "missing_falsifying_observation"
UNSATISFIED_PREREQUISITE = "unsatisfied_prerequisite"
UNSUPPORTED_HOST_ASSUMPTION = "unsupported_host_assumption"

#: Obligation kinds a V1 Contract may carry. Everything else is either an
#: external/publication obligation V1 cannot execute or an unrecognized kind;
#: both fail closed.
SUPPORTED_OBLIGATION_KINDS: frozenset[str] = frozenset(
    {"candidate_change", "local_validation"}
)

#: Recognized-but-unsupported obligation kinds, named so the refusal says which
#: external authority the Contract actually asked for.
EXTERNAL_OBLIGATION_KINDS: frozenset[str] = frozenset(
    {
        "commit_as_delivery",
        "push",
        "pull_request",
        "merge",
        "issue_mutation",
        "publication",
        "deployment",
        "external_effect",
    }
)

#: Evidence-population kinds that can bound a criterion.
FINITE_POPULATION_KINDS: frozenset[str] = frozenset({"enumerated", "declared_surface"})

ADMITTED = "admitted"
REJECTED = "rejected"


@dataclass(frozen=True, slots=True)
class ClosabilityFinding:
    """One blocking finding, carrying the preserved obligation that caused it."""

    code: str
    subject: str
    preserved_obligation: str
    detail: str

    def to_mapping(self) -> dict[str, Any]:
        return {
            "code": self.code,
            "subject": self.subject,
            "preservedObligation": self.preserved_obligation,
            "detail": self.detail,
        }


@dataclass(frozen=True, slots=True)
class ClosabilityAssessment:
    """The deterministic outcome of assessing one Contract."""

    admissible: bool
    findings: tuple[ClosabilityFinding, ...]

    @property
    def outcome(self) -> str:
        return ADMITTED if self.admissible else REJECTED

    def to_mapping(self) -> dict[str, Any]:
        return {
            "outcome": self.outcome,
            "findings": [finding.to_mapping() for finding in self.findings],
        }


def _population_is_bounded(criterion: Criterion) -> bool:
    population = criterion.evidence_population
    if population.kind not in FINITE_POPULATION_KINDS:
        return False
    if population.kind == "enumerated":
        return bool(population.members)
    return bool(population.surface.strip())


def assess(contract: Contract) -> ClosabilityAssessment:
    """Assess one Contract against the qualified V1 profile.

    Pure and deterministic: the same Contract always yields the same findings in
    the same order, so a re-decision can never quietly differ from the recorded
    one.
    """

    findings: list[ClosabilityFinding] = []

    if not contract.source_attribution:
        findings.append(
            ClosabilityFinding(
                code=NO_SOURCE_ATTRIBUTION,
                subject="contract",
                preserved_obligation="",
                detail="a Contract must attribute at least one entitled source",
            )
        )

    for effect in contract.required_effects:
        findings.append(
            ClosabilityFinding(
                code=REQUIRED_EFFECT_PRESENT,
                subject=f"requiredEffect:{effect.effect_id}",
                preserved_obligation=effect.statement,
                detail=(
                    f"V1 executes no authoritative external effects; required effect "
                    f"of kind {effect.kind!r} is unsupported and is preserved, not waived"
                ),
            )
        )

    for obligation in contract.obligations:
        if obligation.kind in SUPPORTED_OBLIGATION_KINDS:
            continue
        if obligation.kind in EXTERNAL_OBLIGATION_KINDS:
            findings.append(
                ClosabilityFinding(
                    code=UNSUPPORTED_EXTERNAL_OBLIGATION,
                    subject=f"obligation:{obligation.obligation_id}",
                    preserved_obligation=obligation.statement,
                    detail=(
                        f"obligation kind {obligation.kind!r} requires external "
                        "authority that V1 does not execute"
                    ),
                )
            )
        else:
            findings.append(
                ClosabilityFinding(
                    code=UNRECOGNIZED_OBLIGATION_KIND,
                    subject=f"obligation:{obligation.obligation_id}",
                    preserved_obligation=obligation.statement,
                    detail=(
                        f"obligation kind {obligation.kind!r} is not a supported V1 "
                        "kind; an unclassified obligation cannot be assumed local"
                    ),
                )
            )

    if not contract.criteria:
        findings.append(
            ClosabilityFinding(
                code=NO_CRITERIA,
                subject="contract",
                preserved_obligation="",
                detail="a Contract with no criterion has nothing to close against",
            )
        )

    for criterion in contract.criteria:
        subject = f"criterion:{criterion.criterion_id}"
        if not _population_is_bounded(criterion):
            findings.append(
                ClosabilityFinding(
                    code=MISSING_EVIDENCE_POPULATION,
                    subject=subject,
                    preserved_obligation=criterion.statement,
                    detail=(
                        "criterion declares neither a finite evidence population nor "
                        f"a validation surface (population kind "
                        f"{criterion.evidence_population.kind!r})"
                    ),
                )
            )
        if not criterion.validation_seam.strip():
            findings.append(
                ClosabilityFinding(
                    code=MISSING_VALIDATION_SEAM,
                    subject=subject,
                    preserved_obligation=criterion.statement,
                    detail="criterion declares no available validation seam",
                )
            )
        if not criterion.validation_action.strip():
            findings.append(
                ClosabilityFinding(
                    code=MISSING_VALIDATION_ACTION,
                    subject=subject,
                    preserved_obligation=criterion.statement,
                    detail="criterion declares no executable validation action",
                )
            )
        if not criterion.falsifying_observation.strip():
            findings.append(
                ClosabilityFinding(
                    code=MISSING_FALSIFYING_OBSERVATION,
                    subject=subject,
                    preserved_obligation=criterion.statement,
                    detail="criterion declares no falsifying observation",
                )
            )
        for dependency in criterion.evidence_effect_dependencies:
            findings.append(
                ClosabilityFinding(
                    code=EFFECT_DEPENDENT_EVIDENCE,
                    subject=subject,
                    preserved_obligation=criterion.statement,
                    detail=(
                        f"criterion evidence depends on effect {dependency!r}, which "
                        "V1 cannot execute or observe"
                    ),
                )
            )

    for prerequisite in contract.prerequisites:
        if prerequisite.satisfied_within_profile:
            continue
        findings.append(
            ClosabilityFinding(
                code=UNSATISFIED_PREREQUISITE,
                subject=f"prerequisite:{prerequisite.prerequisite_id}",
                preserved_obligation=prerequisite.statement,
                detail=(
                    "prerequisite is not satisfied inside the qualified V1 profile; "
                    "Broodling identifies it and hands back rather than waiting"
                ),
            )
        )

    for assumption in contract.host_assumptions:
        if assumption in SUPPORTED_HOST_ASSUMPTIONS:
            continue
        findings.append(
            ClosabilityFinding(
                code=UNSUPPORTED_HOST_ASSUMPTION,
                subject=f"hostAssumption:{assumption}",
                preserved_obligation=assumption,
                detail=(
                    f"host/runtime assumption {assumption!r} is outside the qualified "
                    "single-host, one-Attempt/one-dedicated-worktree, no-effect profile"
                ),
            )
        )

    return ClosabilityAssessment(admissible=not findings, findings=tuple(findings))
