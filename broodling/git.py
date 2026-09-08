"""The minimal local Git surface Broodling needs for administrative setup.

Provisioning a worktree is host-local administrative setup, not an authoritative
delivery effect: nothing here pushes, publishes, fetches or mutates a remote, and
nothing here writes to shared Git metadata beyond the worktree registration that
``git worktree add`` performs.

Every command runs with a sanitized environment. Inherited ``GIT_*`` variables
can silently redirect a command at a different repository, index or worktree, so
they are dropped rather than trusted; terminal prompting is disabled so a
credential prompt fails instead of hanging.
"""

from __future__ import annotations

import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

from .errors import GitCommandError

GIT = "git"

#: Environment passed to every Git command. ``LC_ALL`` keeps porcelain-adjacent
#: messages stable; ``GIT_OPTIONAL_LOCKS`` keeps read-only queries from taking
#: the index lock of a repository another Work Unit may be using.
_SAFE_ENVIRONMENT_OVERRIDES = {
    "GIT_TERMINAL_PROMPT": "0",
    "GIT_OPTIONAL_LOCKS": "0",
    "LC_ALL": "C",
}


def _environment() -> dict[str, str]:
    env = {
        key: value for key, value in os.environ.items() if not key.startswith("GIT_")
    }
    env.update(_SAFE_ENVIRONMENT_OVERRIDES)
    return env


def run(
    repository: Path | str, *arguments: str, inherited_fds: tuple[int, ...] = ()
) -> str:
    """Run one Git command inside ``repository`` and return its stdout.

    A non-zero exit is a refusal, not a value: it raises with the exact stderr so
    a caller never mistakes a failed query for an empty answer.
    """

    completed = subprocess.run(
        # A fixed argument vector, never a shell string.
        [GIT, "-C", str(repository), *arguments],
        capture_output=True,
        text=True,
        env=_environment(),
        pass_fds=inherited_fds,
        check=False,
    )
    if completed.returncode != 0:
        raise GitCommandError(
            f"git {' '.join(arguments)} failed in {repository} "
            f"({completed.returncode}): {completed.stderr.strip()}"
        )
    return completed.stdout


def _try(repository: Path | str, *arguments: str) -> str | None:
    try:
        return run(repository, *arguments)
    except GitCommandError:
        return None


def run_bytes(repository: Path | str, *arguments: str, stdin: bytes = b"") -> bytes:
    """Run a binary, NUL-delimited Git query without filename decoding."""
    completed = subprocess.run(
        [GIT, "-C", str(repository), *arguments],
        input=stdin,
        capture_output=True,
        env=_environment(),
        check=False,
    )
    if completed.returncode:
        raise GitCommandError(
            f"git {' '.join(arguments)} failed in {repository} "
            f"({completed.returncode}): "
            f"{completed.stderr.decode('utf-8', errors='replace').strip()}"
        )
    return completed.stdout


def read_object(repository: Path, object_id: str, object_type: str) -> bytes:
    """Read exact pinned SHA-1 object bytes, without replacement or text filters.

    This narrow binary reader cannot resolve live refs, revision expressions,
    paths or pathspecs. The admitted V1 starting-state identity is a full SHA-1.
    """
    if len(object_id) != 40 or not _is_hex(object_id):
        raise ValueError("a full pinned Git object id is required")
    if object_type not in {"commit", "tree", "blob"}:
        raise ValueError("unsupported custody Git object type")
    completed = subprocess.run(
        [
            GIT,
            "--no-replace-objects",
            "-C",
            str(repository),
            "cat-file",
            object_type,
            object_id,
        ],
        capture_output=True,
        env=_environment(),
        check=False,
    )
    if completed.returncode:
        raise GitCommandError(
            f"git cat-file {object_type} {object_id} failed in {repository} "
            f"({completed.returncode}): "
            f"{completed.stderr.decode('utf-8', errors='replace').strip()}"
        )
    return completed.stdout


def is_repository(path: Path) -> bool:
    return _try(path, "rev-parse", "--git-dir") is not None


def is_bare(repository: Path) -> bool:
    return run(repository, "rev-parse", "--is-bare-repository").strip() == "true"


def common_directory(repository: Path) -> Path:
    """The repository's shared Git directory.

    This is the identity every worktree of one repository shares, which is what
    makes ``refs/heads/*`` a single namespace across them: two Work Units on the
    same repository resolve to the same common directory and therefore cannot
    claim the same branch.
    """

    raw = run(repository, "rev-parse", "--git-common-dir").strip()
    candidate = Path(raw)
    if not candidate.is_absolute():
        candidate = Path(repository) / candidate
    return candidate.resolve()


def resolve_commit(repository: Path, revision: str) -> str | None:
    """Resolve ``revision`` to a full commit object id, or ``None``.

    ``^{commit}`` is deliberate: a tag or tree that does not name a commit
    resolves to nothing rather than to some other object type.
    """

    output = _try(
        repository,
        "rev-parse",
        "--verify",
        "--end-of-options",
        f"{revision}^{{commit}}",
    )
    if output is None:
        return None
    resolved = output.strip()
    return resolved if len(resolved) == 40 and _is_hex(resolved) else None


def _is_hex(value: str) -> bool:
    return all(character in "0123456789abcdef" for character in value)


def uncommitted_entries(repository: Path) -> tuple[str, ...]:
    """Every tracked modification, staged change and untracked path.

    ``--untracked-files=all`` lists files rather than directories, so a refusal
    can name the exact material that would otherwise be dropped.
    """

    output = run(
        repository, "status", "--porcelain=v1", "--untracked-files=all", "--no-renames"
    )
    return tuple(line for line in output.splitlines() if line.strip())


def branch_commit(repository: Path, branch: str) -> str | None:
    """The commit a local branch points at, or ``None`` when it does not exist."""

    output = _try(
        repository, "rev-parse", "--verify", "--end-of-options", f"refs/heads/{branch}"
    )
    return None if output is None else output.strip()


@dataclass(frozen=True, slots=True)
class WorktreeEntry:
    """A registered worktree of a repository: where it is and what it checks out.

    A registration can outlive the directory it names — that is exactly what a
    crash during provisioning leaves behind — so the presence of an entry says
    who claims the path, not that the path is on disk.
    """

    path: Path
    branch: str | None


def list_worktrees(repository: Path) -> tuple[WorktreeEntry, ...]:
    output = run(repository, "worktree", "list", "--porcelain")
    entries: list[WorktreeEntry] = []
    path: Path | None = None
    branch: str | None = None

    def flush() -> None:
        nonlocal path, branch
        if path is not None:
            entries.append(WorktreeEntry(path, branch))
        path, branch = None, None

    for line in output.splitlines():
        if not line.strip():
            flush()
            continue
        key, _, value = line.partition(" ")
        if key == "worktree":
            flush()
            path = Path(value).resolve()
        elif key == "branch":
            branch = value.removeprefix("refs/heads/")
    flush()
    return tuple(entries)


def find_worktree(repository: Path, path: Path) -> WorktreeEntry | None:
    resolved = Path(path)
    for entry in list_worktrees(repository):
        if entry.path == resolved:
            return entry
    return None


def add_worktree(
    repository: Path,
    path: Path,
    branch: str,
    commit: str,
    *,
    reuse_branch: bool = False,
    force: bool = False,
    inherited_fds: tuple[int, ...] = (),
) -> None:
    """Attach a worktree at ``path`` on ``branch``, starting at ``commit``.

    The branch is attached because the qualified sidecar profile rejects a
    detached HEAD. ``reuse_branch`` adopts a branch a previous interrupted
    provisioning already created at the same commit; ``force`` re-uses a path
    that is registered but missing on disk. Both are convergence, not overrides:
    the caller has already established that the existing state is this Attempt's.
    """

    # Materialize the immutable object itself, without local replacement refs
    # or hooks introducing different candidate content or detached host writers.
    arguments = [
        "--no-replace-objects",
        "-c",
        "core.hooksPath=/dev/null",
        "worktree",
        "add",
    ]
    if force:
        arguments.append("--force")
    if reuse_branch:
        arguments += [str(path), branch]
    else:
        arguments += ["-b", branch, str(path), commit]
    run(repository, *arguments, inherited_fds=inherited_fds)


def head_commit(worktree: Path) -> str:
    return run(worktree, "rev-parse", "HEAD").strip()


def current_branch(worktree: Path) -> str:
    return run(worktree, "rev-parse", "--abbrev-ref", "HEAD").strip()


def origin_url(worktree: Path) -> str | None:
    """Pin the source configuration consumed by the qualified local target."""
    value = _try(worktree, "config", "--get", "remote.origin.url")
    return None if value is None else value.strip()
