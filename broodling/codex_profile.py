"""Explicit host paths for the existing G1-V1 Codex launch profile.

The host provisions these directories before submission. This module neither
copies credentials nor owns provider sessions. Zeroshot selects each occurrence's
sandbox and lifecycle; the launcher applies the qualified W4 command settings.
"""

from __future__ import annotations

import hashlib
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

from .containment import CONTAINMENT_PROFILE
from .errors import UnsupportedRuntime

QUALIFIED_CODEX_VERSION = "codex-cli 0.153.4"
LAUNCHER = Path(__file__).parent / "codex_bin" / "codex"
EVIDENCE_LEAF = Path(__file__).parent / "mechanical_evidence.py"
CONTAINMENT = Path(__file__).parent / "containment.py"
BWRAP = Path("/usr/bin/bwrap")
PROFILE_ENVIRONMENT_NAMES = (
    "BROODLING_REAL_CODEX",
    "BROODLING_PROFILE_HOME",
    "BROODLING_ISOLATED_CODEX_HOME",
)


@dataclass(frozen=True, slots=True)
class QualifiedCodexProfile:
    real_codex: Path
    profile_home: Path
    isolated_codex_home: Path

    def __post_init__(self) -> None:
        for field in ("real_codex", "profile_home", "isolated_codex_home"):
            object.__setattr__(
                self, field, Path(getattr(self, field)).expanduser().resolve()
            )

    def environment(self, path: str) -> dict[str, str]:
        """Return only explicit nonsecret paths, including the product launcher."""

        return {
            "PATH": str(LAUNCHER.parent.resolve()) + os.pathsep + path,
            "BROODLING_REAL_CODEX": str(self.real_codex),
            "BROODLING_PROFILE_HOME": str(self.profile_home),
            "BROODLING_ISOLATED_CODEX_HOME": str(self.isolated_codex_home),
            "BROODLING_EVIDENCE_LEAF": "deterministic-v1",
        }

    def identity(self) -> dict[str, str]:
        """Public request identity; no authentication bytes or provider history."""

        return {
            "profile": "g1-v1-codex-w4",
            "containmentProfile": CONTAINMENT_PROFILE,
            "containmentSha256": hashlib.sha256(CONTAINMENT.read_bytes()).hexdigest(),
            "codexVersion": QUALIFIED_CODEX_VERSION,
            "realCodex": str(self.real_codex),
            "profileHome": str(self.profile_home),
            "isolatedCodexHome": str(self.isolated_codex_home),
            "launcherSha256": hashlib.sha256(LAUNCHER.read_bytes()).hexdigest(),
            "evidenceLeafSha256": hashlib.sha256(
                EVIDENCE_LEAF.read_bytes()
            ).hexdigest(),
            "evidenceSandboxSha256": hashlib.sha256(BWRAP.read_bytes()).hexdigest(),
        }

    def validate(self, workspace: Path | str, *, path: str) -> None:
        """Check the initially provisioned profile before its first dispatch.

        Do not repeat the empty/auth-only checks after dispatch: Codex owns its
        runtime files and can create them in its isolated home while executing.
        """

        candidate = Path(workspace).resolve()
        paths = (
            self.real_codex,
            self.profile_home,
            self.isolated_codex_home,
            LAUNCHER.resolve(),
            EVIDENCE_LEAF.resolve(),
            CONTAINMENT.resolve(),
            BWRAP.resolve(),
        )
        if any(path.is_relative_to(candidate) for path in paths):
            raise UnsupportedRuntime(
                "qualified provider paths must be outside the candidate"
            )
        if self.profile_home.is_relative_to(
            self.isolated_codex_home
        ) or self.isolated_codex_home.is_relative_to(self.profile_home):
            raise UnsupportedRuntime("qualified HOME and CODEX_HOME must be separate")
        if not self.profile_home.is_dir() or any(self.profile_home.iterdir()):
            raise UnsupportedRuntime("qualified HOME must already exist and be empty")
        auth = self.isolated_codex_home / "auth.json"
        if (
            not self.isolated_codex_home.is_dir()
            or {item.name for item in self.isolated_codex_home.iterdir()}
            != {"auth.json"}
            or not auth.is_file()
            or auth.is_symlink()
        ):
            raise UnsupportedRuntime(
                "qualified CODEX_HOME must contain only an auth.json file"
            )
        if (
            not LAUNCHER.is_file()
            or not EVIDENCE_LEAF.is_file()
            or not CONTAINMENT.is_file()
            or not BWRAP.is_file()
            or not os.access(BWRAP, os.X_OK)
            or not os.access(LAUNCHER, os.X_OK)
            or not self.real_codex.is_file()
            or not os.access(self.real_codex, os.X_OK)
            or self.real_codex == LAUNCHER.resolve()
        ):
            raise UnsupportedRuntime(
                "qualified Codex launcher and executable are required"
            )
        try:
            version = subprocess.run(
                [str(self.real_codex), "--version"],
                capture_output=True,
                text=True,
                check=True,
                timeout=10,
                env={
                    "PATH": path,
                    "HOME": str(self.profile_home),
                    "CODEX_HOME": str(self.isolated_codex_home),
                },
            ).stdout.strip()
        except (OSError, subprocess.SubprocessError) as error:
            raise UnsupportedRuntime(
                "qualified Codex version could not be checked"
            ) from error
        if version != QUALIFIED_CODEX_VERSION:
            raise UnsupportedRuntime(
                "Codex CLI differs from the G1-V1 qualified version"
            )
