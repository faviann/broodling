"""Crash windows around Attempt admission and worktree provisioning.

Every case kills a child process at a chosen point and then asks the surviving
state one question: can the operation simply be repeated? The answer must always
be one recoverable current Attempt converging on the same worktree at the same
B1 — never a second Attempt, a second worktree or a second branch.
"""

from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path

from support import AttemptTestCase, git, move_head, tracked_files

CHILD = Path(__file__).resolve().parent / "attempt_crash_child.py"


class AttemptCrashTestCase(AttemptTestCase):
    def child(
        self,
        crash_point: str,
        *,
        revision: str = "HEAD",
        gate: Path | None = None,
        expect_return_code: int | None = 97,
    ) -> subprocess.CompletedProcess:
        completed = subprocess.run(
            self.command(crash_point, revision=revision, gate=gate),
            capture_output=True,
            text=True,
            check=False,
        )
        if expect_return_code is not None:
            self.assertEqual(
                completed.returncode,
                expect_return_code,
                f"child did not die as instructed: {completed.stderr}",
            )
        return completed

    def command(
        self, crash_point: str, *, revision: str = "HEAD", gate: Path | None = None
    ) -> list[str]:
        return [
            sys.executable,
            str(CHILD),
            str(self.store_path),
            str(self.workspace_root),
            str(self.repository),
            self.revision.contract_revision_id,
            crash_point,
            revision,
            str(gate) if gate is not None else "",
        ]

    @staticmethod
    def derived_attempt_id(completed: subprocess.CompletedProcess) -> str:
        attempt_id = completed.stdout.strip().splitlines()[0]
        assert attempt_id.startswith("at-"), completed.stdout
        return attempt_id

    def worktree_paths(self) -> tuple[str, ...]:
        listing = git(self.repository, "worktree", "list", "--porcelain")
        return tuple(
            line.removeprefix("worktree ")
            for line in listing.splitlines()
            if line.startswith("worktree ")
        )

    def branches(self) -> tuple[str, ...]:
        return tuple(
            git(self.repository, "branch", "--format=%(refname:short)").split()
        )


class InterruptedAdmissionTests(AttemptCrashTestCase):
    def test_a_crash_mid_admission_leaves_no_attempt_at_all(self) -> None:
        completed = self.child("mid_attempt_write")
        attempt_id = self.derived_attempt_id(completed)
        store = self.reopen()

        self.assertIsNone(store.current_attempt(self.work_unit.work_unit_id))
        self.assertIsNone(store.find_attempt(attempt_id))
        rows = store.connection.execute(
            "SELECT count(*) AS total FROM worktree_assignments"
        ).fetchone()
        self.assertEqual(rows["total"], 0)

    def test_a_half_written_admission_can_simply_be_requested_again(self) -> None:
        completed = self.child("mid_attempt_write")
        attempt_id = self.derived_attempt_id(completed)
        self.reopen()

        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        self.assertEqual(attempt.attempt_id, attempt_id)
        self.assertEqual(attempt.b1_commit_oid, self.b1)


class InterruptedProvisioningTests(AttemptCrashTestCase):
    def test_a_crash_before_worktree_creation_leaves_one_recoverable_attempt(
        self,
    ) -> None:
        completed = self.child("after_attempt_commit")
        attempt_id = self.derived_attempt_id(completed)
        store = self.reopen()

        current = store.current_attempt(self.work_unit.work_unit_id)
        self.assertEqual(current.attempt_id, attempt_id)
        assignment = store.worktree_assignment(attempt_id)
        self.assertFalse(assignment.provisioned)
        self.assertFalse(assignment.path.exists())
        self.assertNotIn(assignment.worktree_path, self.worktree_paths())

    def test_repeating_the_operation_converges_on_that_attempt(self) -> None:
        completed = self.child("after_attempt_commit")
        attempt_id = self.derived_attempt_id(completed)
        self.reopen()

        provisioned = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )
        self.assertEqual(provisioned.attempt.attempt_id, attempt_id)
        self.assertEqual(git(provisioned.path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(self.attempt_count(), 1)

    def test_no_replacement_attempt_is_allocated_after_live_head_drift(self) -> None:
        completed = self.child("after_attempt_commit")
        attempt_id = self.derived_attempt_id(completed)
        move_head(self.repository)
        self.reopen()

        # The recorded B1, not live HEAD, is what the retry materializes.
        provisioned = self.provisioner().provision(attempt_id)
        self.assertEqual(git(provisioned.path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(self.attempt_count(), 1)

    def test_a_lost_acknowledgement_does_not_create_a_second_worktree(self) -> None:
        completed = self.child("after_worktree_add")
        attempt_id = self.derived_attempt_id(completed)
        store = self.reopen()

        # Git already has the worktree; Broodling never heard about it.
        assignment = store.worktree_assignment(attempt_id)
        self.assertFalse(assignment.provisioned)
        self.assertIn(assignment.worktree_path, self.worktree_paths())

        provisioned = self.provisioner().provision(attempt_id)
        self.assertEqual(provisioned.assignment.worktree_path, assignment.worktree_path)
        self.assertTrue(provisioned.assignment.provisioned)
        self.assertEqual(len(self.worktree_paths()), 2)
        self.assertEqual(len(self.branches()), 2)
        self.assertEqual(git(provisioned.path, "rev-parse", "HEAD"), self.b1)

    def test_a_crash_after_provisioning_makes_the_retry_a_no_op(self) -> None:
        completed = self.child("after_provision")
        attempt_id = self.derived_attempt_id(completed)
        store = self.reopen()

        assignment = store.worktree_assignment(attempt_id)
        self.assertTrue(assignment.provisioned)
        provisioned = self.provisioner().provision(attempt_id)
        self.assertEqual(provisioned.assignment, assignment)
        self.assertEqual(len(self.worktree_paths()), 2)
        self.assertEqual(len(self.branches()), 2)
        self.assertEqual(tracked_files(provisioned.path), ("README.md",))

    def attempt_count(self) -> int:
        row = self.store.connection.execute(
            "SELECT count(*) AS total FROM attempts"
        ).fetchone()
        return int(row["total"])


class ConcurrentAdmissionTests(AttemptCrashTestCase):
    """Two processes racing on one Work Unit cannot both become authority."""

    def race(self, first_revision: str, second_revision: str) -> list[str]:
        gate = self.workspace_root / "gate"
        children = [
            subprocess.Popen(
                self.command("none", revision=revision, gate=gate),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            for revision in (first_revision, second_revision)
        ]
        gate.write_text("go\n", encoding="utf-8")
        return [child.communicate() for child in children], children

    def test_two_identical_concurrent_admissions_resolve_one_attempt(self) -> None:
        (outputs, children) = self.race("HEAD", "HEAD")
        for (stdout, stderr), child in zip(outputs, children, strict=True):
            self.assertEqual(child.returncode, 0, stderr)
            self.assertEqual(len(stdout.strip().splitlines()), 2, stdout)
        derived = {stdout.strip().splitlines()[0] for stdout, _ in outputs}
        self.assertEqual(len(derived), 1)

        store = self.reopen()
        current = store.current_attempt(self.work_unit.work_unit_id)
        self.assertEqual(current.attempt_id, derived.pop())
        self.assertEqual(self.attempt_count(store), 1)
        self.assertEqual(len(self.worktree_paths()), 2)

    def test_two_differing_concurrent_admissions_leave_one_authority(self) -> None:
        b2 = move_head(self.repository)
        (outputs, children) = self.race(self.b1, b2)

        codes = [child.returncode for child in children]
        self.assertEqual(sorted(codes), [0, 1], [out for out in outputs])
        loser = outputs[codes.index(1)][1]
        self.assertIn("AttemptConflict", loser)

        store = self.reopen()
        current = store.current_attempt(self.work_unit.work_unit_id)
        self.assertIn(current.b1_commit_oid, {self.b1, b2})
        self.assertEqual(self.attempt_count(store), 1)
        self.assertEqual(len(self.worktree_paths()), 2)

    @staticmethod
    def attempt_count(store) -> int:
        row = store.connection.execute(
            "SELECT count(*) AS total FROM attempts"
        ).fetchone()
        return int(row["total"])


if __name__ == "__main__":
    unittest.main()
