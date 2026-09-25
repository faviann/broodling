# .NET release and operations

The supported source and operator path is .NET 10 on Linux x86-64. This guide
builds an ordinary release artifact and describes the existing callable/operator
operations. **No live .NET deployment has been validated by this migration.**
The [#77 validation record](validation.md) and
[Python installation guide at the frozen baseline](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/deployment/README.md)
are historical evidence for their own revisions/profile.

Use is limited to operator-supervised internal PR proposals. [P5 remains scoped
FAIL](../evaluation/p5/README.md). Review every exact accepted revision against
the frozen request and admitted Contract, considering tests/CI, before a separate
merge decision. Native acceptance and `SUCCEEDED` authorize neither merge,
deployment nor release.

## Existing Python work and the operational switch

Source retirement can finish without an operational switch. **Before** any
switch, the owner must record the selected treatment of every existing Python
Work Unit/Attempt and its durable state:

1. **Drain:** keep the existing pinned Python release (the Python application is
   unchanged from `b3f61a9` through `da7e156`) and its original environment
   available to its owner to finish/inspect the already-authorized work. Preserve
   exact accepted revisions and receipts; dispatched work remains quarantined.
2. **Explicitly abandon and retain:** record abandonment through the existing
   owning implementation, request native stop when correlation is known, and
   retain unresolved/quarantined state. Abandonment and a terminal/stop label
   grant no deletion or workspace-replacement authority.

Record affected store/Attempt/run identities and the choice before using .NET
operationally. Preserve the Python store and SQLite sidecars, source Git common
directories and B1 refs/objects, worktrees, SDK/native state, target image/origin/
mounts/home and UID ownership, release/configuration, receipt exports and exact
accepted Git objects at their recorded absolute paths. Retain an appropriate
consistent backup. Do not run two implementations against that authority.

.NET starts with a deliberately chosen **separate fresh store and paths**; there
is no Python database import, reinterpretation or in-flight takeover. Ordinary
open refuses missing/foreign/old schemas. Restore a missing existing store;
do not initialize an empty replacement. The HTTP DirectTarget integration also
needs a fresh store; see [state and operator commands](#state-and-operator-commands).

The owner decision is a future operational gate, not a prerequisite for finishing
source retirement. This guide and #140 authorize no deployment, state switch,
silent replacement, deletion, target creation or new live-provider campaign.
Remaining #100 product intent is unchanged; public URL-only HTTP intake,
automatic execution/completion, Compose, maintenance and retention work remain
separate. Callable pre-Contract Issue
submission identity is documented in the state API.

## Build a release artifact

Build from a reviewed full source revision on a compatible Linux x86-64 host.
Use a .NET 10 SDK (tested with 10.0.401), Git, `cc` and libc development
headers. The native shim uses the build host's libc; this is not a portable
glibc/musl or arbitrary-host binary qualification. Runtime host requirements
include .NET 10 / ASP.NET Core 10, Git, Python 3.13+ only for the no-effect
LocalTarget bridge, and ordinary
non-PID-1 child ownership as described by
[materialization](../docs/implementation/dotnet-worktree-materialization.md).
Run Broodling as an unprivileged dedicated account with access to the local
rootful Docker socket for the selected DirectTarget. In an LXC, the host
operator must enable Docker nesting and the UID/GID operations rootful Docker
needs. No installer checks these host prerequisites; the operator owns them.

From the repository root, choose a new output directory for each release:

```bash
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export UseSharedCompilation=false
export NUGET_HTTP_CACHE_PATH="$PWD/tmp/nuget-http"
dotnet build Broodling.sln --configuration Release
dotnet publish src/Broodling.Host --configuration Release --output out/release/host
dotnet publish src/Broodling.Codex --configuration Release --output out/release/codex
git rev-parse HEAD > out/release/source-revision.txt
tar -C out/release -czf out/broodling-linux-x64.tar.gz host codex source-revision.txt
```

Keep both complete publish directories. `host/` contains the framework-dependent
`Broodling.Host` entrypoint, managed assemblies, runtime/dependency manifests,
SQLite native assets, `libbroodling_git.so`, `bridge/zeroshot_bridge.py`,
`bridge/requirements.txt` and the approved DirectTarget execution asset with its
manifest under `execution-assets/`. `codex/` is the complete self-contained C# launcher,
with its pinned .NET **10.0.12** runtime; copying only its `codex` apphost is
insufficient. Preserve executable modes and package paths. Invoke the host with
`dotnet /RELEASE/host/Broodling.Host.dll`; its optional `Broodling.Host` apphost
requires a registered .NET installation or the appropriate `DOTNET_ROOT` when
the runtime lives in a nonstandard location. Publish neither
creates application state nor installs a target.

Only the no-effect LocalTarget bridge needs a Python environment; authorized PR
work over the HTTP DirectTarget does not. For development, or a separately
approved installation, create a dedicated environment and install its dependency
using the published file:

```bash
python3 -m venv /CHOSEN/NEW/bridge-venv
/CHOSEN/NEW/bridge-venv/bin/python -m pip install -r /RELEASE/host/bridge/requirements.txt
```

This keeps the exact official SDK **10.3.0.post1** wheel URL and SHA-256. Its
bundled native is **10.3.0**; Codex is still **0.153.4**. The application refuses
different SDK/native versions. There is no Python Broodling package, installer
or importable proposer. Protect and retain the selected release and dependency
environment for replay; do not relocate a launcher already frozen in an invocation.

### Image packaging handoff (#121)

[Build and publish the Broodling and native-target images](https://github.com/faviann/broodling/issues/121)
receives these inputs; this repository builds and publishes neither image:

- The execution asset, its
  [approval manifest](../src/Broodling/execution-assets/approval.json) and the
  [generation recipe](../src/Broodling/execution-assets/generate.sh). Before
  packaging, run `src/Broodling/execution-assets/generate.sh
  /PATH/TO/pinned/zeroshot` with the SDK-bundled 10.3.0 executable. It requires
  executable SHA-256 `afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06`,
  regenerates the asset offline, verifies SHA-256
  `10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8` and checks
  native admission. It needs `python3` but no credentials, target or provider. See
  [execution asset](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset).
  The TUnit suite checks build output only. The image build owns the regression
  that published Broodling output carries `execution-assets/` with the approved
  SHA-256; without it, HTTP preparation refuses.
- The native pins: `zeroshot 10.3.0`, source
  `054ad3fd6c763b98d12f5b2e90830b97116561ad`, from the SDK 10.3.0.post1 wheel in
  [bridge/requirements.txt](../src/Broodling/bridge/requirements.txt), and the
  target dependencies in the [DirectTarget Dockerfile](DirectTarget.Dockerfile)
  (Codex 0.153.4, gh 2.101.0).
- The Broodling image needs no Python or SDK for authorized PR work; Python
  remains only for a no-effect LocalTarget profile.
- Frozen-reference access (#114): the DirectTarget image installs
  `/usr/local/bin/broodling-reference`, which native agents run to read one
  RequestBundle reference from the read-only HTTP reader. Its reader origin is
  the image file `/etc/broodling/reader-origin`, `http://broodling:8080`. The
  Broodling image must serve the reader on port 8080 (the ASP.NET Core container
  default) as the `broodling` service on the Compose project network, reachable
  from the `zeroshot` service; homelab-iac#353 wires and proves that path. Native
  clears agent environments, so a different address means replacing that file
  (for example with a read-only mount), not setting an environment variable. See
  [frozen-reference access](../docs/implementation/zeroshot-native-integration.md#frozen-reference-access).
- Evidence: the [controlled stock DirectTarget witness](../tests/README.md#controlled-stock-directtarget-witness)
  runs that unmodified native as `zeroshot target serve` in the actual
  DirectTarget image with the approved asset. Its test-only layer replaces the
  Codex provider and the `git`/`gh` forge, so its PR receipt is controlled. It
  covers exact B1 after branch movement, no client checkout, failure rather than
  fallback for an unavailable B1, same-run replay, restart retention and offline
  completion replay, and an agent reading frozen references through the installed
  helper from a real reader. It is not image publication, production topology
  (#155, homelab-iac#353), a real GitHub PR or provider quality evidence.
- Submit timing: the target acknowledges a run only after its own checkout. In
  the witness, with a local forge, acknowledgement took about 0.4 s and the
  unavailable-B1 refusal about 2.5–2.9 s. A slow real fetch can exceed
  Broodling's fixed 60-second submit budget; the send then stays unresolved
  until an exact replay, which converges on the same run.

## State and operator commands

The examples below describe a future approved fresh installation. Replace
`/RELEASE` and `/NEW` with its chosen absolute durable paths; they are not
instructions to switch an existing installation. Keep state, source Git, Attempt
root and runtime directories separate from the release and source checkout.

```bash
dotnet /RELEASE/host/Broodling.Host.dll initialize-store /NEW/state.sqlite3
dotnet /RELEASE/host/Broodling.Host.dll history /NEW/state.sqlite3 OWNER/REPO 123
```

Initialization exclusively creates a new path. Current format is
`broodling.application`, schema **1**. Pre-transition `broodling.dotnet`
stores (schemas 1–12) are unsupported and `upgrade-store` refuses them without
change; leave them and their associated resources in place. Use the
persisted installation pause/status/release commands for operator maintenance;
they do not stop native execution or prove container cessation. During verified
stopped-target maintenance the host procedure passes its current check to
`retire-attempt <path> <attempt-id> <stopped-target-check-json>`; see
[verified maintenance retirement](../docs/implementation/dotnet-retirement-replacement.md#verified-maintenance-retirement).
See [state lifecycle](../docs/implementation/dotnet-identity-custody.md).

The host routes commands before HTTP startup. Running it without a command starts
the read-only HTTP server over existing state named by `Broodling:Store`
(for example `--Broodling:Store=/NEW/state.sqlite3` or `Broodling__Store`); bind
it with the standard `--urls`/`ASPNETCORE_URLS`. Startup only opens the store and
refuses missing or incompatible state with a safe error code before listening.
Each request uses its own session; nothing contacts GitHub, a provider or the
target. `GET /health` opens the store and answers 503 when it is unavailable.
Retained reads are `/issues?url=<issue-url>`, `/submissions/{id}`,
`/submissions/{id}/bundle`, `/bundles/{id}/reference?id=<reference-id>`,
`/revisions/{id}` and `/attempts/{id}`, mapping the application reads in
[invocation](../docs/implementation/invocation.md). The server has no submission
intake or automatic progression. No daemon is needed to supervise native runs.

### Invocation configuration

A secret-free `config.json` names exactly one target kind. For authorized PR
invocation over the HTTP DirectTarget:

```json
{
  "target": "direct",
  "directOrigin": "https://zeroshot.dev.faviann.com",
  "directRootCertificate": "/NEW/zeroshot-tls/root.crt"
}
```

It needs no Python executable, SDK client state, workspace root or launcher. The
origin follows the native rule: canonical HTTPS, or literal-loopback HTTP such as
`http://127.0.0.1:18770`, spelled as scheme and authority only, with a default
port omitted and never port 0. Anything else refuses.

The optional `directRootCertificate` is the absolute path of a PEM root
certificate, such as Caddy's `tls internal` root. When it is set, HTTPS and WSS
connections to the DirectTarget trust exactly that root, not system trust. When
it is unset, system trust applies. No option disables certificate validation.
Each new TLS connection rereads the file, so a regenerated root is used without
restarting Broodling. A missing or unreadable file fails only
that operation, as a transport failure; a dispatch fails before recording any
dispatch intent. `check-target` uses this same file, but until #186 it still
accepts only a loopback HTTP origin, `http://127.0.0.1:<port>`.

The no-effect LocalTarget alternative uses the SDK bridge:

```json
{
  "target": "local",
  "pythonExecutable": "/NEW/bridge-venv/bin/python",
  "stateDirectory": "/NEW/runtime",
  "workspaceRoot": "/NEW/attempts",
  "realCodex": "/NEW/codex-cli/codex",
  "profileHome": "/NEW/profile-home",
  "codexHome": "/NEW/codex-home",
  "launcher": "/RELEASE/codex/codex"
}
```

A missing or unknown kind, a field of the other kind, an unknown field or a
credential field refuses. All four Codex-profile paths are required.
See [dispatch policy](../docs/implementation/dotnet-native-dispatch.md#fixed-policy-and-transport).
Local HOME starts empty and CODEX_HOME auth-only. The trusted-host prerequisite
excludes operator-managed effect-capable MCP/extensions. This profile still
cannot produce a stable successful local disposition. A new Attempt takes the
configured kind; an existing Attempt continues only through its retained kind,
and a mismatch refuses before any contact.

## Existing-target readiness

`TargetReadiness.CheckAsync` and the thin command preserve actual-target
configuration/dependency checks without an installer:

```bash
dotnet /RELEASE/host/Broodling.Host.dll check-target /NEW/target-inventory.json /NEW/config.json
```

An operator records the **existing selected target's** exact image and canonical
mount paths in `target-inventory.json`:

```json
{
  "containerName": "broodling-target",
  "imageId": "sha256:EXACT_EXISTING_IMAGE_ID",
  "directOrigin": "http://127.0.0.1:18770",
  "stateMount": "/NEW/target-state",
  "homeMount": "/NEW/target-home"
}
```

The [readiness reference](../docs/implementation/dotnet-target-readiness.md) lists
every check: selected image/container, running/root/isolation/restart settings,
exact mounts and loopback port, credential exclusion, native/Codex/Node/gh
pins and binary hashes, `gh api graphql --paginate --slurp`, hosted UID/GID
transition and native discovery. Both files must select the same origin. It
creates no target/state and dispatches zero provider tasks.

The retained [DirectTarget Dockerfile](DirectTarget.Dockerfile) records the
target dependency recipe: Node **22.23.2**, Codex **0.153.4**, gh
**2.101.0** and the native **10.3.0** binary hash. Its build context requires the
official wheel's `zeroshot/_bin/zeroshot` as `zeroshot`, plus
`DirectTarget.Dockerfile`, `direct-target-entrypoint.sh` and `broodling-reference`. Target provisioning is
operator-owned and separately authorized; the .NET release process does not
build or deploy this image.

The supported target is rootful Docker on the same trusted host, root inside the
container with ordinary capabilities, two durable mounts at `/state` and
`/home/node`, and unauthenticated native access published only on loopback.
Local users and containers able to reach its bridge are trusted. Public exposure,
rootless/altered UID mapping, Docker-socket or extra credential/source mounts are
outside this profile. Preserve native UID ownership; never recursively chown
used target storage.

### Explicit native initialization and guarded startup

The target image entrypoint is `/usr/local/bin/broodling-target`. It keeps the
native paths `/state`, `/home/node` and `CODEX_HOME=/home/node/.codex` fixed.
`ZEROSHOT_CONFIG_DIR` and `XDG_CONFIG_HOME` overrides are refused. Build the image
from a directory containing the three build inputs described above:

```bash
docker build -f DirectTarget.Dockerfile -t broodling-target:REVIEWED_REVISION .
```

For an authorized **new installation only**, first provision two empty durable
host directories with explicit root ownership and private permissions. With no
existing target using them, run the image once without publishing a port:

```bash
docker run --rm --network none \
  --mount type=bind,src=/NEW/target-state,dst=/state \
  --mount type=bind,src=/NEW/target-home,dst=/home/node \
  broodling-target:REVIEWED_REVISION initialize \
  --listen 0.0.0.0:18770 --public-origin http://127.0.0.1:18770 --storage /state
```

Initialization refuses nonempty roots, including partially initialized state.
It briefly starts native on container loopback at the origin's port, records
`broodling` through native `target add` in the home registry, uses public
`zeroshot list` to create the native ledger without submitting work, and stops
that process. Success leaves no server running. Initialization failure leaves
partial durable state for inspection; it never deletes it or silently retries
over it.

Pinned native, not the entrypoint, decides which public origins are valid and
records its canonical spelling; configure that exact spelling. Zeroshot 10.3.0
accepts only HTTPS origins or literal loopback HTTP (`http://127.0.0.1:PORT`),
and `target serve` itself provides no TLS. It refuses a plain-HTTP Compose
service-name origin such as `http://broodling-target:18770` before creating
state. The homelab installation's replacement topology and HTTPS origin are
decided in [ADR 0001](../docs/adr/0001-directtarget-https-origin-and-compose-topology.md);
the loopback commands in this section describe current behavior until that
origin is implemented.

Ordinary startup uses the same arguments **without `initialize`**, the same
mounts and the recorded origin. Before executing `zeroshot target serve`, the
entrypoint requires unredirected `/state`, `/state/runs`, `/state/runs.sqlite3`
and home registry, a ledger that already contains native's `v2_runs` and
`v2_run_events` tables (opened read-only with the image's Python `sqlite3`), and
a `broodling` registry entry whose origin equals the configured origin. Native
creates its tables in any SQLite file it opens, so an unrelated database must be
refused before serving. Native owns the table shapes, rows and registry
format/version.
Missing, foreign or redirected state refuses without creating replacement files.
Restore missing state; do not initialize an empty replacement at an existing
origin. Existing targets without this binding require a separately reviewed
stopped-target transition; this command does not adopt them. Readiness now
rejects the former unguarded native entrypoint.

Neither mode recursively changes ownership. Native itself prepares traversable
state/run roots; existing run-specific UIDs, GIDs and permissions remain native's
responsibility. Keep the target stopped during mount changes and serialize
operator initialization/startup. These checks recognize initialized native files
and the origin binding, not snapshot freshness or cross-version compatibility: a
matching old snapshot or another valid state/home pair with the same origin
cannot be distinguished. There is no new installation identity, private run-row
inspection, history pruning, upgrade, backup/restore or maintenance protocol here.

Readiness is a point-in-time dependency/configuration check. Before an authorized
dispatch, the operator must separately establish actual-target gateway
authentication/model availability, repository permissions and remote B1
availability. No fallback provider/runtime is supported. Readiness proves none
of those, nor PR delivery, semantic quality or physical cessation.

## Invocation and recovery

The caller must review the complete exact GitHub issue response. Capture it with
the same REST resource/headers as acquisition:

```bash
gh api --hostname github.com --method GET \
  --header 'Accept: application/vnd.github+json' \
  --header 'X-GitHub-Api-Version: 2022-11-28' \
  /repos/OWNER/REPO/issues/123 > /NEW/reviewed-issue.json
```

`ReviewedIssueProposal` refuses any subsequent byte change. Use it only for a
self-contained request whose prerequisites and effects the operator has reviewed;
richer inputs use the [typed .NET proposer](../docs/implementation/dotnet-github-ingress.md).
Comments and links require separate explicit entitlement. The source repository
must have an `origin` naming the admitted GitHub repository and contain the
selected full B1 commit, which Broodling pins in its common Git directory; no
execution checkout is created from it. B1 must also be fetchable by the target,
which fails the run rather than substituting the branch tip. A no-effect
LocalTarget worktree additionally needs a clean committed source that supports
the checkout policy.

Initial PR dispatch and acknowledgement replay require current `GH_TOKEN`,
`GATEWAY_API_KEY` and exactly
`GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1` in the caller's
environment. Supply secrets through an appropriate secret source, never argv,
tracked configuration or Docker environment. The fixed runtime is standard
`software-change`, Codex / `gateway` / `gpt-5.6-sol` / medium.
The GitHub identity needs repository/source access and native PR delivery
permissions; broad credential privileges do not authorize broader effects.

```text
Broodling.Host submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch|-> <reviewed-issue.json> <producer>
Broodling.Host status <store> <contract-revision-id>
Broodling.Host history <store> <repository> <issue>
Broodling.Host resume <store> <contract-revision-id> [config.json [checkout [revision]]]
Broodling.Host wait <store> <attempt-id> [config.json]
Broodling.Host stop <store> <attempt-id> <reason> [config.json]
```

Here `Broodling.Host` abbreviates `dotnet /RELEASE/host/Broodling.Host.dll`.
`producer` normally is `caller`; `-` explicitly
authorizes no effect. Retain JSON `revision.contractRevisionId`,
`attempts[].attemptId` and `submissions[].runId`. The `submit`, `resume` and
`stop` handback reports each submission only as its status facts (Attempt,
format, phase, intended and confirmed run IDs, replay block); `status` and
`history` show the complete retained record, including the frozen request. Source/canonical bytes are
base64; inspect the exact retained material. Errors can follow committed facts:
use history/status to find handles before choosing recovery.

Resume the same revision after interruption; repeating submit reacquires bytes.
Uncorrelated replay needs the same target configuration and current credentials.
Correlated resume and retained status/history need neither. `wait` and `stop`
take the same optional `config.json`, and the retained record decides what they
use from it. A LocalTarget record needs a LocalTarget configuration for its
pinned SDK Python. An HTTP record connects to its retained origin, and uses a
Direct configuration only for its root certificate. A configuration of the other
kind, or a Direct origin that differs from the retained one, refuses before any
target contact or abandonment. For an HTTPS target with a
private root, pass the Direct `config.json` to `wait` and `stop`. Without it,
system trust applies and the connection fails. `stop` then records the
abandonment and reports native stop as not sent (`transport_failed`); running
`stop` again with the configuration requests it. Retained completion works offline. Until completion
is retained, the host user also needs Git fetch access to the frozen origin URL,
because wait fetches and pins the exact accepted commit before recording success. Native failure abandons; transport loss or a
cancelled wait only detaches. Restore access to the same target and wait again.

Stop records abandonment first, then requests native stop when the run is known.
A dispatched Attempt returns cessation refusal/quarantine even after terminal
stop. Unknown correlation is never redispatched to discover a run. Explicit
never-dispatched retirement/retry remain [callable operations](../docs/implementation/dotnet-retirement-replacement.md),
not an automatic CLI recovery sequence.

### Bundled Contract proposer

The callable `AdmitRequestBundleAsync` prepares a completed RequestBundle's
Contract with the one built-in proposer
([reference](../docs/implementation/dotnet-contract-admission.md#bundled-proposer)).
No host command runs it yet; #116 and #120 compose it into the service. Its
profile is fixed, not configurable:

| Setting | Value |
| --- | --- |
| Gateway | OpenAI-compatible Chat Completions at exactly `https://cliproxy.local.faviann.com/v1` |
| Model | `gpt-5.6-sol` |
| Credentials | Current `GATEWAY_API_KEY` and exactly `GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`, read from the process environment for each proposal |
| Trust | The host's system TLS trust for the gateway |

The gateway's support for `/chat/completions` with tools, `tool_choice` and the
`json_object` response format for `gpt-5.6-sol` is an assumption. It has not
been confirmed against the real gateway; the historical #77 record probed only
`/models` ([validation record](validation.md)).

Supply the key through the same secret source as dispatch credentials. It is
sent only as the gateway's bearer token. It is never written to the
RequestBundle, Contract revision, refusal findings, submission rows or error
messages. A missing key, another base URL or a gateway refusal is a
non-retryable `ContractProposerError`. Transport loss, timeout, HTTP
408/429/5xx, a reply cut off at the output limit or ended by any other finish
reason than `stop` (absent counts as `stop`; `tool_calls` only with tool
calls), non-text content, tool calls on the final call and any
other unusable gateway response are retryable. Neither retains anything, so a later call proposes
again from the same frozen request. A bound or refused submission never calls
the gateway.

## Retention and limitations

Keep the authoritative SQLite store/sidecars, original source common Git and
B1 objects, Attempt enclosures/worktrees, runtime state, native target state/home,
exact target image/origin, release/dependency environment and operator inventory
at their retained paths. Protect native session state and private content as
sensitive. Retain exact accepted Git objects and full receipts for independent
review; moving PR branch tips are insufficient.

Cancelling the caller does not stop native work. Target restart can preserve
completed results; interrupted active native runs may become `RuntimeLost`.
A fresh empty target at an old origin cannot recover them. Reconnect to the
retained identity and inspect/consume its outcome; do not silently replace it.

For a consistent filesystem backup, stop callers and quiesce/stop the native
target with those interruption consequences understood. Preserve SQLite
sidecars, absolute paths, mount/image/origin and account/native UID ownership.
Restore within the same host boundary; do not run a restored authority copy
alongside the original. Live coordinated backup, takeover and lost-target
reconstruction are not provided.

All dispatched Attempts remain ineligible for automatic deletion or
replacement; only explicit verified maintenance retirement lifts a DirectTarget
Attempt's quarantine. Terminal success/failure/stop and external containment create no
product cleanup override. Plan capacity for indefinite quarantine. There is no
automatic merge, execution supervisor, maintenance or retention service.
