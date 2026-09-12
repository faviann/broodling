"""Hypothesis availability and the shared settings profile for property tests.

Hypothesis is a test-only extra (``pip install -e '.[test]'``). The product
package itself stays on the standard library, so a host that has not installed
the extra skips the property modules instead of failing the whole run.

The profile is deliberately deterministic: the same run generates the same
inputs, a failure shrinks as usual, and ``print_blob`` prints the
``@reproduce_failure`` blob that replays the exact counterexample on another
host. Nothing is written to the repository, so there is no example database to
keep in or out of Git.
"""

from __future__ import annotations

import unittest

try:
    from hypothesis import settings
except ModuleNotFoundError as missing:
    raise unittest.SkipTest(
        "hypothesis is not installed; install the test extra "
        "(pip install -e '.[test]') to run the property tests"
    ) from missing

PROFILE = "broodling"

settings.register_profile(
    PROFILE,
    derandomize=True,
    print_blob=True,
    database=None,
    # SQLite file creation and Git-free store writes vary by an order of
    # magnitude between a warm and a cold host; a per-example deadline would
    # report that as a failure.
    deadline=None,
)
settings.load_profile(PROFILE)
