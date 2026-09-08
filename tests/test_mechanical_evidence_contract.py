"""Runnable evidence is explicit new Contract meaning, never inferred text."""

from __future__ import annotations

import hashlib
import json
import unittest
from dataclasses import FrozenInstanceError, replace

from support import StoreTestCase, criterion

from broodling import Contract, MechanicalEvidence
from broodling.contract import contract_from_mapping, validate_mechanical_evidence


def declared_contract(declaration: MechanicalEvidence | None) -> Contract:
    return Contract("wu-fixed", (), (criterion(mechanical_evidence=declaration),))


class MechanicalDeclarationTests(unittest.TestCase):
    def test_legacy_canonical_bytes_and_meaning_are_unchanged(self) -> None:
        contract = declared_contract(None)
        # Captured from the product Contract serializer before #18's new field.
        self.assertEqual(
            hashlib.sha256(contract.canonical_bytes()).hexdigest(),
            "ff68c8b4a71376776f7def018c738599a6a715f86958645bb51e2498c3f833c7",
        )
        mapping = json.loads(contract.canonical_bytes())
        self.assertNotIn("mechanicalEvidence", mapping["criteria"][0])
        restored = contract_from_mapping(mapping)
        self.assertEqual(restored, contract)
        self.assertIsNone(restored.criteria[0].mechanical_evidence)
        with self.assertRaisesRegex(ValueError, "explicit declaration"):
            validate_mechanical_evidence(restored)

    def test_roundtrip_preserves_exact_argv_context_and_material_order(self) -> None:
        declaration = MechanicalEvidence(
            ("/usr/bin/python3", "check.py", "$(no expansion)", "a b", ""),
            cwd="checks",
            materials=("raw/result.bin", "candidate.txt"),
        )
        contract = declared_contract(declaration)
        restored = contract_from_mapping(json.loads(contract.canonical_bytes()))
        self.assertEqual(restored, contract)
        self.assertEqual(
            restored.criteria[0].mechanical_evidence.to_mapping(),
            {
                "argv": list(declaration.argv),
                "cwd": "checks",
                "materials": ["raw/result.bin", "candidate.txt"],
            },
        )
        validate_mechanical_evidence(restored)
        with self.assertRaises(FrozenInstanceError):
            declaration.cwd = "other"

    def test_every_runnable_input_change_is_a_new_revision(self) -> None:
        declaration = MechanicalEvidence(
            ("/usr/bin/true",), materials=("raw/a", "raw/b")
        )
        original = declared_contract(declaration)
        for changed in (
            replace(declaration, argv=("/usr/bin/false",)),
            replace(declaration, cwd="checks"),
            replace(declaration, materials=("raw/a",)),
            replace(declaration, materials=("raw/b", "raw/a")),
        ):
            with self.subTest(declaration=changed):
                self.assertNotEqual(
                    original.contract_revision_id,
                    declared_contract(changed).contract_revision_id,
                )

    def test_unsupported_declaration_fails_before_execution(self) -> None:
        for declaration in (
            MechanicalEvidence(()),
            MechanicalEvidence(("python", "check.py")),
            MechanicalEvidence(("./check",)),
            MechanicalEvidence(("/usr/bin/true", "bad\x00argument")),
            MechanicalEvidence(("/usr/bin/true",), cwd="../sibling"),
            MechanicalEvidence(("/usr/bin/true",), cwd="/tmp"),
            MechanicalEvidence(("/usr/bin/true",), cwd="a/../b"),
            MechanicalEvidence(("/usr/bin/true",), cwd="a//b"),
            MechanicalEvidence(("/usr/bin/true",), materials=(".",)),
            MechanicalEvidence(("/usr/bin/true",), materials=("/tmp/raw",)),
            MechanicalEvidence(("/usr/bin/true",), materials=("../raw",)),
            MechanicalEvidence(("/usr/bin/true",), materials=("raw\x00",)),
            MechanicalEvidence(["/usr/bin/true"]),
            MechanicalEvidence(("/usr/bin/true",), materials=["raw"]),
        ):
            with self.subTest(declaration=declaration), self.assertRaises(ValueError):
                validate_mechanical_evidence(declared_contract(declaration))

    def test_malformed_structured_mapping_is_not_silently_dropped(self) -> None:
        for declaration in (
            None,
            {"argv": ["/usr/bin/true"]},
            {"argv": "/usr/bin/true", "cwd": ".", "materials": []},
            {"argv": ["/usr/bin/true"], "cwd": ".", "materials": "raw"},
            {"argv": ["/usr/bin/true"], "cwd": ".", "materials": [], "shell": True},
        ):
            with self.subTest(declaration=declaration):
                mapping = declared_contract(None).to_mapping()
                mapping["criteria"][0]["mechanicalEvidence"] = declaration
                with self.assertRaises(ValueError):
                    contract_from_mapping(mapping)


class MechanicalRevisionTests(StoreTestCase):
    def test_admitted_old_revision_never_acquires_execution_semantics(self) -> None:
        _, _, contract = self.admissible_contract()
        first = self.store.record_contract_revision(contract)
        self.assertTrue(self.store.admit(first.contract_revision_id).admitted)
        changed = replace(
            contract,
            criteria=(
                replace(
                    contract.criteria[0],
                    mechanical_evidence=MechanicalEvidence(("/usr/bin/true",)),
                ),
            ),
        )
        second = self.store.record_contract_revision(changed)
        self.assertNotEqual(first.contract_revision_id, second.contract_revision_id)
        self.assertEqual(second.supersedes_revision_id, first.contract_revision_id)
        reopened = self.reopen()
        legacy = reopened.get_contract_revision(first.contract_revision_id)
        runnable = reopened.get_contract_revision(second.contract_revision_id)
        self.assertEqual(legacy.canonical_bytes, first.canonical_bytes)
        self.assertTrue(reopened.is_admitted(first.contract_revision_id))
        with self.assertRaisesRegex(ValueError, "explicit declaration"):
            validate_mechanical_evidence(legacy.contract)
        validate_mechanical_evidence(runnable.contract)
        self.assertFalse(reopened.is_admitted(second.contract_revision_id))
        self.assertTrue(reopened.admit(second.contract_revision_id).admitted)


if __name__ == "__main__":
    unittest.main()
