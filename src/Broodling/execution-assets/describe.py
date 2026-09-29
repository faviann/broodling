"""Print the reviewable structure of an execution asset ({graph, runtime}) as JSON.

Usage: describe.py ASSET_JSON

Reports what the approval decision requires of the stock PR asset: every executable graph node with
its worker and its complete runtime binding, node deadlines (graph `timeoutMs`), the review/repair
loop limit, and the delivery node's receipt (output) schema, signals and repair routing. It reads
the native values as generated; it does not normalize or validate them.
"""
import json
import sys


def walk(node):
    if isinstance(node, dict):
        yield node
        for value in node.values():
            yield from walk(value)
    elif isinstance(node, list):
        for value in node:
            yield from walk(value)


asset = json.load(open(sys.argv[1]))
graph = list(walk(asset["graph"]["root"]))
runtime = asset["runtime"]
executable = {node["name"]: node for node in graph if "worker" in node}
(delivery,) = [node for node in executable.values() if node["worker"].startswith("builtin.git-delivery.")]


def field_type(schema):
    return schema["values"] if schema["kind"] == "enum" else schema["kind"]


print(json.dumps({
    "runtime": {key: value for key, value in runtime.items() if key != "nodes"},
    "executableNodes": {
        name: {"kind": node["kind"], "worker": node["worker"], "runtime": runtime["nodes"].get(name)}
        for name, node in sorted(executable.items())
    },
    "runtimeOnlyNodes": sorted(set(runtime["nodes"]) - set(executable)),
    "nodeDeadlines": sorted(name for name, node in executable.items() if "timeoutMs" in node),
    "loops": {node["name"]: {"maxIterations": node.get("maxIterations")} for node in graph if node.get("kind") == "loop"},
    "delivery": {
        "node": delivery["name"],
        "worker": delivery["worker"],
        "receipt": {
            name: {"type": field_type(field["type"]), "required": field["required"]}
            for name, field in sorted(delivery["output"]["fields"].items())
        },
        "signals": delivery.get("signals"),
        # Native serializes the default `consider` policy by omitting the field.
        "pullRequestFeedback": runtime["nodes"][delivery["name"]].get("pullRequestFeedback", "consider"),
        "signalRoutes": {
            branch["node"]["name"]: branch["when"]["labels"]
            for node in graph for branch in node.get("branches", [])
            if branch.get("when", {}).get("kind") == "in"
            and branch["when"]["value"] == {"name": delivery["name"], "source": "signal", "field": "delivery"}
        },
    },
}, indent=2, sort_keys=True))
