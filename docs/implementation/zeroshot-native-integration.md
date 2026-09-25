# Native Zeroshot integration

The [current architecture](../governing/current.md) governs product responsibility.
C# owns the application; the [dispatch seam](dotnet-native-dispatch.md) is the
detailed reference for frozen invocation, policy, SDK transport and correlation.
[Completion](dotnet-receipt-completion.md) and
[stop/retirement/replacement](dotnet-retirement-replacement.md) own lifecycle
decisions. The [release guide](../../deployment/README.md) describes packaging,
target readiness and the separate operational cutover gate.

## Responsibility and selected workflow

Broodling freezes one admitted Work Unit/Attempt with complete entitled sources,
Contract, original B1, source/workspace identity and exact effect authority.
Zeroshot's standard `software-change` workflow owns implementation, independent
acceptance/code review, repair, provider sessions and native delivery.
Broodling consumes public results without reconstructing graph history or adding
a supervisor, separate adjudication record or mechanical-evidence execution.

Criteria alone can be admitted. Evidence population, validation action/seam and
falsifying observation remain optional frozen guidance. Unsupported obligations,
effect-dependent evidence, unresolved prerequisites and selected-final-material
requests still refuse; repository guidance cannot amend stored authority.

| Frozen effects | Native selection and Broodling outcome |
| --- | --- |
| Empty | LocalTarget, `delivery=none`. Native success has null output and no stable accepted result; successful disposition refuses. |
| Exactly one GitHub `pull_request` naming a target branch | DirectTarget, `delivery=pull_request`, with frozen repository, authorized branch and original B1 selectors. A matching `v1/pr/opened` receipt supplies a stable non-B1 `headRevision`. |
| Other, mixed, multiple or underspecified | Refusal; no implicit fallback or wider effect. |

PR delivery includes native commit, push and open-or-update. It promises neither
passing CI nor merge. Broodling retains the full matching receipt and commits
completion/current-authority loss atomically for the exact Attempt.

## Pinned dependency and bridge

The official [Zeroshot 10.3.0 release](https://github.com/the-open-engine/zeroshot/releases/tag/v10.3.0)
and [SDK 10.3.0.post1](https://github.com/the-open-engine/zeroshot/releases/tag/zeroshot-python-v10.3.0_1)
remain selected. The Linux x86-64 wheel bundles the native engine; its exact URL
and SHA-256 live in [bridge/requirements.txt](../../src/Broodling/bridge/requirements.txt).

The sole production Python source file is
[zeroshot_bridge.py](../../src/Broodling/bridge/zeroshot_bridge.py). It translates
one version/submit/wait/stop/status request into the official SDK, returns public fields
or typed error classification and exits. It owns no Broodling policy, database,
lifecycle or recovery. C# validates versions and owns authority, credentials,
same-key reconciliation and receipt validation. Controlled Python SDK/provider
fixtures remain test-only.

The fixed PR runtime is uniform Codex / `gateway` / `gpt-5.6-sol` / medium /
small / execution-scoped sessions through exactly
`https://cliproxy.local.faviann.com/v1`. Codex stays **0.153.4**. The no-effect
LocalTarget uses Codex/OpenAI. Per-node runtime, model/harness selection,
node-local OAuth PR delivery and fleet placement remain unsupported.

## Approved DirectTarget execution asset

The HTTP DirectTarget path (#163) submits one release-bundled graph/runtime,
[`execution-assets/software-change-pr-codex-gateway.json`](../../src/Broodling/execution-assets/software-change-pr-codex-gateway.json).
Its identity is the SHA-256 of the exact file bytes, formatting included:
`10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8` (77,069
bytes). This identity is Broodling's approval. It is not a stock `profileId` or a
native digest. The [approval manifest](../../src/Broodling/execution-assets/approval.json)
binds the asset to native `zeroshot 10.3.0`, source
`054ad3fd6c763b98d12f5b2e90830b97116561ad`, the Linux x86-64 executable
`afeb4372…6ee06`, the fixed runtime policy, the symbolic `gateway`/`github`
connections and the gateway URL. It also records the SDK 10.3.0.post1 generation
provenance. The asset and manifest hold no credential values. The asset also
omits the gateway URL, which `DispatchCredentials` enforces separately.

[`ExecutionAsset.LoadBundled`](../../src/Broodling/ExecutionAsset.cs) checks the
packaged files against the identity and binding compiled into the release. It
refuses a missing, changed or unapproved asset and a manifest bound to another
native release or policy. C# passes the graph and runtime through opaquely.
It never expands, edits or regenerates them. Changing the asset requires a
reviewed release with a new approved identity.

[`generate.sh`](../../src/Broodling/execution-assets/generate.sh) is the
build-time recipe. It takes the pinned native executable, runs
`profile set --template software-change --delivery pull_request
--uniform-runtime-config` and `profile show` with an isolated HOME/config, then
checks the exact approved bytes. It then re-admits the graph/runtime through
`profile set --graph --runtime-config` and requires an exact round trip. It runs
no target or provider. It is not a runtime helper.

## Dispatch, recovery and completion

Preparation retains the immutable request and submission key. A short transaction
commits dispatch intent before the external SDK call; no SQLite writer spans it.
Concurrent callers submit the identical request and converge through native
submission-key idempotency. An acknowledgement arriving after abandonment is
retained as factual correlation without restoring authority.

Acknowledgement-loss replay uses only current authority and the exact frozen
invocation. Native conflict alone cannot establish safe recovery; the narrow
owned-source/HEAD-drift case is checked in C#. Abandoned unknown-run work is never
replayed to discover execution.

PR dispatch/replay requires current `GH_TOKEN`, `GATEWAY_API_KEY` and the exact
gateway URL. Values travel separately from the persisted request. After durable
correlation, wait/stop receive the retained run binding (locator, run ID, frozen
title, size and PR source) through separate read and stop roles; the bridge uses
only the frozen locator and run identity with an empty explicit SDK environment.
They need no old workspace or dispatch credentials.
Cancelling/killing a bridge waiter detaches that caller rather than stopping native
execution. Completed receipt replay needs no target.

`BroodlingStore.ObserveAsync` reads a correlated Attempt's current native phase and
active nodes through the same retained locator and run ID. It returns null
without native contact when no run is correlated. The 10-second observation bound
covers the version preflight and native status read. A cancelled or expired
preflight stops before status starts; the bridge gives the SDK the remaining time
for status, so the SDK stops its own command on timeout. Once status starts, caller
cancellation detaches without killing its bridge. Each read is stamped with its
observation time; it is never persisted
and never updates admission, abandonment, completion or authority. A timeout,
transport loss, unknown run or unsupported runtime returns an unavailable
observation with a safe reason, not an execution failure. Retained status never
contacts native and is unaffected. A `finished` phase is progress only: result
consumption and disposition remain `WaitAsync`'s responsibility.

Completion rechecks currentness, admitted Contract, invocation/run binding and
exact authorized delivery. Native failure records abandonment; invalid receipts,
late success after abandonment and no-effect stable-result gaps refuse successful
completion. Store errors grant no partial disposition.

## DirectTarget HTTP transport limits

[`DirectTargetExchange.cs`](../../src/Broodling/DirectTargetExchange.cs) holds the
fixed client bounds of the selected
[HTTP/OECP contract](https://github.com/faviann/broodling/issues/167#issuecomment-5823939438).
Target readiness discovery is its first caller; submit, observation, wait and
stop do not use it yet. The bounds are internal constants, not operator
settings:

| Resource | Limit |
| --- | --- |
| HTTP JSON body in either direction; assembled incoming WebSocket message | 4 MiB, counted as bytes arrive regardless of chunking, fragmentation or declared length |
| Outgoing WebSocket message | 1 MiB, refused before sending |
| HTTP response headers / JSON nesting | 32 KiB / 64 levels; duplicate properties refuse at any level |
| Operation budgets | Progress 10s, submit 60s, stop 30s, wait setup and first status 30s, each later wait read 10s |

One budget encloses every exchange in an operation: setup, headers, body or
message assembly and parsing. After caller cancellation or expiry, no further
exchange starts. Caller cancellation propagates as cancellation. Expiry, by
contrast, becomes the fixed `TimeoutError` kind. The budgets limit only client
operations, never native execution. The HTTP client disables redirects, proxying,
cookies and ambient credentials and keeps ordinary TLS verification. It never
retries. Failures are fixed `NativeTransportError` kinds (`TimeoutError`,
`transport_failed`, `invalid_response`, `request_too_large`) and never include
response bytes. Discovery accepts only the stock
`zeroshot.native-v2-target/v2` document with `authentication: none`, `audience:
controller` and the exact run, session and OECP routes. `privateBootstrapPath`,
`oauth` and `loginSession` may only be absent or null, and `extensions` absent
or empty. Any other field refuses as an unsupported runtime. Discovery confirms
protocol shape. It does not attest native or image bytes or durable target state.

## Local policy and cleanup limitation

The [C# Codex launcher](../../src/Broodling/CodexLauncher.cs) applies explicit
workspace-write worker/read-only verifier modes, strips sandbox/approval bypasses,
disables shell networking, search, apps/plugins/hooks/notifications and excludes
ambient Codex user config and exec-policy rules. It uses isolated homes, refuses
app-server probing and `execve`s the configured CLI with the same PID/stdin.
It interprets no prompts/results and supervises no execution.

Local HOME starts empty and CODEX_HOME auth-only. Launcher/executable/state remain
outside candidate/shared Git. Current repository guidance can be execution
context but cannot change frozen Contract/source authority.

**The local profile requires a trusted host with no operator-managed
effect-capable MCP or extension configuration.** An empty managed
`[mcp_servers]` allowlist can enforce that precondition. Broodling does not scan
arbitrary managed settings or prove their enforcement. Shell network restrictions
are not a universal no-effect guarantee.

**Every dispatched Attempt is permanently ineligible for automatic cleanup or
replacement**, even after native success or force-stop. `StopAsync` commits
abandonment and requests native stop when known; a terminal result still provides
no physical-cessation receipt. Operators retain emergency containment
responsibility. There is no override turning incomplete proof into cleanup
authority. An exactly owned, proven never-dispatched Attempt can be explicitly
retired and replaced from original B1 under the lifecycle seam.

## Evidence and history

The [TUnit suite](../../tests/README.md) covers Broodling authority, Git/SQLite
durability and controlled released-SDK/native behavior. Stub PR receipts are
distinct from live DirectTarget delivery. Neither establishes provider quality,
hostile sandbox resistance or physical cessation. [P5 remains scoped FAIL](../../evaluation/p5/README.md),
requiring human review of each exact accepted revision.

.NET state uses its own format; Python schema history and application APIs are
retired, with no import or in-flight takeover. The prior implementation and
removed assurance/supervisor machinery remain
[dated history](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/docs/implementation/zeroshot-native-integration.md).
Historical passes do not transfer to the current release.
