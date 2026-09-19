"""Post-outcome evaluator diagnostic; no provider work, file writes, or rescoring.

Run in the approved credential-free/network-disabled sandbox with read-only
R01/review-inputs mounted at /judge. This is not an activated evaluator version.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else "/judge")
expected = {
    "frozen-v1-judge.py": "b6dbf57a606e95377eadb89dea72ad2979b84147705be384c361f883483ddb95",
    "b1-tiny.py": "f541d2923c53624d41284164d00cb7c8aa025dfda3883ac4c0b3c211d691385c",
    "receipt-tiny.py": "8baa4bad9f19f6a64f4fb515298a741297065bd96b3955aa129242048ec69582",
}
assert set(os.environ) <= {"PATH", "HOME", "LANG", "LC_ALL", "LC_CTYPE", "PWD"}
sources = {name: (root / name).read_bytes() for name in expected}
assert {k: hashlib.sha256(v).hexdigest() for k, v in sources.items()} == expected

def load_judge():
    ns = {"__name__": "offline_frozen_judge", "__file__": str(root / "frozen-v1-judge.py")}
    exec(compile(sources["frozen-v1-judge.py"], ns["__file__"], "exec"), ns)
    return ns

frozen = load_judge()
class FalseyString(str):
    def __bool__(self):
        return False

argument = FalseyString("80")
cloned = copy.deepcopy((argument,))[0]
assert type(cloned) is FalseyString and not bool(cloned)
assert str.__len__(cloned) == 2 and str.isascii(cloned) and str.isdigit(cloned)
regression = ("valid-falsey-str-80", (argument,), 80, None)
original_reference = frozen["REFERENCES"]["T1"]
needle = '    if not text or any(c not in "0123456789" for c in text):'
assert original_reference.count(needle) == 1
corrected_reference = original_reference.replace(needle, "    text = str.__str__(text)\n" + needle, 1)
prospective = load_judge()
prospective["cases"] = lambda task: frozen["cases"](task) + ([regression] if task == "T1" else [])

def summary(rows):
    return {"passed": sum(bool(r["pass"]) for r in rows), "total": len(rows),
            "failures": [r for r in rows if not r["pass"]]}

controls = []
for task in frozen["FUNCTIONS"]:
    for label, source, wanted in (("B1", sources["b1-tiny.py"], False),
                                   ("reference", frozen["REFERENCES"][task], True)):
        passed = all(r["pass"] for r in frozen["check_source"](task, source))
        assert passed == wanted
        controls.append({"task": task, "control": label, "expected_pass": wanted, "observed_pass": passed})
comparisons = {}
for label, source in (("exact_accepted_revision", sources["receipt-tiny.py"]),
                      ("frozen_reference", original_reference),
                      ("proposed_reference", corrected_reference)):
    old = frozen["check_source"]("T1", source)
    new = prospective["check_source"]("T1", source)
    assert all(r["pass"] for r in old)
    assert [r["id"] for r in new if not r["pass"]] == ([] if label == "proposed_reference" else [regression[0]])
    comparisons[label] = {"frozen_behavioral_checks": summary(old), "one_case_prospective_simulation": summary(new)}
assert len(frozen["cases"]("T1")) == 16
assert frozen["REFERENCES"]["T1"] == original_reference
assert {k: hashlib.sha256((root / k).read_bytes()).hexdigest() for k in expected} == expected
print(json.dumps({
    "record_kind": "post-outcome offline calibration diagnostic; not a cohort or rescore",
    "headRevision": "248d67d35fc8fe6ac5dba9a0fb8cae831ae22631",
    "python": sys.version,
    "environment_keys": sorted(os.environ),
    "network_namespace": os.readlink("/proc/self/ns/net"),
    "ipv4_routes": Path("/proc/net/route").read_text(),
    "input_sha256_before_and_after": expected,
    "deepcopy_preserves_regression_subclass_and_false_truth": True,
    "frozen_calibration_controls": controls,
    "comparisons": comparisons,
    "proposed_reference_sha256": hashlib.sha256(corrected_reference.encode()).hexdigest(),
    "diagnostic_assertions_passed": True,
    "historical_scope_checks": "Not rerun here; retained full CLI result remains 18/18 PASS with independent review required.",
}, indent=2))
