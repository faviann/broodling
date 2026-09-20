"""Current package import boundary."""

from __future__ import annotations

import subprocess
import sys
import unittest


class ProductDependencyBoundaryTests(unittest.TestCase):
    def test_importing_broodling_does_not_eagerly_load_zeroshot(self) -> None:
        probe = subprocess.run(
            [
                sys.executable,
                "-c",
                (
                    "import sys; import broodling; "
                    "assert not any(name == 'zeroshot' or name.startswith('zeroshot.') "
                    "for name in sys.modules)"
                ),
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(probe.returncode, 0, probe.stderr)


if __name__ == "__main__":
    unittest.main()
