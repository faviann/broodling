"""The real-Zeroshot integration/qualification lane (issue #39).

Two lanes, one mechanism. Broodling regression is the default and runs
everything not marked here; the marked witnesses run real Zeroshot end to end
and are opt-in:

    python -m pytest                                   # Broodling regression
    BROODLING_ZEROSHOT_LANE=1 python -m pytest         # both lanes

An environment variable rather than a pytest marker, because the suite is also
documented to run under ``python -m unittest discover -s tests``, where markers
do not exist. ``unittest.skipUnless`` is understood by both runners and needs no
plugin, conftest hook or configuration.

What belongs here is real Zeroshot execution whose cost is paid for a gate
rather than for the next commit: the G3 actual graph and evidence controls, the
G4 disposition and replacement races, #19 custody and current-run observation,
and the stop/cessation windows. Nothing here is weakened, deleted or doubled —
the lane changes when these run, not what they prove.

What does *not* belong here is a real-SDK test that is cheap and whose subject
is Broodling's own durable behaviour at the boundary. The P2 submission and
crash-window witnesses stay in regression for that reason: real duplicate
submission under one key is the cross-boundary behaviour Broodling's
reconciliation is built on, and the whole group costs seconds.
"""

from __future__ import annotations

import importlib.util
import os
import unittest

#: Set to any non-empty value to include the lane in a run.
LANE_ENV = "BROODLING_ZEROSHOT_LANE"

_SELECTED = bool(os.environ.get(LANE_ENV))
_INSTALLED = importlib.util.find_spec("zeroshot") is not None

qualification_lane = unittest.skipUnless(
    _SELECTED and _INSTALLED,
    f"real-Zeroshot lane: set {LANE_ENV}=1 with the G1-V1 qualified SDK/sidecar"
    if not _SELECTED
    else "install the G1-V1 qualified SDK/sidecar",
)
