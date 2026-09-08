"""Build exact published old DDL for migration fixtures, preserving test facts."""

import hashlib
import sqlite3

from broodling.schema import (
    ABANDONMENT_SQL,
    ASSURANCE_SQL,
    RETIREMENT_SQL,
    RETRY_SQL,
    SCHEMA_SQL,
    SUBMISSION_SQL,
    V2_SCHEMA_SHA256,
    V3_SCHEMA_SHA256,
    V4_SCHEMA_SHA256,
    V5_SCHEMA_SHA256,
    V6_SCHEMA_SHA256,
)


def published_schema(version):
    v6 = SCHEMA_SQL.removesuffix(RETRY_SQL)
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
    }[version]
    # A product edit to any old DDL must not silently change the fixture.
    assert hashlib.sha256(definition.encode()).hexdigest() == expected
    return definition, expected


def restore_published_schema(store, version):
    """Replace this fixture with its exact old schema and surviving old facts."""
    definition, expected = published_schema(version)
    previous = store.connection
    with sqlite3.connect(":memory:") as old:
        old.executescript(definition)
        # sqlite_schema creation order respects these tables' binding triggers.
        tables = old.execute(
            "SELECT name FROM sqlite_schema WHERE type = 'table'"
        ).fetchall()
        for (table,) in tables:
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
