"""Build exact published old DDL for migration fixtures, preserving test facts."""

import hashlib
import sqlite3
from pathlib import Path

from broodling.schema import (
    ABANDONMENT_SQL,
    ASSURANCE_SQL,
    DISPOSITION_SQL,
    RETIREMENT_SQL,
    RETRY_SQL,
    SCHEMA_SQL,
    SUBMISSION_SQL,
    V2_SCHEMA_SHA256,
    V3_SCHEMA_SHA256,
    V4_SCHEMA_SHA256,
    V5_SCHEMA_SHA256,
    V6_SCHEMA_SHA256,
    V7_SCHEMA_SHA256,
    V8_SCHEMA_SHA256,
    V9_SCHEMA_SHA256,
    V10_SCHEMA_SHA256,
)


def published_schema(version):
    if version == 10:
        raw = (
            Path(__file__)
            .with_name("fixtures")
            .joinpath("schema-v10.sql")
            .read_bytes()
        )
        expected = V10_SCHEMA_SHA256
        # Hash the fixture's raw bytes before its digest can be stamped into a
        # restored database.
        assert hashlib.sha256(raw).hexdigest() == expected
        return raw.decode("utf-8"), expected

    v7 = SCHEMA_SQL.removesuffix(DISPOSITION_SQL)
    v6 = v7.removesuffix(RETRY_SQL)
    v5 = v6.removesuffix(RETIREMENT_SQL)
    v4 = v5.removesuffix(ABANDONMENT_SQL)
    v3 = v4.removesuffix(ASSURANCE_SQL)
    v2 = v3.removesuffix(SUBMISSION_SQL)
    definition, expected = {
        2: (v2, V2_SCHEMA_SHA256),
        3: (v3, V3_SCHEMA_SHA256),
        4: (v4, V4_SCHEMA_SHA256),
        5: (v5, V5_SCHEMA_SHA256),
        6: (v6, V6_SCHEMA_SHA256),
        7: (v7, V7_SCHEMA_SHA256),
        8: (
            v7
            + Path(__file__)
            .with_name("fixtures")
            .joinpath("schema-v8-disposition.sql")
            .read_text(),
            V8_SCHEMA_SHA256,
        ),
        9: (
            v7
            + Path(__file__)
            .with_name("fixtures")
            .joinpath("schema-v9-disposition.sql")
            .read_text(),
            V9_SCHEMA_SHA256,
        ),
    }[version]
    # A product edit to any old DDL must not silently change the fixture.
    assert hashlib.sha256(definition.encode()).hexdigest() == expected
    return definition, expected


def restore_published_schema(store, version):
    """Replace this fixture with its exact old schema and surviving old facts."""
    if version == 10:
        _restore_v10_schema(store)
        return
    definition, expected = published_schema(version)
    previous = store.connection
    with sqlite3.connect(":memory:") as old:
        old.executescript(definition)
        # sqlite_schema creation order respects these tables' binding triggers.
        tables = old.execute(
            "SELECT name FROM sqlite_schema WHERE type = 'table'"
        ).fetchall()
        for (table,) in tables:
            if (
                previous.execute(
                    "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = ?",
                    (table,),
                ).fetchone()
                is None
            ):
                continue
            rows = [tuple(row) for row in previous.execute(f"SELECT * FROM {table}")]
            if rows:
                placeholders = ",".join("?" for _ in rows[0])
                old.executemany(f"INSERT INTO {table} VALUES ({placeholders})", rows)
        old.executemany(
            "UPDATE schema_meta SET value = ? WHERE key = ?",
            (
                (str(version), "schema_version"),
                (expected, "schema_sha256"),
            ),
        )
        old.commit()
        old.backup(previous)


def _restore_v10_schema(store):
    """Restore frozen v10 DDL while preserving the current database's rows."""
    definition, expected = published_schema(10)
    previous = store.connection
    with sqlite3.connect(":memory:") as old:
        old.executescript(definition)
        triggers = old.execute(
            "SELECT name, sql FROM sqlite_schema WHERE type = 'trigger'"
        ).fetchall()
        # Copy historical rows verbatim, then reinstate their DDL constraints.
        for name, _ in triggers:
            old.execute(f'DROP TRIGGER "{name}"')
        tables = [
            row[0]
            for row in old.execute(
                "SELECT name FROM sqlite_schema WHERE type = 'table'"
            )
        ]
        for table in tables:
            if (
                previous.execute(
                    "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = ?",
                    (table,),
                ).fetchone()
                is None
            ):
                continue
            rows = [
                tuple(row)
                for row in previous.execute(f"SELECT * FROM {table}")
            ]
            if rows:
                placeholders = ",".join("?" for _ in rows[0])
                old.executemany(f"INSERT INTO {table} VALUES ({placeholders})", rows)
        for _, statement in triggers:
            old.execute(statement)
        old.executemany(
            "UPDATE schema_meta SET value = ? WHERE key = ?",
            (
                ("10", "schema_version"),
                (expected, "schema_sha256"),
            ),
        )
        old.commit()
        old.backup(previous)
