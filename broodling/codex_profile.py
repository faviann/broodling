"""Explicit no-effect configuration absent from Zeroshot's local defaults.

The host provisions these directories before submission. This module neither
copies credentials nor owns provider sessions. Zeroshot selects each occurrence's
sandbox and lifecycle; the launcher disables ambient rules/config and shell network.
"""

from __future__ import annotations

import hashlib
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

from .errors import UnsupportedRuntime

CODEX_VERSION = "codex-cli 0.153.4"
LAUNCHER = Path(__file__).parent / "codex_bin" / "codex"
# Match the operating variables inherited by the supported SDK. Empty values
# suppress ambient homes/config/scratch while explicit policy supplies the rest.
OPERATING_ENVIRONMENT = dict.fromkeys(
    (
        "HOME",
        "CODEX_HOME",
        "LANG",
        "LC_ALL",
        "SYSTEMROOT",
        "TEMP",
        "TMP",
        "TMPDIR",
        "USERPROFILE",
        "XDG_CACHE_HOME",
        "XDG_CONFIG_HOME",
    ),
    "",
)


@dataclass(frozen=True, slots=True)
class CodexProfile:
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
            **OPERATING_ENVIRONMENT,
            "PATH": str(LAUNCHER.parent.resolve()) + os.pathsep + path,
            "HOME": str(self.profile_home),
            "CODEX_HOME": str(self.isolated_codex_home),
            "BROODLING_REAL_CODEX": str(self.real_codex),
            "BROODLING_PROFILE_HOME": str(self.profile_home),
            "BROODLING_ISOLATED_CODEX_HOME": str(self.isolated_codex_home),
        }

    def identity(self) -> dict[str, str]:
        """Public request identity; no authentication bytes or provider history."""
        if any(
            path.resolve() != path
            for path in (
                self.real_codex,
                self.profile_home,
                self.isolated_codex_home,
            )
        ):
            raise UnsupportedRuntime("provider paths must remain canonical")
        return {
            "profile": "broodling-no-effect-codex/v2",
            "codexVersion": CODEX_VERSION,
            "realCodex": str(self.real_codex),
            "profileHome": str(self.profile_home),
            "isolatedCodexHome": str(self.isolated_codex_home),
            "launcherSha256": hashlib.sha256(LAUNCHER.read_bytes()).hexdigest(),
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
        )
        if any(path.resolve() != path for path in paths):
            raise UnsupportedRuntime("provider paths must remain canonical")
        if any(path.is_relative_to(candidate) for path in paths):
            raise UnsupportedRuntime("provider paths must be outside the candidate")
        if self.profile_home.is_relative_to(
            self.isolated_codex_home
        ) or self.isolated_codex_home.is_relative_to(self.profile_home):
            raise UnsupportedRuntime("HOME and CODEX_HOME must be separate")
        if not self.profile_home.is_dir() or any(self.profile_home.iterdir()):
            raise UnsupportedRuntime("HOME must already exist and be empty")
        auth = self.isolated_codex_home / "auth.json"
        if (
            not self.isolated_codex_home.is_dir()
            or {item.name for item in self.isolated_codex_home.iterdir()}
            != {"auth.json"}
            or not auth.is_file()
            or auth.is_symlink()
        ):
            raise UnsupportedRuntime("CODEX_HOME must contain only an auth.json file")
        if (
            not LAUNCHER.is_file()
            or not os.access(LAUNCHER, os.X_OK)
            or not self.real_codex.is_file()
            or not os.access(self.real_codex, os.X_OK)
            or self.real_codex == LAUNCHER.resolve()
        ):
            raise UnsupportedRuntime("Codex launcher and executable are required")
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
            raise UnsupportedRuntime("Codex version could not be checked") from error
        if version != CODEX_VERSION:
            raise UnsupportedRuntime(
                f"Codex CLI {CODEX_VERSION} is required by the no-effect settings"
            )
