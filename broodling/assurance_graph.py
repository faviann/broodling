"""Product-owned V1 assurance protocol, executed entirely by Zeroshot.

Candidate identity is the admitted B1/C0 followed by successful graph-authorized
mutation occurrences in runtime order. No worker-supplied ordinal is bound or
used. Every implement/repair route requires new evidence and assurance before
the distinct final assessor. This module builds a definition; it neither observes
occurrences nor implements execution, validation, routing or session lifecycle.

The W3 topology and three-repair bound are retained. Issue #17's explicit boundary
decision removes worker generation labels, guards unusable round controls, and
adds narrow semantic findings/directive bindings. Historical qualification graphs
remain evidence, not production dependencies.
"""

from __future__ import annotations

from .reviewer import REVIEW_INSTRUCTIONS

REPAIR_BOUND = 3
# W4's finite real-provider bound replaces the controlled W3 leaf's 250 ms.
NODE_TIMEOUT_MS = 300_000
EXECUTABLE_NODES = (
    "implement",
    "initial_evidence_check",
    "initial_review",
    "adjudicate_authority",
    "repair",
    "repair_evidence_check",
    "repair_review",
    "resolution_authority",
    "round_complete",
    "final_assessment_authority_clean",
    "final_assessment_authority_repaired",
)


def _record(**fields: dict) -> dict:
    return {
        "kind": "record",
        "fields": {
            key: {"required": True, "type": value} for key, value in fields.items()
        },
    }


def _enum(*values: str) -> dict:
    return {"kind": "enum", "values": list(values)}


def _state() -> dict:
    return _record(
        contract={"kind": "string"},
        evidence=_enum("unchecked", "valid", "missing"),
        findings=_enum("unexecuted", "clean", "found"),
        obligation=_enum("none", "open_d1", "resolved_d1"),
        findingContent={"kind": "string"},
        directiveContent=_record(
            directive={"kind": "string"}, correction={"kind": "string"}
        ),
    )


def initial_state(contract: str) -> dict:
    """Initial graph inputs at B1/C0; frozen Contract supplied by admission."""
    return {
        "contract": contract,
        "evidence": "unchecked",
        "findings": "unexecuted",
        "obligation": "none",
        "findingContent": "",
        "directiveContent": {"directive": "", "correction": ""},
    }


def _paths() -> list[list[str]]:
    return [[key] for key in _state()["fields"]]


def _group(kind: str, name: str, **body) -> dict:
    return {
        "kind": kind,
        "name": name,
        "state": _state(),
        "promotedStatePaths": _paths(),
        **body,
    }


def _seq(name: str, *children: dict) -> dict:
    return _group("seq", name, children=list(children))


def _signal(node: str, signal: str, *labels: str) -> dict:
    return {
        "kind": "in",
        "value": {"name": node, "source": "signal", "field": signal},
        "labels": list(labels),
    }


def _error(node: str) -> dict:
    return {
        "kind": "in",
        "value": {"name": node, "source": "error", "field": None},
        "labels": ["timeout", "crash", "malformed", "refusal"],
    }


def _fail(name: str, reason: str = "execution_unusable") -> dict:
    return {"kind": "fail", "name": name, "reason": reason}


def _choice(name: str, branches: list[tuple[dict, dict]], otherwise: dict) -> dict:
    return _group(
        "choice",
        name,
        branches=[{"when": guard, "node": node} for guard, node in branches],
        otherwise=otherwise,
    )


def _leaf(
    name: str,
    mapping: dict[str, str],
    *,
    instructions: str,
    signal: tuple[str, list[str], str | None] | None = None,
    payload: str | None = None,
) -> dict:
    """Declare typed public SDK contracts; the runtime validates all responses."""
    fields = _state()["fields"]
    node = {
        "kind": "step" if signal is None else "verifier",
        "name": name,
        "worker": f"agent.broodling-{name.replace('_', '-')}@1",
        "instructions": f"BROODLING_NODE={name}. {instructions}",
        "input": {
            "kind": "record",
            "fields": {target: fields[source] for target, source in mapping.items()},
        },
        "inputBindings": [
            {"target": [target], "value": {"source": "state", "path": [source]}}
            for target, source in mapping.items()
        ],
        "output": {"kind": "null"}
        if payload is None
        else {"kind": "record", "fields": {payload: fields[payload]}},
        "writeBindings": [],
        "timeoutMs": NODE_TIMEOUT_MS,
        "attempts": 1,
    }
    if signal is not None:
        key, labels, target = signal
        node["signals"] = {key: labels}
        node["diagnostic"] = {
            "kind": "record",
            "fields": {
                key: {"required": False, "type": {"kind": "string"}}
                for key in (
                    "contractId",
                    "sourceId",
                    "evidenceId",
                    "predecessorId",
                    "note",
                )
            },
        }
        if target is not None:
            node["writeBindings"].append(
                {
                    "value": {"node": name, "channel": "signal", "path": [key]},
                    "target": [target],
                }
            )
    if payload is not None:
        node["writeBindings"].append(
            {
                "value": {"node": name, "channel": "out", "path": [payload]},
                "target": [payload],
            }
        )
    return node


def _same(*keys: str) -> dict[str, str]:
    return {key: key for key in keys}


def _final(suffix: str) -> dict:
    name = f"final_assessment_authority_{suffix}"
    assessor = _leaf(
        name,
        _same(*_state()["fields"]),
        instructions="Read-only designated final semantic authority. Assess the complete frozen Contract "
        "against the stable current candidate, available required evidence and applicable directives. "
        "Clean review alone is insufficient. Signal gap for insufficient evidence or unresolved correction, "
        "refused for an authority question, and accepted only for criterion-level semantic satisfaction.",
        signal=("assessment", ["accepted", "gap", "refused"], None),
    )
    accepted = {
        "kind": "succeed",
        "name": f"semantic_acceptance_{suffix}",
        "output": _state(),
        "bindings": [
            {"target": path, "value": {"source": "state", "path": path}}
            for path in _paths()
        ],
    }
    return _seq(
        f"final_semantic_assessment_{suffix}",
        assessor,
        _choice(
            f"final_route_{suffix}",
            [
                (_error(name), _fail(f"final_unusable_{suffix}")),
                (
                    _signal(name, "assessment", "gap"),
                    _fail(f"final_gap_{suffix}", "semantic_gap"),
                ),
                (
                    _signal(name, "assessment", "refused"),
                    _fail(f"final_refused_{suffix}", "authority_gap"),
                ),
            ],
            accepted,
        ),
    )


def _assurance(repaired: bool, continuation: dict) -> dict:
    prefix = "repair" if repaired else "initial"
    evidence_name, review_name = f"{prefix}_evidence_check", f"{prefix}_review"
    evidence = _leaf(
        evidence_name,
        _same("contract"),
        instructions="Read-only required-evidence role. Check required material for the current stable "
        "candidate under the frozen Contract; missing required raw material must signal missing.",
        signal=("availability", ["valid", "missing"], "evidence"),
    )
    review = _leaf(
        review_name,
        _same("contract", "evidence"),
        instructions=REVIEW_INSTRUCTIONS,
        signal=("findings", ["clean", "found"], "findings"),
        payload="findingContent",
    )
    review_route = _choice(
        f"{prefix}_review_route",
        [
            (_error(review_name), _fail(f"{prefix}_review_unusable")),
        ],
        continuation,
    )
    return _seq(
        "renewed_evidence_and_assurance"
        if repaired
        else "initial_evidence_and_assurance",
        evidence,
        _choice(
            f"{prefix}_evidence_route",
            [
                (_error(evidence_name), _fail(f"{prefix}_evidence_unusable")),
                (
                    _signal(evidence_name, "availability", "missing"),
                    _fail(f"{prefix}_evidence_missing", "required_evidence_missing"),
                ),
            ],
            _seq(
                "fresh_review_and_resolution"
                if repaired
                else "initial_review_and_adjudication",
                review,
                review_route,
            ),
        ),
    )


def _authority_inputs() -> dict[str, str]:
    return {
        **_same(
            "contract", "evidence", "findings", "findingContent", "directiveContent"
        ),
        "outstanding": "obligation",
    }


def _repair_loop() -> dict:
    resolution = _leaf(
        "resolution_authority",
        _authority_inputs(),
        instructions="Read-only designated resolution authority. Assess fresh findings, current candidate "
        "and evidence against the outstanding adjudicated directive and correction. A clean review or "
        "worker claim does not resolve it. Signal resolved_d1 only on explicit supported resolution; "
        "otherwise retain open_d1. The directive payload persists unchanged through the loop.",
        signal=("resolution", ["open_d1", "resolved_d1"], "obligation"),
    )
    control = _leaf(
        "round_complete",
        {},
        instructions="Read-only required round control. Signal completed.",
        signal=("status", ["completed"], None),
    )
    resolution_flow = _seq(
        "repair_resolution",
        resolution,
        _choice(
            "resolution_route",
            [
                (_error("resolution_authority"), _fail("resolution_unusable")),
            ],
            control,
        ),
    )
    repair = _leaf(
        "repair",
        {
            "contract": "contract",
            "directive": "obligation",
            "directiveContent": "directiveContent",
        },
        instructions="Graph-authorized candidate mutation. Repair the current candidate using only the "
        "frozen Contract and adjudicated directive/correction content. Do not treat raw/rejected findings "
        "as directives or discharge your own obligation. Return null on completed mutation.",
    )
    body = _seq(
        "repair_round",
        repair,
        _choice(
            "repair_execution_route",
            [
                (_error("repair"), _fail("repair_unusable")),
            ],
            _assurance(True, resolution_flow),
        ),
    )
    return _group(
        "loop",
        "bounded_repair",
        body=body,
        maxIterations=REPAIR_BOUND,
        until={
            "kind": "any",
            "guards": [
                _signal("resolution_authority", "resolution", "resolved_d1"),
                _error("round_complete"),
            ],
        },
    )


def assurance_graph() -> dict:
    """Return a fresh product GraphSpec definition, with no caller graph seam."""
    adjudicator = _leaf(
        "adjudicate_authority",
        _authority_inputs(),
        instructions="Read-only designated adjudication authority. Assess actual findingContent against "
        "the frozen Contract, stable candidate and required evidence. Signal open_d1 only with the "
        "applicable directive and correction content in directiveContent; signal none only with no "
        "unresolved directive. Payload text and diagnostic identifiers do not confer routing authority.",
        signal=("decision", ["none", "open_d1"], "obligation"),
        payload="directiveContent",
    )
    after_repair = _choice(
        "post_repair_bound_route",
        [
            (_error("round_complete"), _fail("round_complete_unusable")),
            (
                _signal("resolution_authority", "resolution", "open_d1"),
                _fail("obligations_exhausted", "obligations_exhausted"),
            ),
        ],
        _final("repaired"),
    )
    adjudication = _seq(
        "initial_adjudication",
        adjudicator,
        _choice(
            "adjudication_route",
            [
                (_error("adjudicate_authority"), _fail("adjudication_unusable")),
                (_signal("adjudicate_authority", "decision", "none"), _final("clean")),
            ],
            _seq("repair_then_final", _repair_loop(), after_repair),
        ),
    )
    implement = _leaf(
        "implement",
        _same("contract"),
        instructions="Graph-authorized candidate mutation from admitted B1/C0. Implement the frozen "
        "Contract in the current candidate workspace. Return null on completed mutation; worker labels "
        "and claims confer no candidate identity or semantic authority.",
    )
    root = _seq(
        "broodling_v1_assurance",
        implement,
        _choice(
            "implementation_route",
            [
                (_error("implement"), _fail("implementation_unusable")),
            ],
            _assurance(False, adjudication),
        ),
    )
    root["promotedStatePaths"] = []
    return {
        "profile": "openengine.graph.full/v1",
        "initialInput": _state(),
        "policy": {"policy": "policy.native-v2@1", "default": "deny"},
        "root": root,
    }


def assurance_runtime() -> dict:
    """Execution-scoped agents; step/verifier kinds select qualified sandbox modes."""
    return {
        "harness": "codex",
        "provider": "openai",
        "size": "small",
        "nodes": {
            name: {
                "kind": "agent",
                "model": "gpt-5.6-sol",
                "effort": "low",
                "sessionScope": "execution",
                "connections": {
                    "profile": [
                        "BROODLING_REAL_CODEX",
                        "BROODLING_PROFILE_HOME",
                        "BROODLING_ISOLATED_CODEX_HOME",
                    ]
                },
            }
            for name in EXECUTABLE_NODES
        },
    }
