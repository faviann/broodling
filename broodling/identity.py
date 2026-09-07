"""Stable Work Unit / Work Reference identity.

One Work Unit is one primary authoritative GitHub issue plus one target
repository. Repeated canonical ingress must resolve the same Work Unit, and a
different reference must not be able to alias onto it.
"""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass

from .errors import InvalidWorkReference

#: Bare ``owner/repo`` submissions are a documented forge default, not an
#: inference: any other host must be spelled out, so ``gitlab.com/o/r`` and
#: ``o/r`` never collapse onto the same canonical key.
DEFAULT_FORGE_HOST = "github.com"

_SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*://")
_SCP_LIKE = re.compile(r"^(?:(?P<user>[^@/]+)@)?(?P<host>[^:/@]+):(?P<path>[^:].*)$")
_SEGMENT = re.compile(r"^[A-Za-z0-9._-]+$")
_HOST = re.compile(r"^[A-Za-z0-9.-]+(?::[0-9]+)?$")
_ISSUE_URL_TAIL = re.compile(r"/(?:issues|-/issues)/(?P<number>[0-9]+)/?$")


def digest(*parts: str) -> str:
    """Deterministic hex digest over ``\\x1f``-separated parts."""

    return hashlib.sha256("\x1f".join(parts).encode("utf-8")).hexdigest()


def content_digest(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _strip_repository_path(path: str) -> tuple[str, str]:
    cleaned = path.strip().strip("/")
    if cleaned.endswith(".git"):
        cleaned = cleaned[: -len(".git")]
    segments = [segment for segment in cleaned.split("/") if segment]
    if len(segments) != 2:
        raise InvalidWorkReference(
            f"expected a single 'owner/repository' path, found {cleaned!r}"
        )
    owner, repository = segments
    for segment in (owner, repository):
        if not _SEGMENT.match(segment):
            raise InvalidWorkReference(f"unusable repository path segment {segment!r}")
    return owner.lower(), repository.lower()


def canonicalize_repository(submitted: str) -> tuple[str, str, str]:
    """Canonicalize a repository reference to ``(host, owner, repository)``.

    Accepts HTTPS/SSH URLs, ``scp``-like Git remotes, ``host/owner/repo`` and
    bare ``owner/repo``. Case and a ``.git`` suffix are not identity.
    """

    if not isinstance(submitted, str) or not submitted.strip():
        raise InvalidWorkReference("repository reference is empty")
    raw = submitted.strip().rstrip("/")

    if _SCHEME.match(raw):
        scheme, _, remainder = raw.partition("://")
        if scheme.lower() not in {"http", "https", "ssh", "git"}:
            raise InvalidWorkReference(f"unsupported repository scheme {scheme!r}")
        authority, _, path = remainder.partition("/")
        _, _, host = authority.rpartition("@")
        if not path:
            raise InvalidWorkReference(f"repository reference has no path: {raw!r}")
    else:
        scp = _SCP_LIKE.match(raw)
        if scp is not None:
            host = scp.group("host")
            path = scp.group("path")
        elif "/" in raw:
            head, _, tail = raw.partition("/")
            if "." in head and tail.count("/") == 1:
                host, path = head, tail
            else:
                host, path = DEFAULT_FORGE_HOST, raw
        else:
            raise InvalidWorkReference(f"unusable repository reference {raw!r}")

    host = host.strip().lower().rstrip(".")
    if not _HOST.match(host):
        raise InvalidWorkReference(f"unusable repository host {host!r}")
    owner, repository = _strip_repository_path(path)
    return host, owner, repository


def _canonicalize_issue(submitted: object) -> tuple[int, tuple[str, str, str] | None]:
    """Return ``(issue_number, repository_from_issue_url_or_None)``."""

    if isinstance(submitted, bool):
        raise InvalidWorkReference("issue reference must be a number or locator")
    if isinstance(submitted, int):
        number, embedded = submitted, None
    elif isinstance(submitted, str):
        raw = submitted.strip().rstrip("/")
        if not raw:
            raise InvalidWorkReference("issue reference is empty")
        tail = _ISSUE_URL_TAIL.search(raw)
        if tail is not None:
            number = int(tail.group("number"))
            embedded = canonicalize_repository(raw[: tail.start()])
        else:
            digits = raw.lstrip("#")
            if not digits.isdigit():
                raise InvalidWorkReference(f"unusable issue reference {raw!r}")
            number, embedded = int(digits), None
    else:
        raise InvalidWorkReference("issue reference must be a number or locator")
    if number <= 0:
        raise InvalidWorkReference(f"issue number must be positive, found {number}")
    return number, embedded


class WorkReferenceMismatch(InvalidWorkReference):
    """The submitted repository and issue locators disagree."""


@dataclass(frozen=True, slots=True)
class WorkReference:
    """The canonical identity of one Work Unit plus the retained raw ingress.

    ``repository_identity``/``issue_identity`` are optional opaque upstream
    identities (for example GitHub node ids). They are not part of the canonical
    key, because a Work Unit must resolve from a plain URL, but once pinned they
    make a renamed/recreated repository or issue a conflict instead of a silent
    alias onto the same ``owner/repo#number`` path.
    """

    host: str
    owner: str
    repository: str
    issue_number: int
    submitted_repository: str
    submitted_issue: str
    repository_identity: str | None = None
    issue_identity: str | None = None

    @classmethod
    def parse(
        cls,
        repository: str,
        issue: object,
        *,
        repository_identity: str | None = None,
        issue_identity: str | None = None,
    ) -> WorkReference:
        host, owner, name = canonicalize_repository(repository)
        issue_number, embedded = _canonicalize_issue(issue)
        if embedded is not None and embedded != (host, owner, name):
            raise WorkReferenceMismatch(
                "issue locator names "
                f"{'/'.join(embedded)} but the repository reference names "
                f"{host}/{owner}/{name}"
            )
        return cls(
            host=host,
            owner=owner,
            repository=name,
            issue_number=issue_number,
            submitted_repository=str(repository),
            submitted_issue=str(issue),
            repository_identity=repository_identity or None,
            issue_identity=issue_identity or None,
        )

    @property
    def key(self) -> str:
        """The canonical reference key. Equal keys are the same Work Unit."""

        return f"{self.host}/{self.owner}/{self.repository}#{self.issue_number}"

    @property
    def work_unit_id(self) -> str:
        """Durable Work Unit id, derived so it survives process and store restarts."""

        return "wu-" + digest("broodling.work-unit.v1", self.key)

    @property
    def issue_locator(self) -> str:
        """Canonical locator of the primary authoritative issue."""

        return (
            f"https://{self.host}/{self.owner}/{self.repository}"
            f"/issues/{self.issue_number}"
        )
