"""The shared store fixture closes every connection a test opened."""

from __future__ import annotations

import sqlite3
import unittest

from support import StoreTestCase


def is_closed(store) -> bool:
    try:
        store.connection.execute("SELECT 1")
    except sqlite3.ProgrammingError:
        return True
    return False


class _ReopeningCase(StoreTestCase):
    """A fixture consumer that restarts twice, as `admitted_case()` does."""

    #: Driven by the cases below; not a test of its own.
    __test__ = False

    def __init__(self, method_name: str = "runTest", *, fail: bool = False) -> None:
        super().__init__(method_name)
        self.fail_in_body = fail
        self.opened: list = []

    def runTest(self) -> None:
        self.opened.append(self.store)
        self.opened.append(self.reopen())
        self.opened.append(self.reopen())
        if self.fail_in_body:
            raise RuntimeError("fixture body failed before cleanup")


class StoreFixtureCleanupTests(unittest.TestCase):
    def run_fixture(self, *, fail: bool):
        case = _ReopeningCase(fail=fail)
        result = case.run()
        self.assertEqual(len({id(store) for store in case.opened}), 3)
        return case, result

    def test_original_and_reopened_stores_close_after_a_passing_test(self) -> None:
        case, result = self.run_fixture(fail=False)
        self.assertTrue(result.wasSuccessful())
        self.assertTrue(all(is_closed(store) for store in case.opened))

    def test_original_and_reopened_stores_close_after_a_failing_test(self) -> None:
        case, result = self.run_fixture(fail=True)
        self.assertFalse(result.wasSuccessful())
        self.assertTrue(all(is_closed(store) for store in case.opened))


if __name__ == "__main__":
    unittest.main()
