"""Offline p5-judge-v2 verification; run only in the credential-free sandbox.

Mount evaluation/p5 read-only at /p5 and the retained Git archive at /repository,
or pass its sandbox path. This executes inspected retained code, not a new run.
"""
import argparse
import ast
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("repository", nargs="?", type=Path, default=Path("/repository"))
args = parser.parse_args()
if not args.repository.is_dir():
    parser.error("mount the retained Git archive at /repository or pass its sandbox path")

root = Path(__file__).resolve().parents[2]
run = root / "runs/2026-09-19-v6-r01/R01"
inputs = run / "review-inputs"
head = "248d67d35fc8fe6ac5dba9a0fb8cae831ae22631"
regression = "valid-falsey-str-80"
assert set(os.environ) <= {"PATH", "HOME", "LANG", "LC_ALL", "LC_CTYPE", "PWD"}
routes = Path("/proc/net/route").read_text()
assert len(routes.splitlines()) == 1, "sandbox must have no IPv4 routes"

paths = [root / "v1/judge.py", root / "v1/corpus.md", root / "v1/protocol.md",
         root / "v1/fixture/tiny.py", Path(__file__), Path(__file__).with_name("judge.py"),
         run / "receipt.json", *sorted(inputs.iterdir())]
sources = {path: path.read_bytes() for path in paths}
digests = {str(path.relative_to(root)): hashlib.sha256(data).hexdigest()
           for path, data in sources.items()}
expected = {
    root / "v1/judge.py": "b6dbf57a606e95377eadb89dea72ad2979b84147705be384c361f883483ddb95",
    root / "v1/corpus.md": "7b8de1a891d0d152602b26ea2e2c361b74d6ea4e34d87b38df8295c879e22fb8",
    root / "v1/fixture/tiny.py": "f541d2923c53624d41284164d00cb7c8aa025dfda3883ac4c0b3c211d691385c",
    inputs / "receipt-tiny.py": "8baa4bad9f19f6a64f4fb515298a741297065bd96b3955aa129242048ec69582",
}
for path, digest in expected.items():
    assert hashlib.sha256(sources[path]).hexdigest() == digest
assert sources[root / "v1/judge.py"] == sources[inputs / "frozen-v1-judge.py"]
assert sources[root / "v1/fixture/tiny.py"] == sources[inputs / "b1-tiny.py"]
assert json.loads(sources[run / "receipt.json"])["headRevision"] == head


def load(path):
    namespace = {"__name__": "offline_judge", "__file__": str(path)}
    exec(compile(sources[path], str(path), "exec"), namespace)
    return namespace


old_path, new_path = root / "v1/judge.py", Path(__file__).with_name("judge.py")
old, new = load(old_path), load(new_path)
assert new["VERSION"] == "p5-judge-v2"
assert old["B1"] == new["B1"] == "884bd64264df1515bee76a63f548db9cabe25a35"
assert old["FUNCTIONS"] == new["FUNCTIONS"]
for task in old["FUNCTIONS"]:
    original, revised = old["cases"](task), new["cases"](task)
    assert original == (revised[:-1] if task == "T1" else revised)
    if task != "T1":
        assert old["REFERENCES"][task] == new["REFERENCES"][task]
assert len(old["cases"]("T1")) == 16 and len(new["cases"]("T1")) == 17
assert new["OLD_T1_REFERENCE"] == old["REFERENCES"]["T1"]
assert new["REFERENCES"]["T1"] == old["REFERENCES"]["T1"].replace(
    '        raise TypeError("string required")\n',
    '        raise TypeError("string required")\n    text = str.__str__(text)\n', 1)
case_id, arguments, expected_value, exception = new["cases"]("T1")[-1]
assert (case_id, expected_value, exception) == (regression, 80, None)
assert type(expected_value) is int
for argument in (arguments[0], copy.deepcopy(arguments)[0]):
    assert type(argument) is new["FalseyString"] and not bool(argument)
    assert str.__str__(argument) == "80" and str.__len__(argument) == 2

# The source checker and receipt/scope CLI retain their exact v1 logic.
trees = [ast.parse(sources[path]) for path in (old_path, new_path)]
for name in ("check_source", "main"):
    functions = [next(node for node in tree.body
                      if isinstance(node, ast.FunctionDef) and node.name == name)
                 for tree in trees]
    for node in ast.walk(functions[1]):
        if isinstance(node, ast.Dict):
            for index in reversed(range(len(node.keys))):
                if isinstance(node.keys[index], ast.Constant) and node.keys[index].value == "evaluator":
                    assert isinstance(node.values[index], ast.Name) and node.values[index].id == "VERSION"
                    del node.keys[index], node.values[index]
    assert ast.dump(functions[0]) == ast.dump(functions[1]), name


def summary(rows):
    return {"passed": sum(bool(row["pass"]) for row in rows), "total": len(rows),
            "failures": [row for row in rows if not row["pass"]]}


comparisons = {}
for label, source in (("exact_accepted_revision", sources[inputs / "receipt-tiny.py"]),
                      ("frozen_reference", new["OLD_T1_REFERENCE"]),
                      ("corrected_reference", new["REFERENCES"]["T1"])):
    before, after = [judge["check_source"]("T1", source) for judge in (old, new)]
    assert all(row["pass"] for row in before) and before == after[:-1]
    assert [row["id"] for row in after if not row["pass"]] == (
        [] if label == "corrected_reference" else [regression])
    comparisons[label] = {"v1": summary(before), "v2": summary(after)}


def cli(path, *arguments):
    result = subprocess.run([sys.executable, "-I", "-B", str(path), *map(str, arguments)],
                            capture_output=True, text=True, timeout=30)
    assert result.stderr == "", result.stderr
    return {"exit_code": result.returncode, "stderr": result.stderr,
            "stdout": json.loads(result.stdout)}


calibration = {name: cli(path, "--self-test") for name, path in (("v1", old_path), ("v2", new_path))}
for name, count in (("v1", 8), ("v2", 9)):
    result = calibration[name]
    assert result["exit_code"] == 0 and result["stdout"]["calibration_pass"] is True
    assert len(result["stdout"]["controls"]) == count
assert calibration["v1"]["stdout"]["controls"] == calibration["v2"]["stdout"]["controls"][:8]
assert calibration["v2"]["stdout"]["evaluator"] == new["VERSION"]
assert calibration["v2"]["stdout"]["controls"][-1] == {
    "task": "T1", "control": "v1-reference", "expected_pass": False, "observed_pass": False,
    "expected_failures": [regression], "observed_failures": [regression]}


def archive_objects():
    objects = {}
    for revision, label in ((old["B1"], "b1"), (head, "receipt")):
        for filename in ("tiny.py", "README.md"):
            data = subprocess.run(
                ["git", "--no-replace-objects", "-C", str(args.repository),
                 "show", f"{revision}:{filename}"],
                check=True, capture_output=True, timeout=10).stdout
            assert data == sources[inputs / f"{label}-{filename}"]
            objects[f"{revision}:{filename}"] = hashlib.sha256(data).hexdigest()
    return objects


archive_before = archive_objects()
receipt_cli = {name: cli(path, "T1", args.repository, run / "receipt.json")
               for name, path in (("v1", old_path), ("v2", new_path))}
for name, code, total, verdict in (("v1", 0, 18, "PASS"), ("v2", 1, 19, "FAIL")):
    result = receipt_cli[name]
    output = result["stdout"]
    assert result["exit_code"] == code and output["automated_checks"] == verdict
    assert output["independent_review"] == "REQUIRED"
    assert summary(output["observations"])["passed"] == 18
    assert len(output["observations"]) == total
    assert output["baseRevision"] == old["B1"] and output["headRevision"] == head
    assert output["source_sha256"] == expected[inputs / "receipt-tiny.py"]
before, after = [receipt_cli[name]["stdout"] for name in ("v1", "v2")]
assert after["evaluator"] == new["VERSION"]
assert before["observations"] == [row for row in after["observations"] if row["id"] != regression]
assert [row["id"] for row in after["observations"] if not row["pass"]] == [regression]
assert before["observations"][-2:] == [
    {"id": "README-unchanged", "pass": True}, {"id": "file-scope", "pass": True}]
assert archive_objects() == archive_before
assert all(path.read_bytes() == data for path, data in sources.items())

print(json.dumps({
    "record_kind": "post-outcome offline evaluator verification; not a cohort or historical rescore",
    "evaluator": new["VERSION"], "python": sys.version,
    "environment_keys": sorted(os.environ), "network_namespace": os.readlink("/proc/self/ns/net"),
    "ipv4_routes": routes, "input_sha256_before_and_after": digests,
    "archive_git_objects_sha256_before_and_after": archive_before,
    "deepcopy_preserves_regression_subclass_and_false_truth": True,
    "existing_cases_references_checker_and_cli_preserved": True,
    "calibration": calibration, "behavioral_comparisons": comparisons,
    "retained_receipt_cli": receipt_cli, "verification_assertions_passed": True,
}, indent=2))
