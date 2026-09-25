# Broodling tests

The supported suite is TUnit on .NET 10. The [current authority](../docs/governing/current.md)
governs scope; these checks establish Broodling-owned behavior and its public
SDK seam, not provider semantic quality or a validated live deployment.

## Run

Use Linux x86-64, a .NET 10 SDK (tested with 10.0.401), Git, `cc` and libc headers.
`global.json` selects the Microsoft.Testing.Platform runner, not an SDK version.
The administrative Git tests require an ordinary non-PID-1 host with waitable
children and no competing reaper. Install only the bridge's pinned SDK in a
dedicated Python 3.13+ environment:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r src/Broodling/bridge/requirements.txt
export BROODLING_TEST_PYTHON="$PWD/.venv/bin/python"
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export UseSharedCompilation=false
export NUGET_HTTP_CACHE_PATH="$PWD/tmp/nuget-http"
dotnet test --solution Broodling.sln
dotnet build Broodling.sln --configuration Release
```

`BROODLING_TEST_PYTHON` can point to an existing pinned SDK environment shared
across worktrees; its default is the repository's `.venv/bin/python`.
Missing SDK/native dependencies fail rather than skip.
The image startup tests also require rootful Docker access. They build the actual
`deployment/DirectTarget.Dockerfile` using the pinned SDK binary, so an uncached
build needs access to the pinned image/package sources. They also use the pinned
`zeroshot-tls` Caddy image; when it is absent, the run pulls it by digest and removes
it afterwards unless another concurrent run still uses it. Each run owns and removes
its uniquely tagged test images, its containers, networks and disposable volumes,
named with unique `broodling-186-` (ADR stack) or `broodling-180-` (stock target)
prefixes, and its host directories, unique `target-stack-*` and
`target-readiness-*` children of `~/.cache/broodling-tests`. The ADR stack tests (`TargetStack`)
give each case its own Docker network with the origin's alias; only `zeroshot-tls`
publishes, on a host-loopback port Docker chooses. Refusal cases use `--network none`.
The [stock target](fixtures/README.md#controlled-stock-directtarget)
serves on a bridge network because Docker cannot publish a port otherwise; it publishes
only on host loopback. Its fixtures' only outbound call is the reference read from
the test's reader, bound to the host's bridge gateway. No existing target is
accessed. No real credentials, networked provider or opt-in live campaign is required.

Durable Git/SQLite fixtures use unique owned children of
`~/.cache/broodling-tests`; set `BROODLING_TEST_WORKSPACE_ROOT` to another durable
root outside temporary paths if needed. Only disposable native state/sockets use
`/dev/shm`. Preserve other runs' directories and production state.

## Owning boundaries

| Boundary | Tests and detailed reference |
| --- | --- |
| Identity, exact source bytes, immutable admission, coherent observation | `IdentityTests`, `SourceCustodyTests`, `ContractIngressTests`, `ContractPolicyTests`, `AdmissionPersistenceTests`: [admission](../docs/implementation/dotnet-contract-admission.md) |
| Bundle-bound admission: request-only attribution, retained PR authority, preserved rejection findings, guarded decision, no re-proposal and refusal of an [earlier unbound association](Broodling.Tests/Fixtures/README.md#current-format-state-from-an-earlier-application) | `RequestAdmissionTests`: [bundle-bound admission](../docs/implementation/dotnet-contract-admission.md#bundle-bound-admission) |
| Bundled proposer over real capture, Git and SQLite with a controlled in-process gateway: request/manifest-only initial context, on-demand frozen reads, bundle-bound admission of typed output, retained final refusal of malformed or authority-changing output, preserved unsupported requirements, unretained operational failures with a later successful proposal, and no gateway key in retained state | `BundledProposerTests`: [bundled proposer](../docs/implementation/dotnet-contract-admission.md#bundled-proposer) |
| Exact-submission preparation over real capture, Git and SQLite with controlled GitHub and gateway peers: one owner per submission with independent submissions, a caller's cancellation ending only its own wait, a proposal interrupted after capture repeated against the frozen bundle, a Contract committed before its decision decided without proposing again, retained findings, cancellation or a retryable failure as results, and a non-retryable guard abort | `SubmissionPreparationTests`: [submission preparation](../docs/implementation/dotnet-contract-admission.md#submission-preparation) |
| Explicit fresh initialization, reopen and unchanged refusal of pre-transition state | `StoreLifecycleTests`, authentic [pre-transition .NET fixtures](Broodling.Tests/Fixtures/README.md): [state](../docs/implementation/dotnet-identity-custody.md) |
| Persisted installation pause, transition ordering and dispatch drain | `InstallationPauseTests`: [installation pause](../docs/implementation/dotnet-installation-pause.md) |
| Interrupted first capture, growing reference checkpoints, immutable RequestBundle completion and scoped source/Git reads | `RequestBundleTests`: [state](../docs/implementation/dotnet-identity-custody.md) |
| Original B1 custody, worktree and no-directory HTTP allocation, owned materialization and surviving Git children | `AttemptAdmissionTests`, `GitCustodyTests`, `WorktreeProvisioningTests`, `ProvisioningProcessTests`: [materialization](../docs/implementation/dotnet-worktree-materialization.md) |
| Controlled GitHub issue and service-owned repository acquisition; v1 request grammar, bounded reference closure and retained capture refusals | `GitHubAdmissionTests`, `RepositoryPreparationTests`, `RequestCaptureTests`, [retained issue fixtures](fixtures/ingress/README.md): [ingress](../docs/implementation/dotnet-github-ingress.md) |
| LocalTarget bridge: frozen dispatch, caller death, launcher policy, released SDK/native transport and refusal of a direct locator | `NativeDispatchTests`, `DispatchProcessTests`, `NativePolicyTests`, `NativeTransportTests`: [dispatch](../docs/implementation/dotnet-native-dispatch.md) |
| Offline HTTP preparation, retained asset/request reopen, a bundle-bound task's compact manifest without reference bodies, corrupt-content refusal and HTTP submission SQL guards | `HttpSubmissionTests`: [HTTP preparation](../docs/implementation/zeroshot-native-integration.md#http-submission-preparation) |
| HTTP send gates, intent before discovery, exact acknowledgement, retained conflict, concurrent replies, late acknowledgement stop, killed HTTP callers, a buffered request accepted after caller death and exact replay of a bundle-bound request frozen before reference access | `HttpDispatchTests`, `DispatchProcessTests`: loopback stock-target stand-in, [HTTP dispatch](../docs/implementation/zeroshot-native-integration.md#http-dispatch-and-acknowledgement) |
| Approved execution asset: build-output inclusion, loader refusals, pinned-tool regeneration and native admission | `ExecutionAssetTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset) |
| Bounded native progress observation, unavailable/timeout mapping and unchanged retained facts | `NativeObservationTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#dispatch-recovery-and-completion) |
| Receipt validation, atomic exact-Attempt completion, late results (correlated HTTP Attempts over the loopback stand-in) | `AttemptCompletionTests`, `CompletionPersistenceTests`: [completion](../docs/implementation/dotnet-receipt-completion.md) |
| Automatic completion without a reader: startup/running discovery that never dispatches, per-scan retry of temporary failures, retained receipt and accepted-pin refusal, detachment after authority loss, shutdown and restart, no rediscovery of retained results | `CompletionObserverTests`: [automatic observation](../docs/implementation/dotnet-receipt-completion.md#automatic-completion-observation) |
| Stop/quarantine, safe undispatched retirement (including HTTP Attempts), verified stopped-target maintenance retirement, original-B1 replacement | `RetirementTests`, `RetirementProcessTests`, `ReplacementTests`, `ReplacementCompletionTests`: [lifecycle](../docs/implementation/dotnet-retirement-replacement.md) |
| Read-only HTTP server: existing-state startup refusal and session release, retained reads mapped to application operations without external services or writes, reads while another session holds the writer | `HttpReadTests` |
| Composed application/operator recovery and handback; explicit Local/Direct target configuration, retained-kind routing and mismatch refusal; PR operations without a Python helper, over loopback HTTP and over HTTPS with a configured root | `InvocationTests`: [invocation](../docs/implementation/invocation.md) |
| TLS root created once with a private key and public certificate; `zeroshot-tls` refusing to start without its provided root; native initialization through the HTTPS origin, the fixed unpublished inner port, refusal before serving, restart and mixed UID preservation | `NativeTargetStartupTests`: actual target and pinned Caddy images with disposable state, [initialization](../deployment/README.md#explicit-initialization-and-guarded-startup) |
| Selected ADR stack configuration, dependency and discovery decisions; a stale Caddy intermediate after incomplete root rotation | `TargetReadinessTests`: controlled inspection and discovery, plus one actual-image stack, [readiness](../docs/implementation/dotnet-target-readiness.md) |
| Shared DirectTarget HTTP/WebSocket bounds, budgets and stock discovery I/O | `DirectTargetExchangeTests`: [transport limits](../docs/implementation/zeroshot-native-integration.md#directtarget-http-transport-limits) |
| HTTPS/WSS DirectTarget trust in exactly the configured private root, refusal of another root, system trust or a mismatched host name, root re-read per TLS connection within one operation, a missing root failing only its operation with no dispatch intent, and HTTPS completion wait | `DirectTargetTrustTests`: the loopback stand-in serving TLS from an in-process private authority, [transport limits](../docs/implementation/zeroshot-native-integration.md#directtarget-http-transport-limits) |
| DirectTarget session setup, JSON-RPC envelope and run status projection validation | `DirectTargetSessionTests`: loopback stock-target stand-in, [status reader](../docs/implementation/zeroshot-native-integration.md#directtarget-run-status-reader) |
| DirectTarget wait polling cadence, per-read deadlines and cancellation; stop precheck, single force and shared deadline | `DirectTargetRunTests`: the same loopback stand-in with a controlled clock, [status reader](../docs/implementation/zeroshot-native-integration.md#directtarget-run-status-reader) |
| Unmodified native HTTP/OECP boundary with the approved asset, through the application: controlled PR delivery from exact B1 without a client checkout, receipt consumption, restart and offline replay, unavailable-B1 failure and same-run replay, and on-demand frozen-reference reads through the installed helper | `StockDirectTargetTests`: [witness](#controlled-stock-directtarget-witness) |

`Broodling.ProcessWitness` is a test-only caller for real process-death and
Git-lock boundaries. Ordinary build/test/publish copies the C administrative
shim. The separate C# Codex launcher builds self-contained for Linux x64 using
the pinned .NET 10.0.12 runtime.

The retained [controlled Codex provider](fixtures/README.md) replaces only the
provider; the released SDK and bundled native engine run. The files in
[Fixtures](Broodling.Tests/Fixtures/README.md) control a bridge submit response or
provider inspection. None implements another Broodling application or authority
store. Readiness, exchange, session, run, completion and invocation tests control
Docker/HTTP boundaries or use loopback peers and contact no real target; the one
readiness case on actual images inspects only its own disposable stack.

## Controlled stock DirectTarget witness

`StockDirectTargetTests` runs the selected unmodified native: `zeroshot 10.3.0`
(source `054ad3fd6c763b98d12f5b2e90830b97116561ad`, Linux x86-64 executable
SHA-256 `afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06`) from
the pinned SDK 10.3.0.post1 wheel, as `zeroshot target serve` inside the actual
DirectTarget image, with the approved asset
`sha256:10f410b4a3ba06f69ead07b5d281d289fd6e378854bcb0600b1d963bdfce55d8`. Only
the [controlled provider and forge](fixtures/README.md#controlled-stock-directtarget)
are replaced. Each test uses fresh volumes, fake credentials and a new target.
The host-side application needs an origin it reaches without `zeroshot-tls`, so
the witness binds native to a literal-loopback origin. It records that binding
with native's own `target add` and `list`, because the entrypoint initializes only
through `zeroshot-tls`. It then serves through the unchanged entrypoint at the
fixed inner port, published on host loopback. The application alone submits,
observes and consumes:

- With the forge branch moved past B1, Invocation with a Direct target admits,
  prepares and correlates the HTTP Attempt without Python or a client checkout.
  The delivered commit descends from exact B1. Wait validates the receipt,
  fetches and pins the accepted commit from the forge and disposes atomically.
  The consumed receipt equals the target's terminal output after a target
  restart, and the retained completion replays with the target gone.
- An exact B1 missing from the forge leaves the first send unresolved. The stock
  target records the failed checkout but replies `503 target.unavailable`. An
  exact replay with rotated credentials correlates the same single run, whose
  failure abandons the Attempt with no delivery branch.
- A bundle-bound Attempt's controlled agent reads every reference listed in its
  task through the image's `broodling-reference` helper, using only the bundle
  and reference IDs. A real application reader over the retained store serves
  them. It binds only to the host's default-bridge gateway, which the target
  resolves as `broodling`; a mounted file replaces only the image's reader
  origin. The delivered commit carries the exact retained bytes. This proves the
  native execution environment's read, not Compose service-name networking
  (homelab-iac#353).

On the development host, the first Resume through acknowledgement took about
0.4 s with the checkout from the local forge, and about 2.5–2.9 s to the 503 for
an unavailable B1, including the target's checkout retries. These figures
describe a local forge only. The target acknowledges after its checkout, so a
slow real fetch can exceed the 60-second submit budget and leave the send
unresolved until exact replay.

The provider, forge and PR receipt are controlled. The run is not a real GitHub
PR, semantic-quality result, image publication or production topology check.

## Evidence limits and history

No-effect native success still refuses stable completion. Terminal labels do
not prove physical cessation; every dispatched Attempt remains quarantined
until verified maintenance retirement, whose actual target stop is host-owned.
The suite does not prove hostile sandbox containment, the trusted-host MCP
precondition, model reliability or authority for automatic merge/deployment.
[P5 remains scoped FAIL](../evaluation/p5/README.md).

The [baseline record](../docs/migration/130-baseline-validation.md),
[parity map](../docs/migration/130-parity-map.md) and
[passed migration review](../docs/migration/130-migration-review.md) distinguish
exact-baseline Python runs from later candidates and controlled .NET results.
The [retirement validation](../docs/migration/140-retirement.md) records the
post-retirement suite and local release smoke. Issue #151 is closed; its former
intermittent process-launch note is not a waiver for a current failure. The
current supported run and its environment are authoritative. Earlier unexplained
Git/provider and acquisition failures remain in the G and migration review records.

Python application tests, pytest tooling and Python schema fixtures are retired.
Their [frozen baseline](https://github.com/faviann/broodling/tree/b3f61a96c40401722ec16fc361958d1690982e02/tests)
remains historical evidence, not a second current acceptance gate.
