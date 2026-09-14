"""Stable Work Unit identity: repeated ingress resolves one unit, conflicts do not alias."""

from __future__ import annotations

import sqlite3
import unittest

from broodling import WorkReference, WorkUnitIdentityConflict
from support import REPOSITORY, StoreTestCase, work_reference


#: The Work Unit id the V1 identity scheme derives for
#: ``github.com/faviann/broodling#12``. Stores persist this value in
#: ``work_units.work_unit_id`` and migrate in place across schema versions, and
#: ``resolve_work_unit`` looks a Work Unit up *by* the id it derives — so the
#: derivation recipe is a compatibility contract, not an implementation detail.
#: Change it and an upgraded build stops finding a Work Unit its store already
#: holds, then fails to insert the replacement against the existing
#: ``reference_key``. A new scheme therefore needs a migration, which is what
#: this constant exists to force.
V1_WORK_UNIT_ID = "wu-88920fac767d5d1561fc0ede6f80349fd02caa2a01ad9f1b19ad07e14660587d"


class CanonicalIngressTests(StoreTestCase):
    CANONICAL_FORMS = (
        ("https://github.com/faviann/broodling", 12),
        ("https://github.com/faviann/broodling.git", "#12"),
        ("git@github.com:faviann/broodling.git", "12"),
        ("ssh://git@github.com/Faviann/Broodling", 12),
        ("github.com/faviann/broodling/", 12),
        ("faviann/broodling", 12),
        (
            "https://github.com/faviann/broodling",
            "https://github.com/faviann/broodling/issues/12",
        ),
    )

    def test_repeated_canonical_ingress_resolves_one_work_unit(self) -> None:
        resolved = [
            self.store.resolve_work_unit(WorkReference.parse(repository, issue))
            for repository, issue in self.CANONICAL_FORMS
        ]
        self.assertEqual(
            {record.work_unit_id for record in resolved}, {resolved[0].work_unit_id}
        )
        self.assertEqual(
            {record.reference_key for record in resolved},
            {"github.com/faviann/broodling#12"},
        )
        rows = self.store.connection.execute(
            "SELECT count(*) AS total FROM work_units"
        ).fetchone()
        self.assertEqual(rows["total"], 1)

    def test_every_submission_is_retained_against_the_one_work_unit(self) -> None:
        for repository, issue in self.CANONICAL_FORMS:
            self.store.resolve_work_unit(WorkReference.parse(repository, issue))
        work_unit = self.store.resolve_work_unit(work_reference())
        self.assertEqual(
            self.store.submission_count(work_unit.work_unit_id),
            len(self.CANONICAL_FORMS) + 1,
        )

    def test_identity_survives_reopen(self) -> None:
        first = self.store.resolve_work_unit(work_reference())
        reopened = self.reopen()
        again = reopened.resolve_work_unit(work_reference())
        self.assertEqual(again.work_unit_id, first.work_unit_id)
        self.assertEqual(again.first_seen_at, first.first_seen_at)

    def test_work_unit_id_is_derived_not_allocated(self) -> None:
        self.assertEqual(
            work_reference().work_unit_id,
            self.store.resolve_work_unit(work_reference()).work_unit_id,
        )

    def test_a_work_unit_persisted_by_the_v1_scheme_still_resolves(self) -> None:
        """This build resolves the identity an earlier build would have stored."""

        resolved = self.store.resolve_work_unit(work_reference())
        self.assertEqual(resolved.reference_key, "github.com/faviann/broodling#12")
        self.assertEqual(resolved.work_unit_id, V1_WORK_UNIT_ID)


class DistinctIdentityTests(StoreTestCase):
    """Asserting an identity the reference does not name.

    Plain non-aliasing — two references differing in one canonical component
    resolving to two Work Units, and a disagreeing issue locator or unusable
    ingress being refused — is generated in
    ``test_work_reference_properties``, over every component rather than the two
    or three this module used to name. What is left here is the store operation
    that has no counterpart there: a submission that *asserts* a Work Unit id.
    """

    def test_asserting_a_known_id_for_a_different_reference_conflicts(self) -> None:
        original = self.store.resolve_work_unit(work_reference())
        with self.assertRaises(WorkUnitIdentityConflict) as caught:
            self.store.resolve_work_unit(
                WorkReference.parse(REPOSITORY, 13),
                expected_work_unit_id=original.work_unit_id,
            )
        self.assertIn(original.work_unit_id, str(caught.exception))
        self.assertEqual(
            self.store.get_work_unit(original.work_unit_id).issue_number, 12
        )


class UpstreamIdentityPinningTests(StoreTestCase):
    def test_an_unset_upstream_identity_is_pinned_once(self) -> None:
        self.store.resolve_work_unit(work_reference())
        pinned = self.store.resolve_work_unit(
            work_reference(
                issue_identity="I_kwDO_issue_12", repository_identity="R_repo"
            )
        )
        self.assertEqual(pinned.issue_identity, "I_kwDO_issue_12")
        self.assertEqual(pinned.repository_identity, "R_repo")

    def test_a_different_upstream_issue_cannot_reuse_the_path(self) -> None:
        original = self.store.resolve_work_unit(
            work_reference(issue_identity="I_kwDO_issue_12")
        )
        with self.assertRaises(WorkUnitIdentityConflict) as caught:
            self.store.resolve_work_unit(work_reference(issue_identity="I_recreated"))
        self.assertIn("I_kwDO_issue_12", str(caught.exception))
        self.assertEqual(
            self.store.get_work_unit(original.work_unit_id).issue_identity,
            "I_kwDO_issue_12",
        )

    def test_a_different_upstream_repository_cannot_reuse_the_path(self) -> None:
        self.store.resolve_work_unit(work_reference(repository_identity="R_original"))
        with self.assertRaises(WorkUnitIdentityConflict):
            self.store.resolve_work_unit(work_reference(repository_identity="R_other"))

    def test_a_conflicting_submission_leaves_no_partial_record(self) -> None:
        work_unit = self.store.resolve_work_unit(
            work_reference(issue_identity="I_kwDO_issue_12")
        )
        submissions = self.store.submission_count(work_unit.work_unit_id)
        with self.assertRaises(WorkUnitIdentityConflict):
            self.store.resolve_work_unit(work_reference(issue_identity="I_recreated"))
        self.assertEqual(
            self.store.submission_count(work_unit.work_unit_id), submissions
        )


class IdentityStabilityTests(StoreTestCase):
    def test_direct_sql_cannot_repoint_or_delete_a_work_unit(self) -> None:
        work_unit = self.store.resolve_work_unit(work_reference())
        for statement, arguments in (
            (
                "UPDATE work_units SET issue_number = 13 WHERE work_unit_id = ?",
                (work_unit.work_unit_id,),
            ),
            (
                "UPDATE work_units SET reference_key = 'x/y#1' WHERE work_unit_id = ?",
                (work_unit.work_unit_id,),
            ),
            (
                "DELETE FROM work_units WHERE work_unit_id = ?",
                (work_unit.work_unit_id,),
            ),
        ):
            with self.subTest(statement=statement):
                with self.assertRaises(sqlite3.DatabaseError):
                    self.store.connection.execute(statement, arguments)
        self.assertEqual(
            self.store.get_work_unit(work_unit.work_unit_id).reference_key,
            "github.com/faviann/broodling#12",
        )

    def test_a_pinned_upstream_identity_cannot_be_changed_by_sql(self) -> None:
        work_unit = self.store.resolve_work_unit(
            work_reference(issue_identity="I_kwDO_issue_12")
        )
        with self.assertRaises(sqlite3.DatabaseError):
            self.store.connection.execute(
                "UPDATE work_units SET issue_identity = 'I_other' WHERE work_unit_id = ?",
                (work_unit.work_unit_id,),
            )


if __name__ == "__main__":
    unittest.main()
