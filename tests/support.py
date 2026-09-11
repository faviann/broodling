"""Fixture builders shared by the V1-P2 admission tests."""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

from broodling import (
    BroodlingStore,
    Contract,
    Criterion,
    EvidencePopulation,
    SourceAttribution,
    SourceSubmission,
    WorkReference,
)
from broodling.workspace import NON_DURABLE_ROOTS

#: Where worktree tests put their durable workspace roots. The qualified profile
#: refuses `/tmp`, so these fixtures cannot use the usual temporary directory.
DURABLE_ROOT_ENV = "BROODLING_TEST_WORKSPACE_ROOT"

REPOSITORY = "https://github.com/faviann/broodling"
ISSUE = 12

ISSUE_BODY = (
    b"Build the minimal admission nucleus for Work Unit identity, source "
    b"entitlement, immutable Contract revisions and V1 no-effect admission.\n"
)

SUPPORTED_HOST_ASSUMPTIONS = (
    "single_host",
    "one_attempt_one_dedicated_worktree",
    "no_authoritative_effects",
)


def work_reference(**overrides) -> WorkReference:
    fields = {"repository": REPOSITORY, "issue": ISSUE}
    fields.update(overrides)
    return WorkReference.parse(**fields)


def criterion(criterion_id: str = "c1", **overrides) -> Criterion:
    fields = {
        "criterion_id": criterion_id,
        "statement": "Repeated canonical ingress resolves one Work Unit.",
        "evidence_population": EvidencePopulation(
            kind="enumerated",
            members=("tests/test_work_unit_identity.py::CanonicalIngressTests",),
        ),
        "validation_seam": "broodling.store.BroodlingStore.resolve_work_unit",
        "validation_action": "python -m unittest tests.test_work_unit_identity",
        "falsifying_observation": (
            "two canonical submissions of the same repository and issue resolve "
            "different Work Unit ids"
        ),
    }
    fields.update(overrides)
    return Criterion(**fields)


class StoreTestCase(unittest.TestCase):
    """A temporary Broodling-owned store outside any worktree."""

    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="broodling-p2-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.store_path = self.root / "state" / "broodling.sqlite3"
        self._opened_stores: list[BroodlingStore] = []
        # Registered after the directory cleanup so it runs before it: every
        # connection this test opened is closed, including the ones `reopen`
        # replaced, and including when the test body raises.
        self.addCleanup(self._close_opened_stores)
        self.store = self.open_store()

    def open_store(self) -> BroodlingStore:
        """Open a store on the fixture path and arrange for it to be closed."""

        store = BroodlingStore.open(self.store_path)
        self._opened_stores.append(store)
        return store

    def _close_opened_stores(self) -> None:
        """Close every store this test opened, newest first.

        Closing a connection twice is harmless, so cases that close a handle
        themselves (the crash fixtures) stay correct.
        """

        while self._opened_stores:
            self._opened_stores.pop().close()

    def reopen(self) -> BroodlingStore:
        """Close and reopen the store, as a restart would."""

        self.store.close()
        self.store = self.open_store()
        return self.store

    def admitted_work_unit(self):
        work_unit = self.store.resolve_work_unit(work_reference())
        source = self.store.entitle_source(
            work_unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=work_unit.issue_locator,
                content=ISSUE_BODY,
                media_type="text/markdown; charset=utf-8",
                retrieved_at="2026-09-07T00:00:00+00:00",
            ),
        )
        return work_unit, source

    def contract(self, work_unit, source, **overrides) -> Contract:
        fields = {
            "work_unit_id": work_unit.work_unit_id,
            "source_attribution": (
                SourceAttribution(source.source_id, source.content_sha256),
            ),
            "criteria": (criterion(),),
            "host_assumptions": SUPPORTED_HOST_ASSUMPTIONS,
        }
        fields.update(overrides)
        return Contract(**fields)

    def admissible_contract(self) -> tuple:
        work_unit, source = self.admitted_work_unit()
        return work_unit, source, self.contract(work_unit, source)


def durable_test_root(prefix: str = "broodling-p2-workspace-") -> Path:
    """A temporary directory that the durable-workspace-root policy accepts.

    ``tempfile.mkdtemp`` lands under ``/tmp``, which the qualified V1 profile
    rejects as a worktree root — the point of the policy. These fixtures
    therefore allocate under the user's cache directory, or under
    ``BROODLING_TEST_WORKSPACE_ROOT`` when the host wants somewhere else.
    """

    configured = os.environ.get(DURABLE_ROOT_ENV)
    base = (
        Path(configured) if configured else Path.home() / ".cache" / "broodling-tests"
    )
    base = base.expanduser().resolve()
    for forbidden in NON_DURABLE_ROOTS:
        if base == Path(forbidden) or Path(forbidden) in base.parents:
            raise unittest.SkipTest(
                f"{base} is under {forbidden}, which the durable workspace-root "
                f"policy refuses; set {DURABLE_ROOT_ENV} to a durable directory"
            )
    base.mkdir(parents=True, exist_ok=True)
    return Path(tempfile.mkdtemp(prefix=prefix, dir=base))


def git(repository: Path, *arguments: str) -> str:
    """Run a Git command in a fixture repository."""

    completed = subprocess.run(
        ["git", "-C", str(repository), *arguments],
        capture_output=True,
        text=True,
        check=True,
    )
    return completed.stdout.strip()


def make_repository(
    path: Path, *, content: str = "original admitted state\n", bulk: int = 0
) -> str:
    """Create a repository with one commit and return that commit's object id.

    ``bulk`` adds that many further files to B1. Checking a tree out is not
    instant, and a caller racing another one can only observe the difference
    when the checkout takes long enough to be observed at all; ``bulk`` is how a
    test buys that window instead of hoping for it.
    """

    path.mkdir(parents=True, exist_ok=True)
    git(path, "init", "--quiet", "-b", "main")
    git(path, "config", "user.name", "Broodling P2 Fixture")
    git(path, "config", "user.email", "broodling-p2@example.invalid")
    git(path, "config", "commit.gpgsign", "false")
    (path / "README.md").write_text(content, encoding="utf-8")
    for index in range(bulk):
        (path / f"b1-{index:04d}.txt").write_text("B1\n" * 200, encoding="utf-8")
    git(path, "add", "-A")
    git(path, "commit", "--quiet", "-m", "B1")
    return git(path, "rev-parse", "HEAD")


def move_head(repository: Path, *, content: str = "live head drift\n") -> str:
    """Advance the repository's live HEAD and return the new commit id."""

    (repository / "README.md").write_text(content, encoding="utf-8")
    git(repository, "add", "README.md")
    git(repository, "commit", "--quiet", "-m", "live head drift")
    return git(repository, "rev-parse", "HEAD")


def tracked_files(worktree: Path) -> tuple[str, ...]:
    listing = git(worktree, "ls-files")
    return tuple(line for line in listing.splitlines() if line)


class AttemptTestCase(StoreTestCase):
    """A store, one admitted Contract revision, a source repository and a root."""

    def setUp(self) -> None:
        super().setUp()
        self.workspace_root = durable_test_root()
        source_root = durable_test_root(prefix="broodling-source-")
        self.addCleanup(shutil.rmtree, source_root, ignore_errors=True)
        self.addCleanup(self._retire_workspaces)
        # Qualified provider dispatch also requires shared Git metadata outside
        # its ambient writable /tmp scratch root (discovered during #21).
        self.repository = source_root / "source"
        self.b1 = make_repository(self.repository)
        work_unit, _source, contract = self.admissible_contract()
        self.work_unit = work_unit
        self.revision = self.store.record_contract_revision(contract)
        self.decision = self.store.admit(self.revision.contract_revision_id)

    def _retire_workspaces(self) -> None:
        """Remove worktrees before the source repository disappears."""

        for entry in sorted(self.workspace_root.glob("*")):
            shutil.rmtree(entry, ignore_errors=True)
        shutil.rmtree(self.workspace_root, ignore_errors=True)

    def provisioner(self, root: Path | None = None):
        from broodling import AttemptProvisioner

        return AttemptProvisioner(self.store, root or self.workspace_root)
