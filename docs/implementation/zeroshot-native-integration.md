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

## HTTP submission preparation

`PrepareHttpSubmission(attemptId, directOrigin)` freezes one HTTP Attempt's
complete request without target contact or dispatch credentials. The
[prepared record](../../src/Broodling/HttpSubmission.cs) is the preparation fact.
It implies neither dispatch intent nor native acceptance. The origin must be
canonical HTTPS or literal-loopback HTTP; the status reader uses the same check.

Preparation first establishes exact B1 custody: the direct
`refs/broodling/starting/<B1>` pin in the Attempt's common Git directory. It then
captures the result-fetch origin from that directory's `remote.origin.url`. The
origin must name the admitted GitHub repository and carry no user information. A
crash after pinning but before the SQLite commit leaves only the pin, which grants
nothing. Preparation then rechecks current authority and pause in one immediate
transaction, including the explicit-replacement exception. That transaction
commits all of the following together:

- The stock request `{runId, submission: {title, graph, runtime,
  initialInput: {task}, source: {repository, branch, revision}, submissionKey}}`,
  without `connections` or `githubToken`. `graph` and `runtime` are the approved
  asset's values, and `task` is the same complete Contract, entitled bytes and B1
  authority that the bridge freezes. `source` names the admitted owner/repository
  and the authorized PR branch, separate from exact original B1.
- The intended run ID, a canonical UUIDv7 generated only when no record exists,
  and the key `broodling:http:v1:<attempt-id>`.
- The exact asset bytes in the content-addressed `execution_assets` row.
- A Broodling-only binding that is never sent: protocol
  `zeroshot.native-v2-target/v2`, the target origin, the common Git directory,
  the frozen result-fetch origin and the native release pins.

Concurrent preparers converge on the first committed record; a loser never
generates or compares another identity. Repeating preparation does not read the
installed asset. It validates the retained record against the retained asset
bytes, the approved identity and native binding, and a rebuild of the request
from admitted authority. A missing or corrupt asset, a request whose graph,
runtime or task differs, an unsupported binding or a different target origin
refuses. Nothing is regenerated or rebound.

The record's retained binding is a `NativeRunBinding` with a `direct` locator
whose `SdkVersion` is null, since no bridge SDK is involved. The bridge
`PrepareSubmission`/`DispatchAsync` refuse HTTP Attempts.

## HTTP dispatch and acknowledgement

`DispatchHttpAsync(attemptId, credentials)` sends an already prepared HTTP
submission for the first time, or replays it exactly after a lost
acknowledgement. It never prepares implicitly. A correlated record is returned
at once, with no authority, pause, credential, custody or target requirement.

Otherwise the caller must hold current Attempt authority, the record must carry
no retained replay block and the installation must be unpaused. The current
`GH_TOKEN`, `GATEWAY_API_KEY` and exact gateway URL must be valid, and the
common Git directory must hold the direct `refs/broodling/starting/<B1>` pin with
B1's snapshot objects. These checks never create or repair the pin. An absent,
symbolic or conflicting pin refuses. The credential and Git checks run with no
lock or writer held. The operation then takes the installation initiation lock.
One immediate transaction rechecks authority, the replay block, the pause and
the retained record against its retained asset and admitted authority. For a
first send it commits `prepared → dispatched`. The writer is released before
discovery, and the lock is released when this caller's send ends. The recorded
result-fetch origin is used as retained; remote configuration is not read again.

The adapter validates discovery, then posts the frozen request plus
`connections.gateway` (`GATEWAY_BASE_URL`, `GATEWAY_API_KEY`),
`connections.github` (`GH_TOKEN`) and the outer `githubToken` to
`/native-v2/run`. The request has a known `Content-Length` and no transfer
encoding. The final credential-bearing body must fit the 4 MiB limit before any
exchange starts. The whole operation shares the 60-second submit budget.
Credentials never enter SQLite, the frozen request or a diagnostic, and rotating
them changes neither identity nor the request bytes.

Only HTTP 200 whose body is exactly `{runId}`, equal to the intended canonical
UUIDv7, is an acknowledgement. A different or re-cased ID is `foreign_run`, and
any other 200 body is `invalid_response`. A valid stock problem `{code, message,
details?}` with HTTP 409 and `request.conflict` is a submission conflict; it
carries no run ID and nothing is adopted from it. Other valid problems are
`TargetError`; malformed ones are `invalid_response`. Timeouts, cancellation,
caller death and every refusal preserve the existing facts, so the intent stays
unresolved and exact replay remains available while the Attempt is current.
The stock target answers only after its checkout, and it can create a run yet
reply `503 target.unavailable` (for example when exact B1 is missing from the
forge). A slow checkout can exhaust the submit budget. In each case the intent
stays unresolved, and an exact replay returns the same ID. For the same key, the
stock target answers a different proposed ID with the original ID. That reply is
`foreign_run`, never adopted.

Settling a reply uses a second short transaction. It rechecks only the stored
Attempt, request and identity binding, not custody, credentials, installed files
or current authority. An acknowledgement commits `dispatched → correlated` with
`run_id = intended_run_id`, even after pause, abandonment or custody loss. A
conflict records `replay_blocked_reason = submission_conflict` once, in either
phase. It blocks every later send but does not settle dispatch, so an
acknowledgement for a request already in flight still correlates. Both orderings
converge on the same correlation and conflict fact. A caller whose conflict
arrives after correlation gets the correlated record back. A correlation that
arrives after abandonment raises `StaleAttempt` without restoring authority. A
duplicate acknowledgement after another caller completed the Attempt returns the
retained record. Abandoned or replay-blocked work is never sent again to
discover its run.

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

For an HTTP record, `ObserveAsync` routes on the retained `http.v1` format and
ignores any bridge transport. A merely prepared record returns null without
contacting the target. A dispatched record without acknowledgement is read
through its intended run ID. A correlated record is read through its confirmed
ID. Every observation reports that identity as `Intended` or `Confirmed`, so a
caller cannot infer acknowledgement from available progress. The read is one
session and status request under the 10-second progress budget, and it is
validated by the [run status reader](#directtarget-run-status-reader) against
the retained title, size and PR source. The read also works while the
installation is paused or after abandonment. It writes nothing and never
correlates, completes or abandons. An unknown, foreign or malformed run, a
timeout or transport loss is an unavailable observation with that fixed kind.
Bridge observations always report `Confirmed`.

Completion rechecks currentness, admitted Contract, invocation/run binding and
exact authorized delivery. Native failure records abandonment; invalid receipts,
late success after abandonment and no-effect stable-result gaps refuse successful
completion. Store errors grant no partial disposition.

## DirectTarget HTTP transport limits

[`DirectTargetExchange.cs`](../../src/Broodling/DirectTargetExchange.cs) holds the
fixed client bounds of the selected
[HTTP/OECP contract](https://github.com/faviann/broodling/issues/167#issuecomment-5823939438).
Target readiness discovery, HTTP submission, the run status reader below and
public HTTP observation, wait and stop use it. The bounds are internal
constants, not operator settings:

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

### DirectTarget run status reader

[`DirectTargetSession.cs`](../../src/Broodling/DirectTargetSession.cs) is the one
validated status reader that progress, wait and stop share. Its input is a `NativeRunBinding`: the direct
locator's retained origin, the run ID, frozen title, size and PR source. The origin
must be canonical HTTPS or literal-loopback HTTP, with no path, query, fragment or
user information. The binding carries no credentials.

Opening a session validates discovery and posts exactly `{runId}` to
`/native-v2/oecp-session`. The reply must be HTTP 200 with an `endpoint` and an
absent or null `bearerToken`. The endpoint must equal the origin's paired
`ws`/`wss` authority plus `/native-v2/oecp`, and is checked before connecting.
The WebSocket upgrade uses the same redirect-, proxy- and cookie-free handler.
`initialize` must return the pinned stock reply exactly. At native revision
`054ad3fd` that reply is constant: the full graph profile, logs, agent attach and
an empty controller status. The stock controller reports that status for every
connection, and neither the session nor initialization proves that the run exists.

Each `run/status` request names the retained run. The session keeps one
outstanding JSON-RPC request with string IDs. A reply must be exactly
`{jsonrpc: "2.0", id, result}` or `{jsonrpc: "2.0", id, error}` for that ID.
Batches, notifications, stale or wrong IDs and unknown envelope fields refuse.
The projection must be exactly `{runId, title, source, size, atCursor, status}`,
with run, title, size, repository, branch and B1 equal to the binding. The
reader accepts only the pinned phase union. `admitted` carries only its phase.
`running` and `stopping` also list active executions with their nodes.
`finished` adds a terminal result and optional metadata. Unknown fields and
contradictory variants refuse. `atCursor` must be a string, never interpreted.
Output is opaque within the transport bounds. Metadata is validated and dropped:
absent metadata means empty, explicit null refuses. The reader passes through the
failure labels `force_stopped`, `runtime_lost` and `runtime_failed`. It maps every
other valid native label to `native_failed`.

The validated read returns progress (phase and active nodes) and, for `finished`,
a `NativeResult`. Reading a finished status consumes no completion and establishes
no acknowledgement. Setup and each request run inside the caller's budget, so
wait and stop can reuse one session under their own remaining deadline. Failures
are fixed kinds that never contain remote text. `foreign_run` means the
projection names a different run or source. `RunNotFoundError` is OECP
`NOT_FOUND`. `TargetError` covers any other valid OECP error or HTTP problem.
`invalid_response` covers every malformed reply. An unsupported binding,
discovery or initialization refuses as an unsupported runtime.

One polling loop serves wait and stop. A terminal status returns at once.
After each nonterminal status the loop pauses two seconds, then reads again,
with one read outstanding. The pause runs under the enclosing budget and
responds to caller cancellation. It never sends `run/watch`, never retries and
never compares cursors, so a repeated cursor cannot hide a new terminal result.
The persistence-free wait operation in
[`DirectTargetRun.cs`](../../src/Broodling/DirectTargetRun.cs) opens a fresh
session and reads immediately. Setup and that first read share 30 seconds. Each
later read then has its own 10 seconds through the complete reply. The wait has
no overall deadline. Cancellation or any failure detaches the caller without
stopping the run.

The persistence-free stop operation uses one 30-second budget for discovery,
setup, an optional precheck, force and any polling. For an intended but
unacknowledged run identity it first requires a valid matching status. Any
unknown, foreign, malformed or unavailable precheck sends no force. A matching
precheck, even a finished one, still leads to force. `run/force` names exactly
the session's run and is sent once. Its reply goes through the same projection
validator. A nonterminal reply continues through the polling loop within the
remaining stop budget. The result is a closed value: `NotSent`, `Uncertain` or
`Terminal` with the validated result. `NotSent` and `Uncertain` carry a fixed
error kind. Any failure from the force request onward, including expiry, counts
as `Uncertain`, because force may have been sent. The operation never fabricates
a stopped result. Caller cancellation propagates as cancellation, even after force.
No outcome proves physical cessation or establishes correlation.

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
