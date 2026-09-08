#!/usr/bin/env python3
"""Checked-in fixture commands; never a product worker or lifecycle service."""

import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path


def writer(path, marker):
    if os.fork():
        return
    os.setsid()
    if os.fork():
        os._exit(0)
    fd = os.open("/dev/null", os.O_RDWR)
    for number in (0, 1, 2):
        os.dup2(fd, number)
    for index in range(300):
        with Path(path).open("a") as stream:
            stream.write(f"{marker}:{index}\n")
            stream.flush()
            os.fsync(stream.fileno())
        time.sleep(0.2)
    os._exit(0)


def main():
    mode = sys.argv[1]
    config = json.loads(Path("probe-config.json").read_text())
    if mode == "writer":
        writer("heartbeat.txt", config["marker"])
        time.sleep(55)
        return
    records = []
    for name, path in [("own", "candidate.txt"), ("sibling", config["sibling"])]:
        try:
            Path(path).write_text(
                "UNAUTHORIZED"
                if name == "sibling" or mode == "readonly"
                else "AUTHORIZED\n"
            )
            records.append({"name": name, "succeeded": True})
        except OSError as error:
            records.append({"name": name, "succeeded": False, "error": str(error)})
    for args in [
        ["git", "config", "broodling.probe", "UNAUTHORIZED"],
        ["git", "tag", "issue21-unwanted-effect"],
        ["git", "push", config["remote"], "HEAD:refs/heads/unwanted"],
    ]:
        result = subprocess.run(args, text=True, capture_output=True, check=False)
        records.append(
            {
                "argv": args,
                "returncode": result.returncode,
                "stdout": result.stdout,
                "stderr": result.stderr,
            }
        )
    try:
        with socket.create_connection(("127.0.0.1", config["port"]), timeout=1):
            records.append({"name": "network", "succeeded": True})
    except OSError as error:
        records.append({"name": "network", "succeeded": False, "error": str(error)})
    records.append(
        {
            "name": "github_credentials",
            "readable": os.access(Path.home() / ".config/gh/hosts.yml", os.R_OK),
        }
    )
    print("ISSUE21_PROBE=" + json.dumps(records, sort_keys=True), flush=True)


if __name__ == "__main__":
    main()
