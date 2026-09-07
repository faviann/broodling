"""One immutable Attempt per Work Unit, permanently bound to one revision and B1."""

from __future__ import annotations

import sqlite3
import unittest

from broodling import (
    AttemptAdmissionError,
    AttemptConflict,
    UnsupportedStartingState,
    UnsupportedWorkspaceRoot,
)
from broodling.starting_state import admitted_material_digest, resolve_starting_state
from support import AttemptTestCase, durable_test_root, git, move_head


class AttemptAdmissionTests(AttemptTestCase):
    def test_an_attempt_is_bound_to_one_admitted_contract_revision_and_b1(self) -> None:
        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        self.assertEqual(
            attempt.contract_revision_id, self.revision.contract_revision_id
        )
        self.assertEqual(attempt.work_unit_id, self.work_unit.work_unit_id)
        self.assertEqual(attempt.b1_commit_oid, self.b1)
        self.assertTrue(attempt.is_current)

    def test_b1_records_the_frozen_admitted_material_of_that_revision(self) -> None:
        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        material = self.store.contract_source_material(
            self.revision.contract_revision_id
        )
        self.assertEqual(
            attempt.b1_material_sha256,
            admitted_material_digest(
                (item.source_id, item.content_sha256) for item in material
            ),
        )

    def test_the_attempt_is_the_current_attempt_of_its_work_unit(self) -> None:
        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        current = self.store.current_attempt(self.work_unit.work_unit_id)
        self.assertEqual(current, attempt)

    def test_a_work_unit_has_no_current_attempt_before_admission(self) -> None:
        self.assertIsNone(self.store.current_attempt(self.work_unit.work_unit_id))

    def test_admission_allocates_the_worktree_identity_before_any_host_work(
        self,
    ) -> None:
        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        assignment = self.store.worktree_assignment(attempt.attempt_id)
        self.assertEqual(assignment.state, "allocated")
        self.assertIsNone(assignment.provisioned_at)
        self.assertTrue(
            str(assignment.worktree_path).startswith(str(self.workspace_root))
        )
        self.assertIn(self.work_unit.repository, assignment.branch)
        self.assertFalse(assignment.path.exists())

    def test_an_unadmitted_contract_revision_cannot_carry_an_attempt(self) -> None:
        work_unit, source = self.admitted_work_unit()
        other = self.store.record_contract_revision(
            self.contract(work_unit, source, notes="a second meaning")
        )
        with self.assertRaises(AttemptAdmissionError):
            self.provisioner().admit(other.contract_revision_id, self.repository)
        self.assertIsNone(self.store.current_attempt(work_unit.work_unit_id))

    def test_an_unknown_contract_revision_is_refused(self) -> None:
        from broodling import UnknownRecord

        with self.assertRaises(UnknownRecord):
            self.provisioner().admit("cr-absent", self.repository)


class RepeatedAndConflictingAdmissionTests(AttemptTestCase):
    def test_repeating_the_identical_request_returns_the_same_attempt(self) -> None:
        provisioner = self.provisioner()
        first = provisioner.admit(self.revision.contract_revision_id, self.repository)
        second = provisioner.admit(self.revision.contract_revision_id, self.repository)
        self.assertEqual(first, second)
        self.assertEqual(self.attempt_count(), 1)

    def test_repeating_it_keeps_the_original_worktree_allocation(self) -> None:
        provisioner = self.provisioner()
        first = provisioner.admit(self.revision.contract_revision_id, self.repository)
        allocation = self.store.worktree_assignment(first.attempt_id)
        provisioner.admit(self.revision.contract_revision_id, self.repository)
        self.assertEqual(self.store.worktree_assignment(first.attempt_id), allocation)

    def test_a_different_workspace_root_does_not_move_the_allocation(self) -> None:
        first = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        elsewhere = durable_test_root("broodling-p2-other-root-")
        self.addCleanup(_remove, elsewhere)
        again = self.provisioner(elsewhere).admit(
            self.revision.contract_revision_id, self.repository
        )
        self.assertEqual(again, first)
        assignment = self.store.worktree_assignment(first.attempt_id)
        self.assertTrue(
            str(assignment.worktree_path).startswith(str(self.workspace_root))
        )

    def test_an_admission_at_a_different_b1_conflicts(self) -> None:
        provisioner = self.provisioner()
        first = provisioner.admit(self.revision.contract_revision_id, self.repository)
        move_head(self.repository)
        with self.assertRaises(AttemptConflict) as raised:
            provisioner.admit(self.revision.contract_revision_id, self.repository)
        self.assertIn(first.attempt_id, str(raised.exception))
        self.assertEqual(self.attempt_count(), 1)
        self.assertEqual(self.store.current_attempt(self.work_unit.work_unit_id), first)

    def test_a_second_contract_revision_cannot_open_a_second_current_attempt(
        self,
    ) -> None:
        provisioner = self.provisioner()
        first = provisioner.admit(self.revision.contract_revision_id, self.repository)
        work_unit, source = self.admitted_work_unit()
        other = self.store.record_contract_revision(
            self.contract(work_unit, source, notes="externally authorized new meaning")
        )
        self.store.admit(other.contract_revision_id)
        with self.assertRaises(AttemptConflict):
            provisioner.admit(other.contract_revision_id, self.repository)
        self.assertEqual(self.store.current_attempt(self.work_unit.work_unit_id), first)

    def test_a_conflicting_admission_allocates_no_worktree(self) -> None:
        provisioner = self.provisioner()
        provisioner.admit(self.revision.contract_revision_id, self.repository)
        move_head(self.repository)
        with self.assertRaises(AttemptConflict):
            provisioner.admit(self.revision.contract_revision_id, self.repository)
        rows = self.store.connection.execute(
            "SELECT count(*) AS total FROM worktree_assignments"
        ).fetchone()
        self.assertEqual(rows["total"], 1)

    def attempt_count(self) -> int:
        row = self.store.connection.execute(
            "SELECT count(*) AS total FROM attempts"
        ).fetchone()
        return int(row["total"])


class UnsupportedAdmissionTests(AttemptTestCase):
    def test_dirty_starting_material_refuses_before_anything_is_written(self) -> None:
        (self.repository / "generated.txt").write_text("new\n", encoding="utf-8")
        with self.assertRaises(UnsupportedStartingState):
            self.provisioner().admit(
                self.revision.contract_revision_id, self.repository
            )
        self.assertIsNone(self.store.current_attempt(self.work_unit.work_unit_id))
        rows = self.store.connection.execute(
            "SELECT count(*) AS total FROM worktree_assignments"
        ).fetchone()
        self.assertEqual(rows["total"], 0)

    def test_a_temporary_workspace_root_is_refused(self) -> None:
        for candidate in ("/tmp/broodling-worktrees", "/dev/shm/broodling"):
            with (
                self.subTest(root=candidate),
                self.assertRaises(UnsupportedWorkspaceRoot),
            ):
                self.provisioner(candidate)

    def test_a_relative_workspace_root_is_refused(self) -> None:
        with self.assertRaises(UnsupportedWorkspaceRoot):
            self.provisioner("relative/worktrees")

    def test_a_workspace_root_inside_the_repository_is_refused(self) -> None:
        nested = self.repository / "worktrees"
        nested.mkdir()
        with self.assertRaises(UnsupportedWorkspaceRoot):
            self.provisioner(nested).admit(
                self.revision.contract_revision_id, self.repository
            )

    def test_a_workspace_root_inside_a_disposable_worktree_is_refused(self) -> None:
        from broodling import DISPOSABLE_WORKTREE_MARKER

        other = durable_test_root("broodling-p2-nested-")
        self.addCleanup(_remove, other)
        (other / DISPOSABLE_WORKTREE_MARKER).write_text("at-other\n", encoding="utf-8")
        with self.assertRaises(UnsupportedWorkspaceRoot):
            self.provisioner(other / "inside")


class AttemptImmutabilityTests(AttemptTestCase):
    def setUp(self) -> None:
        super().setUp()
        self.attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )

    def test_an_attempt_cannot_be_rebound_to_another_contract_revision(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "UPDATE attempts SET contract_revision_id = 'cr-other' "
                "WHERE attempt_id = ?",
                (self.attempt.attempt_id,),
            )
        self.assertEqual(self.store.get_attempt(self.attempt.attempt_id), self.attempt)

    def test_b1_cannot_be_moved_in_place(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "UPDATE attempts SET b1_commit_oid = ? WHERE attempt_id = ?",
                ("0" * 40, self.attempt.attempt_id),
            )

    def test_an_attempt_cannot_be_deleted(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                "DELETE FROM attempts WHERE attempt_id = ?", (self.attempt.attempt_id,)
            )

    def test_a_second_current_attempt_cannot_be_written_even_by_raw_sql(self) -> None:
        with self.assertRaises(sqlite3.IntegrityError):
            self.store.connection.execute(
                """
                INSERT INTO attempts (
                    attempt_id, work_unit_id, contract_revision_id, is_current,
                    b1_repository, b1_commit_oid, b1_material_sha256,
                    b1_requested_revision, admitted_at
                ) VALUES ('at-rival', ?, ?, 1, ?, ?, ?, 'HEAD', 'now')
                """,
                (
                    self.attempt.work_unit_id,
                    self.attempt.contract_revision_id,
                    self.attempt.b1_repository,
                    "0" * 40,
                    "0" * 64,
                ),
            )

    def test_b1_survives_live_head_drift_and_a_restart(self) -> None:
        b2 = move_head(self.repository)
        store = self.reopen()
        recovered = store.current_attempt(self.work_unit.work_unit_id)
        self.assertEqual(recovered.b1_commit_oid, self.b1)
        self.assertNotEqual(recovered.b1_commit_oid, b2)
        self.assertEqual(git(self.repository, "rev-parse", "HEAD"), b2)

    def test_a_later_issue_edit_does_not_change_the_admitted_material(self) -> None:
        from broodling import SourceSubmission

        self.store.entitle_source(
            self.work_unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=self.work_unit.issue_locator,
                content=b"the issue text was edited after admission\n",
            ),
        )
        material = self.store.contract_source_material(
            self.revision.contract_revision_id
        )
        self.assertEqual(
            self.store.get_attempt(self.attempt.attempt_id).b1_material_sha256,
            admitted_material_digest(
                (item.source_id, item.content_sha256) for item in material
            ),
        )

    def test_the_recorded_b1_still_resolves_after_drift(self) -> None:
        move_head(self.repository)
        state = resolve_starting_state(self.repository, self.attempt.b1_commit_oid)
        self.assertEqual(state.commit_oid, self.b1)


def _remove(path) -> None:
    import shutil

    shutil.rmtree(path, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
