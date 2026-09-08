"""Collect only frozen, explicitly selected final candidate and B1 material.

The caller establishes the admitted Attempt, B1 identity and graph's stable final
interval. This collector establishes neither candidate applicability nor semantic
sufficiency, and retains no undeclared repository material.
"""

from __future__ import annotations

import base64
import os
import stat
from pathlib import Path
from typing import Any

from . import git
from .contract import Contract, validate_final_assurance_materials


def collect_final_material(
    contract: Contract, worktree: Path, comparison_base_commit: str
) -> tuple[dict[str, Any], ...]:
    """Retain bytes or explicit absence for every declared path and state.

    Literal path components are used throughout. Symlink leaves retain their
    target bytes; symlink ancestors, directories and special files are refused.
    Git material comes from the immutable B1 object graph, never live HEAD.
    Permission errors and unavailable objects propagate rather than becoming
    absence. Filesystem reads require the caller's existing stable interval.
    """
    validate_final_assurance_materials(contract)
    materials = contract.final_assurance_materials
    assert materials is not None
    root_tree = None
    if any(item.comparison_base for item in materials):
        commit = git.read_object(worktree, comparison_base_commit, "commit")
        header = commit.split(b"\n", 1)[0]
        if not header.startswith(b"tree "):
            raise ValueError("B1 commit has no root tree")
        root_tree = header[5:].decode("ascii")
    records = []
    for item in materials:
        if item.final_candidate:
            records.append(
                _record(item.path, "final_candidate", _candidate(worktree, item.path))
            )
        if item.comparison_base:
            assert root_tree is not None
            records.append(
                _record(
                    item.path, "comparison_base", _base(worktree, root_tree, item.path)
                )
            )
    return tuple(records)


def _record(
    path: str, state: str, material: tuple[str, str | None, bytes | None]
) -> dict[str, Any]:
    kind, mode, content = material
    return {
        "path": path,
        "state": state,
        "kind": kind,
        "mode": mode,
        "contentBase64": (
            None if content is None else base64.b64encode(content).decode("ascii")
        ),
    }


def _candidate(worktree: Path, path: str) -> tuple[str, str | None, bytes | None]:
    # Directory descriptors plus NOFOLLOW prevent traversing symlinks, including
    # an ancestor swapped between the directory walk and the leaf read.
    descriptor = os.open(worktree, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        parts = path.split("/")
        for component in parts[:-1]:
            try:
                child = os.open(
                    component,
                    os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                    dir_fd=descriptor,
                )
            except FileNotFoundError:
                return "absent", None, None
            os.close(descriptor)
            descriptor = child
        leaf = parts[-1]
        try:
            info = os.stat(leaf, dir_fd=descriptor, follow_symlinks=False)
        except FileNotFoundError:
            return "absent", None, None
        if stat.S_ISLNK(info.st_mode):
            return (
                "symlink",
                "120000",
                os.readlink(os.fsencode(leaf), dir_fd=descriptor),
            )
        if not stat.S_ISREG(info.st_mode):
            raise ValueError(f"unsupported final material file type: {path!r}")
        opened = os.open(
            leaf, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=descriptor
        )
        with os.fdopen(opened, "rb") as stream:
            actual = os.fstat(stream.fileno())
            if not stat.S_ISREG(actual.st_mode):
                raise ValueError(f"unsupported final material file type: {path!r}")
            mode = "100755" if actual.st_mode & stat.S_IXUSR else "100644"
            return "file", mode, stream.read()
    finally:
        os.close(descriptor)


def _base(
    repository: Path, tree_id: str, path: str
) -> tuple[str, str | None, bytes | None]:
    parts = path.split("/")
    for index, component in enumerate(parts):
        entry = _tree_entry(
            git.read_object(repository, tree_id, "tree"), os.fsencode(component)
        )
        if entry is None:
            return "absent", None, None
        mode, object_id = entry
        if index < len(parts) - 1:
            if mode != "40000":
                raise ValueError(f"unsupported B1 material ancestor: {path!r}")
            tree_id = object_id
            continue
        if mode not in {"100644", "100755", "120000"}:
            raise ValueError(f"unsupported B1 material file type: {path!r}")
        return (
            "symlink" if mode == "120000" else "file",
            mode,
            git.read_object(repository, object_id, "blob"),
        )
    raise ValueError("empty material path")


def _tree_entry(tree: bytes, name: bytes) -> tuple[str, str] | None:
    """Match exactly one name in a SHA-1 tree, without Git path interpretation."""
    offset = 0
    while offset < len(tree):
        separator = tree.index(b" ", offset)
        end = tree.index(b"\x00", separator)
        object_start = end + 1
        object_end = object_start + 20
        if object_end > len(tree):
            raise ValueError("truncated B1 tree object")
        if tree[separator + 1 : end] == name:
            return tree[offset:separator].decode("ascii"), tree[
                object_start:object_end
            ].hex()
        offset = object_end
    return None
