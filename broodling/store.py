"""The Broodling-owned durable store.

One SQLite database, owned by Broodling and living outside disposable Attempt
worktrees, holds the four durable facts V1-P2 is responsible for:

1. stable Work Unit identity for one repository + one primary issue;
2. explicitly entitled source snapshots, with their exact bytes;
3. immutable Contract revisions; and
4. the V1 no-effect Closability/admission decision for each revision.

Each operation commits its own transaction, so a crash leaves either the whole
fact or none of it. A Contract revision on its own is not authority: only a
committed ``admitted`` decision is, which is why a half-written admission can
never read back as admitted.
"""

from __future__ import annotations

import json
import os
import sqlite3
import uuid
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from . import closability
from .contract import Contract, contract_from_mapping
from .entitlement import PRIMARY_ISSUE, SourceSubmission, evaluate_entitlement
from .errors import (
    ContractImmutabilityError,
    SchemaVersionMismatch,
    SourceAttributionError,
    SourceNotEntitled,
    StoreLocationError,
    UnknownRecord,
    WorkUnitIdentityConflict,
)
from .identity import WorkReference, digest
from .profile import assert_supported_runtime, product_configuration
from .schema import (
    DISPOSABLE_WORKTREE_MARKER,
    SCHEMA_SHA256,
    SCHEMA_SQL,
    SCHEMA_VERSION,
)

DEFAULT_STORE_FILENAME = "broodling.sqlite3"


def _now() -> str:
    return datetime.now(UTC).isoformat(timespec="microseconds")


def default_store_path(environ: dict[str, str] | None = None) -> Path:
    """Where a Broodling-owned store lives when the caller does not choose.

    Under the user's state directory, never under a repository or worktree.
    """

    env = os.environ if environ is None else environ
    explicit = env.get("BROODLING_STORE")
    if explicit:
        return Path(explicit).expanduser()
    state_home = env.get("XDG_STATE_HOME") or str(Path.home() / ".local" / "state")
    return Path(state_home).expanduser() / "broodling" / DEFAULT_STORE_FILENAME


def assert_outside_disposable_worktree(path: Path) -> None:
    """Refuse a store path inside a disposable Attempt worktree.

    An Attempt's worktree is retired wholesale when the Attempt is abandoned; the
    Contract/admission record must outlive that. V1-P2 provisions no worktrees,
    so this is a boundary check on the caller's chosen path.
    """

    resolved = path.expanduser().resolve()
    for directory in (resolved, *resolved.parents):
        if (directory / DISPOSABLE_WORKTREE_MARKER).exists():
            raise StoreLocationError(
                f"{path} is inside disposable Attempt worktree {directory}; the "
                "Broodling store must outlive any Attempt"
            )


@dataclass(frozen=True, slots=True)
class WorkUnitRecord:
    work_unit_id: str
    reference_key: str
    host: str
    owner: str
    repository: str
    issue_number: int
    issue_locator: str
    repository_identity: str | None
    issue_identity: str | None
    first_seen_at: str


@dataclass(frozen=True, slots=True)
class EntitledSourceRecord:
    source_id: str
    work_unit_id: str
    kind: str
    locator: str
    content: bytes
    content_sha256: str
    media_type: str
    entitled_by: str
    entitlement_basis: str
    retrieved_at: str
    recorded_at: str


@dataclass(frozen=True, slots=True)
class ContractRevisionRecord:
    contract_revision_id: str
    work_unit_id: str
    revision_number: int
    contract_sha256: str
    canonical_bytes: bytes
    constructed_by: str
    supersedes_revision_id: str | None
    recorded_at: str

    @property
    def contract(self) -> Contract:
        """The stored Contract, rebuilt from its canonical bytes."""

        return contract_from_mapping(json.loads(self.canonical_bytes))


@dataclass(frozen=True, slots=True)
class AdmissionDecisionRecord:
    decision_id: str
    contract_revision_id: str
    work_unit_id: str
    outcome: str
    findings: tuple[closability.ClosabilityFinding, ...]
    configuration: dict[str, Any]
    decided_at: str

    @property
    def admitted(self) -> bool:
        return self.outcome == closability.ADMITTED

    @property
    def preserved_obligations(self) -> tuple[str, ...]:
        return tuple(
            finding.preserved_obligation
            for finding in self.findings
            if finding.preserved_obligation
        )


class BroodlingStore:
    """Transactional access to the Broodling-owned durable facts."""

    def __init__(self, connection: sqlite3.Connection, path: Path) -> None:
        self._connection = connection
        self.path = path

    # ---------------------------------------------------------------- lifecycle

    @classmethod
    def open(cls, path: Path | str) -> BroodlingStore:
        """Open or create the store at ``path`` and bring the schema up."""

        assert_supported_runtime()
        target = Path(path).expanduser()
        assert_outside_disposable_worktree(target)
        target.parent.mkdir(parents=True, exist_ok=True)
        connection = sqlite3.connect(target, isolation_level=None)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA journal_mode = WAL")
        connection.execute("PRAGMA synchronous = FULL")
        store = cls(connection, target)
        store._initialize_schema()
        return store

    def close(self) -> None:
        self._connection.close()

    def __enter__(self) -> BroodlingStore:
        return self

    def __exit__(self, *_exc: object) -> None:
        self.close()

    @property
    def connection(self) -> sqlite3.Connection:
        """The live connection. Reads are free; writes still hit the triggers."""

        return self._connection

    def _initialize_schema(self) -> None:
        existing = self._connection.execute(
            "SELECT name FROM sqlite_schema WHERE type = 'table' AND name = 'schema_meta'"
        ).fetchone()
        if existing is None:
            # `executescript` cannot be nested inside a Python-managed
            # transaction, so the script opens its own; SQLite DDL is
            # transactional, which keeps initialization all-or-nothing.
            self._connection.executescript("BEGIN IMMEDIATE;\n" + SCHEMA_SQL)
            try:
                self._connection.executemany(
                    "INSERT INTO schema_meta (key, value) VALUES (?, ?)",
                    [
                        ("schema_version", str(SCHEMA_VERSION)),
                        ("schema_sha256", SCHEMA_SHA256),
                        ("initialized_at", _now()),
                    ],
                )
            except BaseException:
                self._connection.execute("ROLLBACK")
                raise
            self._connection.execute("COMMIT")
            return
        meta = self.schema_meta()
        if meta.get("schema_version") != str(SCHEMA_VERSION):
            raise SchemaVersionMismatch(
                f"store at {self.path} has schema version "
                f"{meta.get('schema_version')!r}, this build writes {SCHEMA_VERSION}"
            )
        if meta.get("schema_sha256") != SCHEMA_SHA256:
            raise SchemaVersionMismatch(
                f"store at {self.path} was initialized from a different schema "
                f"definition ({meta.get('schema_sha256')!r})"
            )

    def schema_meta(self) -> dict[str, str]:
        rows = self._connection.execute("SELECT key, value FROM schema_meta").fetchall()
        return {row["key"]: row["value"] for row in rows}

    @contextmanager
    def _write(self) -> Iterator[sqlite3.Connection]:
        self._connection.execute("BEGIN IMMEDIATE")
        try:
            yield self._connection
        except BaseException:
            self._connection.execute("ROLLBACK")
            raise
        self._connection.execute("COMMIT")

    # --------------------------------------------------------------- work units

    def resolve_work_unit(
        self, reference: WorkReference, *, expected_work_unit_id: str | None = None
    ) -> WorkUnitRecord:
        """Resolve, or create, the single Work Unit for this reference.

        Repeated canonical ingress resolves the same Work Unit. A conflicting
        identity — a different reference asserted under a known id, or a
        different pinned upstream repository/issue identity for the same
        ``owner/repo#number`` path — raises instead of aliasing or overwriting.
        """

        work_unit_id = reference.work_unit_id
        if expected_work_unit_id is not None and expected_work_unit_id != work_unit_id:
            existing = self._work_unit_row(expected_work_unit_id)
            described = (
                f"resolves {existing['reference_key']}"
                if existing is not None
                else "is unknown to this store"
            )
            raise WorkUnitIdentityConflict(
                f"submission asserts work unit {expected_work_unit_id}, which "
                f"{described}, but the submitted reference {reference.key} resolves "
                f"{work_unit_id}"
            )

        with self._write():
            row = self._work_unit_row(work_unit_id)
            if row is None:
                self._connection.execute(
                    """
                    INSERT INTO work_units (
                        work_unit_id, reference_key, host, owner, repository,
                        issue_number, issue_locator, repository_identity,
                        issue_identity, first_seen_at
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        work_unit_id,
                        reference.key,
                        reference.host,
                        reference.owner,
                        reference.repository,
                        reference.issue_number,
                        reference.issue_locator,
                        reference.repository_identity,
                        reference.issue_identity,
                        _now(),
                    ),
                )
            else:
                self._pin_upstream_identities(row, reference)
            self._record_submission(work_unit_id, reference)

        return self.get_work_unit(work_unit_id)

    def _pin_upstream_identities(
        self, row: sqlite3.Row, reference: WorkReference
    ) -> None:
        """Pin an unset upstream identity once; never change a pinned one."""

        for column, submitted in (
            ("repository_identity", reference.repository_identity),
            ("issue_identity", reference.issue_identity),
        ):
            stored = row[column]
            if submitted is None or submitted == stored:
                continue
            if stored is not None:
                raise WorkUnitIdentityConflict(
                    f"{reference.key} is already pinned to {column} {stored!r}; "
                    f"ingress presenting {submitted!r} names different upstream "
                    "material and cannot alias onto this Work Unit"
                )
            self._connection.execute(
                f"UPDATE work_units SET {column} = ? WHERE work_unit_id = ?",
                (submitted, row["work_unit_id"]),
            )

    def _record_submission(self, work_unit_id: str, reference: WorkReference) -> None:
        self._connection.execute(
            """
            INSERT INTO work_unit_submissions (
                submission_id, work_unit_id, submitted_repository, submitted_issue,
                received_at
            ) VALUES (?, ?, ?, ?, ?)
            """,
            (
                f"sub-{uuid.uuid4().hex}",
                work_unit_id,
                reference.submitted_repository,
                reference.submitted_issue,
                _now(),
            ),
        )

    def _work_unit_row(self, work_unit_id: str) -> sqlite3.Row | None:
        return self._connection.execute(
            "SELECT * FROM work_units WHERE work_unit_id = ?", (work_unit_id,)
        ).fetchone()

    def get_work_unit(self, work_unit_id: str) -> WorkUnitRecord:
        row = self._work_unit_row(work_unit_id)
        if row is None:
            raise UnknownRecord(f"unknown work unit {work_unit_id}")
        return WorkUnitRecord(
            work_unit_id=row["work_unit_id"],
            reference_key=row["reference_key"],
            host=row["host"],
            owner=row["owner"],
            repository=row["repository"],
            issue_number=row["issue_number"],
            issue_locator=row["issue_locator"],
            repository_identity=row["repository_identity"],
            issue_identity=row["issue_identity"],
            first_seen_at=row["first_seen_at"],
        )

    def submission_count(self, work_unit_id: str) -> int:
        row = self._connection.execute(
            "SELECT count(*) AS total FROM work_unit_submissions WHERE work_unit_id = ?",
            (work_unit_id,),
        ).fetchone()
        return int(row["total"])

    # ---------------------------------------------------------- entitled sources

    def entitle_source(
        self, work_unit_id: str, submission: SourceSubmission
    ) -> EntitledSourceRecord:
        """Record an entitled source snapshot with its exact bytes.

        Raises ``SourceNotEntitled`` when no entitling authority granted the
        payload. The submission's content is stored, never consulted: a payload
        that describes itself as entitled gains nothing from saying so.
        """

        work_unit = self.get_work_unit(work_unit_id)
        grant = evaluate_entitlement(submission)
        if (
            submission.kind == PRIMARY_ISSUE
            and submission.locator != work_unit.issue_locator
        ):
            raise SourceNotEntitled(
                f"primary issue source {submission.locator!r} is not this Work "
                f"Unit's primary authoritative issue {work_unit.issue_locator!r}"
            )

        source_id = "src-" + digest(
            "broodling.entitled-source.v1",
            work_unit_id,
            submission.kind,
            submission.locator,
            submission.content_sha256,
        )
        with self._write():
            self._connection.execute(
                """
                INSERT OR IGNORE INTO entitled_sources (
                    source_id, work_unit_id, kind, locator, content, content_sha256,
                    media_type, entitled_by, entitlement_basis, retrieved_at,
                    recorded_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    source_id,
                    work_unit_id,
                    submission.kind,
                    submission.locator,
                    submission.content,
                    submission.content_sha256,
                    submission.media_type,
                    grant.entitled_by,
                    grant.basis,
                    submission.retrieved_at or _now(),
                    _now(),
                ),
            )
        return self.get_entitled_source(source_id)

    def get_entitled_source(self, source_id: str) -> EntitledSourceRecord:
        row = self._connection.execute(
            "SELECT * FROM entitled_sources WHERE source_id = ?", (source_id,)
        ).fetchone()
        if row is None:
            raise UnknownRecord(f"unknown entitled source {source_id}")
        return EntitledSourceRecord(
            source_id=row["source_id"],
            work_unit_id=row["work_unit_id"],
            kind=row["kind"],
            locator=row["locator"],
            content=row["content"],
            content_sha256=row["content_sha256"],
            media_type=row["media_type"],
            entitled_by=row["entitled_by"],
            entitlement_basis=row["entitlement_basis"],
            retrieved_at=row["retrieved_at"],
            recorded_at=row["recorded_at"],
        )

    def list_entitled_sources(
        self, work_unit_id: str
    ) -> tuple[EntitledSourceRecord, ...]:
        rows = self._connection.execute(
            "SELECT source_id FROM entitled_sources WHERE work_unit_id = ? "
            "ORDER BY source_id",
            (work_unit_id,),
        ).fetchall()
        return tuple(self.get_entitled_source(row["source_id"]) for row in rows)

    # -------------------------------------------------------- contract revisions

    def record_contract_revision(self, contract: Contract) -> ContractRevisionRecord:
        """Store a Contract as an immutable revision.

        Re-recording identical meaning resolves the existing revision. Changed
        meaning becomes the next revision, superseding the previous one; the
        earlier revision's bytes are never touched.
        """

        self.get_work_unit(contract.work_unit_id)
        self._verify_source_attribution(contract)

        revision_id = contract.contract_revision_id
        existing = self._contract_row(revision_id)
        if existing is not None:
            return self.get_contract_revision(revision_id)

        canonical = contract.canonical_bytes()
        with self._write():
            previous = self._connection.execute(
                """
                SELECT contract_revision_id, revision_number FROM contract_revisions
                WHERE work_unit_id = ? ORDER BY revision_number DESC LIMIT 1
                """,
                (contract.work_unit_id,),
            ).fetchone()
            self._connection.execute(
                """
                INSERT INTO contract_revisions (
                    contract_revision_id, work_unit_id, revision_number,
                    contract_sha256, canonical_bytes, constructed_by,
                    supersedes_revision_id, recorded_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    revision_id,
                    contract.work_unit_id,
                    1 if previous is None else previous["revision_number"] + 1,
                    contract.contract_sha256,
                    canonical,
                    contract.constructed_by,
                    None if previous is None else previous["contract_revision_id"],
                    _now(),
                ),
            )
            self._connection.executemany(
                """
                INSERT INTO contract_source_attributions (
                    contract_revision_id, source_id, content_sha256
                ) VALUES (?, ?, ?)
                """,
                [
                    (revision_id, item.source_id, item.content_sha256)
                    for item in contract.source_attribution
                ],
            )
        return self.get_contract_revision(revision_id)

    def _verify_source_attribution(self, contract: Contract) -> None:
        """Every attributed source must be an entitled source of this Work Unit.

        This is where model-proposed structure stops: extraction may shape the
        criteria, but it cannot introduce material that no authority entitled.
        """

        for attribution in contract.source_attribution:
            row = self._connection.execute(
                "SELECT work_unit_id, content_sha256 FROM entitled_sources "
                "WHERE source_id = ?",
                (attribution.source_id,),
            ).fetchone()
            if row is None:
                raise SourceAttributionError(
                    f"Contract attributes {attribution.source_id}, which is not an "
                    "entitled source"
                )
            if row["work_unit_id"] != contract.work_unit_id:
                raise SourceAttributionError(
                    f"entitled source {attribution.source_id} belongs to work unit "
                    f"{row['work_unit_id']}, not {contract.work_unit_id}"
                )
            if row["content_sha256"] != attribution.content_sha256:
                raise SourceAttributionError(
                    f"Contract pins {attribution.source_id} at content "
                    f"{attribution.content_sha256}, but the entitled snapshot is "
                    f"{row['content_sha256']}"
                )

    def _contract_row(self, revision_id: str) -> sqlite3.Row | None:
        return self._connection.execute(
            "SELECT * FROM contract_revisions WHERE contract_revision_id = ?",
            (revision_id,),
        ).fetchone()

    def get_contract_revision(self, revision_id: str) -> ContractRevisionRecord:
        row = self._contract_row(revision_id)
        if row is None:
            raise UnknownRecord(f"unknown contract revision {revision_id}")
        stored = digest(
            "broodling.contract.v1", bytes(row["canonical_bytes"]).decode("utf-8")
        )
        if stored != row["contract_sha256"]:
            # The database triggers make in-place amendment abort. If the bytes
            # and the recorded digest still disagree, the revision was changed
            # around them: read loudly rather than serve altered meaning.
            raise ContractImmutabilityError(
                f"contract revision {revision_id} no longer matches its recorded "
                f"digest {row['contract_sha256']}"
            )
        return ContractRevisionRecord(
            contract_revision_id=row["contract_revision_id"],
            work_unit_id=row["work_unit_id"],
            revision_number=row["revision_number"],
            contract_sha256=row["contract_sha256"],
            canonical_bytes=row["canonical_bytes"],
            constructed_by=row["constructed_by"],
            supersedes_revision_id=row["supersedes_revision_id"],
            recorded_at=row["recorded_at"],
        )

    def list_contract_revisions(
        self, work_unit_id: str
    ) -> tuple[ContractRevisionRecord, ...]:
        rows = self._connection.execute(
            "SELECT contract_revision_id FROM contract_revisions WHERE work_unit_id = ? "
            "ORDER BY revision_number",
            (work_unit_id,),
        ).fetchall()
        return tuple(
            self.get_contract_revision(row["contract_revision_id"]) for row in rows
        )

    def contract_source_material(
        self, revision_id: str
    ) -> tuple[EntitledSourceRecord, ...]:
        """The exact admitted bytes this revision was built from."""

        rows = self._connection.execute(
            "SELECT source_id FROM contract_source_attributions "
            "WHERE contract_revision_id = ? ORDER BY source_id",
            (revision_id,),
        ).fetchall()
        return tuple(self.get_entitled_source(row["source_id"]) for row in rows)

    # ------------------------------------------------------------------ admission

    def admit(self, revision_id: str) -> AdmissionDecisionRecord:
        """Decide V1 Closability/admission for one Contract revision.

        Creates no Attempt, worktree or run: admission is the durable statement
        that this Contract *may* be executed later, nothing more. Re-deciding an
        already-decided revision returns the recorded decision rather than
        writing a second one.
        """

        revision = self.get_contract_revision(revision_id)
        existing = self._decision_row(revision_id)
        if existing is not None:
            return self.get_admission_decision(revision_id)

        assessment = closability.assess(revision.contract)
        configuration = product_configuration()
        with self._write():
            self._connection.execute(
                """
                INSERT INTO admission_decisions (
                    decision_id, contract_revision_id, work_unit_id, outcome,
                    findings_json, configuration_json, decided_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    "ad-" + digest("broodling.admission.v1", revision_id),
                    revision_id,
                    revision.work_unit_id,
                    assessment.outcome,
                    json.dumps(
                        [finding.to_mapping() for finding in assessment.findings],
                        sort_keys=True,
                        ensure_ascii=False,
                    ),
                    json.dumps(configuration, sort_keys=True, ensure_ascii=False),
                    _now(),
                ),
            )
        return self.get_admission_decision(revision_id)

    def _decision_row(self, revision_id: str) -> sqlite3.Row | None:
        return self._connection.execute(
            "SELECT * FROM admission_decisions WHERE contract_revision_id = ?",
            (revision_id,),
        ).fetchone()

    def get_admission_decision(self, revision_id: str) -> AdmissionDecisionRecord:
        row = self._decision_row(revision_id)
        if row is None:
            raise UnknownRecord(f"no admission decision for {revision_id}")
        return AdmissionDecisionRecord(
            decision_id=row["decision_id"],
            contract_revision_id=row["contract_revision_id"],
            work_unit_id=row["work_unit_id"],
            outcome=row["outcome"],
            findings=tuple(
                closability.ClosabilityFinding(
                    code=item["code"],
                    subject=item["subject"],
                    preserved_obligation=item["preservedObligation"],
                    detail=item["detail"],
                )
                for item in json.loads(row["findings_json"])
            ),
            configuration=json.loads(row["configuration_json"]),
            decided_at=row["decided_at"],
        )

    def find_admission_decision(
        self, revision_id: str
    ) -> AdmissionDecisionRecord | None:
        if self._decision_row(revision_id) is None:
            return None
        return self.get_admission_decision(revision_id)

    def is_admitted(self, revision_id: str) -> bool:
        """True only for a committed ``admitted`` decision.

        A stored Contract revision with no committed decision is not authority,
        so an interrupted admission can never read back as half-admitted.
        """

        row = self._decision_row(revision_id)
        return row is not None and row["outcome"] == closability.ADMITTED
