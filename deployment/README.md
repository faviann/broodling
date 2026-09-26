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
Remaining #100 product intent is unchanged; Compose, maintenance and retention
work remain separate. URL-only HTTP submission with automatic processing and
result capture is the [processing server](#processing-server). Callable
pre-Contract Issue submission identity is documented in the state API.

## Build a release artifact

Build from a reviewed full source revision on a compatible Linux x86-64 host.
Use a .NET 10 SDK (tested with 10.0.401), Git, `cc` and libc development
headers. The native shim uses the build host's libc; this is not a portable
glibc/musl or arbitrary-host binary qualification. Runtime host requirements
include .NET 10 / ASP.NET Core 10, Git, the GitHub CLI `gh` for issue and
repository acquisition by `submit` and the processing server, Python 3.13+ only for the no-effect
LocalTarget bridge, and ordinary
non-PID-1 child ownership as described by
[materialization](../docs/implementation/dotnet-worktree-materialization.md).
Run Broodling as an unprivileged dedicated account; with the
[Broodling image](#broodling-image), commands that share its store run as its
user `1654:1654`. Only the host-side
`check-target` command needs access to the local rootful Docker socket; the
[Broodling image](#images) never mounts one. In an LXC, the host
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

## Images

This repository builds, checks and publishes two images; `zeroshot-tls` runs
the upstream pinned Caddy image unchanged.

| Service | Recipe and build context | Published repository | User |
| --- | --- | --- | --- |
| `broodling` | [`Broodling.Dockerfile`](Broodling.Dockerfile), repository root | `ghcr.io/faviann/broodling` | `1654:1654` |
| `zeroshot` | [`DirectTarget.Dockerfile`](DirectTarget.Dockerfile), `deployment/` | `ghcr.io/faviann/broodling-target` | container root |
| `zeroshot-tls` | not built | `caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b` | `10443:10443` |

To build and check them locally from the repository root:

```bash
docker build -f deployment/Broodling.Dockerfile -t broodling:REVIEWED_REVISION .
docker build -f deployment/DirectTarget.Dockerfile -t broodling-target:REVIEWED_REVISION deployment
tests/images/demonstrate.sh broodling:REVIEWED_REVISION broodling-target:REVIEWED_REVISION
```

Both builds fetch the official SDK 10.3.0.post1 wheel by its pinned SHA-256
(the [bridge/requirements.txt](../src/Broodling/bridge/requirements.txt) pin)
and use only its native `zeroshot 10.3.0` executable, source
`054ad3fd6c763b98d12f5b2e90830b97116561ad`, SHA-256
`afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06`. Neither
image contains the SDK. Base images are pinned by digest; apt packages come from
the base distribution at build time, so select a published image by digest, not
by rebuilding.

### Broodling image

The runtime stage is ASP.NET Core 10.0.12 on Ubuntu 24.04. The build stage uses
the matching SDK 10.0.401 image, so `libbroodling_git.so` links against the
runtime's libc. The image holds the published host output at `/app`, including
`execution-assets/`, plus Git, gh 2.101.0 (the DirectTarget image's pin, also
Git's credential helper for `https://github.com`), curl and tini. It runs
`dotnet /app/Broodling.Host.dll` under tini as the image's non-root `app` user,
`1654:1654`. Administrative Git refuses a PID-1 process, so the host never runs
as PID 1.

- With no arguments it serves HTTP on port **8080** over
  `Broodling__Store=/var/lib/broodling/state.sqlite3`: the read-only reader, or
  the [processing server](#processing-server) when the service also sets
  `Broodling__Invocation` and `Broodling__RepositoryRoot` and supplies
  credentials. For the ADR 0001 stack, mount a Direct `config.json` read-only
  (for example at `/etc/broodling/invocation.json`) naming
  `https://zeroshot.dev.faviann.com` and `/tls-root/root.crt`, and use
  `Broodling__RepositoryRoot=/var/lib/broodling/repositories`, a directory the
  operator creates in the state directory, owned by `1654:1654`. The
  DirectTarget image's reference helper reads from `http://broodling:8080`, so
  keep that port and service name.
- Arguments select a store, inspection or maintenance command instead. For
  example, a new installation initializes its store once with
  `docker compose run --rm --no-deps broodling initialize-store /var/lib/broodling/state.sqlite3`.
  The store-only commands `upgrade-store`, `status`, `history` and the
  installation pause commands work the same way.
- For an Attempt that the processing server created, `retire-attempt` and
  `replace-attempt` run in the image against the stopped application's store
  during [verified maintenance retirement](../docs/implementation/dotnet-retirement-replacement.md#verified-maintenance-retirement),
  for example
  `docker compose run --rm --no-deps broodling retire-attempt /var/lib/broodling/state.sqlite3 ATTEMPT_ID CHECK_JSON`
  and then, still paused,
  `docker compose run --rm --no-deps broodling replace-attempt /var/lib/broodling/state.sqlite3 ATTEMPT_ID RETRY_KEY`.
  Such an Attempt's B1 and accepted-revision pins are in its service-owned bare
  repository under `Broodling:RepositoryRoot`, recorded as the container path.
  With that root in the state directory (`/var/lib/broodling/repositories`), the
  commands need only the state mount: no credentials, network, host release
  artifact or caller checkout.
  They keep every refusal and pause requirement they have from the release
  artifact. The image has no Docker
  socket, so the host maintenance procedure stops and verifies the target and
  supplies the stopped-target check.
- After `release-installation`, restart the target and the processing server,
  then dispatch that Attempt's prepared successor with the image's `resume`, run
  as the processing service itself:
  `docker compose run --rm --no-deps broodling resume /var/lib/broodling/state.sqlite3 CONTRACT_REVISION_ID /etc/broodling/invocation.json`.
  `CONTRACT_REVISION_ID` is the replaced Attempt's `contractRevisionId`, from
  `GET /attempts/{id}` or `history`. The command needs what the server has: the
  state mount, whose repository root holds the successor's B1 custody at its
  recorded container path; the Direct `config.json`, whose origin must be the
  successor's retained one and whose root it trusts; `GH_TOKEN`,
  `GATEWAY_BASE_URL` and `GATEWAY_API_KEY` from the service's own environment;
  and the project network to the origin. It refuses while the installation is
  paused. It sends the prepared request with its intended run ID and prints the
  Contract revision's status, where the successor's submission is `correlated`
  with that run ID; repeating it returns that correlation, or replays the exact
  request if the acknowledgement was lost. Running it beside the server is safe:
  both processes write the same SQLite WAL store under its locks, and automatic
  progression never selects a Replacement Attempt or its submission, so only
  this command dispatches the successor. Once the successor is correlated, the
  server's completion observation retains its result and `POST /attempts/{id}/stop`
  stops it, as for the server's own Attempts. Dispatch the successor rather than
  stopping it while prepared: a stopped, never-dispatched successor keeps a
  `no_dispatch_intent` proof that nothing acknowledges, so it cannot itself be
  replaced. The [image demonstration](../tests/README.md#image-demonstration)
  retires, replaces and then dispatches an Attempt that the image's processing
  server created.
- CLI Attempts stay release-artifact-only. The invocation commands (`submit`,
  `resume`, `wait`, `stop`) are not supported in the image, except `resume` for
  a processing-server Replacement Attempt as above, and
  `retire-attempt` and `replace-attempt` for an Attempt that `submit` created run
  from the release artifact. A CLI Attempt's source custody is the common Git directory of the caller checkout
  named to `submit`, at its host path, which the image does not mount.
  Resuming or waiting on the Attempt needs it, `retire-attempt` checks the
  Attempt's B1 and accepted-revision pins there, and `replace-attempt` pins B1
  and reads the result origin there.
- The operator provides the state directory, mounted read/write at
  `/var/lib/broodling` and owned by `1654:1654` (for example mode `0700`). The
  image never changes mounted ownership. Mount the public root directory
  read-only at `/tls-root`.
- Release-artifact commands that share the image's store, so that the reader
  serves what they retain, must run as UID/GID `1654:1654` against the same
  state directory's host path: the store uses SQLite WAL, whose sidecar files
  must stay usable by the image user. This combination is not demonstrated in
  this revision.
- The image health check runs `GET http://127.0.0.1:8080/health` with no
  credentials. It fails while the store is unavailable.
- It mounts no Docker socket. `check-target` inspects containers on the Docker
  host, so run it there: from the release artifact, or from the image's own
  output copied out with `docker cp` and a host .NET 10 ASP.NET runtime, with a
  configuration that names the host path of `root.crt`.

The image contains no Python, SDK, native client, Codex CLI or C# Codex
launcher. HTTP DirectTarget submission, observation and control need none of
them and no client helper process; the processing server runs them in the
image, while the CLI invocation commands run from the release artifact as
described above. The no-effect LocalTarget profile needs
all of them, so it remains available only from the [release artifact](#build-a-release-artifact).
Like the release, the image initializes and opens only the current store
format, `broodling.application` schema 1. Under
[#169's fresh-state decision](https://github.com/faviann/broodling/issues/169#issuecomment-5824930913)
there is no legacy invocation state to carry, and pre-transition stores refuse
unchanged.

The build regenerates the approved execution asset with
[`generate.sh`](../src/Broodling/execution-assets/generate.sh) and the pinned
native, then fails unless the published `execution-assets/` asset equals the
generated bytes and the [approval manifest](../src/Broodling/execution-assets/approval.json)
equals the reviewed file. `generate.sh` verifies SHA-256
`10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8` and native
admission offline, without credentials, target or provider. See
[execution asset](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset).
Without the asset, HTTP preparation refuses.

### DirectTarget image

The [target recipe](#existing-target-readiness) pins Node 22.23.2, Codex
0.153.4, gh 2.101.0 and the native binary hash. It runs rootful with Docker's
default capabilities, and its entrypoint requires explicitly initialized state.
Zeroshot materializes its own execution checkout, separate from Broodling's
source and Git custody; the `zeroshot` and `broodling` services share only the
read-only public root.

- Frozen-reference access (#114): the image installs
  `/usr/local/bin/broodling-reference`, which native agents run to read one
  RequestBundle reference from the read-only HTTP reader. Its reader origin is
  the image file `/etc/broodling/reader-origin`, `http://broodling:8080`. Native
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
  helper from a real reader. It is not production topology (homelab-iac#353), a
  real GitHub PR or provider quality evidence.
- Submit timing: the target acknowledges a run only after its own checkout. In
  the witness, with a local forge, acknowledgement took about 0.4 s and the
  unavailable-B1 refusal about 2.5–2.9 s. A slow real fetch can exceed
  Broodling's fixed 60-second submit budget; the send then stays unresolved
  until an exact replay, which converges on the same run.

### Publication and release records

The [images workflow](../.github/workflows/images.yml) runs for every push. It
builds both images, runs the [image demonstration](../tests/README.md#image-demonstration)
on them, the [native-state transition check](../tests/README.md#native-state-transition-check)
on the target image and `ApplicationSchemaFreezeTests`, and then publishes
exactly those images to GHCR:

- A branch push publishes `sha-<full commit>` candidates, so a pull request's
  head is published before merge.
- A `v*` tag publishes that tag name and attaches the release record to a
  GitHub release of the same name. Publish a version by pushing the tag only.
  Do not create the GitHub release first: that also creates the tag, and the
  workflow's release creation then fails after the images were pushed. A tag
  publishes nothing unless the Broodling image's application schema is frozen
  (see [application schema compatibility](#application-schema-compatibility)).
- Pull requests from forks only build, demonstrate and run the checks.

Published tags are never moved: a run refuses to publish over an existing tag,
and a re-run of a published commit therefore fails at publication. Select images
by digest. If publication fails after only one image was pushed, a re-run refuses
too; that half-published tag is not a release and has no record. Publish again
from a new commit or a new version tag.

Each publishing run uploads a `release-record` artifact, `release-record.json`
(kind `broodling.image-release/v1`). For a `sha-` candidate that workflow
artifact is the only copy, and it expires under the repository's artifact
retention; a `v*` version keeps its record on the GitHub release. The record
contains:

- the source revision, and the version or `sha-` tag;
- `images.broodling` and `images.zeroshot` as `repository@sha256:DIGEST`, and
  `images["zeroshot-tls"]`, the pinned Caddy reference;
- `zeroshotTls`: the user and the Caddyfile to mount (path, SHA-256 and content);
- `application`: the exact application schema the Broodling image initializes
  and opens, taken from its own `initialize-store` output: `storeFormat`,
  `storeSchemaVersion` and `storeDefinitionSha256`, the SHA-256 of the schema
  definition. `upgradesFrom` lists the identities, with the same three fields,
  that its `upgrade-store` upgrades; it is empty so far (see
  [application schema compatibility](#application-schema-compatibility));
- `executionAsset`: the approved asset SHA-256 and its native pins from the
  approval manifest;
- `nativeStateTransitions`: `to`, the published `images.zeroshot`, and `from`,
  the published target images listed in
  [`native-state-transitions.json`](native-state-transitions.json) at that
  revision, each with its source revision and native version and executable
  SHA-256 (see [native-state transitions](#native-state-transitions));
- `demonstration`: the native, Codex, Node and gh versions and the other
  dependency facts that `check-target` observed in the demonstration.

A record states that these two images passed the demonstration together at that
revision, and that its target image passed the transition check from each listed
source. It is not a compatibility registry, and the two images need not share a
version. It does not establish supported-profile readiness (#126). An operator
records a deployed target's image ID from `docker inspect` of the running
container in the readiness inventory.

The first publication created the `broodling` and `broodling-target` packages
linked to this repository and, like it, public: anonymous pulls by digest work.
Visibility is an operator setting in each package's settings; the workflow
never changes it. Keep both packages readable by the installation host.

### Native-state transitions

An update may change the `zeroshot` image only by an established transition. A
release establishes exactly these:

- from each published target image listed in its revision's
  [`native-state-transitions.json`](native-state-transitions.json) to its
  record's `images.zeroshot`;
- keeping the deployed target image, which changes no native state.

No `v*` version has been tagged, so the list holds published `sha-` candidates,
not versions: the two target images published from `main` since #121,
`sha-ae6fe2baa1beffe083683bddb6ce8cced5eecfc9`
(`@sha256:48055a75977cf85c95739abae4b6b9c348fd3f191509f6b8d596c55bbc854dc5`) and
`sha-47b4e4b86b1f6ca9af9a1dafae8f34ccc6e4980a`
(`@sha256:a17d56177d3794eaf19bf22f2040b5b41cac3faf28f52cf08dd43da79a5d645f`).
Each listed image and this revision's image carry native `zeroshot 10.3.0`,
executable SHA-256
`afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06`, so every
established transition keeps that native version. No transition to or from
another native version, and none from an unlisted image, is established. Adding
a published image to the list is a reviewed change that the next workflow run
checks.

Before publishing, the images workflow runs the
[transition check](../tests/README.md#native-state-transition-check) from every
listed source to the candidate target image; if one fails, nothing is published.
The host update procedure owns refusing any other selection.

Keeping the deployed target image needs no swap: the `zeroshot` container,
its mounts and native state stay as they are. The new record's demonstration
covers its own two images together, not that older target image with the new
Broodling image. `check-target` still applies after the update, with the
inventory's `imageId` unchanged, because it also inspects the replaced
`broodling` container.

To change the `zeroshot` image, the procedure needs:

- The deployed `zeroshot` image, selected by digest, to be a listed source.
- The installation pause, drained execution with its results captured, and
  `zeroshot` stopped for the swap. The check covers finished runs and a
  submission that the source image recorded without acknowledging; a run still
  active at the swap may become `RuntimeLost`, as on any restart.
- The same host directories, unchanged, at `/state` and `/home/node`, and the
  same public root read-only at `/tls-root`.
- The same arguments without `initialize`: `--listen 0.0.0.0:18770
  --public-origin` with the recorded origin, and `--storage /state`. Ordinary
  startup of the new image refuses unrecognized state or a different origin;
  never initialize over existing state.
- Unchanged container root with Docker's default capabilities, the `zeroshot`
  alias, `zeroshot-tls` and its root, and Broodling's `directOrigin` and
  `directRootCertificate`: retained Attempts reconnect to their retained origin.
- Native UID ownership preserved: no recursive ownership change.
- Afterwards, `check-target` with the inventory's `imageId` updated to the new
  image.

Rollback is not a reverse transition: restore the pre-update snapshot with the
prior image. Running a prior image over state that a newer image served is not
established.

### Application schema compatibility

The application schema is separate from native state. A record's `application`
identifies it exactly, so the update procedure decides from the deployed
release's record and the selected release's record alone, without running
either image:

- Equal `storeFormat`, `storeSchemaVersion` and `storeDefinitionSha256`: the
  selected image opens the deployed store unchanged.
- Otherwise the selection is compatible only if the deployed record's three
  fields are an entry of the selected record's `application.upgradesFrom`: the
  selected image's `upgrade-store` upgrades that store. Any other selection is
  unsupported. No release upgrades an earlier identity yet.
- A record without `storeDefinitionSha256`, published before that field
  existed, does not identify its definition and establishes no compatibility.

Apply a compatible selection explicitly with the selected image's
`upgrade-store` against the stopped application's store, for example
`docker compose run --rm --no-deps broodling upgrade-store /var/lib/broodling/state.sqlite3`.
It accepts a store of exactly its current identity unchanged, upgrades one listed
in its `upgradesFrom` (none so far), and refuses anything else without change
(`StoreLifecycleTests`,
[state lifecycle](../docs/implementation/dotnet-identity-custody.md)). A released
earlier identity that it does not upgrade is refused with `released_schema_refused`
and that identity's documented reason in `reason`; any other store is refused with
`incompatible_store`. Either refusal means the selection is unsupported.

[`application-schemas.json`](application-schemas.json) freezes each released
identity of the `broodling.application` format by `schemaVersion` and
`definitionSha256`. Once a version is frozen its definition never changes: any
later schema change increments the version. The images workflow enforces this
before it pushes any image:

- On every push, `ApplicationSchemaFreezeTests` compares this revision's code
  with the list. The current version must not be below a frozen one, a frozen
  current version must have the current definition, and every older frozen
  version needs an explicit `upgrade-store` disposition in `StoreSchema`: an
  upgrade listed in `UpgradesFrom` or a refusal in `Refused` whose reason is
  documented here.
- On a `v*` tag, the image's reported identity must be in the list, and the
  list must freeze each version at most once. It is checked against the
  records attached to earlier `v*` releases: the list must still contain each
  of their `application` identities, and none of them may have the image's
  format and version with another definition. A failure to list those
  releases or download a record also refuses publication. The gate relies on
  those records staying attached: do not delete or replace them. A `v*`
  release without a record refuses every later tag until its run's
  `release-record` artifact is attached with `gh release upload`.

`sha-` candidates may carry an unfrozen identity. No version is frozen yet, so
no refusal reason exists.

The first `v*` version freezes schema version 1. Before tagging it, take
`application.storeDefinitionSha256` from the release record of the `sha-`
candidate to be released, add `{"schemaVersion": 1, "definitionSha256": "…"}`
to `frozen` in a reviewed commit, and tag that commit once its own candidate
record reports the same identity. Until then `sha-` candidates of schema
version 1 can carry different definitions and refuse each other's stores: after
#123 the `sha-47b4e4b…` Broodling image refuses a store that the `sha-ae6fe2b…`
image initialized with `incompatible_store`. Records published since #206 tell
such candidates apart by `storeDefinitionSha256`.

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
`retire-attempt <path> <attempt-id> <stopped-target-check-json>`, then, still
paused, may replace an abandoned retired Attempt with
`replace-attempt <path> <predecessor-attempt-id> <retry-key>`, which prepares but
never dispatches the successor; after `release-installation`, `resume` of its
Contract revision dispatches it; see
[verified maintenance retirement](../docs/implementation/dotnet-retirement-replacement.md#verified-maintenance-retirement).
See [state lifecycle](../docs/implementation/dotnet-identity-custody.md).

The host routes commands before HTTP startup. Running it without a command starts
the HTTP server over existing state named by `Broodling:Store`
(for example `--Broodling:Store=/NEW/state.sqlite3` or `Broodling__Store`); bind
it with the standard `--urls`/`ASPNETCORE_URLS`. Startup only opens the store and
refuses missing or incompatible state with a safe error code before listening.
Without processing configuration it only reads; with it, it is the
[processing server](#processing-server). Each request uses its own session and
never contacts GitHub or a provider. `GET /health` opens the store and answers
503 when it is unavailable. Retained reads are `/issues?url=<issue-url>`,
`/submissions/{id}`, `/submissions/{id}/bundle`,
`/bundles/{id}/reference?id=<reference-id>`, `/revisions/{id}` and
`/attempts/{id}`, mapping the application reads in
[invocation](../docs/implementation/invocation.md).

`/submissions/{id}` returns `{submission, admission, progression, observation}`
and `/attempts/{id}` returns `{attempt, submission, completion, observation}`.
The retained facts come from the store: the accepted submission with its state,
proposal refusal and cancellation, and the admission decision with its findings;
capture refusals are in the bundle. `progression` is the processing server's
in-process state for an unfinished submission (`progressing`, `waiting` with
`retryAt`, or `stopped`, with the last failure's stage, code, retryable flag and
count; its message stays in the server log), or null. `observation` is one bounded (10 s), unretained native read of the
submission's latest Attempt or of that Attempt: `availability` `available`
(with `phase` and `activeNodes`) or `unavailable` (with a fixed `reason`), its
`observedAt` time and `runIdentity` (`intended` or `confirmed`). It is null when
no run is addressable or the completion is already retained. An unreachable
target makes only the observation unavailable; it is never an execution failure
and the retained facts still answer.

### Invocation configuration

A secret-free `config.json` names exactly one target kind. For authorized PR
invocation over the HTTP DirectTarget:

```json
{
  "target": "direct",
  "directOrigin": "https://zeroshot.dev.faviann.com",
  "directRootCertificate": "/NEW/zeroshot-tls/root/root.crt"
}
```

It needs no Python executable, SDK client state, workspace root or launcher. The
origin follows the native rule: canonical HTTPS, or literal-loopback HTTP such as
`http://127.0.0.1:18770`, spelled as scheme and authority only, with a default
port omitted and never port 0. Anything else refuses.

The optional `directRootCertificate` is the absolute path of a PEM root
certificate: for the homelab stack, the `root.crt` that
[`initialize-tls`](#explicit-initialization-and-guarded-startup) created in the
public root directory. When it is set, HTTPS and WSS
connections to the DirectTarget trust exactly that root, not system trust. When
it is unset, system trust applies. No option disables certificate validation.
Each new TLS connection rereads the file, so a regenerated root is used without
restarting Broodling. A missing or unreadable file fails only
that operation, as a transport failure; a dispatch fails before recording any
dispatch intent. `check-target` trusts this same file for its discovery.

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

### Processing server

The server accepts, resumes and stops exact work and processes accepted work
unattended when it is configured with both:

- `Broodling:Invocation` (`Broodling__Invocation`): the path of a Direct
  [`config.json`](#invocation-configuration). Its origin is where new Attempts
  are sent and its root certificate is trusted for every target connection.
- `Broodling:RepositoryRoot` (`Broodling__RepositoryRoot`): an existing absolute,
  durable directory writable by the service, where it keeps the service-owned
  bare repositories that hold each submission's B1 and accepted commits. It is
  durable operating state: keep it at its path with the store.

Its process environment must hold `GH_TOKEN`, `GATEWAY_API_KEY` and exactly
`GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`. They are read for each
operation and never retained or echoed; in Compose, supply them only to
`broodling` through its own protected env file. Startup refuses, with a safe
code and before listening, one setting without the other, a LocalTarget
configuration, a missing repository root or missing or invalid credentials. The
host also needs Git and `gh` (the image has both). Completion fetches each
accepted commit from the repository's GitHub origin with plain Git, using the
service user's Git credential configuration. The image configures
`credential.https://github.com.helper` as `!/usr/bin/gh auth git-credential` in
its system Git configuration, so that fetch uses the current `GH_TOKEN` and a
private repository's result is retained. A release-artifact host needs the same
helper for the service user (`gh auth setup-git`, or that `git config`);
without it a private repository's result is never retained and observation
retries it. There is no application authentication: expose the server only to
trusted callers.

| Route | Maps to | Answers |
| --- | --- | --- |
| `POST /submissions` with `{"issueUrl": "…"}` | `SubmitIssue` | `202` with `{submission}` and `Location: /submissions/{id}` once the submission is committed, with no GitHub, model or target contact. Repeating an issue URL returns its latest existing submission. `400 invalid_work_reference` before anything is written. |
| `POST /submissions/{id}/revisions` | [`ReviseIssueSubmission`](../docs/implementation/invocation.md#revised-work) | `202` with `{submission}` and `Location: /submissions/{id}` of the successor once it is committed, and the same successor on replay. `409 issue_submission_conflict` while the predecessor is unfinished or not the latest, an Attempt is current, or a dispatched Attempt is not yet retired under verified maintenance. An `unchanged` successor's read carries its explanation and the linked submission and Contract. |
| `POST /submissions/{id}/resume` | [`SubmissionProgressor.Resume`](../docs/implementation/invocation.md#automatic-progression) | `202` with `Location` when that unfinished submission is continued at the next scan, forgetting an in-process stop or wait. `200` with `resumed: false` when its end or native correlation is retained. Never a Replacement Attempt. |
| `POST /submissions/{id}/stop` with `{"reason": "…"}` | `CancelIssueSubmissionAsync` | `200` with `{submission, attempt, error}` once the cancellation is committed: `attempt` is the stop report of the Attempt it abandoned, if any. `409` for completed work. |
| `POST /attempts/{id}/stop` with `{"reason": "…"}` | `StopAsync` | The `stop` command's report: `200` once abandonment is committed, with `error` naming what ended the native stop (such as `cessation_unconfirmed`, a transport timeout, or `caller_detached` at shutdown); `409` when nothing was abandoned. A dispatched LocalTarget run refuses with `python_required`, unabandoned. |

A stop without a reason answers `400 reason_required`. A reader answers
`503 processing_not_configured` to all five.

For the server's lifetime, the process's one preparer,
[automatic progression](../docs/implementation/invocation.md#automatic-progression)
and [completion observation](../docs/implementation/dotnet-receipt-completion.md#automatic-completion-observation)
run at startup and then every 15 seconds, so a new submission starts within
about 15 seconds. Neither a response nor a disconnect changes accepted work, and
a stop continues to its own bound (30 seconds for the native stop) after its
caller leaves. Ordinary shutdown detaches both services without stopping,
abandoning or cleaning up anything; the next process continues from retained
checkpoints and replays an unresolved dispatch exactly. A submission that stops
for attention is logged and shown as its `progression`; resolve the cause, then
resume it or restart the server. An Attempt whose completion observation fails
unexpectedly is logged and not observed again until the server restarts;
resume does not re-arm it, so restart is the remedy. If either service fails unexpectedly, the
server logs it, stops and exits with status 1, so that a supervisor restarts it
rather than it accepting work it would not process.

## Existing-target readiness

`TargetReadiness.CheckAsync` and the thin command check an existing
[ADR 0001](../docs/adr/0001-directtarget-https-origin-and-compose-topology.md)
stack's configuration and dependencies without an installer. Run it on the
Docker host as the account with Docker access:

```bash
dotnet /RELEASE/host/Broodling.Host.dll check-target /NEW/target-inventory.json /NEW/config.json
```

An operator records the **existing stack's** containers, the exact `zeroshot`
image, the Compose project network and canonical host mount paths in
`target-inventory.json`:

```json
{
  "containerName": "broodling-zeroshot-1",
  "imageId": "sha256:EXACT_EXISTING_IMAGE_ID",
  "directOrigin": "https://zeroshot.dev.faviann.com",
  "stateMount": "/NEW/target-state",
  "homeMount": "/NEW/target-home",
  "network": "broodling_default",
  "tlsContainerName": "broodling-zeroshot-tls-1",
  "rootKeyMount": "/NEW/zeroshot-tls/key",
  "rootCertificateMount": "/NEW/zeroshot-tls/root",
  "broodlingContainerName": "broodling-broodling-1"
}
```

The [readiness reference](../docs/implementation/dotnet-target-readiness.md)
lists every check: the `zeroshot` image, running/root/isolation/restart settings,
exact mounts, no published port, fixed launch arguments and the `zeroshot` alias
on the project network; `zeroshot-tls`'s pinned image, non-root user,
`NET_BIND_SERVICE`-only capabilities, 443 publication, origin alias and separate
root mounts; a read-only public root mount and no key mount in `broodling`;
credential exclusion, native/Codex/Node/gh pins and binary hashes,
`gh api graphql --paginate --slurp`, the hosted UID/GID transition, and native
discovery through `zeroshot-tls` using the configuration's root. Both files must
select the same origin, and the configuration's `directRootCertificate` must be
`root.crt` in the recorded `rootCertificateMount`. It creates no target/state and
dispatches zero provider tasks.

The [DirectTarget Dockerfile](DirectTarget.Dockerfile) records the
target dependency recipe: Node **22.23.2**, Codex **0.153.4**, gh
**2.101.0** and the native **10.3.0** binary hash. Its build context is
`deployment/`; it fetches the native binary from the pinned SDK wheel itself.
The [images workflow](#publication-and-release-records) builds and publishes
it. Target provisioning is operator-owned and separately authorized; nothing
here deploys it.

The supported topology is ADR 0001's single Compose project on rootful Docker on
one trusted host:

- `zeroshot` runs this image as container root with Docker's default
  capabilities. It has three bind mounts: state at `/state`, home at
  `/home/node` and the public root directory read-only at `/tls-root`. Native
  listens on the project network at the fixed inner port **18770**, which is
  never published.
- `zeroshot-tls` runs
  `caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b`
  as the package-defined user **`10443:10443`**, with `cap_drop: [ALL]` and
  `cap_add: [NET_BIND_SERVICE]`. It mounts the root key directory read-only at
  `/tls-root-key`, the public root directory read-only at `/tls-root`,
  [`zeroshot-tls.Caddyfile`](zeroshot-tls.Caddyfile) read-only at
  `/etc/caddy/Caddyfile`, and a named volume at `/data`. It listens on 443,
  publishes that port on the LXC and carries `zeroshot.dev.faviann.com` as its
  alias on the project network.
- `broodling` mounts only the public root directory, read-only, and never the key
  directory or Caddy's data.

Native access is unauthenticated: callers that Traefik admits and anything that
reaches the published port are trusted (ADR 0001). Rootless or altered UID
mapping, and Docker-socket or extra credential/source mounts, are outside this
profile. Preserve native UID ownership; never recursively chown used target
storage.

### Explicit initialization and guarded startup

The target image entrypoint is `/usr/local/bin/broodling-target`. It keeps the
native paths `/state`, `/home/node` and `CODEX_HOME=/home/node/.codex` and the
listener `0.0.0.0:18770` fixed. `ZEROSHOT_CONFIG_DIR` and `XDG_CONFIG_HOME`
overrides are refused. Select a published image by digest, or build it from the
repository root:

```bash
docker build -f deployment/DirectTarget.Dockerfile -t broodling-target:REVIEWED_REVISION deployment
```

For an authorized **new installation only**, first provision four empty durable
host directories: target state and home, with explicit root ownership and
private permissions, and the root key and public root directories. Then, in
this order:

1. **Create the TLS root once**, before `zeroshot-tls` first starts. The target
   image's one-off root helper runs as container root:

   ```bash
   docker run --rm --network none \
     --mount type=bind,src=/NEW/zeroshot-tls/key,dst=/tls-root-key \
     --mount type=bind,src=/NEW/zeroshot-tls/root,dst=/tls-root \
     broodling-target:REVIEWED_REVISION initialize-tls
   ```

   It writes an OpenSSL P-256 key, `root.key`, mode `0400` in the key directory,
   which becomes mode `0700`; `10443:10443` owns both. It writes a self-signed
   CA certificate valid for ten years, `root.crt`, mode `0644` and root-owned,
   as the only file in the public directory (`0755`). It refuses unless both
   locations exist, are empty and are distinct, so it never overwrites or
   regenerates a root. A failure leaves any partial file for inspection.
2. **Start `zeroshot-tls`** (`docker compose up -d zeroshot-tls`). Caddy signs
   only with the provided root (`pki { ca local { root { cert, key } } }`) and
   issues and renews its own intermediate and leaf, stored under `/data`. With a
   missing or mismatched root file it exits with status 2 (a bare panic) and
   generates nothing. The image's `/data/caddy` is world-writable with the
   sticky bit, which the non-root user needs, so use a named volume that Docker
   fills from the image rather than an empty bind mount.
3. **Initialize native state** through the origin:

   ```bash
   docker compose run --rm --no-deps --use-aliases zeroshot initialize \
     --listen 0.0.0.0:18770 --public-origin https://zeroshot.dev.faviann.com --storage /state
   ```

   `--use-aliases` gives the one-off container the `zeroshot` alias, so
   `zeroshot-tls` can forward to it. Initialization refuses nonempty state or
   home, including partially initialized state, and requires
   `/tls-root/root.crt`. It briefly serves native on the project network,
   records `broodling` through native `target add` in the home registry, uses
   public `zeroshot list` to create the native ledger without submitting work,
   and stops that process. Native's client reaches the origin through the alias
   and `zeroshot-tls`, and `SSL_CERT_FILE=/tls-root/root.crt` makes it trust
   only the public root. Success leaves no native server running. Failure
   leaves partial durable state for inspection; it never deletes it or silently
   retries over it. If it cannot reach the origin, check that `zeroshot-tls` is
   running with this root and that the one-off container has the `zeroshot`
   alias. To retry, deliberately clear the partial state and home first:
   initialization refuses nonempty state.
4. **Start the target** (`docker compose up -d zeroshot`) with the same
   arguments without `initialize`.

Pinned native, not the entrypoint, decides which public origins are valid and
records its canonical spelling; configure exactly
`https://zeroshot.dev.faviann.com`. `target serve` provides no TLS;
`zeroshot-tls` terminates it, and the Caddyfile names that host. Changing the
origin later requires a stopped-target transition of native state.

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
origin. Existing targets without this binding, including targets bound to a
loopback origin, require a separately reviewed stopped-target transition; this
command does not adopt them. Readiness rejects the former unguarded native
entrypoint.

For operator diagnosis, run native's client inside the target with the public
root:

```bash
docker compose exec zeroshot env SSL_CERT_FILE=/tls-root/root.crt zeroshot list --target broodling
```

Set `SSL_CERT_FILE` per command, not in the service environment, so nothing
else in the container trusts only the private root. Without it, native uses
system trust and reports only `Zeroshot observation transport disconnected`.

Neither mode recursively changes ownership. Native itself prepares traversable
state/run roots; existing run-specific UIDs, GIDs and permissions remain native's
responsibility. Keep the target stopped during mount changes and serialize
operator initialization/startup. These checks recognize initialized native files
and the origin binding, not snapshot freshness or cross-version compatibility: a
matching old snapshot or another valid state/home pair with the same origin
cannot be distinguished. There is no new installation identity, private run-row
inspection, history pruning, upgrade, backup/restore or maintenance protocol here.

### TLS root rotation

Rotation is a deliberate step, not something Caddy or Broodling does:

1. Stop `zeroshot-tls` (`docker compose stop zeroshot-tls`).
2. Move `root.key` and `root.crt` out of their directories together. Keep any
   copy of the old key out of the public directory.
3. Remove Caddy's stored intermediate and leaf, as its own user:

   ```bash
   docker compose run --rm --no-deps --entrypoint rm zeroshot-tls \
     -rf /data/caddy/pki/authorities/local /data/caddy/certificates/local
   ```

4. Create the new pair with `initialize-tls`, exactly as at first
   initialization, and start `zeroshot-tls` again.
5. Update Traefik's copy of the root certificate, then run `check-target`.

Broodling rereads the root for each new connection, and native's client reads it
for each command, so neither needs a restart. If step 3 is skipped, or Caddy's
data is restored without its matching root, Caddy keeps serving an intermediate
signed by the old root. Clients trusting the new root then refuse the
connection, and `check-target` reports discovery as unavailable.

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
tracked configuration or a command's Docker environment; the
[processing server](#processing-server) takes them from its own protected
service environment. The fixed runtime is standard
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
never-dispatched retirement and replacement remain
[callable operations](../docs/implementation/dotnet-retirement-replacement.md),
not an automatic CLI recovery sequence. `replace-attempt` replaces an abandoned,
retired HTTP predecessor at its retained origin; only a `stopped_target`
predecessor requires the pause.

### Bundled Contract proposer

The callable `AdmitRequestBundleAsync` prepares a completed RequestBundle's
Contract with the one built-in proposer
([reference](../docs/implementation/dotnet-contract-admission.md#bundled-proposer)).
The callable `IssueSubmissionPreparer` (#116) runs it after capture, and the
callable `SubmissionProgressor` (#117) calls that preparer with no caller
connected. The [processing server](#processing-server) runs them for its
lifetime (#120); no host command does. The proposer's profile
is fixed, not configurable:

| Setting | Value |
| --- | --- |
| Gateway | OpenAI-compatible Chat Completions at exactly `https://cliproxy.local.faviann.com/v1` |
| Model | `gpt-5.6-sol` |
| Credentials | Current `GATEWAY_API_KEY` and exactly `GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`, read from the process environment for each proposal |
| Trust | The host's system TLS trust for the gateway |

The gateway's support for these requests was confirmed on 2026-09-26 by a probe
that mirrored `BundledProposer`'s exact request against
`https://cliproxy.local.faviann.com/v1`. Turn 1 used tools, `tool_choice: auto`
and the `json_object` response format, and the model returned a `read_reference`
tool call. Turn 2 carried the tool result under `tool_choice: none` and returned
a valid JSON object. Proposal quality on real issues is still unverified.

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
exact target image (changed only by an [established transition](#native-state-transitions))
and origin, release/dependency environment and operator inventory
at their retained paths. Protect native session state and private content as
sensitive. Retain exact accepted Git objects and full receipts for independent
review; moving PR branch tips are insufficient.

Cancelling the caller does not stop native work. Target restart can preserve
completed results; interrupted active native runs may become `RuntimeLost`.
A fresh empty target at an old origin cannot recover them. Reconnect to the
retained identity and inspect/consume its outcome; do not silently replace it.

For a consistent filesystem backup, stop callers and the processing server, and quiesce/stop the native
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
