"""One diagnostic chain, never a #23/G4 integrated real-provider witness.

Reuse frozen natural-fixture admission, public observation, custody and cleanup.
A declared no-op implementer leaves a B1 with one known duplicate-header defect.
Every downstream model role delegates unchanged prompts to actual qualified Codex.
The executable shim is a declared diagnostic substitution, not a qualified all-real
configuration. No product source, graph, prompt or runtime configuration is edited.
"""

import argparse
import hashlib
import json
import shutil
import subprocess
import traceback
from datetime import UTC, datetime
from pathlib import Path
from unittest.mock import patch

import issue23_natural_provider as fixture

CANDIDATE = """import csv
import io


def read_records(text):
    rows = csv.reader(io.StringIO(text, newline=""))
    header = next((row for row in rows if row != []), None)
    if header is None:
        return []
    header = [name.strip() for name in header]
    if any(name == "" for name in header):
        raise ValueError("empty header name")
    records = []
    for row in rows:
        if row == []:
            continue
        if len(row) != len(header):
            raise ValueError("row width does not match header")
        records.append(dict(zip(header, row)))
    return records
"""


def hashes():
    values = fixture.hashes()
    for path in (
        Path(__file__),
        Path(__file__).with_name("issue23-diagnostic-bin") / "codex",
    ):
        values[str(path.resolve().relative_to(fixture.ROOT))] = hashlib.sha256(
            path.read_bytes()
        ).hexdigest()
    return values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() or output.with_suffix(".sqlite3").exists():
        parser.error("refusing to overwrite evidence")
    shim = Path(__file__).with_name("issue23-diagnostic-bin").resolve() / "codex"
    record = {
        "scope": __doc__,
        "maximumDiagnosticChains": 1,
        "integratedRealRepairWitness": False,
        "passed": False,
        "controlledRoles": ["implement"],
        "predeclaredCandidate": CANDIDATE,
        "knownDefect": "Duplicate normalized headers are not rejected; name, name produces silent key collision, including header-only input.",
        "startedAt": datetime.now(UTC).isoformat(),
        "productCommit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], text=True
        ).strip(),
        "sourceSha256": hashes(),
    }
    checkpoint = lambda: fixture.save(output, record)
    original_which = shutil.which
    checkpoint()
    try:
        record["runtime"] = fixture.runtime_versions()
        record["integration"] = fixture.installed_integration()
        with (
            patch.object(fixture, "CSV_RECORDS", CANDIDATE),
            patch.object(
                shutil,
                "which",
                side_effect=lambda name, *a, **kw: (
                    str(shim) if name == "codex" else original_which(name, *a, **kw)
                ),
            ),
        ):
            fixture.run(record, checkpoint, output, 900)
    except BaseException as error:  # noqa: BLE001 - preserve diagnostic failure and cleanup evidence
        record["error"] = repr(error)
        record["traceback"] = traceback.format_exc()
    finally:
        record["finalSourceSha256"] = hashes()
        record["sourceHashesUnchangedThroughout"] = (
            record["sourceSha256"] == record["finalSourceSha256"]
        )
        record["finishedAt"] = datetime.now(UTC).isoformat()
        # The inherited structural checks cannot establish all-real provenance.
        record["inheritedChecksAreDiagnosticOnly"] = True
        record["integratedRealRepairWitness"] = False
        record["passed"] = False
        checkpoint()
    print(
        json.dumps(
            {
                key: record.get(key)
                for key in ("runId", "error", "cleanupError", "disposition", "checks")
            }
        )
    )
    return 1 if "error" in record or "cleanupError" in record else 0


if __name__ == "__main__":
    raise SystemExit(main())
