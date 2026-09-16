"""Frozen Work Unit authorization selects one supported Zeroshot delivery."""

from __future__ import annotations

from dataclasses import dataclass

from .contract import Contract

NONE = "none"
PULL_REQUEST = "pull_request"


@dataclass(frozen=True, slots=True)
class DeliveryAuthorization:
    mode: str
    target_branch: str | None = None


def authorization(contract: Contract) -> DeliveryAuthorization:
    """Return the exact supported delivery, or reject unsupported authority."""
    effects = contract.required_effects
    if not effects:
        return DeliveryAuthorization(NONE)
    if (
        len(effects) == 1
        and effects[0].kind == PULL_REQUEST
        and isinstance(effects[0].target_branch, str)
        and effects[0].target_branch.strip()
    ):
        return DeliveryAuthorization(PULL_REQUEST, effects[0].target_branch)
    raise ValueError("Contract does not authorize one supported result delivery")
