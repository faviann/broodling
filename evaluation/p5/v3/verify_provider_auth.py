#!/usr/bin/env python3
"""Verify the PR credential path without admitting or submitting work.

Uses non-secret sentinels only. Native profile materialization and --validate-only
run in disposable local state: no target, task execution, or real credential is
used. The resulting JSON combines observed bindings with a pinned-source audit.
"""

from __future__ import annotations

import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from unittest.mock import patch

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY))

from broodling.zeroshot_sdk import ZeroshotSubmitter  # noqa: E402
from zeroshot import UniformRuntime  # noqa: E402
from zeroshot._binary import resolve_binary  # noqa: E402

UPSTREAM = "https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/"


def observe() -> dict:
    with patch.dict(os.environ, {"OPENAI_API_KEY": "ambient-must-not-win-sentinel"}):
        submitter = ZeroshotSubmitter(
            "/tmp/p5-auth-probe-unused-state",
            delivery_target_origin="http://127.0.0.1:8123",
            github_token="non-secret-delivery-sentinel",
            openai_api_key="non-secret-provider-sentinel",
        )
        request = {
            "target": submitter.target,
            "preset": {"name": "software-change", "delivery": "pull_request"},
            "runtime": submitter.runtime_for("pull_request"),
            "workspace": str(REPOSITORY),
        }
        client = submitter._submission_client(request)
        environment, _ = client._environment()
        environment_probe = {
            "ambientOpenaiKeyPresentForProbe": True,
            "sdkEnvironmentKeyNames": sorted(environment),
            "sdkOpenaiKeyPresent": "OPENAI_API_KEY" in environment,
            "sdkOpenaiKeyMatchesDispatchCredential": (
                environment.get("OPENAI_API_KEY") == "non-secret-provider-sentinel"
            ),
            "sdkCodexKeyPresent": "CODEX_API_KEY" in environment,
            "sdkDeliveryKeyPresent": "GH_TOKEN" in environment,
            "sdkHomeIsEmpty": environment["HOME"] == "",
            "sdkCodexHomeIsEmpty": environment["CODEX_HOME"] == "",
        }
    runtime = UniformRuntime(**request["runtime"]).to_dict()
    binary = resolve_binary()
    with tempfile.TemporaryDirectory(prefix="p5-auth-check-") as directory:
        scratch = Path(directory)
        clean_environment = {
            "PATH": "/usr/local/bin:/usr/bin:/bin",
            "HOME": str(scratch / "home"),
            "ZEROSHOT_STATE_DIR": str(scratch / "state"),
            "ZEROSHOT_CONFIG_DIR": str(scratch / "config"),
        }
        runtime_file = scratch / "runtime.json"
        input_file = scratch / "input.json"
        runtime_file.write_text(json.dumps(runtime))
        input_file.write_text(json.dumps({"task": "Non-executed configuration probe."}))
        commands = []

        def native(*arguments: str) -> str:
            result = subprocess.run(
                [str(binary), *arguments],
                cwd=scratch,
                env=clean_environment,
                capture_output=True,
                text=True,
                check=True,
            )
            commands.append(["zeroshot", *[
                argument.replace(str(scratch), "<temporary-directory>")
                for argument in arguments
            ]])
            return result.stdout.strip()

        version = native("--version")
        native(
            "profile", "set", "p5-auth-verification",
            "--template", "software-change", "--delivery", "pull_request",
            "--uniform-runtime-config", str(runtime_file),
        )
        profile = json.loads(native("profile", "show", "p5-auth-verification"))
        validation = json.loads(native(
            "run", "--template", "software-change", "--delivery", "pull_request",
            "--uniform-runtime-config", str(runtime_file),
            "--title", "P5 non-executed configuration verification",
            "--input", str(input_file), "--validate-only",
        ))
        expanded_runtime = profile.get("profile", profile)["runtime"]
    assert version == "zeroshot 10.3.0", "native baseline changed; reassess this probe"
    assert validation == {"valid": True}
    assert environment_probe["sdkOpenaiKeyPresent"], "provider credential was not handed off"
    assert environment_probe["sdkOpenaiKeyMatchesDispatchCredential"]
    assert not environment_probe["sdkCodexKeyPresent"], "credential path changed; reassess"
    assert environment_probe["sdkDeliveryKeyPresent"]
    assert "non-secret-provider-sentinel" not in json.dumps(request)
    assert "non-secret-delivery-sentinel" not in json.dumps(request)
    for binding in expanded_runtime["nodes"].values():
        if binding["kind"] == "agent":
            assert binding["connections"] == {"openai": ["OPENAI_API_KEY"]}
    return {
        "schema": "p5-v3-provider-auth-verification/v2",
        "verdict": "PROVIDER_AUTH_PATH_READY",
        "trialState": "R01_NOT_STARTED",
        "scope": {
            "broodlingAdmissions": 0,
            "nativeRunSubmissions": 0,
            "providerTasks": 0,
            "targetConnections": 0,
            "realCredentialsReadOrRetained": False,
            "persistentConfigurationChanges": False,
            "temporaryLocalProfileMaterialization": True,
        },
        "integration": {
            "sdkVersion": importlib.metadata.version("the-open-engine-zeroshot"),
            "nativeVersion": version,
            "nativeBinarySha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
            "selectedRuntime": runtime,
        },
        "observed": {
            "productionClientEnvironment": environment_probe,
            "installedNativeExpandedRuntime": expanded_runtime,
            "nativeValidateOnly": validation,
            "nativeCommands": commands,
        },
        "conclusion": (
            "The installed native materializer requires an openai connection with "
            "OPENAI_API_KEY for each selected agent. Broodling supplies the explicit "
            "current dispatch credential to the SDK alongside GH_TOKEN while keeping "
            "both values out of the persisted request. Pinned source confirms that "
            "this is the supported DirectTarget path. No R01 admission occurred."
        ),
        "sourceAudit": [
            {
                "fact": "Default OpenAI runtime connection is openai: [OPENAI_API_KEY].",
                "source": UPSTREAM + "zeroshot/src/native_v2_cli/execution/submission.rs#L432-L441",
            },
            {
                "fact": "Direct submission passes supplied connections with connection_resolver=None.",
                "source": UPSTREAM + "zeroshot/src/native_v2_target.rs#L355-L370",
            },
            {
                "fact": "Target uses RunEnvironment::exact when no resolver is supplied.",
                "source": UPSTREAM + "zeroshot/src/native_v2_hosting.rs#L160-L167",
            },
            {
                "fact": "Exact environment requires every declared connection before run submission.",
                "source": UPSTREAM + "zeroshot/src/native_v2_supervisor/environment.rs#L235-L246",
            },
            {
                "fact": "DirectTarget does not advertise connection management.",
                "source": UPSTREAM + "zeroshot/src/native_v2_target/controller_authority/connections.rs#L12-L20",
            },
            {
                "fact": "Hosted Codex configuration has local_user=None.",
                "source": UPSTREAM + "zeroshot/src/native_v2_hosting/allocator.rs#L209-L218",
            },
            {
                "fact": "Hosted OpenAI adapter requires a supplied API key before launching Codex.",
                "source": UPSTREAM + "zeroshot/src/native_v2_codex/command.rs#L85-L100",
            },
        ],
        "limits": [
            "The validation-only command proves runtime validity, not provider authentication.",
            "The environment probe observes current production code with non-secret sentinels only.",
            "No target-side rejection, provider failure, live receipt, or quality judgment was observed.",
            "Pinned source and wheel report the same release identity; full binary/source parity was not established. The source serve implementation mentions bootstrap-key-file while the installed binary help exposes only listen/public-origin/storage. Installed binary runtime expansion independently confirms the audit-critical credential binding.",
            "The publicly mutable submitter.target mapping is not used to inject credentials: doing so would retain secret values in the frozen invocation and would not be the configured product credential path.",
        ],
    }


if __name__ == "__main__":
    print(json.dumps(observe(), indent=2, sort_keys=True))
