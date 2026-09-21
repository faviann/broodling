#!/usr/bin/env python3
"""Install one immutable Broodling release and create its local native target.

Run as the dedicated host account with Docker access. No work is submitted and
no credentials are read. Application state is initialized separately through the
installed CLI. Docker and Zeroshot own process lifecycle.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import sqlite3
import subprocess
import sys
import tarfile


NATIVE_SHA256 = "afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06"


def command(*argv: str, capture: bool = False) -> str:
    result = subprocess.run(argv, check=True, text=True, stdout=subprocess.PIPE if capture else None)
    return result.stdout.strip() if capture else ""


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n")


def install(root: Path, revision: str, port: int, container: str) -> dict:
    if sys.version_info[:2] != (3, 13) or sqlite3.sqlite_version_info < (3, 37):
        raise ValueError("this installer requires Python 3.13 and SQLite 3.37+")
    if platform.system() != "Linux" or platform.machine() != "x86_64":
        raise ValueError("this profile requires Linux x86-64")
    if os.getuid() == 0:
        raise ValueError("run as the dedicated non-root host account with Docker access")
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("--revision must be a full immutable Git commit id")
    if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9_.-]+", container):
        raise ValueError("invalid Docker container name")
    if not 1024 <= port <= 65535:
        raise ValueError("--port must be between 1024 and 65535")
    root = root.expanduser().absolute()
    if root.resolve() != root or any(root.is_relative_to(p) for p in ("/tmp", "/var/tmp", "/run", "/dev/shm")):
        raise ValueError("--root must be a canonical durable path, without symlinks")
    source = Path(__file__).resolve().parents[1]
    if root.is_relative_to(source) or source.is_relative_to(root):
        raise ValueError("installation and source repository must be separate")
    origin = f"http://127.0.0.1:{port}"
    manifest = root / "installation.json"
    if manifest.exists():
        existing = json.loads(manifest.read_text())
        expected = {"installation_root": str(root), "broodling_revision": revision, "direct_target_origin": origin,
                    "container_name": container, "host_uid": os.getuid(), "host_gid": os.getgid()}
        if any(existing.get(key) != value for key, value in expected.items()):
            raise ValueError("existing installation differs; in-place upgrades/moves are unsupported")
        if not all((root / name).is_file() for name in ("config.json", "bin/broodling")):
            raise ValueError("existing installation is incomplete; restore retained files before use")
        return existing
    if root.exists() and any(root.iterdir()):
        raise ValueError("root must be empty; preserve and inspect any incomplete installation")
    endpoint = os.environ.get("DOCKER_HOST") or command(
        "docker", "context", "inspect", "--format", "{{.Endpoints.docker.Host}}", capture=True)
    if os.environ.get("DOCKER_CONTEXT"):
        endpoint = command("docker", "context", "inspect", "--format", "{{.Endpoints.docker.Host}}", capture=True)
    if not endpoint.startswith("unix:///"):
        raise ValueError("this single-host profile requires a local Docker Unix socket")
    if command("docker", "info", "--format", "{{.OSType}}", capture=True) != "linux":
        raise ValueError("Docker must run Linux containers on this host")
    if "rootless" in command("docker", "info", "--format", "{{json .SecurityOptions}}", capture=True):
        raise ValueError("this profile requires rootful Docker")
    host_git = command("git", "--version", capture=True)
    host_gh = command("gh", "--version", capture=True).splitlines()[0]
    if subprocess.run(["docker", "container", "inspect", container], stdout=subprocess.DEVNULL,
                      stderr=subprocess.DEVNULL).returncode == 0:
        raise ValueError("container name already exists; choose a fresh installation name")
    resolved = command("git", "-C", str(source), "rev-parse", f"{revision}^{{commit}}", capture=True)
    if resolved != revision:
        raise ValueError("revision does not resolve to the requested commit")
    # Archive only committed product/install files, never evaluation state or
    # credentials from the operator's working tree.
    archive = subprocess.run(["git", "-C", str(source), "archive", revision,
                              "broodling", "pyproject.toml", "deployment"],
                             check=True, stdout=subprocess.PIPE).stdout
    os.umask(0o077)
    root.mkdir(parents=True, exist_ok=True)
    for name in ("release", "bin", "state", "runtime", "attempts", "repositories",
                 "target-state", "target-home", "build"):
        (root / name).mkdir(mode=0o700)
    with tarfile.open(fileobj=io.BytesIO(archive)) as bundle:
        bundle.extractall(root / "release", filter="data")
    command(sys.executable, "-I", "-m", "venv", str(root / "venv"))
    python = str(root / "venv/bin/python")
    command(python, "-I", "-m", "pip", "install", "pip==25.1.1", "setuptools==80.9.0", "wheel==0.45.1")
    command(python, "-I", "-m", "pip", "install", "--no-build-isolation", str(root / "release"))
    native_path = command(python, "-I", "-c", "import zeroshot; from pathlib import Path; "
                          "print(Path(zeroshot.__file__).parent / '_bin/zeroshot')", capture=True)
    native = Path(native_path)
    if hashlib.sha256(native.read_bytes()).hexdigest() != NATIVE_SHA256:
        raise ValueError("installed native executable differs from the pinned release")
    shutil.copy2(native, root / "build/zeroshot")
    shutil.copy2(root / "release/deployment/DirectTarget.Dockerfile", root / "build/Dockerfile")
    command("docker", "build", "--platform", "linux/amd64", "--iidfile", str(root / "build/image-id"),
            str(root / "build"))
    image = (root / "build/image-id").read_text().strip()
    # Root inside the container is required by the native capsule identity
    # allocator. Docker defaults preserve the qualified capability profile.
    command("docker", "create", "--name", container, "--init", "--restart=no", "--stop-timeout", "30",
            "--publish", f"127.0.0.1:{port}:{port}",
            "--mount", f"type=bind,src={root / 'target-state'},dst=/state",
            "--mount", f"type=bind,src={root / 'target-home'},dst=/home/node",
            image, "--listen", f"0.0.0.0:{port}", "--public-origin", origin, "--storage", "/state")
    write_json(root / "config.json", {"direct_target_origin": origin})
    for name, argv in {
        "broodling": [python, "-I", "-m", "broodling.cli", "--root", str(root)],
        "check-target": [python, "-I", str(root / "release/deployment/check_target.py"), "--root", str(root)],
    }.items():
        wrapper = root / "bin" / name
        wrapper.write_text("#!/bin/sh\numask 077\nexport PATH="
                           + shlex.quote(f"{root}/venv/bin:/usr/local/bin:/usr/bin:/bin")
                           + "\nexec " + shlex.join(argv) + ' "$@"\n')
        wrapper.chmod(0o700)
    record = {
        "installation_root": str(root),
        "broodling_revision": revision,
        "broodling_version": command(python, "-I", "-c", "import broodling; print(broodling.__version__)", capture=True),
        "host_uid": os.getuid(), "host_gid": os.getgid(),
        "python_version": platform.python_version(), "sqlite_version": sqlite3.sqlite_version,
        "host_git": host_git, "host_gh": host_gh,
        "container_name": container, "image_id": image, "direct_target_origin": origin,
        "zeroshot_sdk": "10.3.0.post1", "zeroshot_native": "10.3.0",
        "zeroshot_native_sha256": NATIVE_SHA256,
        "codex": "0.153.4", "node": "22.23.2", "target_gh": "2.101.0",
    }
    write_json(manifest, record)
    return record


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--port", type=int, default=18770)
    parser.add_argument("--container", default="broodling-target")
    args = parser.parse_args()
    try:
        print(json.dumps(install(args.root, args.revision, args.port, args.container), indent=2))
        return 0
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        print(f"Installation incomplete: {error}. Preserve the root for inspection; no Work Unit was submitted.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
