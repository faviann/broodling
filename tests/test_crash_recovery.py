"""Crash and reopen: committed facts survive, partial writes leave no authority."""

from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path

from broodling import ADMITTED, BroodlingStore, UnknownRecord
from support import StoreTestCase, work_reference

CHILD = Path(__file__).resolve().parent / "crash_child.py"


class CrashRecoveryTests(StoreTestCase):
    """Each case writes through a child process that is killed mid-flight."""

    def setUp(self) -> None:
        super().setUp()
        # The store is created by the child; close the parent handle first so the
        # child owns the database for the duration of its (interrupted) write.
        self.store.close()

    def crash_at(self, crash_point: str) -> str:
        completed = subprocess.run(
            [sys.executable, str(CHILD), str(self.store_path), crash_point],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(
            completed.returncode,
            97,
            f"child did not die as instructed: {completed.stderr}",
        )
        revision_id = completed.stdout.strip().splitlines()[-1]
        self.assertTrue(revision_id.startswith("cr-"), completed.stdout)
        return revision_id

    def reopened(self) -> BroodlingStore:
        store = BroodlingStore.open(self.store_path)
        self.addCleanup(store.close)
        return store

    def test_a_crash_mid_contract_write_leaves_no_revision(self) -> None:
        revision_id = self.crash_at("mid_contract_write")
        store = self.reopened()

        with self.assertRaises(UnknownRecord):
            store.get_contract_revision(revision_id)
        self.assertEqual(store.contract_source_material(revision_id), ())
        self.assertFalse(store.is_admitted(revision_id))
        self.assertIsNone(store.find_admission_decision(revision_id))

        # The Work Unit and its entitled source committed earlier and survive.
        work_unit = store.get_work_unit(work_reference().work_unit_id)
        self.assertEqual(work_unit.reference_key, "github.com/faviann/broodling#12")
        self.assertEqual(len(store.list_entitled_sources(work_unit.work_unit_id)), 1)

    def test_a_crash_after_the_contract_commit_leaves_no_admission(self) -> None:
        revision_id = self.crash_at("after_contract_commit")
        store = self.reopened()

        revision = store.get_contract_revision(revision_id)
        self.assertEqual(revision.revision_number, 1)
        self.assertEqual(len(store.contract_source_material(revision_id)), 1)

        # A stored revision is not authority until a decision commits.
        self.assertIsNone(store.find_admission_decision(revision_id))
        self.assertFalse(store.is_admitted(revision_id))

    def test_a_crash_mid_admission_write_leaves_no_half_admitted_authority(
        self,
    ) -> None:
        revision_id = self.crash_at("mid_admission_write")
        store = self.reopened()

        store.get_contract_revision(revision_id)
        self.assertIsNone(store.find_admission_decision(revision_id))
        self.assertFalse(store.is_admitted(revision_id))
        rows = store.connection.execute(
            "SELECT count(*) AS total FROM admission_decisions"
        ).fetchone()
        self.assertEqual(rows["total"], 0)

    def test_an_interrupted_admission_can_simply_be_decided_again(self) -> None:
        revision_id = self.crash_at("mid_admission_write")
        store = self.reopened()
        decision = store.admit(revision_id)
        self.assertEqual(decision.outcome, ADMITTED)
        self.assertTrue(store.is_admitted(revision_id))

    def test_a_crash_after_the_admission_commit_keeps_every_fact(self) -> None:
        revision_id = self.crash_at("after_admission_commit")
        store = self.reopened()

        work_unit = store.get_work_unit(work_reference().work_unit_id)
        self.assertEqual(len(store.list_entitled_sources(work_unit.work_unit_id)), 1)
        revision = store.get_contract_revision(revision_id)
        self.assertEqual(revision.work_unit_id, work_unit.work_unit_id)
        decision = store.get_admission_decision(revision_id)
        self.assertEqual(decision.outcome, ADMITTED)
        self.assertTrue(store.is_admitted(revision_id))
        material = store.contract_source_material(revision_id)
        self.assertEqual(len(material), 1)
        self.assertEqual(
            material[0].content_sha256,
            revision.contract.source_attribution[0].content_sha256,
        )

    def test_a_crashed_write_does_not_block_a_later_writer(self) -> None:
        self.crash_at("mid_contract_write")
        store = self.reopened()
        work_unit = store.resolve_work_unit(work_reference())
        self.assertEqual(store.submission_count(work_unit.work_unit_id), 2)


class DurabilityAcrossReopenTests(StoreTestCase):
    def test_every_admitted_fact_is_recoverable_from_a_fresh_process(self) -> None:
        work_unit, source, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        self.store.admit(revision.contract_revision_id)
        self.store.close()

        script = (
            "import json, sys;"
            "sys.path.insert(0, %r);"
            "from broodling import BroodlingStore;"
            "store = BroodlingStore.open(%r);"
            "revision = store.get_contract_revision(%r);"
            "decision = store.get_admission_decision(%r);"
            "print(json.dumps({"
            "'workUnit': store.get_work_unit(%r).reference_key,"
            "'sources': [item.content.decode() for item in "
            "store.contract_source_material(%r)],"
            "'contractSha256': revision.contract_sha256,"
            "'outcome': decision.outcome,"
            "'admitted': store.is_admitted(%r)}))"
        ) % (
            str(Path(__file__).resolve().parents[1]),
            str(self.store_path),
            revision.contract_revision_id,
            revision.contract_revision_id,
            work_unit.work_unit_id,
            revision.contract_revision_id,
            revision.contract_revision_id,
        )
        completed = subprocess.run(
            [sys.executable, "-c", script], capture_output=True, text=True, check=True
        )
        import json

        recovered = json.loads(completed.stdout)
        self.assertEqual(recovered["workUnit"], "github.com/faviann/broodling#12")
        self.assertEqual(recovered["sources"], [source.content.decode()])
        self.assertEqual(recovered["contractSha256"], contract.contract_sha256)
        self.assertEqual(recovered["outcome"], ADMITTED)
        self.assertTrue(recovered["admitted"])


if __name__ == "__main__":
    unittest.main()
