"""Serialization and declaration enumeration shared with qualification records.

This module does not run an assurance campaign. The retired #17 engine controls
are reproducible from their historical Git revisions, not rerun as Broodling
requirements. Enumerating declared leaves makes no claim about execution order.
"""

import dataclasses
import hashlib
import json


def canonical_hash(value):
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()


def jsonable(value):
    if dataclasses.is_dataclass(value):
        return {
            field.name: jsonable(getattr(value, field.name))
            for field in dataclasses.fields(value)
        }
    if isinstance(value, tuple):
        return [jsonable(item) for item in value]
    return value


def executable_nodes(node):
    if node["kind"] in {"step", "verifier"}:
        yield node
    elif node["kind"] == "seq":
        for child in node["children"]:
            yield from executable_nodes(child)
    elif node["kind"] == "choice":
        for branch in node["branches"]:
            yield from executable_nodes(branch["node"])
        if node.get("otherwise"):
            yield from executable_nodes(node["otherwise"])
    elif node["kind"] == "loop":
        yield from executable_nodes(node["body"])
