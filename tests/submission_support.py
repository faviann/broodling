"""Small caller-owned P2 fixture; no assurance graph or result interpretation."""

import importlib.util
import shutil
import tempfile
import unittest
from pathlib import Path

from support import AttemptTestCase, git

from broodling.submission import SubmissionCoordinator
from broodling.zeroshot_sdk import ZeroshotSubmitter

GRAPH = {
    "profile": "openengine.graph.full/v1",
    "initialInput": {"kind": "null"},
    "policy": {"policy": "policy.native-v2@1", "default": "deny"},
    "root": {
        "kind": "succeed",
        "name": "inert",
        "output": {"kind": "null"},
        "bindings": [],
    },
}
RUNTIME = {"harness": "codex", "provider": "openai", "size": "small", "nodes": {}}
REQUEST = {"graph": GRAPH, "runtime": RUNTIME}


class SubmissionCase(AttemptTestCase):
    def setUp(self):
        super().setUp()
        git(
            self.repository,
            "remote",
            "add",
            "origin",
            "https://github.com/faviann/broodling.git",
        )
        provisioned = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )
        self.attempt = provisioned.attempt
        self.attempt_id = self.attempt.attempt_id
        self.path = provisioned.path
        # Only sockets/controller state use shm; the dedicated worktree is durable.
        self.runtime_state = Path(tempfile.mkdtemp(prefix="b14-", dir="/dev/shm"))
        self.addCleanup(shutil.rmtree, self.runtime_state, ignore_errors=True)
        self.adapter = ZeroshotSubmitter(self.runtime_state)
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)

    def submit(self, **overrides):
        return self.coordinator.submit(self.attempt_id, **(REQUEST | overrides))

    def prepare(self):
        return self.coordinator.prepare(self.attempt_id, **REQUEST)

    def assert_single(self, run_id):
        row = self.coordinator.record(self.attempt_id)
        self.assertEqual(row.run_id, run_id)
        self.assertEqual(row.state, "correlated")
        for table in (
            "work_units",
            "contract_revisions",
            "attempts",
            "worktree_assignments",
            "attempt_submissions",
        ):
            self.assertEqual(
                self.store.connection.execute(
                    f"SELECT count(*) FROM {table}"
                ).fetchone()[0],
                1,
            )
        return row

    def restart(self):
        self.reopen()
        self.coordinator = SubmissionCoordinator(self.store, self.adapter)


class RealSubmissionCase(SubmissionCase):
    def setUp(self):
        if importlib.util.find_spec("zeroshot") is None:
            raise unittest.SkipTest("install the G1-V1 qualified SDK/sidecar")
        super().setUp()


# A single caller-owned task, not the W3 assurance graph. The controlled leaf
# changes the owned worktree only after the parent releases a filesystem gate.
MUTATING_REQUEST = {
    "graph": GRAPH
    | {
        "root": {
            "kind": "step",
            "name": "mutate",
            "worker": "agent.broodling-p2-mutator@1",
            "instructions": "BROODLING_P2_MUTATION",
            "input": {"kind": "null"},
            "output": {"kind": "null"},
            "inputBindings": [],
            "writeBindings": [],
            "timeoutMs": 45000,
            "attempts": 1,
        }
    },
    "runtime": RUNTIME
    | {
        "nodes": {
            "mutate": {
                "kind": "agent",
                "model": "broodling-p2-controlled",
                "effort": "low",
                "sessionScope": "execution",
                "connections": {},
            }
        }
    },
}

_MUTATION_STEP = MUTATING_REQUEST["graph"]["root"]
_EMPTY_STATE = {"kind": "record", "fields": {}}
MUTATING_REQUEST["initial_input"] = {}
MUTATING_REQUEST["graph"]["initialInput"] = _EMPTY_STATE
MUTATING_REQUEST["graph"]["root"] = {
    "kind": "seq",
    "name": "mutation_fixture",
    "state": _EMPTY_STATE,
    "children": [_MUTATION_STEP, GRAPH["root"]],
    "promotedStatePaths": [],
}
