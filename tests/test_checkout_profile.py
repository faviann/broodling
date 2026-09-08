"""V1 refuses checkout conversion before any driver or provider can run."""

from pathlib import Path
from unittest.mock import patch

from retry_test_support import RetryCase
from submission_support import SubmissionCase
from support import AttemptTestCase, git

from broodling.checkout_profile import assert_supported_checkout
from broodling.errors import UnsupportedStartingState
from broodling.starting_state import resolve_starting_state


class CheckoutProfileTests(AttemptTestCase):
    def test_plain_commit_and_common_directory_are_supported_without_index_change(self):
        index = self.repository / ".git" / "index"
        before = index.read_bytes()
        assert_supported_checkout(self.repository, self.b1)
        assert_supported_checkout(self.repository / ".git", self.b1)
        self.assertEqual(index.read_bytes(), before)
        provisioned = self.provisioner().admit_and_provision(
            self.revision.contract_revision_id, self.repository
        )
        self.assertEqual(
            (provisioned.path / "README.md").read_bytes(),
            (self.repository / "README.md").read_bytes(),
        )

    def test_ambient_builtin_conversions_are_refused(self):
        attributes = self.repository / ".git" / "info" / "attributes"
        for attribute in (
            "text",
            "eol=crlf",
            "ident",
            "working-tree-encoding=UTF-16",
            "crlf",
            "filter=unknown",
        ):
            with self.subTest(attribute=attribute):
                attributes.write_text(f"README.md {attribute}\n")
                with self.assertRaisesRegex(UnsupportedStartingState, "attribute"):
                    assert_supported_checkout(self.repository, self.b1)

    def test_builtin_conversion_configuration_is_refused(self):
        for key, value in (
            ("core.autocrlf", "true"),
            ("core.eol", "crlf"),
            ("core.symlinks", "false"),
            ("core.sparseCheckout", "true"),
        ):
            with self.subTest(key=key):
                git(self.repository, "config", key, value)
                with self.assertRaisesRegex(UnsupportedStartingState, "configuration"):
                    assert_supported_checkout(self.repository, self.b1)
                git(self.repository, "config", "--unset", key)

    def test_pinned_nested_attributes_are_checked_after_live_head_removes_them(self):
        nested = self.repository / "nested"
        nested.mkdir()
        (nested / "file.txt").write_text("$Id$\n")
        (nested / ".gitattributes").write_text("*.txt ident\n")
        git(self.repository, "add", ".")
        git(self.repository, "commit", "-qm", "pinned ident")
        pinned = git(self.repository, "rev-parse", "HEAD")
        (nested / ".gitattributes").unlink()
        git(self.repository, "add", ".")
        git(self.repository, "commit", "-qm", "live attributes removed")
        index = (self.repository / ".git" / "index").read_bytes()
        with self.assertRaisesRegex(UnsupportedStartingState, "ident"):
            assert_supported_checkout(self.repository / ".git", pinned)
        assert_supported_checkout(
            self.repository, git(self.repository, "rev-parse", "HEAD")
        )
        self.assertEqual((self.repository / ".git" / "index").read_bytes(), index)

    def test_global_attributes_and_included_filter_configuration_are_checked(self):
        attributes = self.root / "global-attributes"
        attributes.write_text("README.md working-tree-encoding=UTF-16\n")
        git(self.repository, "config", "core.attributesFile", str(attributes))
        with self.assertRaisesRegex(UnsupportedStartingState, "working-tree-encoding"):
            assert_supported_checkout(self.repository, self.b1)
        attributes.write_text("")
        included = self.root / "filter-config"
        included.write_text('[filter "ambient"]\n\tprocess = executable-not-run\n')
        git(self.repository, "config", "include.path", str(included))
        with self.assertRaisesRegex(UnsupportedStartingState, "filter.ambient.process"):
            assert_supported_checkout(self.repository, self.b1)

    def test_inactive_branch_conditional_configuration_is_refused_before_checkout(self):
        included = self.root / "only-new-worktrees"
        included.write_text('[filter "late"]\n\tsmudge = executable-not-run\n')
        git(
            self.repository,
            "config",
            "includeIf.onbranch:broodling/**.path",
            str(included),
        )
        with self.assertRaisesRegex(UnsupportedStartingState, "includeif"):
            assert_supported_checkout(self.repository, self.b1)

    def test_bare_source_attributes_are_checked(self):
        bare = self.root / "bare.git"
        git(self.repository, "clone", "--bare", str(self.repository), str(bare))
        assert_supported_checkout(bare, self.b1)
        (bare / "info" / "attributes").write_text("README.md ident\n")
        with self.assertRaisesRegex(UnsupportedStartingState, "ident"):
            assert_supported_checkout(bare, self.b1)

    def test_admission_refuses_before_status_can_execute_a_clean_filter(self):
        marker = self.root / "clean-executed"
        (self.repository / ".git" / "info" / "attributes").write_text(
            "README.md filter=canary\n"
        )
        git(self.repository, "config", "filter.canary.clean", f"touch '{marker}'; cat")
        (self.repository / "README.md").write_text("force clean conversion\n")
        with self.assertRaisesRegex(UnsupportedStartingState, "filter.canary.clean"):
            resolve_starting_state(self.repository)
        self.assertFalse(marker.exists())


class RetryCheckoutRestrictionTests(RetryCase):
    def test_smudge_clean_canary_refuses_before_checkout_and_dispatch(self):
        marker = self.root / "filter-executed"
        info = Path(self.attempt.b1_repository) / "info" / "attributes"
        info.write_text("README.md filter=reviewcarry\n")
        git(
            self.repository,
            "config",
            "filter.reviewcarry.smudge",
            f"touch '{marker}'; cat; printf 'ABANDONED_SMUDGE_CANARY\\n'",
        )
        git(
            self.repository,
            "config",
            "filter.reviewcarry.clean",
            "sed '/ABANDONED_SMUDGE_CANARY/d'",
        )
        with patch.object(self.new_adapter, "submit") as submit:
            with self.assertRaises(UnsupportedStartingState):
                self.retry_coordinator().retry(
                    self.attempt_id, "checkout-canary-refusal"
                )
            submit.assert_not_called()
        self.assertFalse(marker.exists())
        current = self.store.current_attempt(self.attempt.work_unit_id)
        if current is not None:
            candidate = self.store.worktree_assignment(current.attempt_id).path
            self.assertFalse((candidate / "README.md").exists())


class SubmissionCheckoutRestrictionTests(SubmissionCase):
    def inject_conversion(self):
        marker = self.root / "submission-filter-executed"
        (Path(self.attempt.b1_repository) / "info" / "attributes").write_text(
            "README.md filter=reviewcarry\n"
        )
        git(
            self.repository,
            "config",
            "filter.reviewcarry.smudge",
            f"touch '{marker}'; cat; printf 'ABANDONED_SMUDGE_CANARY\\n'",
        )
        git(
            self.repository,
            "config",
            "filter.reviewcarry.clean",
            f"touch '{marker}'; sed '/ABANDONED_SMUDGE_CANARY/d'",
        )
        # Make status need its clean conversion if the restriction is misplaced.
        (self.path / "README.md").write_text(
            "original admitted state\nABANDONED_SMUDGE_CANARY\n"
        )
        return marker

    def test_conversion_added_after_provision_refuses_before_preparation(self):
        marker = self.inject_conversion()
        with patch.object(self.adapter, "submit") as submit:
            with self.assertRaisesRegex(UnsupportedStartingState, "filter.reviewcarry"):
                self.submit()
            submit.assert_not_called()
        self.assertFalse(marker.exists())
        self.assertIsNone(self.coordinator.record(self.attempt_id))

    def test_conversion_added_after_preparation_refuses_before_first_dispatch(self):
        prepared = self.prepare()
        marker = self.inject_conversion()
        self.restart()
        with patch.object(self.adapter, "submit") as submit:
            with self.assertRaisesRegex(UnsupportedStartingState, "filter.reviewcarry"):
                self.coordinator.reconcile(self.attempt_id)
            submit.assert_not_called()
        self.assertFalse(marker.exists())
        self.assertEqual(self.coordinator.record(self.attempt_id), prepared)
