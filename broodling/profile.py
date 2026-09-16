"""Current product configuration; historical qualification is not a runtime pin."""

from __future__ import annotations

import sqlite3
import sys

from .errors import UnsupportedRuntime

#: Selected product runtime. Python 3.13 is the minimum interpreter family and
#: SQLite 3.37 is the first release with ``STRICT`` tables, which the schema uses
#: to make column typing an enforced durable property rather than a convention.
MIN_PYTHON = (3, 13)
MIN_SQLITE = (3, 37, 0)

ZEROSHOT_VERSION = "10.3.0"
ZEROSHOT_SDK_VERSION = "10.3.0.post1"
ZEROSHOT_BOUNDARY: dict[str, str] = {
    "integration": (
        "official Python SDK LocalTarget/DirectTarget with bundled native engine"
    ),
    "zeroshotRevision": "054ad3fd6c763b98d12f5b2e90830b97116561ad",
    "sdk": f"the-open-engine-zeroshot {ZEROSHOT_SDK_VERSION}",
    "engine": ZEROSHOT_VERSION,
}

#: No effect is implicitly granted. A Contract may separately authorize the one
#: supported pull-request delivery; every other effect remains refused.
V1_REQUIRED_EFFECTS: tuple[str, ...] = ()

#: Host/runtime assumptions a Contract may rely on inside the current
#: single-host, one-Attempt/one-dedicated-worktree, no-effect V1 profile. The set
#: is an allowlist: an assumption that is not listed is unsupported, so a new
#: profile claim fails admission instead of being silently accepted.
SUPPORTED_HOST_ASSUMPTIONS: frozenset[str] = frozenset(
    {
        "single_host",
        "one_attempt_one_dedicated_worktree",
        "disposable_worktree",
        "local_filesystem_only",
        "no_authoritative_effects",
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
        "zeroshotBoundary": dict(ZEROSHOT_BOUNDARY),
        "requiredEffects": list(V1_REQUIRED_EFFECTS),
        "supportedResultDeliveries": ["none", "pull_request"],
        "supportedHostAssumptions": sorted(SUPPORTED_HOST_ASSUMPTIONS),
    }
