"""Explicit frozen final material is exact custody selection only."""

from __future__ import annotations

import base64
import hashlib
import json
import os
import unittest
from dataclasses import FrozenInstanceError, replace

from support import StoreTestCase, criterion, git, make_repository

from broodling import Contract, FinalAssuranceMaterial, GitCommandError
from broodling.contract import contract_from_mapping, validate_final_assurance_materials
from broodling.final_material import collect_final_material


class DeclarationTests(unittest.TestCase):
    def test_legacy_bytes_remain_exact_and_new_selection_changes_revision(self) -> None:
        old = Contract("wu-fixed", (), (criterion(),))
        self.assertEqual(
            hashlib.sha256(old.canonical_bytes()).hexdigest(),
            "ff68c8b4a71376776f7def018c738599a6a715f86958645bb51e2498c3f833c7",
        )
        self.assertNotIn("finalAssuranceMaterials", old.to_mapping())
        changed = replace(
            old,
            final_assurance_materials=(
                FinalAssuranceMaterial("candidate.bin", True, True),
            ),
        )
        self.assertNotEqual(old.contract_revision_id, changed.contract_revision_id)
        self.assertEqual(
            contract_from_mapping(json.loads(changed.canonical_bytes())), changed
        )
        validate_final_assurance_materials(changed)
        with self.assertRaises(FrozenInstanceError):
            changed.final_assurance_materials[0].path = "other"
        with self.assertRaisesRegex(ValueError, "explicit declaration"):
            validate_final_assurance_materials(old)


class CollectionTests(StoreTestCase):
    def test_exact_binary_material_and_absence_survive_candidate_changes(self) -> None:
        repository = self.root / "source"
        make_repository(repository)
        (repository / "literal[ab]*.bin").write_bytes(b"\x00base\xff")
        (repository / "removed.bin").write_bytes(b"removed\x00")
        git(repository, "add", "-A")
        git(repository, "commit", "-qm", "declared B1")
        b1 = git(repository, "rev-parse", "HEAD")
        (repository / "literal[ab]*.bin").write_bytes(b"\xfffinal\x00")
        (repository / "literalab.bin").write_bytes(b"undeclared")
        (repository / "removed.bin").unlink()
        (repository / "new.bin").write_bytes(b"new\x00")
        contract = Contract(
            "wu-fixed",
            (),
            (criterion(),),
            final_assurance_materials=tuple(
                FinalAssuranceMaterial(path, True, True)
                for path in ("literal[ab]*.bin", "removed.bin", "new.bin")
            ),
        )
        actual = {
            (item["path"], item["state"]): (
                item["kind"],
                None
                if item["contentBase64"] is None
                else base64.b64decode(item["contentBase64"]),
            )
            for item in collect_final_material(contract, repository, b1)
        }
        self.assertEqual(
            actual,
            {
                ("literal[ab]*.bin", "final_candidate"): ("file", b"\xfffinal\x00"),
                ("literal[ab]*.bin", "comparison_base"): ("file", b"\x00base\xff"),
                ("removed.bin", "final_candidate"): ("absent", None),
                ("removed.bin", "comparison_base"): ("file", b"removed\x00"),
                ("new.bin", "final_candidate"): ("file", b"new\x00"),
                ("new.bin", "comparison_base"): ("absent", None),
            },
        )

    def test_symlinks_and_executable_modes_retain_exact_material(self) -> None:
        repository = self.root / "source"
        make_repository(repository)
        (repository / "tool").write_bytes(b"#!/bin/sh\n")
        (repository / "tool").chmod(0o755)
        (repository / "link").symlink_to("tool")
        git(repository, "add", "-A")
        git(repository, "commit", "-qm", "B1 modes")
        b1 = git(repository, "rev-parse", "HEAD")
        (repository / "link").unlink()
        os.symlink(b"../../outside\xff", os.fsencode(repository / "link"))
        (repository / "tool").chmod(0o644)
        contract = Contract(
            "wu-fixed",
            (),
            (criterion(),),
            final_assurance_materials=(
                FinalAssuranceMaterial("tool", True, True),
                FinalAssuranceMaterial("link", True, True),
            ),
        )
        result = collect_final_material(contract, repository, b1)
        self.assertEqual(
            [item["mode"] for item in result], ["100644", "100755", "120000", "120000"]
        )
        self.assertEqual(
            [item["kind"] for item in result], ["file", "file", "symlink", "symlink"]
        )
        self.assertEqual(
            base64.b64decode(result[2]["contentBase64"]), b"../../outside\xff"
        )
        self.assertEqual(base64.b64decode(result[3]["contentBase64"]), b"tool")

    def test_b1_is_pinned_despite_head_movement_and_git_replacement(self) -> None:
        repository = self.root / "source"
        b1 = make_repository(repository, content="real B1\n")
        (repository / "README.md").write_bytes(b"later HEAD\n")
        git(repository, "add", "-A")
        git(repository, "commit", "-qm", "later")
        later = git(repository, "rev-parse", "HEAD")
        git(repository, "replace", b1, later)
        contract = Contract(
            "wu-fixed",
            (),
            (criterion(),),
            final_assurance_materials=(
                FinalAssuranceMaterial("README.md", False, True),
            ),
        )
        result = collect_final_material(contract, repository, b1)
        self.assertEqual(len(result), 1)
        self.assertEqual(base64.b64decode(result[0]["contentBase64"]), b"real B1\n")
        for live_ref in ("HEAD", "main", b1[:8]):
            with self.subTest(live_ref=live_ref), self.assertRaises(ValueError):
                collect_final_material(contract, repository, live_ref)

    def test_symlink_ancestors_and_directories_are_refused_in_each_state(self) -> None:
        repository = self.root / "source"
        make_repository(repository)
        (repository / "directory").mkdir()
        (repository / "directory" / "file").write_bytes(b"content")
        (repository / "link").symlink_to("directory")
        git(repository, "add", "-A")
        git(repository, "commit", "-qm", "B1 links")
        b1 = git(repository, "rev-parse", "HEAD")
        for path in ("link/file", "directory", "README.md/child"):
            for final in (True, False):
                with self.subTest(path=path, final=final):
                    contract = Contract(
                        "wu-fixed",
                        (),
                        (criterion(),),
                        final_assurance_materials=(
                            FinalAssuranceMaterial(path, final, not final),
                        ),
                    )
                    with self.assertRaises((ValueError, OSError)):
                        collect_final_material(contract, repository, b1)

    def test_missing_ancestor_records_absence_but_unavailable_b1_refuses(self) -> None:
        repository = self.root / "source"
        b1 = make_repository(repository)
        contract = Contract(
            "wu-fixed",
            (),
            (criterion(),),
            final_assurance_materials=(
                FinalAssuranceMaterial("missing/leaf", True, True),
            ),
        )
        result = collect_final_material(contract, repository, b1)
        self.assertEqual([item["kind"] for item in result], ["absent", "absent"])
        with self.assertRaises(GitCommandError):
            collect_final_material(contract, repository, "0" * 40)

    def test_special_file_is_refused_without_reading_or_blocking(self) -> None:
        repository = self.root / "source"
        b1 = make_repository(repository)
        os.mkfifo(repository / "pipe")
        contract = Contract(
            "wu-fixed",
            (),
            (),
            final_assurance_materials=(FinalAssuranceMaterial("pipe"),),
        )
        with self.assertRaisesRegex(ValueError, "unsupported final material file type"):
            collect_final_material(contract, repository, b1)


class InvalidSelectionTests(unittest.TestCase):
    def test_unsupported_paths_and_states_are_not_inferred_or_normalized(self) -> None:
        selections = [None, (), [], ("candidate",)]
        selections.extend(
            (FinalAssuranceMaterial(path),)
            for path in (
                "",
                ".",
                "..",
                "../outside",
                "/absolute",
                "a/../b",
                "a//b",
                "a/./b",
                "a/",
                ".git",
                ".git/config",
                "a/.git/file",
                "nul\x00",
            )
        )
        selections.extend(
            (
                (FinalAssuranceMaterial("a"), FinalAssuranceMaterial("a", False, True)),
                (FinalAssuranceMaterial("a", False, False),),
                (FinalAssuranceMaterial("a", 1, False),),
                (FinalAssuranceMaterial("a", True, "true"),),
            )
        )
        for selection in selections:
            with self.subTest(selection=selection), self.assertRaises(ValueError):
                validate_final_assurance_materials(
                    Contract("wu", (), (), final_assurance_materials=selection)
                )

    def test_malformed_mappings_do_not_discard_undeclared_meaning(self) -> None:
        for selection in (
            None,
            {},
            [None],
            [{"path": "a"}],
            [
                {
                    "path": "a",
                    "finalCandidate": True,
                    "comparisonBase": False,
                    "extra": "authority",
                }
            ],
        ):
            mapping = Contract("wu", (), ()).to_mapping()
            mapping["finalAssuranceMaterials"] = selection
            with self.subTest(selection=selection), self.assertRaises(ValueError):
                contract_from_mapping(mapping)


class DeclarationRevisionTests(StoreTestCase):
    def test_new_material_declaration_is_a_separate_revision_requiring_admission(
        self,
    ) -> None:
        _, _, contract = self.admissible_contract()
        old = self.store.record_contract_revision(contract)
        self.assertTrue(self.store.admit(old.contract_revision_id).admitted)
        changed = self.store.record_contract_revision(
            replace(
                contract,
                final_assurance_materials=(FinalAssuranceMaterial("README.md"),),
            )
        )
        self.assertEqual(changed.supersedes_revision_id, old.contract_revision_id)
        self.assertFalse(self.store.is_admitted(changed.contract_revision_id))
        self.assertTrue(self.store.admit(changed.contract_revision_id).admitted)
        reopened = self.reopen()
        self.assertEqual(
            reopened.get_contract_revision(old.contract_revision_id).canonical_bytes,
            old.canonical_bytes,
        )
        self.assertIsNone(
            reopened.get_contract_revision(
                old.contract_revision_id
            ).contract.final_assurance_materials
        )
        self.assertEqual(
            reopened.get_contract_revision(
                changed.contract_revision_id
            ).contract.final_assurance_materials,
            (FinalAssuranceMaterial("README.md"),),
        )
