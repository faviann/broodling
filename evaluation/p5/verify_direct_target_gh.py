#!/usr/bin/env python3
"""Check the target's native PR CLI dependency without network or provider work."""

from __future__ import annotations

import argparse
import json
import re
import subprocess

# Zeroshot 10.3.0's hosted delivery configuration uses this absolute path.
GH_PROGRAM = "/usr/bin/gh"


class IncompatibleGitHubCLI(RuntimeError):
    """Credential-free compatibility facts, including any observed version."""

    def __init__(self, facts: dict):
        self.facts = facts
        super().__init__("DirectTarget requires /usr/bin/gh api --paginate --slurp; "
                         "compatibility check failed before admission")


def verify(*command_prefix: str) -> dict:
    """Probe the same binary as native delivery; help never sends an API request.

    A live preflight supplies ``docker exec CONTAINER``. Image validation can
    instead use a fresh container with no network, credentials or state mounts.
    Both success and refusal retain the installed version, when readable.
    """
    facts = {"gh_program": GH_PROGRAM, "gh_version": None, "api_paginate_slurp": False}

    def command(*arguments: str) -> str:
        return subprocess.run(
            (*command_prefix, GH_PROGRAM, *arguments),
            check=True, capture_output=True, text=True, timeout=30,
        ).stdout.strip()

    try:
        version = command("--version")
        if not version:
            raise IncompatibleGitHubCLI(facts)
        facts["gh_version"] = version.splitlines()[0]
        help_text = command("api", "graphql", "--paginate", "--slurp", "--help")
    except (OSError, subprocess.SubprocessError):
        # Subprocess stderr and environment may contain credentials; omit them.
        raise IncompatibleGitHubCLI(facts) from None
    if not re.search(r"^\s+--slurp(?:\s|$)", help_text, re.MULTILINE):
        raise IncompatibleGitHubCLI(facts)
    facts["api_paginate_slurp"] = True
    return facts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--container", help="already-running DirectTarget container ID")
    target.add_argument("--image", help="image identity to check in disposable offline containers")
    args = parser.parse_args()
    prefix = ("docker", "exec", args.container) if args.container else (
        "docker", "run", "--rm", "--network", "none", "--read-only",
        "--cap-drop=ALL", "--entrypoint", "", args.image,
    )
    try:
        facts = verify(*prefix)
    except IncompatibleGitHubCLI as error:
        print(json.dumps({**error.facts, "error": str(error)}, indent=2, sort_keys=True))
        return 1
    print(json.dumps(facts, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
