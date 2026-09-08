"""Read-only retained-source diagnostic checks; no provider or authority calls."""

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = Path(__file__).with_name("evidence")
CASES = (
    ("duplicate-with-data", "name, name\nAda,Grace\n", ValueError),
    ("duplicate-header-only", "name, name\n", ValueError),
    ("unicode-normalized-duplicate", "\u2003name,name\u2003\n", ValueError),
    ("empty-header-only", "name, \n", ValueError),
    ("short-row", "name,city\nAda\n", ValueError),
    ("long-row", "name,city\nAda,Paris,extra\n", ValueError),
    ("empty-document", "", []),
    ("blank-document", "\n\n", []),
    ("header-only", "name,city\n", []),
    (
        "blank-rows-and-preservation",
        "\n name ,city\n\n Ada , Paris \n\n",
        [{"name": " Ada ", "city": " Paris "}],
    ),
    (
        "quoted-newline",
        'name,note\nAda,"first\nsecond"\n',
        [{"name": "Ada", "note": "first\nsecond"}],
    ),
    ("empty-cell-is-not-blank-row", 'name\n""\n', [{"name": ""}]),
)


def probe(source):
    # Both retained finite sources were inspected before this diagnostic audit.
    namespace = {}
    exec(compile(source, "retained-csv-diagnostic", "exec"), namespace)  # noqa: S102 - inspected finite fixture source
    result = []
    for name, text, expected in CASES:
        try:
            actual = namespace["read_records"](text)
            result.append(
                {
                    "case": name,
                    "input": text,
                    "actual": actual,
                    "passed": expected is not ValueError and actual == expected,
                }
            )
        except ValueError as error:
            result.append(
                {
                    "case": name,
                    "input": text,
                    "error": str(error),
                    "passed": expected is ValueError,
                }
            )
    return result


def main():
    raw = EVIDENCE / "issue-23-repair-diagnostic.json"
    record = json.loads(raw.read_text())
    assert not any(
        key in record for key in ("error", "cleanupError", "diagnosticLogError")
    )
    assert record["integratedRealRepairWitness"] is False and record["passed"] is False
    assert record["sourceHashesUnchangedThroughout"]
    for path, digest in record["sourceSha256"].items():
        assert hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == digest, path
    nodes = {}
    for status in record["publicStatuses"]:
        for execution in status["active_executions"]:
            nodes.setdefault(execution["node"], set()).add(execution["execution"])
    assert len(nodes["repair"]) == 1
    before = probe(record["b1Files"]["csv_records.py"])
    after = probe(record["finalFiles"]["csv_records.py"])
    assert [item["case"] for item in before if not item["passed"]] == [
        case[0] for case in CASES[:3]
    ]
    assert all(item["passed"] for item in after)
    assert (
        record["b1Files"]["check_csv_records.py"]
        == record["finalFiles"]["check_csv_records.py"]
    )
    assert record["rereadCustody"] == record["custody"]
    assert record["rereadDisposition"] == record["disposition"]
    assert record["disposition"]["outcome"] == "SUCCEEDED"
    roles = record["roleResponses"]
    expected_signals = {
        "initial_review": {"findings": "found"},
        "adjudicate_authority": {"decision": "open_d1"},
        "repair_review": {"findings": "clean"},
        "resolution_authority": {"resolution": "resolved_d1"},
        "final_assessment_authority_repaired": {"assessment": "accepted"},
    }
    for node, signal in expected_signals.items():
        assert [r["response"]["signals"] for r in roles if r["node"] == node] == [
            signal
        ]
    for node in ("initial_evidence_check", "repair_evidence_check"):
        response = next(r["response"] for r in roles if r["node"] == node)
        observation = response["output"]["evidenceContent"]["observations"][0]
        assert observation["exitCode"] == 0
        assert len(json.loads(observation["stdout"])) == 3
        assert all(item["passed"] for item in json.loads(observation["stdout"]))
    files = [
        raw,
        raw.with_suffix(".sqlite3"),
        Path(__file__),
        Path(__file__).with_name("issue23_repair_diagnostic.py"),
        Path(__file__).with_name("issue23-diagnostic-bin") / "codex",
    ]
    result = {
        "scope": __doc__,
        "integratedRealRepairWitness": False,
        "runId": record["runId"],
        "diagnosticChainCompleted": True,
        "uniqueExecutions": {
            node: sorted(executions) for node, executions in nodes.items()
        },
        "before": before,
        "after": after,
        "sourceHashesVerified": True,
        "initialAndRenewedSmokeCasesPassed": True,
        "checkerUnchanged": True,
        "cleanupReadbackVerified": True,
        "evidenceSha256": {
            str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in files
        },
        "limits": [
            "Known B1 defect and substituted no-op implementer; not an all-real configuration.",
            "One positive diagnostic chain cannot establish general semantic reliability or all negative resolution behavior.",
            "Inherited allRequiredActualProductRolesObserved checks role names, not provider provenance; it does not establish its name literally.",
            "Two repair response messages share one structural execution; there was one repair occurrence.",
            "No #23 completion or G4 PASS; #24 remains blocked.",
        ],
    }
    output = EVIDENCE / "issue-23-repair-diagnostic-audit.json"
    with output.open("x") as stream:
        json.dump(result, stream, indent=2, sort_keys=True)
        stream.write("\n")
    print(
        "Diagnostic audit passed: 3 discriminating B1 failures, 12/12 corrected cases, one repair occurrence; no integrated witness credit."
    )


if __name__ == "__main__":
    main()
