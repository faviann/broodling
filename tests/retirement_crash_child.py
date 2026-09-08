"""Process-level retirement fault injection; never imported by the product."""

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from broodling import (
    AbandonmentCoordinator,
    BroodlingStore,
    ZeroshotSubmitter,
    git,
)

store_path, attempt_id, action = sys.argv[1:4]
with BroodlingStore.open(store_path) as store:
    coordinator = AbandonmentCoordinator(store, ZeroshotSubmitter("/unused"))
    if action == "held-git":
        git.GIT = sys.argv[4]
    elif action == "after-remove":
        coordinator._acknowledge = lambda *_: os._exit(73)
    record = coordinator.retire(attempt_id)
    print(record.retired_at, flush=True)
