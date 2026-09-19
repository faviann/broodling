#!/usr/bin/env python3
"""Inspect this installation's actual target without credentials or provider work."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import urlsplit
from urllib.request import urlopen

NATIVE_SHA256 = "afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06"
GH_SHA256 = "ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff"


class TargetNotReady(RuntimeError):
    """An installation or dependency differs from the supported profile."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise TargetNotReady(message)


def command(*arguments: str) -> str:
    try:
        return subprocess.run(
            arguments, check=True, capture_output=True, text=True, timeout=30,
        ).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        # Neither Docker's configuration nor subprocess diagnostics are safe to dump.
        raise TargetNotReady("target inspection command failed; check container status") from None


def check(root: Path) -> dict:
    root = root.expanduser().resolve()
    manifest = json.loads((root / "installation.json").read_text())
    name = manifest["container_name"]
    origin = manifest["direct_target_origin"]
    invocation_config = json.loads((root / "config.json").read_text())
    require(invocation_config == {"direct_target_origin": origin},
            "invocation configuration differs from the installed DirectTarget")
    address = urlsplit(origin)
    require(
        address.scheme == "http" and address.hostname == "127.0.0.1"
        and address.port is not None and not address.username and not address.password
        and not address.path and not address.query and not address.fragment,
        "DirectTarget origin must be the recorded loopback HTTP origin",
    )
    records = json.loads(command("docker", "inspect", name))
    require(len(records) == 1, "expected exactly one installed target")
    actual = records[0]
    require(actual["Image"] == manifest["image_id"], "target image differs from installation")
    require(actual["State"]["Running"], "target is stopped; start its existing container")
    config, host = actual["Config"], actual["HostConfig"]
    require(config.get("User", "") in ("", "0", "0:0", "root"),
            "native target must run as container root for its isolated process identities")
    require(not host["Privileged"] and host["NetworkMode"] != "host",
            "target must use ordinary Docker isolation")
    require(not host.get("CapDrop"), "target requires Docker's default Linux capabilities")
    require(host["RestartPolicy"]["Name"] == "no", "target restart must remain operator controlled")
    require(config["Entrypoint"] == ["zeroshot", "target", "serve"],
            "target entrypoint differs from the supported profile")
    forbidden = {"GH_TOKEN", "GITHUB_TOKEN", "GATEWAY_API_KEY", "OPENAI_API_KEY",
                 "ANTHROPIC_API_KEY", "CODEX_API_KEY"}
    require(not any(value.partition("=")[0] in forbidden for value in config["Env"]),
            "dispatch credentials must not be installed in target configuration")
    expected_mounts = {(str(root / "target-state"), "/state"),
                       (str(root / "target-home"), "/home/node")}
    mounts = actual["Mounts"]
    require(len(mounts) == 2 and all(m["Type"] == "bind" and m["RW"] for m in mounts)
            and {(m["Source"], m["Destination"]) for m in mounts} == expected_mounts,
            "target persistent mounts differ from installation")
    ports = host["PortBindings"]
    require(len(ports) == 1, "target must publish only its loopback endpoint")
    inner_port, binding = next(iter(ports.items()))
    require(inner_port.endswith("/tcp") and binding == [
        {"HostIp": "127.0.0.1", "HostPort": str(address.port)},
    ], "target endpoint is not exclusively bound to the recorded loopback port")
    require(config["Cmd"] == ["--listen", "0.0.0.0:" + inner_port.removesuffix("/tcp"),
                              "--public-origin", origin, "--storage", "/state"],
            "target launch arguments differ from installation")

    container_id = actual["Id"]

    def execute(*arguments: str) -> str:
        return command("docker", "exec", container_id, *arguments)

    versions = {}
    for label, program, expected in (
        ("native", "/usr/local/bin/zeroshot", "zeroshot 10.3.0"),
        ("codex", "/usr/local/bin/codex", "codex-cli 0.153.4"),
        ("node", "/usr/local/bin/node", "v22.23.2"),
    ):
        versions[label] = execute(program, "--version")
        require(versions[label] == expected, f"target {label} version differs from supported pin")
    gh_version = execute("/usr/bin/gh", "--version").splitlines()[0]
    require(gh_version.startswith("gh version 2.101.0 "), "target GitHub CLI version differs from supported pin")
    versions["gh"] = gh_version
    for program, expected in (("/usr/local/bin/zeroshot", NATIVE_SHA256), ("/usr/bin/gh", GH_SHA256)):
        require(execute("sha256sum", program).split()[0] == expected,
                "target executable bytes differ from supported pin")
    gh_help = execute("/usr/bin/gh", "api", "graphql", "--paginate", "--slurp", "--help")
    require(bool(re.search(r"^\s+--slurp(?:\s|$)", gh_help, re.MULTILINE)),
            "actual target GitHub CLI lacks api --paginate --slurp")
    # No provider task: check that this Docker/LXC baseline permits the same
    # group/UID transitions used by native hosted processes, inside one exec.
    execute("python3", "-c", "import os; os.setgroups([10002]); os.setgid(10002); "
            "os.setuid(10002); assert os.getuid() == 10002 and os.getgid() == 10002")
    with urlopen(origin + "/.well-known/zeroshot-native-v2", timeout=10) as response:
        discovery = json.load(response)
    require(discovery.get("kind") == "zeroshot.native-v2-target/v2"
            and discovery.get("authentication") == "none"
            and discovery.get("oecpPath") == "/native-v2/oecp",
            "target discovery differs from supported DirectTarget")
    return {"container_name": name, "container_id": container_id,
            "image_id": actual["Image"], "direct_target_origin": origin,
            "versions": versions, "api_paginate_slurp": True,
            "hosted_uid_transition": True, "ready": True,
            "provider_tasks": 0}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    args = parser.parse_args()
    try:
        facts = check(args.root)
    except TargetNotReady as error:
        facts = {"ready": False, "error": str(error)}
    except (OSError, ValueError, KeyError, TypeError, IndexError):
        facts = {"ready": False, "error": "installation metadata or target discovery is unavailable or invalid"}
    print(json.dumps(facts, indent=2, sort_keys=True))
    return 0 if facts["ready"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
