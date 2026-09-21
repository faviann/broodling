"""Installed single-host operator commands over the existing Broodling facade."""

from __future__ import annotations

import argparse
import asyncio
import base64
import importlib.util
import json
import os
import sys
from dataclasses import fields, is_dataclass
from pathlib import Path
from urllib.parse import urlsplit

from .abandonment import CessationUnconfirmed
from .contract import RequiredEffect
from .identity import WorkReference
from .invocation import Broodling
from .store import BroodlingStore, ContractRevisionRecord
from .zeroshot_sdk import ZeroshotSubmitter


class InstallationError(Exception):
    """The selected installation is missing or has invalid configuration."""


def _json_value(value):
    if isinstance(value, bytes):
        return {"encoding": "base64", "data": base64.b64encode(value).decode("ascii")}
    if is_dataclass(value):
        result = {
            field.name: _json_value(getattr(value, field.name))
            for field in fields(value)
        }
        if isinstance(value, ContractRevisionRecord):
            result["contract"] = value.contract.to_mapping()
        return result
    if isinstance(value, dict):
        return {key: _json_value(item) for key, item in value.items()}
    if isinstance(value, (tuple, list)):
        return [_json_value(item) for item in value]
    return value


def _configuration(root: Path, *, require_store: bool = True) -> dict:
    # A mistyped root must never initialize a second empty lifecycle store.
    database = root / "state" / "broodling.sqlite3"
    if require_store and (not database.is_file() or database.stat().st_size == 0):
        raise InstallationError("installed state is missing")
    config = json.loads((root / "config.json").read_text(encoding="utf-8"))
    if not isinstance(config, dict) or set(config) != {"direct_target_origin"}:
        raise InstallationError("installation configuration is invalid")
    origin = config["direct_target_origin"]
    if not isinstance(origin, str):
        raise InstallationError("direct target origin is invalid")
    parsed = urlsplit(origin)
    if (
        parsed.scheme != "http"
        or parsed.hostname != "127.0.0.1"
        or parsed.port is None
        or not 1 <= parsed.port <= 65535
        or parsed.username is not None
        or parsed.password is not None
        or parsed.path
        or parsed.query
        or parsed.fragment
        or origin != f"http://127.0.0.1:{parsed.port}"
    ):
        raise InstallationError("direct target origin is invalid")
    return config


def _proposer(path: Path):
    spec = importlib.util.spec_from_file_location("broodling_operator_proposer", path)
    if spec is None or spec.loader is None:
        raise ValueError("proposer must be an importable Python file")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    propose = getattr(module, "propose", None)
    if not callable(propose):
        raise ValueError("proposer must export propose(inputs)")
    return propose


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path, help="installed host root")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser(
        "initialize-store", help="create a new application store at this installation"
    )
    commands.add_parser(
        "upgrade-store", help="explicitly migrate a recognized application store"
    )
    submit = commands.add_parser("submit", help="capture, admit and submit one issue")
    history = commands.add_parser("history", help="read retained revisions for an issue")
    for command in (submit, history):
        command.add_argument("--repository", required=True, help="GitHub OWNER/REPO")
        command.add_argument("--issue", required=True, type=int)
    submit.add_argument("--checkout", required=True, type=Path)
    submit.add_argument("--revision", default="HEAD")
    submit.add_argument("--target-branch", required=True)
    submit.add_argument(
        "--proposer", required=True, type=Path,
        help="trusted operator Python file exporting propose(inputs) -> Contract",
    )
    resume = commands.add_parser("resume", help="resume one retained Contract revision")
    resume.add_argument("contract_revision_id")
    resume.add_argument("--checkout", type=Path)
    resume.add_argument("--revision", default="HEAD")
    status = commands.add_parser("status", help="read one retained Contract revision")
    status.add_argument("contract_revision_id")
    wait = commands.add_parser("wait", help="wait for and record the bound result")
    wait.add_argument("attempt_id")
    stop = commands.add_parser("stop", help="abandon and request native stop")
    stop.add_argument("attempt_id")
    stop.add_argument("--reason", required=True)
    return parser


def _invoke(app: Broodling, args):
    if args.command == "submit":
        return app.submit(
            WorkReference.parse(args.repository, args.issue),
            _proposer(args.proposer),
            repository=args.checkout,
            revision=args.revision,
            required_effects=(RequiredEffect(
                "deliver",
                f"Open a pull request targeting {args.target_branch}.",
                "pull_request",
                args.target_branch,
            ),),
            constructed_by="caller",
        )
    if args.command == "resume":
        return app.resume(
            args.contract_revision_id, repository=args.checkout, revision=args.revision
        )
    if args.command == "history":
        return app.history(WorkReference.parse(args.repository, args.issue))
    if args.command == "status":
        return app.status(args.contract_revision_id)
    if args.command == "wait":
        return asyncio.run(app.wait(args.attempt_id))
    return asyncio.run(app.stop(args.attempt_id, args.reason))


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        root = args.root.expanduser().resolve()
        if args.command in {"initialize-store", "upgrade-store"}:
            _configuration(root, require_store=False)
            operation = (
                BroodlingStore.initialize
                if args.command == "initialize-store"
                else BroodlingStore.upgrade
            )
            with operation(root / "state" / "broodling.sqlite3") as store:
                result = {
                    "operation": args.command,
                    "store": str(store.path),
                    "schema": store.schema_meta(),
                }
            print(json.dumps(result, ensure_ascii=False, allow_nan=False))
            return 0
        config = _configuration(root)
        credentials = {}
        if args.command in {"submit", "resume"}:
            credentials = {
                "github_token": os.environ.get("GH_TOKEN"),
                "gateway_base_url": os.environ.get("GATEWAY_BASE_URL"),
                "gateway_api_key": os.environ.get("GATEWAY_API_KEY"),
            }
        submitter = ZeroshotSubmitter(
            root / "runtime",
            delivery_target_origin=config["direct_target_origin"],
            **credentials,
        )
        with BroodlingStore.open(root / "state" / "broodling.sqlite3") as store:
            result = _invoke(Broodling(store, submitter, root / "attempts"), args)
        print(json.dumps(_json_value(result), ensure_ascii=False, allow_nan=False))
        return 0
    except KeyboardInterrupt:
        print(json.dumps({
            "error": "Interrupted",
            "message": "Caller detached; this does not stop an Attempt. Inspect history/status.",
        }), file=sys.stderr)
        return 130
    except Exception as error:
        # Provider/transport exceptions may contain live credential values.
        # Durable facts remain discoverable without exposing those diagnostics.
        quarantined = isinstance(error, CessationUnconfirmed)
        store_command = args.command in {"initialize-store", "upgrade-store"}
        if quarantined:
            message = (
                "Attempt abandoned; physical cessation is unconfirmed. Retain its "
                "worktree in quarantine; cleanup and replacement are unauthorized. "
                "Inspect history/status."
            )
        elif store_command:
            message = f"{error}. Existing state was not replaced."
        else:
            message = (
                "Command failed. Check installation and command inputs; inspect "
                "history/status for retained authority before resuming."
            )
        print(json.dumps({
            "error": type(error).__name__,
            "message": message,
        }), file=sys.stderr)
        return 3 if quarantined else 1


if __name__ == "__main__":
    raise SystemExit(main())
