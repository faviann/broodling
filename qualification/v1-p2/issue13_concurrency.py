#!/usr/bin/env python3
"""Reproducible concurrency evidence for issue #13, G2-V1 blocker 5.3.

Two questions, kept apart on purpose, because the G2-V1 gate found the existing
end-to-end witness could not tell them apart:

``--stages``
    What does ``git worktree add`` publish, and in what order? This is the
    mechanism. It writes the registration, then attaches the branch, then checks
    the tree out, so a concurrent reader can observe a worktree that is
    registered but detached, a directory that exists but is not yet registered,
    or a branch attached over a tree that is not yet B1.

``--race``
    Do N processes issuing the *semantically identical* admission request
    converge? Each round records what every caller saw and what the durable
    state became, and classifies any divergence. Ambiguity is the failure mode
    the gate named, so an inconclusive round is reported as a violation rather
    than passed over.

Run it against ``f42f00d`` to reproduce the blocker and against the remediation
commit to see it closed. See README.md for the exact invocations.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from collections import Counter
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
REPOSITORY_ROOT = HERE.parent.parent
sys.path.insert(0, str(REPOSITORY_ROOT))

from broodling import (  # noqa: E402
    BroodlingStore,
    Contract,
    Criterion,
    EvidencePopulation,
    SourceAttribution,
    SourceSubmission,
    WorkReference,
)
from broodling.workspace import NON_DURABLE_ROOTS  # noqa: E402

RACER = HERE / "issue13_racer.py"

REPOSITORY = "https://github.com/faviann/broodling"
ISSUE = 13
ISSUE_BODY = (
    b"Admit one immutable Attempt at B1 and assign it an exclusive disposable "
    b"worktree.\n"
)
SUPPORTED_HOST_ASSUMPTIONS = (
    "single_host",
    "one_attempt_one_dedicated_worktree",
    "no_authoritative_effects",
)


# ------------------------------------------------------------------- utilities


def git(cwd: Path, *arguments: str) -> str:
    completed = subprocess.run(
        ["git", "-C", str(cwd), *arguments],
        capture_output=True,
        text=True,
        check=True,
    )
    return completed.stdout.strip()


def assert_durable(root: Path) -> Path:
    """The qualified V1 profile refuses volatile workspace roots."""

    resolved = root.expanduser().resolve()
    for forbidden in NON_DURABLE_ROOTS:
        if resolved == Path(forbidden) or Path(forbidden) in resolved.parents:
            raise SystemExit(
                f"workspace root {resolved} is under {forbidden}, which the "
                "qualified V1 profile does not accept; pass --workspace-root"
            )
    resolved.mkdir(parents=True, exist_ok=True)
    return resolved


def make_repository(path: Path, *, files: int, size: int) -> str:
    """A repository whose one commit is B1.

    ``files``/``size`` set how long a checkout takes, which is what sets the
    width of the window a concurrent reader can land in.
    """

    path.mkdir(parents=True, exist_ok=True)
    git(path, "init", "--quiet", "-b", "main")
    git(path, "config", "user.name", "Broodling P2 Evidence")
    git(path, "config", "user.email", "broodling-p2@example.invalid")
    git(path, "config", "commit.gpgsign", "false")
    for index in range(files):
        (path / f"b1-{index:05d}.txt").write_text("B1\n" * size, encoding="utf-8")
    git(path, "add", "-A")
    git(path, "commit", "--quiet", "-m", "B1")
    return git(path, "rev-parse", "HEAD")


# ------------------------------------------------------- mechanism: stage order


def observe_stages(repository: Path, worktree: Path, commit: str) -> dict[str, Any]:
    """Time what ``git worktree add`` publishes, and when.

    Polls the four facts a concurrent ``provision`` reads: the registration
    ``git worktree list`` enumerates, the moment the directory stops being
    empty, the attached branch the listing reports, and the checked-out tree.
    Nothing here uses Broodling; it is a property of Git.
    """

    admin = Path(git(repository, "rev-parse", "--git-common-dir"))
    if not admin.is_absolute():
        admin = (repository / admin).resolve()
    entry = admin / "worktrees" / worktree.name
    expected = len(list(repository.glob("b1-*.txt")))
    seen: dict[str, float] = {}
    stop = threading.Event()

    def poll() -> None:
        start = time.monotonic()
        while not stop.is_set():
            now = (time.monotonic() - start) * 1000.0
            try:
                if "registered" not in seen and (entry / "gitdir").exists():
                    seen["registered"] = now
                if "directory_populated" not in seen and worktree.is_dir():
                    if any(worktree.iterdir()):
                        seen["directory_populated"] = now
                if "branch_attached" not in seen and (entry / "HEAD").exists():
                    if (entry / "HEAD").read_text().startswith("ref: refs/heads/"):
                        seen["branch_attached"] = now
                if "tree_complete" not in seen and worktree.is_dir():
                    if sum(1 for _ in worktree.glob("b1-*.txt")) == expected:
                        seen["tree_complete"] = now
            except OSError:  # a file being rewritten under us is expected
                pass

    watcher = threading.Thread(target=poll)
    watcher.start()
    subprocess.run(
        [
            "git",
            "-C",
            str(repository),
            "worktree",
            "add",
            "-b",
            "probe",
            str(worktree),
            commit,
        ],
        capture_output=True,
        check=True,
    )
    stop.set()
    watcher.join()
    return {
        "milestones_ms": {
            key: round(value, 2)
            for key, value in sorted(seen.items(), key=lambda item: item[1])
        },
        "registered_before_branch_attached": (
            seen.get("registered", 0.0) <= seen.get("branch_attached", 0.0)
        ),
        # If this holds, "contains files but is not registered" is never a true
        # statement about a settled worktree - only about one being built.
        "registered_before_directory_populated": (
            seen.get("registered", 0.0) <= seen.get("directory_populated", 0.0)
        ),
        "detached_window_ms": round(
            seen.get("branch_attached", 0.0) - seen.get("registered", 0.0), 2
        ),
        "incomplete_tree_window_ms": round(
            seen.get("tree_complete", 0.0) - seen.get("branch_attached", 0.0), 2
        ),
    }


def stages(arguments: argparse.Namespace) -> dict[str, Any]:
    trials = []
    for index in range(arguments.trials):
        base = Path(
            tempfile.mkdtemp(prefix="b13-stages-", dir=arguments.workspace_root)
        )
        try:
            repository = base / "source"
            commit = make_repository(
                repository, files=arguments.files, size=arguments.file_lines
            )
            trials.append(observe_stages(repository, base / "worktree", commit))
        finally:
            shutil.rmtree(base, ignore_errors=True)
        del index
    return {
        "files_in_b1": arguments.files,
        "trials": trials,
        "every_trial_registers_before_attaching_the_branch": all(
            trial["registered_before_branch_attached"] for trial in trials
        ),
        "every_trial_registers_before_populating_the_directory": all(
            trial["registered_before_directory_populated"] for trial in trials
        ),
        "widest_detached_window_ms": max(
            trial["detached_window_ms"] for trial in trials
        ),
        "widest_incomplete_tree_window_ms": max(
            trial["incomplete_tree_window_ms"] for trial in trials
        ),
    }


# --------------------------------------------------------- the admission fixture


def criterion() -> Criterion:
    return Criterion(
        criterion_id="c1",
        statement="Concurrent identical admission converges on one current Attempt.",
        evidence_population=EvidencePopulation(
            kind="enumerated",
            members=("qualification/v1-p2/issue13_concurrency.py",),
        ),
        validation_seam="broodling.provisioning.AttemptProvisioner.provision",
        validation_action="python qualification/v1-p2/issue13_concurrency.py --race",
        falsifying_observation=(
            "two semantically identical concurrent admissions return different "
            "Attempts, different worktrees, or one of them an error"
        ),
    )


def admitted_revision(store: BroodlingStore) -> tuple[Any, Any]:
    """One Work Unit with one admitted no-effect Contract revision."""

    work_unit = store.resolve_work_unit(
        WorkReference.parse(repository=REPOSITORY, issue=ISSUE)
    )
    source = store.entitle_source(
        work_unit.work_unit_id,
        SourceSubmission(
            kind="primary_issue",
            locator=work_unit.issue_locator,
            content=ISSUE_BODY,
            media_type="text/markdown; charset=utf-8",
            retrieved_at="2026-09-07T00:00:00+00:00",
        ),
    )
    revision = store.record_contract_revision(
        Contract(
            work_unit_id=work_unit.work_unit_id,
            source_attribution=(
                SourceAttribution(source.source_id, source.content_sha256),
            ),
            criteria=(criterion(),),
            host_assumptions=SUPPORTED_HOST_ASSUMPTIONS,
        )
    )
    store.admit(revision.contract_revision_id)
    return work_unit, revision


# --------------------------------------------------------------- the race itself


def durable_state(store_path: Path, work_unit_id: str, repository: Path) -> dict:
    """What the store and the host actually hold once the racers are done."""

    store = BroodlingStore.open(store_path)
    try:
        attempts = [
            dict(row)
            for row in store.connection.execute(
                "SELECT attempt_id, is_current, contract_revision_id, "
                "b1_commit_oid FROM attempts WHERE work_unit_id = ?",
                (work_unit_id,),
            )
        ]
        assignments = [
            dict(row)
            for row in store.connection.execute(
                "SELECT attempt_id, worktree_path, branch, state FROM "
                "worktree_assignments WHERE work_unit_id = ?",
                (work_unit_id,),
            )
        ]
    finally:
        store.close()

    listing = git(repository, "worktree", "list", "--porcelain")
    registered = [
        line.removeprefix("worktree ")
        for line in listing.splitlines()
        if line.startswith("worktree ")
    ]
    branches = git(repository, "branch", "--format=%(refname:short)").split()
    heads = {}
    for assignment in assignments:
        path = Path(assignment["worktree_path"])
        if path.is_dir() and (path / ".git").exists():
            heads[assignment["worktree_path"]] = {
                "head": git(path, "rev-parse", "HEAD"),
                "branch": git(path, "rev-parse", "--abbrev-ref", "HEAD"),
                "tracked_files": len(git(path, "ls-files").splitlines()),
            }
    return {
        "attempts": attempts,
        "current_attempts": [row for row in attempts if row["is_current"]],
        "worktree_assignments": assignments,
        "registered_worktrees": registered,
        "branches": branches,
        "materialized": heads,
    }


def classify(
    round_number: int, racers: list[dict], state: dict, b1: str, tracked_in_b1: int
) -> list[str]:
    """Name every way this round failed to converge. Silence is the pass."""

    violations = []
    if len(state["attempts"]) != 1:
        violations.append(f"competing_authority: {len(state['attempts'])} Attempt rows")
    if len(state["current_attempts"]) != 1:
        violations.append(
            f"competing_authority: {len(state['current_attempts'])} current Attempts"
        )
    if len(state["worktree_assignments"]) != 1:
        violations.append(
            f"duplicate_worktree: {len(state['worktree_assignments'])} assignments"
        )
    failed = [racer for racer in racers if not racer["ok"]]
    if failed:
        violations.append(
            "inconsistent_result: " + "; ".join(f"{racer['error']}" for racer in failed)
        )
    returned = {racer["attempt_id"] for racer in racers if racer["ok"]}
    if len(returned) > 1:
        violations.append(f"competing_authority: racers returned {sorted(returned)}")
    paths = {racer["worktree_path"] for racer in racers if racer["ok"]}
    if len(paths) > 1:
        violations.append(f"duplicate_worktree: racers returned {sorted(paths)}")
    for path, observed in state["materialized"].items():
        if observed["head"] != b1:
            violations.append(f"not_at_b1: {path} is at {observed['head']}")
        if observed["tracked_files"] != tracked_in_b1:
            violations.append(
                f"incomplete_tree: {path} has {observed['tracked_files']} of "
                f"{tracked_in_b1} tracked files"
            )
    for racer in racers:
        if not racer["ok"]:
            continue
        if racer["observed_head"] != b1:
            violations.append(
                f"inconsistent_result: racer was handed a worktree at "
                f"{racer['observed_head']}, not B1 {b1}"
            )
        # The durable state settles once the winner finishes, so a late look
        # always finds B1. Only the caller can see what it was actually given.
        if racer["observed_tracked_files"] != tracked_in_b1:
            violations.append(
                f"inconsistent_result: racer was handed a worktree holding "
                f"{racer['observed_tracked_files']} of {tracked_in_b1} files "
                f"with provisioned={racer['provisioned']}"
            )
    del round_number
    return violations


def one_round(arguments: argparse.Namespace, round_number: int) -> dict:
    base = Path(tempfile.mkdtemp(prefix="b13-race-", dir=arguments.workspace_root))
    store_path = base / "state" / "broodling.sqlite3"
    repository = base / "source"
    workspace_root = base / "workspaces"
    workspace_root.mkdir(parents=True)
    try:
        b1 = make_repository(
            repository, files=arguments.files, size=arguments.file_lines
        )
        store = BroodlingStore.open(store_path)
        work_unit, revision = admitted_revision(store)
        store.close()

        gate = base / "gate"
        started = [
            subprocess.Popen(
                [
                    sys.executable,
                    str(RACER),
                    str(store_path),
                    str(workspace_root),
                    str(repository),
                    revision.contract_revision_id,
                    str(gate),
                ],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            for _ in range(arguments.racers)
        ]
        gate.write_text("go\n", encoding="utf-8")
        racers = []
        for process in started:
            stdout, stderr = process.communicate()
            line = stdout.strip().splitlines()[-1] if stdout.strip() else ""
            if line.startswith("{"):
                racers.append(json.loads(line))
            else:
                racers.append(
                    {
                        "ok": False,
                        "error": f"racer exited {process.returncode}: "
                        f"{stderr.strip().splitlines()[-1] if stderr.strip() else '?'}",
                        "attempt_id": None,
                        "worktree_path": None,
                        "observed_head": None,
                    }
                )
        state = durable_state(store_path, work_unit.work_unit_id, repository)
        violations = classify(round_number, racers, state, b1, arguments.files)
        return {
            "round": round_number,
            "b1": b1,
            "racers": racers,
            "durable_state": state,
            "violations": violations,
        }
    finally:
        for entry in sorted(workspace_root.glob("*")):
            shutil.rmtree(entry, ignore_errors=True)
        shutil.rmtree(base, ignore_errors=True)


def race(arguments: argparse.Namespace) -> dict:
    rounds = []
    for number in range(arguments.rounds):
        result = one_round(arguments, number)
        rounds.append(result)
        if result["violations"]:
            print(f"round {number}: {'; '.join(result['violations'])}", flush=True)
    violated = [result for result in rounds if result["violations"]]
    kinds = Counter(
        violation.split(":", 1)[0]
        for result in violated
        for violation in result["violations"]
    )
    return {
        "rounds": arguments.rounds,
        "racers_per_round": arguments.racers,
        "files_in_b1": arguments.files,
        "rounds_with_violations": len(violated),
        "violation_kinds": dict(kinds),
        "converged": not violated,
        # Failing rounds are kept whole; passing ones would only add bulk.
        "failing_rounds": violated,
        "sample_passing_round": next(
            (result for result in rounds if not result["violations"]), None
        ),
    }


# ---------------------------------------------------------------------- driver


def environment() -> dict:
    return {
        "python": platform.python_version(),
        "platform": platform.platform(),
        "git": subprocess.run(
            ["git", "--version"], capture_output=True, text=True, check=True
        ).stdout.strip(),
        "broodling_commit": subprocess.run(
            ["git", "-C", str(REPOSITORY_ROOT), "rev-parse", "HEAD"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip(),
        # Named, not a boolean: a pre-fix run happens in a checkout of the
        # reviewed commit with this harness copied in, and the record should say
        # exactly that rather than only "dirty".
        "uncommitted_paths": sorted(
            line[3:]
            for line in subprocess.run(
                ["git", "-C", str(REPOSITORY_ROOT), "status", "--porcelain"],
                capture_output=True,
                text=True,
                check=True,
            ).stdout.splitlines()
            if line.strip()
        ),
        "cpu_count": os.cpu_count(),
        "recorded_at": datetime.now(UTC).isoformat(timespec="seconds"),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--race", action="store_true", help="run the racing witness")
    parser.add_argument("--stages", action="store_true", help="time git's stage order")
    parser.add_argument("--rounds", type=int, default=200)
    parser.add_argument("--racers", type=int, default=4)
    parser.add_argument("--trials", type=int, default=5)
    parser.add_argument(
        "--files",
        type=int,
        default=1,
        help="files in B1; a larger tree widens the incomplete-checkout window",
    )
    parser.add_argument("--file-lines", type=int, default=1)
    parser.add_argument(
        "--workspace-root",
        type=Path,
        default=Path.home() / ".cache" / "broodling-evidence",
        help="durable root; the qualified profile refuses /tmp and /dev/shm",
    )
    parser.add_argument("--output", type=Path)
    arguments = parser.parse_args()
    if not (arguments.race or arguments.stages):
        parser.error("choose --race, --stages, or both")
    arguments.workspace_root = assert_durable(arguments.workspace_root)

    record: dict[str, Any] = {"environment": environment()}
    if arguments.stages:
        record["stage_order"] = stages(arguments)
    if arguments.race:
        record["race"] = race(arguments)

    text = json.dumps(record, indent=2, sort_keys=False)
    if arguments.output:
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(text + "\n", encoding="utf-8")
        print(f"wrote {arguments.output}")
    else:
        print(text)
    return 0 if record.get("race", {"converged": True})["converged"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
