"""V1 Closability and admission: what is admissible, what is refused, and why."""

from __future__ import annotations

import sqlite3
import unittest

from support import ISSUE_BODY, StoreTestCase, criterion, work_reference

from broodling import (
    ADMITTED,
    REJECTED,
    Criterion,
    EvidencePopulation,
    Obligation,
    Prerequisite,
    RequiredEffect,
    SourceSubmission,
    assess,
)
from broodling import closability as codes


class ValidNoEffectAdmissionTests(StoreTestCase):
    def test_acceptance_criterion_needs_no_predeclared_validation_plan(self) -> None:
        work_unit, source = self.admitted_work_unit()
        required = Criterion("c1", "Repeated submissions retain one Work Unit.")
        contract = self.contract(work_unit, source, criteria=(required,))
        revision = self.store.record_contract_revision(contract)

        decision = self.store.admit(revision.contract_revision_id)

        self.assertEqual(decision.outcome, ADMITTED)
        self.assertEqual(decision.findings, ())
        restored = self.reopen().get_contract_revision(revision.contract_revision_id)
        self.assertEqual(restored.contract.criteria, (required,))
        self.assertEqual(restored.canonical_bytes, contract.canonical_bytes())

    def test_a_no_effect_contract_is_admitted(self) -> None:
        _, _, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        decision = self.store.admit(revision.contract_revision_id)

        self.assertEqual(decision.outcome, ADMITTED)
        self.assertTrue(decision.admitted)
        self.assertEqual(decision.findings, ())
        self.assertTrue(self.store.is_admitted(revision.contract_revision_id))

    def test_admission_records_the_product_configuration(self) -> None:
        _, _, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        configuration = self.store.admit(revision.contract_revision_id).configuration

        self.assertTrue(configuration["runtime"]["python"].startswith("3.13"))
        self.assertEqual(configuration["requiredEffects"], [])
        boundary = configuration["zeroshotBoundary"]
        self.assertEqual(boundary["engine"], "10.3.0")
        self.assertEqual(boundary["sdk"], "the-open-engine-zeroshot 10.3.0.post1")
        self.assertNotIn("gateVerdict", boundary)

    def test_admission_creates_no_attempt_worktree_or_run(self) -> None:
        _, _, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        self.store.admit(revision.contract_revision_id)

        # Only the database and its own SQLite sidecars may exist: no Attempt
        # record, no worktree, no run artefact.
        stray = [
            path
            for path in self.root.rglob("*")
            if path.is_file() and not path.name.startswith(self.store_path.name)
        ]
        self.assertEqual(stray, [])
        self.assertEqual(
            [path for path in self.root.rglob("*") if path.is_dir()],
            [self.store_path.parent],
        )

    def test_admission_survives_reopen(self) -> None:
        _, _, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        decision = self.store.admit(revision.contract_revision_id)

        reopened = self.reopen()
        restored = reopened.get_admission_decision(revision.contract_revision_id)
        self.assertEqual(restored.decision_id, decision.decision_id)
        self.assertEqual(restored.outcome, ADMITTED)
        self.assertEqual(restored.decided_at, decision.decided_at)
        self.assertTrue(reopened.is_admitted(revision.contract_revision_id))

    def test_re_deciding_returns_the_recorded_decision(self) -> None:
        _, _, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        first = self.store.admit(revision.contract_revision_id)
        again = self.store.admit(revision.contract_revision_id)

        self.assertEqual(again, first)
        rows = self.store.connection.execute(
            "SELECT count(*) AS total FROM admission_decisions"
        ).fetchone()
        self.assertEqual(rows["total"], 1)

    def test_a_decision_is_immutable(self) -> None:
        _, _, contract = self.admissible_contract()
        revision = self.store.record_contract_revision(contract)
        decision = self.store.admit(revision.contract_revision_id)
        for statement in (
            "UPDATE admission_decisions SET outcome = 'admitted' WHERE decision_id = ?",
            "DELETE FROM admission_decisions WHERE decision_id = ?",
        ):
            with self.subTest(statement=statement):
                with self.assertRaises(sqlite3.DatabaseError):
                    self.store.connection.execute(statement, (decision.decision_id,))


class EffectRejectionTests(StoreTestCase):
    def reject(self, **overrides):
        work_unit, source = self.admitted_work_unit()
        contract = self.contract(work_unit, source, **overrides)
        revision = self.store.record_contract_revision(contract)
        return revision, self.store.admit(revision.contract_revision_id)

    def test_one_exact_pull_request_delivery_is_admitted(self) -> None:
        statement = "Open a pull request against main."
        revision, decision = self.reject(
            required_effects=(
                RequiredEffect("e-pr", statement, "pull_request", "main"),
            ),
            host_assumptions=("single_host", "one_attempt_one_dedicated_worktree"),
        )
        self.assertEqual(decision.outcome, ADMITTED)
        self.assertEqual(revision.contract.required_effects[0].target_branch, "main")

    def test_pull_request_delivery_is_limited_to_github_work_units(self) -> None:
        work_unit = self.store.resolve_work_unit(
            work_reference(repository="https://gitlab.com/faviann/broodling")
        )
        source = self.store.entitle_source(
            work_unit.work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=work_unit.issue_locator,
                content=ISSUE_BODY,
                retrieved_at="2026-09-16T00:00:00+00:00",
            ),
        )
        contract = self.contract(
            work_unit,
            source,
            required_effects=(
                RequiredEffect("deliver", "Open the PR.", "pull_request", "main"),
            ),
            host_assumptions=("single_host",),
        )
        revision = self.store.record_contract_revision(contract)
        decision = self.store.admit(revision.contract_revision_id)
        self.assertEqual(decision.outcome, REJECTED)
        self.assertEqual(
            [finding.code for finding in decision.findings],
            [codes.UNSUPPORTED_DELIVERY_HOST],
        )

    def test_unsupported_or_underspecified_required_effect_is_rejected(self) -> None:
        effects = (
            ("commit_as_delivery", "Commit the fix as the delivered artifact."),
            ("push", "Push the branch to origin."),
            ("pull_request", "Open a pull request without naming its target."),
            ("merge", "Merge the pull request into main."),
            ("issue_mutation", "Close issue #12 with a summary comment."),
            ("deployment", "Deploy the built image to staging."),
            ("publication", "Publish the release notes."),
        )
        for kind, statement in effects:
            with self.subTest(kind=kind):
                _, decision = self.reject(
                    required_effects=(RequiredEffect(f"e-{kind}", statement, kind),)
                )
                self.assertEqual(decision.outcome, REJECTED)
                self.assertEqual(
                    [finding.code for finding in decision.findings],
                    [codes.UNSUPPORTED_REQUIRED_EFFECT],
                )
                self.assertEqual(decision.preserved_obligations, (statement,))

    def test_effect_dependent_evidence_is_rejected(self) -> None:
        _, decision = self.reject(
            criteria=(
                criterion(
                    statement="The published release page shows the new version.",
                    evidence_effect_dependencies=("publication",),
                ),
            )
        )
        self.assertEqual(decision.outcome, REJECTED)
        self.assertIn(
            codes.EFFECT_DEPENDENT_EVIDENCE, {f.code for f in decision.findings}
        )
        self.assertEqual(
            decision.preserved_obligations,
            ("The published release page shows the new version.",),
        )

    def test_multiple_pull_request_effects_do_not_widen_authority(self) -> None:
        _, decision = self.reject(
            required_effects=(
                RequiredEffect("one", "Open one PR.", "pull_request", "main"),
                RequiredEffect("two", "Open another PR.", "pull_request", "release"),
            ),
            host_assumptions=("single_host",),
        )
        self.assertEqual(decision.outcome, REJECTED)
        self.assertEqual(
            [finding.code for finding in decision.findings],
            [codes.UNSUPPORTED_REQUIRED_EFFECT, codes.UNSUPPORTED_REQUIRED_EFFECT],
        )

    def test_pr_authority_cannot_coexist_with_no_effect_assumptions(self) -> None:
        for assumption in ("no_authoritative_effects", "local_filesystem_only"):
            with self.subTest(assumption=assumption):
                _, decision = self.reject(
                    required_effects=(
                        RequiredEffect(
                            "deliver-pr",
                            "Open a pull request.",
                            "pull_request",
                            "main",
                        ),
                    ),
                    host_assumptions=("single_host", assumption),
                )
                self.assertEqual(decision.outcome, REJECTED)
                self.assertEqual(
                    [finding.code for finding in decision.findings],
                    [codes.UNSUPPORTED_HOST_ASSUMPTION],
                )

    def test_legacy_selected_material_request_is_not_silently_ignored(self) -> None:
        from broodling import FinalAssuranceMaterial

        _, decision = self.reject(
            final_assurance_materials=(FinalAssuranceMaterial("README.md"),)
        )
        self.assertEqual(decision.outcome, REJECTED)
        self.assertEqual(
            [finding.code for finding in decision.findings],
            [codes.UNSUPPORTED_FINAL_MATERIAL_SELECTION],
        )

    def test_external_and_publication_obligations_are_rejected(self) -> None:
        statement = "Announce the change on the project blog."
        _, decision = self.reject(
            obligations=(Obligation("o1", statement, "publication"),)
        )
        self.assertEqual(decision.outcome, REJECTED)
        self.assertEqual(
            [finding.code for finding in decision.findings],
            [codes.UNSUPPORTED_EXTERNAL_OBLIGATION],
        )
        self.assertEqual(decision.preserved_obligations, (statement,))

    def test_an_unclassified_obligation_is_not_assumed_local(self) -> None:
        _, decision = self.reject(
            obligations=(Obligation("o1", "Do the needful.", "misc"),)
        )
        self.assertEqual(
            [finding.code for finding in decision.findings],
            [codes.UNRECOGNIZED_OBLIGATION_KIND],
        )

    def test_local_obligations_are_admissible(self) -> None:
        _, decision = self.reject(
            obligations=(
                Obligation("o1", "Change the admission module.", "candidate_change"),
                Obligation("o2", "Run the unit tests.", "local_validation"),
            )
        )
        self.assertEqual(decision.outcome, ADMITTED)


class ClosabilityRejectionTests(StoreTestCase):
    def decide(self, **overrides):
        work_unit, source = self.admitted_work_unit()
        contract = self.contract(work_unit, source, **overrides)
        revision = self.store.record_contract_revision(contract)
        return self.store.admit(revision.contract_revision_id)

    def test_a_contract_with_no_criterion_is_rejected(self) -> None:
        decision = self.decide(criteria=())
        self.assertEqual(
            [finding.code for finding in decision.findings], [codes.NO_CRITERIA]
        )

    def test_an_unsatisfied_prerequisite_is_handed_back_not_awaited(self) -> None:
        statement = "The upstream schema migration in project-b must land first."
        decision = self.decide(prerequisites=(Prerequisite("p1", statement, False),))
        self.assertEqual(decision.outcome, REJECTED)
        self.assertEqual(
            [finding.code for finding in decision.findings],
            [codes.UNSATISFIED_PREREQUISITE],
        )
        self.assertEqual(decision.preserved_obligations, (statement,))

    def test_a_satisfied_prerequisite_is_admissible(self) -> None:
        decision = self.decide(
            prerequisites=(
                Prerequisite("p1", "Python 3.13 is available on the host.", True),
            )
        )
        self.assertEqual(decision.outcome, ADMITTED)


class HostProfileRejectionTests(StoreTestCase):
    def test_assumptions_outside_the_qualified_profile_are_rejected(self) -> None:
        work_unit, source = self.admitted_work_unit()
        unsupported = (
            "multi_host",
            "distributed_execution",
            "shared_worktree",
            "concurrent_external_writers",
            "cross_attempt_recovery",
            "network_access",
            "effect_capable_runtime",
        )
        for assumption in unsupported:
            with self.subTest(assumption=assumption):
                contract = self.contract(
                    work_unit, source, host_assumptions=("single_host", assumption)
                )
                revision = self.store.record_contract_revision(contract)
                decision = self.store.admit(revision.contract_revision_id)
                self.assertEqual(decision.outcome, REJECTED)
                self.assertEqual(
                    [finding.code for finding in decision.findings],
                    [codes.UNSUPPORTED_HOST_ASSUMPTION],
                )
                self.assertEqual(decision.preserved_obligations, (assumption,))


class PreservationTests(StoreTestCase):
    """Rejection must keep the obligation it refused, in the Contract and the decision."""

    def test_rejection_neither_deletes_nor_weakens_the_obligation(self) -> None:
        work_unit, source = self.admitted_work_unit()
        effect = "Merge PR #40 and close issue #12 as delivered."
        obligation = "Announce the release in the changelog."
        prerequisite = "The dependency bump must be released upstream first."
        contract = self.contract(
            work_unit,
            source,
            required_effects=(RequiredEffect("e1", effect, "merge"),),
            obligations=(Obligation("o1", obligation, "publication"),),
            prerequisites=(Prerequisite("p1", prerequisite, False),),
            host_assumptions=("single_host", "multi_host"),
            criteria=(
                criterion(
                    statement="The merged commit appears on origin/main.",
                    evidence_effect_dependencies=("push",),
                ),
            ),
        )
        revision = self.store.record_contract_revision(contract)
        decision = self.store.admit(revision.contract_revision_id)

        self.assertEqual(decision.outcome, REJECTED)
        self.assertEqual(
            {finding.code for finding in decision.findings},
            {
                codes.UNSUPPORTED_REQUIRED_EFFECT,
                codes.UNSUPPORTED_EXTERNAL_OBLIGATION,
                codes.EFFECT_DEPENDENT_EVIDENCE,
                codes.UNSATISFIED_PREREQUISITE,
                codes.UNSUPPORTED_HOST_ASSUMPTION,
            },
        )
        for statement in (effect, obligation, prerequisite):
            self.assertIn(statement, decision.preserved_obligations)

        reopened = self.reopen()
        stored = reopened.get_contract_revision(revision.contract_revision_id).contract
        self.assertEqual(stored.required_effects[0].statement, effect)
        self.assertEqual(stored.obligations[0].statement, obligation)
        self.assertEqual(stored.prerequisites[0].statement, prerequisite)
        self.assertIn("multi_host", stored.host_assumptions)
        self.assertEqual(stored.criteria[0].evidence_effect_dependencies, ("push",))
        self.assertFalse(reopened.is_admitted(revision.contract_revision_id))

    def test_a_rejected_revision_is_not_authority(self) -> None:
        work_unit, source = self.admitted_work_unit()
        contract = self.contract(
            work_unit,
            source,
            required_effects=(RequiredEffect("e1", "Push to origin.", "push"),),
        )
        revision = self.store.record_contract_revision(contract)
        self.store.admit(revision.contract_revision_id)
        self.assertFalse(self.store.is_admitted(revision.contract_revision_id))

    def test_a_narrowed_replacement_is_a_new_revision_beside_the_refused_one(
        self,
    ) -> None:
        work_unit, source = self.admitted_work_unit()
        refused = self.store.record_contract_revision(
            self.contract(
                work_unit,
                source,
                required_effects=(RequiredEffect("e1", "Push to origin.", "push"),),
            )
        )
        self.store.admit(refused.contract_revision_id)

        narrowed = self.store.record_contract_revision(self.contract(work_unit, source))
        self.store.admit(narrowed.contract_revision_id)

        self.assertEqual(narrowed.supersedes_revision_id, refused.contract_revision_id)
        self.assertTrue(self.store.is_admitted(narrowed.contract_revision_id))
        self.assertFalse(self.store.is_admitted(refused.contract_revision_id))
        self.assertEqual(
            self.store.get_contract_revision(refused.contract_revision_id)
            .contract.required_effects[0]
            .statement,
            "Push to origin.",
        )


class DeterminismTests(unittest.TestCase):
    def test_assessment_is_a_pure_function_of_the_contract(self) -> None:
        from broodling import Contract, SourceAttribution

        contract = Contract(
            work_unit_id="wu-1",
            source_attribution=(SourceAttribution("src-a", "a" * 64),),
            criteria=(
                Criterion(
                    "c1",
                    "unbounded work",
                    EvidencePopulation(kind="unbounded"),
                ),
            ),
            required_effects=(RequiredEffect("e1", "push", "push"),),
            host_assumptions=("multi_host",),
        )
        first = assess(contract, work_unit_host="github.com")
        second = assess(contract, work_unit_host="github.com")
        self.assertEqual(first, second)
        self.assertEqual(first.to_mapping(), second.to_mapping())
        self.assertFalse(first.admissible)


if __name__ == "__main__":
    unittest.main()
