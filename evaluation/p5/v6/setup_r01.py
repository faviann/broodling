#!/usr/bin/env python3
"""Prepare v6 R01 inputs without admission, Attempt provisioning or execution.

Default operation is offline. --github-issue separately reads an existing private
disposable fixture and records unadmitted product inputs. It uses the v6
disposable-repository description marker. Neither
mode mutates GitHub, connects to a DirectTarget or gateway, nor opens a PR.
"""

from __future__ import annotations

import argparse
import fcntl
import hashlib
import json
from pathlib import Path
import re
import sqlite3
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[3]
PROTOCOL = "p5-native-pr-v6"
FREEZE = "4a2b0b8a0340b747e805f51da218a77ad81278f9"
GATEWAY_BASE_URL = "https://cliproxy.local.faviann.com/v1"
DESCRIPTION = "Disposable Broodling P5 v6 evaluation fixture (issue #83)"
TASKS = ("T1", "T2", "T3", "T4", "T4", "T3", "T2", "T1")


def execution_profile(direct_target_origin: str | None) -> dict:
    if direct_target_origin is not None:
        origin = urlsplit(direct_target_origin)
        if (origin.scheme not in ("http", "https") or not origin.hostname
                or origin.username is not None or origin.password is not None
                or origin.query or origin.fragment or origin.path not in ("", "/")):
            raise ValueError("DirectTarget origin must be a credential-free HTTP(S) origin")
        if origin.hostname == urlsplit(GATEWAY_BASE_URL).hostname:
            raise ValueError("DirectTarget origin must be separate from the gateway endpoint")
    return {
        "protocol": PROTOCOL,
        "freeze_commit": FREEZE,
        "zeroshot_version": "10.3.0",
        "zeroshot_sdk_version": "10.3.0.post1",
        "template": "software-change",
        "delivery": "pull_request",
        "target_kind": "DirectTarget",
        "direct_target_origin": direct_target_origin,
        "gateway_base_url": GATEWAY_BASE_URL,
        "required_dispatch_environment": ["GATEWAY_BASE_URL", "GATEWAY_API_KEY", "GH_TOKEN"],
        "uniform_runtime": {
            "harness": "codex", "provider": "gateway", "model": "gpt-5.6-sol",
            "effort": "medium", "size": "small", "session_scope": "execution",
        },
    }


def slots() -> dict:
    return {
        "protocol": PROTOCOL,
        "freeze_commit": FREEZE,
        "counts": {"P": 8, "S": 0, "D": 0, "U": 0, "A": 0, "J_A": 0},
        "slots": [{
            "slot": f"R{i:02}", "task": task,
            "repetition": 1 if i <= 4 else 2, "expected": "CO",
            "status": "NOT_STARTED", "primary_class": None,
            "native_result": None, "broodling_disposition": None,
            "independent_judgment": None,
            "reason": "Awaiting v6 prerequisites and separately authorized first admission",
        } for i, task in enumerate(TASKS, 1)],
    }


def _overlap(left: Path, right: Path) -> bool:
    return left == right or left in right.parents or right in left.parents


def github_inputs(repository: str, issue_number: int, source: Path,
                  evidence: Path, body: str) -> tuple[dict, dict]:
    """Read back an already-provisioned fixture; never create remote material."""
    from evaluation.p5.v3 import setup_r01 as setup

    remote = setup.api(f"repos/{repository}")
    if (not remote["private"] or remote["description"] != DESCRIPTION
            or remote["full_name"].lower() != repository.lower()):
        raise RuntimeError("repository is not the designated private disposable fixture")
    ref = setup.api(f"repos/{repository}/git/ref/heads/{setup.BRANCH}")
    if ref["object"]["sha"] != setup.B1:
        raise RuntimeError("remote target branch is not frozen B1; refusing reset")
    readback = setup.api(f"repos/{repository}/issues/{issue_number}")
    expected_url = f"https://github.com/{repository}/issues/{issue_number}"
    if (readback["title"] != "P5 v1 / T1 / R01" or readback["body"] != body
            or readback["state"] != "open" or "pull_request" in readback
            or readback["html_url"].lower() != expected_url.lower()):
        raise RuntimeError("GitHub issue differs from the designated open frozen R01 task")
    repo_record = {key: remote[key] for key in ("id", "node_id", "full_name", "html_url", "private")}
    repo_record.update(target_branch=setup.BRANCH, target_branch_oid=setup.B1)
    issue_path = evidence / "R01/issue-readback.json"
    issue = {
        "number": issue_number, "title": readback["title"], "body": readback["body"],
        "url": readback["html_url"], "id": readback["node_id"],
        "retrieved_at": setup.load(issue_path)["retrieved_at"] if issue_path.exists() else setup.now(),
    }
    setup.record(evidence / "github-repository.json", repo_record)
    setup.record(issue_path, issue)
    expected_origin = f"https://github.com/{repository}.git"
    if "origin" in setup.git("remote", cwd=source).splitlines():
        if setup.git("remote", "get-url", "origin", cwd=source) != expected_origin:
            raise RuntimeError("existing source origin differs from designated repository")
    else:
        setup.git("remote", "add", "origin", expected_origin, cwd=source)
    return repo_record, issue


def prepare(*, state_dir: Path, evidence: Path, repository: str,
            direct_target_origin: str | None = None,
            github_issue: int | None = None) -> dict:
    """Write immutable preparation records; a separate driver owns admission."""
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("repository must be OWNER/REPO")
    if github_issue is not None and github_issue < 1:
        raise ValueError("GitHub issue number must be positive")
    profile = execution_profile(direct_target_origin)

    # Lazy reuse keeps importing this module inert. These tracked v3 primitives
    # reconstruct the unchanged v1 corpus/B1 and create only unadmitted inputs.
    from evaluation.p5.v3 import setup_r01 as setup

    state = setup.assert_durable_workspace_root(state_dir)
    evidence = setup.assert_durable_workspace_root(evidence)
    run_root = ROOT / "evaluation/p5/runs"
    if (_overlap(state, evidence) or _overlap(state, ROOT)
            or (_overlap(evidence, ROOT) and evidence.parent != run_root)):
        raise ValueError("state must be outside the checkout; evidence must be disjoint and fresh")
    if any((state / name).is_symlink() for name in ("source", "workspaces", "broodling.sqlite3")):
        raise ValueError("setup source, workspaces and store must stay inside the selected state directory")
    for directory, marker in ((state, "setup-v6.json"), (evidence, "slots.json")):
        if directory.exists() and any(directory.iterdir()) and not (directory / marker).is_file():
            raise RuntimeError("existing directory has no v6 setup identity; choose a fresh directory")

    state.mkdir(parents=True, exist_ok=True)
    evidence.mkdir(parents=True, exist_ok=True)
    with (state / ".setup.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        # Verify the whole allocation, including zero counts and empty outcome
        # fields; a rerun may never erase evidence that first admission started.
        setup.record(evidence / "slots.json", slots())
        store = state / "broodling.sqlite3"
        if store.exists():
            with sqlite3.connect(f"{store.as_uri()}?mode=ro", uri=True) as database:
                for table in ("admission_decisions", "attempts"):
                    if database.execute(f"SELECT 1 FROM {table} LIMIT 1").fetchone():
                        raise RuntimeError("product admission has started; setup cannot continue")
        binding = {
            "protocol": PROTOCOL, "freeze_commit": FREEZE,
            "repository": repository, "evidence_directory": str(evidence),
            "state_directory": str(state), "execution_profile": profile,
        }
        setup.record(state / "setup-v6.json", binding)
        setup.record(evidence / "execution-profile.json", profile)
        scope, behavior, delivery, body = setup.task_material()
        setup.immutable(evidence / "R01/issue-body.md", body.encode())
        facts = setup.fixture(state / "source")
        (state / "workspaces").mkdir(exist_ok=True)
        facts.update(
            protocol=PROTOCOL, freeze_commit=FREEZE,
            corpus_freeze_commit=setup.CORPUS_FREEZE,
            repository=repository, target_branch=setup.BRANCH,
            store_path=str(store), workspace_root=str(state / "workspaces"),
            evidence_directory=str(evidence), admission_started=False,
            issue_body_sha256=hashlib.sha256(body.encode()).hexdigest(),
            protocol_sha256=hashlib.sha256(
                setup.frozen("evaluation/p5/v6/protocol.md", FREEZE)
            ).hexdigest(),
        )
        setup.record(evidence / "setup.json", facts)
        if github_issue is not None:
            remote, issue = github_inputs(repository, github_issue, state / "source", evidence, body)
            facts["records"] = setup.product_records(
                state, evidence, remote, issue, scope, behavior, delivery
            )
        return facts


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--state-dir", type=Path, required=True)
    parser.add_argument("--repository", required=True, help="designated disposable OWNER/REPO")
    parser.add_argument("--direct-target-origin", help="optional nonsecret Zeroshot HTTP(S) origin")
    parser.add_argument("--github-issue", type=int,
                        help="read existing private fixture/issue, then record unadmitted inputs")
    args = parser.parse_args()
    facts = prepare(state_dir=args.state_dir, evidence=args.evidence,
                    repository=args.repository, direct_target_origin=args.direct_target_origin,
                    github_issue=args.github_issue)
    print(json.dumps(facts, indent=2, sort_keys=True))


if __name__ == "__main__":
    import sys

    sys.path.insert(0, str(ROOT))
    main()
