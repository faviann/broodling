"""One dedicated attached worktree per Attempt, at exactly B1, owned exclusively."""

from __future__ import annotations

import shutil
import sqlite3
import unittest

from broodling import (
    DISPOSABLE_WORKTREE_MARKER,
    BroodlingStore,
    StoreLocationError,
    WorktreeOwnershipConflict,
)
from support import AttemptTestCase, git, move_head, tracked_files, work_reference


class ProvisioningTests(AttemptTestCase):
    def setUp(self) -> None:
        super().setUp()
        self.provisioned = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )

    def test_the_worktree_is_attached_and_starts_at_b1(self) -> None:
        path = self.provisioned.path
        self.assertTrue(path.is_dir())
        self.assertEqual(git(path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(
            git(path, "rev-parse", "--abbrev-ref", "HEAD"), self.provisioned.branch
        )

    def test_the_branch_is_local_unique_and_created_at_b1(self) -> None:
        self.assertEqual(
            git(self.repository, "rev-parse", self.provisioned.branch), self.b1
        )
        branches = git(self.repository, "branch", "--format=%(refname:short)").split()
        self.assertEqual(branches.count(self.provisioned.branch), 1)

    def test_the_worktree_is_registered_against_the_source_repository(self) -> None:
        listing = git(self.repository, "worktree", "list", "--porcelain")
        self.assertIn(str(self.provisioned.path), listing)

    def test_the_worktree_holds_b1_and_nothing_else(self) -> None:
        path = self.provisioned.path
        self.assertEqual(tracked_files(path), ("README.md",))
        self.assertEqual(
            git(path, "rev-parse", "HEAD^{tree}"),
            git(self.repository, "rev-parse", f"{self.b1}^{{tree}}"),
        )
        self.assertEqual(git(path, "status", "--porcelain", "-uall"), "")

    def test_the_disposable_marker_sits_outside_the_candidate_tree(self) -> None:
        enclosure = self.provisioned.path.parent
        marker = enclosure / DISPOSABLE_WORKTREE_MARKER
        self.assertTrue(marker.is_file())
        self.assertEqual(
            marker.read_text(encoding="utf-8").strip(),
            self.provisioned.attempt.attempt_id,
        )
        self.assertFalse((self.provisioned.path / DISPOSABLE_WORKTREE_MARKER).exists())

    def test_the_store_refuses_to_live_inside_the_provisioned_worktree(self) -> None:
        self.store.close()
        with self.assertRaises(StoreLocationError):
            BroodlingStore.open(self.provisioned.path / "state" / "broodling.sqlite3")

    def test_provisioning_is_acknowledged_durably(self) -> None:
        assignment = self.reopen().worktree_assignment(
            self.provisioned.attempt.attempt_id
        )
        self.assertTrue(assignment.provisioned)
        self.assertIsNotNone(assignment.provisioned_at)


class IdempotentProvisioningTests(AttemptTestCase):
    def setUp(self) -> None:
        super().setUp()
        self.provisioner_ = self.provisioner()
        self.provisioned = self.provisioner_.admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )

    def test_provisioning_again_creates_no_second_worktree_or_branch(self) -> None:
        again = self.provisioner_.provision(self.provisioned.attempt.attempt_id)
        self.assertEqual(again.path, self.provisioned.path)
        self.assertEqual(again.branch, self.provisioned.branch)
        self.assertEqual(self.worktree_count(), 2)
        self.assertEqual(self.branch_count(), 2)

    def test_repeating_the_whole_operation_converges_on_one_attempt(self) -> None:
        again = self.provisioner_.admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )
        self.assertEqual(again.attempt, self.provisioned.attempt)
        self.assertEqual(self.worktree_count(), 2)

    def test_live_head_drift_does_not_change_repeated_provisioning(self) -> None:
        b2 = move_head(self.repository)
        again = self.provisioner_.provision(self.provisioned.attempt.attempt_id)
        self.assertEqual(git(again.path, "rev-parse", "HEAD"), self.b1)
        self.assertNotEqual(self.b1, b2)

    def test_a_removed_worktree_is_rematerialized_at_b1_not_live_head(self) -> None:
        move_head(self.repository)
        shutil.rmtree(self.provisioned.path)
        again = self.provisioner_.provision(self.provisioned.attempt.attempt_id)
        self.assertEqual(again.path, self.provisioned.path)
        self.assertEqual(git(again.path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(tracked_files(again.path), ("README.md",))
        self.assertEqual(self.worktree_count(), 2)

    def test_a_rematerialized_worktree_carries_no_prior_candidate_material(
        self,
    ) -> None:
        (self.provisioned.path / "candidate.txt").write_text("c1\n", encoding="utf-8")
        shutil.rmtree(self.provisioned.path)
        again = self.provisioner_.provision(self.provisioned.attempt.attempt_id)
        self.assertFalse((again.path / "candidate.txt").exists())
        self.assertEqual(git(again.path, "status", "--porcelain", "-uall"), "")

    def worktree_count(self) -> int:
        listing = git(self.repository, "worktree", "list", "--porcelain")
        return sum(1 for line in listing.splitlines() if line.startswith("worktree "))

    def branch_count(self) -> int:
        return len(git(self.repository, "branch", "--format=%(refname:short)").split())


class ExclusiveOwnershipTests(AttemptTestCase):
    """Two Work Units on one repository never share a worktree or a branch."""

    def setUp(self) -> None:
        super().setUp()
        self.first = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )
        self.second = self.provisioner().admit_and_provision(
            self.other_revision().contract_revision_id, self.repository
        )

    def other_revision(self):
        reference = work_reference(issue=99)
        work_unit = self.store.resolve_work_unit(reference)
        from support import ISSUE_BODY

        from broodling import SourceSubmission

        source = self.store.entitle_source(
            work_unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=work_unit.issue_locator,
                content=ISSUE_BODY,
            ),
        )
        revision = self.store.record_contract_revision(self.contract(work_unit, source))
        self.store.admit(revision.contract_revision_id)
        return revision

    def test_the_two_work_units_use_different_worktrees(self) -> None:
        self.assertNotEqual(self.first.path, self.second.path)
        self.assertNotEqual(self.first.branch, self.second.branch)
        self.assertNotEqual(
            self.first.attempt.work_unit_id, self.second.attempt.work_unit_id
        )

    def test_both_worktrees_start_at_the_same_b1_of_the_same_repository(self) -> None:
        for provisioned in (self.first, self.second):
            with self.subTest(path=provisioned.path):
                self.assertEqual(git(provisioned.path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(
            git(self.first.path, "rev-parse", "--git-common-dir"),
            str((self.repository / ".git").resolve()),
        )

    def test_neither_worktree_can_see_the_other_work_units_material(self) -> None:
        (self.first.path / "candidate.txt").write_text("c1\n", encoding="utf-8")
        self.assertFalse((self.second.path / "candidate.txt").exists())
        self.assertEqual(tracked_files(self.second.path), ("README.md",))
        self.assertEqual(git(self.second.path, "status", "--porcelain", "-uall"), "")

    def test_ownership_is_queryable_from_broodling_state(self) -> None:
        owner = self.store.worktree_owner(self.first.path)
        self.assertEqual(owner.attempt_id, self.first.attempt.attempt_id)
        self.assertEqual(owner.work_unit_id, self.first.attempt.work_unit_id)
        by_branch = self.store.branch_owner(
            self.second.attempt.b1_repository, self.second.branch
        )
        self.assertEqual(by_branch.attempt_id, self.second.attempt.attempt_id)

    def test_an_unowned_path_has_no_owner(self) -> None:
        self.assertIsNone(self.store.worktree_owner(self.workspace_root / "nobody"))

    def test_two_attempts_cannot_claim_one_worktree_path_even_by_raw_sql(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                """
                INSERT INTO worktree_assignments (
                    attempt_id, work_unit_id, repository, worktree_path, branch,
                    state, allocated_at, provisioned_at
                ) VALUES (?, ?, ?, ?, 'broodling/rival', 'allocated', 'now', NULL)
                """,
                (
                    self.second.attempt.attempt_id + "-rival",
                    self.second.attempt.work_unit_id,
                    self.second.attempt.b1_repository,
                    self.first.assignment.worktree_path,
                ),
            )

    def test_two_attempts_cannot_claim_one_branch_even_by_raw_sql(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                """
                INSERT INTO worktree_assignments (
                    attempt_id, work_unit_id, repository, worktree_path, branch,
                    state, allocated_at, provisioned_at
                ) VALUES (?, ?, ?, ?, ?, 'allocated', 'now', NULL)
                """,
                (
                    self.second.attempt.attempt_id + "-rival",
                    self.second.attempt.work_unit_id,
                    self.second.attempt.b1_repository,
                    str(self.workspace_root / "elsewhere"),
                    self.first.branch,
                ),
            )

    def test_worktree_ownership_cannot_be_deleted(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "DELETE FROM worktree_assignments WHERE attempt_id = ?",
                (self.first.attempt.attempt_id,),
            )


class ProvisioningRefusalTests(AttemptTestCase):
    def setUp(self) -> None:
        super().setUp()
        self.provisioner_ = self.provisioner()
        self.attempt = self.provisioner_.admit(
            self.revision.contract_revision_id, self.repository
        )
        self.assignment = self.store.worktree_assignment(self.attempt.attempt_id)

    def test_a_foreign_directory_at_the_allocated_path_is_refused(self) -> None:
        self.assignment.path.mkdir(parents=True)
        (self.assignment.path / "someone-elses.txt").write_text("x", encoding="utf-8")
        with self.assertRaises(WorktreeOwnershipConflict):
            self.provisioner_.provision(self.attempt.attempt_id)
        self.assertFalse(
            self.store.worktree_assignment(self.attempt.attempt_id).provisioned
        )

    def test_an_enclosure_marked_by_another_attempt_is_refused(self) -> None:
        enclosure = self.assignment.path.parent
        enclosure.mkdir(parents=True)
        (enclosure / DISPOSABLE_WORKTREE_MARKER).write_text(
            "at-somebody-else\n", encoding="utf-8"
        )
        with self.assertRaises(WorktreeOwnershipConflict) as raised:
            self.provisioner_.provision(self.attempt.attempt_id)
        self.assertIn("at-somebody-else", str(raised.exception))

    def test_a_branch_that_already_exists_elsewhere_is_not_reused(self) -> None:
        b2 = move_head(self.repository)
        git(self.repository, "branch", self.assignment.branch, b2)
        with self.assertRaises(WorktreeOwnershipConflict) as raised:
            self.provisioner_.provision(self.attempt.attempt_id)
        self.assertIn(self.b1, str(raised.exception))

    def test_provisioning_an_unknown_attempt_is_refused(self) -> None:
        from broodling import UnknownRecord

        with self.assertRaises(UnknownRecord):
            self.provisioner_.provision("at-absent")


class NoDeliveryEffectTests(AttemptTestCase):
    """Provisioning is host-local administrative setup, not a delivery effect."""

    def setUp(self) -> None:
        super().setUp()
        self.remote = self.root / "remote.git"
        git(self.root, "init", "--quiet", "--bare", str(self.remote))
        git(self.repository, "remote", "add", "origin", str(self.remote))
        self.provisioned = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )

    def test_nothing_is_published_to_the_remote(self) -> None:
        self.assertEqual(git(self.remote, "for-each-ref"), "")

    def test_the_source_checkout_is_left_exactly_as_it_was(self) -> None:
        self.assertEqual(git(self.repository, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(
            git(self.repository, "rev-parse", "--abbrev-ref", "HEAD"), "main"
        )
        self.assertEqual(git(self.repository, "status", "--porcelain", "-uall"), "")

    def test_the_only_new_local_ref_is_this_attempt_disposable_branch(self) -> None:
        branches = set(
            git(self.repository, "branch", "--format=%(refname:short)").split()
        )
        self.assertEqual(branches, {"main", self.provisioned.branch})


if __name__ == "__main__":
    unittest.main()
