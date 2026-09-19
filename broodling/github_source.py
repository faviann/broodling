"""Acquire one primary GitHub issue without following its referenced material."""

from __future__ import annotations

import json
import subprocess
from dataclasses import dataclass, replace
from datetime import datetime, timezone

from .entitlement import PRIMARY_ISSUE, SourceSubmission
from .errors import BroodlingError, InvalidWorkReference
from .identity import WorkReference


class GitHubSourceError(BroodlingError):
    """GitHub acquisition failed or returned material for a different identity."""


@dataclass(frozen=True, slots=True)
class AcquiredIssue:
    reference: WorkReference
    source: SourceSubmission


def acquire_github_issue(reference: WorkReference) -> AcquiredIssue:
    """Fetch and identity-check the exact REST resource for a canonical issue.

    Only the issue resource is acquired. Comments, repository instructions and
    links in its body require their own explicit source acquisition/entitlement.
    The response bytes, including title/body and metadata, are never rewritten.
    """
    if reference.host != "github.com":
        raise GitHubSourceError("primary issue acquisition requires github.com")
    try:
        canonical = WorkReference.parse(
            f"https://{reference.host}/{reference.owner}/{reference.repository}",
            reference.issue_number,
        )
    except InvalidWorkReference:
        raise GitHubSourceError("primary issue reference is not canonical") from None
    if canonical.key != reference.key:
        raise GitHubSourceError("primary issue reference is not canonical")

    endpoint = (
        f"/repos/{reference.owner}/{reference.repository}"
        f"/issues/{reference.issue_number}"
    )
    try:
        result = subprocess.run(
            [
                "gh",
                "api",
                "--hostname",
                "github.com",
                "--method",
                "GET",
                "--header",
                "Accept: application/vnd.github+json",
                "--header",
                "X-GitHub-Api-Version: 2022-11-28",
                endpoint,
            ],
            capture_output=True,
            check=True,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError):
        # CLI diagnostics can contain credentials or private response details.
        raise GitHubSourceError("GitHub primary issue acquisition failed") from None

    content = result.stdout
    if not isinstance(content, bytes):
        raise GitHubSourceError("GitHub primary issue response is not exact bytes")
    try:
        payload = json.loads(content)
    except (ValueError, UnicodeError):
        raise GitHubSourceError("GitHub primary issue response is not JSON") from None
    if not isinstance(payload, dict):
        raise GitHubSourceError("GitHub primary issue response is not an object")
    if "pull_request" in payload:
        raise GitHubSourceError(
            "primary work reference must be an issue, not a pull request"
        )
    if (
        type(payload.get("number")) is not int
        or payload["number"] != reference.issue_number
    ):
        raise GitHubSourceError(
            "GitHub primary issue number does not match the reference"
        )
    expected_repository = (
        f"https://api.github.com/repos/{reference.owner}/{reference.repository}"
    )
    for field, expected in (
        ("html_url", reference.issue_locator),
        ("repository_url", expected_repository),
    ):
        actual = payload.get(field)
        if not isinstance(actual, str) or actual.lower() != expected:
            raise GitHubSourceError(
                "GitHub primary issue locator does not match the reference"
            )
    title, body, node_id = (
        payload.get("title"),
        payload.get("body"),
        payload.get("node_id"),
    )
    if not isinstance(title, str) or not title.strip():
        raise GitHubSourceError("GitHub primary issue response has no title")
    if "body" not in payload or (body is not None and not isinstance(body, str)):
        raise GitHubSourceError("GitHub primary issue response has an invalid body")
    if not isinstance(node_id, str) or not node_id.strip():
        raise GitHubSourceError("GitHub primary issue response has no stable identity")
    if reference.issue_identity is not None and reference.issue_identity != node_id:
        raise GitHubSourceError(
            "GitHub primary issue identity does not match the reference"
        )

    return AcquiredIssue(
        reference=replace(reference, issue_identity=node_id),
        source=SourceSubmission(
            kind=PRIMARY_ISSUE,
            locator=reference.issue_locator,
            content=content,
            media_type="application/json",
            retrieved_at=datetime.now(timezone.utc).isoformat(),
            origin="broodling_policy",
        ),
    )
