"""Precise offline SDK constructor/receipt witness; never DirectTarget delivery evidence."""
import importlib.metadata
import importlib.resources
import json
import runpy
import subprocess
import sys
import types
from pathlib import Path


class Target:
    def __init__(self, address=None, *, workspace=None, state_dir=None):
        self.address = address
        self.workspace = workspace
        self.state_dir = state_dir


class Arguments:
    def __init__(self, **values):
        self.values = values


receipt = {"version": "v1", "mode": "pr", "outcome": "opened", "repository": "acme/widget",
           "targetBranch": "main", "headRevision": "b" * 40, "pullRequestId": "50"}


class Run:
    id = "stub-pr-run"

    def __init__(self, address):
        self.address = address

    async def wait(self):
        output = {"receipt": receipt, "null": None, "false": False, "number": 12.5,
                  "string": "unchanged", "array": [False, None, 7]}[self.address.rsplit("/", 1)[-1]]
        return types.SimpleNamespace(run_id=self.id, succeeded=True, output=output, failure=None)

    async def force_stop(self):
        return types.SimpleNamespace(run_id=self.id, succeeded=False, output=None, failure="force_stopped")


class Client:
    def __init__(self, *, target, environment):
        self.target = target
        self.environment = environment

    async def __aenter__(self):
        return self

    async def __aexit__(self, *args):
        pass

    async def submit(self, task, *, title, preset, runtime, submission_key, **source):
        assert self.target.workspace
        assert self.target.address == "http://stub-target.invalid/receipt"
        assert self.target.state_dir is None
        assert preset.values == {"name": "software-change", "delivery": "pull_request"}
        assert runtime.values == {"harness": "codex", "provider": "gateway", "model": "gpt-5.6-sol",
                                  "effort": "medium", "size": "small", "session_scope": "execution"}
        assert source["repository"] == "acme/widget" and source["branch"] == "main"
        assert len(source["revision"]) == 40 and set(source) == {"repository", "branch", "revision"}
        assert self.environment["GH_TOKEN"] == "SYNTHETIC_GH_SECRET"
        assert self.environment["GATEWAY_API_KEY"] == "SYNTHETIC_GATEWAY_SECRET"
        assert self.environment["GATEWAY_BASE_URL"] == "https://cliproxy.local.faviann.com/v1"
        assert not {"OPENAI_API_KEY", "CODEX_API_KEY", "GITHUB_TOKEN", "OPENROUTER_API_KEY", "AWS_BEARER_TOKEN_BEDROCK"} & set(self.environment)
        assert "comparisonBase" in task and "admittedInstructions" in task and "contract" in task
        assert submission_key.startswith("broodling:dotnet:v1:")
        return Run(self.target.address)

    def get_run(self, run_id):
        assert run_id == Run.id
        assert self.environment == {}
        assert self.target.workspace is None
        return Run(self.target.address)


sdk = types.ModuleType("zeroshot")
sdk.Client = Client
sdk.DirectTarget = sdk.LocalTarget = Target
sdk.Preset = sdk.UniformRuntime = Arguments
errors = types.ModuleType("zeroshot.run_errors")
errors.SubmissionConflictError = type("SubmissionConflictError", (Exception,), {})
sys.modules["zeroshot"] = sdk
sys.modules["zeroshot.run_errors"] = errors
importlib.metadata.version = lambda _: "10.3.0.post1"
importlib.resources.files = lambda _: Path("/unused-stub-native")
subprocess.run = lambda *args, **kwargs: types.SimpleNamespace(stdout="zeroshot 10.3.0")
runpy.run_path(str(Path(__file__).parent.parent / "bridge" / "zeroshot_bridge.py"), run_name="__main__")
