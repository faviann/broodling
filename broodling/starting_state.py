"""B1 — the original admitted starting state of an Attempt.

The first product profile supports exactly one representation of B1: an
**immutable Git commit object id**, plus the frozen admitted instruction/source
bytes the Contract was already built from. That pair is enough to rematerialize
the Attempt's starting point at any later time, however far live ``HEAD``, branch
tips or issue text have moved since.

Anything the caller wants as starting material that this pair cannot carry —
uncommitted edits, staged-but-uncommitted work, untracked files — is refused
here, by name. It is deliberately *not* committed on the caller's behalf, and not
quietly reclassified as environment state: silently dropping starting material is
the failure this refusal exists to prevent. Committing it would be Broodling
inventing admitted material no authority entitled.

This is not a general source snapshot or sealing system, and V1 does not need
one: B1 is a commit, and the commit is already immutable.
"""

from __future__ import annotations

from collections.abc import Iterable
from dataclasses import dataclass
from pathlib import Path

from . import git
from .errors import UnsupportedStartingState
from .identity import digest

#: How many uncommitted entries a refusal lists before summarizing. The point is
#: to name the material, not to reproduce an unbounded status output.
MAX_REPORTED_ENTRIES = 20


@dataclass(frozen=True, slots=True)
class StartingState:
    """The supported B1 starting point, resolved from a local repository."""

    repository: str
    commit_oid: str
    requested_revision: str


def resolve_starting_state(
    repository_path: Path | str, revision: str = "HEAD"
) -> StartingState:
    """Resolve and validate B1 for ``repository_path`` at ``revision``.

    Returns the pinned commit id, or raises ``UnsupportedStartingState`` naming
    exactly why the requested starting state is outside the supported profile.
    """

    path = Path(repository_path).expanduser()
    if not path.is_dir() or not git.is_repository(path):
        raise UnsupportedStartingState(
            f"{repository_path} is not a Git repository; the supported B1 policy "
            "is an immutable commit in a local Git repository"
        )

    if not git.is_bare(path):
        entries = git.uncommitted_entries(path)
        if entries:
            raise UnsupportedStartingState(_uncommitted_refusal(path, entries))

    commit = git.resolve_commit(path, revision)
    if commit is None:
        raise UnsupportedStartingState(
            f"{revision!r} does not name a commit in {repository_path}; B1 must be "
            "an exact immutable commit object"
        )

    return StartingState(
        repository=str(git.common_directory(path)),
        commit_oid=commit,
        requested_revision=revision,
    )


def _uncommitted_refusal(path: Path, entries: tuple[str, ...]) -> str:
    shown = entries[:MAX_REPORTED_ENTRIES]
    listed = "\n".join(f"  {entry}" for entry in shown)
    remainder = len(entries) - len(shown)
    if remainder > 0:
        listed += f"\n  ... and {remainder} more"
    return (
        f"{path} has uncommitted or untracked starting material, which the "
        "supported B1 policy (an immutable commit object) cannot represent:\n"
        f"{listed}\n"
        "Broodling will not commit this material on your behalf and will not drop "
        "it from B1. Commit or remove it, then admit the resulting commit."
    )


def admitted_material_digest(attributions: Iterable[tuple[str, str]]) -> str:
    """Digest of the frozen admitted instruction/source material.

    Computed over the ``(source_id, content_sha256)`` pairs the Contract revision
    pins, so B1 records *which exact bytes* were admitted alongside the commit.
    The bytes themselves stay in ``entitled_sources``; this is the fingerprint the
    Attempt is bound to, not a second copy of the material.
    """

    parts: list[str] = []
    for source_id, content_sha256 in sorted(attributions):
        parts.extend((source_id, content_sha256))
    return digest("broodling.admitted-material.v1", *parts)
