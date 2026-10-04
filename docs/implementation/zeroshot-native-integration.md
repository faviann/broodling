# Native Zeroshot integration

The [current architecture](../governing/current.md) governs product responsibility.
C# owns the application; the [dispatch seam](dotnet-native-dispatch.md) is the
detailed reference for the LocalTarget bridge's frozen invocation, policy, SDK
transport and correlation, and this document for the HTTP DirectTarget.
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
| Empty | LocalTarget worktree Attempt through the SDK bridge, `delivery=none`. Native success has null output and no stable accepted result; successful disposition refuses. |
| Exactly one GitHub `pull_request` naming a target branch | HTTP DirectTarget Attempt with the approved `delivery=pull_request` asset and frozen repository, authorized branch and original B1 selectors. A matching `v2/pr/ready` receipt supplies a stable non-B1 `headRevision`. The bridge never carries PR work. |
| Other, mixed, multiple or underspecified | Refusal; no implicit fallback or wider effect. |

PR delivery includes native commit, push, open-or-update and native's readiness
assessment of the PR: `ready` means its required checks and policy gates passed
by native's own classification, with PR feedback considered. It promises neither
human approval, semantic correctness nor merge, and native requests no merge. Broodling retains the full matching receipt and commits
completion/current-authority loss atomically for the exact Attempt.

## Pinned dependencies and bridge

The two execution targets pin their native separately.

| Pin | Owner and consumers |
| --- | --- |
| LocalTarget bridge: SDK 10.3.0.post1 and its bundled `zeroshot 10.3.0` | [`NativeProfile`](../../src/Broodling/NativeProfile.cs) `SdkVersion`/`NativeVersion` and [bridge/requirements.txt](../../src/Broodling/bridge/requirements.txt). The bridge version handshake, each `local` locator and the Python environment use only these. |
| DirectTarget binding: native release, source revision, Linux x86-64 executable and approved execution asset | [`DirectTargetBinding`](../../src/Broodling/DirectTargetBinding.cs), recorded in the [approval manifest](../../src/Broodling/execution-assets/approval.json). Asset loading, each prepared submission's retained binding and its reopen/dispatch check, target readiness, [`generate.sh`](../../src/Broodling/execution-assets/generate.sh) and the release record use only these. Both image recipes carry a copy of the binding's release archive pin; the DirectTarget image installs its `zeroshot` and the `restic` native run allocation requires, and target readiness checks both executables' SHA-256. |

A DirectTarget binding change is a change to the Execution asset and native an
HTTP Attempt's Prepared submission is bound to. It leaves the no-effect LocalTarget
bridge, its Python environment and its retained `local` locators unchanged;
`NativeTransportTests` ties the bridge dependency file to the handshake SDK.
The pins name different natives: the bridge keeps SDK 10.3.0.post1 and its
bundled `zeroshot 10.3.0`, while the DirectTarget binding is `zeroshot 10.10.0`
from its official release archive.
The target image's Codex is likewise its own readiness pin, apart from the
LocalTarget host's `CodexProfile`. The gateway URL remains one shared constant:
the bridge does not use it, while the bundled proposer calls it and DirectTarget
dispatch credentials and the approved asset policy require it.
`NativeProfile.Runtime()` is the bridge's runtime only; an HTTP Attempt's runtime
is the asset's.

Submission (#216) goes through the pinned
[Zeroshot.Client](https://github.com/faviann/zeroshot-dotnet-sdk) `0.2.0-preview.1`
SDK. [`DirectTargetClient`](../../src/Broodling/DirectTargetClient.cs) is
Broodling's one thin configuration of it: the SDK's supported HTTP handler with
Broodling's TLS trust and no proxy, bound to the DirectTarget binding's native
release. The reader and stopper (`DirectTargetRun`, and the
`INativeReader`/`INativeStopper` seams the application operations accept) and
readiness discovery still use `DirectTargetExchange`; later slices move them onto
the same client. Broodling keeps authority over the Prepared submission,
Dispatch intent, Native correlation, Native observation and Attempt retirement.
The SDK persists nothing.

The bridge uses the official
[SDK 10.3.0.post1](https://github.com/the-open-engine/zeroshot/releases/tag/zeroshot-python-v10.3.0_1),
whose Linux x86-64 wheel bundles native 10.3.0; its exact URL and SHA-256 are in
[bridge/requirements.txt](../../src/Broodling/bridge/requirements.txt). The
DirectTarget uses the official
[v10.10.0 release](https://github.com/the-open-engine/zeroshot/releases/tag/v10.10.0)'s
Linux x86-64 musl archive; its URL and SHA-256 are in the two image recipes and
the approval manifest's `native.release`.

The sole production Python source file is
[zeroshot_bridge.py](../../src/Broodling/bridge/zeroshot_bridge.py). It serves
only no-effect LocalTarget work: it translates one version/submit/wait/stop/status
request for a `local` locator into the official SDK, returns public fields or
typed error classification and exits. It owns no Broodling policy, database,
lifecycle or recovery, and it carries no dispatch credentials. C# validates
versions and owns authority, same-key reconciliation and receipt validation, and
refuses a `direct` locator before starting Python. Controlled Python SDK/provider
fixtures remain test-only.

The fixed PR runtime is uniform Codex / `gateway` / `gpt-5.6-sol` / medium /
small / execution-scoped sessions through exactly
`https://cliproxy.local.faviann.com/v1`, carried by the approved asset below.
Codex stays **0.153.4**. The no-effect LocalTarget uses Codex/OpenAI. Per-node
runtime, model/harness selection, node-local OAuth PR delivery and fleet
placement remain unsupported.

## Approved DirectTarget execution asset

The HTTP DirectTarget path (#163) submits one release-bundled graph/runtime,
[`execution-assets/software-change-pr-codex-gateway-10.10.0.json`](../../src/Broodling/execution-assets/software-change-pr-codex-gateway-10.10.0.json):
native 10.10.0's stock `software-change` template with `pull_request` delivery
(#214; #226 regenerated the same bytes with 10.10.0). Its identity is the
SHA-256 of the exact file bytes, formatting included:
`258dc0ab46f30f05d6c95f7be493ede2ad0963160b9247f5ccdb699e4dcc20fc` (79,660
bytes). This identity is Broodling's approval. It is not a stock `profileId` or a
native digest. The [approval manifest](../../src/Broodling/execution-assets/approval.json)
(#215) records the review that approved it and binds:

- The asset's bytes and SHA-256.
- Native `zeroshot 10.10.0` from source `3ee1192cec359a0b997f464e703a936e8b67d63c`,
  the Linux x86-64 executable `d0c84ffb…bb923e`, and the official `v10.10.0`
  musl release archive (`fbc13b23…28d6f16`) with its `restic` executable
  (`90ab22a5…8a8cf`).
- The policy: Codex / `gateway` / `gpt-5.6-sol` / medium / small / execution
  sessions, the symbolic `gateway` connection on every agent node, native's
  `github` delivery connection, native's default `consider` PR feedback and the
  gateway URL. Native serializes both defaults by omitting `sessionScope` and
  `pullRequestFeedback`.
- The recipe: `generate.sh` and `describe.py` by SHA-256, from revision
  `ebab6f40be923dba37c26a33e00ce94cd7761a71`, with its command, uniform runtime
  and output format.
- The asset structure that
  [`describe.py`](../../src/Broodling/execution-assets/describe.py) reads: six
  executable nodes, the `builtin.git-delivery.pr@2` `v2`/`pr` receipt with
  outcomes `ready`, `ci_failed`, `conflict` and `repair_required`, the three
  non-ready outcomes routing to `delivery_repair`, the ten-iteration
  `change_loop` and no node `timeoutMs`.

The asset and manifest hold no credential values. The asset also omits the
gateway URL, which `DispatchCredentials` enforces separately.

[`ExecutionAsset.LoadBundled`](../../src/Broodling/ExecutionAsset.cs) checks the
packaged files against the SHA-256 of the asset and of the whole reviewed
approval manifest compiled into `DirectTargetBinding`. It refuses a missing,
changed or unapproved asset, including the superseded 10.3.0 asset, and any
other manifest, such as one naming another native release, policy or recipe
revision. `ExecutionAssetTests` checks that the manifest's asset and native
blocks are the binding's and that its policy is the asset's own runtime. C# passes the graph and runtime through opaquely. It never expands,
edits or regenerates them. Changing the asset requires a reviewed release with
a new approved identity.

[`generate.sh`](../../src/Broodling/execution-assets/generate.sh) is the
build-time recipe. It reads one manifest, by default the approval manifest, and
takes the native executable that manifest pins. It refuses a changed recipe file.
It runs `profile set --template software-change --delivery pull_request
--uniform-runtime-config` with the manifest's uniform runtime, then `profile
show`, twice, each under `env -i` with an empty temporary HOME,
`ZEROSHOT_CONFIG_DIR` and `ZEROSHOT_STATE_DIR`. It requires both generations and
the manifest's asset to have identical bytes and the structure `describe.py`
reads to equal the manifest's. It then re-admits the graph/runtime through a
fresh `profile set --graph --runtime-config` and requires an exact round trip.
It runs no target or provider. It is not a runtime helper. It also accepts a
`broodling.execution-asset-candidate/v1` manifest for reviewing a future
candidate before approval. The Broodling
[image build](../../deployment/README.md#broodling-image) runs it with the
pinned native and fails unless the published asset equals its output.

With `--fetch`, it takes the executable from the pinned release archive in a
cache directory. It downloads the archive only when that archive is absent,
after checking that the release tag names the pinned commit and that the release
`SHA256SUMS` lists the archive checksum. The standalone check needs network
access on its first run:

```bash
src/Broodling/execution-assets/generate.sh --fetch ~/.cache/broodling-native
```

## HTTP submission preparation

`PrepareHttpSubmission(attemptId, directOrigin)` freezes one HTTP Attempt's
complete request without target contact or dispatch credentials. The
[prepared record](../../src/Broodling/HttpSubmission.cs) is the preparation fact.
It implies neither dispatch intent nor native acceptance. The origin must be
canonical HTTPS or HTTP to exactly `127.0.0.1` or `[::1]`, as the SDK also
requires; the status reader uses the same check.

Preparation first establishes exact B1 custody: the direct
`refs/broodling/starting/<B1>` pin in the Attempt's common Git directory. It then
captures the result-fetch origin from that directory's `remote.origin.url`. The
origin must name the admitted GitHub repository and carry no credentials: a URL
has no user information except the `git` user of an `ssh://` URL. A
crash after pinning but before the SQLite commit leaves only the pin, which grants
nothing. Preparation then rechecks current authority and pause in one immediate
transaction, including the explicit-replacement exception. That transaction
commits all of the following together:

- The stock request `{runId, submission: {title, graph, runtime,
  initialInput: {task}, source: {repository, branch, revision}, submissionKey}}`,
  without `connections` or `githubToken`. `graph` and `runtime` are the approved
  asset's values, and `task` is the same complete Contract, entitled bytes and B1
  authority that the bridge freezes. For a bundle-bound Contract, whose only
  entitled source is the Executable Request, the task adds the bound
  RequestBundle's compact manifest and reference access; see
  [frozen-reference access](#frozen-reference-access). `source` names the
  admitted owner/repository and the authorized PR branch, separate from exact
  original B1.
- The intended run ID, a canonical UUIDv7 generated only when no record exists,
  and the key `broodling:http:v1:<attempt-id>`.
- The exact asset bytes in the content-addressed `execution_assets` row.
- A Broodling-only binding that is never sent: protocol
  `zeroshot.native-v2-target/v2`, the target origin, the common Git directory,
  the frozen result-fetch origin and the DirectTarget binding's native
  release, source and executable.

Concurrent preparers converge on the first committed record; a loser never
generates or compares another identity. Repeating preparation does not read the
installed asset. It validates the retained record against the retained asset
bytes, the approved identity and native binding, and a rebuild of the request
from admitted authority. A missing or corrupt asset, a request whose graph,
runtime or task differs, an unsupported binding or a different target origin
refuses. Nothing is regenerated or rebound.

### Frozen-reference access

A new bundle-bound task (#114) carries a `requestBundle` object beside the
Contract, the Executable Request and B1: the bundle identity, the manifest digest
the Contract binds, and each member in manifest order with its reference ID,
capture kind, selector (the capture's JSON; any other selector stays base64) and
content digest, plus the pinned commit and path of a Git capture. It carries no
reference bodies. The text tells agents to read a reference on demand with
`/usr/local/bin/broodling-reference <bundleId> <referenceId>` and that a
reference adds no work, cannot amend the Contract or Executable Request and
authorizes no effect. Preparation derives it from the digest-verified retained
manifest only, never from deployment configuration.

The helper is installed in the [DirectTarget image](../../deployment/DirectTarget.Dockerfile).
It sends one `GET /bundles/{bundleId}/reference?id=<referenceId>` to the existing
read-only HTTP reader and prints the exact captured bytes. The reader's sealed
membership rules decide what is readable, so the helper cannot refresh or extend
a bundle, substitute candidate files or perform effects. Its reader origin is
the image file `/etc/broodling/reader-origin`, `http://broodling:8080` by
default: the `broodling` Compose service on the single project network of
[ADR 0001](../adr/0001-directtarget-https-origin-and-compose-topology.md).
Native runs agents with a cleared environment, so this is a file rather than an
environment variable. The helper is not a client submission process and needs no
shared execution worktree.

The record's retained binding is a `NativeRunBinding` with a `direct` locator
whose `SdkVersion` is null, since no bridge SDK is involved. The bridge
`PrepareSubmission`/`DispatchAsync` refuse HTTP Attempts, and a worktree Attempt
refuses an authorized-PR Contract at admission, so PR work cannot reach the bridge.

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
any target contact, and the lock is released when this caller's send ends. The recorded
result-fetch origin is used as retained; remote configuration is not read again.

Preparation fixes the request as an SDK `PreparedSubmission` and retains its
exact exported UTF-8 bytes. Dispatch imports those retained bytes again and makes
one `ZeroshotClient.SubmitAttemptAsync` to `/native-v2/run`, with
`connections.gateway` (`GATEWAY_BASE_URL`, `GATEWAY_API_KEY`),
`connections.github` (`GH_TOKEN`) and the outer `githubToken` supplied
separately; the SDK appends them to the unchanged bytes. It sends once, never
retries, and refuses a credential-bearing body over 4 MiB before sending. The
whole operation shares the 60-second submit budget. Credentials never enter
SQLite, the frozen request or a diagnostic, and rotating them changes neither
identity nor the request bytes.

Broodling classifies the SDK's attempt. Only an acknowledgement naming exactly
the intended run ID correlates; one that names another or re-cased ID is
`foreign_run`. When the named ID is a canonical lowercase UUID, the error's
message, and so the progression failure detail, names it, as does the
`acknowledgedRunId` of the CLI and HTTP error records; any other target text is
never echoed. It is neither adopted nor retained. A captured valid acknowledgement counts even when cancellation
raced it. The SDK's pinned `request.conflict` refusal (HTTP 409) is a submission
conflict; it carries no run ID and nothing is adopted from it. Other valid
problems are `TargetError`; a malformed reply is `invalid_response`. Timeouts, cancellation,
caller death and every refusal preserve the existing facts, so the intent stays
unresolved and exact replay remains available while the Attempt is current.
Native 10.10.0 acknowledges once it has recorded the run and prepares the
execution environment, including the checkout of exact B1, afterwards; a
missing B1 fails that correlated run. A lost reply or a target error after the
run was recorded leaves the intent unresolved, and an exact replay returns the
same ID. For the same key, the
stock target answers a different proposed ID with the original ID. That reply is
`foreign_run`, never adopted.

Settling a reply uses a second short transaction. It checks only that the stored
record is still the HTTP record and intended run ID that was sent, whose content
SQL guards keep immutable, not custody, credentials, installed files or current
authority. An acknowledgement commits `dispatched → correlated` with
`run_id = intended_run_id`, even after pause, abandonment or custody loss. A
conflict records `replay_blocked_reason = submission_conflict` once, in either
phase. It blocks every later send but does not settle dispatch, so an
acknowledgement for a request already in flight still correlates. Both orderings
converge on the same correlation and conflict fact. A caller whose conflict
arrives after correlation gets the correlated record back. When a correlation
arrives after abandonment, it is committed first. The ordinary
`StopAsync(attemptId, reason, null)` then forces exactly the confirmed run, and
the call raises `StaleAttempt` without restoring authority.
`NativeStopRequested` is true when force was sent, whether it reached a terminal
result or an uncertain outcome. It is false when setup failed before force; an
explicit stop can address the run later. Caller cancellation propagates after
correlation is retained. A duplicate acknowledgement after another caller
completed the Attempt returns the retained record without abandoning or
stopping it. Abandoned or replay-blocked work is never sent again to
discover its run.

## Composed invocation and target selection

`Invocation` takes one explicit `InvocationTarget`. `Local(workspaceRoot, profile,
transport)` admits a worktree Attempt and dispatches it through the bridge.
`Direct(origin)` admits an HTTP Attempt, prepares or reopens its submission at
that origin and dispatches it with the caller's current `DispatchCredentials`.
It needs no Python executable, SDK client state, workspace root or launcher.
`ResumeSubmissionAsync(submissionId, credentials)` takes the same Direct path for
one Issue submission's bundle-bound Contract; its first Attempt starts from the
RequestBundle's retained B1, and a Local target refuses before any work.
An existing Attempt continues only through the kind its retained resources name:
a Direct target on a worktree Attempt, a Local target on an HTTP Attempt, or a
Direct origin that differs from the retained binding refuses before any
allocation, preparation or target contact. A correlated, completed or ended
Attempt is handed back from retained state without configuration, credentials or
target contact. `WaitAsync` routes on the retained record and passes the bridge
transport only to a LocalTarget record.

`InvocationTarget.Direct` refuses an origin that is not canonical HTTPS or
HTTP to exactly `127.0.0.1` or `[::1]`. The operator configuration names `"target": "direct"` with
a `directOrigin` that passes that rule and an optional absolute
`directRootCertificate`, or `"target": "local"` with only the bridge, state,
workspace and all four Codex-profile paths. Mixed, unknown or secret fields
refuse. Operator `wait` and `stop` take the same optional configuration: a
LocalTarget record uses its pinned SDK Python, and an HTTP record uses a Direct
configuration's root certificate. A supplied configuration of the other kind, or
a Direct origin that differs from the retained binding, refuses before target
contact or abandonment. See the [release guide](../../deployment/README.md#invocation-configuration).

## Dispatch, recovery and completion

For the LocalTarget bridge, preparation retains the immutable request and
submission key. A short transaction commits dispatch intent before the external
SDK call; no SQLite writer spans it. Concurrent callers submit the identical
request and converge through native submission-key idempotency. An
acknowledgement arriving after abandonment is retained as factual correlation
without restoring authority.

Bridge acknowledgement-loss replay uses only current authority and the exact
frozen invocation. Native conflict alone cannot establish safe recovery; the
narrow owned-source/HEAD-drift case is checked in C#. Abandoned unknown-run work
is never replayed to discover execution. HTTP dispatch and replay follow the
[section above](#http-dispatch-and-acknowledgement).

After durable correlation, wait/stop receive the retained run binding (locator,
run ID, frozen title, size and, for HTTP, PR source) through separate read and
stop roles. The bridge uses only the frozen local locator and run identity with
an empty explicit SDK environment. Neither kind needs an old workspace or
dispatch credentials. Cancelling/killing a waiter detaches that caller rather
than stopping native execution. Completed receipt replay needs no target.

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
Target readiness discovery, the run status reader below and public HTTP
observation, wait and stop use it; submission uses the SDK's own bounds. The bounds are internal
constants, not operator settings:

| Resource | Limit |
| --- | --- |
| HTTP JSON body in either direction; assembled incoming WebSocket message | 4 MiB, counted as bytes arrive regardless of chunking, fragmentation or declared length |
| Outgoing WebSocket message | 1 MiB, refused before sending |
| HTTP response headers / JSON nesting | 32 KiB / 64 levels; duplicate properties and undecodable strings refuse at any level |
| Operation budgets | Progress 10s, submit 60s, stop 30s, wait setup and first status 30s, each later wait read 10s |

One budget encloses every exchange in an operation: setup, headers, body or
message assembly and parsing. After caller cancellation or expiry, no further
exchange starts. Caller cancellation propagates as cancellation. Expiry, by
contrast, becomes the fixed `TimeoutError` kind. The budgets limit only client
operations, never native execution. The HTTP client disables redirects, proxying,
cookies and ambient credentials. It always verifies TLS, including the host
name, with the trust `DirectTargetClient` also gives the SDK. By default it uses system trust. A store session opened with
`OpenStore(path, directTargetRootCertificate)` instead trusts exactly that PEM
root for HTTPS and WSS, with custom root trust that ignores the system store.
Each TLS handshake rereads the root file and accepts only a matching host name
and a server-authentication chain from the presented certificates to that root,
with revocation unchecked as for ordinary TLS. The file is never read when the
store opens. A missing or unreadable file fails that connection, and so its
operation, as `transport_failed`. A dispatch also checks that the root is
readable before its intent commits, so that failure records nothing. A wait
connects during setup and then polls over one open WebSocket, so a root
regenerated mid-wait does not affect it. If the TLS proxy restarts, the wait
detaches with a transport failure, and a new wait connects under the new root.
It never retries. Failures are fixed `NativeTransportError` kinds (`TimeoutError`,
`transport_failed`, `invalid_response`, `request_too_large`) and never include
response bytes. Discovery accepts only the stock
`zeroshot.native-v2-target/v2` document with `authentication: none`, `audience:
controller` and the exact run, session and OECP routes. `privateBootstrapPath`,
`oauth` and `loginSession` may only be absent or null, and `extensions` absent
or an object, whose optional capabilities (native 10.10.0 advertises run history
and workspace recovery/checkpoints) this controller ignores. Any other field refuses as an unsupported runtime. Discovery confirms
protocol shape. It does not attest native or image bytes or durable target state.

### DirectTarget run status reader

[`DirectTargetSession.cs`](../../src/Broodling/DirectTargetSession.cs) is the one
validated status reader that progress, wait and stop share. Its input is a `NativeRunBinding`: the direct
locator's retained origin, the run ID, frozen title, size and PR source. The origin
must be canonical HTTPS or HTTP to exactly `127.0.0.1` or `[::1]`, with no path, query, fragment or
user information. The binding carries no credentials.

Opening a session validates discovery and posts exactly `{runId}` to
`/native-v2/oecp-session`. The reply must be HTTP 200 with an `endpoint` and an
absent or null `bearerToken`. The endpoint must equal the origin's paired
`ws`/`wss` authority plus `/native-v2/oecp`, and is checked before connecting.
The WebSocket upgrade uses the same redirect-, proxy- and cookie-free handler.
`initialize` must return the pinned stock reply exactly. At native revision
`3ee1192c` that reply is constant: the full graph profile, logs, agent attach and
an empty controller status. The stock controller reports that status for every
connection, and neither the session nor initialization proves that the run exists.

Each `run/status` request names the retained run. The session keeps one
outstanding JSON-RPC request with string IDs. A reply must be exactly
`{jsonrpc: "2.0", id, result}` or `{jsonrpc: "2.0", id, error}` for that ID.
Batches, notifications, stale or wrong IDs and unknown envelope fields refuse.
The projection must be exactly `{runId, title, source, size, atCursor, status}`,
with run, title, size, repository, branch and B1 equal to the binding, plus
native's optional `workspaceRecovery`. Native 10.10.0 adds it to a run that
failed with a retained workspace, such as repair exhaustion or a refused PR
identity. It is validated (`recoverable` boolean; optional
`connectionRequirements` object and `resumedFrom`/`successorRunId` strings) and
dropped: Broodling never resumes a run. The
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

**Every dispatched Attempt is ineligible for automatic cleanup or
replacement**, even after native success or force-stop. `StopAsync` commits
abandonment and requests native stop when known; a terminal result still provides
no physical-cessation receipt. Operators retain emergency containment
responsibility. There is no override turning incomplete proof into cleanup
authority. An exactly owned, proven never-dispatched Attempt can be explicitly
retired and replaced from original B1 under the lifecycle seam. Dispatched HTTP
DirectTarget work is retired only by the explicit
[verified maintenance retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement),
after which an abandoned Attempt can be explicitly replaced.

## Resource model for maintenance consumers

[Verified maintenance retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement)
(#122) consumes these retained facts and invents no client worktree to reclaim:

- The Attempt's `resource_kind` (`http` owns no local directory, branch or
  worktree; `worktree` keeps its owned enclosure) and its admission, abandonment,
  retirement and completion records.
- Shared custody: the common Git directory, the direct
  `refs/broodling/starting/<B1>` pin and any `refs/broodling/accepted/<oid>` pin,
  none of which an Attempt cleanup deletes.
- The HTTP submission: format `http.v1`, phase (`prepared`, `dispatched`,
  `correlated`), intended and confirmed run IDs, `replay_blocked_reason`, the
  retained asset identity and the binding (target origin, protocol, native
  release pins, frozen result-fetch origin).
- Dispatch uncertainty: any phase after `prepared` is dispatch intent and keeps
  the Attempt quarantined until verified maintenance retirement. Correlation
  resolves acceptance uncertainty only; retirement resolves neither.

Native checkout paths and runtime state belong to the target. Host
stopped-target and mount verification belong to the host procedure
(homelab-iac#356); Broodling records the check it supplies.

## Evidence and history

The [TUnit suite](../../tests/README.md) covers Broodling authority, Git/SQLite
durability and controlled released-SDK/native behavior. The
[stock DirectTarget witness](../../tests/README.md#controlled-stock-directtarget-witness)
runs the unmodified native 10.10.0 HTTP/OECP target and the approved asset with a
controlled Codex provider and forge. Its PR receipt is controlled, not a real
GitHub PR. None of these establishes provider quality, hostile sandbox resistance
or physical cessation. [P5 remains scoped FAIL](../../evaluation/p5/README.md),
requiring human review of each exact accepted revision.

.NET state uses its own format; Python schema history and application APIs are
retired, with no import or in-flight takeover. The prior implementation and
removed assurance/supervisor machinery remain
[dated history](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/docs/implementation/zeroshot-native-integration.md).
Historical passes do not transfer to the current release.
