"""Normal no-effect Work Unit completion, with no interrupted-result recovery."""

from __future__ import annotations

import asyncio
import fcntl
import hashlib
import json
import os
from contextlib import asynccontextmanager
from dataclasses import dataclass

from .abandonment import AbandonmentCoordinator
from .assurance import FinalAssuranceCoordinator, FinalAssuranceRecord
from .errors import FinalizationInterrupted, SubmissionConflict, SubmissionNotReady
from .store import BroodlingStore, _now
from .zeroshot_sdk import ZeroshotSubmitter


@dataclass(frozen=True, slots=True)
class WorkUnitDisposition:
    work_unit_id: str
    contract_revision_id: str
    attempt_id: str
    outcome: str
    completed_at: str


@asynccontextmanager
async def _sole_finalizer(store: BroodlingStore, attempt_id: str):
    """Serialize host processes and async callers outside the disposable tree.

    The durable marker distinguishes an interrupted owner after the OS releases
    its flock. Lock files are never removed, avoiding split lock ownership.
    """
    database = store.path.resolve()
    directory = database.with_name(database.name + ".finalization-locks")
    directory.mkdir(mode=0o700, exist_ok=True)
    name = hashlib.sha256(attempt_id.encode()).hexdigest() + ".lock"
    fd = os.open(directory / name, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        while True:
            try:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except BlockingIOError:
                await asyncio.sleep(0.025)
        yield
    finally:
        os.close(fd)


class WorkUnitDispositionCoordinator:
    """Retain one justified success from a fresh, normally observed P3 result."""

    def __init__(self, store: BroodlingStore, submitter: ZeroshotSubmitter):
        self.store = store
        self.assurance = FinalAssuranceCoordinator(store, submitter)
        self.abandonment = AbandonmentCoordinator(store, submitter)

    def record(self, attempt_id: str) -> WorkUnitDisposition | None:
        row = self.store.connection.execute(
            "SELECT * FROM work_unit_dispositions WHERE attempt_id = ?", (attempt_id,)
        ).fetchone()
        return None if row is None else WorkUnitDisposition(**dict(row))

    def justification(self, attempt_id: str) -> FinalAssuranceRecord | None:
        """Read all retained justification without a runtime or workspace lookup."""
        if self.record(attempt_id) is None:
            return None
        return self.assurance.record(attempt_id)

    def _require_no_effect_contract(self, attempt_id):
        attempt = self.store.require_current_attempt(attempt_id)
        revision = self.store.get_contract_revision(attempt.contract_revision_id)
        # Inspect the immutable bytes, without supplying defaults for old meaning.
        if (
            json.loads(revision.canonical_bytes).get("requiredEffects") != []
            or not self.store.is_admitted(revision.contract_revision_id)
            or revision.work_unit_id != attempt.work_unit_id
        ):
            raise SubmissionNotReady(
                "success requires the same admitted Contract's explicit empty requiredEffects"
            )
        return attempt

    async def finalize(self, attempt_id: str) -> WorkUnitDisposition:
        async with _sole_finalizer(self.store, attempt_id):
            existing = self.record(attempt_id)
            if existing is not None:
                return existing
            started = self.store.connection.execute(
                "SELECT 1 FROM attempt_finalizations WHERE attempt_id = ?",
                (attempt_id,),
            ).fetchone()
            try:
                if started is None:
                    with self.store._write() as connection:
                        self.store.require_current_attempt(attempt_id)
                        if self.assurance.record(attempt_id) is not None:
                            raise FinalizationInterrupted(
                                "prior P3 custody cannot authorize normal finalization"
                            )
                        connection.execute(
                            "INSERT INTO attempt_finalizations (attempt_id, started_at) VALUES (?, ?)",
                            (attempt_id, _now()),
                        )
                if started is not None:
                    raise FinalizationInterrupted(
                        "prior finalization ended without a complete disposition"
                    )
                self._require_no_effect_contract(attempt_id)
                custody = await self.assurance._capture_fresh(attempt_id)
                return self._commit(attempt_id, custody)
            except BaseException as error:
                # A lost acknowledgement after COMMIT never retroactively abandons
                # success. Otherwise durable ineligibility precedes external stop.
                completed = self.record(attempt_id)
                if completed is not None:
                    return completed
                reason = f"finalization unavailable ({type(error).__name__}): {error}"
                try:
                    self.store.abandon_attempt(attempt_id, reason)
                    await self.abandonment.stop(attempt_id, reason)
                except (Exception, asyncio.CancelledError) as stop_error:
                    error.add_note(f"safe cessation remains unconfirmed: {stop_error}")
                    raise error from stop_error
                raise

    def _commit(
        self, attempt_id: str, custody: FinalAssuranceRecord
    ) -> WorkUnitDisposition:
        with self.store._write() as connection:
            attempt = self._require_no_effect_contract(attempt_id)
            if (
                self.assurance.record(attempt_id) != custody
                or custody.attempt_id != attempt_id
            ):
                raise SubmissionConflict(
                    "finalization lost its immutable P3 custody binding"
                )
            connection.execute(
                "INSERT INTO work_unit_dispositions "
                "(work_unit_id, contract_revision_id, attempt_id, outcome, completed_at) "
                "VALUES (?, ?, ?, 'SUCCEEDED', ?)",
                (
                    attempt.work_unit_id,
                    attempt.contract_revision_id,
                    attempt_id,
                    _now(),
                ),
            )
            return self.record(attempt_id)
