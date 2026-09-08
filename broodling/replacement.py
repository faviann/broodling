"""Explicit fresh-B1 retry through existing allocation and submission seams."""

from __future__ import annotations

from .provisioning import AttemptProvisioner
from .store import AttemptRecord, BroodlingStore
from .submission import AttemptSubmission, SubmissionCoordinator
from .zeroshot_sdk import ZeroshotSubmitter


class RetryCoordinator:
    """One caller-requested successor; never stop, salvage or schedule old work."""

    def __init__(
        self,
        store: BroodlingStore,
        provisioner: AttemptProvisioner,
        submitter: ZeroshotSubmitter,
    ) -> None:
        if provisioner.store is not store:
            raise ValueError("retry provisioning must use the same store")
        submitter.require_assurance_profile()
        self.store = store
        self.provisioner = provisioner
        self.submitter = submitter
        self.submission = SubmissionCoordinator(store, submitter)

    def allocate(self, predecessor_id: str, retry_id: str) -> AttemptRecord:
        """Commit explicit identity, original B1 and fresh target before host work."""
        return self.store.admit_retry(
            predecessor_id,
            retry_id,
            workspace_root=self.provisioner.workspace_root,
            target=self.submitter.target,
        )

    def prepare(self, predecessor_id: str, retry_id: str) -> AttemptSubmission:
        attempt = self.allocate(predecessor_id, retry_id)
        self.store.require_current_attempt(attempt.attempt_id)
        previous = self.submission.record(attempt.attempt_id)
        if previous is not None:
            return previous
        # Provision uses only this committed Attempt's exact recorded B1.
        self.provisioner.provision(attempt.attempt_id)
        return self.submission.prepare_assurance(attempt.attempt_id)

    def retry(self, predecessor_id: str, retry_id: str) -> AttemptSubmission:
        """Continue administrative setup or correlate this same replacement run.

        A dispatched request goes straight to the existing correlation boundary;
        its graph-authorized mutation never triggers B1 restoration or a new run.
        """
        prepared = self.prepare(predecessor_id, retry_id)
        return self.submission.reconcile(prepared.attempt_id)
