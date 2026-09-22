import json
import sys
from pathlib import Path

request = json.load(sys.stdin)
if request["op"] == "version":
    override = Path(__file__).with_suffix(".version")
    print(override.read_text() if override.exists() else json.dumps({"ok": True, "sdkVersion": "10.3.0.post1", "nativeVersion": "zeroshot 10.3.0"}))
else:
    # Separate version preflight succeeds. Only the actual submit acknowledgment is corrupted.
    sys.stdout.write(Path(__file__).with_suffix(".response").read_text())
