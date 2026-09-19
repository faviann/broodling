"""Supplemental full-criteria diagnostics; not a change to the frozen v1 judge.

Execute only in the retained credential-free, network-disabled judging sandbox.
These cases were requested during independent review of the exact receipt source.
"""
import hashlib
import json
import subprocess

HEAD = "248d67d35fc8fe6ac5dba9a0fb8cae831ae22631"
source = subprocess.check_output(
    ["git", "--no-replace-objects", "-C", "/repository", "show", f"{HEAD}:tiny.py"]
)
assert hashlib.sha256(source).hexdigest() == (
    "8baa4bad9f19f6a64f4fb515298a741297065bd96b3955aa129242048ec69582"
)
namespace = {"__name__": "p5_candidate"}
exec(compile(source, "receipt-head/tiny.py", "exec"), namespace)


class FalseyString(str):
    def __bool__(self):
        return False


class ZeroLengthString(str):
    def __len__(self):
        return 0


observations = []
for case_id, text, expected in (
    ("plain-80", "80", 80),
    ("long-leading-zeros", "0" * 10000 + "80", 80),
    ("falsey-string-80", FalseyString("80"), 80),
    ("zero-length-override-string-80", ZeroLengthString("80"), 80),
):
    row = {
        "id": case_id,
        "isinstance_str": isinstance(text, str),
        "underlying_length": str.__len__(text),
        "ascii": str.isascii(text),
        "digits": str.isdigit(text),
        "expected": expected,
    }
    try:
        actual = namespace["parse_port"](text)
        row.update(actual=repr(actual), pass_=type(actual) is int and actual == expected)
    except Exception as error:
        row.update(actual=repr(error), pass_=False)
    observations.append(row)
print(json.dumps({"headRevision": HEAD, "observations": observations}, indent=2))
