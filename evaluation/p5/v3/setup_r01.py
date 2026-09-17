#!/usr/bin/env python3
"""Prepare the frozen R01 fixture and records; never admit or dispatch a trial.

Run with the supported Broodling Python environment from any directory. Remote
creation requires --provision-github. Existing material is verified, never reset.
The separate evaluation driver owns first-admission accounting and execution.
"""

from __future__ import annotations

import argparse
import fcntl
import hashlib
import json
import os
import re
import subprocess
import sys
from dataclasses import asdict
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))

from broodling import (  # noqa: E402
    BroodlingStore,
    Contract,
    Criterion,
    RequiredEffect,
    SourceAttribution,
    SourceSubmission,
    WorkReference,
)
from broodling.workspace import assert_durable_workspace_root  # noqa: E402

PROTOCOL = "p5-native-pr-v3"
FREEZE = "de1da18479fdf846d04994190132357fcb0a87ef"
CORPUS_FREEZE = "717e94b3548d1dc029bfb54e2758e9929f1d93bd"
B1 = "884bd64264df1515bee76a63f548db9cabe25a35"
TREE = "7be09d5ce802612670dff148ebd6b8ff2b5244f4"
BRANCH = "p5-eval"
DESCRIPTION = "Disposable Broodling P5 v3 evaluation fixture (issue #67)"
TASKS = ("T1", "T2", "T3", "T4", "T4", "T3", "T2", "T1")


def now() -> str:
    return datetime.now(UTC).isoformat()


def command(*argv: str, cwd: Path = ROOT, data: bytes | None = None,
            env: dict[str, str] | None = None, missing_ok: bool = False) -> bytes:
    environment = dict(os.environ, GH_PROMPT_DISABLED="1", GIT_TERMINAL_PROMPT="0")
    environment.update(env or {})
    result = subprocess.run(argv, cwd=cwd, env=environment, input=data,
                            capture_output=True, check=False)
    if result.returncode:
        if missing_ok and b"HTTP 404" in result.stderr:
            return b"null"
        if (missing_ok and argv[:2] == ("gh", "api")
                and "/git/ref/heads/" in argv[2]
                and b"Git Repository is empty. (HTTP 409)" in result.stderr):
            return b"null"
        # Deliberately exclude command output and environment from errors.
        raise RuntimeError(f"{argv[0]} {argv[1]} failed (exit {result.returncode})")
    return result.stdout


def git(*args: str, cwd: Path = ROOT, **kwargs) -> str:
    return command("git", *args, cwd=cwd, **kwargs).decode().strip()


def frozen(path: str, commit: str = CORPUS_FREEZE) -> bytes:
    return command("git", "show", f"{commit}:{path}")


def immutable(path: Path, content: bytes) -> None:
    """A rerun may verify evidence but cannot replace different prior evidence."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if path.read_bytes() != content:
            raise RuntimeError(f"existing evidence differs: {path}")
    else:
        with path.open("xb") as output:
            output.write(content)


def record(path: Path, value: object) -> None:
    immutable(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode())


def load(path: Path) -> dict:
    return json.loads(path.read_text())


def initialize_slots(evidence: Path) -> None:
    path = evidence / "slots.json"
    if path.exists():
        existing = load(path)
        if existing["protocol"] != PROTOCOL or existing["freeze_commit"] != FREEZE:
            raise RuntimeError("evidence directory belongs to another protocol")
        expected = [(f"R{i:02}", task) for i, task in enumerate(TASKS, 1)]
        if [(s["slot"], s["task"]) for s in existing["slots"]] != expected:
            raise RuntimeError("existing slot allocation differs from frozen order")
        if any(slot["status"] != "NOT_STARTED" for slot in existing["slots"]):
            raise RuntimeError("a trial has started; setup cannot amend its evidence")
        return
    record(path, {
        "protocol": PROTOCOL,
        "freeze_commit": FREEZE,
        "counts": {"P": 8, "S": 0, "D": 0, "U": 0, "A": 0, "J_A": 0},
        "slots": [{
            "slot": f"R{i:02}", "task": task,
            "repetition": 1 if i <= 4 else 2, "expected": "CO",
            "status": "NOT_STARTED", "primary_class": None,
            "native_result": None, "broodling_disposition": None,
            "independent_judgment": None,
            "reason": "Awaiting v3 prerequisites and first admission",
        } for i, task in enumerate(TASKS, 1)],
    })


def task_material() -> tuple[str, str, str, str]:
    corpus = frozen("evaluation/p5/v1/corpus.md").decode()

    def quote(heading: str) -> str:
        section = corpus.split(heading, 1)[1].split("\n### ", 1)[0]
        lines = re.findall(r"^> ?(.*)$", section, flags=re.MULTILINE)
        if not lines:
            raise RuntimeError(f"missing frozen corpus section: {heading}")
        return "\n".join(lines)

    scope = quote("### Common criteria")
    behavior = quote("### T1 — strict port parsing")
    delivery = quote("### Delivery requirement")
    body = "\n\n".join(("P5 v1 / T1 / R01", scope, behavior, delivery)) + "\n"
    return scope, behavior, delivery, body


def fixture(source: Path) -> dict:
    contents = {name: frozen(f"evaluation/p5/v1/fixture/{name}")
                for name in ("README.md", "tiny.py")}
    if not source.exists():
        source.mkdir(parents=True)
        for name, content in contents.items():
            immutable(source / name, content)
            (source / name).chmod(0o644)
        environment = {"GIT_CONFIG_NOSYSTEM": "1", "GIT_CONFIG_GLOBAL": "/dev/null"}
        git("init", "--template=", "--object-format=sha1", "-b", BRANCH,
            cwd=source, env=environment)
        git("-c", "core.autocrlf=false", "add", "--", "README.md", "tiny.py",
            cwd=source, env=environment)
        tree = git("write-tree", cwd=source, env=environment)
        environment.update({
            "GIT_AUTHOR_NAME": "P5 Fixture", "GIT_AUTHOR_EMAIL": "p5@example.invalid",
            "GIT_COMMITTER_NAME": "P5 Fixture", "GIT_COMMITTER_EMAIL": "p5@example.invalid",
            "GIT_AUTHOR_DATE": "2026-09-17T00:00:00+0000",
            "GIT_COMMITTER_DATE": "2026-09-17T00:00:00+0000",
        })
        commit = git("-c", "commit.gpgsign=false", "commit-tree", tree,
                     cwd=source, env=environment, data=b"P5 v1 fixture B1\n")
        if (commit, tree) != (B1, TREE):
            raise RuntimeError("reconstructed fixture does not match frozen B1")
        git("update-ref", f"refs/heads/{BRANCH}", commit, cwd=source, env=environment)
    if git("rev-parse", "HEAD", cwd=source) != B1:
        raise RuntimeError("existing source HEAD is not frozen B1; refusing reset")
    if git("rev-parse", "HEAD^{tree}", cwd=source) != TREE:
        raise RuntimeError("source tree differs from frozen B1")
    if git("status", "--porcelain", "--untracked-files=all", cwd=source):
        raise RuntimeError("source fixture is dirty; refusing overwrite")
    for name, content in contents.items():
        if (source / name).read_bytes() != content:
            raise RuntimeError(f"fixture bytes changed: {name}")
    return {"b1_commit_oid": B1, "b1_tree_oid": TREE, "source_repository": str(source),
            "files_sha256": {name: hashlib.sha256(content).hexdigest()
                             for name, content in contents.items()}}


def api(endpoint: str, *, missing_ok: bool = False) -> dict | None:
    return json.loads(command("gh", "api", endpoint, missing_ok=missing_ok))


def provision_github(repository: str, source: Path, evidence: Path, body: str) -> tuple[dict, dict]:
    expected_origin = f"https://github.com/{repository}.git"
    remotes = git("remote", cwd=source).splitlines()
    if "origin" in remotes:
        if git("remote", "get-url", "origin", cwd=source) != expected_origin:
            raise RuntimeError("existing source origin differs from designated repository")
    else:
        git("remote", "add", "origin", expected_origin, cwd=source)
    remote = api(f"repos/{repository}", missing_ok=True)
    if remote is None:
        command("gh", "repo", "create", repository, "--private", "--description", DESCRIPTION)
        remote = api(f"repos/{repository}")
    if not remote["private"] or remote["description"] != DESCRIPTION:
        raise RuntimeError("repository is not the designated private disposable fixture")
    if remote["full_name"].lower() != repository.lower():
        raise RuntimeError("GitHub repository identity changed")
    ref = api(f"repos/{repository}/git/ref/heads/{BRANCH}", missing_ok=True)
    if ref is None:
        # gh supplies authentication directly to Git; no token is read or retained.
        git("-c", "credential.helper=", "-c",
            "credential.https://github.com.helper=!gh auth git-credential",
            "push", "origin", f"{B1}:refs/heads/{BRANCH}",
            cwd=source)
        ref = api(f"repos/{repository}/git/ref/heads/{BRANCH}")
    if ref["object"]["sha"] != B1:
        raise RuntimeError("remote target branch advanced; refusing reset")
    repo_record = {key: remote[key] for key in ("id", "node_id", "full_name", "html_url", "private")}
    repo_record.update(target_branch=BRANCH, target_branch_oid=ref["object"]["sha"])
    record(evidence / "github-repository.json", repo_record)
    title = "P5 v1 / T1 / R01"
    issues = json.loads(command("gh", "issue", "list", "--repo", repository,
                               "--state", "all", "--limit", "1000",
                               "--json", "number,title,body,url,id"))
    matches = [issue for issue in issues if issue["title"] == title]
    if not matches:
        url = command("gh", "issue", "create", "--repo", repository,
                      "--title", title, "--body-file", str(evidence / "R01/issue-body.md")).decode().strip()
        issue = json.loads(command("gh", "issue", "view", url, "--json", "number,title,body,url,id"))
    elif len(matches) == 1:
        issue = matches[0]
    else:
        raise RuntimeError("multiple R01 issues exist; refusing ambiguous identity")
    if issue["body"] != body:
        raise RuntimeError("GitHub issue body differs from frozen task text")
    issue_path = evidence / "R01/issue-readback.json"
    retrieved_at = load(issue_path)["retrieved_at"] if issue_path.exists() else now()
    issue["retrieved_at"] = retrieved_at
    record(issue_path, issue)
    return repo_record, issue


def product_records(state: Path, evidence: Path, repository: dict, issue: dict,
                    scope: str, behavior: str, delivery: str) -> dict:
    with BroodlingStore.open(state / "broodling.sqlite3") as store:
        reference = WorkReference.parse(repository["full_name"], issue["url"],
                                        repository_identity=repository["node_id"],
                                        issue_identity=issue["id"])
        existing_path = evidence / "R01/input-records.json"
        if existing_path.exists():
            existing = load(existing_path)
            work = store.get_work_unit(existing["work_unit_id"])
            source = store.get_entitled_source(existing["source_id"])
            revision = store.get_contract_revision(existing["contract_revision_id"])
            if work.work_unit_id != reference.work_unit_id or source.content != issue["body"].encode():
                raise RuntimeError("recorded product inputs differ from current fixture")
            immutable(evidence / "R01/contract.json", revision.canonical_bytes)
            return existing
        work = store.resolve_work_unit(reference)
        source = store.entitle_source(work.work_unit_id, SourceSubmission(
            kind="primary_issue", locator=work.issue_locator, content=issue["body"].encode(),
            media_type="text/markdown; charset=utf-8", retrieved_at=issue["retrieved_at"],
        ))
        revision = store.record_contract_revision(Contract(
            work_unit_id=work.work_unit_id, constructed_by="caller",
            source_attribution=(SourceAttribution(source.source_id, source.content_sha256),),
            criteria=(Criterion("scope", scope), Criterion("behavior", behavior)),
            required_effects=(RequiredEffect("deliver-pr", delivery, "pull_request", BRANCH),),
            host_assumptions=("single_host", "one_attempt_one_dedicated_worktree"),
        ))
        source_record = asdict(source)
        source_record.pop("content")
        record(evidence / "R01/work-unit.json", asdict(work))
        record(evidence / "R01/source.json", source_record)
        immutable(evidence / "R01/contract.json", revision.canonical_bytes)
        result = {"work_unit_id": work.work_unit_id, "source_id": source.source_id,
                  "source_content_sha256": source.content_sha256,
                  "contract_revision_id": revision.contract_revision_id,
                  "contract_sha256": revision.contract_sha256,
                  "b1_commit_oid": B1, "store_path": str(store.path),
                  "source_repository": str(state / "source"),
                  "workspace_root": str(state / "workspaces"),
                  "admission_started": False}
        record(existing_path, result)
        return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--state-dir", type=Path, required=True)
    parser.add_argument("--repository", required=True, help="designated disposable OWNER/REPO")
    parser.add_argument("--provision-github", action="store_true",
                        help="create/verify private repository, B1 branch, issue and product records")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", args.repository):
        parser.error("--repository must be OWNER/REPO")
    state = assert_durable_workspace_root(args.state_dir.expanduser().resolve())
    evidence = assert_durable_workspace_root(args.evidence.expanduser().resolve())
    state.mkdir(parents=True, exist_ok=True)
    evidence.mkdir(parents=True, exist_ok=True)
    with (state / ".setup.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        initialize_slots(evidence)
        scope, behavior, delivery, body = task_material()
        immutable(evidence / "R01/issue-body.md", body.encode())
        facts = fixture(state / "source")
        (state / "workspaces").mkdir(exist_ok=True)
        facts.update(protocol=PROTOCOL, freeze_commit=FREEZE, corpus_freeze_commit=CORPUS_FREEZE,
                     repository=args.repository, target_branch=BRANCH,
                     store_path=str(state / "broodling.sqlite3"),
                     workspace_root=str(state / "workspaces"), evidence_directory=str(evidence),
                     issue_body_sha256=hashlib.sha256(body.encode()).hexdigest(),
                     protocol_sha256=hashlib.sha256(frozen("evaluation/p5/v3/protocol.md", FREEZE)).hexdigest())
        record(evidence / "setup.json", facts)
        if args.provision_github:
            repository, issue = provision_github(args.repository, state / "source", evidence, body)
            facts["records"] = product_records(state, evidence, repository, issue, scope, behavior, delivery)
        print(json.dumps(facts, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
