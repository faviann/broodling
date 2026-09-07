"""Stable Work Unit identity: repeated ingress resolves one unit, conflicts do not alias."""

from __future__ import annotations

import sqlite3
import unittest

from broodling import InvalidWorkReference, WorkReference, WorkUnitIdentityConflict
from support import ISSUE, REPOSITORY, StoreTestCase, work_reference


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


class DistinctIdentityTests(StoreTestCase):
    DISTINCT = (
        ("https://gitlab.com/faviann/broodling", ISSUE),
        ("https://github.com/faviann/broodling-fork", ISSUE),
        ("https://github.com/someone-else/broodling", ISSUE),
        (REPOSITORY, 13),
    )

    def test_a_different_reference_never_aliases_onto_an_existing_unit(self) -> None:
        original = self.store.resolve_work_unit(work_reference())
        for repository, issue in self.DISTINCT:
            other = self.store.resolve_work_unit(WorkReference.parse(repository, issue))
            self.assertNotEqual(other.work_unit_id, original.work_unit_id)
        self.assertEqual(
            self.store.get_work_unit(original.work_unit_id).reference_key,
            "github.com/faviann/broodling#12",
        )

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

    def test_repository_and_issue_locators_must_agree(self) -> None:
        with self.assertRaises(InvalidWorkReference):
            WorkReference.parse(
                REPOSITORY, "https://github.com/someone-else/broodling/issues/12"
            )

    def test_unusable_references_are_refused(self) -> None:
        for repository, issue in (
            ("", 1),
            ("https://github.com/faviann", 1),
            ("https://github.com/a/b/c", 1),
            ("ftp://github.com/a/b", 1),
            (REPOSITORY, 0),
            (REPOSITORY, "not-a-number"),
            (REPOSITORY, None),
        ):
            with self.subTest(repository=repository, issue=issue):
                with self.assertRaises(InvalidWorkReference):
                    WorkReference.parse(repository, issue)


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
