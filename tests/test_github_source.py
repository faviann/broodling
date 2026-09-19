"""Primary source acquisition preserves bytes and refuses ambiguous identity."""

import json
import subprocess
import unittest
from dataclasses import replace
from datetime import datetime, timezone
from unittest.mock import patch

from broodling.entitlement import evaluate_entitlement
from broodling.github_source import GitHubSourceError, acquire_github_issue
from broodling.identity import WorkReference


def issue_resource(**changes):
    return {
        "number": 75,
        "node_id": "I_example75",
        "html_url": "https://github.com/Faviann/Broodling/issues/75",
        "repository_url": "https://api.github.com/repos/Faviann/Broodling",
        "title": "Acquire the primary issue",
        "body": "See https://example.test/governing and #76.\nDo not fetch either.",
        "comments_url": "https://api.github.com/repos/faviann/broodling/issues/75/comments",
        **changes,
    }


class GitHubSourceTests(unittest.TestCase):
    def setUp(self):
        self.reference = WorkReference.parse("Faviann/Broodling", "#75")

    def acquire(self, content, reference=None):
        with patch(
            "broodling.github_source.subprocess.run",
            return_value=(subprocess.CompletedProcess("gh", 0, content, b"")),
        ) as run:
            acquired = acquire_github_issue(reference or self.reference)
        return acquired, run

    def test_one_pinned_read_preserves_exact_bytes_and_primary_issue_provenance(self):
        content = (json.dumps(issue_resource(), indent=3) + "\n\n").encode()
        before = datetime.now(timezone.utc)
        acquired, run = self.acquire(content)
        after = datetime.now(timezone.utc)
        self.assertEqual(
            acquired.reference, replace(self.reference, issue_identity="I_example75")
        )
        self.assertIsNone(self.reference.issue_identity)
        self.assertEqual(acquired.source.content, content)
        self.assertEqual(acquired.source.locator, self.reference.issue_locator)
        self.assertEqual(acquired.source.kind, "primary_issue")
        self.assertEqual(acquired.source.media_type, "application/json")
        self.assertEqual(acquired.source.origin, "broodling_policy")
        self.assertEqual(
            evaluate_entitlement(acquired.source).entitled_by, "broodling_policy"
        )
        retrieved = datetime.fromisoformat(acquired.source.retrieved_at)
        self.assertLessEqual(before, retrieved)
        self.assertLessEqual(retrieved, after)
        run.assert_called_once_with(
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
                "/repos/faviann/broodling/issues/75",
            ],
            capture_output=True,
            check=True,
            timeout=30,
        )

    def test_null_body_and_matching_pinned_identity_are_valid(self):
        reference = replace(self.reference, issue_identity="I_example75")
        acquired, _ = self.acquire(
            json.dumps(issue_resource(body=None)).encode(), reference
        )
        self.assertEqual(acquired.reference, reference)
        self.assertIsNone(json.loads(acquired.source.content)["body"])

    def test_non_github_or_noncanonical_reference_refuses_before_acquisition(self):
        references = (
            WorkReference.parse("https://gitlab.com/faviann/broodling", 75),
            replace(self.reference, owner="../another"),
            replace(self.reference, repository="broodling?override=1"),
            replace(self.reference, issue_number=True),
        )
        with patch("broodling.github_source.subprocess.run") as run:
            for reference in references:
                with (
                    self.subTest(reference=reference),
                    self.assertRaises(GitHubSourceError),
                ):
                    acquire_github_issue(reference)
        run.assert_not_called()

    def test_malformed_or_mismatched_response_refuses(self):
        changes = (
            {"number": 76},
            {"number": "75"},
            {"number": True},
            {"html_url": "https://github.com/faviann/other/issues/75"},
            {"html_url": "https://github.com/faviann/broodling/pull/75"},
            {"html_url": self.reference.issue_locator + "#issuecomment-1"},
            {"html_url": self.reference.issue_locator + "?comment=1"},
            {"html_url": "https://example.test/faviann/broodling/issues/75"},
            {"repository_url": "https://api.github.com/repos/other/broodling"},
            {"pull_request": None},
            {"title": ""},
            {"title": 1},
            {"body": {}},
            {"node_id": None},
            {"node_id": ""},
        )
        resources = [issue_resource(**change) for change in changes]
        missing_body = issue_resource()
        del missing_body["body"]
        contents = [
            json.dumps(resource).encode() for resource in (*resources, missing_body)
        ]
        contents.extend([b"{", b"[]", b"null", b"\xff", "not bytes"])
        for content in contents:
            with self.subTest(content=content), self.assertRaises(GitHubSourceError):
                self.acquire(content)

    def test_conflicting_pinned_identity_refuses(self):
        reference = replace(self.reference, issue_identity="I_original")
        with self.assertRaises(GitHubSourceError):
            self.acquire(json.dumps(issue_resource()).encode(), reference)

    def test_transport_refusals_do_not_expose_diagnostics(self):
        for error in (
            FileNotFoundError("credential sentinel"),
            subprocess.CalledProcessError(1, "gh", stderr=b"credential sentinel"),
            subprocess.TimeoutExpired("gh", 30, stderr=b"credential sentinel"),
        ):
            with self.subTest(error=type(error).__name__):
                with patch("broodling.github_source.subprocess.run", side_effect=error):
                    with self.assertRaises(GitHubSourceError) as raised:
                        acquire_github_issue(self.reference)
                self.assertNotIn("credential sentinel", str(raised.exception))
                self.assertTrue(raised.exception.__suppress_context__)


if __name__ == "__main__":
    unittest.main()
