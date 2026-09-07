"""Recorded product configuration and qualified V1 profile boundary.

V1-P2 submits through the qualified SDK/sidecar without rerunning qualification.
The qualification evidence for these versions is retained
under ``qualification/v1-p1`` and reviewed by the G1-V1 gate record.
"""

from __future__ import annotations

import sqlite3
import sys

from .errors import UnsupportedRuntime

#: Selected product runtime. Python 3.13 is the qualified interpreter family and
#: SQLite 3.37 is the first release with ``STRICT`` tables, which the schema uses
#: to make column typing an enforced durable property rather than a convention.
MIN_PYTHON = (3, 13)
MIN_SQLITE = (3, 37, 0)

#: External runtime boundary qualified by V1-P1 and reviewed at G1-V1. P2 owns
#: submission correlation only; execution state remains in Zeroshot.
QUALIFIED_ZEROSHOT_BOUNDARY: dict[str, str] = {
    "integration": "official Python SDK LocalTarget -> matching Rust sidecar",
    "zeroshotRevision": "d0909615d6ba3c179b58bce15a059f40400ec995",
    "sdk": "zeroshot-rust 0.1.0.dev0",
    "wheelSha256": "16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb",
    "sidecar": "zeroshot-rust 0.1.0",
    "sidecarSha256": "9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86",
    "gateRecord": "qualification/v1-p1/issue-11-g1-v1.md",
    "gateVerdict": "G1-V1 PASS",
}

#: The V1 required authoritative-effect set. Empty, not waived: a Contract that
#: requires any authoritative effect is rejected rather than admitted with the
#: effect ignored.
V1_REQUIRED_EFFECTS: tuple[str, ...] = ()

#: Host/runtime assumptions a Contract may rely on inside the qualified
#: single-host, one-Attempt/one-dedicated-worktree, no-effect V1 profile. The set
#: is an allowlist: an assumption that is not listed is unsupported, so a new
#: profile claim fails admission instead of being silently accepted.
SUPPORTED_HOST_ASSUMPTIONS: frozenset[str] = frozenset(
    {
        "single_host",
        "one_attempt_one_dedicated_worktree",
        "disposable_worktree",
        "graph_ordered_mutation",
        "read_only_assurance",
        "local_filesystem_only",
        "no_authoritative_effects",
        "abandon_and_restart",
    }
)


def runtime_versions() -> dict[str, str]:
    """Report the interpreter/SQLite versions this store is running on."""

    return {
        "python": ".".join(str(part) for part in sys.version_info[:3]),
        "sqlite": sqlite3.sqlite_version,
    }


def assert_supported_runtime() -> None:
    """Fail closed when the interpreter or SQLite build is outside the selection."""

    if sys.version_info[:2] < MIN_PYTHON:
        raise UnsupportedRuntime(
            f"Python {'.'.join(str(part) for part in MIN_PYTHON)}+ is required, "
            f"found {'.'.join(str(part) for part in sys.version_info[:3])}"
        )
    found = tuple(int(part) for part in sqlite3.sqlite_version.split("."))
    if found < MIN_SQLITE:
        raise UnsupportedRuntime(
            f"SQLite {'.'.join(str(part) for part in MIN_SQLITE)}+ is required, "
            f"found {sqlite3.sqlite_version}"
        )


def product_configuration() -> dict[str, object]:
    """The full recorded configuration persisted with every admission decision."""

    return {
        "runtime": runtime_versions(),
        "zeroshotBoundary": dict(QUALIFIED_ZEROSHOT_BOUNDARY),
        "requiredEffects": list(V1_REQUIRED_EFFECTS),
        "supportedHostAssumptions": sorted(SUPPORTED_HOST_ASSUMPTIONS),
    }
