"""Caller-facing composition of Broodling's existing lifecycle services."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from .abandonment import AbandonmentCoordinator, AttemptRetirement
from .contract import Contract, RequiredEffect
from .disposition import WorkUnitDisposition, WorkUnitDispositionCoordinator
from .entitlement import SourceSubmission
from .errors import SubmissionNotReady
from .identity import WorkReference
from .ingress import ContractIngress, ContractProposalInput
from .provisioning import AttemptProvisioner
from .store import (
    AdmissionDecisionRecord,
    AttemptAbandonmentRecord,
    AttemptRecord,
    BroodlingStore,
    ContractRevisionRecord,
    EntitledSourceRecord,
    WorktreeAssignmentRecord,
    WorkUnitRecord,
)
from .submission import AttemptSubmission, SubmissionCoordinator
from .zeroshot_sdk import ZeroshotSubmitter


@dataclass(frozen=True, slots=True)
class InvocationStatus:
    """Existing durable facts for one exact Contract revision, not a new state."""

    work_unit: WorkUnitRecord
    sources: tuple[EntitledSourceRecord, ...]
    revision: ContractRevisionRecord
    decision: AdmissionDecisionRecord | None
    attempt: AttemptRecord | None
    worktree: WorktreeAssignmentRecord | None
    submission: AttemptSubmission | None
    abandonment: AttemptAbandonmentRecord | None
    disposition: WorkUnitDisposition | None


class Broodling:
    """One explicit work reference through receipt-backed lifecycle disposition.

    The caller owns the store's lifetime and supplies the existing supported
    Zeroshot configuration. This facade adds no execution or retry policy.
    """

    def __init__(
        self,
        store: BroodlingStore,
        submitter: ZeroshotSubmitter,
        workspace_root: Path | str,
    ) -> None:
        self.store = store
        self.submitter = submitter
        self.workspace_root = workspace_root

    def submit(
        self,
        reference: WorkReference,
        propose: Callable[[ContractProposalInput], Contract],
        *,
        repository: Path | str,
        required_effects: tuple[RequiredEffect, ...],
        revision: str = "HEAD",
        additional_sources: tuple[SourceSubmission, ...] = (),
        constructed_by: str = "model_extraction",
    ) -> InvocationStatus:
        """Capture, propose, admit, provision and dispatch one GitHub request.

        Identical source bytes and proposal resolve the same revision/Attempt.
        Use ``resume`` with its revision ID to avoid fetching or proposing again.
        Repository/revision select B1 only when first admitting an Attempt.
        """
        admitted = ContractIngress(self.store).from_github(
            reference,
            propose,
            required_effects=required_effects,
            additional_sources=additional_sources,
            constructed_by=constructed_by,
        )
        return self.resume(
            admitted.revision.contract_revision_id,
            repository=repository,
            revision=revision,
        )

    def resume(
        self,
        contract_revision_id: str,
        *,
        repository: Path | str | None = None,
        revision: str = "HEAD",
    ) -> InvocationStatus:
        """Continue stored authority without re-fetching or changing frozen B1.

        Repository/revision are required only before first Attempt admission.
        Rejection, completion and abandonment return their retained facts;
        this operation never creates a replacement Attempt.
        """
        decision = self.store.admit(contract_revision_id)
        status = self.status(contract_revision_id)
        if not decision.admitted or status.disposition or status.abandonment:
            return status
        attempt = status.attempt
        if attempt is None:
            if repository is None:
                raise SubmissionNotReady(
                    "first Attempt admission requires a repository"
                )
            attempt = AttemptProvisioner(self.store, self.workspace_root).admit(
                contract_revision_id, repository, revision
            )
        submission = SubmissionCoordinator(self.store, self.submitter)
        submitted = submission.record(attempt.attempt_id)
        if submitted is None:
            AttemptProvisioner(self.store, self.workspace_root).provision(
                attempt.attempt_id
            )
            submission.submit(attempt.attempt_id)
        else:
            # Reconcile the frozen request, never reconstruct it from current
            # execution settings. Already-correlated runs need no credentials.
            submission.reconcile(attempt.attempt_id)
        return self.status(contract_revision_id)

    def status(self, contract_revision_id: str) -> InvocationStatus:
        """Inspect pinned lineage without fetching sources or contacting Zeroshot.

        A current Attempt takes precedence over this revision's historical
        Attempts. Otherwise the last admitted Attempt supplies terminal history.
        No later Contract revision is substituted for the requested one.
        """
        # Read one consistent snapshot using the store's existing transaction
        # boundary; no external operation or mutation occurs in this block.
        with self.store._write():
            revision = self.store.get_contract_revision(contract_revision_id)
            attempts = tuple(
                attempt
                for attempt in self.store.list_attempts(revision.work_unit_id)
                if attempt.contract_revision_id == contract_revision_id
            )
            attempt = next(
                (item for item in attempts if item.is_current),
                attempts[-1] if attempts else None,
            )
            attempt_id = None if attempt is None else attempt.attempt_id
            return InvocationStatus(
                work_unit=self.store.get_work_unit(revision.work_unit_id),
                sources=self.store.contract_source_material(contract_revision_id),
                revision=revision,
                decision=self.store.find_admission_decision(contract_revision_id),
                attempt=attempt,
                worktree=(
                    self.store.worktree_assignment(attempt_id) if attempt else None
                ),
                submission=(
                    SubmissionCoordinator(self.store, self.submitter).record(attempt_id)
                    if attempt
                    else None
                ),
                abandonment=self.store.abandonment(attempt_id) if attempt else None,
                disposition=(
                    WorkUnitDispositionCoordinator(self.store, self.submitter).record(
                        attempt_id
                    )
                    if attempt
                    else None
                ),
            )

    def history(self, reference: WorkReference) -> tuple[InvocationStatus, ...]:
        """Discover retained revision handles, including after a lost response.

        Returns oldest revision first, or an empty tuple for unknown references.
        This never acquires sources, runs a proposer, or resolves a new Work Unit.
        """
        return tuple(
            self.status(revision.contract_revision_id)
            for revision in self.store.list_contract_revisions(reference.work_unit_id)
        )

    async def wait(self, attempt_id: str) -> WorkUnitDisposition:
        """Consume the bound result; cancellation detaches without abandonment.

        SUCCEEDED records authorized delivery, not semantic certification.
        Native failure retains abandonment and raises the existing domain error.
        """
        return await WorkUnitDispositionCoordinator(
            self.store, self.submitter
        ).finalize(attempt_id)

    async def stop(self, attempt_id: str, reason: str) -> AttemptRetirement:
        """Abandon and request native stop, preserving existing cleanup refusals.

        Dispatched Attempts raise CessationUnconfirmed and remain quarantined.
        This operation does not delete worktrees or authorize replacement.
        """
        return await AbandonmentCoordinator(self.store, self.submitter).stop(
            attempt_id, reason
        )
