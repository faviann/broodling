"""Installation preserves retained authority; review pins exact issue bytes."""

import importlib.util
import json
import shutil
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch

from support import StoreTestCase, durable_test_root

from broodling import RequiredEffect, WorkReference
from broodling.ingress import ContractIngress, InvalidContractProposal
from deployment import install as installer


REVISION = "a" * 40
PORT = 18770
CONTAINER = "broodling-fixture-target"


class DeploymentInstallTests(unittest.TestCase):
    def setUp(self):
        self.host = durable_test_root(prefix="broodling-install-")
        self.addCleanup(shutil.rmtree, self.host, ignore_errors=True)
        self.root = self.host / "installation"
        self.enterContext(patch.object(installer.sys, "version_info", (3, 13, 1)))
        self.enterContext(patch.object(installer.sqlite3, "sqlite_version_info", (3, 37, 0)))
        self.enterContext(patch.object(installer.platform, "system", return_value="Linux"))
        self.enterContext(patch.object(installer.platform, "machine", return_value="x86_64"))
        self.enterContext(patch.object(installer.os, "getuid", return_value=1000))
        self.enterContext(patch.object(installer.os, "getgid", return_value=1000))
        # These tests exercise the retention boundary without installing packages,
        # building an image, or touching any real Docker container.
        self.run = self.enterContext(patch.object(installer.subprocess, "run"))
        self.command = self.enterContext(patch.object(installer, "command"))

    def retained_installation(self):
        self.root.mkdir()
        record = {
            "installation_root": str(self.root),
            "broodling_revision": REVISION,
            "direct_target_origin": f"http://127.0.0.1:{PORT}",
            "container_name": CONTAINER,
            "host_uid": 1000,
            "host_gid": 1000,
        }
        retained = {
            "installation.json": (json.dumps(record) + "\n").encode(),
            "config.json": b'{"direct_target_origin":"http://127.0.0.1:18770"}\n',
            "state/broodling.sqlite3": b"retained durable semantic authority",
            "bin/broodling": b"#!/bin/sh\nexit 0\n",
            "attempts/attempt-a/evidence.json": b'{"receipt":"retain exactly"}\n',
            "target-state/native-state": b"native run association",
        }
        for name, content in retained.items():
            destination = self.root / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(content)
        return record, retained

    def assert_no_external_work(self):
        self.command.assert_not_called()
        self.run.assert_not_called()

    def test_reapplying_same_release_preserves_every_retained_byte_without_recreating_target(self):
        record, retained = self.retained_installation()
        self.assertEqual(installer.install(self.root, REVISION, PORT, CONTAINER), record)
        self.assert_no_external_work()
        for name, content in retained.items():
            self.assertEqual((self.root / name).read_bytes(), content, name)

    def test_reapplying_different_revision_origin_identity_or_owner_refuses_without_changes(self):
        _, retained = self.retained_installation()
        changes = (
            ("b" * 40, PORT, CONTAINER, 1000),
            (REVISION, PORT + 1, CONTAINER, 1000),
            (REVISION, PORT, "another-target", 1000),
            (REVISION, PORT, CONTAINER, 1001),
        )
        for revision, port, container, uid in changes:
            with self.subTest(revision=revision, port=port, container=container, uid=uid):
                with patch.object(installer.os, "getuid", return_value=uid):
                    with self.assertRaisesRegex(ValueError, "existing installation differs"):
                        installer.install(self.root, revision, port, container)
                self.assert_no_external_work()
                for name, content in retained.items():
                    self.assertEqual((self.root / name).read_bytes(), content, name)

    def test_missing_store_does_not_silently_reinitialize_semantic_authority(self):
        _, retained = self.retained_installation()
        missing = self.root / "state/broodling.sqlite3"
        missing.unlink()
        with self.assertRaisesRegex(ValueError, "existing installation is incomplete"):
            installer.install(self.root, REVISION, PORT, CONTAINER)
        self.assertFalse(missing.exists())
        self.assertEqual(
            (self.root / "target-state/native-state").read_bytes(),
            retained["target-state/native-state"],
        )
        self.assert_no_external_work()

    def test_partial_installation_is_preserved_instead_of_overwritten(self):
        self.root.mkdir()
        native_state = self.root / "orphaned-native-state"
        native_state.write_bytes(b"inspect this after interrupted install")
        with self.assertRaisesRegex(ValueError, "root must be empty"):
            installer.install(self.root, REVISION, PORT, CONTAINER)
        self.assertEqual(native_state.read_bytes(), b"inspect this after interrupted install")
        self.assert_no_external_work()

    def test_mutable_revision_and_nondurable_or_symlinked_roots_refuse_before_install(self):
        with self.assertRaisesRegex(ValueError, "immutable Git commit"):
            installer.install(self.root, "main", PORT, CONTAINER)
        with self.assertRaisesRegex(ValueError, "canonical durable path"):
            installer.install(Path("/tmp") / self.host.name, REVISION, PORT, CONTAINER)
        actual = self.host / "actual"
        actual.mkdir()
        alias = self.host / "alias"
        alias.symlink_to(actual, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "canonical durable path"):
            installer.install(alias / "installation", REVISION, PORT, CONTAINER)
        self.assertFalse(self.root.exists())
        self.assertFalse((actual / "installation").exists())
        self.assert_no_external_work()

    def test_remote_nonlinux_and_rootless_docker_refuse_before_creating_installation(self):
        profiles = (
            ("ssh://remote-host", [], "local Docker Unix socket"),
            ("unix:///var/run/docker.sock", ["windows"], "Linux containers"),
            ("unix:///var/run/docker.sock", ["linux", '["name=rootless"]'], "rootful Docker"),
        )
        for endpoint, output, message in profiles:
            with self.subTest(endpoint=endpoint, output=output):
                self.command.side_effect = output
                with patch.dict(installer.os.environ, {"DOCKER_HOST": endpoint, "DOCKER_CONTEXT": ""}):
                    with self.assertRaisesRegex(ValueError, message):
                        installer.install(self.root, REVISION, PORT, CONTAINER)
                self.assertFalse(self.root.exists())
                self.run.assert_not_called()

    def test_missing_host_gh_refuses_before_creating_a_target_or_partial_installation(self):
        self.command.side_effect = [
            "linux", '["name=seccomp,profile=builtin"]', "git version 2.47.3",
            FileNotFoundError("gh"),
        ]
        with patch.dict(installer.os.environ, {
            "DOCKER_HOST": "unix:///var/run/docker.sock", "DOCKER_CONTEXT": "",
        }):
            with self.assertRaises(FileNotFoundError):
                installer.install(self.root, REVISION, PORT, CONTAINER)
        self.assertFalse(self.root.exists())
        self.run.assert_not_called()


class ReviewedIssueProposalTests(StoreTestCase):
    def setUp(self):
        super().setUp()
        proposer_path = self.root / "reviewed_issue.py"
        shutil.copy2(Path(installer.__file__).with_name("reviewed_issue.py"), proposer_path)
        spec = importlib.util.spec_from_file_location("reviewed_issue_fixture", proposer_path)
        self.proposer = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.proposer)
        self.issue = {
            "number": 77,
            "node_id": "I_reviewed_77",
            "html_url": "https://github.com/faviann/broodling/issues/77",
            "repository_url": "https://api.github.com/repos/faviann/broodling",
            "title": "Add a configuration example",
            "body": "Add the documented configuration example and deliver a PR.",
        }
        self.reviewed = (json.dumps(self.issue) + "\n").encode()
        proposer_path.with_suffix(".json").write_bytes(self.reviewed)
        self.reference = WorkReference.parse("faviann/broodling", 77)
        self.effects = (RequiredEffect("deliver", "Open the selected PR.", "pull_request", "main"),)

    def propose(self, content):
        response = subprocess.CompletedProcess("gh", 0, stdout=content, stderr=b"")
        with patch("broodling.github_source.subprocess.run", return_value=response):
            return ContractIngress(self.store).from_github(
                self.reference, self.proposer.propose,
                required_effects=self.effects, constructed_by="caller",
            )

    def test_reviewed_issue_retains_complete_request_and_exact_effect_authority(self):
        result = self.propose(self.reviewed)
        self.assertTrue(result.decision.admitted)
        contract = result.revision.contract
        self.assertEqual(contract.criteria[0].statement, self.issue["title"] + "\n\n" + self.issue["body"])
        self.assertEqual(contract.required_effects, self.effects)
        self.assertEqual(contract.constructed_by, "caller")
        self.assertEqual(result.sources[0].content, self.reviewed)

    def test_changed_issue_or_equivalent_json_bytes_require_fresh_operator_review(self):
        changes = (
            json.dumps({**self.issue, "body": "Deploy it to production too."}).encode(),
            json.dumps(self.issue, indent=2).encode(),
            json.dumps({**self.issue, "comments": 1}).encode(),
        )
        for changed in changes:
            with self.subTest(content=changed):
                with self.assertRaisesRegex(InvalidContractProposal, "issue changed since operator review"):
                    self.propose(changed)
                self.assertEqual(self.store.list_contract_revisions(self.reference.work_unit_id), ())


if __name__ == "__main__":
    unittest.main()
