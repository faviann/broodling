"""Throwaway #55 experiment; explicitly selected, never in the normal suite.

Run: python -m pytest tests/prototype_provisioning_race.py
Optional fault: BROODLING_RACE_PROTOTYPE_FAULT=flock|both
"""

import json
import os
import select
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from attempt_crash_child import wait_for_gate
from support import AttemptTestCase, git

from broodling import AttemptProvisioner, BroodlingStore
from broodling import git as product_git
from broodling import provisioning


def child_main():
    database, root, attempt_id, role, gate, fault = sys.argv[1:]
    with BroodlingStore.open(database) as store:
        provisioner = AttemptProvisioner(store, root)
        if role == "leader":
            def staged_add(repository, path, branch, commit_oid, **options):
                # Both stages are real Git operations. The held intermediate
                # state is registered/attached at B1 but has no checked-out bytes.
                product_git.run(
                    repository, "-c", "core.hooksPath=/dev/null",
                    "worktree", "add", "--no-checkout", "-b", branch,
                    str(path), commit_oid,
                    inherited_fds=options["inherited_fds"],
                )
                print(json.dumps({"partial": str(path)}), flush=True)
                wait_for_gate(gate)
                product_git.run(
                    path, "-c", "core.hooksPath=/dev/null", "reset", "--hard",
                    commit_oid, inherited_fds=options["inherited_fds"],
                )

            product_git.add_worktree = staged_add
            if fault == "both":
                materialize = provisioner._materialize

                def without_writer(*arguments):
                    # Deliberate test-local loss of the SQLite exclusion layer.
                    store.connection.execute("COMMIT")
                    try:
                        return materialize(*arguments)
                    finally:
                        store.connection.execute("BEGIN IMMEDIATE")

                provisioner._materialize = without_writer
        elif fault in {"flock", "both"}:
            # Skip only this follower's host lock; real SQLite remains in use.
            provisioning.fcntl.flock = lambda *_: None

        if role == "follower":
            print(json.dumps({"started": True}), flush=True)
        result = provisioner.provision(attempt_id)
        readme = result.path / "README.md"
        print(json.dumps({
            "attempt_id": result.attempt.attempt_id,
            "path": str(result.path),
            "branch": result.branch,
            "head": product_git.head_commit(result.path),
            "provisioned": result.assignment.provisioned,
            "readme": readme.read_text() if readme.exists() else None,
        }), flush=True)


class ProvisioningRacePrototype(AttemptTestCase):
    def test_two_callers_receive_original_materialized_worktree(self):
        attempt = self.provisioner().admit(
            self.revision.contract_revision_id, self.repository
        )
        assignment = self.store.worktree_assignment(attempt.attempt_id)
        gate = self.root / "finish-checkout"
        fault = os.environ.get("BROODLING_RACE_PROTOTYPE_FAULT", "")
        self.assertIn(fault, {"", "flock", "both"})
        children = []

        def start(role):
            child = subprocess.Popen(
                [sys.executable, str(Path(__file__).resolve()), str(self.store_path),
                 str(self.workspace_root), attempt.attempt_id, role, str(gate), fault],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0,
            )
            children.append(child)
            return child

        def event(child):
            ready, _, _ = select.select([child.stdout], [], [], 10)
            self.assertTrue(ready, "child did not reach the staged boundary")
            return json.loads(child.stdout.readline())

        try:
            leader = start("leader")
            self.assertEqual(event(leader), {"partial": str(assignment.path)})
            self.assertEqual(git(assignment.path, "rev-parse", "HEAD"), self.b1)
            self.assertEqual(git(assignment.path, "branch", "--show-current"), assignment.branch)
            self.assertFalse((assignment.path / "README.md").exists())
            self.assertFalse(self.store.worktree_assignment(attempt.attempt_id).provisioned)

            follower = start("follower")
            self.assertEqual(event(follower), {"started": True})
            try:
                # Same 200 ms observation budget as the retained lock witness.
                # This does not prove the follower reached its actual lock call.
                early = follower.communicate(timeout=0.2)
            except subprocess.TimeoutExpired:
                early = None
            finally:
                gate.touch()

            answers = []
            for child in (leader, follower):
                output, error = (
                    early if child is follower and early is not None
                    else child.communicate(timeout=30)
                )
                self.assertEqual(child.returncode, 0, error)
                answers.append(json.loads(output))

            for answer in answers:
                # Observe physical bytes at return, not only Git's index/HEAD.
                self.assertEqual(answer["readme"], "original admitted state\n")
                self.assertEqual(answer["attempt_id"], attempt.attempt_id)
                self.assertEqual(answer["path"], str(assignment.path))
                self.assertEqual(answer["branch"], assignment.branch)
                self.assertEqual(answer["head"], self.b1)
                self.assertTrue(answer["provisioned"])
            self.assertEqual(len(product_git.list_worktrees(self.repository)), 2)
            self.assertEqual(len(git(self.repository, "branch", "--list").splitlines()), 2)
            self.assertEqual(self.store.current_attempt(self.work_unit.work_unit_id), attempt)
            self.assertEqual(
                self.store.connection.execute("SELECT count(*) FROM attempts").fetchone()[0], 1
            )
        finally:
            gate.touch()
            for child in children:
                if child.poll() is None:
                    child.kill()
                child.communicate(timeout=10)


if __name__ == "__main__":
    child_main()
