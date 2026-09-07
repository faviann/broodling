"""Contract revisions are immutable; new meaning is a new revision, not an edit."""

from __future__ import annotations

import json
import sqlite3
import unittest

from broodling import (
    Contract,
    ContractImmutabilityError,
    EvidencePopulation,
    Obligation,
    RequiredEffect,
    SourceAttribution,
    SourceSubmission,
)
from support import ISSUE_BODY, StoreTestCase, criterion


class RevisionRecordingTests(StoreTestCase):
    def test_a_recorded_revision_holds_the_contract_verbatim(self) -> None:
        work_unit, source, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        self.assertEqual(revision.revision_number, 1)
        self.assertIsNone(revision.supersedes_revision_id)
        self.assertEqual(revision.canonical_bytes, contract.canonical_bytes())
        self.assertEqual(revision.contract, contract)

    def test_identical_meaning_resolves_the_same_revision(self) -> None:
        _, _, contract = self.admissible_contract()
        first = self.store.record_contract_revision(contract)
        again = self.store.record_contract_revision(contract)
        self.assertEqual(again.contract_revision_id, first.contract_revision_id)
        self.assertEqual(again.revision_number, 1)
        self.assertEqual(len(self.store.list_contract_revisions(first.work_unit_id)), 1)

    def test_a_meaning_change_becomes_a_new_revision(self) -> None:
        work_unit, source, contract = self.admissible_contract()
        first = self.store.record_contract_revision(contract)
        widened = self.contract(
            work_unit,
            source,
            criteria=(
                criterion(),
                criterion(
                    "c2",
                    statement="Rejection preserves the obligation it refused.",
                ),
            ),
        )
        second = self.store.record_contract_revision(widened)

        self.assertNotEqual(second.contract_revision_id, first.contract_revision_id)
        self.assertEqual(second.revision_number, 2)
        self.assertEqual(second.supersedes_revision_id, first.contract_revision_id)
        stored_first = self.store.get_contract_revision(first.contract_revision_id)
        self.assertEqual(stored_first.canonical_bytes, contract.canonical_bytes())
        self.assertEqual(len(stored_first.contract.criteria), 1)

    def test_revision_lineage_survives_reopen(self) -> None:
        work_unit, source, contract = self.admissible_contract()
        first = self.store.record_contract_revision(contract)
        second = self.store.record_contract_revision(
            self.contract(work_unit, source, notes="second reading of the same issue")
        )
        reopened = self.reopen()
        lineage = reopened.list_contract_revisions(work_unit.work_unit_id)
        self.assertEqual(
            [record.contract_revision_id for record in lineage],
            [first.contract_revision_id, second.contract_revision_id],
        )
        self.assertEqual([record.revision_number for record in lineage], [1, 2])


class LiveSourceDriftTests(StoreTestCase):
    def test_live_issue_edits_do_not_change_an_admitted_revision(self) -> None:
        work_unit, source, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        decision = self.store.admit(revision.contract_revision_id)
        self.assertTrue(decision.admitted)

        # The upstream issue is edited after admission and re-ingested.
        edited = self.store.entitle_source(
            work_unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=work_unit.issue_locator,
                content=ISSUE_BODY + b"\nAlso publish a release announcement.\n",
            ),
        )
        self.assertNotEqual(edited.source_id, source.source_id)

        reopened = self.reopen()
        stored = reopened.get_contract_revision(revision.contract_revision_id)
        self.assertEqual(stored.canonical_bytes, contract.canonical_bytes())
        material = reopened.contract_source_material(revision.contract_revision_id)
        self.assertEqual([record.content for record in material], [ISSUE_BODY])
        self.assertTrue(reopened.is_admitted(revision.contract_revision_id))

    def test_the_admitted_material_reconstructs_what_the_contract_was_built_from(
        self,
    ) -> None:
        work_unit, source, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        material = self.store.contract_source_material(revision.contract_revision_id)
        attributed = {
            item.source_id: item.content_sha256 for item in contract.source_attribution
        }
        self.assertEqual(
            {record.source_id: record.content_sha256 for record in material}, attributed
        )
        self.assertEqual(material[0].content, ISSUE_BODY)


class ImmutabilityEnforcementTests(StoreTestCase):
    def setUp(self) -> None:
        super().setUp()
        self.work_unit, self.source, contract = self.admissible_contract()
        self.revision = self.store.record_contract_revision(contract)
        self.store.admit(self.revision.contract_revision_id)

    def test_direct_sql_cannot_amend_or_delete_a_revision(self) -> None:
        for statement in (
            "UPDATE contract_revisions SET canonical_bytes = X'7B7D' "
            "WHERE contract_revision_id = ?",
            "UPDATE contract_revisions SET contract_sha256 = '0' "
            "WHERE contract_revision_id = ?",
            "DELETE FROM contract_revisions WHERE contract_revision_id = ?",
        ):
            with self.subTest(statement=statement):
                with self.assertRaises(sqlite3.DatabaseError):
                    self.store.connection.execute(
                        statement, (self.revision.contract_revision_id,)
                    )
        self.assertEqual(
            self.store.get_contract_revision(
                self.revision.contract_revision_id
            ).canonical_bytes,
            self.revision.canonical_bytes,
        )

    def test_direct_sql_cannot_restate_source_attribution(self) -> None:
        for statement in (
            "UPDATE contract_source_attributions SET content_sha256 = '0' "
            "WHERE contract_revision_id = ?",
            "DELETE FROM contract_source_attributions WHERE contract_revision_id = ?",
        ):
            with self.subTest(statement=statement):
                with self.assertRaises(sqlite3.DatabaseError):
                    self.store.connection.execute(
                        statement, (self.revision.contract_revision_id,)
                    )

    def test_bytes_altered_around_the_triggers_are_read_as_a_failure(self) -> None:
        # Simulates out-of-band tampering: drop the guard, rewrite the bytes.
        # The recorded digest must still refuse to serve altered meaning.
        self.store.connection.execute("DROP TRIGGER contract_revisions_no_update")
        self.store.connection.execute(
            "UPDATE contract_revisions SET canonical_bytes = ? "
            "WHERE contract_revision_id = ?",
            (b"{}", self.revision.contract_revision_id),
        )
        with self.assertRaises(ContractImmutabilityError):
            self.store.get_contract_revision(self.revision.contract_revision_id)

    def test_the_api_offers_no_way_to_amend_a_revision(self) -> None:
        mutating = [
            name
            for name in dir(self.store)
            if not name.startswith("_")
            and any(
                verb in name
                for verb in ("amend", "update", "edit", "delete", "replace", "patch")
            )
        ]
        self.assertEqual(mutating, [])


class ObligationPreservationTests(StoreTestCase):
    def test_an_inadmissible_obligation_is_stored_exactly_as_stated(self) -> None:
        work_unit, source = self.admitted_work_unit()
        statement = "Push the merged branch to origin/main and close issue #12."
        contract = self.contract(
            work_unit,
            source,
            obligations=(Obligation("o1", statement, "push"),),
            required_effects=(RequiredEffect("e1", statement, "push"),),
        )
        revision = self.store.record_contract_revision(contract)
        stored = json.loads(revision.canonical_bytes)
        self.assertEqual(stored["obligations"][0]["statement"], statement)
        self.assertEqual(stored["requiredEffects"][0]["statement"], statement)
        self.assertEqual(revision.contract.required_effects[0].statement, statement)

    def test_an_unbounded_population_is_representable_so_it_can_be_refused(
        self,
    ) -> None:
        work_unit, source = self.admitted_work_unit()
        contract = self.contract(
            work_unit,
            source,
            criteria=(
                criterion(
                    evidence_population=EvidencePopulation(kind="unbounded"),
                    validation_seam="",
                ),
            ),
        )
        revision = self.store.record_contract_revision(contract)
        self.assertEqual(
            revision.contract.criteria[0].evidence_population.kind, "unbounded"
        )


class ContractIdentityTests(unittest.TestCase):
    def test_canonical_bytes_are_stable_across_field_ordering(self) -> None:
        attribution = (SourceAttribution("src-a", "a" * 64),)
        first = Contract(
            work_unit_id="wu-1",
            source_attribution=attribution,
            criteria=(criterion(),),
            host_assumptions=("single_host",),
        )
        second = Contract(
            criteria=(criterion(),),
            host_assumptions=("single_host",),
            source_attribution=attribution,
            work_unit_id="wu-1",
        )
        self.assertEqual(first.canonical_bytes(), second.canonical_bytes())
        self.assertEqual(first.contract_revision_id, second.contract_revision_id)

    def test_meaning_changes_change_the_revision_id(self) -> None:
        base = Contract(
            work_unit_id="wu-1",
            source_attribution=(SourceAttribution("src-a", "a" * 64),),
            criteria=(criterion(),),
        )
        variants = (
            Contract(
                work_unit_id="wu-1",
                source_attribution=(SourceAttribution("src-b", "b" * 64),),
                criteria=(criterion(),),
            ),
            Contract(
                work_unit_id="wu-1",
                source_attribution=(SourceAttribution("src-a", "a" * 64),),
                criteria=(criterion(statement="something else entirely"),),
            ),
            Contract(
                work_unit_id="wu-1",
                source_attribution=(SourceAttribution("src-a", "a" * 64),),
                criteria=(criterion(),),
                required_effects=(RequiredEffect("e1", "push", "push"),),
            ),
        )
        for variant in variants:
            with self.subTest(variant=variant.contract_sha256):
                self.assertNotEqual(
                    variant.contract_revision_id, base.contract_revision_id
                )


if __name__ == "__main__":
    unittest.main()
