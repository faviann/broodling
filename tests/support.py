"""Fixture builders shared by the V1-P2 admission tests."""

from __future__ import annotations

import shutil
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
        self.store = BroodlingStore.open(self.store_path)
        self.addCleanup(self.store.close)

    def reopen(self) -> BroodlingStore:
        """Close and reopen the store, as a restart would."""

        self.store.close()
        self.store = BroodlingStore.open(self.store_path)
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
