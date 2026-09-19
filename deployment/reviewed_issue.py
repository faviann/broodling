"""Example proposer for a self-contained, operator-reviewed PR-only request.

Copy this file outside the source checkout alongside an exact GitHub issue
response with the same stem and .json suffix. Review that issue before submit:
all prerequisites must already be met and its only external effect must be the
explicitly selected PR. For requests needing structured obligations or
prerequisites, supply a different typed proposer using the existing ingress API.
"""

import json
from pathlib import Path

from broodling import Contract, Criterion, InvalidContractProposal


def propose(inputs):
    primary, = inputs.sources
    reviewed = Path(__file__).with_suffix(".json").read_bytes()
    if primary.content != reviewed:
        raise InvalidContractProposal("issue changed since operator review; review the current snapshot")
    issue = json.loads(reviewed)
    statement = issue["title"] + "\n\n" + (issue["body"] or "")
    return Contract(
        work_unit_id=inputs.work_unit.work_unit_id,
        source_attribution=inputs.source_attribution,
        criteria=(Criterion("reviewed-request", statement),),
        required_effects=inputs.required_effects,
        constructed_by=inputs.constructed_by,
    )
