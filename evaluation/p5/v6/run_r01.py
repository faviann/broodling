#!/usr/bin/env python3
"""Issue #83's single v6 R01 boundary; check is read-only.

Only ``start --authorize-r01`` may admit/provision/submit. That flag represents
separate live authorization with active operator supervision. A durable start
marker is committed before Contract admission. Start never recovers or replaces
a started Attempt; finalize/stop reconnect from its persisted origin without
dispatch credentials. Independent exact-revision judgment remains a later step.
"""

from __future__ import annotations

import argparse
import asyncio
from dataclasses import asdict
from datetime import UTC, datetime
import fcntl
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))

from broodling import (  # noqa: E402
    AbandonmentCoordinator,
    AttemptProvisioner,
    BroodlingStore,
    SubmissionCoordinator,
    WorkUnitDispositionCoordinator,
    ZeroshotSubmitter,
)
from evaluation.p5.verify_direct_target_gh import IncompatibleGitHubCLI, verify as verify_gh  # noqa: E402

PROTOCOL = "p5-native-pr-v6"
FREEZE = "4a2b0b8a0340b747e805f51da218a77ad81278f9"
BASELINE = "da5db3167eda0ef458cf0875b5a1c64138637b9d"
B1 = "884bd64264df1515bee76a63f548db9cabe25a35"
REPOSITORY = "faviann/broodling-p5-v6-20260919"
BRANCH = "p5-eval"
TARGET = "http://127.0.0.1:18768"
IMAGE = "sha256:3511d1b7134167b6a845bdc0536532a2265cba580e77b475e26d25d0382f4e5a"
GH_VERSION = "2.101.0"
GATEWAY = "https://cliproxy.local.faviann.com/v1"
SETUP = ROOT / "evaluation/p5/runs/2026-09-19-v6-setup"
EVIDENCE = ROOT / "evaluation/p5/runs/2026-09-19-v6-r01"
STATE = Path("/home/faviann/.local/share/broodling-p5-v6/r01-inputs")
RUNTIME = {
    "harness": "codex", "provider": "gateway", "model": "gpt-5.6-sol",
    "effort": "medium", "size": "small", "session_scope": "execution",
}
CREDENTIALS = ("GATEWAY_BASE_URL", "GATEWAY_API_KEY", "GH_TOKEN")
LEGACY = ("OPENAI_API_KEY", "OPENAI_BASE_URL", "GITHUB_TOKEN",
          "ANTHROPIC_API_KEY", "GEMINI_API_KEY", "GOOGLE_API_KEY")
EMPTY_TABLES = (
    "admission_decisions", "attempts", "worktree_assignments",
    "attempt_submissions", "final_assurance", "work_unit_dispositions",
    "attempt_abandonments", "attempt_retirements", "attempt_retries",
)
TESTED_PATHS = (
    "broodling", "tests", "pyproject.toml", "conftest.py",
    "evaluation/p5/v6/setup_r01.py", "evaluation/p5/v6/run_r01.py",
    "evaluation/p5/verify_direct_target_gh.py",
)


class Refusal(RuntimeError):
    """A fixed, credential-free refusal safe to show to the operator."""


def now() -> str:
    return datetime.now(UTC).isoformat()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Refusal(message)


def load(path: Path) -> dict:
    return json.loads(path.read_bytes())


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path: Path, value: object, *, replace: bool = False) -> None:
    """Durable evidence, with exclusive creation for irreversible boundaries."""
    data = (json.dumps(value, indent=2, sort_keys=True, default=str) + "\n").encode()
    target = path.with_name(f".{path.name}.{os.getpid()}.tmp") if replace else path
    with target.open("xb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    if replace:
        os.replace(target, path)
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def command(*arguments: str, cwd: Path = ROOT) -> str:
    result = subprocess.run(
        arguments, cwd=cwd, capture_output=True, text=True, timeout=30,
        env=dict(os.environ, GH_PROMPT_DISABLED="1", GIT_TERMINAL_PROMPT="0"),
    )
    # Never persist subprocess output on error: delivery/provider errors may
    # contain credentials. Only predetermined messages reach our error record.
    require(result.returncode == 0, f"{arguments[0]} read-only check failed")
    return result.stdout.strip()


def github(path: str) -> object:
    return json.loads(command("gh", "api", path))


def inputs() -> dict:
    records = load(SETUP / "R01/input-records.json")
    require(load(EVIDENCE / "R01/input-records.json") == records,
            "live inputs differ from immutable #83 setup")
    require(records["store_path"] == str(STATE / "broodling.sqlite3")
            and records["source_repository"] == str(STATE / "source")
            and records["workspace_root"] == str(STATE / "workspaces")
            and records["b1_commit_oid"] == B1,
            "prepared store/source/workspace/B1 identity changed")
    for name in ("broodling.sqlite3", "source", "workspaces"):
        require((STATE / name).resolve() == STATE / name,
                "prepared input path is no longer canonical")
    return records


def readonly_store(records: dict) -> sqlite3.Connection:
    connection = sqlite3.connect(
        Path(records["store_path"]).as_uri() + "?mode=ro", uri=True,
    )
    connection.row_factory = sqlite3.Row
    return connection


def check_store(records: dict) -> None:
    with readonly_store(records) as database:
        for table in EMPTY_TABLES:
            require(database.execute(f"SELECT count(*) FROM {table}").fetchone()[0] == 0,
                    f"R01 is not pristine: {table} exists; no rerun permitted")
        row = database.execute(
            "SELECT * FROM contract_revisions WHERE contract_revision_id = ?",
            (records["contract_revision_id"],),
        ).fetchone()
        require(row is not None and row["contract_sha256"] == records["contract_sha256"],
                "Contract identity changed")
        require(row["canonical_bytes"] == (SETUP / "R01/contract.json").read_bytes(),
                "Contract differs from retained exact authority")
        unit = database.execute("SELECT * FROM work_units WHERE work_unit_id = ?",
                                (records["work_unit_id"],)).fetchone()
        require(unit is not None and unit["owner"] + "/" + unit["repository"] == REPOSITORY
                and unit["issue_number"] == 1, "Work Unit authority changed")
        source = database.execute("SELECT * FROM entitled_sources WHERE source_id = ?",
                                  (records["source_id"],)).fetchone()
        require(source is not None and hashlib.sha256(source["content"]).hexdigest()
                == records["source_content_sha256"], "entitled source bytes changed")


async def target_check() -> None:
    from zeroshot import Client, DirectTarget

    async with Client(target=DirectTarget(TARGET), environment={}) as client:
        require(not await client.list_runs(), "DirectTarget inventory is not empty")
        await client.get_preset("software-change", delivery="pull_request")


def preflight() -> dict:
    """No admission, store writes, task run, GitHub mutation or evidence write."""
    ready = load(EVIDENCE / "pre-admission-verification.json")
    require(ready.get("ready_for_separate_live_authorization") is True
            and ready.get("issue") == 83 and ready.get("protocol") == PROTOCOL
            and ready.get("freeze_commit") == FREEZE
            and ready.get("baseline_commit") == BASELINE,
            "reviewed #83 prerequisite package is missing or not ready")
    for line in (SETUP / "SHA256SUMS").read_text().splitlines():
        expected, relative = line.split(maxsplit=1)
        path = (SETUP / relative).resolve()
        require(path.is_relative_to(SETUP) and sha(path) == expected,
                "preserved #83 setup package failed its hash manifest")
    require(not (EVIDENCE / "R01/execution-start.json").exists(),
            "R01 start was already recorded; no rerun permitted")
    require(load(EVIDENCE / "slots.json") == load(SETUP / "slots.json"),
            "live allocation differs from preserved zero-start allocation")
    allocation = load(EVIDENCE / "slots.json")
    require(allocation["counts"] == {"P": 8, "S": 0, "D": 0, "U": 0, "A": 0, "J_A": 0}
            and len(allocation["slots"]) == 8
            and all(slot["status"] == "NOT_STARTED" for slot in allocation["slots"]),
            "one or more frozen slots has started")
    target = load(EVIDENCE / "target-preparation.json")
    target_gh = verify_gh("docker", "exec", target["container_id"])
    require(target_gh["gh_version"].split()[2] == GH_VERSION,
            "actual target GitHub CLI differs from the selected pinned image")
    require(not any(name in os.environ for name in LEGACY),
            "conflicting legacy credentials/environment are present")
    require(os.environ.get("GATEWAY_BASE_URL") == GATEWAY,
            "GATEWAY_BASE_URL differs from corrected v6 endpoint")
    for name in ("GATEWAY_API_KEY", "GH_TOKEN"):
        require(bool(os.environ.get(name, "").strip()) and len(os.environ[name]) <= 4096,
                f"current nonempty {name} is required")
    require(not os.environ.get("ZEROSHOT_PYTHON_NATIVE_BINARY"),
            "native binary override is unsupported")
    command("git", "diff", "--exit-code", BASELINE, "--",
            "broodling", "pyproject.toml", "conftest.py")
    baseline = load(SETUP / "baseline-verification.json")
    tested = baseline["compatible_tested_commit"]
    require(len(tested) == 40 and all(character in "0123456789abcdef" for character in tested),
            "compatible tested revision must be an exact commit")
    require(baseline["tested_path_identities"] == {
        path: command("git", "rev-parse", f"{tested}:{path}") for path in TESTED_PATHS
    }, "tested product/dependency/test/helper identities differ from recorded revision")
    command("git", "diff", "--exit-code", tested, "--", *TESTED_PATHS)
    require(not command("git", "ls-files", "--others", "--exclude-standard", "--", *TESTED_PATHS),
            "untracked product/test/helper/config files need compatible validation")
    require(baseline["tests"]["exit_code"] == 0 and baseline["tests"]["passed"] >= 356
            and baseline["tests"]["failed"] == 0 and baseline["tests"]["skipped"] == 0,
            "supported full-suite baseline is missing")
    require(sha(ROOT / "evaluation/p5/v6/protocol.md") == baseline["protocol_sha256"],
            "corrected frozen protocol bytes changed")
    dependency = load(SETUP / "dependency-verification.json")["dependency"]
    require(importlib.metadata.version("the-open-engine-zeroshot") == "10.3.0.post1",
            "unsupported SDK version")
    require(sha(Path(dependency["native_binary"])) == dependency["native_binary_sha256"],
            "native binary differs from supported baseline")
    wheel = Path(dependency["cached_wheel"])
    require(sha(wheel) == dependency["expected_wheel_sha256"], "pinned wheel bytes changed")
    package = Path(dependency["native_binary"]).parents[1]
    import zeroshot

    require(Path(zeroshot.__file__).resolve().parent == package,
            "imported SDK differs from the verified package location")
    with zipfile.ZipFile(wheel) as archive:
        for name in archive.namelist():
            if "/purelib/zeroshot/" in name and not name.endswith("/"):
                relative = name.split("/purelib/zeroshot/", 1)[1]
                require((package / relative).read_bytes() == archive.read(name),
                        "installed SDK package differs from pinned wheel")
    require(ZeroshotSubmitter(STATE / "zeroshot-runtime").runtime_for("pull_request") == RUNTIME,
            "product-selected runtime differs from v6")
    records = inputs()
    check_store(records)
    source = Path(records["source_repository"])
    from broodling.starting_state import resolve_starting_state
    from broodling.workspace import assert_durable_workspace_root

    resolve_starting_state(source, B1)
    assert_durable_workspace_root(Path(records["workspace_root"]))
    require(command("git", "rev-parse", "HEAD", cwd=source) == B1
            and not command("git", "status", "--porcelain", "--untracked-files=all", cwd=source)
            and command("git", "ls-tree", "--name-only", "HEAD", cwd=source).splitlines()
            == ["README.md", "tiny.py"], "local fixture is no longer exact two-file B1")
    require(command("git", "remote", "get-url", "origin", cwd=source)
            == f"https://github.com/{REPOSITORY}.git", "source origin differs from authority")
    remote = github(f"repos/{REPOSITORY}")
    user_headers = command("gh", "api", "--include", "user")
    scopes = next((line.partition(":")[2].strip() for line in user_headers.splitlines()
                   if line.lower().startswith("x-oauth-scopes:")), "")
    require("repo" in {scope.strip() for scope in scopes.split(",")},
            "current GitHub delivery credential lacks previously verified repo scope")
    retained = load(SETUP / "github-repository.json")
    require(remote["id"] == retained["id"] and remote["private"]
            and remote["full_name"] == REPOSITORY and remote["permissions"]["push"],
            "current GitHub credential lacks exact private repository push authority")
    require(github(f"repos/{REPOSITORY}/git/ref/heads/{BRANCH}")["object"]["sha"] == B1,
            "authorized target branch moved from frozen B1")
    require(not github(f"repos/{REPOSITORY}/branches/{BRANCH}")["protected"],
            "target branch protection changed; recheck exact delivery authority")
    require(not github(f"repos/{REPOSITORY}/pulls?state=all&per_page=1"),
            "disposable repository already has a PR")
    issue = github(f"repos/{REPOSITORY}/issues/1")
    frozen_issue = load(SETUP / "R01/issue-readback.json")
    require(issue["state"] == "open" and "pull_request" not in issue
            and issue["node_id"] == frozen_issue["id"]
            and issue["body"] == frozen_issue["body"] and issue["title"] == frozen_issue["title"],
            "designated open issue differs from frozen R01 text")
    actual = json.loads(command("docker", "inspect", target["container_id"]))[0]
    require(actual["State"]["Running"] and actual["Image"] == IMAGE,
            "prepared DirectTarget container/image is no longer running")
    require(command("docker", "exec", target["container_id"], "zeroshot", "--version")
            == "zeroshot 10.3.0", "running target native version changed")
    require(command("docker", "exec", target["container_id"], "codex", "--version")
            == "codex-cli 0.153.4", "running target Codex version changed")
    with urllib.request.urlopen(TARGET + "/.well-known/zeroshot-native-v2", timeout=5) as response:
        discovery = json.load(response)
    require(discovery.get("kind") == "zeroshot.native-v2-target/v2", "incompatible target discovery")
    asyncio.run(target_check())
    metadata_request = urllib.request.Request(
        GATEWAY + "/models", headers={"Authorization": "Bearer " + os.environ["GATEWAY_API_KEY"]},
    )
    with urllib.request.urlopen(metadata_request, timeout=15) as response:
        models = json.load(response)
        require(response.status == 200 and any(model.get("id") == "gpt-5.6-sol"
                for model in models.get("data", [])), "gateway credential/model preflight failed")
    return {
        "observed_at": now(), "issue": 83, "protocol": PROTOCOL,
        "freeze_commit": FREEZE, "baseline_commit": BASELINE,
        "compatible_tested_commit": tested,
        "driver_sha256": sha(Path(__file__)),
        "reviewed_preparation_sha256": sha(EVIDENCE / "pre-admission-verification.json"),
        "input_records_sha256": sha(SETUP / "R01/input-records.json"),
        "direct_target_origin": TARGET, "runtime": RUNTIME,
        "direct_target_github_cli": target_gh,
        "repository": REPOSITORY, "target_branch": BRANCH, "B1": B1,
        "target_run_count": 0, "credentials_present": list(CREDENTIALS),
        "gateway_metadata_status": 200, "github_repo_scope_verified": True,
        "credential_values_retained": False, "admission_started": False,
    }


def update_counts(store: BroodlingStore, attempt_id: str) -> None:
    path = EVIDENCE / "slots.json"
    slots = load(path)
    submitter = ZeroshotSubmitter(STATE / "credential-free-reconnect")
    submission = SubmissionCoordinator(store, submitter).record(attempt_id)
    disposition = WorkUnitDispositionCoordinator(store, submitter).record(attempt_id)
    slots["counts"]["D"] = int(submission is not None and submission.state in {"dispatched", "correlated", "blocked"})
    slots["counts"]["U"] = int(submission is not None and bool(submission.run_id))
    slots["counts"]["A"] = int(disposition is not None and disposition.outcome == "SUCCEEDED")
    write(path, slots, replace=True)


def start() -> None:
    # The same lock as setup prevents a concurrent setup verification from
    # racing admission. File creation alone does not consume the slot.
    with (STATE / ".setup.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        facts = preflight()
        records = inputs()
        write(EVIDENCE / "launch-preflight.json", facts, replace=True)
        with BroodlingStore.open(records["store_path"]) as store:
            check_store(records)
            slots = load(EVIDENCE / "slots.json")
            write(EVIDENCE / "R01/execution-start.json", {
                "started_at": now(), "slot": "R01", "issue": 83,
                "caller_pid": os.getpid(),
                "protocol": PROTOCOL, "freeze_commit": FREEZE,
                "driver_sha256": facts["driver_sha256"],
                "counting_boundary": "recorded immediately before first Contract admission attempt",
                "authority": "separate --authorize-r01 action with active operator supervision",
                "rerun_permitted": False,
            })
            slots["counts"]["S"] = 1
            slots["slots"][0].update(status="STARTED", reason="First admission boundary recorded; no replacement or rerun.")
            write(EVIDENCE / "slots.json", slots, replace=True)
            decision = store.admit(records["contract_revision_id"])
            write(EVIDENCE / "R01/admission-decision.json", asdict(decision))
            require(decision.admitted, "R01 Contract admission rejected; started slot retained")
            provisioned = AttemptProvisioner(store, records["workspace_root"]).admit_and_provision(
                records["contract_revision_id"], records["source_repository"], B1,
            )
            write(EVIDENCE / "R01/attempt.json", {
                "attempt": asdict(provisioned.attempt), "assignment": asdict(provisioned.assignment),
                "quarantine": "Dispatched Attempt must be retained; no cleanup or replacement.",
            })
            submitter = ZeroshotSubmitter(
                STATE / "zeroshot-runtime", delivery_target_origin=TARGET,
                github_token=os.environ["GH_TOKEN"], gateway_base_url=os.environ["GATEWAY_BASE_URL"],
                gateway_api_key=os.environ["GATEWAY_API_KEY"],
            )
            coordinator = SubmissionCoordinator(store, submitter)
            try:
                prepared = coordinator.prepare(provisioned.attempt.attempt_id)
                require(not any(os.environ[name] in prepared.request_json for name in ("GH_TOKEN", "GATEWAY_API_KEY")),
                        "credential unexpectedly entered invocation")
                request = json.loads(prepared.request_json)
                require(request["runtime"] == RUNTIME and request["delivery"] == {
                    "repository": REPOSITORY, "targetBranch": BRANCH, "baseRevision": B1,
                }, "persisted invocation differs from exact v6 authority")
                write(EVIDENCE / "R01/invocation.json", request)
                submission = coordinator.reconcile(provisioned.attempt.attempt_id)
                write(EVIDENCE / "R01/correlation.json", {
                    "attempt_id": submission.attempt_id, "run_id": submission.run_id,
                    "submission_key": submission.submission_key, "state": submission.state,
                    "request_sha256": hashlib.sha256(submission.request_json.encode()).hexdigest(),
                    "observed_at": now(), "caller_detaches_without_wait": True,
                })
            finally:
                update_counts(store, provisioned.attempt.attempt_id)
    print(json.dumps({"slot": "R01", "state": submission.state, "run_id": submission.run_id}))


async def completed_observation(request: dict, run_id: str) -> dict:
    """Observe retained completion before the detached caller consumes it."""
    from zeroshot import Client, DirectTarget

    async with Client(target=DirectTarget(request["target"]["deliveryTargetOrigin"]),
                      environment={}) as client:
        status = await client.get_run(run_id).status()
    require(status.run_id == run_id and status.phase == "finished" and status.result is not None,
            "finalize requires an already-completed native result; leave the run detached")
    return {"observed_at": now(), "run_id": run_id, "phase": status.phase,
            "cursor": status.cursor, "result_already_present": True,
            "credential_free": True}


def reopened_replay(records: dict, attempt_id: str, expected) -> None:
    """Prove durable replay after closing the first disposition's store."""
    class NoExecution(ZeroshotSubmitter):
        async def wait(self, request: dict, run_id: str):
            raise Refusal("reopened disposition replay unexpectedly tried to wait")

        def submit(self, request: dict):
            raise Refusal("reopened disposition replay unexpectedly tried to submit")

    with BroodlingStore.open(records["store_path"]) as reopened:
        coordinator = WorkUnitDispositionCoordinator(
            reopened, NoExecution(STATE / "credential-free-reconnect"),
        )
        replayed = asyncio.run(coordinator.finalize(attempt_id))
        require(replayed == expected, "reopened store returned a different retained disposition")
        require(coordinator.justification(attempt_id) == expected.result,
                "reopened store returned a different retained result")
    path = EVIDENCE / "R01/reopened-store-replay.json"
    if not path.exists():
        write(path, {"observed_at": now(), "attempt_id": attempt_id,
                     "identical_disposition": True, "identical_retained_result": True,
                     "native_waits": 0, "submissions": 0, "credential_free": True,
                     "disposition": asdict(replayed)})


def reconnect(phase: str) -> None:
    require(not any(name in os.environ for name in CREDENTIALS + LEGACY),
            "finalize/stop require an environment without dispatch credentials")
    require((EVIDENCE / "R01/execution-start.json").is_file(), "R01 has not started")
    records = inputs()
    # Resolve the sole Attempt from the original durable store even if a crash
    # prevented correlation.json from being written. Never submit or replay here.
    with readonly_store(records) as database:
        rows = database.execute("SELECT attempt_id FROM attempts").fetchall()
    require(len(rows) == 1, "R01 lacks exactly one retained Attempt; manual evidence review required")
    attempt_id = rows[0]["attempt_id"]
    submitter = ZeroshotSubmitter(STATE / "credential-free-reconnect")
    with BroodlingStore.open(records["store_path"]) as store:
        if phase == "stop":
            try:
                asyncio.run(AbandonmentCoordinator(store, submitter).stop(
                    attempt_id, "Operator stopped the single v6 R01 trial; retain quarantine.",
                ))
            finally:
                update_counts(store, attempt_id)
            return
        submitted = SubmissionCoordinator(store, submitter).record(attempt_id)
        require(submitted is not None and submitted.state == "correlated" and bool(submitted.run_id),
                "no durable correlation; never rerun start to recover it")
        request = json.loads(submitted.request_json)
        require(request["target"]["deliveryTargetOrigin"] == TARGET,
                "persisted DirectTarget differs from prepared origin")
        require(WorkUnitDispositionCoordinator(store, submitter).record(attempt_id) is None,
                "first disposition already exists; use replay to verify retained state")
        # Reserve a caller-reattachment record before contacting the target.
        # Exclusive creation limits concurrent/repeated finalize calls to two.
        for number in (1, 2):
            try:
                write(EVIDENCE / "R01" / f"reattachment-{number}.json", {
                    "observed_at": now(), "attempt_id": attempt_id,
                    "run_id": submitted.run_id, "number": number,
                    "credential_free": True, "caller_pid": os.getpid(),
                })
                break
            except FileExistsError:
                continue
        else:
            raise Refusal("two caller reattachments already recorded; retain partial evidence")
        completed = asyncio.run(completed_observation(request, submitted.run_id))
        result = asyncio.run(submitter.wait(request, submitted.run_id))
        require(result.run_id == submitted.run_id, "native result belongs to another run")
        require(WorkUnitDispositionCoordinator(store, submitter).record(attempt_id) is None,
                "disposition appeared before detached completed-result consumption")
        completed_path = EVIDENCE / "R01/detached-completed-result.json"
        if not completed_path.exists():
            write(completed_path, {**completed, "consumed_at": now(),
                                  "attempt_id": attempt_id,
                                  "first_disposition_absent": True,
                                  "start_caller_pid": load(EVIDENCE / "R01/execution-start.json")["caller_pid"],
                                  "detached_caller_pid": os.getpid()})
        result_path = EVIDENCE / "R01/native-result.json"
        if not result_path.exists():
            write(result_path, {
                "run_id": result.run_id, "succeeded": result.succeeded,
                "output": result.output,
                "failure": None if result.failure is None else {
                    "type": type(result.failure).__name__,
                    "detail": "Raw provider error omitted to prevent credential retention.",
                },
                "observed_at": now(), "credential_free_reconnection": True,
            })
        try:
            class RetainedResult(ZeroshotSubmitter):
                async def wait(self, requested: dict, run_id: str):
                    require(requested == request and run_id == result.run_id,
                            "disposition requested a different retained run")
                    return result

            retained = RetainedResult(STATE / "credential-free-reconnect")
            disposition = asyncio.run(WorkUnitDispositionCoordinator(store, retained).finalize(attempt_id))
            for name, value in (("disposition.json", asdict(disposition)),
                                ("receipt.json", disposition.result["deliveryReceipt"])):
                path = EVIDENCE / "R01" / name
                if not path.exists():
                    write(path, value)
        except Exception:
            slots = load(EVIDENCE / "slots.json")
            slots["slots"][0].update(
                status="TERMINAL_AWAITING_CLASSIFICATION",
                native_result="SUCCEEDED" if result.succeeded else "FAILED",
                broodling_disposition=None,
                reason="Native result retained; no successful disposition. Classify under frozen rules.",
            )
            write(EVIDENCE / "slots.json", slots, replace=True)
            raise
        finally:
            update_counts(store, attempt_id)
        slots = load(EVIDENCE / "slots.json")
        slots["slots"][0].update(
            status="DELIVERED_AWAITING_JUDGMENT", native_result="SUCCEEDED",
            broodling_disposition="SUCCEEDED", reason="Independent exact-revision judgment remains required.",
        )
        write(EVIDENCE / "slots.json", slots, replace=True)
    reopened_replay(records, attempt_id, disposition)
    print(json.dumps({"slot": "R01", "state": "DELIVERED_AWAITING_JUDGMENT", "run_id": result.run_id}))


def replay() -> None:
    """Repeat only the retained disposition readback, never native execution."""
    require(not any(name in os.environ for name in CREDENTIALS + LEGACY),
            "replay requires an environment without dispatch credentials")
    records = inputs()
    with BroodlingStore.open(records["store_path"]) as store:
        rows = store.connection.execute("SELECT attempt_id FROM attempts").fetchall()
        require(len(rows) == 1, "R01 lacks exactly one retained Attempt")
        attempt_id = rows[0]["attempt_id"]
        expected = WorkUnitDispositionCoordinator(
            store, ZeroshotSubmitter(STATE / "credential-free-reconnect"),
        ).record(attempt_id)
        require(expected is not None, "R01 has no retained disposition to replay")
    reopened_replay(records, attempt_id, expected)
    print(json.dumps({"slot": "R01", "retained_disposition_replayed": True}))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("check", "start", "finalize", "replay", "stop"))
    parser.add_argument("--authorize-r01", action="store_true",
                        help="separate authorization for one R01 admission under active supervision")
    args = parser.parse_args()
    if args.phase == "start" and not args.authorize_r01:
        parser.error("start requires separate live authorization: --authorize-r01")
    if args.phase != "start" and args.authorize_r01:
        parser.error("--authorize-r01 is accepted only for start")
    try:
        if args.phase == "check":
            print(json.dumps(preflight(), indent=2, sort_keys=True))
        elif args.phase == "start":
            start()
        elif args.phase == "replay":
            replay()
        else:
            reconnect(args.phase)
    except Exception as error:
        # No exception text/traceback: third-party failures may contain secrets.
        record = {"phase": args.phase, "observed_at": now(), "error_type": type(error).__name__,
                  "message": str(error) if isinstance(error, (Refusal, IncompatibleGitHubCLI)) else
                  "Phase refused or failed; inspect sanitized prerequisites and durable state. Never rerun start after the start marker exists."}
        if isinstance(error, IncompatibleGitHubCLI):
            record["direct_target_github_cli"] = error.facts
        if args.phase != "check" and (EVIDENCE / "R01/execution-start.json").exists():
            write(EVIDENCE / "R01" / f"{args.phase}-error-{os.getpid()}.json", record)
        print(json.dumps(record), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
