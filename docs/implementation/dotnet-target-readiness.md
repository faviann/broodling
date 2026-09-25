# .NET existing-target readiness

This callable .NET operation preserves the existing operator readiness boundary
from frozen [check_target.py](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/deployment/check_target.py)
and [its witnesses](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/tests/test_deployment_target.py).
The pre-cutover #130 repair and its dated validation are recorded below. The
[release guide](../../deployment/README.md#existing-target-readiness) supplies
current operator use; controlled validation is not a live .NET deployment claim.

## Operation and input

`TargetReadiness.CheckAsync(inventory, selectedDirectOrigin, cancellationToken)`
is callable without a store, application initialization, SDK bridge or HTTP host.
The selected origin is the same DirectOrigin used for invocation. The thin host
command reads the existing invocation configuration using its shared strict
parser and checks equality with the inventory before accessing the target:

```text
check-target <target-inventory.json> <config.json>
```

The new, small inventory shape records only the expected existing target:

```json
{
  "containerName": "broodling-target",
  "imageId": "sha256:EXACT_EXISTING_IMAGE_ID",
  "directOrigin": "http://127.0.0.1:18770",
  "stateMount": "/srv/broodling/target-state",
  "homeMount": "/srv/broodling/target-home"
}
```

Mount sources are canonical absolute host paths; their container destinations
are fixed to `/state` and `/home/node`. This records paths directly rather than
reproducing the retired Python installer's manifest or directory API. The operator
records the exact selected existing target. Readiness creates no directories or files.
The separate `config.json` is the existing
[invocation configuration](dotnet-native-dispatch.md#thin-operator-commands) for
PR work: `{"target": "direct", "directOrigin": ...}`. A LocalTarget
configuration, a field of the other kind, an unknown field or a secret is refused
in both files. Readiness shares the invocation's strict parser and canonical
loopback validation.

The origin must be exactly `http://127.0.0.1:<port>` with an explicit port from
1 through 65535, matching current operator invocation policy. No user info,
path, query, fragment, alternate scheme or noncanonical spelling is accepted.

Success returns JSON with `ready`, selected `containerName`, `containerId`,
`imageId`, `directOrigin`, checked `versions`, `apiPaginateSlurp`,
`hostedUidTransition`, and `providerTasks: 0`. Version facts are the checked pins,
not raw process output. Refusal returns `ready: false` and a fixed safe `error`
with exit 1. Usage exits 2; caller cancellation exits 130. No exception text,
process stdout/stderr, configuration dump or discovery response is printed.
The callable refusal is `TargetNotReady`; caller cancellation remains cancellation.

## Preserved checks and authority boundary

Docker inspect selects exactly one container by the recorded name and requires
the exact recorded image, running state, container root (`""`, `0`, `0:0`, or
`root`), ordinary isolation (not privileged or host networking), no dropped
capabilities, and restart policy `no`. It rejects installed `GH_TOKEN`,
`GITHUB_TOKEN`, `GATEWAY_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY` and
`CODEX_API_KEY`, including empty values. There must be exactly the two recorded
read/write bind mounts and one TCP port binding exclusively on the recorded
loopback port. Entrypoint is exactly `/usr/local/bin/broodling-target`; arguments must be
exactly `--listen 0.0.0.0:<inner-port> --public-origin <origin> --storage /state`.
The guarded entrypoint replaces the formerly accepted bare native serve command
for #104. Other checks retain the baseline scope. See the
[initialization/startup procedure](../../deployment/README.md#explicit-native-initialization-and-guarded-startup)
for the package-owned state checks; readiness itself initializes nothing.

Subsequent execs use the inspected container ID, never a newly selected name:

| Probe | Required observation |
| --- | --- |
| `/usr/local/bin/zeroshot --version` | `zeroshot 10.3.0` |
| `/usr/local/bin/codex --version` | `codex-cli 0.153.4` |
| `/usr/local/bin/node --version` | `v22.23.2` |
| `/usr/bin/gh --version` | First line begins `gh version 2.101.0 ` |
| `sha256sum /usr/local/bin/zeroshot` | `afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06` |
| `sha256sum /usr/bin/gh` | `ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff` |
| `/usr/bin/gh api graphql --paginate --slurp --help` | Declares the `--slurp` flag |
| Short Python exec | `setgroups([10002])`, `setgid(10002)`, `setuid(10002)` and assertions of the resulting UID/GID succeed |

Finally a GET of `<origin>/.well-known/zeroshot-native-v2` must return HTTP 200
and the exact stock discovery document: kind `zeroshot.native-v2-target/v2`,
authentication `none`, audience `controller` and the stock run, session and OECP
routes. `privateBootstrapPath`, `oauth` and `loginSession` may be absent or null,
and `extensions` absent or empty. Unknown or duplicate fields refuse. Discovery
uses the shared bounded exchange
([limits](zeroshot-native-integration.md#directtarget-http-transport-limits)):
redirects, proxying, cookies and ambient credentials are disabled, and the
response is byte-counted and parsed within one 10-second budget. Commands have a
30-second inspection timeout. Discovery expiry is a safe refusal. Caller
cancellation remains cancellation and ends only the selected Docker CLI process
or local HTTP exchange. It never stops the target or asserts cessation of an exec.

Readiness inspects existing state and performs the baseline's controlled short
UID-transition exec. It never creates, initializes, stops, restarts or replaces a
target, dispatches provider work, or grants cleanup/lifecycle authority. There is
no installer, Compose definition, HTTP endpoint, supervisor or persisted result
ledger. This is an observation at check time, not protection against later host
changes. Readiness proves neither credentials/model availability nor real PR
delivery, semantic quality, or physical cessation. P5 FAIL and independent
human/operator review of exact accepted revisions remain in force.

## Owning witnesses

`tests/Broodling.Tests/TargetReadinessTests.cs` owns controlled command and HTTP
boundaries. Its success witness asserts exact inspect/exec selection, the complete
UID probe, discovery URL/method, no authentication header, checked facts and
exclusion of a synthetic secret appended to otherwise valid gh output. Refusal
cases own image/state/root/isolation/capability/restart, entrypoint/argument,
mount, port, credential, version/hash/help, UID and stock-discovery schema checks,
plus a controlled-clock discovery stall that expires as refusal and cancels as
cancellation. Invalid
configuration and origin witnesses require zero target access. The small composed
command case uses a Direct configuration and creates no store.
The Program usage witness checks routing without Docker or HTTP access.

`DirectTargetExchangeTests` own the shared bounds over real loopback sockets:
chunked stock discovery, redirect refusal, header limit, truncated and oversized
chunked bodies, stall expiry versus caller cancellation, no exchange after expiry,
and fragmented, binary, closed and oversized WebSocket messages.

Only the process adapter's safe-error witness runs harmless local shell commands
and a missing executable. These readiness tests contact no Docker, real target, forge, gateway
or provider. Separate `NativeTargetStartupTests` exercise the actual target image
and its initialization/startup boundary using disposable volumes, no published
ports and no provider workload. These checks test Broodling's readiness decisions, not Docker's or
its dependencies' implementations. Existing invocation witnesses cover the
unchanged shared configuration policy. Full-suite controlled-native evidence
remains distinct from real DirectTarget delivery or deployment validation.

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
`DOTNET_CLI_USE_MSBUILD_SERVER=0`, `UseSharedCompilation=false`, and
`BROODLING_TEST_PYTHON=/home/faviann/repos/broodling/.venv/bin/python`.
Full-suite durable fixtures used the owned
`/home/faviann/.cache/broodling-tests/130-target-readiness` root; focused readiness
fixtures used unique owned directories under `.cache/broodling-tests`.
No failing runs were suppressed or retried. No real target, Docker installation,
live provider, deployment or evaluation was exercised. Fresh combined migration
review remains the root's gate before #140.
