"""Exact local Git object retention through Broodling-owned refs."""

from __future__ import annotations

import shlex
import tempfile
import unittest
from pathlib import Path

from broodling.git import retain_commit
from support import git, make_repository


class GitRetentionHookTests(unittest.TestCase):
    def test_retaining_a_commit_does_not_run_reference_transaction_hooks(self) -> None:
        with tempfile.TemporaryDirectory(prefix="broodling-retention-") as directory:
            root = Path(directory)
            repository = root / "repository"
            commit_oid = make_repository(repository)
            hook_path = repository / ".git" / "hooks" / "reference-transaction"
            sentinel = root / "hook-ran"
            hook_path.write_text(
                "#!/bin/sh\n"
                f"printf '%s\\n' called >> {shlex.quote(str(sentinel))}\n",
                encoding="utf-8",
            )
            hook_path.chmod(0o755)

            # Prove the repository hook is active before testing Broodling's
            # narrower administrative ref update.
            git(repository, "update-ref", "refs/heads/hook-check", commit_oid)
            self.assertTrue(sentinel.is_file())
            git(repository, "update-ref", "-d", "refs/heads/hook-check")
            sentinel.unlink()

            starting_ref = f"refs/broodling/starting/{commit_oid}"
            retain_commit(repository, starting_ref, commit_oid)

            self.assertFalse(sentinel.exists())
            self.assertEqual(
                git(repository, "show-ref", "--verify", "--hash", starting_ref),
                commit_oid,
            )


if __name__ == "__main__":
    unittest.main()
