# Current Zeroshot source audit

Investigated 16 September 2026, starting from Broodling main
`87f228652117557de95a679d4f22bca76cb86b83`. The initial audit examined Zeroshot
10.2.9 at `70a6c80d3d6b00f4300776fe2bbe1b4ba86763bf`; release rechecking found
10.3.0 published during this work. Current source links below name its immutable
commit `054ad3fd6c763b98d12f5b2e90830b97116561ad`.

The selected design uses Zeroshot's standard `software-change` workflow with no
Git delivery. The user explicitly chose to drop Broodling's separate mechanical
evidence, adjudication and per-criterion proof protocol. Broodling admits one
invocation, consumes its eventual result and decides the Work Unit lifecycle
under current-Attempt authority. Dispatched Attempt workspaces remain quarantined
because the local target offers no public physical cessation receipt. This is a
prospective decision, not a revision of historical qualification evidence.

The owner also removed admission gates requiring a predeclared evidence
population, validation seam, validation action, and falsifying observation.
Broodling supplies acceptance criteria and any existing guidance; Zeroshot
chooses how to validate during the run. Existing supplied Contract fields remain
immutable, but a complete validation plan is not an admission prerequisite.

## Release and distribution

The latest stable engine on recheck is **Zeroshot 10.3.0**, released on 16 September
2026 at 17:07 UTC. Its matching stable Python SDK is **10.3.0.post1**, released at
17:12 UTC under tag `zeroshot-python-v10.3.0_1`. Both tags name the current commit
above. The distribution is `the-open-engine-zeroshot`, imported as `zeroshot`;
the old unpublished `zeroshot-rust==0.1.0.dev0` integration is not the current
release line. Sources: [engine release](https://github.com/the-open-engine/zeroshot/releases/tag/v10.3.0),
[SDK release](https://github.com/the-open-engine/zeroshot/releases/tag/zeroshot-python-v10.3.0_1),
[Python packaging](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/pyproject.toml).

The SDK supports Python 3.11 or later and has no Python runtime dependencies.
Platform wheels bundle the matching standalone Rust executable. Python provides
transport and typed projections; native code owns admission, presets, provider
execution and durable run state. Sources: [SDK README](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/README.md),
[install guide](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/docs/getting-started/install.md).

The documented [PyPI project](https://pypi.org/project/the-open-engine-zeroshot/)
and its JSON endpoint returned HTTP 404 during the initial investigation. The
official GitHub SDK release publishes all five platform wheels. The 10.3.0 Linux
x86-64 wheel was downloaded and checked against its GitHub release asset digest:

```text
the_open_engine_zeroshot-10.3.0.post1-py3-none-manylinux_2_17_x86_64.whl
sha256:f3629459837a27b7496f98fe0034e7b47a00079d93d3374922c2960952b8ace9
```

Its executable reports `zeroshot 10.3.0`. The official matching wheel provides an
installation path when the documented index is unavailable. Source:
[matching SDK release](https://github.com/the-open-engine/zeroshot/releases/tag/zeroshot-python-v10.3.0_1).

## Supported submission and terminal-result boundary

The standard public integration surface supports beginning/end participation:

| Broodling operation | Supported Python surface |
| --- | --- |
| Bind a no-effect Attempt workspace and runtime store | `LocalTarget(workspace, state_dir=...)` |
| Bind an authorized PR to explicit remote source | `DirectTarget(origin)` plus repository/branch/revision submission selectors |
| Select the frozen Work Unit delivery | `Preset("software-change", delivery="none"|"pull_request")` |
| Specify admitted provider configuration | `UniformRuntime(harness=..., provider=..., model=..., ...)` |
| Submit the immutable task and key | `Client.submit(task, title=..., preset=..., runtime=..., submission_key=...)` |
| Consume an existing correlated run | `Client.get_run(run_id).wait()` |
| Read status for diagnostics | `Run.status()` |
| Request stop for an already-correlated run | `Run.force_stop()` |

`RunResult` contains `run_id`, `succeeded`, `output`, and `failure`. A completed
run is returned immediately by `wait()`, including after reopening a client.
An active wait uses Zeroshot's durable status cursor internally. Python timeout,
cancellation and client close detach observation; only `force_stop()` changes
run lifetime. Broodling needs no watch stream, intermediate execution ID or
cursor to consume the result. Sources: [public request/result models](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/runs.py),
[client and Run implementation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py).

The adapter submits the admitted standard-workflow invocation and later consumes
the correlated result:

```python
from zeroshot import Client, LocalTarget, Preset, UniformRuntime

target = LocalTarget(
    admitted["workspace"], state_dir=admitted["target"]["stateDir"]
)
environment = admitted["target"]["environment"]

async with Client(target=target, environment=environment) as client:
    run = await client.submit(
        admitted["task"],
        title=admitted["title"],
        preset=Preset("software-change", delivery="none"),
        runtime=UniformRuntime(**admitted["runtime"]),
        submission_key=admitted["submissionKey"],
    )
    # Persist run.id against the already-admitted Attempt.

# Reconnection does not reconstruct submission policy or the old workspace.
reconnect_target = LocalTarget(state_dir=admitted["target"]["stateDir"])
async with Client(target=reconnect_target, environment={}) as client:
    result = await client.get_run(correlated_run_id).wait()
    # Recheck current-Attempt authority before applying lifecycle decisions.
```

For DirectTarget runs, the corresponding reconnect target is
`DirectTarget(admitted["target"]["deliveryTargetOrigin"])`. Observation and
force-stop need that persisted origin and run ID, not the submission workspace,
runtime, provider environment, or delivery credential. Broodling still requires
the complete frozen selection and current dispatch authority before an initial
submission or acknowledgement-loss replay.

Zeroshot expands the preset and validates before starting a controller. Exact
duplicate submissions return the existing run; changed content under the same
key raises `SubmissionConflictError`. Local source identity is resolved from
the GitHub origin, attached branch and exact HEAD before comparing the submission
digest. A changed HEAD therefore conflicts. Broodling must preserve its admitted
source, invocation and run correlation, and must not use replay to authorize a
replacement while cessation is uncertain. Sources: [SDK submission](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py),
[local source selection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs),
[duplicate/conflict handling](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cli/local.rs).

## Execution and review belong to Zeroshot

The built-in `software-change` graph provides an implementation worker,
independent acceptance/code review, repair, and ten bounded review iterations.
With delivery disabled, successful output is JSON null. It supplies no separate
Broodling criterion evidence, adjudication or final rationale. That is now the
selected execution outcome, but not a stable candidate identity, so Broodling
10.3 refuses successful disposition rather than manufacturing a snapshot. An
authorized PR run instead uses the stable native receipt described in the
[result-handoff audit](zeroshot-result-handoff-audit.md).
Source: [standard graph implementation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_templates.rs).

Native admission validates graph semantics, input, bindings and bounds. The
reducer controls routing and state, the supervisor allocates and settles
executions, and the provider adapters validate structured output and manage
corrections, failures, sessions and cleanup. `sessionScope: "execution"` gives
each graph execution a fresh session. Broodling declares that preference and
trusts the engine to implement it. Sources: [execution model](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/docs/concepts/execution.md),
[runtime options](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/runtime.py),
[native supervisor](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_supervisor/controller.rs),
[response validation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_runner/response.rs).

Fresh execution scope still permits Zeroshot to resume the same provider session
for corrective turns within one execution. Its Codex adapter launches a new
`codex exec resume` process with the recorded thread ID. Broodling must therefore
allow Codex session persistence in the isolated home: adding `--ephemeral` would
remove the session needed by that supported behavior. Removing that inherited
flag leaves cross-execution freshness in Zeroshot's control. Sources:
[native correction/resume](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex.rs),
[pinned Codex CLI](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/exec/src/cli.rs),
[pinned Codex thread resume](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/exec/src/lib.rs).

The SDK also accepts custom `GraphSpec` and `RuntimePlan` documents unchanged,
but those are not needed for the selected Broodling workflow. The old custom
graph, duplicated final branches, round-completion model call, graph hash
compatibility exceptions and graph interpretation tests have no remaining
product purpose. Sources: [SDK exact-document API](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/README.md),
[previous Broodling graph](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/broodling/assurance_graph.py),
[previous ownership audit](pr48-ownership-audit.md).

## Current limitations and retained policy

**No deterministic evidence worker.** The public runtime binding enum contains
only agents and Git delivery, with no command-worker binding or Python callback.
The former Broodling evidence leaf impersonated Codex and parsed a private agent
prompt. Because the user dropped the separate mechanical proof protocol, this
limitation now justifies deleting that adapter, not retaining a custom graph or
introducing a driver framework. Native output validation proves shape, not the
truth of model-reported command output. Sources: [closed runtime binding](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/crates/openengine-cluster-protocol/src/native_v2_run/runtime.rs),
[former evidence adapter](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/broodling/mechanical_evidence.py).

**Local provider policy remains configurable, not a Broodling no-effect guarantee.**
10.3.0 materially changes the initial 10.2.9 findings: it preserves native local
configuration, web-search and endpoint settings instead of forcing worker
workspace-write/network settings. For workers it queries Codex configuration
through `app-server` and applies
`--dangerously-bypass-approvals-and-sandbox` only if permission policy is
confirmed unset. Configured or unavailable policy gets no added permissive
default. Local verifiers retain explicit read-only policy. Broodling's narrow
no-effect policy adapter therefore applies explicit sandbox, network,
web-search and user-configuration policy without assuming the engine supplies
worker sandbox arguments. Sources: [10.3.0 Codex command construction](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex.rs),
[native permission inspection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex/permissions.rs),
[Codex policy arguments](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_codex/command.rs).

An unsupported configuration query resolves to unavailable; its cancellation and
cleanup remain native responsibilities. Broodling can decline that probe and
apply its admitted execution policy directly, without implementing the probe
protocol or supervising its process. The SDK's explicit environment still
inherits ordinary operating variables before applying overrides. The local
target uses HOME/CODEX_HOME and preserves selected endpoint variables.
Broodling freezes its operating environment and isolated configuration paths as
admitted policy. Excluding GitDelivery alone is not an enforceable no-effect
boundary. Sources: [native configuration query](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_capsule/provider_process/configuration.rs),
[SDK environment](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py),
[local environment fields](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_capsule/provider_process/environment.rs),
[local provider construction](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_local.rs).

The selected Codex 0.153.4 declares `--ignore-user-config`, `--ignore-rules`, and
`--config` as global options, so they remain valid before native `exec resume`.
The first two suppress user configuration and execpolicy `.rules`; they do not
claim to suppress all repository instructions. Its supported
`sandbox_workspace_write.exclude_slash_tmp=true` setting removes broad writable
`/tmp` independently of the native per-execution `TMPDIR` scratch directory.
Sources: [Codex CLI option scope](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/exec/src/cli.rs),
[global config overrides](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/utils/cli/src/config_override.rs),
[sandbox settings](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/config/src/types.rs),
[effective sandbox policy](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/core/src/config/permissions.rs).

Shell sandbox policy does not control every effect-capable extension. Codex Apps
are enabled by default with ChatGPT authentication, and their traffic is outside
sandboxed-command networking controls. Local MCP servers and command hooks can
launch host processes outside the shell sandbox. The selected profile therefore
also declares `features.apps=false`, `features.plugins=false`,
`features.hooks=false`, and `notify=[]`. Legacy notifications require their own
setting; the hooks feature does not disable them. The removed `plugin_hooks`
feature is not a substitute for disabling plugins. These are capability choices,
not Broodling supervision. Sources: [official capability reference](https://learn.chatgpt.com/docs/config-file/config-reference),
[pinned feature definitions](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/features/src/lib.rs),
[local MCP launch](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/codex-mcp/src/rmcp_client.rs),
[command hook launch](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/hooks/src/engine/command_runner.rs),
[notification configuration](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/hooks/src/registry.rs).

**The no-effect profile requires a trusted host without operator-managed MCP or
other effect-capable extensions.** Clean isolated homes and ignored user config
provide no project trust entries. Codex resolves trust from non-project layers
before loading project configuration; without external trust, candidate project
configuration cannot authorize itself. This does not suppress normal repository
guidance. System and enterprise-managed configuration still apply and can grant
project trust or configure extensions. There is no public blanket CLI MCP-off
setting in the pinned release: `mcp_servers={}` merges recursively and does not
erase lower-layer servers. Administrators can impose the stronger supported
restriction with an empty `[mcp_servers]` allowlist in `requirements.toml`.
Broodling neither interprets managed configuration nor claims that CLI settings
override administrator policy. Sources: [project trust and configuration layers](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/config/src/loader/mod.rs),
[config merge semantics](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/config/src/merge.rs),
[MCP requirement enforcement](https://github.com/openai/codex/blob/3d2ee51ca2d5db578f328aa75e20aa22c0197c9a/codex-rs/core/src/config/mod.rs),
[official hook trust behavior](https://learn.chatgpt.com/docs/hooks).

**A local terminal result is not a general physical cessation receipt.**
Normal execution checks native process cleanup before settlement, and force stop
closes native run activity. Local containment is nevertheless a process group
with Linux subreaping and group termination; dedicated UID/group containment
belongs to hosted pools. A descendant that creates a new session escapes the
local group. Reopening a dead local controller finalizes a nonterminal run as
`runtime_lost` without reconstructing its runtime. Its portable cleanup adapter
can acknowledge absence without inspecting the former provider tree. The public
result exposes no additional physical cleanup receipt. These boundaries did not
change between 10.2.9 and 10.3.0. Sources: [runner selection](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_capsule/provider_process.rs),
[local containment](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/execution/process.rs),
[Unix process handling](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/execution/process/platform_unix.rs),
[observer reopening](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_portable_controller/controller.rs),
[portable cleanup](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_portable_controller/engine.rs).

Broodling's chosen response is conservative quarantine for every dispatched
Attempt. Abandonment removes currentness before requesting native stop, but no
terminal label, including success or `force_stopped`, authorizes physical
retirement or replacement. Never-dispatched owned workspaces retain the ordinary
safe cleanup path. This is lifecycle policy in response to a capability gap, not
an attempt to supervise or recover Zeroshot execution. Removing the old PID
namespace receipts and launch fences changes the supported cleanup claim; it does
not reinterpret their historical counterexamples as passing. Historical source:
[abandonment design and qualification links](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/docs/implementation/v1-p4-abandonment.md).

## Resulting ownership

Broodling retains immutable Work Unit/Attempt admission, source entitlement,
the frozen request and admitted instructions, no-effect policy, run correlation,
current-Attempt authority, and atomic/idempotent lifecycle and effect decisions.
The frozen request remains stored lifecycle authority; it is not an independent
proof that execution consumed no additional repository guidance or context.
Zeroshot owns the standard execution/review/repair workflow and its eventual
result. Broodling does not independently establish criterion evidence, recreate
adjudication, capture intermediate occurrences, derive candidate generations,
or reconstruct execution provenance. The user's selected product scope replaces
those historical implementation requirements; old evidence remains historical.
Context: [original admission design](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/docs/implementation/v1-p2-admission-nucleus.md),
[former adapter](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/broodling/zeroshot_sdk.py),
[previous ownership audit](pr48-ownership-audit.md).

## Investigation validation

The initial 10.2.9 wheel was checksum-verified and its Python source compared with
that exact release checkout. The newly published 10.3.0 wheel was separately
checksum-verified and its binary reports 10.3.0. The only Python source difference
is a `UniformRuntime` docstring adding the Copilot harness; client, result and
exception behavior are unchanged. The release delta also adds gateway providers
and Copilot execution, neither of which Broodling selects. Source:
[exact release comparison](https://github.com/the-open-engine/zeroshot/compare/v10.2.9...v10.3.0).

Separate disposable Git workspaces exercised both released Python SDK/native
pairs without invoking a provider: preset discovery, worker-free submit/wait,
duplicate submission identity, reopening and consuming the same terminal result,
force stop preserving an existing result, and changed content raising the typed
submission conflict all passed. These are integration-seam checks, not tests of
the software-change graph, provider judgment or physical containment. Product
workflow validation belongs in the implementation's focused integration tests.
No historical qualification record was changed during this audit.
