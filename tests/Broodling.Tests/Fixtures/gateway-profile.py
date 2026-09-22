"""Local profile materialization only: no target, provider or forge call."""
import importlib.resources
import json
import subprocess
import sys
from pathlib import Path

from zeroshot import UniformRuntime

root = Path.cwd()
runtime = UniformRuntime(**json.load(sys.stdin)).to_dict()
(root / "runtime.json").write_text(json.dumps(runtime))
environment = {"PATH": "/usr/bin:/bin", "HOME": str(root / "home"),
               "ZEROSHOT_CONFIG_DIR": str(root / "config"), "ZEROSHOT_STATE_DIR": str(root / "state"),
               "GH_TOKEN": "GITHUB_CANARY", "GATEWAY_API_KEY": "GATEWAY_CANARY",
               "GATEWAY_BASE_URL": "https://cliproxy.local.faviann.com/v1"}
binary = str(importlib.resources.files("zeroshot").joinpath("_bin", "zeroshot"))


def profile(*arguments):
    result = subprocess.run([binary, "profile", *arguments], cwd=root, env=environment,
                            capture_output=True, check=True, text=True, timeout=30)
    return json.loads(result.stdout)


profile("set", "gateway-witness", "--template", "software-change", "--delivery", "pull_request",
        "--uniform-runtime-config", str(root / "runtime.json"))
expanded = profile("show", "gateway-witness")
print(json.dumps({"runtime": runtime, "profile": expanded.get("profile", expanded)["runtime"]}))
