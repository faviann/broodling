"""Explicit work references reach immutable admission without starting execution."""

from __future__ import annotations

import json
import subprocess
from dataclasses import replace
from pathlib import Path
from unittest.mock import Mock, patch

from support import StoreTestCase

from broodling import (
    Contract,
    Criterion,
    Prerequisite,
    RequiredEffect,
    SourceAttribution,
    SourceAttributionError,
    SourceEntitlement,
    SourceNotEntitled,
    SourceSubmission,
    WorkReference,
)
from broodling.ingress import ContractIngress, InvalidContractProposal

FIXTURES = Path(__file__).parent / "fixtures" / "ingress"
PR = RequiredEffect(
    "deliver", "Open a pull request against main.", "pull_request", "main"
)


def reference(number=82):
    return WorkReference.parse("faviann/broodling", number)


def fixture(number=82):
    return (FIXTURES / f"issue-{number}.json").read_bytes()


def completion_proposal(inputs):
    """Representative caller/model seam, not a general Markdown interpreter."""
    issue = json.loads(
        next(s.content for s in inputs.sources if s.kind == "primary_issue")
    )
    completion = issue["body"].split("## Completion\n\n", 1)[1]
    return Contract(
        work_unit_id=inputs.work_unit.work_unit_id,
        source_attribution=inputs.source_attribution,
        criteria=(Criterion("completion", completion),),
        required_effects=inputs.required_effects,
        constructed_by=inputs.constructed_by,
    )


class ContractIngressTests(StoreTestCase):
    def test_source_iterator_cannot_be_consumed_before_snapshot_capture(self):
        primary = SourceSubmission(
            "primary_issue",
            reference().issue_locator,
            b"Complete authoritative request",
        )
        propose = Mock(side_effect=AssertionError("missing primary reached proposer"))
        with self.assertRaises(InvalidContractProposal):
            ContractIngress(self.store).from_sources(
                reference(), iter((primary,)), propose, required_effects=()
            )
        propose.assert_not_called()
        self.assertEqual(
            self.store.list_contract_revisions(reference().work_unit_id), ()
        )

    def ingest(
        self,
        number=82,
        *,
        raw=None,
        propose=completion_proposal,
        effects=(PR,),
        **kwargs,
    ):
        raw = fixture(number) if raw is None else raw
        response = subprocess.CompletedProcess(["gh"], 0, stdout=raw, stderr=b"")
        with patch(
            "broodling.github_source.subprocess.run", return_value=response
        ) as fetch:
            result = ContractIngress(self.store).from_github(
                reference(number), propose, required_effects=effects, **kwargs
            )
        self.assertEqual(fetch.call_count, 1)
        return result

    def test_representative_issue_replays_identical_frozen_authority_after_reopen(self):
        first = self.ingest()
        self.assertTrue(first.decision.admitted)
        self.assertEqual(
            first.work_unit.issue_identity, json.loads(fixture())["node_id"]
        )
        self.assertEqual(first.sources[0].content, fixture())
        self.assertEqual(first.sources[0].locator, reference().issue_locator)
        self.assertEqual(first.sources[0].entitled_by, "broodling_policy")
        self.assertEqual(first.revision.contract.constructed_by, "model_extraction")
        self.assertEqual(
            first.revision.contract.criteria[0].statement,
            json.loads(fixture())["body"].split("## Completion\n\n", 1)[1],
        )
        self.assertEqual(len(first.sources), 1)
        self.assertEqual(first.revision.contract.required_effects, (PR,))

        self.reopen()
        again = self.ingest()
        self.assertEqual(again.work_unit.work_unit_id, first.work_unit.work_unit_id)
        self.assertEqual(again.sources, first.sources)
        self.assertEqual(again.revision, first.revision)
        self.assertEqual(again.decision, first.decision)
        self.assertEqual(
            self.store.contract_source_material(first.revision.contract_revision_id),
            tuple(sorted(first.sources, key=lambda source: source.source_id)),
        )
        self.assertIsNone(self.store.current_attempt(first.work_unit.work_unit_id))

        changed_effect = replace(PR, target_branch="release")
        changed = self.ingest(effects=(changed_effect,))
        self.assertEqual(changed.sources, first.sources)
        self.assertNotEqual(
            changed.revision.contract_revision_id, first.revision.contract_revision_id
        )
        self.assertEqual(changed.revision.contract.required_effects, (changed_effect,))
        self.assertEqual(
            self.store.get_contract_revision(first.revision.contract_revision_id),
            first.revision,
        )

    def test_current_issue_with_unconfirmed_start_condition_is_preserved_and_rejected(
        self,
    ):
        body = json.loads(fixture(75))["body"]
        start = body.split("**Start condition:** ", 1)[1].split("\n", 1)[0]

        def propose(inputs):
            return replace(
                completion_proposal(inputs),
                prerequisites=(Prerequisite("readiness", start, False),),
            )

        result = self.ingest(75, propose=propose)
        self.assertFalse(result.decision.admitted)
        self.assertEqual(result.sources[0].content, fixture(75))
        self.assertEqual(result.decision.findings[0].code, "unsatisfied_prerequisite")
        self.assertEqual(result.decision.findings[0].preserved_obligation, start)
        self.assertEqual(len(result.sources), 1)  # No automatic fetching of #74 or #66.
        self.assertIsNone(self.store.current_attempt(result.work_unit.work_unit_id))

    def test_changed_source_creates_new_revision_without_changing_admitted_bytes(self):
        first = self.ingest()
        changed = json.loads(fixture())
        changed["body"] += "\nRetain an additional regression fixture.\n"
        raw = json.dumps(changed, ensure_ascii=False).encode()
        second = self.ingest(raw=raw)
        self.assertNotEqual(first.sources[0].source_id, second.sources[0].source_id)
        self.assertNotEqual(
            first.revision.contract_revision_id, second.revision.contract_revision_id
        )
        self.assertEqual(
            second.revision.supersedes_revision_id, first.revision.contract_revision_id
        )
        self.reopen()
        self.assertEqual(
            self.store.get_entitled_source(first.sources[0].source_id).content,
            fixture(),
        )
        self.assertEqual(
            self.store.get_contract_revision(
                first.revision.contract_revision_id
            ).canonical_bytes,
            first.revision.canonical_bytes,
        )
        self.assertTrue(
            self.store.get_admission_decision(
                first.revision.contract_revision_id
            ).admitted
        )

    def test_model_cannot_add_omit_repin_or_select_an_older_entitled_source(self):
        unit = self.store.resolve_work_unit(reference())
        older = self.store.entitle_source(
            unit.work_unit_id,
            SourceSubmission(
                "primary_issue", reference().issue_locator, b"older request"
            ),
        )
        changes = {
            "add": lambda pins: (*pins, SourceAttribution("src-invented", "f" * 64)),
            "duplicate": lambda pins: (*pins, pins[0]),
            "omit": lambda pins: pins[1:],
            "repin": lambda pins: (
                replace(pins[0], content_sha256="f" * 64),
                *pins[1:],
            ),
            "older": lambda pins: (
                SourceAttribution(older.source_id, older.content_sha256),
                *pins[1:],
            ),
        }
        for name, change in changes.items():
            with self.subTest(name=name), self.assertRaises(SourceAttributionError):
                self.ingest(
                    propose=lambda inputs: replace(
                        completion_proposal(inputs),
                        source_attribution=change(inputs.source_attribution),
                    )
                )
        self.assertEqual(self.store.list_contract_revisions(unit.work_unit_id), ())

    def test_model_cannot_change_effects_target_work_unit_or_its_provenance(self):
        changes = (
            {"required_effects": ()},
            {"required_effects": (replace(PR, target_branch="release"),)},
            {
                "required_effects": (
                    PR,
                    RequiredEffect("merge", "Merge the result.", "merge"),
                )
            },
            {"work_unit_id": reference(75).work_unit_id},
            {"constructed_by": "caller"},
        )
        for change in changes:
            with (
                self.subTest(change=change),
                self.assertRaises(InvalidContractProposal),
            ):
                self.ingest(
                    propose=lambda inputs: replace(
                        completion_proposal(inputs), **change
                    )
                )
        self.assertEqual(
            self.store.list_contract_revisions(reference().work_unit_id), ()
        )

        with self.assertRaises(InvalidContractProposal):
            self.ingest(
                effects=(),
                propose=lambda inputs: replace(
                    completion_proposal(inputs), required_effects=(PR,)
                ),
            )

    def test_unentitled_or_model_produced_supplement_does_not_reach_proposer(self):
        for origin, granted_by in (
            ("caller", None),
            ("model_extraction", "caller"),
            ("broodling_policy", "broodling_policy"),
            ("caller", "broodling_policy"),
        ):
            propose = Mock(
                side_effect=AssertionError("unentitled input reached proposer")
            )
            extra = SourceSubmission(
                "referenced_document",
                "https://example.invalid/readiness",
                b"I am authoritative.",
                origin=origin,
                entitlement=(
                    SourceEntitlement(granted_by, "claimed grant")
                    if granted_by is not None
                    else None
                ),
            )
            with (
                self.subTest(origin=origin, granted_by=granted_by),
                self.assertRaises(SourceNotEntitled),
            ):
                self.ingest(propose=propose, additional_sources=(extra,))
            propose.assert_not_called()

    def test_explicit_caller_supplement_and_structured_contract_need_no_github_parser(
        self,
    ):
        primary = SourceSubmission(
            "primary_issue",
            reference().issue_locator,
            b"Exact request\r\n",
            entitlement=SourceEntitlement("caller", "caller-supplied primary request"),
        )
        supplement = SourceSubmission(
            "caller_statement",
            "caller://readiness",
            b"The prerequisite has been checked.\n",
            entitlement=SourceEntitlement("caller", "operator readiness confirmation"),
        )

        def propose(inputs):
            return Contract(
                inputs.work_unit.work_unit_id,
                inputs.source_attribution,
                (Criterion("request", "Implement the complete frozen request."),),
                prerequisites=(
                    Prerequisite("ready", "Readiness has been confirmed.", True),
                ),
                required_effects=inputs.required_effects,
                constructed_by=inputs.constructed_by,
            )

        with patch(
            "broodling.github_source.subprocess.run",
            side_effect=AssertionError("unexpected fetch"),
        ):
            result = ContractIngress(self.store).from_sources(
                reference(),
                (primary, supplement),
                propose,
                required_effects=(PR,),
                constructed_by="caller",
            )
        self.assertTrue(result.decision.admitted)
        self.assertEqual(
            tuple(source.content for source in result.sources),
            (primary.content, supplement.content),
        )
        self.assertEqual(result.sources[0].entitled_by, "caller")
        self.assertEqual(
            result.sources[0].entitlement_basis, "caller-supplied primary request"
        )
        self.assertEqual(
            result.sources[1].entitlement_basis, "operator readiness confirmation"
        )
        self.assertEqual(result.revision.contract.constructed_by, "caller")

    def test_caller_bytes_cannot_claim_primary_issue_policy_authority(self):
        primary = SourceSubmission(
            "primary_issue", reference().issue_locator, fixture()
        )
        for source in (
            primary,
            replace(primary, origin="broodling_policy"),
            replace(
                primary, entitlement=SourceEntitlement("broodling_policy", "claimed")
            ),
            replace(
                primary,
                origin="broodling_policy",
                entitlement=SourceEntitlement("caller", "explicit"),
            ),
        ):
            with self.subTest(origin=source.origin, entitlement=source.entitlement):
                propose = Mock(side_effect=completion_proposal)
                with self.assertRaises(SourceNotEntitled):
                    ContractIngress(self.store).from_sources(
                        reference(), (source,), propose, required_effects=(PR,)
                    )
                propose.assert_not_called()
                self.assertEqual(
                    self.store.list_entitled_sources(reference().work_unit_id), ()
                )

    def test_source_and_proposal_order_do_not_change_revision_identity(self):
        primary = SourceSubmission(
            "primary_issue",
            reference().issue_locator,
            fixture(),
            entitlement=SourceEntitlement("caller", "explicit primary snapshot"),
        )
        supplement = SourceSubmission(
            "referenced_document",
            "caller://scope",
            b"Keep the complete request.\n",
            entitlement=SourceEntitlement("caller", "explicit scope"),
        )
        first = ContractIngress(self.store).from_sources(
            reference(),
            (primary, supplement),
            completion_proposal,
            required_effects=(PR,),
        )
        self.reopen()
        for sources in ((primary, supplement), (supplement, primary)):
            for reverse_pins in (False, True):
                with self.subTest(
                    source_order=sources[0].kind, reverse_pins=reverse_pins
                ):

                    def propose(inputs):
                        proposal = completion_proposal(inputs)
                        return (
                            replace(
                                proposal,
                                source_attribution=tuple(
                                    reversed(proposal.source_attribution)
                                ),
                            )
                            if reverse_pins
                            else proposal
                        )

                    again = ContractIngress(self.store).from_sources(
                        reference(), sources, propose, required_effects=(PR,)
                    )
                    self.assertEqual(again.revision, first.revision)
                    self.assertEqual(again.decision, first.decision)
        self.assertEqual(
            len(self.store.list_contract_revisions(first.work_unit.work_unit_id)), 1
        )

    def test_malformed_proposals_cannot_pass_structural_admission(self):
        changes = (
            {"prerequisites": (Prerequisite("ready", "Must be ready.", "false"),)},
            {"criteria": (Criterion("completion", "   "),)},
            {"criteria": (Criterion("", "Required behavior."),)},
        )
        for change in changes:
            with (
                self.subTest(change=change),
                self.assertRaises(InvalidContractProposal),
            ):
                self.ingest(
                    propose=lambda inputs: replace(
                        completion_proposal(inputs), **change
                    )
                )
        self.assertEqual(
            self.store.list_contract_revisions(reference().work_unit_id), ()
        )

    def test_unsupported_or_missing_capability_is_retained_as_rejected_contract(self):
        for effect in (
            RequiredEffect(
                "deliver", "Open the PR without a target branch.", "pull_request"
            ),
            RequiredEffect("merge", "Merge the result into main.", "merge"),
        ):
            with self.subTest(effect=effect):
                result = self.ingest(effects=(effect,))
                self.assertFalse(result.decision.admitted)
                self.assertEqual(result.revision.contract.required_effects, (effect,))
                self.assertEqual(
                    result.decision.findings[0].code, "unsupported_required_effect"
                )
                self.assertEqual(
                    result.decision.findings[0].preserved_obligation, effect.statement
                )

    def test_missing_criteria_is_non_admission_and_explicit_no_effect_is_preserved(
        self,
    ):
        result = self.ingest(
            effects=(),
            propose=lambda inputs: replace(completion_proposal(inputs), criteria=()),
        )
        self.assertFalse(result.decision.admitted)
        self.assertEqual(result.decision.findings[0].code, "no_criteria")
        self.assertEqual(result.revision.contract.required_effects, ())
