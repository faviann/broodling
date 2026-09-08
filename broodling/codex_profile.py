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

from . import git
from .containment import CONTAINMENT_PROFILE
from .errors import GitCommandError, UnsupportedRuntime

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


def _declared_context_paths(target: dict) -> list[Path]:
    profile = target.get("codexProfile", {})
    environment = target.get("environment", {})
    paths = [
        Path(value)
        for value in (
            target.get("stateDir"),
            profile.get("profileHome"),
            profile.get("isolatedCodexHome"),
            environment.get("BROODLING_PROFILE_HOME"),
            environment.get("BROODLING_ISOLATED_CODEX_HOME"),
        )
        if value is not None
    ]
    if any(not path.is_absolute() for path in paths):
        raise UnsupportedRuntime("declared runtime/profile paths must be absolute")
    return paths


def assert_retry_homes_available(
    target: dict, reservations: list[dict], write_paths: tuple[Path, ...] = ()
) -> None:
    """Exclude reserved homes from declared provider/runtime/provisioning writes."""
    paths = [
        path.resolve() for path in [*_declared_context_paths(target), *write_paths]
    ]
    for reservation in reservations:
        for name in ("profileHome", "isolatedCodexHome"):
            reserved = Path(reservation["codexProfile"][name])
            for path in paths:
                if path.is_relative_to(reserved) or reserved.is_relative_to(path):
                    raise UnsupportedRuntime(
                        "declared write path overlaps an Attempt's retry home reservation"
                    )


def assert_fresh_retry_profile(
    target: dict, historical_targets: list[dict], protected_paths: list[Path]
) -> None:
    """Validate fresh host-provisioned homes against retained Attempt bindings.

    The store calls this while reserving a retry and before its first dispatch.
    Shared executables and SDK state directories remain supported; provider
    automatic-context homes must be disjoint from all old homes and history.
    Nothing is created, copied, cleaned or recovered here.
    """
    try:
        identity = target["codexProfile"]
        profile = QualifiedCodexProfile(
            identity["realCodex"],
            identity["profileHome"],
            identity["isolatedCodexHome"],
        )
        environment = target["environment"]
        search_path = environment["PATH"].partition(os.pathsep)[2]
        if (
            profile.identity() != identity
            or profile.environment(search_path) != environment
        ):
            raise UnsupportedRuntime("retry requires the current qualified profile")
        homes = [Path(identity[name]) for name in ("profileHome", "isolatedCodexHome")]
        for home in homes:
            if (
                not home.is_absolute()
                or home.resolve(strict=True) != home
                or not home.is_dir()
            ):
                raise UnsupportedRuntime(
                    "retry profile homes must remain canonical directories"
                )
        state_dir = Path(target["stateDir"])
        if not state_dir.is_absolute() or state_dir.resolve() != state_dir:
            raise UnsupportedRuntime("retry runtime directory must remain canonical")
        excluded = [Path(path) for path in protected_paths]
        excluded.append(state_dir)
        for old in historical_targets:
            for path in _declared_context_paths(old):
                excluded.extend((path, path.resolve()))
        for index, home in enumerate(homes):
            for previous in [*excluded, *homes[:index]]:
                if home.is_relative_to(previous) or previous.is_relative_to(home):
                    raise UnsupportedRuntime(
                        "retry profile homes overlap retained Attempt context"
                    )
        if any(profile.profile_home.iterdir()):
            raise UnsupportedRuntime("retry HOME must be fresh and empty")
        auth = profile.isolated_codex_home / "auth.json"
        if (
            {item.name for item in profile.isolated_codex_home.iterdir()}
            != {"auth.json"}
            or not auth.is_file()
            or auth.is_symlink()
        ):
            raise UnsupportedRuntime("retry CODEX_HOME must be fresh and auth-only")
    except (KeyError, TypeError, AttributeError, OSError, RuntimeError) as error:
        raise UnsupportedRuntime(
            "retry qualified profile could not be validated"
        ) from error


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
            "sharedGitPolicy": "canonical-outside-slash-tmp-v1",
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
        # Current assurance connections never declare TMPDIR. The pinned
        # process runner clears ambient environment before applying those exact
        # connections, so /tmp is this Linux profile's only extra writable
        # scratch root. Do not substitute the broader durable-workspace policy.
        try:
            common = git.common_directory(candidate).resolve(strict=True)
            scratch = Path("/tmp").resolve(strict=True)
        except (GitCommandError, OSError, RuntimeError) as error:
            raise UnsupportedRuntime(
                "qualified shared Git directory could not be resolved"
            ) from error
        if common.is_relative_to(scratch):
            raise UnsupportedRuntime(
                "qualified shared Git directory must be outside provider writable /tmp"
            )
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
