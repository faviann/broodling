"""Single-host physical launch fence and W5 PID-namespace cessation barrier.

The one receipt identifies only the latest kernel namespace init process. It is
not runtime history or semantic evidence. Provider execution requires its durable
receipt; pidfd readiness, not lock release or a terminal label, proves teardown.

Bubblewrap 0.12.0 bubblewrap.c arms stock do_init parent death after forking
its child. Its --block-fd also accepts EOF. The trusted PID1 entry below closes
both startup gaps with an explicit token and a live launcher pidfd. Linux 6.17
exit.c closes files before exit_notify tears down PID namespaces, so an advisory
lock alone is insufficient; PID1 pidfd readiness follows that teardown.
Sources: github.com/containers/bubblewrap/blob/v0.12.0/bubblewrap.c and
github.com/torvalds/linux/blob/v6.17/kernel/exit.c.
"""

from __future__ import annotations

import ctypes
import fcntl
import hashlib
import json
import os
import select
import signal
import subprocess
import sys
import tempfile
from contextlib import contextmanager
from pathlib import Path

CONTAINMENT_PROFILE = "linux-pidns-parent-death-v1"
LOCK = ".broodling-containment.lock"
FENCE = ".broodling-containment.closed"
RECEIPT = ".broodling-containment.json"
SIDECAR_SHA256 = "9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86"
BWRAP = "/usr/bin/bwrap"


def _sync(directory: Path) -> None:
    fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def _root(workspace: Path) -> Path:
    return Path(workspace).resolve().parent


def close_launches(workspace: Path) -> None:
    """Permanently fence future launches; does not claim cessation."""
    root = _root(workspace)
    try:
        fd = os.open(root / FENCE, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        if not (root / FENCE).is_file() or (root / FENCE).is_symlink():
            raise ValueError("invalid containment fence")
    else:
        try:
            os.fsync(fd)
        finally:
            os.close(fd)
    _sync(root)


@contextmanager
def _lock(root: Path, *, blocking: bool = True):
    fd = os.open(root / LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | (0 if blocking else fcntl.LOCK_NB))
        yield fd
    finally:
        os.close(fd)


def _starttime(pid: int) -> str:
    return Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[19]


def _ready(fd: int) -> bool:
    poll = select.poll()
    poll.register(fd, select.POLLIN)
    return bool(poll.poll(0))


def _dead(receipt: dict) -> bool:
    if (
        set(receipt) != {"profile", "pid", "starttime"}
        or receipt["profile"] != CONTAINMENT_PROFILE
    ):
        raise ValueError("unrecognized containment receipt")
    pid = receipt["pid"]
    if type(pid) is not int or pid <= 1 or not isinstance(receipt["starttime"], str):
        raise ValueError("invalid containment process identity")
    try:
        fd = os.pidfd_open(pid)
    except ProcessLookupError:
        return True
    try:
        try:
            current = _starttime(pid)
        except FileNotFoundError:
            return True
        return current != receipt["starttime"] or _ready(fd)
    finally:
        os.close(fd)


def _previous_dead(root: Path) -> bool:
    path = root / RECEIPT
    if path.is_symlink():
        raise ValueError("invalid containment receipt path")
    try:
        value = json.loads(path.read_text())
    except FileNotFoundError:
        return True
    return _dead(value)


def confirm_ceased(workspace: Path) -> bool:
    """Check a closed launch boundary and complete kernel namespace teardown."""
    root = _root(workspace)
    if not (root / FENCE).is_file() or (root / FENCE).is_symlink():
        return False
    try:
        with _lock(root, blocking=False):
            return _previous_dead(root)
    except BlockingIOError:
        return False


def _retain(root: Path, receipt: dict) -> None:
    fd, name = tempfile.mkstemp(prefix=".broodling-containment-", dir=root)
    try:
        with os.fdopen(fd, "w") as stream:
            json.dump(receipt, stream, sort_keys=True)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, root / RECEIPT)
        _sync(root)
    finally:
        Path(name).unlink(missing_ok=True)


def _guard_parent() -> None:
    """Reject pre-entry orphans, then arm and recheck the pinned controller."""
    parent = os.getppid()
    if parent <= 1:
        raise ValueError("provider launcher has no controller parent")
    start = _starttime(parent)
    command = Path(f"/proc/{parent}/cmdline").read_bytes().split(b"\0")
    if len(command) != 5 or command[1:3] != [
        b"__zeroshot-run-controller",
        b"--bootstrap",
    ]:
        raise ValueError("provider launcher parent is not the pinned controller")
    with Path(f"/proc/{parent}/exe").open("rb") as executable:
        digest = hashlib.file_digest(executable, "sha256").hexdigest()
    if digest != SIDECAR_SHA256:
        raise ValueError("provider launcher parent executable is not qualified")
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(1, signal.SIGKILL, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), "PR_SET_PDEATHSIG failed")
    if os.getppid() != parent or _starttime(parent) != start:
        raise ValueError("provider controller was lost during launcher setup")


def enclose(argv: list[str], environment: dict[str, str], workspace: Path) -> int:
    """Run one product occurrence inside W5 descendant containment."""
    _guard_parent()
    root = _root(workspace)
    with _lock(root):
        if (root / FENCE).exists():
            raise ValueError("Attempt launches are permanently closed")
        if not _previous_dead(root):
            raise ValueError("previous occurrence namespace has not ceased")
        gate_read, gate_write = os.pipe()
        launcher = os.pidfd_open(os.getpid())
        info_read, info_write = os.pipe()
        command = [
            BWRAP,
            "--bind",
            "/",
            "/",
            "--dev-bind",
            "/dev",
            "/dev",
            "--proc",
            "/proc",
            "--unshare-pid",
            "--as-pid-1",
            "--die-with-parent",
            "--new-session",
            "--json-status-fd",
            str(info_write),
            "--",
            sys.executable,
            str(Path(__file__).resolve()),
            "--entry",
            str(gate_read),
            str(launcher),
            *argv,
        ]
        child = None
        try:
            child = subprocess.Popen(
                command,
                env=environment,
                cwd=workspace,
                pass_fds=(gate_read, launcher, info_write),
            )
            os.close(info_write)
            info_write = -1
            os.close(gate_read)
            gate_read = -1
            with os.fdopen(info_read, "r") as info:
                info_read = -1
                line = info.readline()
                status = json.loads(line)
                pid = status["child-pid"]
                receipt = {
                    "profile": CONTAINMENT_PROFILE,
                    "pid": pid,
                    "starttime": _starttime(pid),
                }
                _retain(root, receipt)
                if (root / FENCE).exists():
                    raise ValueError("Attempt closed during launch setup")
                os.write(gate_write, b"G")
                os.close(gate_write)
                gate_write = -1
                # Keep the status pipe readable until the monitor exits.
                info.read()
            result = child.wait()
            # The outer monitor can return before PID1 has torn down children.
            try:
                fd = os.pidfd_open(pid)
            except ProcessLookupError:
                return result
            try:
                if _starttime(pid) == receipt["starttime"]:
                    poll = select.poll()
                    poll.register(fd, select.POLLIN)
                    poll.poll()
            except FileNotFoundError:
                pass
            finally:
                os.close(fd)
            return result
        finally:
            for fd in (gate_read, gate_write, launcher, info_read, info_write):
                if fd >= 0:
                    os.close(fd)
            if child is not None and child.poll() is None:
                child.kill()
                child.wait()


def _entry(arguments: list[str]) -> int:
    gate, launcher = map(int, arguments[:2])
    try:
        if os.read(gate, 1) != b"G" or _ready(launcher):
            return 78
    finally:
        os.close(gate)
        os.close(launcher)
    # This trusted entry is PID1, after bwrap armed parent death. Starting the
    # provider here avoids bwrap init's fork-before-PDEATHSIG setup window.
    # Namespace teardown on entry exit kills every descendant, including setsid.
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(143))
    return subprocess.call(arguments[2:])


if __name__ == "__main__":
    if sys.argv[1:2] != ["--entry"]:
        raise SystemExit(78)
    raise SystemExit(_entry(sys.argv[2:]))
