"""Controlled launcher tests substitute only the absent real controller parent.

This is never a product launch path. All namespace, receipt, gate and provider
argument behavior remains actual product code.
"""

import runpy
import sys
from pathlib import Path

original = runpy.run_path


def controlled_load(path, *args, **kwargs):
    result = original(path, *args, **kwargs)
    if Path(path).name == "containment.py":
        result["enclose"].__globals__["_guard_parent"] = lambda: None
    return result


runpy.run_path = controlled_load
sys.argv = sys.argv[1:]
original(sys.argv[0], run_name="__main__")
