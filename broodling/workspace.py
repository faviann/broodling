"""Disposable-worktree convention and durable workspace-root policy.

One marker file, ``.broodling-disposable-worktree``, names a directory tree as
Attempt scaffolding that may be retired wholesale. It has two jobs, and they are
the same job seen from both sides:

* the Broodling store refuses to live under one, so the durable record outlives
  the Attempt it describes (issue #12);
* an allocated Attempt enclosure carries one, naming the Attempt that owns it, so
  provisioning can tell *its* directory from somebody else's (issue #13).

The marker sits on the enclosure directory, not inside the Git worktree, so the
checked-out candidate tree is exactly B1 and nothing else. That keeps Broodling's
own scaffolding out of the material a later phase will read as candidate source.

Nothing here runs Git or touches the store; it is path and naming policy only.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .errors import StoreLocationError, UnsupportedWorkspaceRoot

#: Marker file that names a directory as a disposable Attempt enclosure.
DISPOSABLE_WORKTREE_MARKER = ".broodling-disposable-worktree"

#: Directory the Git worktree itself is checked out into, inside the enclosure.
WORKTREE_DIRECTORY = "worktree"

#: Lock file, in the enclosure, that makes materializing one Attempt's worktree a
#: single-writer operation on this host. It sits beside the marker rather than
#: inside the checkout, for the same reason the marker does: the candidate tree
#: is exactly B1 and nothing else.
PROVISIONING_LOCK = ".broodling-provisioning.lock"

#: Roots the qualified V1 profile does not accept as a workspace root. The
#: profile requires a dedicated *non-temporary* root: a worktree that a reboot,
#: a tmpfs eviction or a system cleaner can remove out from under an admitted
#: Attempt is not durable Attempt state.
NON_DURABLE_ROOTS: tuple[str, ...] = ("/tmp", "/dev/shm", "/var/tmp", "/run")

#: Longest Work Unit slug used in a directory or branch name. The durable
#: uniqueness authority is the store's constraints, not the name; the slug is
#: readability only.
MAX_SLUG_LENGTH = 60

#: Hex characters of the Attempt id carried into the allocated names.
ATTEMPT_NAME_LENGTH = 24

BRANCH_NAMESPACE = "broodling"


def enclosing_disposable_worktree(path: Path) -> Path | None:
    """The nearest ancestor (or ``path``) carrying the disposable marker."""

    resolved = Path(path).expanduser()
    resolved = resolved if resolved.is_absolute() else resolved.resolve()
    for directory in (resolved, *resolved.parents):
        if (directory / DISPOSABLE_WORKTREE_MARKER).exists():
            return directory
    return None


def assert_outside_disposable_worktree(path: Path) -> None:
    """Refuse a store path inside a disposable Attempt enclosure.

    An Attempt's worktree is retired wholesale; the Contract/admission record
    must outlive that.
    """

    enclosure = enclosing_disposable_worktree(Path(path).expanduser().resolve())
    if enclosure is not None:
        raise StoreLocationError(
            f"{path} is inside disposable Attempt worktree {enclosure}; the "
            "Broodling store must outlive any Attempt"
        )


def assert_durable_workspace_root(root: Path) -> Path:
    """Return the resolved workspace root, or refuse it.

    Fails closed on a relative path, a temporary/volatile root and a root that is
    itself inside somebody's disposable enclosure. It does not create the root:
    a workspace root is deployment configuration, not something admission
    invents.
    """

    candidate = Path(root).expanduser()
    if not candidate.is_absolute():
        raise UnsupportedWorkspaceRoot(
            f"workspace root {root} must be an absolute path"
        )
    resolved = candidate.resolve()
    for forbidden in NON_DURABLE_ROOTS:
        forbidden_path = Path(forbidden)
        if resolved == forbidden_path or forbidden_path in resolved.parents:
            raise UnsupportedWorkspaceRoot(
                f"workspace root {resolved} is under {forbidden}, which the "
                "qualified V1 profile does not accept as a durable worktree root"
            )
    enclosure = enclosing_disposable_worktree(resolved)
    if enclosure is not None:
        raise UnsupportedWorkspaceRoot(
            f"workspace root {resolved} is inside disposable Attempt worktree "
            f"{enclosure}; one Attempt's workspace cannot contain another's"
        )
    return resolved


def work_unit_slug(owner: str, repository: str, issue_number: int) -> str:
    slug = f"{owner}-{repository}-{issue_number}"
    safe = "".join(
        character if character.isalnum() or character in "._-" else "-"
        for character in slug
    ).strip("-.")
    return safe[:MAX_SLUG_LENGTH] or "work-unit"


def attempt_name(attempt_id: str) -> str:
    """The readable fragment of an Attempt id used in allocated names."""

    _, _, body = attempt_id.partition("-")
    return (body or attempt_id)[:ATTEMPT_NAME_LENGTH]


@dataclass(frozen=True, slots=True)
class WorktreeAllocation:
    """A stable worktree/branch identity, chosen before any host-side work."""

    enclosure: Path
    worktree_path: Path
    branch: str

    @property
    def marker_path(self) -> Path:
        return self.enclosure / DISPOSABLE_WORKTREE_MARKER


def allocate(
    workspace_root: Path,
    *,
    owner: str,
    repository: str,
    issue_number: int,
    attempt_id: str,
) -> WorktreeAllocation:
    """Derive the worktree path and branch for one Attempt.

    Derived, not allocated from a counter: the same Attempt always resolves the
    same path and branch, so a retry after a crash converges on the directory the
    interrupted run was creating instead of claiming a second one.
    """

    slug = work_unit_slug(owner, repository, issue_number)
    name = attempt_name(attempt_id)
    enclosure = Path(workspace_root) / f"{slug}-{name}"
    return WorktreeAllocation(
        enclosure=enclosure,
        worktree_path=enclosure / WORKTREE_DIRECTORY,
        branch=f"{BRANCH_NAMESPACE}/{slug}/{name}",
    )


def write_marker(enclosure: Path, attempt_id: str) -> None:
    """Mark an enclosure as disposable scaffolding owned by ``attempt_id``.

    Idempotent: re-marking an enclosure this Attempt already owns rewrites the
    same bytes, and a marker naming a different Attempt is left alone for the
    caller to refuse.
    """

    enclosure.mkdir(parents=True, exist_ok=True)
    marker = enclosure / DISPOSABLE_WORKTREE_MARKER
    if marker.exists() and read_marker(enclosure) != attempt_id:
        return
    marker.write_text(f"{attempt_id}\n", encoding="utf-8")


def read_marker(enclosure: Path) -> str | None:
    """The Attempt id an enclosure claims, or ``None`` when unmarked."""

    marker = enclosure / DISPOSABLE_WORKTREE_MARKER
    if not marker.is_file():
        return None
    return marker.read_text(encoding="utf-8").strip() or None
