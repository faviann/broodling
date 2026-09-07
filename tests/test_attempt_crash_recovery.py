"""Crash windows around Attempt admission and worktree provisioning.

Every case kills a child process at a chosen point and then asks the surviving
state one question: can the operation simply be repeated? The answer must always
be one recoverable current Attempt converging on the same worktree at the same
B1 — never a second Attempt, a second worktree or a second branch.
"""

from __future__ import annotations

import fcntl
import json
import os
import subprocess
import sys
import time
import unittest
from pathlib import Path

from broodling.workspace import DISPOSABLE_WORKTREE_MARKER, PROVISIONING_LOCK
from support import AttemptTestCase, git, make_repository, move_head, tracked_files

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
        self,
        crash_point: str,
        *,
        revision: str = "HEAD",
        gate: Path | None = None,
        repository: Path | None = None,
    ) -> list[str]:
        return [
            sys.executable,
            str(CHILD),
            str(self.store_path),
            str(self.workspace_root),
            str(repository or self.repository),
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

    def worktree_paths(self, repository: Path | None = None) -> tuple[str, ...]:
        listing = git(repository or self.repository, "worktree", "list", "--porcelain")
        return tuple(
            line.removeprefix("worktree ")
            for line in listing.splitlines()
            if line.startswith("worktree ")
        )

    def branches(self, repository: Path | None = None) -> tuple[str, ...]:
        return tuple(
            git(
                repository or self.repository, "branch", "--format=%(refname:short)"
            ).split()
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
    """Processes racing on one Work Unit cannot both become authority."""

    def race(
        self,
        *revisions: str,
        crash_point: str = "none",
        repository: Path | None = None,
    ) -> tuple[list[tuple[str, str]], list[subprocess.Popen]]:
        """Start one child per revision, then open the gate they all wait on."""

        gate = self.workspace_root / "gate"
        children = [
            subprocess.Popen(
                self.command(
                    crash_point, revision=revision, gate=gate, repository=repository
                ),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            for revision in revisions
        ]
        gate.write_text("go\n", encoding="utf-8")
        return [child.communicate() for child in children], children

    def test_identical_concurrent_admissions_alone_resolve_one_attempt(self) -> None:
        """Admission on its own, with no provisioning outcome in front of it.

        The invariant is about currentness, so it is witnessed where currentness
        is decided. Racing the whole of ``admit_and_provision`` and reading the
        result is a witness for two things at once; when it fails, it does not
        say which.
        """

        (outputs, children) = self.race(*["HEAD"] * 4, crash_point="stop_after_admit")
        derived, admitted = set(), set()
        for (stdout, stderr), child in zip(outputs, children, strict=True):
            self.assertEqual(child.returncode, 0, stderr)
            lines = stdout.strip().splitlines()
            self.assertEqual(len(lines), 2, stdout)
            derived.add(lines[0])
            admitted.add(lines[1])

        # Every caller derived, and was returned, the one Attempt.
        self.assertEqual(len(derived), 1)
        self.assertEqual(derived, admitted)

        store = self.reopen()
        current = store.current_attempt(self.work_unit.work_unit_id)
        self.assertEqual(current.attempt_id, admitted.pop())
        self.assertEqual(self.attempt_count(store), 1)
        self.assertEqual(
            store.connection.execute(
                "SELECT count(*) AS total FROM worktree_assignments"
            ).fetchone()["total"],
            1,
        )
        # Nothing was provisioned, so the host still shows only the source tree.
        self.assertEqual(len(self.worktree_paths()), 1)

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

    def test_every_concurrent_caller_is_handed_the_finished_worktree(self) -> None:
        """A caller is never given a worktree that is still being checked out.

        ``git worktree add`` publishes its result in stages, and the tree is the
        last of them. B1 here holds enough files that the gap between the branch
        attaching and the checkout finishing is wide enough to be observed, which
        is what makes this a witness rather than a coincidence.
        """

        repository = self.root / "bulky"
        b1 = make_repository(repository, bulk=600)
        expected = len(tracked_files(repository))
        outputs, children = self.race(
            *["HEAD"] * 4, crash_point="report", repository=repository
        )

        answers = []
        for (stdout, stderr), child in zip(outputs, children, strict=True):
            self.assertEqual(child.returncode, 0, stderr)
            answers.append(json.loads(stdout.strip().splitlines()[-1]))

        for answer in answers:
            self.assertEqual(answer, answers[0], answers)
            self.assertEqual(answer["head"], b1)
            self.assertEqual(answer["tracked_files"], expected)
            self.assertTrue(answer["provisioned"])

        store = self.reopen()
        self.assertEqual(self.attempt_count(store), 1)
        self.assertEqual(len(self.worktree_paths(repository)), 2)
        self.assertEqual(len(self.branches(repository)), 2)

    def test_a_second_provisioner_waits_rather_than_reading_a_half_built_worktree(
        self,
    ) -> None:
        """Provisioning one Attempt is single-writer on this host.

        Held from outside, the enclosure lock keeps a provisioner out entirely —
        it cannot even look — and once released it converges on what it finds.
        This is the mechanism the previous two tests rely on, asserted directly
        rather than through the outcome it produces.
        """

        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        assignment = self.store.worktree_assignment(attempt.attempt_id)
        enclosure = assignment.path.parent
        enclosure.mkdir(parents=True, exist_ok=True)
        marker = enclosure / DISPOSABLE_WORKTREE_MARKER

        handle = os.open(enclosure / PROVISIONING_LOCK, os.O_CREAT | os.O_WRONLY, 0o600)
        self.addCleanup(os.close, handle)
        fcntl.flock(handle, fcntl.LOCK_EX)

        child = subprocess.Popen(
            self.command("none"),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        self.addCleanup(child.kill)

        # The marker is written immediately before the lock is taken, so its
        # appearance says the child has reached the lock and nothing further.
        deadline = time.monotonic() + 30.0
        while not marker.is_file():
            self.assertIsNone(child.poll(), "child exited before reaching the lock")
            self.assertLess(
                time.monotonic(), deadline, "child never claimed the enclosure"
            )
            time.sleep(0.005)

        for _ in range(40):
            self.assertIsNone(child.poll(), "a second provisioner entered the lock")
            self.assertFalse(assignment.path.exists(), "a locked-out process wrote")
            time.sleep(0.005)

        fcntl.flock(handle, fcntl.LOCK_UN)
        stdout, stderr = child.communicate(timeout=60)
        self.assertEqual(child.returncode, 0, stderr)
        self.assertEqual(stdout.strip().splitlines()[-1], attempt.attempt_id)
        self.assertEqual(git(assignment.path, "rev-parse", "HEAD"), self.b1)
        self.assertEqual(self.attempt_count(self.reopen()), 1)

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
