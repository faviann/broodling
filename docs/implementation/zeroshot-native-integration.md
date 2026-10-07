# Native Zeroshot integration

The [current architecture](../governing/current.md) governs product responsibility.
C# owns the application. This document is the detailed reference for its one
execution target, the HTTP DirectTarget: frozen invocation, submission, dispatch
intent, correlation and the SDK transport.
[Completion](dotnet-receipt-completion.md) and
[stop/retirement/replacement](dotnet-retirement-replacement.md) own lifecycle
decisions. The [release guide](../../deployment/README.md) describes packaging,
target readiness and the separate operational cutover gate.

## Responsibility and selected workflow

Broodling freezes one admitted Work Unit/Attempt with complete entitled sources,
Contract, original B1, source identity and exact effect authority.
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
| Exactly one GitHub `pull_request` naming a target branch | HTTP DirectTarget Attempt with the approved `delivery=pull_request` asset and frozen repository, authorized branch and original B1 selectors. A matching `v2/pr/ready` receipt supplies a stable non-B1 `headRevision`. |
| Empty | Refusal at Contract admission with a retained `no_required_effect` finding. No Attempt can be allocated; stable no-effect results are future work ([#78](https://github.com/faviann/broodling/issues/78)). |
| Other, mixed, multiple or underspecified | Refusal; no implicit fallback or wider effect. |

PR delivery includes native commit, push, open-or-update and native's readiness
assessment of the PR: `ready` means its required checks and policy gates passed
by native's own classification, with PR feedback considered. It promises neither
human approval, semantic correctness nor merge, and native requests no merge. Broodling retains the full matching receipt and commits
completion/current-authority loss atomically for the exact Attempt.

## Pinned dependencies

The [`DirectTargetBinding`](../../src/Broodling/DirectTargetBinding.cs) pins the
native release, source revision, Linux x86-64 executable and approved execution
asset, recorded in the [approval manifest](../../src/Broodling/execution-assets/approval.json).
Asset loading, each prepared submission's retained binding and its
reopen/dispatch check, target readiness,
[`generate.sh`](../../src/Broodling/execution-assets/generate.sh) and the release
record use only these. Both image recipes carry a copy of the binding's release
archive pin; the DirectTarget image installs its `zeroshot` and the `restic`
native run allocation requires, and target readiness checks both executables'
SHA-256.

A DirectTarget binding change is a change to the Execution asset and native an
HTTP Attempt's Prepared submission is bound to. The gateway URL is one shared
constant: the bundled proposer calls it, and DirectTarget dispatch credentials
and the approved asset policy require it. An Attempt's runtime is the asset's.

Submission (#216), the run reader and the stopper (#217) go through the pinned
[Zeroshot.Client](https://github.com/faviann/zeroshot-dotnet-sdk) `10.10.0.1-preview.1`
SDK, an exact version whose package bytes `packages.lock.json` fixes; the
[adoption record](../../tests/README.md#sdk-adoption) identifies it and the
Broodling-owned evidence that accepted it (#220).
[`DirectTargetClient`](../../src/Broodling/DirectTargetClient.cs) is Broodling's
one thin configuration of it: the SDK's own transport with the
configured root as `TransportOptions.TrustedRootCertificatePath`, bound to the
DirectTarget binding's native release. Broodling builds no HTTP handler or
client of its own for the SDK. Readiness discovery uses the SDK's handler
factory with the same root (see [readiness](dotnet-target-readiness.md)).
Broodling keeps authority over the Prepared submission, Dispatch intent, Native
correlation, Native observation, completion and Attempt retirement. The SDK
persists nothing.

The SDK keeps .NET's default proxy behaviour. No ambient proxy is a
**deployment assumption**, not a guarantee in code: Broodling's container must
not define proxy variables (`HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY` or their
lowercase forms), because a proxy would otherwise receive DirectTarget
connections, including dispatch credentials in the submission body.

The DirectTarget uses the official
[v10.10.0 release](https://github.com/the-open-engine/zeroshot/releases/tag/v10.10.0)'s
Linux x86-64 musl archive; its URL and SHA-256 are in the two image recipes and
the approval manifest's `native.release`.

Broodling has no production Python source and needs no Python environment.
Target readiness runs a short inline `python3` UID probe inside the target, and
the asset recipe runs [`describe.py`](../../src/Broodling/execution-assets/describe.py)
at build time; neither is a Broodling runtime dependency.

The fixed PR runtime is uniform Codex / `gateway` / `gpt-5.6-sol` / medium /
small / execution-scoped sessions through exactly
`https://cliproxy.local.faviann.com/v1`, carried by the approved asset below.
The DirectTarget image pins Codex **0.153.4**; Broodling installs no Codex.
Per-node runtime, model/harness selection, node-local OAuth PR delivery and
fleet placement remain unsupported.

## Approved DirectTarget execution asset

The HTTP DirectTarget path (#163) submits one release-bundled graph/runtime,
[`execution-assets/software-change-pr-codex-gateway.json`](../../src/Broodling/execution-assets/software-change-pr-codex-gateway.json):
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
  asset's values. `task` carries the complete Contract, every exact entitled
  source byte and original B1 authority. Valid UTF-8 source content stays exact
  text, including CRLF; other content is base64 with `"encoding": "base64"`.
  Construction is deterministic. For a bundle-bound Contract, whose only
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

The record's retained binding is a `NativeRunBinding`: the target origin, the
frozen title and runtime size, and the frozen repository, authorized branch and
B1 source. It carries no credentials or adapter settings. Read and stop add the
run ID. One internal reader of the retained request supplies that binding and
the delivery, source and custody facts that dispatch and receipt validation use;
it never rewrites the saved bytes.

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
lock or writer held. The operation then takes the shared
[installation initiation lock](dotnet-installation-pause.md#ordering-boundary).
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

The send also carries the target's private control token as its bearer (#187).
It is read, like the root, when the operation's SDK client is created, before
the intent commits, from the token file the session's `DirectTargetAccess`
names for exactly the retained origin, so a `credentials_unavailable` dispatch
records and sends nothing. An `unauthorized` one, like every outcome but the
exact acknowledgement, leaves the intent unresolved. The control token is not a
dispatch credential: it is never part of the request body, the frozen request,
the binding or any record, and replacing it changes neither the request nor its
authority, so an exact replay with the current token converges on the intended run.

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
`StopAsync(attemptId, reason)` then forces exactly the confirmed run, and
the call raises `StaleAttempt` without restoring authority.
`NativeStopRequested` is true when force was sent, whether it reached a terminal
result or an uncertain outcome. It is false when setup failed before force; an
explicit stop can address the run later. Caller cancellation propagates after
correlation is retained. A duplicate acknowledgement after another caller
completed the Attempt returns the retained record without abandoning or
stopping it. Abandoned or replay-blocked work is never sent again to
discover its run.

## Composed invocation

```csharp
var access = DirectTargetAccess.For(directTargetOrigin, controlTokenFile, rootCertificate);
using var store = new BroodlingApplication().OpenStore(databasePath, access);
var invocation = new Invocation(store, new InvocationTarget(directTargetOrigin));
var credentials = new DispatchCredentials(githubToken, gatewayBaseUrl, gatewayApiKey);
var status = await invocation.SubmitAsync(reference, propose,
    [new RequiredEffect("pr", "Deliver a PR to main.", "pull_request", "main")],
    repositoryPath, revision: originalCommit, credentials: credentials);
// Retain status.Revision.ContractRevisionId and status.Attempts.Single().AttemptId.
var resumed = await invocation.ResumeAsync(status.Revision.ContractRevisionId,
    credentials: credentials);
```

`Invocation` takes one `InvocationTarget`, the DirectTarget origin. It admits an
HTTP Attempt, prepares or reopens its submission at that origin and dispatches it
with the caller's current `DispatchCredentials`. It needs no Python, SDK client
state, workspace root or launcher. `InvocationTarget` refuses an origin that is
not canonical HTTPS or HTTP to exactly `127.0.0.1` or `[::1]`.
`ResumeSubmissionAsync(submissionId, credentials)` takes the same path for one
Issue submission's bundle-bound Contract; its first Attempt starts from the
RequestBundle's retained B1. An existing Attempt whose retained binding names a
different origin refuses before any preparation or target contact. A correlated,
completed or ended Attempt is handed back from retained state without
configuration, credentials or target contact.

The caller owns the store lifetime and uses a separate session per caller.
`SubmitAsync` composes `AdmitGitHubAsync`; its proposer, exact required effects,
additional caller-entitled sources, source boundary and producer are the
existing [ingress](dotnet-github-ingress.md) inputs. Repetition reacquires
source bytes and may create a new revision. `ResumeAsync` uses only the specified
stored revision. Rejected admission or ended Attempt authority returns retained
status. Before the first Attempt it requires a repository; afterward the original
B1 governs. Once a submission is prepared, resume reopens that frozen request.

`Status` and `History` include `Submissions`, with each record's
`IntendedRunId` and `ReplayBlockedReason`; `RunId` stays confirmed correlation
only. They sit beside the exact revision's sources, Contract, decision and
Attempts and remain coherent, deferred SQLite reads without native access.
Exceptions do not undo earlier commits: use history to find the exact revision
after a failed submit.

## Thin operator commands

The host accepts these commands without starting HTTP:

```text
submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch> <reviewed-issue.json> <producer>
resume <store> <contract-revision-id> [config.json [checkout [revision]]]
wait <store> <attempt-id> [config.json]
stop <store> <attempt-id> <reason> [config.json]
check-target <target-inventory.json> <config.json>
bootstrap-target <config.json> <bootstrap-key-file>
```

`submit` authorizes exactly one `pull_request` effect to `<target-branch>`; `-`
is refused as a usage error. The
producer normally is `caller`. `ReviewedIssueProposal` requires the complete
acquired issue to match the operator-reviewed file exactly. The `<checkout>`
names the source repository whose exact revision becomes B1; it is not an
execution checkout. The operator command obtains current PR credentials from
its environment and passes them explicitly to the application.

The configuration is `{"directOrigin": ..., "directControlTokenFile": ...}` with
an optional absolute `directRootCertificate`. It contains no secrets: the
required absolute `directControlTokenFile` names the file holding the target's
private control token, which is read again by each operation and sent only to
`directOrigin`. Any other member, including a `target` member, an inline token
or another credential field, is refused. The origin must pass the
`InvocationTarget` rule above, without user information, path, query or
fragment, and never port 0. `wait` and `stop` take the same optional
configuration and use only its connection material (token file and root). The
retained binding decides where they connect, and a configuration naming a
different origin refuses before target contact or abandonment. Without a
configuration, `wait` returns only a retained completion and otherwise fails as
`credentials_unavailable` without contacting the target, and `stop` still
commits abandonment but reports its native stop as not sent. CLI error records
carry a native transport failure's fixed `kind`. See the
[release guide](../../deployment/README.md#invocation-configuration).

`bootstrap-target` installs the configuration's control token in the target
process now serving its origin, and `check-target` verifies the stack, private
discovery and authenticated control; see
[private control access](../../deployment/README.md#private-control-access).

The submit, resume and stop handbacks report each submission only as its status
facts (Attempt, phase, intended and confirmed run IDs, replay block); `status`
and `history` remain the full retained-fact inspection, including the frozen
request. Correlated, rejected or ended resume requires no configuration file.
Safe failures point to retained history and status; Ctrl+C returns a detached
handback. The callable proposer API remains available for richer source and
Contract inputs.

### DirectTarget origin, initialization and readiness

The homelab DirectTarget origin is `https://zeroshot.dev.faviann.com`
([ADR 0001](../adr/0001-directtarget-https-origin-and-compose-topology.md)).
The `zeroshot-tls` Caddy container serves it on port 443, signing with the
stack's own root, and forwards to native at the fixed inner port 18770 on the
Compose project network. Native itself is never published. The configuration
names that origin, its control token file and, as `directRootCertificate`, the
public `root.crt`, which Broodling rereads for each TLS connection. Explicit
first initialization creates the root once with the target image's
`initialize-tls` helper: the key is readable only by `zeroshot-tls`'s user, and
the certificate is the only file in a public directory. Native initialization
then creates the ledger and records the canonical origin without any network or
client. Rotation replaces the key and certificate together and removes Caddy's
stored intermediate and leaf.

Since #187 the target serves only native 10.10.0's private mode: the
entrypoint always starts `zeroshot target serve` with a private copy of its
root-only bootstrap key, and refuses to start without one. Native then refuses
every control route (submission, OECP session and WebSocket, operator
diagnostics and history) without the bearer token that the encrypted bootstrap
installed in that process; discovery stays public. `bootstrap-target`, through
the SDK's `NativeClient.Private.BootstrapAsync`, installs the configured token
after every target-process start, including a restart over existing state.
`check-target` verifies the stack, private discovery, refused unauthenticated
control and accepted authenticated control through `zeroshot-tls` with the
configured root, which also catches an intermediate left from before a rotation.
See [initialization](../../deployment/README.md#explicit-initialization-and-guarded-startup),
[private control access](../../deployment/README.md#private-control-access),
[rotation](../../deployment/README.md#tls-root-rotation) and
[readiness](dotnet-target-readiness.md).

## Dispatch, recovery and completion

Each Attempt has at most one `native_submissions` row. Any state after
`prepared` is committed dispatch intent, the one fact that retirement,
replacement, quarantine and unresolved-dispatch counting read. SQL guards
protect the request and key and permit only `prepared → dispatched →
correlated`, with `run_id` equal to the immutable `intended_run_id`.
`replay_blocked_reason = submission_conflict` may be recorded once while
dispatched or correlated and is never cleared. A cross-row guard refuses an
intended run ID that another Attempt's confirmed `run_id` names, and the reverse.
The completion trigger accepts only the record's exact source binding.
Preparation and dispatch require current authority; correlation deliberately
does not (see [HTTP dispatch](#http-dispatch-and-acknowledgement)).

`CancelIssueSubmissionAsync` uses the same authority, not a parallel execution
ledger. Its immediate transaction records the exact submission's immutable
stop/no-stop binding. Only a cancellation that owns the last relevant shared
Contract authority also commits abandonment against its exact Attempt before any
native stop. A sibling cancellation records a null binding and leaves the shared
current Attempt available. If an owned cancellation's Attempt is later
acknowledged, correlation stores that run identity on the abandoned Attempt and
the ordinary stop forces it through the retained binding. No replay discovers or
selects a later replacement. The callable result is either the durable cancelled
submission or the documented stop or transport exception; every such failure
leaves the cancellation and any abandonment facts inspectable.

After durable correlation, wait and stop receive the retained run binding
(origin, run ID, frozen title, size and PR source) through the
[run reader and stopper](#directtarget-run-reader-and-stopper). They need no
workspace or dispatch credentials, only the current control token for the
retained origin. Cancelling or killing a waiter detaches that
caller rather than stopping native execution. Completed receipt replay needs no
target and no token.

`BroodlingStore.ObserveAsync` reads an Attempt's current native phase and active
nodes through its retained binding. A merely prepared record returns null
without contacting the target. A dispatched record without acknowledgement is
read through its intended run ID. A correlated record is read through its
confirmed ID. Every observation reports that identity as `Intended` or
`Confirmed`, so a caller cannot infer acknowledgement from available progress.
The read is one SDK status request, setup included, under the 10-second progress
budget, and it is checked by the [run reader](#directtarget-run-reader-and-stopper)
against the retained title, size and PR source. The read also works while the
installation is paused or after abandonment. Each read is stamped with its
observation time. It is never persisted, writes nothing and never correlates,
completes, abandons or changes authority. An unknown, foreign or malformed run,
a timeout or transport loss is an unavailable observation with that fixed kind,
not an execution failure. Retained status never contacts native and is
unaffected. A `finished` phase is progress only: result consumption and
disposition remain `WaitAsync`'s responsibility.

Completion rechecks currentness, admitted Contract, invocation/run binding and
exact authorized delivery. Native failure records abandonment; invalid receipts
and late success after abandonment refuse successful completion. Store errors
grant no partial disposition.

## DirectTarget transport and budgets

The SDK owns the HTTP and OECP wire: discovery, session creation, WebSocket
connection, initialization, JSON-RPC envelopes, the typed status, force and
watch contracts and their size limits. Broodling's
[`DirectTargetRun.cs`](../../src/Broodling/DirectTargetRun.cs) owns
reconnection, identity checks, operation budgets and the fixed failure kinds,
and [`DirectTargetExchange.cs`](../../src/Broodling/DirectTargetExchange.cs)
keeps only the canonical-origin rule, the budgets and readiness discovery's
bounded read. The budgets are internal constants, not operator settings:

| Operation | Budget |
| --- | --- |
| Progress (one status read, setup included) | 10s |
| Submit | 60s, also the SDK's per-request ceiling |
| Stop (setup, precheck, force and waiting for its result) | 30s |
| Wait setup and first status | 30s; the wait itself has no deadline |

One budget encloses every SDK call in an operation. After caller cancellation
or expiry, no further call starts. Caller cancellation propagates as
cancellation. Expiry becomes the fixed `TimeoutError` kind. The budgets limit
only client operations, never native execution.

TLS is always verified, including the host name. By default it uses system
trust. A store session opened with `OpenStore(path, directTarget)` passes the
`DirectTargetAccess`'s PEM root to the SDK, which trusts exactly it for every HTTPS and WSS
connection, ignoring the system store, and rereads it for each TLS handshake.
For a loopback HTTP origin the SDK ignores the root. The file is never read when
the store opens. Each operation creates its own SDK client, which reads the root
once and refuses a missing or unreadable one as `transport_failed` before
anything is sent; a dispatch creates its client before its intent commits, so
that failure records nothing. A root that becomes unreadable later fails the
handshake, and so its operation, as `transport_failed`. Nothing retries.

Each SDK client also carries private-capability control credentials: the token
read, at client creation, from the file that the `DirectTargetAccess` names for
exactly the canonical origin being contacted, which for an existing Attempt is
its retained origin. The SDK sends it as the bearer of submissions and of the
OECP session, never to discovery, and uses the session the target issues for the
WebSocket. Its failures are the fixed kinds `credentials_unavailable` and
`unauthorized` ([failure kinds](#directtarget-run-reader-and-stopper)). Reading the file per operation lets an explicit rotation take
effect without restarting Broodling. A token configured for one origin is never
sent to another, and a configuration change never redirects an Attempt: its
retained origin still decides where it connects, and with no token for that
origin it contacts nothing. `DirectTargetAccess` takes an origin-to-file lookup,
so one session can serve several separately configured origins, each with its
own token (the contract for environment selection, #234/#235).

### DirectTarget run reader and stopper

Every operation reconnects by exactly the retained `NativeRunBinding`: its
origin, which must be canonical HTTPS or HTTP to exactly
`127.0.0.1` or `[::1]` with no path, query, fragment or user information, the
run ID and the DirectTarget binding's native release, as an SDK `RunReference`.
The binding carries no credentials; the control token is looked up for its
origin at each operation. An unsupported binding refuses as an
unsupported runtime before contact.

The SDK checks that a status or force reply names the requested run. Broodling
then requires its title, size, repository, branch and B1 to equal the binding;
anything else is `foreign_run`. Progress is the phase (`admitted`, `running`,
`stopping` or `finished`) and active nodes. The SDK validates and exposes
native's metadata and workspace-recovery facts; Broodling drops them and never
resumes a run. A result passes through the failure labels `force_stopped`,
`runtime_lost` and `runtime_failed` and maps every other native label to
`native_failed`. A `NativeResult` carries the run ID, success, arbitrary JSON
output (including null) and its failure. A `NativeProgress` carries only
the phase and the node names of active executions. Reading a run, finished or
not, consumes no completion and establishes no acknowledgement or correlation.

Failures are fixed kinds that never contain remote text. `RunNotFoundError` is
OECP `NOT_FOUND`. `TargetError` covers any other OECP error or HTTP problem.
`invalid_response` covers a reply the SDK rejects as malformed, including one
naming another run ID, and a watch that ends without a terminal result.
`TimeoutError` is expiry and `transport_failed` connection loss.
`unauthorized` is an HTTP 401: the target refuses the control token.
`credentials_unavailable` sent nothing because no readable token (exactly 64
lowercase hexadecimal characters) is configured for the origin. An unsupported OECP protocol or SDK binding refuses as an
unsupported runtime.

Progress is one bounded status read. The wait reads status once within its
30-second setup budget, which also checks identity. A finished status is the
result at once. Otherwise the SDK's `Run.WaitAsync` waits: it reads status again
and watches the run from that cursor until a terminal event, reopening an
interrupted watch itself. There is no client polling and no overall deadline.
Cancellation or any failure detaches the caller without stopping the run.

The stop uses one 30-second budget for setup, an optional precheck, force and
waiting for its result. For an intended but unacknowledged run identity it first
requires a valid matching status. Any unknown, foreign, malformed or unavailable
precheck sends no force. A matching precheck, even a finished one, still leads
to force. The SDK's force attempt names exactly the run and is sent once. Its
reply's identity is checked like a status. A nonterminal reply is followed by the
SDK's wait within the remaining stop budget. The result is a closed value:
`NotSent`, `Uncertain` or `Terminal`. `NotSent` and `Uncertain` carry a fixed
error kind. A failure before the force request is sent, including connection
setup inside the force attempt, is `NotSent`. Once it may have been sent, any
failure, including expiry, is `Uncertain`. The operation never fabricates a
stopped result. Caller cancellation propagates as cancellation, even after
force. No outcome proves physical cessation or establishes correlation.

## Cleanup limitation

**Every dispatched Attempt is ineligible for automatic cleanup or
replacement**, even after native success or force-stop. `StopAsync` commits
abandonment and requests native stop when known; a terminal result still provides
no physical-cessation receipt. Operators retain emergency containment
responsibility. There is no override turning incomplete proof into cleanup
authority. A proven never-dispatched Attempt can be explicitly retired and
replaced from original B1 under the lifecycle seam. Dispatched work is retired
only by the explicit
[verified maintenance retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement),
after which an abandoned Attempt can be explicitly replaced.

## Resource model for maintenance consumers

[Verified maintenance retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement)
(#122) consumes these retained facts and invents no client worktree to reclaim:

- The Attempt's admission, abandonment, retirement and completion records. An
  Attempt owns no local directory, branch or worktree.
- Shared custody: the common Git directory, the direct
  `refs/broodling/starting/<B1>` pin and any `refs/broodling/accepted/<oid>` pin,
  none of which an Attempt cleanup deletes.
- The submission: phase (`prepared`, `dispatched`, `correlated`), intended and
  confirmed run IDs, `replay_blocked_reason`, the retained asset identity and the
  binding (target origin, protocol, native release pins, frozen result-fetch
  origin).
- Dispatch uncertainty: any phase after `prepared` is dispatch intent and keeps
  the Attempt quarantined until verified maintenance retirement. Correlation
  resolves acceptance uncertainty only; retirement resolves neither.

Native checkout paths and runtime state belong to the target. Host
stopped-target and mount verification belong to the host procedure
(homelab-iac#356); Broodling records the check it supplies.

## Evidence and history

The [TUnit suite](../../tests/README.md) covers Broodling authority, Git/SQLite
durability and the SDK client against a loopback stock-target stand-in. The
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
