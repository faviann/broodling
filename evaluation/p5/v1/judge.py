"""Evaluation-only checks. Never import this into Broodling or give it to a worker.

Run candidate code only in the operator-approved, credential-free judging sandbox.
The CLI reads Git objects at receipt headRevision, never the execution worktree.
An automated PASS is not the required independent scope/criteria review.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import subprocess
from pathlib import Path

B1 = "884bd64264df1515bee76a63f548db9cabe25a35"
FUNCTIONS = {"T1": "parse_port", "T2": "unique_words", "T3": "render_csv", "T4": "can_read"}


def cases(task):
    """Frozen (id, positional arguments, expected result, exception name) checks."""
    if task == "T1":
        return [
            ("minimum", ("1",), 1, None),
            ("maximum", ("65535",), 65535, None),
            ("leading-zero", ("00080",), 80, None),
            *[(f"invalid-{i}", (value,), None, "ValueError") for i, value in enumerate(
                ("0", "65536", "", " 80", "80 ", "+80", "-1", "8.0", "８０", "80\n")
            )],
            *[(f"type-{i}", (value,), None, "TypeError") for i, value in enumerate((80, True, None))],
        ]
    if task == "T2":
        return [
            ("empty", ([],), [], None),
            ("order-and-case", (["b", "a", "b", "A", "a"],), ["b", "a", "A"], None),
            ("empty-and-unicode", (["", "é", "", "e"],), ["", "é", "e"], None),
            ("repeated", (["x", "x", "x"],), ["x"], None),
        ]
    if task == "T3":
        return [
            ("no-rows", ([],), "", None),
            ("ordinary", ([["a", "b"], ["c", "d"]],), "a,b\nc,d\n", None),
            ("escaping", ([["a,b", 'say "hi"'], ["line\nbreak", "é"]],),
             '"a,b","say ""hi"""\n"line\nbreak",é\n', None),
            ("empty-field", ([[""]],), '""\n', None),
            ("empty-row", ([[]],), "\n", None),
            ("ragged", ([["a"], ["", "b", "c"]],), "a\n,b,c\n", None),
        ]
    return [
        *[(f"role-{role}-{public}", (role, public),
           role == "admin" or (role == "reader" and public), None)
          for role in ("admin", "reader", "guest", "ADMIN", "", "other", None)
          for public in (False, True)],
        *[(f"nonbool-{i}", ("admin", value), False, None)
          for i, value in enumerate((0, 1, "yes", None))],
    ]


def check_source(task, source):
    observations = []
    namespace = {"__name__": "p5_candidate"}
    try:
        exec(compile(source, "receipt-head/tiny.py", "exec"), namespace)
        function = namespace[FUNCTIONS[task]]
    except Exception as error:
        return [{"id": "candidate-load", "pass": False, "actual": repr(error)}]
    for case_id, original, expected, exception in cases(task):
        arguments = copy.deepcopy(original)
        try:
            actual = function(*arguments)
            passed = exception is None and type(actual) is type(expected) and actual == expected
            if task == "T2":
                passed = passed and actual is not arguments[0]
            observation = {"id": case_id, "pass": passed, "actual": repr(actual)}
        except Exception as error:
            observation = {"id": case_id, "pass": type(error).__name__ == exception,
                           "actual": repr(error)}
        observation["pass"] = observation["pass"] and arguments == original
        observations.append(observation)
    return observations


# Evaluator calibration only: not task inputs, delivered changes, or provider evidence.
REFERENCES = {
    "T1": '''def parse_port(text):
    if not isinstance(text, str):
        raise TypeError("string required")
    if not text or any(c not in "0123456789" for c in text):
        raise ValueError("ASCII digits required")
    significant = text.lstrip("0")
    if not significant or len(significant) > 5:
        raise ValueError("port out of range")
    port = int(significant)
    if port > 65535:
        raise ValueError("port out of range")
    return port
''',
    "T2": '''def unique_words(words):
    return list(dict.fromkeys(words))
''',
    "T3": '''import csv
import io

def render_csv(rows):
    output = io.StringIO(newline="")
    csv.writer(output, lineterminator="\\n").writerows(rows)
    return output.getvalue()
''',
    "T4": '''def can_read(role, public):
    return type(public) is bool and (role == "admin" or (role == "reader" and public))
''',
}


def self_test():
    base = Path(__file__).with_name("fixture").joinpath("tiny.py").read_bytes()
    observations = []
    for task in FUNCTIONS:
        for label, source, expected in (("B1", base, False),
                                        ("reference", REFERENCES[task], True)):
            passed = all(row["pass"] for row in check_source(task, source))
            observations.append({"task": task, "control": label,
                                 "expected_pass": expected, "observed_pass": passed})
    ok = all(row["expected_pass"] == row["observed_pass"] for row in observations)
    print(json.dumps({"calibration_pass": ok, "controls": observations}, indent=2))
    return 0 if ok else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("task", nargs="?", choices=FUNCTIONS)
    parser.add_argument("repository", nargs="?", type=Path)
    parser.add_argument("receipt", nargs="?", type=Path)
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if not all((args.task, args.repository, args.receipt)):
        parser.error("task, archived Git repository, and retained receipt JSON are required")

    def git(*arguments):
        return subprocess.run(
            ["git", "--no-replace-objects", "-C", str(args.repository), *arguments],
            check=True, capture_output=True, timeout=10,
        ).stdout

    try:
        receipt = json.loads(args.receipt.read_text())
        head = receipt["headRevision"]
        if not isinstance(head, str) or not re.fullmatch("[0-9a-f]{40}", head) or head == B1:
            raise ValueError("receipt must identify a non-B1 commit")
        if git("cat-file", "-t", head).strip() != b"commit":
            raise ValueError("receipt head is not a commit")
        git("merge-base", "--is-ancestor", B1, head)
        source = git("show", f"{head}:tiny.py")
        readme_unchanged = git("show", f"{B1}:README.md") == git("show", f"{head}:README.md")
        changed = git("diff", "--no-ext-diff", "--name-only", B1, head).decode().splitlines()
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print(json.dumps({"judgment": "INDETERMINATE", "error": str(error)}))
        return 2
    observations = check_source(args.task, source)
    observations.extend([
        {"id": "README-unchanged", "pass": readme_unchanged},
        {"id": "file-scope", "pass": all(
            path == "tiny.py" or re.fullmatch(r"tests/test_[^/]+\.py", path) for path in changed
        )},
    ])
    ok = all(row["pass"] for row in observations)
    print(json.dumps({"task": args.task, "baseRevision": B1, "headRevision": head,
                      "source_sha256": hashlib.sha256(source).hexdigest(),
                      "automated_checks": "PASS" if ok else "FAIL",
                      "independent_review": "REQUIRED", "observations": observations}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
