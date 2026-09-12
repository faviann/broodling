"""The shared Hypothesis settings profile for the property tests.

Hypothesis is a test-only dependency — the product package keeps an empty
dependency list — so it is declared in the ``test`` extra and installed with
``pip install -e '.[test]'``. It is not optional at test time: a run without it
fails to import these modules rather than quietly dropping their assurance.

The profile is deliberately deterministic: the same run generates the same
inputs, a failure shrinks as usual, and ``print_blob`` prints the
``@reproduce_failure`` blob that replays the exact counterexample on another
host. Nothing is written to the repository, so there is no example database to
keep in or out of Git.
"""

from __future__ import annotations

from hypothesis import settings

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
