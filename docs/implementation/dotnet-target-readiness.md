# .NET existing-target readiness

This callable .NET operation preserves the existing operator readiness boundary
from frozen [check_target.py](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/deployment/check_target.py)
and [its witnesses](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/tests/test_deployment_target.py).
Since #186 it checks the ADR 0001 stack behind the HTTPS origin instead of a
target published on loopback; the image, pin, credential-exclusion and UID
checks are unchanged. Since #187 the target serves only native's private mode,
and readiness also requires actual authenticated control with the configured
token. The pre-cutover #130 repair and its dated validation are
recorded below. The
[release guide](../../deployment/README.md#existing-target-readiness) supplies
current operator use; controlled validation is not a live .NET deployment claim.

## Operation and input

`TargetReadiness.CheckAsync(inventory, selectedDirectOrigin, access, cancellationToken)`
is callable without a store, application initialization or HTTP host.
The selected origin and the `DirectTargetAccess` (that origin's control token
file and the root certificate) are the ones used for invocation. The thin host
command reads the existing invocation configuration
using its shared strict parser and checks origin equality with the inventory
before accessing the target:

```text
check-target <target-inventory.json> <config.json>
```

Since #186 the inventory describes the existing
[ADR 0001](../adr/0001-directtarget-https-origin-and-compose-topology.md) stack:

```json
{
  "containerName": "broodling-zeroshot-1",
  "imageId": "sha256:EXACT_EXISTING_IMAGE_ID",
  "directOrigin": "https://zeroshot.dev.faviann.com",
  "stateMount": "/srv/broodling/target-state",
  "homeMount": "/srv/broodling/target-home",
  "network": "broodling_default",
  "tlsContainerName": "broodling-zeroshot-tls-1",
  "rootKeyMount": "/srv/broodling/zeroshot-tls/key",
  "rootCertificateMount": "/srv/broodling/zeroshot-tls/root",
  "broodlingContainerName": "broodling-broodling-1",
  "bootstrapKeyFile": "/srv/broodling/secrets/zeroshot/zeroshot-bootstrap-key"
}
```

`containerName` is the `zeroshot` target. The three container names must differ.
`network` is the Compose project network. Mount sources are canonical absolute
host paths, and the root key and certificate locations must not contain each
other. `bootstrapKeyFile` is the host file the target mounts as its bootstrap
key. Their container
destinations are fixed. The operator records the exact existing stack, and
readiness creates no directories or files. The separate `config.json` is the
existing [invocation configuration](zeroshot-native-integration.md#thin-operator-commands):
`{"directOrigin": ..., "directControlTokenFile": ..., "directRootCertificate": ...}`.
Its root must be exactly `root.crt` in the recorded `rootCertificateMount`, so
discovery trusts the root the stack mounts rather than system trust or another
copy, and its token file must be readable by the account running the check.
An unknown field or a secret value is refused in both files; the files name
secrets only by path.

The origin must be a canonical HTTPS origin with a DNS host name and the default
port, such as `https://zeroshot.dev.faviann.com`. In-project clients reach
`zeroshot-tls` through the alias on that port. No explicit port, user info,
path, query, fragment, IP literal, loopback HTTP origin or noncanonical spelling
is accepted. Targets bound to a loopback origin are not adopted.

Success returns JSON with `ready`, selected `containerName`, `containerId`,
`imageId`, `directOrigin`, checked `versions`, `apiPaginateSlurp`,
`hostedUidTransition` and `providerTasks: 0`.
Version facts are the checked pins, not raw process output. Refusal returns
`ready: false` and a fixed safe `error` with exit 1. Usage exits 2; caller
cancellation exits 130. No exception text, process stdout/stderr, configuration
dump, token or discovery or session response is printed.
The callable refusal is `TargetNotReady`; caller cancellation remains cancellation.

## Checks and authority boundary

One `docker inspect --type container` selects exactly the three recorded
containers.

The `zeroshot` target must have the exact recorded image, be running, run as
container root (`""`, `0`, `0:0`, or `root`), use ordinary isolation (not
privileged or host networking), drop no capabilities and use restart policy
`no`. It rejects installed `GH_TOKEN`, `GITHUB_TOKEN`, `GATEWAY_API_KEY`,
`OPENAI_API_KEY`, `ANTHROPIC_API_KEY` and `CODEX_API_KEY`, including empty
values. Its mounts must be exactly the recorded read/write state and home binds
at `/state` and `/home/node`, the public root directory bound read-only at
`/tls-root` and the recorded bootstrap key file bound read-only at
`/run/secrets/zeroshot-bootstrap-key`. It publishes no port, including through publish-all, and carries
the `zeroshot` alias on the recorded network, where `zeroshot-tls` forwards. Entrypoint is exactly `/usr/local/bin/broodling-target`;
arguments must be exactly
`--listen 0.0.0.0:18770 --public-origin <origin> --storage /state`, the fixed
inner port. See the
[initialization/startup procedure](../../deployment/README.md#explicit-initialization-and-guarded-startup)
for the package-owned state checks; readiness itself initializes nothing.

`zeroshot-tls` must have been created from exactly
`caddy:2.11.4-alpine@sha256:6aeddd44c3078b0f9a35206472a11420648a79c184603ef95957d0a20044cb2b`,
be running and run as the package-defined user `10443:10443`. It must use
ordinary isolation, drop `ALL` capabilities and add only `NET_BIND_SERVICE`,
publish only `443/tcp` and carry the origin's host name as an alias on the
recorded network. Its mounts at `/tls-root-key` and `/tls-root` must be exactly
the recorded key and public root directories, both read-only binds. No other
`zeroshot-tls` mount may equal, contain or lie within the public root directory,
so Caddy's data, with its intermediate key, stays out of what `broodling` and
`zeroshot` mount.

`broodling` must mount the public root directory, only as a read-only bind, and
no other mount of it may equal, contain or lie within any other `zeroshot-tls`
mount source: the root key directory or Caddy's data, which holds the
intermediate key. Neither `broodling` nor `zeroshot-tls` may mount the bootstrap
key file or a directory containing it: only the target and the one-off
bootstrap operation receive it.

Subsequent execs use the inspected target container ID, never a newly selected
name. The native version and the `zeroshot` and `restic` hashes are the
[DirectTarget binding](zeroshot-native-integration.md#pinned-dependencies);
the other pins are the DirectTarget image's own:

| Probe | Required observation |
| --- | --- |
| `/usr/local/bin/zeroshot --version` | `zeroshot 10.10.0` |
| `/usr/local/bin/codex --version` | `codex-cli 0.153.4` |
| `/usr/local/bin/node --version` | `v22.23.2` |
| `/usr/bin/gh --version` | First line begins `gh version 2.101.0 ` |
| `sha256sum /usr/local/bin/zeroshot` | `d0c84ffbafa731ef7fa6b61f87af9c000cc4e5b4d2e0d3b7df461fd239bb923e` |
| `sha256sum /usr/local/bin/restic` | `90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf`, the release archive's `restic` that native run allocation requires |
| `sha256sum /usr/bin/gh` | `ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff` |
| `/usr/bin/gh api graphql --paginate --slurp --help` | Declares the `--slurp` flag |
| Short Python exec | `setgroups([10002])`, `setgid(10002)`, `setuid(10002)` and assertions of the resulting UID/GID succeed |

The configuration's control token for the origin must then be readable (exactly
64 lowercase hexadecimal characters); otherwise readiness refuses before any
target request. A GET of `<origin>/.well-known/zeroshot-native-v2` must return
HTTP 200 and the exact stock private-mode discovery document: kind
`zeroshot.native-v2-target/v2`, authentication `private_capability`, audience
`controller`, the stock run, session and OECP routes and `privateBootstrapPath`
`/native-v2/private-bootstrap`. `oauth` and `loginSession` may be absent or null,
and `extensions` absent or an object, whose optional capabilities are ignored.
Unknown or duplicate fields refuse.

Public discovery alone never passes. Two OECP session requests follow, which
issue a session but submit or change nothing: without credentials it must be
refused with `401`, so a target that actually serves unauthenticated control
(native's direct mode, or anything advertising private discovery without
enforcing it) is not ready; with the configured token as bearer it must return
`200`. A `401` there means the running target holds no token or
another one: it has not been bootstrapped since it started, or was bootstrapped
with a former or different token. The refusal names `bootstrap-target`.

Discovery keeps the origin's host name for TLS but connects to `zeroshot-tls`'s
actual host publication, read from the inspection (a wildcard address is reached
on loopback), rather than wherever the name resolves on the host; on the LAN it
resolves to Traefik, which terminates TLS itself. It trusts exactly the
configuration's `directRootCertificate`, as invocation does, never system
trust. The chain Caddy serves must therefore reach the configured root, so
an intermediate that Caddy kept from before a rotation or restore fails
discovery. Discovery and both session requests use the SDK's HTTP handler with
that root as its trusted root, so redirects and cookies are disabled, and a
bounded read ([transport](zeroshot-native-integration.md#directtarget-transport-and-budgets)):
each response is byte-counted and parsed within one shared 10-second budget. Like every
DirectTarget connection it relies on the deployment assumption that no proxy
variables are defined. Commands have a
30-second inspection timeout. Discovery expiry is a safe refusal. Caller
cancellation remains cancellation and ends only the selected Docker CLI process
or local HTTP exchange. It never stops the target or asserts cessation of an exec.

Readiness inspects existing state and performs the baseline's controlled short
UID-transition exec. It never creates, initializes, stops, restarts or replaces a
target, dispatches provider work, or grants cleanup/lifecycle authority. There is
no installer, Compose definition, HTTP endpoint, supervisor or persisted result
ledger. It never bootstraps: installing the token is the separate
`bootstrap-target` operation. This is an observation at check time, not
protection against later host changes; a target restarted afterwards is not
ready again until bootstrapped. Readiness proves neither dispatch credentials/model availability nor real PR
delivery, semantic quality, or physical cessation. P5 FAIL and independent
human/operator review of exact accepted revisions remain in force.

## Owning witnesses

`tests/Broodling.Tests/TargetReadinessTests.cs` owns controlled command and HTTP
boundaries. Its success witness asserts the exact three-container inspection and
exec selection, the complete UID probe, discovery URL/method, no authentication
header on discovery, an unauthenticated then a token-bearing session request,
the published endpoint and trusted root handed to discovery, checked facts and
exclusion of the token and of a synthetic secret appended to otherwise valid gh output.
Refusal cases own every target, `zeroshot-tls` and `broodling` inspection check
(including the bootstrap key mount and its absence from other services),
inventory validity, credential, version/hash/help, UID and stock-discovery schema
checks, and private control: unauthenticated control accepted or answered otherwise than
`401`, and a refused or missing token, plus a
controlled-clock discovery stall that expires as refusal and
cancels as cancellation. Invalid configuration and origin witnesses require zero
target access, including a configuration with no root or another root. The small
composed command case uses an invocation configuration, passes its root and creates no
store. The Program usage witness checks routing
without Docker or HTTP access.

One case uses the actual images on host Docker (see `TargetStack`): the ADR stack
with a started but unbootstrapped target is not ready, and ready once
bootstrapped; after a rotation that replaced the root but kept Caddy's stored
intermediate, discovery and a control client both refuse; after the
documented TLS rotation, both succeed again without restarting the target.

`DirectTargetExchangeTests` own discovery's bounded read over real loopback
sockets: stock discovery without ambient credentials, truncated and oversized
chunked bodies, duplicate properties, stall expiry versus caller cancellation and
no exchange after expiry.

Only the process adapter's safe-error witness runs harmless local shell commands
and a missing executable. `NativeTargetStartupTests` exercise the actual target
and pinned Caddy images for the root helper, `zeroshot-tls`'s refusal without its
root, and initialization, private startup, bootstrap, restart and rotation behind
the origin, using disposable state, synthetic secrets and no provider workload.
These checks test Broodling's readiness decisions and package
configuration, not Docker's, Caddy's or native's implementations. Existing
invocation witnesses cover the unchanged shared configuration policy.
Full-suite controlled-native evidence remains distinct from real DirectTarget
delivery or deployment validation.

## Repair validation

On 22 September 2026, in `issue/130-target-readiness` based on
`d7239041d88d0bb05f704266fc40c4b21e9d16ba`:

- Focused `TargetReadinessTests`: **66 passed**, zero failed/skipped, **2.486s**
  reported test duration.
- `dotnet test --solution Broodling.sln`: **323 passed**, zero failed/skipped,
  **27.102s** reported test duration, including the existing invocation witnesses.
- `dotnet build Broodling.sln --configuration Release`: **0 warnings/errors**,
  **23.77s**.
- Unchanged Python reference, `python -m pytest tests`: **404 passed in 85.81s**.
  This is the current worktree reference run, not another exact-baseline claim.

.NET commands used `MSBUILDDISABLENODEREUSE=1`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0` and `UseSharedCompilation=false`.
Full-suite durable fixtures used the owned
`/home/faviann/.cache/broodling-tests/130-target-readiness` root; focused readiness
fixtures used unique owned directories under `.cache/broodling-tests`.
No failing runs were suppressed or retried. No real target, Docker installation,
live provider, deployment or evaluation was exercised. Fresh combined migration
review remains the root's gate before #140.
