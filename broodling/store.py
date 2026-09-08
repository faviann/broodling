"""The Broodling-owned durable store.

One SQLite database, owned by Broodling and living outside disposable Attempt
worktrees, holds the durable facts V1-P2 is responsible for:

1. stable Work Unit identity for one repository + one primary issue;
2. explicitly entitled source snapshots, with their exact bytes;
3. immutable Contract revisions;
4. the V1 no-effect Closability/admission decision for each revision; and
5. the immutable Attempt — one admitted Contract revision, one B1, one
   exclusively owned worktree path and branch.

Each operation commits its own transaction, so a crash leaves either the whole
fact or none of it. A Contract revision on its own is not authority: only a
committed ``admitted`` decision is, which is why a half-written admission can
never read back as admitted. The same rule shapes Attempt admission: the Attempt
and its worktree allocation commit together, before any host-side directory
exists, so a crash during provisioning leaves exactly one recoverable Attempt
identity to converge on rather than an orphaned directory nobody owns.
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
    AttemptAdmissionError,
    AttemptConflict,
    ContractImmutabilityError,
    SchemaVersionMismatch,
    SourceAttributionError,
    SourceNotEntitled,
    UnknownRecord,
    WorktreeOwnershipConflict,
    WorkUnitIdentityConflict,
)
from .identity import WorkReference, digest
from .profile import assert_supported_runtime, product_configuration
from .schema import (
    ASSURANCE_SQL,
    SCHEMA_SHA256,
    SCHEMA_SQL,
    SCHEMA_VERSION,
    SUBMISSION_SQL,
    V2_SCHEMA_SHA256,
    V3_SCHEMA_SHA256,
)
from .starting_state import StartingState, admitted_material_digest
from .workspace import (
    WorktreeAllocation,
    allocate,
    assert_durable_workspace_root,
    assert_outside_disposable_worktree,
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


@dataclass(frozen=True, slots=True)
class AttemptRecord:
    """One immutable Attempt: one Contract revision, one B1, one worktree.

    ``b1_repository`` is the repository's shared Git directory, ``b1_commit_oid``
    the exact immutable commit, and ``b1_material_sha256`` the fingerprint of the
    frozen admitted instruction/source bytes the Contract pins. Together they are
    B1; none of them follows a later ``HEAD``, branch tip or issue edit.
    """

    attempt_id: str
    work_unit_id: str
    contract_revision_id: str
    is_current: bool
    b1_repository: str
    b1_commit_oid: str
    b1_material_sha256: str
    b1_requested_revision: str
    admitted_at: str


@dataclass(frozen=True, slots=True)
class WorktreeAssignmentRecord:
    """Durable, queryable ownership of one worktree path and branch.

    ``state`` is the whole pre-run lifecycle: ``allocated`` means the identity is
    reserved but the host directory may or may not exist yet; ``provisioned``
    means a dedicated attached worktree at B1 was created and acknowledged.
    """

    attempt_id: str
    work_unit_id: str
    repository: str
    worktree_path: str
    branch: str
    state: str
    allocated_at: str
    provisioned_at: str | None

    ALLOCATED = "allocated"
    PROVISIONED = "provisioned"

    @property
    def provisioned(self) -> bool:
        return self.state == self.PROVISIONED

    @property
    def path(self) -> Path:
        return Path(self.worktree_path)


def derive_attempt_id(
    contract_revision_id: str, starting_state: StartingState, material_sha256: str
) -> str:
    """The durable Attempt id for one revision admitted at one B1.

    Derived rather than allocated, so a repeated admission of the *same* Contract
    revision at the *same* B1 resolves the Attempt that already exists instead of
    minting a rival one. A different revision or a different B1 derives a
    different id, which the one-current constraint then refuses.
    """

    return "at-" + digest(
        "broodling.attempt.v1",
        contract_revision_id,
        starting_state.repository,
        starting_state.commit_oid,
        material_sha256,
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
        # Two processes admitting the same Work Unit must serialize on the
        # write lock and let the loser observe the winner's committed row, not
        # fail with "database is locked" and retry into an unclear state.
        connection.execute("PRAGMA busy_timeout = 10000")
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
        migrations = {
            "2": (V2_SCHEMA_SHA256, SUBMISSION_SQL + ASSURANCE_SQL),
            "3": (V3_SCHEMA_SHA256, ASSURANCE_SQL),
        }
        if meta.get("schema_version") in migrations:
            # Acquire before rereading: concurrent openers must migrate once.
            with self._write() as connection:
                current = self.schema_meta()
                migration = migrations.get(current.get("schema_version"))
                if (
                    migration is not None
                    and current.get("schema_sha256") == migration[0]
                ):
                    statement = ""
                    for line in migration[1].splitlines(keepends=True):
                        statement += line
                        if sqlite3.complete_statement(statement):
                            connection.execute(statement)
                            statement = ""
                    connection.executemany(
                        "UPDATE schema_meta SET value = ? WHERE key = ?",
                        [
                            (str(SCHEMA_VERSION), "schema_version"),
                            (SCHEMA_SHA256, "schema_sha256"),
                        ],
                    )
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

    # ------------------------------------------------------------------- attempts

    def admit_attempt(
        self,
        revision_id: str,
        starting_state: StartingState,
        *,
        workspace_root: Path | str,
    ) -> AttemptRecord:
        """Admit the current Attempt for an admitted Contract revision.

        Allocates the Attempt identity, pins B1, and reserves a unique worktree
        path and branch — all in one transaction, before any host-side directory
        exists. Nothing is provisioned here.

        Repeating the identical request returns the same Attempt, with the
        allocation it already owns — a later call naming a different workspace
        root does not move a worktree somebody may already be working in. A
        request that differs in Contract revision or B1 conflicts rather than
        creating a second current authority: replacing an Attempt is
        abandon/restart, which is later V1-P4 work.
        """

        revision = self.get_contract_revision(revision_id)
        if not self.is_admitted(revision_id):
            raise AttemptAdmissionError(
                f"contract revision {revision_id} has no committed admitted "
                "decision; an Attempt may only be admitted for an admitted "
                "Contract revision"
            )
        root = assert_durable_workspace_root(Path(workspace_root))
        work_unit = self.get_work_unit(revision.work_unit_id)
        material = admitted_material_digest(
            (item.source_id, item.content_sha256)
            for item in revision.contract.source_attribution
        )
        attempt_id = derive_attempt_id(revision_id, starting_state, material)
        allocation = allocate(
            root,
            owner=work_unit.owner,
            repository=work_unit.repository,
            issue_number=work_unit.issue_number,
            attempt_id=attempt_id,
        )

        with self._write():
            if self._attempt_row(attempt_id) is None:
                self._open_attempt(
                    attempt_id,
                    work_unit.work_unit_id,
                    revision_id,
                    starting_state,
                    material,
                )
                self._claim_worktree(
                    attempt_id, work_unit.work_unit_id, starting_state, allocation
                )
        return self.get_attempt(attempt_id)

    def _open_attempt(
        self,
        attempt_id: str,
        work_unit_id: str,
        revision_id: str,
        starting_state: StartingState,
        material: str,
    ) -> None:
        """Insert the Attempt, or refuse because this Work Unit already has one.

        The partial unique index is the enforcement — it holds against a racing
        process too. Reading the competitor out when it fires turns the loser's
        constraint error into a statement of which Attempt already holds the
        Work Unit.
        """

        try:
            self._connection.execute(
                """
                INSERT INTO attempts (
                    attempt_id, work_unit_id, contract_revision_id, is_current,
                    b1_repository, b1_commit_oid, b1_material_sha256,
                    b1_requested_revision, admitted_at
                ) VALUES (?, ?, ?, 1, ?, ?, ?, ?, ?)
                """,
                (
                    attempt_id,
                    work_unit_id,
                    revision_id,
                    starting_state.repository,
                    starting_state.commit_oid,
                    material,
                    starting_state.requested_revision,
                    _now(),
                ),
            )
        except sqlite3.IntegrityError as error:
            raise self._competing_attempt(work_unit_id) from error

    def _competing_attempt(self, work_unit_id: str) -> AttemptConflict:
        current = self._connection.execute(
            "SELECT attempt_id, contract_revision_id, b1_commit_oid FROM attempts "
            "WHERE work_unit_id = ? AND is_current = 1",
            (work_unit_id,),
        ).fetchone()
        if current is None:
            return AttemptConflict(
                f"work unit {work_unit_id} cannot admit this Attempt"
            )
        return AttemptConflict(
            f"work unit {work_unit_id} already has current attempt "
            f"{current['attempt_id']} on contract revision "
            f"{current['contract_revision_id']} at B1 {current['b1_commit_oid']}; "
            "a differing admission request cannot create a second current Attempt"
        )

    def _claim_worktree(
        self,
        attempt_id: str,
        work_unit_id: str,
        starting_state: StartingState,
        allocation: WorktreeAllocation,
    ) -> None:
        try:
            self._connection.execute(
                """
                INSERT INTO worktree_assignments (
                    attempt_id, work_unit_id, repository, worktree_path, branch,
                    state, allocated_at, provisioned_at
                ) VALUES (?, ?, ?, ?, ?, 'allocated', ?, NULL)
                """,
                (
                    attempt_id,
                    work_unit_id,
                    starting_state.repository,
                    str(allocation.worktree_path),
                    allocation.branch,
                    _now(),
                ),
            )
        except sqlite3.IntegrityError as error:
            owner = self.worktree_owner(allocation.worktree_path)
            claimed = (
                f"attempt {owner.attempt_id} of work unit {owner.work_unit_id}"
                if owner is not None
                else "another attempt"
            )
            raise WorktreeOwnershipConflict(
                f"worktree {allocation.worktree_path} on branch "
                f"{allocation.branch} is already owned by {claimed}"
            ) from error

    def _attempt_row(self, attempt_id: str) -> sqlite3.Row | None:
        return self._connection.execute(
            "SELECT * FROM attempts WHERE attempt_id = ?", (attempt_id,)
        ).fetchone()

    def get_attempt(self, attempt_id: str) -> AttemptRecord:
        row = self._attempt_row(attempt_id)
        if row is None:
            raise UnknownRecord(f"unknown attempt {attempt_id}")
        return self._attempt(row)

    def find_attempt(self, attempt_id: str) -> AttemptRecord | None:
        row = self._attempt_row(attempt_id)
        return None if row is None else self._attempt(row)

    def current_attempt(self, work_unit_id: str) -> AttemptRecord | None:
        """The single current Attempt of a Work Unit, if one was admitted."""

        row = self._connection.execute(
            "SELECT * FROM attempts WHERE work_unit_id = ? AND is_current = 1",
            (work_unit_id,),
        ).fetchone()
        return None if row is None else self._attempt(row)

    @staticmethod
    def _attempt(row: sqlite3.Row) -> AttemptRecord:
        return AttemptRecord(
            attempt_id=row["attempt_id"],
            work_unit_id=row["work_unit_id"],
            contract_revision_id=row["contract_revision_id"],
            is_current=bool(row["is_current"]),
            b1_repository=row["b1_repository"],
            b1_commit_oid=row["b1_commit_oid"],
            b1_material_sha256=row["b1_material_sha256"],
            b1_requested_revision=row["b1_requested_revision"],
            admitted_at=row["admitted_at"],
        )

    # --------------------------------------------------------- worktree ownership

    def _assignment_row(self, attempt_id: str) -> sqlite3.Row | None:
        return self._connection.execute(
            "SELECT * FROM worktree_assignments WHERE attempt_id = ?", (attempt_id,)
        ).fetchone()

    def worktree_assignment(self, attempt_id: str) -> WorktreeAssignmentRecord:
        row = self._assignment_row(attempt_id)
        if row is None:
            raise UnknownRecord(f"attempt {attempt_id} owns no worktree assignment")
        return self._assignment(row)

    def worktree_owner(
        self, worktree_path: Path | str
    ) -> WorktreeAssignmentRecord | None:
        """Which Attempt owns a worktree path, if any. Ownership is queryable."""

        row = self._connection.execute(
            "SELECT * FROM worktree_assignments WHERE worktree_path = ?",
            (str(worktree_path),),
        ).fetchone()
        return None if row is None else self._assignment(row)

    def branch_owner(
        self, repository: Path | str, branch: str
    ) -> WorktreeAssignmentRecord | None:
        row = self._connection.execute(
            "SELECT * FROM worktree_assignments WHERE repository = ? AND branch = ?",
            (str(repository), branch),
        ).fetchone()
        return None if row is None else self._assignment(row)

    def acknowledge_worktree_provisioned(
        self, attempt_id: str
    ) -> WorktreeAssignmentRecord:
        """Record that this Attempt's worktree exists at B1.

        The single ``allocated -> provisioned`` transition, and the only update
        the assignment table permits. Acknowledging twice is a no-op, so a
        provisioning call whose acknowledgement was lost can simply be repeated.
        """

        with self._write():
            row = self._assignment_row(attempt_id)
            if row is None:
                raise UnknownRecord(f"attempt {attempt_id} owns no worktree assignment")
            if row["state"] == WorktreeAssignmentRecord.ALLOCATED:
                self._connection.execute(
                    "UPDATE worktree_assignments SET state = 'provisioned', "
                    "provisioned_at = ? WHERE attempt_id = ? AND state = 'allocated'",
                    (_now(), attempt_id),
                )
        return self.worktree_assignment(attempt_id)

    @staticmethod
    def _assignment(row: sqlite3.Row) -> WorktreeAssignmentRecord:
        return WorktreeAssignmentRecord(
            attempt_id=row["attempt_id"],
            work_unit_id=row["work_unit_id"],
            repository=row["repository"],
            worktree_path=row["worktree_path"],
            branch=row["branch"],
            state=row["state"],
            allocated_at=row["allocated_at"],
            provisioned_at=row["provisioned_at"],
        )
