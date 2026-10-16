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
native release, source revision, Linux x86-64 `zeroshot` and `restic`
executables, the Claude Code release and its managed settings, and the
approved execution asset, recorded in the [approval manifest](../../src/Broodling/execution-assets/approval.json).
Asset loading, each prepared submission's retained binding and its
reopen/dispatch check, target readiness,
[`generate.sh`](../../src/Broodling/execution-assets/generate.sh) and the release
record use only these.

The target image is an adopted environment release
([adopted target environment](../../deployment/README.md#adopted-target-environment)).
Its producer manifest states the image's actual native and components; Broodling
adopts it only when that manifest names exactly this binding. Target readiness
refuses another manifest and checks the running image's executables and
versions against it. The binding's native is upstream's source build in its
published target image for the release,
`ghcr.io/the-open-engine/zeroshot-target:10.12.0@sha256:9eca152ead38e6589b76b13cca802ce2e7538579b851566a891ed470b069bd51`,
which the adopted environment is built on. The Broodling image recipe copies the
executable out of that image to verify the asset.

A DirectTarget binding change is a change to the Execution asset and native an
HTTP Attempt's Prepared submission is bound to. The gateway URL is one shared
constant: the bundled proposer calls it, and DirectTarget dispatch credentials
and the approved asset policy require it. An Attempt's runtime is the asset's.

Submission (#216), the run reader and the stopper (#217) go through the pinned
[Zeroshot.Client](https://github.com/faviann/zeroshot-dotnet-sdk) `10.12.0.1-preview.1`
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

The DirectTarget runs native from the
[v10.12.0 release](https://github.com/the-open-engine/zeroshot/releases/tag/v10.12.0)'s
source revision as upstream's target image builds it, not the release archive's
musl executable. The approval manifest's `native.image` pins that image.

Broodling has no production Python source and needs no Python environment.
Target readiness runs a short inline `python3` UID probe inside the target, and
the asset recipe runs [`describe.py`](../../src/Broodling/execution-assets/describe.py)
at build time; neither is a Broodling runtime dependency.

The fixed PR runtime is uniform Claude Code / `gateway` / `claude-opus-5-5` /
medium / small / execution-scoped sessions through exactly the sub2api gateway
`https://gateway.ai.faviann.com`, carried by the approved asset below. The
gateway URL has no version path: native's `gateway` provider passes
`GATEWAY_BASE_URL` and `GATEWAY_API_KEY` to Claude Code as `ANTHROPIC_BASE_URL`
and `ANTHROPIC_API_KEY`, and the bundled proposer appends
`/v1/chat/completions`. One Claude-group sub2api key serves both. The binding
approves Claude Code **2.1.276**, a native executable that needs no Node.js,
which the adopted environment carries; Broodling installs no harness.
Per-node runtime, model/harness selection, node-local OAuth PR delivery and
fleet placement remain unsupported.

## Approved DirectTarget execution asset

The HTTP DirectTarget path (#163) submits one release-bundled graph/runtime,
[`execution-assets/software-change-pr-claude-gateway.json`](../../src/Broodling/execution-assets/software-change-pr-claude-gateway.json):
native 10.12.0's stock `software-change` template with `pull_request` delivery,
generated with the uniform Claude Code runtime (#246). Its identity is the
SHA-256 of the exact file bytes, formatting included:
`7e232da54ed2ca138b31fea38a6f002d08d77b584965253b18077205b2c1e5be` (101,314
bytes). This identity is Broodling's approval. It is not a stock `profileId` or a
native digest. The [approval manifest](../../src/Broodling/execution-assets/approval.json)
(#215) records the review that approved it and binds:

- The asset's bytes and SHA-256.
- Native `zeroshot 10.12.0` from source `928de8ff918488749ca9d23e0a7349873401b7f7`,
  the Linux x86-64 executable `7b3c74db…52693` and the `restic` executable
  (`90ab22a5…8a8cf`), with upstream's target image that carries both
  (`native.image`).
- Claude Code `2.1.276` (`claude`), the harness the asset's runtime runs on,
  with the managed settings `/etc/claude-code/managed-settings.json`
  (`a9e2560a…3a809f`) that the adopted environment installs.
  Codex and Copilot CLI are not approved.
- The policy: Claude Code / `gateway` / `claude-opus-5-5` / medium / small / execution
  sessions, the symbolic `gateway` connection on every agent node, native's
  `github` delivery connection, native's default `consider` PR feedback and the
  gateway URL. Native serializes both defaults by omitting `sessionScope` and
  `pullRequestFeedback`. The adopted image's managed effort overrides the
  asset's per-node effort, so changing effort needs a new environment release.
- The recipe: `generate.sh` and `describe.py` by SHA-256, from revision
  `04daec4b65cfb16228426979126105cf7c2bb389`, with its command, uniform runtime
  and output format.
- The asset structure that
  [`describe.py`](../../src/Broodling/execution-assets/describe.py) reads: four
  executable nodes (`worker`, `acceptance`, `code` and `deliver`), the
  `builtin.git-delivery.pr@2` `v2`/`pr` receipt with outcomes `ready`,
  `ci_failed`, `conflict` and `repair_required`, the three non-ready outcomes
  routing to `delivery_retry`, the ten-iteration `change_loop` and no node
  `timeoutMs`. Native 10.12.0 has no separate repair nodes: a rejected review
  (through `review_retry`) or a non-ready delivery (through `delivery_retry`)
  ends the iteration, and the next one runs `worker` with that phase and its
  feedback, then both reviews and delivery again.

The asset and manifest hold no credential values. The asset also omits the
gateway URL, which `DispatchCredentials` enforces separately.

[`ExecutionAsset.LoadBundled`](../../src/Broodling/ExecutionAsset.cs) checks the
packaged files against the SHA-256 of the asset and of the whole reviewed
approval manifest compiled into `DirectTargetBinding`. It refuses a missing,
changed or unapproved asset, including the superseded 10.3.0 asset, and any
other manifest, such as one naming another native, harness, policy or recipe
revision. `ExecutionAssetTests` checks that the manifest's asset, native and
`claude` blocks are the binding's, that its policy is the asset's own runtime,
and that `approval.decision` is a public comment URL on `faviann/broodling`.
The images workflow refuses to publish unless that decision is such a URL
([publication](../../deployment/README.md#publication-and-release-records)). C# passes the graph and runtime through opaquely. It never expands,
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

With `--image`, it copies the executable out of the manifest's `native.image`,
which Docker pulls by its pinned digest when absent, so the standalone check
needs Docker and, on its first run, registry access:

```bash
src/Broodling/execution-assets/generate.sh --image
```

## HTTP submission preparation

`PrepareHttpSubmission(attemptId, environments)` freezes one HTTP Attempt's
complete request and its [environment resolution](#environment-selection)
without target contact or dispatch credentials. The
[prepared record](../../src/Broodling/HttpSubmission.cs) is the preparation fact.
It implies neither dispatch intent nor native acceptance. The target origin is
the resolved environment's, which the catalog requires to be canonical HTTPS or
HTTP to exactly `127.0.0.1` or `[::1]`, as the SDK also requires; the status
reader uses the same check.

Preparation first establishes exact B1 custody: the direct
`refs/broodling/starting/<B1>` pin in the Attempt's common Git directory. It then
captures the result-fetch origin from that directory's `remote.origin.url`. The
origin must name the admitted GitHub repository and carry no credentials: a URL
has no user information except the `git` user of an `ssh://` URL. A
crash after pinning but before the SQLite commit leaves only the pin, which grants
nothing. The environment declaration is then read from exact B1 and resolved
against the catalog, and a bundle-bound Attempt's references are read, all
outside any SQLite writer; a refusal leaves no record. Preparation then rechecks current authority and pause in one immediate
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
  RequestBundle's manifest with every member's exact captured bytes; see
  [frozen references](#frozen-references). `source` names the
  admitted owner/repository and the authorized PR branch, separate from exact
  original B1.
- The intended run ID, a canonical UUIDv7 generated only when no record exists,
  and the key `broodling:http:v1:<attempt-id>`.
- The exact asset bytes in the content-addressed `execution_assets` row.
- A Broodling-only binding that is never sent: protocol
  `zeroshot.native-v2-target/v2`, the common Git directory,
  the frozen result-fetch origin, the DirectTarget binding's native
  release, source and executable, and the `environment` resolution, whose
  target names the origin.
- The adoption record, manifest and declaration bytes in the content-addressed
  `environment_documents` rows.

Concurrent preparers converge on the first committed record, its environment
included; a loser never generates or compares another identity or resolution,
whatever catalog it holds. Repeating preparation does not read the
installed asset. It validates the retained record against the retained asset
bytes, the approved identity and native binding, and a rebuild of the request
from admitted authority. That rebuild takes the reference bytes from the retained
task itself and checks each against the admitted manifest digest, so it reads no
Git. A missing or corrupt asset, a request whose graph, runtime or task differs,
reference bytes that differ from their digest, missing or differing environment
documents, or an unsupported binding refuses. A retained manifest that this
release's native binding no longer accepts refuses with a reason to retire and
replace the Attempt. The repository, the catalog and the default are not read
again. Nothing is regenerated or rebound.

The record's retained binding is a `NativeRunBinding`: the target origin, the
frozen title and runtime size, and the frozen repository, authorized branch and
B1 source. It carries no credentials or adapter settings. Read and stop add the
run ID.

### Environment selection

The operator's catalog ([invocation configuration](invocation.md#configuration))
names each approved environment and its target. An Attempt's requirement comes
from `.broodling/environment.json` at the repository root of its original B1,
read through the B1 pin and never from a later revision or the working tree.
The declaration is exactly `{"version": 1, "environment": "<name>"}`: strict
UTF-8 without a byte order mark, at most 1024 bytes, a regular file (mode
`100644` or `100755`). The name is a lowercase DNS label of at most 54
characters and selects that catalog entry exactly. There are no ranges,
families, toolchain or SDK matching, or nearest-version fallback, and
`global.json`, `.tool-versions` and similar files are never interpreted. No
declaration means the configured default. The declaration is execution context:
it never enters the Contract or the native request and grants no authority. A
per-invocation override is deferred.

Refusals happen before any record or target contact, and progression stops
with the code: `environment_declaration_invalid` (not JSON or UTF-8, a byte
order mark, over 1024 bytes, not a regular file, `.broodling` not a directory,
no numeric version, or a name that is not a DNS label),
`environment_declaration_conflict` (a repeated member),
`environment_requirement_unsupported` (a version other than 1 or any other
member, such as `sdk`), `environment_unapproved` (a name the catalog does not
configure) and `environment_default_missing`. An adoption whose native, Claude
Code, Claude Code managed settings or startup interface this release's binding
does not support is refused when the catalog is loaded.

The resolution is fixed for the Attempt's lifetime: adding an environment or
changing the default affects only new Attempts. Dispatch, first send and replay
alike, requires the current catalog to serve the retained environment exactly:
the same name, image, adoption and manifest digests, origin, container and
mounts. Otherwise it refuses with `environment_changed`, naming what changed and
the Attempt's retained environment, before intent and contact. Observation and
wait require only that the catalog still names the retained environment at its
retained origin. Otherwise they fail with the transport kind
`environment_unavailable` and send nothing, so a token never reaches an origin
under another name. Stop under such a catalog still abandons and quarantines the
Attempt but sends no force (`cessation_unconfirmed`, reason
`environment_unavailable`). The `stop` command given such a configuration
refuses with `submission_conflict` before abandonment. A correlated Attempt
therefore still reads, waits and stops after an image swap. The remedy is stop,
retirement under verified maintenance and a Replacement. A Replacement inherits
its predecessor's resolution and is refused with `environment_changed` unless
the catalog still serves it exactly; with `--update-environment` it resolves the
predecessor's environment name against the current catalog instead
([replacement](dotnet-retirement-replacement.md)).

`binding.environment` keeps the requirement apart from its resolution:

```json
{"requirement": {"name": "dotnet",
   "declaration": {"path": ".broodling/environment.json", "commit": "<B1>", "blob": "<oid>", "sha256": "<hex>"}},
 "resolution": {"adoption": {"kind": "broodling.target-environment-adoption/v1", "release": "dotnet-r5", "sha256": "<record>"},
   "manifest": {"schema": "zeroshot-environment.manifest/v2", "sha256": "<manifest>"},
   "image": "ghcr.io/…@sha256:…", "platform": "linux/amd64", "os": "Debian GNU/Linux 13 (trixie)",
   "components": {"native": ["10.12.0"], "claude": ["2.1.276"], "…": ["…"]},
   "target": {"environment": "dotnet", "origin": "https://zeroshot-dotnet.dev.faviann.com",
     "container": "…", "stateMount": "…", "homeMount": "…"}}}
```

An undeclared requirement has `name` and `declaration` null. A Replacement
records where its environment came from on its retry row: its predecessor and
`environment_update`. A trigger freezes the binding, and the documents it names
cannot be changed or deleted. No token value or token path is retained. Status
and Attempt reads report `environment`: name, image, adoption and manifest
digests and the target.

### Frozen references

A bundle-bound task (#241) carries a `requestBundle` object beside the Contract,
the Executable Request and B1: the bundle identity, the manifest digest the
Contract binds, and each member in manifest order with its reference ID, capture
kind, selector (the capture's JSON; any other selector stays base64), content
digest, the pinned commit and path of a Git capture, and its exact captured
bytes, encoded like entitled sources. The text tells agents that a reference
adds no work, cannot amend the Contract or Executable Request and authorizes no
effect.

The members are exactly the sealed manifest's, fixed in the prepared request, so
a run needs no Broodling access and agents have no way to refresh or extend the
bundle. The target image has no Broodling helper or reader address, and
the target needs no route to Broodling. An acknowledgement-loss replay sends the
same bytes, references included.

Native puts the whole task into every agent node's input. A node input is one
ledger event of at most 1 MiB, so preparation refuses a task over Broodling's
512 KiB budget, measured as JSON-escaped UTF-8, an upper bound on what native
writes. That leaves the rest for node instructions, the response contract and
repair feedback. The refusal,
`task_too_large`, states the task's size and the limit, happens before any
prepared record exists and is never resolved by retrying; nothing is truncated.
The model's context window behind the gateway is a provider-side limit that this
budget does not enforce. The
[#241 findings](https://github.com/faviann/broodling/issues/241#issuecomment-6043665375)
record the native, SDK and Codex limits and the alternatives considered.

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

Before opening its client, dispatch (first send and replay alike) applies the
[retained-environment check](#environment-selection), before intent and contact.

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
Native 10.12.0 acknowledges once it has recorded the run and prepares the
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
// The catalog, as InvocationConfiguration reads it over the adoption set bundled with the release.
using var store = new BroodlingApplication().OpenStore(databasePath, environments);
var invocation = new Invocation(store, environments);
var credentials = new DispatchCredentials(githubToken, gatewayBaseUrl, gatewayApiKey);
var status = await invocation.SubmitAsync(reference, propose,
    [new RequiredEffect("pr", "Deliver a PR to main.", "pull_request", "main")],
    repositoryPath, revision: originalCommit, credentials: credentials);
// Retain status.Revision.ContractRevisionId and status.Attempts.Single().AttemptId.
var resumed = await invocation.ResumeAsync(status.Revision.ContractRevisionId,
    credentials: credentials);
```

`Invocation` takes the `EnvironmentCatalog`. It admits an HTTP Attempt,
prepares or reopens its submission at the target its
[environment resolution](#environment-selection) selects and dispatches it with
the caller's current `DispatchCredentials`. It needs no Python, SDK client
state, workspace root or launcher.
`ResumeSubmissionAsync(submissionId, credentials)` takes the same path for one
Issue submission's bundle-bound Contract; its first Attempt starts from the
RequestBundle's retained B1. An existing Attempt keeps its retained resolution,
and dispatch refuses before target contact when the catalog no longer serves it
exactly. A correlated,
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
bootstrap-target <config.json> <environment> <bootstrap-key-file>
```

`submit` authorizes exactly one `pull_request` effect to `<target-branch>`; `-`
is refused as a usage error. The
producer normally is `caller`. `ReviewedIssueProposal` requires the complete
acquired issue to match the operator-reviewed file exactly. The `<checkout>`
names the source repository whose exact revision becomes B1; it is not an
execution checkout. The operator command obtains current PR credentials from
its environment and passes them explicitly to the application.

The configuration is the secret-free environment catalog
([invocation](invocation.md#configuration)): each entry's control token file is
read again by each operation and its token sent only to that entry's origin.
`wait` and `stop` take the same optional configuration and use only its
connection material (token file and root). The retained binding decides where
they connect, and a configuration with no entry of the retained environment
name at the retained origin refuses before target contact or abandonment. Without a
configuration, `wait` returns only a retained completion and otherwise fails as
`credentials_unavailable` without contacting the target, and `stop` still
commits abandonment but reports its native stop as not sent. CLI error records
carry a native transport failure's fixed `kind`. See the
[release guide](../../deployment/README.md#invocation-configuration).

`bootstrap-target` installs the named environment's control token in the
target process now serving its origin, and `check-target` verifies every
catalog target, private discovery and authenticated control; see
[private control access](../../deployment/README.md#private-control-access).

The submit, resume and stop handbacks report each submission only as its status
facts (Attempt, phase, intended and confirmed run IDs, replay block); `status`
and `history` remain the full retained-fact inspection, including the frozen
request. Correlated, rejected or ended resume requires no configuration file.
Safe failures point to retained history and status; Ctrl+C returns a detached
handback. The callable proposer API remains available for richer source and
Contract inputs.

### DirectTarget origin, initialization and readiness

Each catalog environment has its own DirectTarget and origin, by convention
`https://zeroshot-<name>.dev.faviann.com`
([ADR 0002](../adr/0002-one-directtarget-per-environment-behind-one-tls-proxy.md)).
The one `zeroshot-tls` Caddy container serves every origin on port 443, signing
with the stack's own root, and forwards each through its own site file to
`zeroshot-<name>` at the fixed inner port 18770 on the Compose project network.
Native itself is never published. The configuration names each origin and its
control token file and, as `directRootCertificate`, the one public `root.crt`,
which Broodling rereads for each TLS connection. Explicit
first initialization creates the root once with the target image's
`initialize-tls` helper: the key is readable only by `zeroshot-tls`'s user, and
the certificate is the only file in a public directory. Native initialization
then creates the ledger and records the canonical origin without any network or
client. Rotation replaces the key and certificate together and removes Caddy's
stored intermediate and leaf.

Since #187 the target serves only native's private mode: the
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
retained origin. Observation, wait and stop first apply the
[retained-environment check](#environment-selection). Cancelling or killing a waiter detaches that
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
trust. A store session opened with `OpenStore(path, environments)` passes the
catalog's `DirectTargetAccess` PEM root to the SDK, which trusts exactly it for every HTTPS and WSS
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
origin it contacts nothing. The catalog's `DirectTargetAccess` maps each
entry's origin to its own token file, so one session serves every configured
target and each token reaches only its own origin.

### DirectTarget run reader and stopper

Every operation reconnects by exactly the retained `NativeRunBinding`: its
origin, which must be canonical HTTPS or HTTP to exactly
`127.0.0.1` or `[::1]` with no path, query, fragment or user information, the
run ID and the DirectTarget binding's native release, as an SDK `RunReference`.
The binding carries no credentials; the control token is looked up for its
origin at each operation. An unsupported binding refuses as an
unsupported runtime before contact.

The SDK checks that a status or force reply names the requested run. Broodling
then requires its title and size to equal the binding, and its whole resolved
source to equal the frozen repository, branch and B1. Any difference is
`foreign_run`. Broodling submits no `remote`, so that includes a reply with no
source and a source that carries a `remote`. Progress is the phase (`admitted`, `running`,
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
  origin) with its retained environment target: environment name, origin,
  container, state mount and home mount, which a stopped-target check must equal.
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
runs the unmodified native 10.12.0 HTTP/OECP target and the approved asset with a
controlled Claude Code provider and forge. Its PR receipt is controlled, not a real
GitHub PR. None of these establishes provider quality, hostile sandbox resistance
or physical cessation. [P5 remains scoped FAIL](../../evaluation/p5/README.md),
requiring human review of each exact accepted revision.

.NET state uses its own format; Python schema history and application APIs are
retired, with no import or in-flight takeover. The prior implementation and
removed assurance/supervisor machinery remain
[dated history](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/docs/implementation/zeroshot-native-integration.md).
Historical passes do not transfer to the current release.
