"""Fail-closed V1 restriction on Git's ambient checkout transformations.

B1 is the pinned commit's bytes. This profile admits no conversion drivers or
built-in byte conversion attributes. It reads Git's effective configuration and
attributes (including system/global and repository info attributes), using the
pinned tree instead of the caller's index for committed .gitattributes. Nothing
here checks out files, executes a driver, normalizes bytes or changes an index.
The qualified host excludes concurrent hostile mutation of these inputs.
"""

from pathlib import Path

from . import git
from .errors import GitCommandError, UnsupportedStartingState

_ATTRIBUTES = ("filter", "text", "eol", "ident", "working-tree-encoding", "crlf")


def assert_supported_checkout(repository: Path, commit_oid: str) -> None:
    """Refuse unsupported effective checkout behavior before checkout/status.

    Call again on the source and the assigned worktree before first dispatch.
    Git must support check-attr --source; inability to inspect is a refusal.
    Configured external filters are refused even when currently unused, so a
    live/index attribute cannot cause an external clean driver during status.
    """
    if len(commit_oid) != 40 or any(c not in "0123456789abcdef" for c in commit_oid):
        raise UnsupportedStartingState("checkout profile requires a pinned commit id")
    try:
        configuration = git.run_bytes(repository, "config", "--null", "--list")
        for entry in configuration.split(b"\0"):
            if not entry:
                continue
            key, _, value = entry.partition(b"\n")
            key = key.lower()
            normalized = value.lower()
            external_filter = key.startswith(b"filter.") and key.rsplit(b".", 1)[-1] in {
                b"clean", b"smudge", b"process"
            }
            unsupported_setting = (
                key in {b"core.autocrlf", b"core.sparsecheckout", b"core.fsmonitor"}
                and normalized not in {b"false", b"no", b"off", b"0"}
            ) or (
                key == b"core.symlinks" and normalized not in {b"true", b"yes", b"on", b"1", b""}
            ) or (key == b"core.eol" and normalized not in {b"lf", b"native"})
            # A conditional include can activate only after the new branch or
            # worktree path exists, beyond this pre-checkout query's context.
            conditional_include = key.startswith(b"includeif.") and key.endswith(b".path")
            if external_filter or unsupported_setting or conditional_include:
                _refuse(f"configuration {key.decode(errors='replace')}")

        paths = git.run_bytes(
            repository, "--no-replace-objects", "ls-tree", "-rz", "--name-only", commit_oid
        )
        # check-attr requires a worktree context even with --source. This
        # explicit query-only context also supports a bare/common Git directory;
        # --source still reads committed attributes exclusively from the tree.
        git_directory = git.run(repository, "rev-parse", "--absolute-git-dir").strip()
        attributes = git.run_bytes(
            repository, "--no-replace-objects", f"--git-dir={git_directory}",
            f"--work-tree={repository.resolve()}", "check-attr", f"--source={commit_oid}",
            "--stdin", "-z", *_ATTRIBUTES, stdin=paths,
        )
        fields = attributes.split(b"\0")
        if fields[-1] != b"" or (len(fields) - 1) % 3:
            _refuse("unreadable effective attributes")
        for offset in range(0, len(fields) - 1, 3):
            path, attribute, value = fields[offset:offset + 3]
            if value not in {b"unspecified", b"unset"}:
                _refuse(
                    f"attribute {attribute.decode()}={value.decode(errors='replace')} "
                    f"on {path.decode(errors='replace')!r}"
                )
    except GitCommandError as error:
        raise UnsupportedStartingState(
            f"cannot establish the supported V1 checkout profile: {error}"
        ) from error


def _refuse(reason: str) -> None:
    raise UnsupportedStartingState(
        f"unsupported V1 checkout transformation: {reason}; "
        "B1 requires unchanged Git blob bytes; remove the transformation "
        "before admission, provisioning or first dispatch"
    )
