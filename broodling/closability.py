"""V1 Closability and admission decision.

Closability asks whether this Contract can be carried to a definite outcome
*inside the supported profile*: single-host, one Attempt owning one dedicated
worktree, the native software-change workflow, and at most one explicitly
authorized pull-request delivery.

Every refusal carries the obligation that caused it, verbatim. Admission never
edits a Contract to make it admissible; an unsupported obligation is a
non-admission and a handback, not a deletion.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .contract import Contract
from .delivery import PULL_REQUEST, authorization
from .profile import SUPPORTED_HOST_ASSUMPTIONS

# Rejection codes.
NO_CRITERIA = "no_criteria"
NO_SOURCE_ATTRIBUTION = "no_entitled_source_attribution"
UNSUPPORTED_REQUIRED_EFFECT = "unsupported_required_effect"
UNSUPPORTED_DELIVERY_HOST = "unsupported_delivery_host"
UNSUPPORTED_EXTERNAL_OBLIGATION = "unsupported_external_obligation"
UNRECOGNIZED_OBLIGATION_KIND = "unrecognized_obligation_kind"
EFFECT_DEPENDENT_EVIDENCE = "effect_dependent_evidence"
UNSUPPORTED_FINAL_MATERIAL_SELECTION = "unsupported_final_material_selection"
UNSATISFIED_PREREQUISITE = "unsatisfied_prerequisite"
UNSUPPORTED_HOST_ASSUMPTION = "unsupported_host_assumption"

#: Obligation kinds a V1 Contract may carry. Everything else is either an
#: external/publication obligation the profile cannot execute or an unrecognized kind;
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


def assess(contract: Contract, *, work_unit_host: str) -> ClosabilityAssessment:
    """Assess one Contract against the supported conditional-delivery profile.

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

    try:
        delivery = authorization(contract)
    except ValueError:
        delivery = None
        for effect in contract.required_effects:
            findings.append(
                ClosabilityFinding(
                    code=UNSUPPORTED_REQUIRED_EFFECT,
                    subject=f"requiredEffect:{effect.effect_id}",
                    preserved_obligation=effect.statement,
                    detail=(
                        "the supported result deliveries are no effect, or exactly "
                        "one pull_request effect naming a target branch; effect "
                        f"{effect.kind!r} is preserved, not widened or waived"
                    ),
                )
            )

    if (
        delivery is not None
        and delivery.mode == PULL_REQUEST
        and work_unit_host != "github.com"
    ):
        findings.append(
            ClosabilityFinding(
                code=UNSUPPORTED_DELIVERY_HOST,
                subject=f"workUnitHost:{work_unit_host}",
                preserved_obligation=contract.required_effects[0].statement,
                detail=(
                    "Zeroshot 10.3 pull-request delivery is GitHub-specific; "
                    f"the Work Unit belongs to {work_unit_host!r}"
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

    if contract.final_assurance_materials is not None:
        findings.append(
            ClosabilityFinding(
                code=UNSUPPORTED_FINAL_MATERIAL_SELECTION,
                subject="finalAssuranceMaterials",
                preserved_obligation="retain selected final candidate material",
                detail=(
                    "the supported result is Zeroshot's immutable delivery receipt; "
                    "Broodling does not reconstruct selected files from execution state"
                ),
            )
        )

    for criterion in contract.criteria:
        subject = f"criterion:{criterion.criterion_id}"
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
        if (
            delivery is not None
            and delivery.mode == PULL_REQUEST
            and assumption in {"no_authoritative_effects", "local_filesystem_only"}
        ):
            findings.append(
                ClosabilityFinding(
                    code=UNSUPPORTED_HOST_ASSUMPTION,
                    subject=f"hostAssumption:{assumption}",
                    preserved_obligation=assumption,
                    detail=(
                        f"host/runtime assumption {assumption!r} contradicts the "
                        "authorized pull-request delivery"
                    ),
                )
            )
            continue
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
