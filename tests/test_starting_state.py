"""B1 policy: an immutable commit plus frozen admitted material, or a refusal."""

from __future__ import annotations

import unittest

from broodling import UnsupportedStartingState, resolve_starting_state
from broodling.starting_state import admitted_material_digest
from support import StoreTestCase, git, make_repository, move_head


class SupportedStartingStateTests(StoreTestCase):
    def setUp(self) -> None:
        super().setUp()
        self.repository = self.root / "source"
        self.b1 = make_repository(self.repository)

    def test_a_clean_checkout_resolves_head_to_an_immutable_commit(self) -> None:
        state = resolve_starting_state(self.repository)
        self.assertEqual(state.commit_oid, self.b1)
        self.assertEqual(state.requested_revision, "HEAD")
        self.assertEqual(len(state.commit_oid), 40)

    def test_the_repository_identity_is_the_shared_git_directory(self) -> None:
        state = resolve_starting_state(self.repository)
        self.assertEqual(state.repository, str((self.repository / ".git").resolve()))

    def test_an_explicit_commit_is_pinned_even_after_head_moves(self) -> None:
        move_head(self.repository)
        state = resolve_starting_state(self.repository, self.b1)
        self.assertEqual(state.commit_oid, self.b1)

    def test_resolving_the_same_request_twice_pins_the_same_commit(self) -> None:
        first = resolve_starting_state(self.repository)
        second = resolve_starting_state(self.repository)
        self.assertEqual(first, second)

    def test_head_after_drift_resolves_the_new_commit_not_the_old_one(self) -> None:
        b2 = move_head(self.repository)
        self.assertNotEqual(b2, self.b1)
        self.assertEqual(resolve_starting_state(self.repository).commit_oid, b2)


class UnsupportedStartingStateTests(StoreTestCase):
    """Unsupported starting material fails explicitly; it is never dropped."""

    def setUp(self) -> None:
        super().setUp()
        self.repository = self.root / "source"
        self.b1 = make_repository(self.repository)

    def test_a_modified_tracked_file_refuses_and_names_the_file(self) -> None:
        (self.repository / "README.md").write_text("edited\n", encoding="utf-8")
        with self.assertRaises(UnsupportedStartingState) as raised:
            resolve_starting_state(self.repository)
        self.assertIn("README.md", str(raised.exception))
        self.assertIn("uncommitted or untracked", str(raised.exception))

    def test_staged_but_uncommitted_material_refuses(self) -> None:
        (self.repository / "staged.txt").write_text("staged\n", encoding="utf-8")
        git(self.repository, "add", "staged.txt")
        with self.assertRaises(UnsupportedStartingState) as raised:
            resolve_starting_state(self.repository)
        self.assertIn("staged.txt", str(raised.exception))

    def test_an_untracked_file_refuses_rather_than_being_omitted_from_b1(self) -> None:
        (self.repository / "generated.txt").write_text("new\n", encoding="utf-8")
        with self.assertRaises(UnsupportedStartingState) as raised:
            resolve_starting_state(self.repository)
        self.assertIn("generated.txt", str(raised.exception))

    def test_an_untracked_file_in_a_subdirectory_is_named_not_summarized(self) -> None:
        nested = self.repository / "deep" / "nested"
        nested.mkdir(parents=True)
        (nested / "artifact.bin").write_bytes(b"\x00")
        with self.assertRaises(UnsupportedStartingState) as raised:
            resolve_starting_state(self.repository)
        self.assertIn("deep/nested/artifact.bin", str(raised.exception))

    def test_refusing_leaves_the_working_tree_untouched(self) -> None:
        (self.repository / "generated.txt").write_text("new\n", encoding="utf-8")
        with self.assertRaises(UnsupportedStartingState):
            resolve_starting_state(self.repository)
        self.assertEqual(git(self.repository, "rev-parse", "HEAD"), self.b1)
        self.assertTrue((self.repository / "generated.txt").exists())
        self.assertIn("generated.txt", git(self.repository, "status", "--porcelain"))

    def test_a_revision_that_is_not_a_commit_refuses(self) -> None:
        with self.assertRaises(UnsupportedStartingState):
            resolve_starting_state(self.repository, "refs/heads/does-not-exist")

    def test_a_tree_object_is_not_an_acceptable_b1(self) -> None:
        tree = git(self.repository, "rev-parse", "HEAD^{tree}")
        with self.assertRaises(UnsupportedStartingState):
            resolve_starting_state(self.repository, tree)

    def test_a_directory_that_is_not_a_repository_refuses(self) -> None:
        plain = self.root / "plain"
        plain.mkdir()
        with self.assertRaises(UnsupportedStartingState):
            resolve_starting_state(plain)


class AdmittedMaterialDigestTests(unittest.TestCase):
    """The frozen-material half of B1 is a fingerprint, not a second copy."""

    def test_order_of_attribution_does_not_change_the_digest(self) -> None:
        pairs = [("src-a", "1" * 64), ("src-b", "2" * 64)]
        self.assertEqual(
            admitted_material_digest(pairs), admitted_material_digest(reversed(pairs))
        )

    def test_changed_content_changes_the_digest(self) -> None:
        original = admitted_material_digest([("src-a", "1" * 64)])
        edited = admitted_material_digest([("src-a", "3" * 64)])
        self.assertNotEqual(original, edited)

    def test_an_added_source_changes_the_digest(self) -> None:
        one = admitted_material_digest([("src-a", "1" * 64)])
        two = admitted_material_digest([("src-a", "1" * 64), ("src-b", "2" * 64)])
        self.assertNotEqual(one, two)


if __name__ == "__main__":
    unittest.main()
