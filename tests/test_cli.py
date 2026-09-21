"""Operator process boundaries and command dispatch over the existing facade."""

import base64
import io
import json
import os
import shutil
import subprocess
import sys
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

from schema_support import restore_published_schema
from support import StoreTestCase, durable_test_root, git, make_repository
from zeroshot import RunResult

from broodling import BroodlingStore
from broodling.cli import main
from broodling.schema import SCHEMA_VERSION
from broodling.zeroshot_sdk import V1_GATEWAY_BASE_URL


CREDENTIALS = {
    "GH_TOKEN": "fixture-github-secret",
    "GATEWAY_BASE_URL": V1_GATEWAY_BASE_URL,
    "GATEWAY_API_KEY": "fixture-provider-secret",
}


class OperatorCLIBase(StoreTestCase):
    def setUp(self):
        super().setUp()
        (self.root / "config.json").write_text(json.dumps({
            "direct_target_origin": "http://127.0.0.1:8123",
        }))

    def run_cli(self, *arguments, root=None):
        environment = {
            key: value for key, value in os.environ.items()
            if key not in CREDENTIALS
        }
        return subprocess.run(
            [sys.executable, "-m", "broodling.cli", "--root",
             str(self.root if root is None else root), *arguments],
            capture_output=True, text=True, env=environment,
        )

    def invoke(self, *arguments):
        stdout = io.StringIO()
        stderr = io.StringIO()
        with redirect_stdout(stdout), redirect_stderr(stderr):
            code = main(["--root", str(self.root), *arguments])
        return code, stdout.getvalue(), stderr.getvalue()


class OperatorCLITests(OperatorCLIBase):
    def test_fresh_process_reads_frozen_authority_without_credentials_or_proposer(self):
        work_unit, source, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        self.store.admit(revision.contract_revision_id)
        self.store.close()

        status = self.run_cli("status", revision.contract_revision_id)
        self.assertEqual(status.returncode, 0, status.stderr)
        self.assertEqual(status.stderr, "")
        result = json.loads(status.stdout)
        self.assertEqual(result["revision"]["contract"], contract.to_mapping())
        for item, exact in (
            (result["sources"][0]["content"], source.content),
            (result["revision"]["canonical_bytes"], revision.canonical_bytes),
        ):
            self.assertEqual(item["encoding"], "base64")
            self.assertEqual(base64.b64decode(item["data"], validate=True), exact)
        self.assertEqual(result["work_unit"]["work_unit_id"], work_unit.work_unit_id)
        self.assertIsNone(result["attempt"])
        history = self.run_cli("history", "--repository", "faviann/broodling", "--issue", "12")
        self.assertEqual(history.returncode, 0, history.stderr)
        self.assertEqual(json.loads(history.stdout), [result])
        unknown = self.run_cli("history", "--repository", "faviann/broodling", "--issue", "99")
        self.assertEqual(unknown.returncode, 0, unknown.stderr)
        self.assertEqual(json.loads(unknown.stdout), [])

    def test_mistyped_root_or_missing_configuration_never_creates_an_empty_store(self):
        nonexistent = self.root / "missing-installation"
        result = self.run_cli("history", "--repository", "faviann/broodling", "--issue", "12", root=nonexistent)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(json.loads(result.stderr)["error"], "InstallationError")
        self.assertFalse(nonexistent.exists())
        (self.root / "config.json").unlink()
        result = self.run_cli("history", "--repository", "faviann/broodling", "--issue", "12")
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, "")
        self.assertFalse((self.root / "config.json").exists())
        with BroodlingStore.open(self.store_path) as store:
            self.assertEqual(store.connection.execute("SELECT COUNT(*) FROM work_units").fetchone()[0], 0)

    def test_configuration_does_not_accept_credential_fields_or_credential_origins(self):
        for config in (
            {"direct_target_origin": "http://127.0.0.1:8123", "GH_TOKEN": "secret"},
            {"direct_target_origin": "http://user:secret@127.0.0.1:8123"},
            {"direct_target_origin": "http://127.0.0.1:8123?token=secret"},
        ):
            with self.subTest(config=config):
                (self.root / "config.json").write_text(json.dumps(config))
                code, output, error = self.invoke("history", "--repository", "faviann/broodling", "--issue", "12")
                self.assertEqual(code, 1)
                self.assertEqual(output, "")
                self.assertNotIn("secret", error)

    def test_live_errors_and_interrupts_have_safe_explicit_handback(self):
        for error, expected in (
            (OSError("provider failed: " + CREDENTIALS["GATEWAY_API_KEY"]), 1),
            (KeyboardInterrupt(), 130),
        ):
            with self.subTest(error=type(error).__name__), patch(
                "broodling.cli.Broodling.history", side_effect=error
            ):
                code, output, diagnostics = self.invoke("history", "--repository", "faviann/broodling", "--issue", "12")
                self.assertEqual(code, expected)
                self.assertEqual(output, "")
                self.assertNotIn(CREDENTIALS["GATEWAY_API_KEY"], diagnostics)
                self.assertIn("history/status", json.loads(diagnostics)["message"])

    def test_configuration_requires_the_installed_loopback_http_origin(self):
        for origin in (
            "http://remote.example:8123",
            "https://127.0.0.1:8123",
            "http://127.0.0.1",
            "http://127.0.0.1:8123/",
            "http://127.0.0.1:8123/api",
            "http://127.0.0.1:0",
        ):
            with self.subTest(origin=origin), patch("broodling.cli.Broodling") as app:
                (self.root / "config.json").write_text(json.dumps({
                    "direct_target_origin": origin,
                }))
                code, output, diagnostics = self.invoke(
                    "history", "--repository", "faviann/broodling", "--issue", "12"
                )
                self.assertEqual(code, 1)
                self.assertEqual(output, "")
                self.assertEqual(json.loads(diagnostics)["error"], "InstallationError")
                app.assert_not_called()


class OperatorStoreLifecycleTests(OperatorCLIBase):
    def invoke_at(self, root, *arguments):
        stdout = io.StringIO()
        stderr = io.StringIO()
        with redirect_stdout(stdout), redirect_stderr(stderr):
            code = main(["--root", str(root), *arguments])
        return code, stdout.getvalue(), stderr.getvalue()

    def test_normal_command_refuses_missing_store_then_operator_can_initialize(self):
        root = self.root / "fresh-install"
        root.mkdir()
        (root / "config.json").write_text(json.dumps({
            "direct_target_origin": "http://127.0.0.1:8123",
        }))
        database = root / "state" / "broodling.sqlite3"

        code, output, diagnostics = self.invoke_at(
            root, "history", "--repository", "faviann/broodling", "--issue", "12"
        )
        self.assertEqual(code, 1)
        self.assertEqual(output, "")
        self.assertEqual(json.loads(diagnostics)["error"], "InstallationError")
        self.assertFalse(database.exists())

        with patch("broodling.cli.ZeroshotSubmitter") as submitter:
            code, output, diagnostics = self.invoke_at(root, "initialize-store")
        self.assertEqual(code, 0, diagnostics)
        self.assertEqual(diagnostics, "")
        self.assertEqual(
            json.loads(output)["schema"]["schema_version"], str(SCHEMA_VERSION)
        )
        submitter.assert_not_called()

        code, output, diagnostics = self.invoke_at(
            root, "history", "--repository", "faviann/broodling", "--issue", "12"
        )
        self.assertEqual(code, 0, diagnostics)
        self.assertEqual(json.loads(output), [])

    def test_operator_upgrade_command_explicitly_upgrades_known_state(self):
        restore_published_schema(self.store, 9)
        self.store.close()

        with patch("broodling.cli.ZeroshotSubmitter") as submitter:
            code, output, diagnostics = self.invoke("upgrade-store")

        self.assertEqual(code, 0, diagnostics)
        self.assertEqual(diagnostics, "")
        self.assertEqual(
            json.loads(output)["schema"]["schema_version"], str(SCHEMA_VERSION)
        )
        submitter.assert_not_called()

    def test_initialize_command_refuses_existing_state_without_changing_it(self):
        original = self.store_path.read_bytes()

        code, output, diagnostics = self.invoke("initialize-store")

        self.assertEqual(code, 1)
        self.assertEqual(output, "")
        error = json.loads(diagnostics)
        self.assertEqual(error["error"], "FileExistsError")
        self.assertIn("Existing state was not replaced", error["message"])
        self.assertEqual(self.store_path.read_bytes(), original)


class OperatorLifecycleTests(OperatorCLIBase):
    """Real local services, controlled GitHub/SDK effects, repeated CLI calls."""

    def setUp(self):
        super().setUp()
        # First admission requires a durable repository/worktree root.
        self.host = durable_test_root(prefix="broodling-cli-")
        self.addCleanup(shutil.rmtree, self.host, ignore_errors=True)
        self.root = self.host / "installed"
        self.root.mkdir()
        (self.root / "config.json").write_text(json.dumps({
            "direct_target_origin": "http://127.0.0.1:8123",
        }))
        with BroodlingStore.initialize(self.root / "state" / "broodling.sqlite3"):
            pass
        self.repository = self.host / "source"
        make_repository(self.repository)
        git(self.repository, "remote", "add", "origin", "https://github.com/faviann/broodling.git")
        self.proposer = self.host / "proposer.py"
        self.proposer.write_text(
            "from broodling import Contract, Criterion\n"
            "def propose(inputs):\n"
            "    return Contract(\n"
            "        work_unit_id=inputs.work_unit.work_unit_id,\n"
            "        source_attribution=inputs.source_attribution,\n"
            "        criteria=(Criterion('request', 'Implement the complete frozen issue.'),),\n"
            "        required_effects=inputs.required_effects,\n"
            "        constructed_by=inputs.constructed_by)\n"
        )
        self.run = SimpleNamespace(
            id="native-run",
            wait=AsyncMock(return_value=RunResult(
                run_id="native-run", succeeded=True,
                output={
                    "version": "v1", "mode": "pr", "outcome": "opened",
                    "repository": "faviann/broodling", "targetBranch": "main",
                    "headRevision": "b" * 40, "pullRequestId": "50",
                },
            )),
            force_stop=AsyncMock(),
        )
        self.client = MagicMock()
        self.client.__aenter__ = AsyncMock(return_value=self.client)
        self.client.__aexit__ = AsyncMock(return_value=None)
        self.client.submit = AsyncMock(return_value=self.run)
        self.client.get_run.return_value = self.run
        self.enterContext(patch("zeroshot.Client", return_value=self.client))
        self.issue = (Path(__file__).parent / "fixtures/ingress/issue-82.json").read_bytes()
        real_run = subprocess.run

        def external_run(command, *args, **kwargs):
            if command[0] == "gh":
                return subprocess.CompletedProcess(command, 0, stdout=self.issue, stderr=b"")
            return real_run(command, *args, **kwargs)

        self.enterContext(patch("subprocess.run", side_effect=external_run))

    def submit(self):
        with patch.dict(os.environ, CREDENTIALS):
            code, output, error = self.invoke(
                "submit", "--repository", "faviann/broodling", "--issue", "82",
                "--checkout", str(self.repository), "--revision", "HEAD",
                "--target-branch", "main", "--proposer", str(self.proposer),
            )
        self.assertEqual(code, 0, error)
        self.assertEqual(error, "")
        for secret in (CREDENTIALS["GH_TOKEN"], CREDENTIALS["GATEWAY_API_KEY"]):
            self.assertNotIn(secret, output)
        return json.loads(output)

    def test_submit_resume_wait_preserve_authority_and_allow_credential_free_reconnection(self):
        first = self.submit()
        self.assertEqual(first["revision"]["constructed_by"], "caller")
        self.assertEqual(first["revision"]["contract"]["requiredEffects"][0]["targetBranch"], "main")
        revision = first["revision"]["contract_revision_id"]
        attempt = first["attempt"]["attempt_id"]
        self.proposer.unlink()
        with patch.dict(os.environ, {}, clear=True):
            code, output, error = self.invoke("resume", revision)
            self.assertEqual(code, 0, error)
            self.assertEqual(json.loads(output), first)
            code, output, error = self.invoke("wait", attempt)
            self.assertEqual(code, 0, error)
            self.assertEqual(json.loads(output)["outcome"], "SUCCEEDED")
            code, output, error = self.invoke("status", revision)
            self.assertEqual(code, 0, error)
            self.assertEqual(json.loads(output)["disposition"]["outcome"], "SUCCEEDED")
        self.client.submit.assert_awaited_once()
        self.run.wait.assert_awaited_once()

    def test_stop_reports_retained_quarantine_without_success_or_replacement(self):
        first = self.submit()
        attempt = first["attempt"]["attempt_id"]
        code, output, error = self.invoke("stop", attempt, "--reason", "operator stop")
        self.assertEqual(code, 3)
        self.assertEqual(output, "")
        self.assertEqual(json.loads(error)["error"], "CessationUnconfirmed")
        self.assertIn("quarantine", json.loads(error)["message"])
        self.assertTrue(Path(first["worktree"]["worktree_path"]).is_dir())
        code, output, error = self.invoke("resume", first["revision"]["contract_revision_id"])
        self.assertEqual(code, 0, error)
        self.assertEqual(json.loads(output)["abandonment"]["reason"], "operator stop")
        self.run.force_stop.assert_awaited_once()
        self.client.submit.assert_awaited_once()
