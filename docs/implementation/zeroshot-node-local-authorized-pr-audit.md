# Zeroshot node-local authorized-PR audit

**Status: issue #72 completed BLOCKED against the pinned Zeroshot 10.3.0 / Python
SDK 10.3.0.post1 boundary.** This is a dated dependency finding, not a claim
about later Zeroshot releases. It changes neither the supported DirectTarget
profile nor the preserved P5 v1-v3 records. Node-local OAuth is post-V1, so this
finding is not a blocker for the selected V1 DirectTarget/API-key profile; it is
the source-backed dependency record for a future node-local profile.

## Required combination

The intended profile needs both of these properties in one stock Zeroshot run:

1. standard `software-change` execution by a node-local Codex harness using the
   invoking user's normal `HOME`/`CODEX_HOME` authentication state; and
2. native PR delivery bound to the Contract's exact GitHub repository, target
   branch and B1 revision, followed by the existing stable PR receipt.

Zeroshot 10.3.0 implements each half, but not together.

## Source finding

The built-in local target is the only composition that supplies local-user Codex
identity. It reads the invoking `HOME`, uses `CODEX_HOME` or `~/.codex`, and builds
the candidate with local process placement. Codex/OpenAI then permits the user's
normal login state instead of requiring a declared `OPENAI_API_KEY`.

Sources: [local user composition](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs#L198-L269),
[local Codex identity](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex.rs#L46-L102), and
[OpenAI authentication selection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex/command.rs#L40-L102).

The CLI rejects explicit `repository`, `branch` and `revision` unless a named
target is selected. The Python SDK does serialize those arguments, but its own
`RunRequest` documentation describes them as named-target overrides. If local
preparation is reached without them, Zeroshot explicitly discards the empty
submitted source selection (`source: _`) and derives repository identity, branch
and exact revision from the local worktree's GitHub origin, attached branch and
`HEAD`. That derived source becomes the delivery target.

Sources: [explicit-source target requirement](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cli/parser/convert.rs#L358-L372),
[local source discard and snapshot](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs#L70-L139),
[local delivery target](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs#L248-L269), and
[SDK source arguments](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L387-L399).

The target/capsule composition has the inverse property. It can admit an exact
explicit remote source, but non-local candidate construction calls the hosted
Codex adapter, which clears `local_user`. Consequently a DirectTarget cannot use
the node user's normal Codex/ChatGPT login through this interface.

Sources: [candidate placement](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_candidate.rs#L62-L130) and
[hosted Codex identity removal](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex.rs#L76-L102).

`UniformRuntime` and `RuntimePlan` select graph-node harness/provider/model/
connection bindings. They do not change target source resolution or turn hosted
process placement into local-user placement. They remain the correct future
configuration seam once a compatible target exists; Broodling should not build a
parallel scheduler or credential layer around this gap.

## Why the local snapshot is insufficient

Broodling deliberately provisions every Attempt on a unique attached
`broodling/...` branch at exact B1. That branch is disposable workspace
scaffolding and is never delivery authority. An authorized PR Contract instead
names its target branch, commonly `main`.

With `LocalTarget`, the receipt would therefore be for the Attempt branch rather
than the authorized target branch. Broodling's existing receipt validator would
correctly reject it. Renaming or reattaching the worktree to the Contract branch
would weaken exclusive Attempt ownership, collide with other worktrees/Attempts,
and turn source-checkout manipulation into new Broodling execution machinery.
Reimplementing commit/push/PR delivery or copying OAuth state would cross the
same boundary. None is an acceptable workaround.

## Reproducer

Run from the repository root with the pinned environment:

```bash
.venv/bin/python evaluation/p5/verify_node_local_source_authority.py
```

The probe selects the standard `software-change` PR preset, an OpenAI/Codex
runtime with an explicitly empty connection map (the local-user authentication
shape), and Broodling's repository, `main` target and current exact revision. It
supplies a non-secret GitHub sentinel that is never used. Native preflight rejects
the explicit source authority because there is no named target, before a run,
provider task or GitHub operation starts. The retained result is `BLOCKED` with
that native diagnostic and zero effect counts.

This intentionally stops at preflight: triggering a knowingly mis-targeted
effect would be unsafe and unnecessary. The source cited above shows that a local
run without explicit authority constructs native delivery's target from the
attached worktree branch.

## Exact upstream capability needed

Zeroshot needs a supported target that simultaneously:

- runs the harness with local-user process placement and normal harness-owned
  authentication state;
- admits and durably binds explicit repository, target branch and exact revision
  instead of replacing them with the attached worktree branch/HEAD; and
- retains standard native PR delivery, submission-key idempotency, reconnectable
  result observation and the stable receipt.

The narrowest likely change is for `LocalTarget` to honor and validate explicit
source overrides (including a target branch independent of its unique attached
workspace branch) while preserving its current local-user candidate placement.
An equivalent upstream target/API would also satisfy the gap.

Until then, the node-local OAuth-backed authorized-PR profile is unsupported.
At issue #72's completion Broodling retained the working DirectTarget/API-key
profile from #69, created no protocol for the unavailable node-local profile,
and did not consume R01. The later prospective P5 v4 selects that separate,
already-supported DirectTarget profile; it does not change this finding or make
node-local OAuth part of V1.
