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
include .NET 10 / ASP.NET Core 10, Git and the GitHub CLI `gh` for issue and
repository acquisition by `submit` and the processing server. No Python is
needed. Run Broodling as an unprivileged dedicated account; with the
[Broodling image](#broodling-image), commands that share its store run as its
user `1654:1654`. Only the host-side
`check-target` command needs access to the local rootful Docker socket; the
[Broodling image](#images) never mounts one. In an LXC, the host
operator must enable Docker nesting and the UID/GID operations rootful Docker
needs. No installer checks these host prerequisites; the operator owns them.

Restore needs a `read:packages` GitHub credential for the pinned `Zeroshot.Client`,
supplied outside the repository as in the [test setup](../tests/README.md#run).
Every restore refuses a package whose bytes differ from the content hash in
`src/Broodling/packages.lock.json`. A CI build (`-p:ContinuousIntegrationBuild=true`,
as the Broodling image build sets it) restores that project in locked mode and
also refuses a lock file that no longer matches its package references; a local
restore rewrites a stale lock file instead. Release only a revision whose `Zeroshot.Client`
version and lock file match the [current adoption record](../tests/README.md#current-adoption-record);
an SDK upgrade first reruns Broodling's [adoption lane](../tests/README.md#upgrading-the-sdk).
From the repository root, choose a new output directory for each release:

```bash
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export UseSharedCompilation=false
export NUGET_HTTP_CACHE_PATH="$PWD/tmp/nuget-http"
dotnet build Broodling.sln --configuration Release
dotnet publish src/Broodling.Host --configuration Release --output out/release/host
git rev-parse HEAD > out/release/source-revision.txt
tar -C out/release -czf out/broodling-linux-x64.tar.gz host source-revision.txt
```

Keep the complete publish directory. `host/` contains the framework-dependent
`Broodling.Host` entrypoint, managed assemblies, runtime/dependency manifests,
SQLite native assets, `libbroodling_git.so` and the approved DirectTarget
execution asset with its manifest under `execution-assets/`. Preserve executable
modes and package paths. Invoke the host with
`dotnet /RELEASE/host/Broodling.Host.dll`; its optional `Broodling.Host` apphost
requires a registered .NET installation or the appropriate `DOTNET_ROOT` when
the runtime lives in a nonstandard location. Publish neither
creates application state nor installs a target. There is no Python Broodling
package, installer or importable proposer. Protect and retain the selected
release for replay.

## Images

This repository builds, checks and publishes the `broodling` image. Each
`zeroshot-<name>` target service runs an adopted Zeroshot environment image,
one per catalog environment ([several targets](#several-targets-235)), which
[`faviann/zeroshot-environments`](https://github.com/faviann/zeroshot-environments)
builds and publishes ([adopted target environment](#adopted-target-environment)).
`zeroshot-tls` runs the upstream pinned Caddy image unchanged.

| Service | Source | Image | User |
| --- | --- | --- | --- |
| `broodling` | [`Broodling.Dockerfile`](Broodling.Dockerfile), repository root | `ghcr.io/faviann/broodling` | `1654:1654` |
| `zeroshot-<name>`, such as `zeroshot-dotnet` | the adoption record, such as [`dotnet-r6.adoption.json`](../src/Broodling/environments/dotnet-r6.adoption.json) | the record's `image` by digest (`dotnet-r6`) | container root |
| `zeroshot-tls` | not built | `caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b` | `10443:10443` |

To build and check them locally from the repository root:

```bash
export NuGetPackageSourceCredentials_github="Username=YOUR_GITHUB_USER;Password=READ_PACKAGES_TOKEN"
docker build -f deployment/Broodling.Dockerfile \
  --secret id=nuget-github,env=NuGetPackageSourceCredentials_github -t broodling:REVIEWED_REVISION .
target="$(jq -r .image src/Broodling/environments/dotnet-r6.adoption.json)"
docker pull "$target"
tests/images/demonstrate.sh broodling:REVIEWED_REVISION "$target"
```

The Broodling build verifies the asset with the
[DirectTarget binding](../docs/implementation/zeroshot-native-integration.md#pinned-dependencies)'s
native, `zeroshot 10.12.0` from source `928de8ff918488749ca9d23e0a7349873401b7f7`.
It copies that `zeroshot` executable out of upstream's published target image
for the release, pinned by digest in the Dockerfile and the approval manifest.
The adopted environment is built on the same image and carries the same
executable and `restic`. Base images are pinned by digest; apt packages come
from the base distribution at build time, so select a published image by
digest, not by rebuilding.

### Broodling image

The runtime stage is ASP.NET Core 10.0.12 on Ubuntu 24.04. The build stage uses
the matching SDK 10.0.401 image, so `libbroodling_git.so` links against the
runtime's libc. The image holds the published host output at `/app`, including
`execution-assets/`, plus Git, gh 2.101.0 (the adopted environment's version, also
Git's credential helper for `https://github.com`), curl and tini. It runs
`dotnet /app/Broodling.Host.dll` under tini as the image's non-root `app` user,
`1654:1654`; tini is PID 1, forwards signals to the host and reaps orphaned
child processes.

- With no arguments it serves HTTP on port **8080** over
  `Broodling__Store=/var/lib/broodling/state.sqlite3`: the read-only reader, or
  the [processing server](#processing-server) when the service also sets
  `Broodling__Invocation` and `Broodling__RepositoryRoot` and supplies
  credentials. For the ADR 0002 stack, mount the invocation `config.json` read-only
  (for example at `/etc/broodling/invocation.json`): the
  [environment catalog](#invocation-configuration) naming, for `dotnet`,
  `https://zeroshot-dotnet.dev.faviann.com`, the control token file
  `/run/secrets/zeroshot-dotnet-control-token`, and `/tls-root/root.crt`. Give
  the service each target's token file as its own secret (owned by `1654:1654`,
  mode `0400`; see [private control access](#private-control-access)), and use
  `Broodling__RepositoryRoot=/var/lib/broodling/repositories`, a directory the
  operator creates in the state directory, owned by `1654:1654`.
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
  `docker compose run --rm --no-deps broodling replace-attempt /var/lib/broodling/state.sqlite3 ATTEMPT_ID RETRY_KEY /etc/broodling/invocation.json`,
  adding `--update-environment` only to sign off on what the catalog now names
  for the predecessor's environment.
  Such an Attempt's B1 and accepted-revision pins are in its service-owned bare
  repository under `Broodling:RepositoryRoot`, recorded as the container path.
  With that root in the state directory (`/var/lib/broodling/repositories`), the
  commands need only the state mount: no credentials (dispatch or control),
  network, host release artifact or caller checkout.
  They keep every refusal and pause requirement they have from the release
  artifact. The image has no Docker
  socket, so the host maintenance procedure stops and verifies the target and
  supplies the stopped-target check.
- After `release-installation`, restart the target and the processing server,
  then dispatch that Attempt's prepared successor with the image's `resume`, run
  as the processing service itself:
  `docker compose run --rm --no-deps broodling resume /var/lib/broodling/state.sqlite3 CONTRACT_REVISION_ID /etc/broodling/invocation.json`.
  Run it with the same Compose files and overrides as the running processing
  service, so that it gets that service's environment, invocation configuration
  and mounts; do not add credentials with `-e`. `CONTRACT_REVISION_ID` is the replaced Attempt's `contractRevisionId`, from
  `GET /attempts/{id}` or `history`. The command needs what the server has: the
  state mount, whose repository root holds the successor's B1 custody at its
  recorded container path; the invocation `config.json`, which must still serve
  the successor's retained environment exactly and whose root it trusts; `GH_TOKEN`,
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
  configuration that names the host paths of `root.crt` and of the operator's
  copies of the control tokens.
- `bootstrap-target` installs one environment's control token in its newly
  started target. Run it as a one-off `broodling` container on the project
  network, the only one given that target's bootstrap key:
  `docker compose run --rm --no-deps -v /NEW/secrets/bootstrap/zeroshot-dotnet-bootstrap-key:/run/secrets/zeroshot-bootstrap-key:ro broodling bootstrap-target /etc/broodling/invocation.json dotnet /run/secrets/zeroshot-bootstrap-key`.
  See [private control access](#private-control-access).

The image contains no Python, native executable or agent harness; its only
Zeroshot client is the pinned `Zeroshot.Client` library. DirectTarget
submission, observation and control need no client helper process; the
processing server runs them in the image, while the CLI invocation commands run
from the [release artifact](#build-a-release-artifact) as described above.
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
`7e232da54ed2ca138b31fea38a6f002d08d77b584965253b18077205b2c1e5be`, the
asset structure and native admission offline, without credentials, target or provider. See
[execution asset](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset).
Without the asset, HTTP preparation refuses.

### Adopted target environment

The producer, `zeroshot-environments`, owns the environment recipes, their
inventories, image production and publication. Broodling owns adoption: the
adoption evidence, the Execution asset and its approval, and the entrypoint and
its startup interface ([entrypoint boundary](#entrypoint-boundary)).
Publication alone neither adopts nor deploys an image, and adoption deploys
nothing.

The adoption record bundled with the release,
[`dotnet-r6.adoption.json`](../src/Broodling/environments/dotnet-r6.adoption.json)
(`broodling.target-environment-adoption/v1`), names one adopted environment
release:

- `environment`: the environment release identifier, `dotnet-r6`. It is the
  producer's `<environment>-r<N>`, separate from the native release that the
  manifest records. Several environments can carry one native release.
- `producer`: the repository, the release and the release asset the manifest
  came from.
- `manifest`: the retained exact bytes of that `manifest.json`
  (`zeroshot-environment.manifest/v2`), at
  [`dotnet-r6.manifest.json`](../src/Broodling/environments/dotnet-r6.manifest.json),
  and their SHA-256. A mutable registry or release lookup is not evidence.
- `image`: the adopted image by digest, which the manifest also records.

The manifest states the image digest, the recipe revision, the platform, the
OS and base image identity, every installed component with its exact version,
native's identity, the harness versions and the startup interface versions.
Each component names what fixes its bytes: its downloaded artifacts' checksums,
or the base image digest that encloses it. `dotnet-r6` carries native 10.12.0
(upstream's source build, executable SHA-256 `7b3c74db…`), Claude Code 2.1.276,
Codex 0.155.0 and Copilot CLI 1.0.86 on harness Node.js v24.22.0, .NET SDK
10.0.401, gh 2.101.0 and Debian 13. Claude Code is a native executable; the
image's harness launcher starts it directly, without Node.js.

`dotnet-r6` also installs Claude Code's managed settings,
`/etc/claude-code/managed-settings.json`, owned by root with mode `0644` in a
`0755` directory. The manifest records their SHA-256 under
`harnesses.claude.managedSettings`:
`a469a57a1f61686f006df05d41973208f0bd9f81de219db17f4034882b038f2e`. Claude Code
reads them in every process, whatever environment native gives it, and they
take precedence over user and repository settings. They set
`CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`, pin `CLAUDE_CODE_EFFORT_LEVEL` to
`medium`, `CLAUDE_CODE_MAX_OUTPUT_TOKENS` to `32000` and
`CLAUDE_CODE_EXTRA_BODY` to `{}`, set `CLAUDE_CODE_DISABLE_ADVISOR_TOOL`,
`CLAUDE_CODE_DISABLE_BACKGROUND_TASKS` and `CLAUDE_CODE_DISABLE_WORKFLOWS`,
restrict requested models to `claude-opus-5-5` (`availableModels`), set
`skipWebFetchPreflight`, deny the `WebFetch` and `WebSearch` tools, set empty
commit and PR attribution, and allow only managed hooks and MCP servers, of
which the file defines none. The image's managed effort overrides the
Execution asset's per-node effort, so changing effort needs a new environment
release. The deny holds under native's hosted lane, which always passes
`--dangerously-skip-permissions`. What the pins bound:

- **Model.** A subagent that asks for another model, through the `Agent`
  tool, a repository agent or skill, or a repository's
  `CLAUDE_CODE_SUBAGENT_MODEL`, runs on `claude-opus-5-5`. The model is not
  told; only the stream output logs a warning. A session started with another
  `--model` falls back to Claude Code's built-in default, `claude-opus-5`
  with the 1M-context beta. Native passes the asset's `claude-opus-5-5`, so
  its sessions never take that path.
- **Request body.** A repository's or a mid-run settings file's
  `CLAUDE_CODE_EXTRA_BODY` cannot add effort, output tokens, `speed` or the
  advisor tool to a request, and the advisor stays off.
- **Background work.** `Agent` and `Bash` run in the foreground, and the
  `Workflow` tool and its `deep-research` and `workflow-authoring` skills are
  gone, so no call comes after the session's result. Native and the asset's
  prompts use none of them.
When the file is missing, Claude Code runs without these settings and reports
nothing. Adoption and `check-target` therefore require the file's bytes.

Adoption is checked in three places:

- `TargetEnvironmentTests` (the images workflow runs them on every push): the
  record's digest is the retained bytes' digest, and its image is the
  manifest's. The manifest is one this revision supports. Every component is
  fixed by an artifact checksum or the base digest. The image embeds this
  revision's entrypoint. The pulled image is the manifest's platform and
  digest, and its executables' bytes, every component version and its full
  Debian package list are the manifest's. Native's first agent identity,
  `20000:10002`, reads the managed settings with the approved SHA-256, and
  they are the only entry in `/etc/claude-code`, a regular file owned by root
  with mode `0644` in a root-owned `0755` directory. Started as that identity
  with the permission flag native's hosted lane always passes, the adopted
  Claude Code offers neither WebFetch, WebSearch nor Workflow, and asked for
  Haiku it warns that the model is restricted and starts on its built-in
  default.
- Loading the bundled adoption set, at every configuration read: it refuses a
  manifest that is not the record's bytes, a malformed manifest, a schema other
  than `v2`, a platform other than `linux/amd64` and a native, Claude Code,
  Claude Code managed settings, startup interface or discovery other than this
  revision's binding,
  and a component that readiness cannot probe.
  A catalog entry's `image` must be exactly one adopted image.
- [`check-target`](#existing-target-readiness) requires each running target to
  be its catalog image by digest and checks live its OS identity, every
  component the manifest states, the managed settings' bytes, and that they
  are the only entry in `/etc/claude-code`, with that type, owner and mode.
  Claude Code also applies
  `managed-settings.d` drop-ins and a `CLAUDE.md` there.
- The [image demonstration](../tests/README.md#image-demonstration), the
  [stock witness](../tests/README.md#controlled-stock-directtarget-witness) and
  the [transition check](#native-state-transitions) run the pulled image.

To adopt another release, retain its manifest as published
(`gh release download RELEASE -R faviann/zeroshot-environments -p manifest.json`),
compare its SHA-256 with the release asset's digest, and commit the bytes and a
new record. That change's workflow run qualifies it, and a Broodling release
bundles it. Deploying it is a separate step: a catalog entry's `image` names it
([several targets](#several-targets-235)). A release that changes only .NET or
OS packages, with the same native, Claude Code and startup interface, changes
no application code, but it is adopted only through a Broodling release that
bundles its record. A native or Claude Code change also
changes the [binding and its approval](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset),
and needs explicit Execution asset review and a Broodling release.

The approved asset runs only Claude Code. Codex and Copilot CLI are installed
but unapproved: they authorize nothing; check-target checks only that their
versions are the manifest's. The image sets `DISABLE_AUTOUPDATER=1` and `COPILOT_AUTO_UPDATE=false`,
but native 10.12.0 clears its environment before it starts an agent process, so
those values reach only processes that inherit the container's environment,
such as `docker exec`. Native sets `COPILOT_AUTO_UPDATE=false` for Copilot
itself; it sets no update control for Codex or Claude Code. The managed
settings reach Claude Code anyway: Claude Code's documentation says that
`CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` also turns off its automatic update.
No update host was contacted in the [agent traffic capture](#claude-code-agent-traffic).

Limits of the verification:

- The manifest is the producer's statement. Broodling verifies its bytes, its
  consistency with the binding, and, in `TargetEnvironmentTests`, the versions,
  executable digests and Debian packages it lists against the pulled image. It
  does not compare every file. The base's OS packages and tools are fixed only
  by the base digest; the manifest records no per-package checksum for them.
- `check-target` ties the container to the adopted digest through Docker's
  image metadata (`RepoDigests`) and trusts the Docker daemon. It checks live
  only the native, restic, entrypoint and managed settings bytes, the entries
  of `/etc/claude-code` with their type, owner and mode, the OS identity and the
  versions of the components the manifest states. It reads the managed settings
  as container root. It does not detect a change to other files of a running
  container.
- The managed settings do not stop a target repository's own
  `.claude/settings.json` `env` entries beyond the keys they pin, or its
  `apiKeyHelper` ([#249](https://github.com/faviann/broodling/issues/249)).
  Such a repository can set environment variables that send the gateway key
  elsewhere. Native also acts on pull-request feedback from anyone who can
  comment. Run Attempts only under the
  [live-run preconditions](#live-run-preconditions).
- The managed settings bound Claude Code's own requests, not the commands an
  agent runs. Every such command can read the gateway key. A nested
  `claude -p` from Bash reads the same settings but starts on the built-in
  default, `claude-opus-5`, and with `--model` it can reach another model; a
  `curl` to the gateway bypasses the settings altogether. The gateway key has
  no model allowlist, so this is an accepted residual tracked in
  [#249](https://github.com/faviann/broodling/issues/249), and the key's spend
  quota bounds its cost ([live-run preconditions](#live-run-preconditions)).
- Automatic compaction makes one full-context call on `claude-opus-5-5` at
  medium effort with 32000 output tokens.
- An image inventory or an approved environment catalog does not enforce R13's
  prohibition against arbitrary unprivileged downloads: execution agents can
  still fetch and run tools in their own run home.
  [Private control access](#private-control-access) (#187) settles target
  control only; whether the approved-tool policy forbids such downloads stays
  open after environment selection (#235), as do O4 (the package-read
  credential's node scope, #236) and O7 (management access and its trust
  assumption).

The entrypoint requires explicitly initialized state and a root-only bootstrap
key and serves only native's private mode: every control and OECP request needs
the bearer token that the operator's bootstrap installs in each new target
process ([private control access](#private-control-access)). The image contains
no control secret. Zeroshot materializes its own execution checkout, separate
from Broodling's source and Git custody; the target and `broodling` services
share only the read-only public root.

- Evidence: the [controlled stock DirectTarget witness](../tests/README.md#controlled-stock-directtarget-witness)
  runs that unmodified native as `zeroshot target serve` in the adopted image
  with the approved asset, in private mode with synthetic
  control material bootstrapped after every start. Its test-only layer replaces the
  Claude Code provider and the `git`/`gh` forge, so its PR receipt is controlled. It
  covers exact B1 after branch movement with the moved target integrated, a
  `ready` receipt, no client checkout, failure rather than fallback for an
  unavailable B1, same-run replay after a lost acknowledgement, restart
  retention and offline
  completion replay, and an agent reading frozen references from its submission
  with no Broodling reader. It is not production topology (homelab-iac#353), a
  real GitHub PR or provider quality evidence.
- Submit timing: native 10.12.0 acknowledges a run once recorded and checks out
  exact B1 afterwards, so a slow real fetch does not hold Broodling's fixed
  60-second submit budget and an unavailable B1 fails the correlated run. A send
  whose reply is lost stays unresolved until an exact replay, which converges on
  the same run.

### Entrypoint boundary

[`direct-target-entrypoint.sh`](direct-target-entrypoint.sh) is Broodling's
file, and Broodling owns its container interface: the paths, listener,
bootstrap key, commands and refusals in
[explicit initialization and guarded startup](#explicit-initialization-and-guarded-startup).
That interface's version is `zeroshot-environment.direct-target-startup/v1`,
defined here and as `TargetEnvironment.StartupInterface`. A change that an
existing operator procedure or target cannot follow needs a new version.

The entrypoint crosses into an environment image as a versioned interface with
pinned source. An environment release embeds the file from an exact Broodling
commit, verified by its SHA-256, and records that commit, the SHA-256 and the
interface version in its manifest. Broodling adopts an environment release by
its manifest digest. Neither side depends on the other's release, and neither
fetches either repository's moving `main` or `latest`.

Adoption requires the manifest's interface version to be the one this revision
supports, and `TargetEnvironmentTests` requires the embedded entrypoint to be
this revision's file. A Broodling entrypoint change is therefore adopted with
the environment release that carries it:

1. Commit the change in Broodling, with a new interface version if it is
   incompatible.
2. The producer publishes an environment release that embeds the file from that
   exact commit.
3. The same Broodling change adopts that release, and only then merges.

### Publication and release records

The [images workflow](../.github/workflows/images.yml) runs for every push. It
pulls the adopted target image by the digest in the adoption record and builds
the Broodling image. It runs the [image demonstration](../tests/README.md#image-demonstration)
on the two, `TargetEnvironmentTests` and the
[native-state transition check](../tests/README.md#native-state-transition-check)
on the target image, and `ApplicationSchemaFreezeTests`. It then requires a
decided Execution asset approval and publishes exactly that Broodling image to
GHCR. It builds and publishes no target image:
#234 retired the Broodling-built `broodling-target` image and its
`DirectTarget.Dockerfile` recipe.

- No push publishes while the [approval manifest](../src/Broodling/execution-assets/approval.json)'s
  `approval.decision` is not a public comment URL on `faviann/broodling`
  (`https://github.com/faviann/broodling/(issues|pull)/N#issuecomment-M`). The
  run then fails after its checks and publishes nothing, so an Execution asset
  change publishes only after its decision is recorded.
  `ExecutionAssetTests.ApprovalDecisionIsAPublicCommentOnTheRepository` states
  the same rule in the test suite.
- Otherwise a branch push publishes a `sha-<full commit>` candidate, so a pull
  request's head is published before merge.
- A `v*` tag publishes that tag name and attaches the release record to a
  GitHub release of the same name. Publish a version by pushing the tag only.
  Do not create the GitHub release first: that also creates the tag, and the
  workflow's release creation then fails after the images were pushed. A tag
  publishes nothing unless the Broodling image's application schema is frozen
  (see [application schema compatibility](#application-schema-compatibility)).
- Pull requests from forks only build, demonstrate and run the checks.

Published tags are never moved: a run refuses to publish over an existing tag,
and a re-run of a published commit therefore fails at publication. Select images
by digest. If publication fails after the image was pushed, a re-run refuses
too; that tag has no record and is not a release. Publish again from a new
commit or a new version tag.

Each publishing run uploads a `release-record` artifact, `release-record.json`
(kind `broodling.image-release/v4`). For a `sha-` candidate that workflow
artifact is the only copy, and it expires under the repository's artifact
retention; a `v*` version keeps its record on the GitHub release. The record
contains:

- the source revision, and the version or `sha-` tag;
- `images.broodling`, the published image, and `images.zeroshot`, the adopted
  target image, as `repository@sha256:DIGEST`, and `images["zeroshot-tls"]`,
  the pinned Caddy reference;
- `targetEnvironment`: the adoption record of that target image (environment
  release, producer release, manifest path and SHA-256, and image);
- `zeroshotTls`: the user, the Caddyfile to mount (path, SHA-256 and content)
  and `sites`, the per-origin site files the demonstration mounted in
  `/etc/caddy/sites` (path, SHA-256 and content). An installation renders its
  own site file for each catalog origin in the same form;
- `application`: the exact application schema the Broodling image initializes
  and opens, taken from its own `initialize-store` output: `storeFormat`,
  `storeSchemaVersion` and `storeDefinitionSha256`, the SHA-256 of the schema
  definition. `upgradesFrom` lists the identities, with the same three fields,
  that its `upgrade-store` upgrades; it is empty so far (see
  [application schema compatibility](#application-schema-compatibility));
- `executionAsset`: the approved asset SHA-256 and its native and Claude Code
  pins from the approval manifest;
- `nativeStateTransitions`: `to`, the adopted `images.zeroshot`, and `from`,
  the published target images listed in
  [`native-state-transitions.json`](native-state-transitions.json) at that
  revision, each with its source revision and native version and executable
  SHA-256 (see [native-state transitions](#native-state-transitions));
- `demonstration`: for each target `check-target` reported, its environment,
  adopted release, live OS and checked component versions.

A record states that these two images passed the demonstration together at that
revision, and that its target image passed the transition check from each listed
source. It is not a compatibility registry, and the two images need not share a
version. The `v1` records of earlier runs, whose `images.zeroshot` names a
Broodling-built `broodling-target` image, the `v2` records, whose
`demonstration` describes a single target, and the `v3` records, whose
`executionAsset` pins Codex, stay as published and describe only their own
revisions. It does not establish supported-profile readiness (#126). An operator
records a deployed target's image ID from `docker inspect` of the running
container in the readiness inventory.

The first publication created the `broodling` and `broodling-target` packages
linked to this repository and, like it, public: anonymous pulls by digest work.
Visibility is an operator setting in each package's settings; the workflow
never changes it. Keep `broodling` and the adopted environment's package
readable by the installation host. Published `broodling-target` images stay as
they are; nothing publishes there now.

### Native-state transitions

An update may change a target's image only by an established transition. A
release establishes exactly these:

- from each published target image listed in its revision's
  [`native-state-transitions.json`](native-state-transitions.json) to its
  record's `images.zeroshot`;
- keeping the deployed target image, which changes no native state.

Transitions are established only within one native release and executable,
the DirectTarget binding's, so a listed image must carry this revision's native
`zeroshot 10.12.0` with its executable SHA-256 (`7b3c74db…`). A source is a
previously adopted environment image, or another published image with that
native. The list is empty. `dotnet-r6` is the only adopted environment with
native 10.12.0, and its own manifest lists no `nativeStateTransitions.from`
either. #246 moved the binding to native 10.12.0, so `dotnet-r2` and the other
images with native 10.10.0 are not sources. Earlier, #215 replaced the initial native, asset, receipt and store definition, and
removed the two native 10.3.0 `sha-` candidates published from `main` since #121
(`sha-ae6fe2baa1beffe083683bddb6ce8cced5eecfc9`,
`@sha256:48055a75977cf85c95739abae4b6b9c348fd3f191509f6b8d596c55bbc854dc5`, and
`sha-47b4e4b86b1f6ca9af9a1dafae8f34ccc6e4980a`,
`@sha256:a17d56177d3794eaf19bf22f2040b5b41cac3faf28f52cf08dd43da79a5d645f`). No
transition from those images, to or from another native version, or from an
unlisted image is established; their state is not carried forward. #226 moved
the binding to native 10.10.0, so the target images published from `main` with
native 10.9.0 are not sources either. Adding a published image with the
binding's executable to the list is a reviewed change that the next workflow
run checks.

Before publishing, the images workflow runs the
[transition check](../tests/README.md#native-state-transition-check): a restart of
the adopted target image over native state it wrote itself, and an update from
every listed source to it; if one fails, nothing is published.
The host update procedure owns refusing any other selection.

Keeping the deployed target image needs no swap: the target container,
its mounts and native state stay as they are. The new record's demonstration
covers its own two images together, not that older target image with the new
Broodling image. `check-target` still applies after the update, with the
inventory's `imageId` unchanged, because it also inspects the replaced
`broodling` container.

To change a target's image, its catalog entry's `image`, the procedure needs:

- The deployed target image, selected by digest, to be a listed source.
- The installation pause, drained execution with its results captured, and
  the target stopped for the swap. The check covers finished runs and a
  submission that the source image recorded without acknowledging; a run still
  active at the swap may become `RuntimeLost`, as on any restart.
- The same host directories, unchanged, at `/state` and `/home/node`, and the
  same public root read-only at `/tls-root`.
- The same arguments without `initialize`: `--listen 0.0.0.0:18770
  --public-origin` with the recorded origin, and `--storage /state`. Ordinary
  startup of the new image refuses unrecognized state or a different origin;
  never initialize over existing state.
- Unchanged container root with Docker's default capabilities, the
  `zeroshot-<name>` alias, `zeroshot-tls` and its root, the entry's name,
  origin, container and mounts, and `directRootCertificate`. Retire Attempts
  prepared for the old image and replace them with
  `replace-attempt --update-environment`
  ([environment selection](../docs/implementation/zeroshot-native-integration.md#environment-selection)).
- Native UID ownership preserved: no recursive ownership change.
- Afterwards, `check-target` with that target's inventory `imageId` updated to
  the new image.

Rollback is not a reverse transition: restore the pre-update snapshot with the
prior image. Running a prior image over state that a newer image served is not
established.

#### Ending old execution and starting fresh state

Moving to a target image that is not a listed source, such as from a
`broodling-target` image to the adopted environment, is not an image swap over
existing state. No test qualifies carrying that state, so it is not a migration.
The separate operational path ends the old execution and starts fresh native
state:

1. Set the installation pause and let local initiation drain.
2. Let every dispatched Attempt finish with its result captured, or request its
   stop.
3. Stop the old target container and keep it stopped. Retire each dispatched
   Attempt on the verified `stopped_target` maintenance path
   ([lifecycle](../docs/implementation/dotnet-retirement-replacement.md)).
4. Keep the old target state and home directories unchanged. Nothing here
   authorizes deleting them.
5. Provision new empty state and home directories, initialize the adopted image
   over them ([explicit initialization](#explicit-initialization-and-guarded-startup)),
   start and bootstrap it, and run `check-target`. A catalog entry that keeps
   its name but names new mounts or another container is a new target: retained
   Attempts' stopped-target checks name the old ones.
6. Release the pause. Retired Attempts never reconnect. New work, and an explicit
   Replacement Attempt of an abandoned one, are fresh executions on the new
   target.

Nothing is deployed today, so no installation needs this path yet.

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
instructions to switch an existing installation. Keep state and source Git
directories separate from the release and source checkout.

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
`replace-attempt <path> <predecessor-attempt-id> <retry-key> <config.json> [--update-environment]`,
which prepares but never dispatches the successor; after `release-installation`, `resume` of its
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

A secret-free `config.json` is the operator's environment catalog: each approved
environment, the DirectTarget serving it and where its control token is kept
([several targets](#several-targets-235)):

```json
{
  "directRootCertificate": "/NEW/zeroshot-tls/root/root.crt",
  "defaultEnvironment": "dotnet",
  "environments": [
    {
      "name": "dotnet",
      "origin": "https://zeroshot-dotnet.dev.faviann.com",
      "controlTokenFile": "/NEW/secrets/broodling/zeroshot-dotnet-control-token",
      "image": "ghcr.io/faviann/zeroshot-environments/dotnet@sha256:ADOPTED_DIGEST",
      "container": "broodling-zeroshot-dotnet-1",
      "stateMount": "/NEW/targets/dotnet/state",
      "homeMount": "/NEW/targets/dotnet/home"
    }
  ]
}
```

It needs no Python executable, SDK client state, workspace root or launcher. Any
other member, a repeated member, an inline token or another credential field
refuses, and so does a catalog that breaks the
[catalog rules](../docs/implementation/invocation.md#configuration): names are
lowercase DNS labels of at most 54 characters; names, origins and containers
are unique; token files and mounts are normalized absolute paths, and no
state, home or token paths overlap; `image` is exactly one adopted image bundled
with the release; and `defaultEnvironment`, when set, names an entry. Without a
default, a repository without an environment declaration is refused. Each
origin follows the native and SDK rule: canonical HTTPS, or HTTP to exactly
`127.0.0.1` or `[::1]` such as `http://127.0.0.1:18770`, spelled as scheme and
authority only, with a default port omitted and never port 0. `container`,
`stateMount` and `homeMount` are the target's container name and host
directories, which each Attempt retains and every stopped-target check must
name. An existing Attempt continues only through its retained environment and
target
([environment selection](../docs/implementation/zeroshot-native-integration.md#environment-selection)).

`controlTokenFile` is the required absolute path of the file holding that
target's private control token, as the process reading the configuration sees
it (inside the `broodling` container, for example
`/run/secrets/zeroshot-dotnet-control-token`). Each operation that contacts the
target reads it again and sends it only to that entry's origin; it is never retained,
printed or logged. A missing or unreadable file fails only operations that
contact the target, as `credentials_unavailable`, before anything is sent, and
never prevents startup or retained reads. See
[private control access](#private-control-access).

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

DirectTarget connections use .NET's default proxy behaviour. Broodling's
container, and any host running `check-target` or the CLI against the target,
must not define proxy variables (`HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY` or
their lowercase forms): a proxy would receive the connections, including
dispatch credentials. This is a deployment assumption that Broodling does not
check.

### Processing server

The server accepts, resumes and stops exact work and processes accepted work
unattended when it is configured with both:

- `Broodling:Invocation` (`Broodling__Invocation`): the path of the
  [`config.json`](#invocation-configuration), the environment catalog. Each
  new Attempt goes to the target its environment resolution selects, each
  entry's control token file is read for every operation on that entry's origin,
  and its root certificate is trusted for every target connection. An invalid
  catalog refuses startup before listening.
- `Broodling:RepositoryRoot` (`Broodling__RepositoryRoot`): an existing absolute,
  durable directory writable by the service, where it keeps the service-owned
  bare repositories that hold each submission's B1 and accepted commits. It is
  durable operating state: keep it at its path with the store.

Its process environment must hold `GH_TOKEN`, `GATEWAY_API_KEY` and exactly
`GATEWAY_BASE_URL=https://gateway.ai.faviann.com`. They are read for each
operation and never retained or echoed; in Compose, supply them only to
`broodling` through its own protected env file. The control token is separate:
a file named by the configuration, not an environment variable, and never a
dispatch credential. Startup refuses, with a safe
code and before listening, one setting without the other, a missing
repository root or missing or invalid credentials. The
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
| `POST /submissions` with `{"issueUrl": "…"}` | `SubmitIssue` | `202` with `{submission}` and `Location: /submissions/{id}` once the submission is committed, with no GitHub, model or target contact. Repeating an issue URL returns its latest existing submission. `400 invalid_work_reference` before anything is written; `400 invalid_request` for any other member, so callers cannot select an environment, image or target. |
| `POST /submissions/{id}/revisions` | [`ReviseIssueSubmission`](../docs/implementation/invocation.md#revised-work) | `202` with `{submission}` and `Location: /submissions/{id}` of the successor once it is committed, and the same successor on replay. `409 issue_submission_conflict` while the predecessor is unfinished or not the latest, an Attempt is current, or a dispatched Attempt is not yet retired under verified maintenance. An `unchanged` successor's read carries its explanation and the linked submission and Contract. |
| `POST /submissions/{id}/resume` | [`SubmissionProgressor.Resume`](../docs/implementation/invocation.md#automatic-progression) | `202` with `Location` when that unfinished submission is continued at the next scan, forgetting an in-process stop or wait. `200` with `resumed: false` when its end or native correlation is retained. Never a Replacement Attempt. |
| `POST /submissions/{id}/stop` with `{"reason": "…"}` | `CancelIssueSubmissionAsync` | `200` with `{submission, attempt, error}` once the cancellation is committed: `attempt` is the stop report of the Attempt it abandoned, if any. `409` for completed work. |
| `POST /attempts/{id}/stop` with `{"reason": "…"}` | `StopAsync` | The `stop` command's report: `200` once abandonment is committed, with `error` naming what ended the native stop (such as `cessation_unconfirmed`, a transport timeout, or `caller_detached` at shutdown); `409` when nothing was abandoned. |

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
[ADR 0002](../docs/adr/0002-one-directtarget-per-environment-behind-one-tls-proxy.md)
installation's configuration and dependencies, every catalog target at once,
without an installer. Run it on the Docker host as the account with Docker
access:

```bash
dotnet /RELEASE/host/Broodling.Host.dll check-target /NEW/target-inventory.json /NEW/config.json
```

`config.json` is the [environment catalog](#invocation-configuration), with
the host paths of `root.crt` and of the operator's copies of the control
tokens. It supplies each target's origin, container, adopted image and mounts.
`target-inventory.json` records the **existing stack's** other host facts: the
Compose project network, the `zeroshot-tls` and `broodling` containers, the
canonical root mount paths and, for exactly each catalog environment, the
running image ID and the host bootstrap key file:

```json
{
  "network": "broodling_default",
  "tlsContainerName": "broodling-zeroshot-tls-1",
  "rootKeyMount": "/NEW/zeroshot-tls/key",
  "rootCertificateMount": "/NEW/zeroshot-tls/root",
  "broodlingContainerName": "broodling-broodling-1",
  "targets": [
    {
      "environment": "dotnet",
      "imageId": "sha256:EXACT_EXISTING_IMAGE_ID",
      "bootstrapKeyFile": "/NEW/secrets/zeroshot-dotnet/zeroshot-bootstrap-key"
    }
  ]
}
```

The [readiness reference](../docs/implementation/dotnet-target-readiness.md)
lists every check. For each target: its image as the catalog's adopted image
by digest on `linux/amd64`; running/root/isolation/restart settings, exact
mounts (its catalog state and home, the public root, its bootstrap key, beside
the image's declared, unused `/var/lib/zeroshot` volume), no published port,
launch arguments with its origin and its `zeroshot-<name>` alias on the project
network; credential exclusion; the live OS identity and every component version
the manifest states, and the native, `restic` and entrypoint bytes;
`gh api graphql --paginate --slurp` and the hosted UID/GID transition. Across
targets: no shared storage or bootstrap key, no shared token value, and exactly
one container answering each target alias and each origin alias. Then
`zeroshot-tls`'s pinned image, non-root user, `NET_BIND_SERVICE`-only
capabilities, 443 publication, every origin alias and separate root mounts; a
read-only public root mount and no key mount in `broodling`; and, per origin
through `zeroshot-tls` using the configuration's root, native's private-mode
discovery, an unauthenticated control request refused and a control request
with that target's token accepted. The configuration's `directRootCertificate`
must be `root.crt` in the recorded `rootCertificateMount`, and every token file
must be readable by the account running the check. A target started but not yet
bootstrapped, or bootstrapped with another token, is not ready, and one target
not ready makes the installation not ready. It creates no target/state, never
bootstraps and dispatches zero provider tasks.

The adopted manifest is the producer's statement; the live checks are what
readiness verifies. Broodling has no Docker access and cannot run them itself,
so the operator's order is what keeps agents off an unchecked target: set the
installation pause, start or swap the targets, run `bootstrap-target` for each
environment, run `check-target`, and only then release the pause. Pinned
native ends runs interrupted by a restart, and no dispatch passes the pause, so
no agent starts on a target before its check.

The [adopted environment](#adopted-target-environment) supplies each target
image: `dotnet-r6` carries native **10.12.0**, Claude Code **2.1.276** with its
managed settings, and gh
**2.101.0**. Target provisioning is operator-owned and
separately authorized; nothing here deploys it.

The supported topology is ADR 0002's single Compose project on rootful Docker on
one trusted host:

- Each `zeroshot-<name>` target runs its adopted image as container root with
  Docker's default capabilities. It has four bind mounts: its own state at
  `/state` and home at `/home/node` (by convention
  `<root>/targets/<name>/{state,home}`), the public root directory read-only at
  `/tls-root` and its own bootstrap key file read-only at
  `/run/secrets/zeroshot-bootstrap-key` (a Compose file secret). Native listens
  on the project network at the fixed inner port **18770**, which is never
  published, and the target carries the alias `zeroshot-<name>`.
- `zeroshot-tls` runs
  `caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b`
  as the package-defined user **`10443:10443`**, with `cap_drop: [ALL]` and
  `cap_add: [NET_BIND_SERVICE]`. It mounts the root key directory read-only at
  `/tls-root-key`, the public root directory read-only at `/tls-root`,
  [`zeroshot-tls.Caddyfile`](zeroshot-tls.Caddyfile) read-only at
  `/etc/caddy/Caddyfile`, a directory of site files read-only at
  `/etc/caddy/sites`, and a named volume at `/data`. The Caddyfile holds the
  global block and `import /etc/caddy/sites/*.caddy`; the installation renders
  one site file per catalog origin:

  ```caddyfile
  zeroshot-dotnet.dev.faviann.com {
  	tls internal
  	reverse_proxy zeroshot-dotnet:18770
  }
  ```

  It listens on 443, publishes that port on the LXC and carries each origin's
  host name as an alias on the project network.
- `broodling` mounts the public root directory, read-only, and each target's
  control token file, and never the TLS key directory, Caddy's data or a
  bootstrap key.

Native control is authenticated (#187): Traefik and the published port still
admit callers to the origin as in ADR 0001, but every control and OECP request,
including from the target's own execution agents over loopback or the routed
origin, needs the installed control token; only discovery is public. Rootless or
altered UID mapping, and Docker-socket or extra credential/source mounts, are
outside this profile. Preserve native UID ownership; never recursively chown
used target storage.

### Explicit initialization and guarded startup

The target image entrypoint is `/usr/local/bin/broodling-target`. It keeps the
native paths `/state`, `/home/node` and `CODEX_HOME=/home/node/.codex` and the
listener `0.0.0.0:18770` fixed. `ZEROSHOT_CONFIG_DIR` and `XDG_CONFIG_HOME`
overrides are refused. Select the adopted image by the digest in its
[adoption record](#adopted-target-environment), shown below as `TARGET_IMAGE`.

For an authorized **new installation only**, first provision empty durable
host directories: each target's state and home, with explicit root ownership and
private permissions, and the root key and public root directories. Provision
each target's control material and its recipient copies as described in
[private control access](#private-control-access), and its site file. Then, in
this order, with the initialization and start steps once per target (shown for
`dotnet`):

1. **Create the TLS root once**, before `zeroshot-tls` first starts. The target
   image's one-off root helper runs as container root:

   ```bash
   docker run --rm --network none \
     --mount type=bind,src=/NEW/zeroshot-tls/key,dst=/tls-root-key \
     --mount type=bind,src=/NEW/zeroshot-tls/root,dst=/tls-root \
     TARGET_IMAGE initialize-tls
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
3. **Initialize native state**:

   ```bash
   docker compose run --rm --no-deps zeroshot-dotnet initialize \
     --listen 0.0.0.0:18770 --public-origin https://zeroshot-dotnet.dev.faviann.com --storage /state
   ```

   Initialization refuses nonempty state or home, including partially
   initialized state. It needs no network, `zeroshot-tls` or client. It
   briefly runs native in private mode on the one-off container's loopback,
   with a throwaway bootstrap key that is never used, so nothing can control
   it: first on scratch storage, to learn the canonical origin native serves,
   then on `/state`, where native creates its ledger without submitting work.
   It records the configured origin as the binding in
   `/home/node/.config/broodling/origin` and stops native. Success leaves no
   native server running. Failure leaves any partial durable state for
   inspection; it never deletes it or silently retries over it. To retry,
   deliberately clear the partial state and home first: initialization refuses
   nonempty state.
4. **Start the target** (`docker compose up -d zeroshot-dotnet`) with the same
   arguments without `initialize`, then **bootstrap its control token** with
   `bootstrap-target` ([private control access](#private-control-access)).
   Until then the target refuses all control. Then run `check-target`.

Pinned native, not the entrypoint, decides which public origins are valid and
their canonical spelling: initialization refuses, before writing any state, an
origin that native rejects or serves under another spelling. Configure exactly
the catalog entry's origin. `target serve` provides no TLS; `zeroshot-tls`
terminates it, and the target's site file names that host. Changing the origin
later requires a stopped-target transition of native state.

Ordinary startup uses the same arguments **without `initialize`**, the same
mounts and the recorded origin. Before executing `zeroshot target serve`, the
entrypoint requires unredirected `/state`, `/state/runs`, `/state/runs.sqlite3`
and origin binding, a ledger that already contains native's `v2_runs` and
`v2_run_events` tables (opened read-only with the image's Python `sqlite3`), and
a binding equal to the configured origin. Native
creates its tables in any SQLite file it opens, so an unrelated database must be
refused before serving. Native owns the table shapes and rows.

It then requires the bootstrap key at `/run/secrets/zeroshot-bootstrap-key`: a
regular file, not a symbolic link, owned by root with no group or other access.
It copies the key to a new private file, `/run/broodling-target/bootstrap-key`
(root, mode `0600`, in a mode-`0700` directory), and executes
`zeroshot target serve … --bootstrap-key-file /run/broodling-target/bootstrap-key`.
Native reads and unlinks that copy before it listens, and refuses to start
(`private bootstrap key file is unavailable`) unless it is private and holds
exactly 64 lowercase hexadecimal characters with no newline. A missing,
unprotected or malformed key therefore stops startup with a refusal; there is no
unauthenticated fallback.
Missing, foreign or redirected state refuses without creating replacement files.
Restore missing state; do not initialize an empty replacement at an existing
origin. Existing targets without this binding, including targets bound to a
loopback origin, require a separately reviewed stopped-target transition; this
command does not adopt them. Readiness rejects the former unguarded native
entrypoint.

For operator diagnosis, use Broodling's authenticated commands, not native's
client, which has no private-mode access: `check-target` reports whether the
stack is ready and the running target accepts the configured token;
`bootstrap-target` reports whether a token is installed (rerunning it sends
nothing once the target accepts the token); and the retained-fact commands
`status`, `history` and the reader's `/attempts/{id}` show each Attempt with one
bounded native observation, whose `reason` is `unauthorized` when the target
refuses the token and `credentials_unavailable` when none is configured for its
origin.

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

Broodling rereads the root for each new connection, so it needs no restart, and
the target keeps its installed control token. If step 3 is skipped, or Caddy's
data is restored without its matching root, Caddy keeps serving an intermediate
signed by the old root. Clients trusting the new root then refuse the
connection, and `check-target` reports discovery as unavailable.

Readiness is a point-in-time dependency/configuration check. Before an authorized
dispatch, the operator must separately establish actual-target gateway
authentication/model availability, repository permissions and remote B1
availability. No fallback provider/runtime is supported. Readiness proves none
of those, nor PR delivery, semantic quality or physical cessation.

## Private control access

Since #187 only a holder of a target's current control token can submit to,
observe or control its native execution directly. Broodling and the operator's
Broodling commands hold it; the target's execution agents cannot read it, mint
it or use native's control routes through loopback or the routed origin.

Each target has one random bearer token with no expiry, refresh or scheduled
rotation, and a distinct bootstrap key; the operator bootstraps the token into
every new target process and rotates it only as explicit maintenance.

This does not yet stop an agent from acting through Broodling. The agents share
the project network with `broodling`, whose port 8080 serves, with processing
configuration, the unauthenticated mutating routes (`POST /submissions`,
revisions, resume and stops). Runs need nothing from Broodling, since each
submission carries its references (#241), but an agent can still call those
routes, and Broodling then acts with its own token and authority. #240 adds
authorization to those routes after the MVP; it does not block the first
deployment. Until it lands, strict agent isolation is not established.

### Control material and recipients

Generate both values with `openssl rand -hex 32 | tr -d '\n'`: exactly 64
lowercase hexadecimal characters, no newline. Keep them in the homelab secret
system, separate from the GitHub and gateway dispatch credentials and from
package-read credentials, and render one file per recipient with that
recipient's owner and mode. Compose file secrets are bind mounts that keep the
host file's owner and mode.

| Material | Recipient and path | Owner and mode |
| --- | --- | --- |
| Control token, one per target | `broodling` service: `/run/secrets/zeroshot-<name>-control-token`, named by its catalog entry's `controlTokenFile` | `1654:1654`, `0400` |
| Same control token | Operator account running `check-target` on the Docker host: its own file, named by its host configuration | that account, `0400` |
| Bootstrap key, one per target, different from the token | That `zeroshot-<name>` service: `/run/secrets/zeroshot-bootstrap-key` | `0:0`, `0400` |
| Same bootstrap key | One-off `bootstrap-target` container only: its own file | `1654:1654`, `0400` |

No two targets share a token value or a bootstrap key file; `check-target`
refuses either.

```yaml
services:
  broodling:
    secrets: [zeroshot-dotnet-control-token]
  zeroshot-dotnet:
    secrets:
      - { source: zeroshot-dotnet-bootstrap-key, target: zeroshot-bootstrap-key }
secrets:
  zeroshot-dotnet-control-token: { file: /NEW/secrets/broodling/zeroshot-dotnet-control-token }
  zeroshot-dotnet-bootstrap-key: { file: /NEW/secrets/zeroshot-dotnet/zeroshot-bootstrap-key }
```

The target refuses to start unless its key is a root-owned regular file with no
group or other access. Neither value belongs in an image, a Compose environment
variable, a command line, the invocation configuration, `zeroshot-tls`, the
long-running `broodling` (the bootstrap key) or the target (the token).
Broodling never writes either into its store, a Prepared submission, a
RequestBundle manifest, command output or a log: the token is read from its file
for each operation and sent only as the bearer to its own origin. Execution
agents run as native's isolated identities, which cannot read root-only files or
other processes' environment and memory, and have no capabilities to capture
traffic. Native unlinks its private copy of the key before serving and closes
its bootstrap after the first accepted envelope. Provisioning the secrets and
their routes in the homelab, and verifying that installation, belong to
homelab-iac#353 and #354.

### Bootstrap after every target start

Native keeps the token only in process memory, so every new target process,
whether the first start, an ordinary restart, an image update or a rotation,
refuses all control until it is bootstrapped. Run `bootstrap-target` for that
target's environment after it starts, as a one-off `broodling` container on the
project network that alone receives the target's bootstrap key:

```bash
docker compose run --rm --no-deps \
  -v /NEW/secrets/bootstrap/zeroshot-dotnet-bootstrap-key:/run/secrets/zeroshot-bootstrap-key:ro \
  broodling bootstrap-target /etc/broodling/invocation.json dotnet /run/secrets/zeroshot-bootstrap-key
```

It prints `{"bootstrapped":true,"environment":"dotnet","origin":…,"result":"installed"}`,
or `"already_installed"` when the running target already accepts the configured
token, in which case it sends nothing. An environment the catalog does not name
refuses. It requires private discovery, sends at
most one envelope through the SDK's `NativeClient.Private.BootstrapAsync`, and
confirms the result with an authenticated control request. It never restarts the
target, touches native state or replaces an installed token, and never resends
blindly. Its failures print `"bootstrapped":false` with a `code` and a fixed
`error` that names the next step, and exit 1. A key that differs from the
target's consumes nothing: correct the key copy and run it again.

Until the target is bootstrapped, the processing server's operations on it fail
as `unauthorized`: progression retries with its doubling delay and completion
observation at each scan, and observations report `unavailable` with that
reason. Retained reads are unaffected. Any automatic restart of the target, by a
restart policy, a host reboot or a supervisor, leaves the new process refusing
all control until `bootstrap-target` runs, so every automated restart must be
paired with the bootstrap step. Bootstrap promptly after a start, and
before releasing a maintenance pause. A restart still interrupts native
execution; nothing is resumed, replaced or replayed automatically.

### Restart and rotation

An ordinary restart keeps the token: start the target, then bootstrap it.
Rotation is explicit operator maintenance, never scheduled:

1. `pause-installation`, then wait until `status` reports dispatch initiation
   drained. Active native runs are interrupted by the restart: apply the usual
   host containment and stopped-target rules to them.
2. Stop the target (`docker compose stop zeroshot-dotnet`).
3. Render a new random token into every token copy: `broodling`'s and the
   operator's. To replace the bootstrap key too, render it to the target's and
   the bootstrap operation's copies now.
4. Start the target and run `bootstrap-target`.
5. Run `check-target`. The former token is refused and the new one accepted.
6. `release-installation`.

Broodling reads the token file for each operation, so its processes need no
restart. The target's origin, native ledger, Attempt and run correlation and
retained results are unchanged, and an unresolved dispatch replays exactly its
frozen request with the new token. If a token or bootstrap key may have leaked,
replace both: native accepts a captured bootstrap envelope again in any new
process started with the same key, so it could reinstall the former token
before `bootstrap-target` runs, which then reports that the target holds
another token.

### Several targets (#235)

[ADR 0002](../docs/adr/0002-one-directtarget-per-environment-behind-one-tls-proxy.md)
runs one DirectTarget per catalog environment, all behind the one
`zeroshot-tls` and root. Each environment name is a lowercase DNS label of at
most 54 characters; its target is the service and alias `zeroshot-<name>`, at
its own origin (by convention `https://zeroshot-<name>.dev.faviann.com`), with
its own site file, state, home, bootstrap key and control token. A stable name
such as `dotnet` is upgraded by swapping its image in drained maintenance
([native-state transitions](#native-state-transitions)); a release-specific
name such as `dotnet-r6` is a separate target for testing, removed once the
stable name is promoted. There is no load balancing, failover or on-demand
target creation.

- A repository selects an environment by exact name in
  `.broodling/environment.json` at its Attempt's original B1
  (`{"version": 1, "environment": "dotnet"}`); without one, the catalog's
  `defaultEnvironment` serves it. Nothing else in the repository, such as
  `global.json`, is interpreted
  ([environment selection](../docs/implementation/zeroshot-native-integration.md#environment-selection)).
  A per-invocation override is deferred.
- The resolution is retained once with the Prepared submission. Adding an
  environment or changing the default affects only new Attempts. An Attempt
  always connects to its retained target with the token configured for that
  origin, and a token configured for one origin is never sent to another.
- Removing an entry, changing its image, origin, container or mounts, or giving
  its origin to another name rebinds nothing; the
  [environment selection](../docs/implementation/zeroshot-native-integration.md#environment-selection)
  checks refuse instead. Keep an origin, its storage and its catalog entry
  while unfinished work retains them.
- A token change never amends a Contract, Prepared submission, Native
  correlation or retained target binding, and a refused or missing token never
  selects another target, starts a new Attempt or resets storage.
- Maintenance is whole-installation: one pause, every target stopped and
  verified, each Attempt's retirement given a check naming exactly its
  retained target, then bootstrap per target, `check-target` and release.

## Live-run preconditions

Broodling does not check these preconditions. The owner establishes them before
the first live Attempt and keeps them for every later one:

- **Owner-controlled repositories limited to collaborators.** Submit only issues
  from repositories the owner controls whose interactions are limited to
  collaborators. Native acts on pull-request feedback from anyone who can
  comment, and a target repository's own Claude Code settings `env` and
  `apiKeyHelper` still apply ([#249](https://github.com/faviann/broodling/issues/249)).
  GitHub interaction limits expire after at most six months; the limit on the
  canary, `faviann/broodling-canary`, expires on 2027-04-16. Renew the limit
  before it expires, or make the repository private.
- **A dedicated gateway key.** `GATEWAY_API_KEY` is a sub2api key that only
  Broodling uses. The owner configures it in sub2api with a spend quota and an
  IP whitelist of the Broodling host's and every target's egress addresses. It
  has no model allowlist. Every command an agent runs can read the key, so a
  nested `claude -p` or a direct request with the key can reach another model.
  The quota bounds what that costs, and the whitelist limits where a leaked key
  works.

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
which fails the run rather than substituting the branch tip.

Initial PR dispatch and acknowledgement replay require current `GH_TOKEN`,
`GATEWAY_API_KEY` and exactly
`GATEWAY_BASE_URL=https://gateway.ai.faviann.com` in the caller's
environment. Supply secrets through an appropriate secret source, never argv,
tracked configuration or a command's Docker environment; the
[processing server](#processing-server) takes them from its own protected
service environment. The fixed runtime is standard
`software-change`, Claude Code / `gateway` / `claude-opus-5-5` / medium.
`GATEWAY_API_KEY` is one Claude-group sub2api key: native gives the agents the
gateway URL and key as their Anthropic endpoint, and the bundled proposer uses
the same key.
The GitHub identity needs repository/source access and native PR delivery
permissions; broad credential privileges do not authorize broader effects.

```text
Broodling.Host submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch> <reviewed-issue.json> <producer>
Broodling.Host status <store> <contract-revision-id>
Broodling.Host history <store> <repository> <issue>
Broodling.Host resume <store> <contract-revision-id> [config.json [checkout [revision]]]
Broodling.Host wait <store> <attempt-id> [config.json]
Broodling.Host stop <store> <attempt-id> <reason> [config.json]
```

Here `Broodling.Host` abbreviates `dotnet /RELEASE/host/Broodling.Host.dll`.
`producer` normally is `caller`, and `submit` authorizes exactly one
`pull_request` effect to `<target-branch>` (`-` is refused as a usage error).
Retain JSON `revision.contractRevisionId`,
`attempts[].attemptId` and `submissions[].runId`. The `submit`, `resume` and
`stop` handback reports each submission only as its status facts (Attempt,
phase, intended and confirmed run IDs, replay block); `status` and
`history` show the complete retained record, including the frozen request. Source/canonical bytes are
base64; inspect the exact retained material. Errors can follow committed facts:
use history/status to find handles before choosing recovery.

Resume the same revision after interruption; repeating submit reacquires bytes.
Uncorrelated replay needs a catalog that still serves the Attempt's retained
environment exactly, and current credentials. Correlated resume and retained
status/history need neither. `wait` and `stop` take the same optional
`config.json` and use only its token files and root certificate: the record
connects to its retained target, and a configuration with no entry of the
retained environment name at the retained origin refuses before any target
contact or abandonment. For an HTTPS target
with a private root, pass the `config.json` to `wait` and `stop`. Without it,
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
retired predecessor with its retained environment, or with what the catalog now
names for that environment under `--update-environment`; only a
`stopped_target` predecessor requires the pause.

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
| Gateway | sub2api's OpenAI-compatible Chat Completions, `POST https://gateway.ai.faviann.com/v1/chat/completions` |
| Model | `claude-opus-5-5` |
| Credentials | Current `GATEWAY_API_KEY`, a Claude-group sub2api key, and exactly `GATEWAY_BASE_URL=https://gateway.ai.faviann.com`, read from the process environment for each proposal |
| Trust | The host's system TLS trust for the gateway |

The proposer's current request body was probed against the gateway on
2026-10-16 (#246) with a Claude-group key: `claude-opus-5-5`, `max_tokens`
12000, `reasoning_effort` `medium`, the shipped instructions and
`read_reference` tool, and the `json_object` response format. The tool-call
turn (`tool_choice: auto`) returned a `read_reference` call in 2.3 s with 63
completion tokens and 2,014 prompt tokens. With the tool result, the next
`auto` turn returned a JSON object with `criteria`, `obligations`,
`prerequisites` and `notes` (`stop`, 1,164 completion tokens, 10.6 s), and the
same turn under `tool_choice: none`, the last allowed call's shape, returned
the same keys (`stop`, 992 completion tokens, 9.4 s). The reply does not show
which effort the gateway applied upstream. Earlier probes, on 2026-10-15 and
2026-10-16, sent neither `max_tokens` nor `reasoning_effort`; the 2026-10-15
probe also ran Claude Code with `--json-schema` and a Read tool through the
gateway and received `structured_output`. Proposal quality on real issues is
still unverified.

<a id="claude-code-agent-traffic"></a>The agents' Claude Code traffic was
captured offline on 2026-10-17 UTC (#246) in the published `dotnet-r6` image
(`b10a7228…`, managed settings `a469a57a…`). Every case ran with
`--network none` and dummy keys. A fresh CA sat in the container's system
trust store, and no proxy variable or `NODE_EXTRA_CA_CERTS` was set. A DNS
server answered every name with a loopback address and logged the query. A
TLS terminator issued a certificate for any name and logged the SNI and every
request, plain HTTP was logged too, and a raw-socket logger recorded every TCP
SYN and UDP datagram on any loopback port. A scripted model served the
gateway name. The 21 cases were:

- **Hosted, approved asset.** Native 10.12.0's hosted lane in the image
  (`zeroshot target serve`, `zeroshot target add --direct`) ran the approved
  #246 asset from a local Git source. The harness removed deliver's `github`
  connection, so the runs declared only `gateway` and ended `delivery_failed`
  by design, with no push. In one run the worker called Bash, Edit and Write,
  wrote `.claude/settings.json` with a `CLAUDE_CODE_EXTRA_BODY` of effort
  `max`, 64000 tokens and `speed` `fast`, called `Agent` with model `haiku`,
  then checked the workspace; both reviewers then read that file. In another,
  every node called `Agent` with model `fable`. In a third, every node wrote
  that `CLAUDE_CODE_EXTRA_BODY` first.
- **Hosted, single worker.** A `single-worker` run with the runtime Claude
  Code / `gateway` / `claude-opus-5-5` / medium, which declared exactly one
  connection, `{"gateway": ["GATEWAY_API_KEY", "GATEWAY_BASE_URL"]}`, with no
  proxy or CA field. Its values were set at user scope with `zeroshot
  connection set gateway --json-stdin`. One run called Bash, Read, Edit and
  Write, WebFetch and WebSearch although neither was offered, the Explore,
  `fable` and `statusline-setup` agents, an agent asked to run in the
  background, `Workflow` and a skill, and compacted once. One called a
  background agent and planned an edit after the result, and one started a
  `Workflow` meant to outlive the result.
- **Direct.** Claude Code alone as `20000:10002` with native's hosted argv and
  exactly its environment names. Hostile repositories set, through
  `.claude/settings.json`, `CLAUDE_CODE_EXTRA_BODY` (effort `max` with 64000
  tokens, or `speed` `fast`), `CLAUDE_CODE_SUBAGENT_MODEL=claude-fable-5-1`,
  the advisor env and `advisorModel` `fable`, or `advisorModel` alone, or
  carried an agent with model `fable` and a skill with model `haiku`. The
  dotnet-r5 capture's hostile repository came along too: web tools allowed,
  `defaultMode` `bypassPermissions`, effort `max`, 128000 tokens,
  nonessential traffic on, and a repository agent and skill that use the web
  tools. Other direct cases wrote those settings mid-run, called a background
  agent, started a `Workflow` meant to outlive the result, asked `Agent` and
  `Workflow` for `fable`, `sonnet`, `haiku` and full model IDs, and ran Bash,
  Read, Edit and Write.

| Cases | Claude Code's requests | Model calls | Run result |
| --- | --- | --- | --- |
| Hosted, approved asset (3) | gateway: `HEAD /api/hello` 1 per node, `POST /v1/messages` 13, 12 and 9 | 34 | `delivery_failed` |
| Hosted, single worker (3) | gateway: `HEAD /api/hello` 1, `POST /v1/messages` 26 (one compaction), 4 and 2 | 32 | `succeeded` |
| Direct, hostile repository (7) | gateway: `HEAD /api/hello` 1, `POST /v1/messages` 9, 6, 6, 6, 6, 6 and 5 | 44 | one `result` each |
| Direct, other (8) | gateway: `HEAD /api/hello` 1, `POST /v1/messages` 6, 4, 2, 17, 12, 3, 3 and 6 | 53 | one `result` each |

Every one of the 163 model calls went to `claude-opus-5-5` at medium effort
with `max_tokens` 32000 and adaptive thinking. None carried a body key beyond
Claude Code's standard ones, so no extra body or `speed`, and none carried
the advisor tool or its beta or the 1M-context beta. No request followed a
session's structured result, counted by timestamp per session, subagents
included. In the background cases the late edit was never requested. No
model request offered WebFetch, WebSearch or `Workflow`; each forced call got
"No such tool available … disabled for this session, in subagents as well as
here", and the turn went on. Subagents that asked for another model ran on
`claude-opus-5-5`, and the stream output of the direct cases logged that the
model is restricted. Bash, Edit and Write ran as `20000`: the worker's checks printed
`bash-ran-uid-20000`, `write-ran` and the edited line, with ` M README.md`,
`?? BASH.txt` and `?? WRITE.txt`, and the `haiku` subagent's Bash ran too.

Claude Code resolved and reached no name but the gateway's, and only the
gateway received the key, as `x-api-key` on every model call; `HEAD
/api/hello` carried none. In the hosted cases the only other traffic was the
Git source (`git.example`, or `github.com` for the asset runs: 3 `GET
info/refs` and 5 `POST git-upload-pack` each, from the test client's clone
and native's fetch, with no key), DNS lookups of the container's own hostname
with no connection, and connections to native's own `target serve` on
loopback port 18770. Native started every Claude Code process as
`20000:10002` with only `ANTHROPIC_API_KEY`, `ANTHROPIC_BASE_URL`,
`GATEWAY_API_KEY`, `GATEWAY_BASE_URL`, `HOME`, `PATH`, `TMPDIR` and
`ZEROSHOT_TOOLS` (a wrapper's shell added `PWD`), with argv ending `--model
claude-opus-5-5 --json-schema … --effort medium
--dangerously-skip-permissions`. The only Claude Code diagnostic was
`unrecognized_model` for `claude-opus-5-5`.

The same harnesses on `dotnet-r5`, whose settings lack the r6 pins, sent
`fable`, `sonnet` and `haiku` subagent calls, the Haiku one with no effort,
sent every call at effort `max` with 64000 tokens once a repository or a
mid-run settings file set `CLAUDE_CODE_EXTRA_BODY`, offered the advisor tool,
and made calls after the structured result for a background agent. On
`dotnet-r4`, without the r5 pins, they recorded WebFetch's `domain_info`
request to `api.anthropic.com` and a high-effort summary call.

The capture used a scripted model, not the real gateway, and no real task has
run through native and the gateway against GitHub. It did not run a nested
`claude -p` from an agent's Bash; the #246 review measured that on the r6
settings: one call to `claude-opus-5` at medium with 32000 tokens and the
1M-context beta. The gateway key has no model allowlist, so that path, and any
other model it names, is an accepted residual tracked in
[#249](https://github.com/faviann/broodling/issues/249).

Supply the key through the same secret source as dispatch credentials.
Broodling's proposer sends it only to the gateway, as the bearer token, and the
agents' Claude Code sends it only to the gateway, as `x-api-key`. Native also
puts it in every agent's environment as `ANTHROPIC_API_KEY` and
`GATEWAY_API_KEY`, so every command an agent runs can read it, including a
repository's build and test code and its dependencies. The
[live-run preconditions](#live-run-preconditions) say which key to use. The
key is never written to the
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
B1 objects, native target state/home,
exact target image (changed only by an [established transition](#native-state-transitions))
and origin, release and operator inventory
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
