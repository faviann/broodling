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
do not initialize an empty replacement. Explicit `upgrade-store` applies only
to supported earlier **.NET** schemas, preserving retained facts.

The owner decision is a future operational gate, not a prerequisite for finishing
source retirement. This guide and #140 authorize no deployment, state switch,
silent replacement, deletion, target creation or new live-provider campaign.
Remaining #100 product intent is unchanged; public URL-only HTTP intake, bundled
proposer, reference capture, automatic execution/completion, Compose,
maintenance and retention work remain separate. Callable pre-Contract Issue
submission identity is documented in the state API.

## Build a release artifact

Build from a reviewed full source revision on a compatible Linux x86-64 host.
Use a .NET 10 SDK (tested with 10.0.401), Git, `cc` and libc development
headers. The native shim uses the build host's libc; this is not a portable
glibc/musl or arbitrary-host binary qualification. Runtime host requirements
include .NET 10 / ASP.NET Core 10, Python 3.13+ for the bridge, Git, and ordinary
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
SQLite native assets, `libbroodling_git.so`, `bridge/zeroshot_bridge.py` and
`bridge/requirements.txt`. `codex/` is the complete self-contained C# launcher,
with its pinned .NET **10.0.12** runtime; copying only its `codex` apphost is
insufficient. Preserve executable modes and package paths. Invoke the host with
`dotnet /RELEASE/host/Broodling.Host.dll`; its optional `Broodling.Host` apphost
requires a registered .NET installation or the appropriate `DOTNET_ROOT` when
the runtime lives in a nonstandard location. Publish neither
creates application state nor installs a target.

Only the bridge needs a Python environment. For development, or a separately
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

## State and operator commands

The examples below describe a future approved fresh installation. Replace
`/RELEASE` and `/NEW` with its chosen absolute durable paths; they are not
instructions to switch an existing installation. Keep state, source Git, Attempt
root and runtime directories separate from the release and source checkout.

```bash
dotnet /RELEASE/host/Broodling.Host.dll initialize-store /NEW/state.sqlite3
dotnet /RELEASE/host/Broodling.Host.dll history /NEW/state.sqlite3 OWNER/REPO 123
```

Initialization exclusively creates a new path. For supported old .NET state,
stop callers, make a consistent backup and deliberately use
`dotnet /RELEASE/host/Broodling.Host.dll upgrade-store /EXISTING/DOTNET/state.sqlite3`. Current format is
`broodling.dotnet`, schema **10**; v1–v9 require explicit upgrade. Use the
persisted installation pause/status/release commands for operator maintenance;
they do not stop native execution or prove container cessation.
See [state lifecycle](../docs/implementation/dotnet-identity-custody.md).

The host routes commands before HTTP startup. Running it without a command starts
ASP.NET composition/telemetry; it exposes no #100 HTTP intake or automatic
progression. No daemon is needed to supervise native runs.

For authorized PR invocation, a secret-free `config.json` selects:

```json
{
  "pythonExecutable": "/NEW/bridge-venv/bin/python",
  "stateDirectory": "/NEW/runtime",
  "workspaceRoot": "/NEW/attempts",
  "directOrigin": "http://127.0.0.1:18770"
}
```

Unknown fields, including credentials, refuse. The operator origin must be exactly
`http://127.0.0.1:<port>`, with an explicit valid port and no extra components.
The local no-effect alternative configures `realCodex`, `profileHome`,
`codexHome` and `launcher` (the published `/RELEASE/codex/codex`); see
[dispatch policy](../docs/implementation/dotnet-native-dispatch.md#fixed-policy-and-transport).
Local HOME starts empty and CODEX_HOME auth-only. The trusted-host prerequisite
excludes operator-managed effect-capable MCP/extensions. This profile still
cannot produce a stable successful local disposition.

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
`DirectTarget.Dockerfile` and `direct-target-entrypoint.sh`. Target provisioning is
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
state. The #100 service-name topology is therefore unresolved and is not decided
here.

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
must be clean/committed, match the GitHub origin, support the checkout policy,
and contain the selected full B1 commit, fetchable by the target.

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
Broodling.Host wait <store> <attempt-id> [python-executable]
Broodling.Host stop <store> <attempt-id> <reason> <python-executable>
```

Here `Broodling.Host` abbreviates `dotnet /RELEASE/host/Broodling.Host.dll`.
`producer` normally is `caller`; `-` explicitly
authorizes no effect. Retain JSON `revision.contractRevisionId`,
`attempts[].attemptId` and `submissions[].runId`. Source/canonical bytes are
base64; inspect the exact retained material. Errors can follow committed facts:
use history/status to find handles before choosing recovery.

Resume the same revision after interruption; repeating submit reacquires bytes.
Uncorrelated replay needs the same frozen configuration and current credentials.
Correlated resume and retained status/history need neither. Wait requires the
pinned SDK Python executable until completion is retained; afterward it works
offline without that argument. Native failure abandons; transport loss or a
cancelled wait only detaches. Restore access to the same target and wait again.

Stop records abandonment first, then requests native stop when the run is known.
A dispatched Attempt returns cessation refusal/quarantine even after terminal
stop. Unknown correlation is never redispatched to discover a run. Explicit
never-dispatched retirement/retry remain [callable operations](../docs/implementation/dotnet-retirement-replacement.md),
not an automatic CLI recovery sequence.

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

All dispatched Attempts remain permanently ineligible for automatic deletion or
replacement. Terminal success/failure/stop and external containment create no
product cleanup override. Plan capacity for indefinite quarantine. There is no
automatic merge, execution supervisor, maintenance or retention service.
