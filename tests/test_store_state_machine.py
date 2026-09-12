"""Generated sequences of durable store operations checked against store invariants.

The rules are the durable operations an operator can actually repeat: resolve a
work reference, entitle a source and admit a Contract revision, admit the current
Attempt, abandon it, restart the store. After every step the machine rechecks the
durable invariants — one current Attempt at most, irreversible abandonment,
stable and non-aliasing identity, immutable records — and every rule requires a
refused operation to leave the durable record exactly as it was.

The machine keeps only facts it has already observed from the store (which
reference resolved which Work Unit, which revision/B1 pair produced which
Attempt, which Attempts it abandoned). It does not decide which operations
*ought* to be legal, so it is not a second implementation of admission: a refusal
is tolerated except where a fact already observed contradicts it.

Worktree provisioning, cessation, retirement and replacement are deliberately out
of scope here; they are host/Git/process mechanics, and their own tests hold those
guarantees with real witnesses.
"""

from __future__ import annotations

import atexit
import shutil
import tempfile
import unittest
from pathlib import Path

from hypothesis import strategies as st
from hypothesis.stateful import (
    Bundle,
    RuleBasedStateMachine,
    invariant,
    multiple,
    rule,
)
from property_support import settings  # importing loads the shared profile
from support import (
    ISSUE_BODY,
    SUPPORTED_HOST_ASSUMPTIONS,
    criterion,
    durable_test_root,
)

from broodling import (
    AttemptConflict,
    BroodlingStore,
    Contract,
    SourceAttribution,
    SourceSubmission,
    StaleAttempt,
    StartingState,
    WorkReference,
)

#: Store-side allocation only *derives* worktree paths and branch names, so
#: nothing is ever created under this root. It is a real durable directory
#: because the workspace-root policy resolves and refuses volatile roots.
WORKSPACE_ROOT = durable_test_root(prefix="broodling-machine-")
atexit.register(shutil.rmtree, WORKSPACE_ROOT, ignore_errors=True)

#: Ingress forms covering one Work Unit spelled two ways plus two neighbours that
#: must never collapse onto it.
REFERENCE_FORMS = (
    ("https://github.com/faviann/broodling", 12),
    ("git@github.com:faviann/broodling.git", "#12"),
    ("https://github.com/faviann/broodling", 13),
    ("https://gitlab.com/faviann/broodling", 12),
)

#: Two admissible criterion statements, so a Work Unit can reach a second
#: Contract revision without leaving the admissible profile.
STATEMENTS = (
    "Repeated canonical ingress resolves one Work Unit.",
    "An abandoned Attempt never regains current authority.",
)

#: B1 is recorded, not read, by admission: these are immutable commit ids the
#: store binds an Attempt to. Provisioning a worktree from a real repository is
#: the worktree tests' subject, not this machine's.
B1_REPOSITORY = "/srv/broodling/source.git"
B1_COMMITS = ("0" * 40, "1" * 40)

REASONS = ("controller lost", "stopped by operator")

#: Durable tables a refused operation must leave untouched — every row of them,
#: not merely the row count: rewriting an existing fact is exactly the partial
#: authority a refusal must not leave behind.
DURABLE_TABLES = (
    "work_units",
    "entitled_sources",
    "contract_revisions",
    "admission_decisions",
    "attempts",
    "attempt_abandonments",
    "worktree_assignments",
)


class DurableStoreMachine(RuleBasedStateMachine):
    work_units = Bundle("work_units")
    revisions = Bundle("revisions")
    attempts = Bundle("attempts")

    def __init__(self) -> None:
        super().__init__()
        self.root = Path(tempfile.mkdtemp(prefix="broodling-machine-store-"))
        self.store_path = self.root / "state" / "broodling.sqlite3"
        self.store = BroodlingStore.open(self.store_path)
        #: reference key -> the Work Unit id it resolved to.
        self.identities: dict[str, str] = {}
        #: revision id -> the canonical bytes first recorded for it.
        self.revision_bytes: dict[str, bytes] = {}
        #: (revision id, B1 commit) -> the Attempt id it admitted.
        self.admitted: dict[tuple[str, str], str] = {}
        #: attempt id -> its first abandonment record.
        self.abandonments: dict[str, object] = {}
        #: Work Units observed to carry an abandoned Attempt.
        self.abandoned_units: set[str] = set()

    def teardown(self) -> None:
        self.store.close()
        shutil.rmtree(self.root, ignore_errors=True)

    # ------------------------------------------------------------------- rules

    @rule(target=work_units, form=st.sampled_from(REFERENCE_FORMS))
    def resolve_reference(self, form) -> str:
        reference = WorkReference.parse(*form)
        record = self.store.resolve_work_unit(reference)
        known = self.identities.setdefault(reference.key, record.work_unit_id)
        assert record.work_unit_id == known, (
            f"{reference.key} resolved {record.work_unit_id} after {known}"
        )
        assert record.reference_key == reference.key
        return record.work_unit_id

    @rule(
        target=revisions,
        work_unit_id=work_units,
        statement=st.sampled_from(STATEMENTS),
    )
    def admit_contract_revision(self, work_unit_id: str, statement: str) -> str:
        work_unit = self.store.get_work_unit(work_unit_id)
        source = self.store.entitle_source(
            work_unit_id,
            SourceSubmission(
                kind="primary_issue",
                locator=work_unit.issue_locator,
                content=ISSUE_BODY,
                media_type="text/markdown; charset=utf-8",
                retrieved_at="2026-09-07T00:00:00+00:00",
            ),
        )
        revision = self.store.record_contract_revision(
            Contract(
                work_unit_id=work_unit_id,
                source_attribution=(
                    SourceAttribution(source.source_id, source.content_sha256),
                ),
                criteria=(criterion(statement=statement),),
                host_assumptions=SUPPORTED_HOST_ASSUMPTIONS,
            )
        )
        decision = self.store.admit(revision.contract_revision_id)
        assert decision.admitted, decision.findings
        recorded = self.revision_bytes.setdefault(
            revision.contract_revision_id, revision.canonical_bytes
        )
        assert revision.canonical_bytes == recorded, "a revision was rewritten"
        return revision.contract_revision_id

    @rule(
        target=attempts,
        revision_id=revisions,
        commit=st.sampled_from(B1_COMMITS),
    )
    def admit_attempt(self, revision_id: str, commit: str):
        binding = (revision_id, commit)
        work_unit_id = self.store.get_contract_revision(revision_id).work_unit_id
        before = self._durable_state()
        try:
            attempt = self.store.admit_attempt(
                revision_id,
                StartingState(B1_REPOSITORY, commit, "HEAD"),
                workspace_root=WORKSPACE_ROOT,
            )
        except (AttemptConflict, StaleAttempt) as refusal:
            self._assert_unchanged(before, refusal)
            # Repeating an admission that already succeeded must converge on the
            # Attempt it produced — unless this Work Unit has since been
            # abandoned, which withdraws ordinary admission authority outright.
            assert (
                binding not in self.admitted or work_unit_id in self.abandoned_units
            ), f"identical re-admission was refused: {refusal}"
            return multiple()
        recorded = self.admitted.setdefault(binding, attempt.attempt_id)
        assert attempt.attempt_id == recorded, (
            "the same revision and B1 admitted two Attempt identities"
        )
        assert self.store.current_attempt(work_unit_id) == attempt
        return attempt.attempt_id

    @rule(attempt_id=attempts, reason=st.sampled_from(REASONS))
    def abandon_attempt(self, attempt_id: str, reason: str) -> None:
        before = self._durable_state()
        try:
            record = self.store.abandon_attempt(attempt_id, reason)
        except StaleAttempt as refusal:
            self._assert_unchanged(before, refusal)
            assert attempt_id not in self.abandonments, (
                f"an abandoned Attempt refused to report its abandonment: {refusal}"
            )
            return
        first = self.abandonments.setdefault(attempt_id, record)
        assert record == first, "abandonment was rebound to a later reason or time"
        self.abandoned_units.add(self.store.get_attempt(attempt_id).work_unit_id)

    @rule()
    def restart_store(self) -> None:
        self.store.close()
        self.store = BroodlingStore.open(self.store_path)

    # -------------------------------------------------------------- invariants

    @invariant()
    def one_current_attempt_at_most(self) -> None:
        counts = self.store.connection.execute(
            "SELECT work_unit_id, count(*) AS current FROM attempts "
            "WHERE is_current = 1 GROUP BY work_unit_id"
        ).fetchall()
        for row in counts:
            assert row["current"] == 1, (
                f"{row['work_unit_id']} has {row['current']} current Attempts"
            )
        for work_unit_id in set(self.identities.values()):
            current = self.store.current_attempt(work_unit_id)
            if current is not None:
                assert self.store.abandonment(current.attempt_id) is None, (
                    f"{current.attempt_id} is current and abandoned"
                )

    @invariant()
    def abandonment_is_irreversible(self) -> None:
        for attempt_id, record in self.abandonments.items():
            assert self.store.abandonment(attempt_id) == record
            assert not self.store.get_attempt(attempt_id).is_current
            try:
                self.store.require_current_attempt(attempt_id)
            except StaleAttempt:
                continue
            raise AssertionError(f"{attempt_id} regained current authority")

    @invariant()
    def identity_never_aliases(self) -> None:
        for key, work_unit_id in self.identities.items():
            assert self.store.get_work_unit(work_unit_id).reference_key == key
        assert len(set(self.identities.values())) == len(self.identities), (
            f"distinct references share a Work Unit: {self.identities}"
        )

    @invariant()
    def records_stay_bound(self) -> None:
        for revision_id, canonical in self.revision_bytes.items():
            assert (
                self.store.get_contract_revision(revision_id).canonical_bytes
                == canonical
            )
        for (revision_id, commit), attempt_id in self.admitted.items():
            attempt = self.store.get_attempt(attempt_id)
            assert (attempt.contract_revision_id, attempt.b1_commit_oid) == (
                revision_id,
                commit,
            ), f"{attempt_id} was rebound"

    # ----------------------------------------------------------------- helpers

    def _assert_unchanged(self, before, refusal: Exception) -> None:
        """A refused operation must leave no partial authority behind."""

        after = self._durable_state()
        changed = [table for table in DURABLE_TABLES if after[table] != before[table]]
        assert not changed, f"refused operation wrote {changed}: {refusal}"

    def _durable_state(self) -> dict[str, tuple[tuple[object, ...], ...]]:
        """Every row of every durable table, as comparable values.

        Ordered by the first column — the identity of each of these tables — so
        two reads of unchanged state compare equal, and any inserted, deleted or
        rewritten row compares unequal.
        """

        return {
            table: tuple(
                tuple(row)
                for row in self.store.connection.execute(
                    # Fixed identifiers from DURABLE_TABLES, never caller input.
                    f"SELECT * FROM {table} ORDER BY 1"
                )
            )
            for table in DURABLE_TABLES
        }


# Long sequences matter more than many short ones here: an Attempt can only be
# re-admitted, conflicted or abandoned after several earlier steps set that up,
# and a shallower step budget left those paths largely unreached.
DurableStoreMachine.TestCase.settings = settings(
    max_examples=50, stateful_step_count=40
)
DurableStoreTest = DurableStoreMachine.TestCase


if __name__ == "__main__":
    unittest.main()
