"""Child process that hard-kills itself at a chosen point of a durable write.

Run as ``python tests/crash_child.py <store-path> <crash-point>``. It uses
``os._exit`` so nothing unwinds: no ``finally``, no ROLLBACK, no connection
close. Whatever survives is what SQLite actually committed.
"""

from __future__ import annotations

import os
import sqlite3
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from broodling import BroodlingStore, SourceSubmission  # noqa: E402
from support import ISSUE_BODY, criterion, work_reference  # noqa: E402
from broodling.contract import Contract, SourceAttribution  # noqa: E402

HOST_ASSUMPTIONS = ("single_host", "one_attempt_one_dedicated_worktree")


class KillingConnection:
    """Delegates to the real connection, then kills the process at ``fragment``.

    ``sqlite3.Connection`` attributes are read-only, so the store's connection is
    swapped for this proxy rather than patched in place.
    """

    def __init__(self, connection: sqlite3.Connection, fragment: str, before: bool):
        self._connection = connection
        self._fragment = fragment
        self._before = before

    def __getattr__(self, name: str):
        return getattr(self._connection, name)

    def _run(self, method, sql, arguments):
        if self._fragment not in sql:
            return method(sql, *arguments)
        if self._before:
            os._exit(97)
        method(sql, *arguments)
        os._exit(97)

    def execute(self, sql, *arguments):
        return self._run(self._connection.execute, sql, arguments)

    def executemany(self, sql, *arguments):
        return self._run(self._connection.executemany, sql, arguments)


def crash_when(store: BroodlingStore, fragment: str, *, before: bool) -> None:
    store._connection = KillingConnection(store._connection, fragment, before)


def main() -> int:
    store_path, crash_point = Path(sys.argv[1]), sys.argv[2]
    store = BroodlingStore.open(store_path)

    work_unit = store.resolve_work_unit(work_reference())
    source = store.entitle_source(
        work_unit.work_unit_id,
        SourceSubmission(
            kind="primary_issue",
            locator=work_unit.issue_locator,
            content=ISSUE_BODY,
        ),
    )
    contract = Contract(
        work_unit_id=work_unit.work_unit_id,
        source_attribution=(
            SourceAttribution(source.source_id, source.content_sha256),
        ),
        criteria=(criterion(),),
        host_assumptions=HOST_ASSUMPTIONS,
    )
    print(contract.contract_revision_id, flush=True)

    if crash_point == "mid_contract_write":
        # The revision row is inserted; die before its attribution rows commit.
        crash_when(store, "INSERT INTO contract_source_attributions", before=True)
    if crash_point == "mid_admission_write":
        # The decision row is written; die before COMMIT makes it authority.
        crash_when(store, "INSERT INTO admission_decisions", before=False)

    revision = store.record_contract_revision(contract)
    if crash_point == "after_contract_commit":
        os._exit(97)

    store.admit(revision.contract_revision_id)
    if crash_point == "after_admission_commit":
        os._exit(97)

    store.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
