"""Explicit retry races use independent stores and controlled host barriers."""

import asyncio
from concurrent.futures import ThreadPoolExecutor
from threading import Event
from unittest.mock import patch

from retry_test_support import RetryCase
from submission_support import SubmissionCase

from broodling import (
    AbandonmentCoordinator,
    AttemptProvisioner,
    BroodlingStore,
    RetryCoordinator,
)
from broodling.errors import AttemptAdmissionError, StaleAttempt


class RetryRaceTests(RetryCase):
    def setUp(self):
        # Retain RetryCase's Contract/profile helpers, but start with live A1.
        SubmissionCase.setUp(self)
        self.executable = self.root / "controlled-codex"
        self.executable.write_text("#!/bin/sh\nprintf 'codex-cli 0.153.4\\n'\n")
        self.executable.chmod(0o755)
        self.new_adapter = self.fresh_adapter("race-replacement")

    def allocate_independently(self, entered=None):
        with BroodlingStore.open(self.store_path) as store:
            if entered:
                entered.set()
            return RetryCoordinator(
                store, AttemptProvisioner(store, self.workspace_root), self.new_adapter
            ).allocate(self.attempt_id, "race-retry")

    def stop_independently(self, entered=None):
        with BroodlingStore.open(self.store_path) as store:
            if entered:
                entered.set()
            return asyncio.run(
                AbandonmentCoordinator(store, self.adapter).stop(
                    self.attempt_id, "race stop"
                )
            )

    def retire_independently(self):
        with BroodlingStore.open(self.store_path) as store:
            return AbandonmentCoordinator(store, self.adapter).retire(self.attempt_id)

    def assert_one_replacement(self):
        with ThreadPoolExecutor(max_workers=2) as pool:
            first = pool.submit(self.allocate_independently)
            second = pool.submit(self.allocate_independently)
            self.assertEqual(first.result(timeout=15), second.result(timeout=15))
            replacement = first.result()
        self.assertNotEqual(replacement.attempt_id, self.attempt_id)
        self.assertEqual(
            self.store.current_attempt(self.attempt.work_unit_id).attempt_id,
            replacement.attempt_id,
        )
        self.assertEqual(
            self.store.connection.execute("SELECT count(*) FROM attempts").fetchone()[
                0
            ],
            2,
        )
        self.assertFalse(self.path.exists())
        return replacement

    def test_retry_is_refused_while_stop_has_not_established_cessation(self):
        from broodling import containment

        entered, release = Event(), Event()
        original = containment.confirm_ceased

        def held_confirmation(path):
            entered.set()
            if not release.wait(10):
                raise TimeoutError("stop barrier not released")
            return original(path)

        with (
            ThreadPoolExecutor(max_workers=2) as pool,
            patch.object(containment, "confirm_ceased", held_confirmation),
        ):
            stopped = pool.submit(self.stop_independently)
            try:
                self.assertTrue(entered.wait(10))
                self.assertIsNotNone(self.store.abandonment(self.attempt_id))
                retry = pool.submit(self.allocate_independently)
                with self.assertRaises(AttemptAdmissionError):
                    retry.result(timeout=10)
                self.assertIsNone(self.store.retry("race-retry"))
                self.assertIsNone(
                    AbandonmentCoordinator(self.store, self.adapter).record(
                        self.attempt_id
                    )
                )
            finally:
                release.set()
            stopped.result(timeout=10)
        self.retire_independently()
        self.assert_one_replacement()

    def test_retry_waits_for_unfinished_owned_retirement(self):
        self.stop_independently()
        entered, release, retry_entered = Event(), Event(), Event()
        original = AbandonmentCoordinator._remove_owned

        def held_removal(admin, *args):
            entered.set()
            if not release.wait(10):
                raise TimeoutError("retirement barrier not released")
            return original(admin, *args)

        with (
            ThreadPoolExecutor(max_workers=2) as pool,
            patch.object(AbandonmentCoordinator, "_remove_owned", held_removal),
        ):
            retired = pool.submit(self.retire_independently)
            try:
                self.assertTrue(entered.wait(10))
                retry = pool.submit(self.allocate_independently, retry_entered)
                self.assertTrue(retry_entered.wait(10))
                with self.assertRaises(TimeoutError):
                    retry.result(timeout=0.05)
                self.assertIsNone(self.store.retry("race-retry"))
                self.assertIsNone(
                    AbandonmentCoordinator(self.store, self.adapter)
                    .record(self.attempt_id)
                    .retired_at
                )
            finally:
                release.set()
            retired.result(timeout=10)
            retry.result(timeout=10)
        self.assert_one_replacement()

    def test_active_ordinary_provisioning_and_ingress_cannot_bypass_retry_safety(self):
        entered, release, retry_entered, stop_entered = (
            Event(),
            Event(),
            Event(),
            Event(),
        )

        def provision_old():
            with BroodlingStore.open(self.store_path) as store:
                provisioner = AttemptProvisioner(store, self.workspace_root)
                original = provisioner._materialize

                def held_materialization(*args):
                    entered.set()
                    if not release.wait(10):
                        raise TimeoutError("provisioning barrier not released")
                    return original(*args)

                provisioner._materialize = held_materialization
                return provisioner.admit_and_provision(
                    self.attempt.contract_revision_id, self.repository
                )

        with ThreadPoolExecutor(max_workers=3) as pool:
            provisioned = pool.submit(provision_old)
            try:
                self.assertTrue(entered.wait(10))
                retry = pool.submit(self.allocate_independently, retry_entered)
                stopped = pool.submit(self.stop_independently, stop_entered)
                self.assertTrue(retry_entered.wait(10))
                self.assertTrue(stop_entered.wait(10))
                with self.assertRaises(TimeoutError):
                    retry.result(timeout=0.05)
                with self.assertRaises(TimeoutError):
                    stopped.result(timeout=0.05)
                self.assertIsNone(self.store.retry("race-retry"))
            finally:
                release.set()
            self.assertEqual(
                provisioned.result(timeout=10).attempt.attempt_id, self.attempt_id
            )
            with self.assertRaises(AttemptAdmissionError):
                retry.result(timeout=10)
            stopped.result(timeout=10)
        self.retire_independently()
        replacement = self.assert_one_replacement()
        with ThreadPoolExecutor(max_workers=3) as pool:

            def stale_provision():
                with BroodlingStore.open(self.store_path) as store:
                    return AttemptProvisioner(store, self.workspace_root).provision(
                        self.attempt_id
                    )

            def old_ingress():
                with BroodlingStore.open(self.store_path) as store:
                    return AttemptProvisioner(store, self.workspace_root).admit(
                        self.attempt.contract_revision_id, self.repository
                    )

            old_tree = pool.submit(stale_provision)
            ingress = pool.submit(old_ingress)
            stop = pool.submit(self.stop_independently)
            with self.assertRaises(StaleAttempt):
                old_tree.result(timeout=10)
            with self.assertRaises(StaleAttempt):
                ingress.result(timeout=10)
            stop.result(timeout=10)
        self.assertEqual(
            self.store.current_attempt(self.attempt.work_unit_id).attempt_id,
            replacement.attempt_id,
        )
        self.assertIsNone(self.store.abandonment(replacement.attempt_id))
        self.assertFalse(self.path.exists())
