"""Offline P5 preparation never crosses the separately authorized R01 boundary."""

from __future__ import annotations

from contextlib import ExitStack
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import unittest
from unittest.mock import patch

from broodling import BroodlingStore
from broodling.profile import ZEROSHOT_SDK_VERSION, ZEROSHOT_VERSION
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL, ZeroshotSubmitter
from evaluation.p5.v3 import setup_r01 as legacy
from evaluation.p5.v5 import setup_r01 as setup
from tests.support import durable_test_root, git


class P5V5SetupTests(unittest.TestCase):
    def setUp(self):
        self.root = durable_test_root("broodling-p5-v5-")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.state = self.root / "state"
        self.evidence = self.root / "evidence"
        stack = self.enterContext(ExitStack())
        for target in (
            "broodling.BroodlingStore.admit",
            "broodling.BroodlingStore.admit_attempt",
            "broodling.AttemptProvisioner.admit_and_provision",
            "broodling.SubmissionCoordinator.submit",
            "broodling.ZeroshotSubmitter.submit",
        ):
            stack.enter_context(patch(target, side_effect=AssertionError("execution is forbidden")))
        command = legacy.command

        def local_git_only(*argv, **kwargs):
            if argv[0] != "git" or any(arg in ("push", "fetch", "ls-remote") for arg in argv):
                raise AssertionError("network access is forbidden")
            return command(*argv, **kwargs)

        stack.enter_context(patch.object(legacy, "command", side_effect=local_git_only))

    def prepare(self, **overrides):
        arguments = {"state_dir": self.state, "evidence": self.evidence,
                     "repository": "example/p5-disposable"}
        return setup.prepare(**(arguments | overrides))

    def snapshot(self):
        return {str(path.relative_to(self.evidence)): path.read_bytes()
                for path in self.evidence.rglob("*") if path.is_file()}

    def test_offline_preparation_is_idempotent_and_retains_no_credentials(self):
        secrets = {"GATEWAY_API_KEY": "test-gateway-sentinel", "GH_TOKEN": "test-gh-sentinel",
                   "OPENAI_API_KEY": "test-legacy-sentinel"}
        with patch.dict(os.environ, secrets):
            facts = self.prepare()
            before = self.snapshot()
            self.assertEqual(self.prepare(), facts)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(git(self.state / "source", "rev-parse", "HEAD"), legacy.B1)
        self.assertEqual(git(self.state / "source", "rev-parse", "HEAD^{tree}"), legacy.TREE)
        self.assertEqual((self.evidence / "R01/issue-body.md").read_text(), legacy.task_material()[3])
        allocation = json.loads(before["slots.json"])
        self.assertEqual(allocation["counts"], {"P": 8, "S": 0, "D": 0, "U": 0, "A": 0, "J_A": 0})
        self.assertEqual([slot["status"] for slot in allocation["slots"]], ["NOT_STARTED"] * 8)
        self.assertFalse(facts["admission_started"])
        self.assertFalse((self.state / "broodling.sqlite3").exists())
        self.assertEqual(list((self.state / "workspaces").iterdir()), [])
        profile = json.loads(before["execution-profile.json"])
        self.assertEqual(profile["gateway_base_url"], "https://cliproxy.local.faviann.com/v1")
        self.assertEqual(profile["uniform_runtime"]["provider"], "gateway")
        retained = b"".join(before.values()) + (self.state / "setup-v5.json").read_bytes()
        for secret in secrets.values():
            self.assertNotIn(secret.encode(), retained)

    def test_foreign_started_or_nonzero_allocation_is_never_overwritten(self):
        self.prepare()
        path = self.evidence / "slots.json"
        original = json.loads(path.read_text())
        for change in ("protocol", "freeze", "status", "count", "result"):
            with self.subTest(change=change):
                value = json.loads(json.dumps(original))
                if change == "protocol":
                    value["protocol"] = "p5-native-pr-v4"
                elif change == "freeze":
                    value["freeze_commit"] = "c8d3285"
                elif change == "status":
                    value["slots"][0]["status"] = "STARTED"
                elif change == "count":
                    value["counts"]["S"] = 1
                else:
                    value["slots"][0]["native_result"] = {"status": "FAIL"}
                path.write_text(json.dumps(value))
                before = self.snapshot()
                with self.assertRaisesRegex(RuntimeError, "existing evidence differs"):
                    self.prepare()
                self.assertEqual(self.snapshot(), before)

    def test_historical_evidence_directory_is_unchanged(self):
        self.evidence.mkdir()
        for version in ("v3", "v4"):
            with self.subTest(version=version):
                (self.evidence / "slots.json").write_text(json.dumps({
                    "protocol": f"p5-native-pr-{version}", "freeze_commit": "historical",
                }))
                before = self.snapshot()
                with self.assertRaises(RuntimeError):
                    self.prepare()
                self.assertEqual(self.snapshot(), before)
                # Failed setup's lock is new local state, never historical evidence.
                if self.state.exists():
                    shutil.rmtree(self.state)

    def test_recorded_profile_matches_current_product_without_credentials(self):
        profile = setup.execution_profile(None)
        submitter = ZeroshotSubmitter(self.state)
        self.assertEqual(profile["uniform_runtime"], submitter.runtime_for("pull_request"))
        self.assertEqual(profile["gateway_base_url"], V1_GATEWAY_BASE_URL)
        self.assertEqual(profile["zeroshot_version"], ZEROSHOT_VERSION)
        self.assertEqual(profile["zeroshot_sdk_version"], ZEROSHOT_SDK_VERSION)

    def test_changed_profile_or_fixture_is_never_rewritten(self):
        self.prepare()
        path = self.evidence / "execution-profile.json"
        profile = json.loads(path.read_text())
        profile["gateway_base_url"] = "https://cliproxy.local.faviann.com"
        path.write_text(json.dumps(profile))
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, "existing evidence differs"):
            self.prepare()
        self.assertEqual(self.snapshot(), before)
        path.unlink()
        legacy.record(path, setup.execution_profile(None))
        (self.state / "source/tiny.py").write_text("changed fixture\n")
        with self.assertRaisesRegex(RuntimeError, "dirty"):
            self.prepare()
        self.assertEqual((self.state / "source/tiny.py").read_text(), "changed fixture\n")

    def test_unsafe_or_unidentified_destinations_are_refused(self):
        for state, evidence in (
            (self.state, self.state), (self.state, self.state / "source"),
            (self.evidence / "state", self.evidence),
            (self.state, setup.ROOT / "evaluation/p5/v4"),
            (self.state, setup.ROOT / "evaluation/p5/runs"),
            (setup.ROOT, self.evidence),
        ):
            with self.subTest(state=state, evidence=evidence), self.assertRaises(ValueError):
                self.prepare(state_dir=state, evidence=evidence)
        self.assertFalse(self.state.exists())
        self.evidence.mkdir()
        old = self.evidence / "old-evidence.json"
        old.write_text("historical\n")
        with self.assertRaisesRegex(RuntimeError, "fresh directory"):
            self.prepare()
        self.assertEqual(old.read_text(), "historical\n")
        self.assertFalse(self.state.exists())

    def test_source_cannot_redirect_into_other_evidence(self):
        self.state.mkdir()
        (self.state / "source").symlink_to(self.evidence, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "inside the selected state"):
            self.prepare()
        self.assertFalse(self.evidence.exists())

    def test_repository_or_target_cannot_change_on_rerun(self):
        self.prepare(direct_target_origin="https://zeroshot.example.invalid")
        before = self.snapshot()
        for changes in ({"repository": "example/other"},
                        {"direct_target_origin": "https://other.example.invalid"}):
            with self.subTest(changes=changes), self.assertRaisesRegex(RuntimeError, "differs"):
                self.prepare(**changes)
        self.assertEqual(self.snapshot(), before)
        for origin in (setup.GATEWAY_BASE_URL, "https://user:secret@example.invalid", "not-an-origin"):
            with self.subTest(origin=origin), self.assertRaises(ValueError):
                setup.execution_profile(origin)

    def test_mocked_github_readback_prepares_real_unadmitted_product_records(self):
        self.prepare()
        body = (self.evidence / "R01/issue-body.md").read_text()
        remote = {"full_name": "example/p5-disposable", "node_id": "R_fixture", "id": 123,
                  "html_url": "https://github.com/example/p5-disposable", "private": True,
                  "description": legacy.DESCRIPTION}
        issue = {"html_url": "https://github.com/example/p5-disposable/issues/1", "node_id": "I_fixture",
                 "body": body, "title": "P5 v1 / T1 / R01", "state": "open"}
        responses = {"repos/example/p5-disposable": remote,
                     "repos/example/p5-disposable/git/ref/heads/p5-eval": {"object": {"sha": legacy.B1}},
                     "repos/example/p5-disposable/issues/1": issue}
        with patch.object(legacy, "api", side_effect=responses.__getitem__) as readback:
            facts = self.prepare(github_issue=1)
            self.assertEqual(self.prepare(github_issue=1), facts)
        self.assertEqual(readback.call_count, 6)
        self.assertEqual(git(self.state / "source", "remote", "get-url", "origin"),
                         "https://github.com/example/p5-disposable.git")
        records = facts["records"]
        self.assertFalse(records["admission_started"])
        with BroodlingStore.open(self.state / "broodling.sqlite3") as store:
            revision = store.get_contract_revision(records["contract_revision_id"])
            self.assertEqual(revision.canonical_bytes, (self.evidence / "R01/contract.json").read_bytes())
            self.assertIsNone(store.current_attempt(records["work_unit_id"]))
        with sqlite3.connect(self.state / "broodling.sqlite3") as database:
            self.assertEqual(database.execute("SELECT count(*) FROM admission_decisions").fetchone()[0], 0)
            self.assertEqual(database.execute("SELECT count(*) FROM attempts").fetchone()[0], 0)

    def test_incompatible_github_readback_cannot_create_product_inputs(self):
        self.prepare()
        remote = {"full_name": "example/p5-disposable", "private": True,
                  "description": legacy.DESCRIPTION}
        issue = {"html_url": "https://github.com/example/p5-disposable/issues/1",
                 "body": "changed", "title": "P5 v1 / T1 / R01", "state": "open"}
        for change in ("private", "b1", "body"):
            with self.subTest(change=change):
                responses = [remote | {"private": change != "private"},
                             {"object": {"sha": "wrong" if change == "b1" else legacy.B1}}, issue]
                before = self.snapshot()
                with patch.object(legacy, "api", side_effect=responses), self.assertRaises(RuntimeError):
                    self.prepare(github_issue=1)
                self.assertFalse((self.state / "broodling.sqlite3").exists())
                self.assertEqual(self.snapshot(), before)

    def test_import_has_no_path_or_execution_side_effects(self):
        code = """
import sys
before = list(sys.path)
import evaluation.p5.v5.setup_r01
assert sys.path == before
assert 'evaluation.p5.v3.setup_r01' not in sys.modules
assert 'broodling' not in sys.modules
"""
        subprocess.run([sys.executable, "-c", code], cwd=setup.ROOT, check=True,
                       capture_output=True, text=True, timeout=30)


if __name__ == "__main__":
    unittest.main()
