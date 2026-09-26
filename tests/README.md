# Broodling tests

The supported suite is TUnit on .NET 10. The [current authority](../docs/governing/current.md)
governs scope; these checks establish Broodling-owned behavior and its public
SDK seam, not provider semantic quality or a validated live deployment.

## Run

Use Linux x86-64, a .NET 10 SDK (tested with 10.0.401), Git, `cc` and libc headers.
`global.json` selects the Microsoft.Testing.Platform runner, not an SDK version.
The administrative Git tests require an ordinary non-PID-1 host with waitable
children and no competing reaper. The host's system and global Git configuration
must add no checkout transformation, such as Git LFS filters, which Broodling's
source custody refuses. Install only the bridge's pinned SDK in a
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
`deployment/DirectTarget.Dockerfile`, which fetches the pinned SDK wheel for its
native binary, so an uncached build needs access to the pinned image/package sources.
`BROODLING_TEST_TARGET_IMAGE` names a prebuilt DirectTarget image to use instead;
the run keeps it. The [transition check](#native-state-transition-check) pulls the
listed published target images by digest from GHCR when absent. They also use the pinned
`zeroshot-tls` Caddy image. The run removes an image it pulled afterwards unless
another concurrent run still uses it. Each run owns and removes
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
| Exact-submission preparation over real capture, Git and SQLite with controlled GitHub and gateway peers: one owner per submission with independent submissions, a caller's cancellation ending only its own wait, a proposal interrupted after capture repeated against the frozen bundle, a Contract committed before its decision decided without proposing again, retained findings, cancellation or a retryable failure as results, a `gh` that cannot start needing attention without rejecting the submission, and the store-failure classification mapping | `IssueSubmissionPreparationTests`: [Issue submission preparation](../docs/implementation/dotnet-contract-admission.md#issue-submission-preparation) |
| Automatic progression over real capture, Git and SQLite with controlled GitHub, gateway and stock-target stand-in peers: startup and running discovery, continuation from retained B1 to correlation with no client workspace, a submission progressing past another blocked on its model call, a preparer lifetime ending before the service's token only detaching, reads and a writer available in the host process during a send, shutdown leaving the dispatch unresolved and the Attempt current, exact replay with rotated credentials after the pause is released, a doubling retry delay to the limit with the pause not counted, a temporary target failure retried with the same frozen request, a conflict stopping at once with its own stage's count while ended work is neither progressed nor replaced, an Attempt abandoned during its send leaving without a stop, a failing credential provider stopping for attention, and earlier unbound associations never discovered | `SubmissionProgressorTests`: [automatic progression](../docs/implementation/invocation.md#automatic-progression) |
| Explicit fresh initialization, reopen while another session writes and checkpoints, the exact schema identity reported to the release record, unchanged refusal of pre-transition state and of a released earlier identity with its documented reason | `StoreLifecycleTests`, authentic [pre-transition .NET fixtures](Broodling.Tests/Fixtures/README.md): [state](../docs/implementation/dotnet-identity-custody.md) |
| Frozen application schemas against this revision's code, also run by the images workflow on every push: a frozen definition never edited, the current version never below a frozen one, and every older frozen version with an `upgrade-store` disposition | `ApplicationSchemaFreezeTests`: [application schema compatibility](../deployment/README.md#application-schema-compatibility) |
| Persisted installation pause, transition ordering and dispatch drain | `InstallationPauseTests`: [installation pause](../docs/implementation/dotnet-installation-pause.md) |
| Interrupted first capture, growing reference checkpoints, immutable RequestBundle completion and scoped source/Git reads | `RequestBundleTests`: [state](../docs/implementation/dotnet-identity-custody.md) |
| Original B1 custody, worktree and no-directory HTTP allocation, owned materialization and surviving Git children | `AttemptAdmissionTests`, `GitCustodyTests`, `WorktreeProvisioningTests`, `ProvisioningProcessTests`: [materialization](../docs/implementation/dotnet-worktree-materialization.md) |
| Controlled GitHub issue and service-owned repository acquisition; v1 request grammar, bounded reference closure and retained capture refusals; a stalled metadata read ending retryable with its process killed | `GitHubAdmissionTests`, `RepositoryPreparationTests`, `RequestCaptureTests`, [retained issue fixtures](fixtures/ingress/README.md): [ingress](../docs/implementation/dotnet-github-ingress.md) |
| LocalTarget bridge: frozen dispatch, caller death, launcher policy, released SDK/native transport and refusal of a direct locator | `NativeDispatchTests`, `DispatchProcessTests`, `NativePolicyTests`, `NativeTransportTests`: [dispatch](../docs/implementation/dotnet-native-dispatch.md) |
| Offline HTTP preparation, retained asset/request reopen, a bundle-bound task's compact manifest without reference bodies, corrupt-content refusal and HTTP submission SQL guards | `HttpSubmissionTests`: [HTTP preparation](../docs/implementation/zeroshot-native-integration.md#http-submission-preparation) |
| HTTP send gates, intent before discovery, exact acknowledgement, retained conflict, concurrent replies, late acknowledgement stop, killed HTTP callers, a buffered request accepted after caller death and exact replay of a bundle-bound request frozen before reference access | `HttpDispatchTests`, `DispatchProcessTests`: loopback stock-target stand-in, [HTTP dispatch](../docs/implementation/zeroshot-native-integration.md#http-dispatch-and-acknowledgement) |
| Approved execution asset: build-output inclusion, loader refusals, pinned-tool regeneration and native admission | `ExecutionAssetTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset) |
| Bounded native progress observation, unavailable/timeout mapping and unchanged retained facts | `NativeObservationTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#dispatch-recovery-and-completion) |
| Receipt validation, atomic exact-Attempt completion, late results (correlated HTTP Attempts over the loopback stand-in) | `AttemptCompletionTests`, `CompletionPersistenceTests`: [completion](../docs/implementation/dotnet-receipt-completion.md) |
| Automatic completion without a reader: startup/running discovery that never dispatches, per-scan retry of temporary failures, retained receipt and accepted-pin refusal, detachment after authority loss, shutdown and restart, no rediscovery of retained results | `CompletionObserverTests`: [automatic observation](../docs/implementation/dotnet-receipt-completion.md#automatic-completion-observation) |
| Stop/quarantine, safe undispatched retirement (including HTTP Attempts), verified stopped-target maintenance retirement, original-B1 replacement, including after that maintenance retirement | `RetirementTests`, `RetirementProcessTests`, `ReplacementTests`, `ReplacementCompletionTests`: [lifecycle](../docs/implementation/dotnet-retirement-replacement.md) |
| Predecessor-linked revisions over real capture, Git and SQLite with controlled GitHub and gateway peers and the stock-target stand-in: refusal of active work, a current Attempt and unretired dispatched work; concurrent requests converging on one successor, exact replay after later history and ordinary submission creating nothing; material without a committed Contract seeking admission; an unchanged successor ending with its retained link without proposal or execution while a moved starting commit proceeds; an ordinary first Attempt after maintenance retirement with predecessor stop replay unable to reach it; and two revisions retaining each result, with the later revision's failed Attempt still replaceable | `RevisionTests`: [revised work](../docs/implementation/invocation.md#revised-work) |
| HTTP reader: existing-state startup refusal and session release, retained reads mapped to application operations without external services or writes, reads while another session holds the writer | `HttpReadTests` |
| Processing server over real capture, Git and SQLite with controlled GitHub and gateway peers and the stock-target stand-in, through the host's own composition: acknowledgement with handle and `Location` after durable acceptance and before acquisition, refusal of an unsupported reference before any write, processing and result retention with no client connected, a stopped submission's visible reason and exact resume, retained facts apart from available and unavailable native observations, shutdown detaching in-flight dispatch; stops with a required reason that outlive a disconnected caller and report abandonment apart from cessation, submission cancellation and hand-back; a revision accepted once with its `Location` and replay, refused for active work, and its unchanged end readable; startup refusal of configurations that cannot process and a reader refusing submission; a failed service stopping the server | `HttpProcessingTests`: [HTTP service](../docs/implementation/invocation.md#http-service) |
| Composed application/operator recovery and handback; explicit Local/Direct target configuration, retained-kind routing and mismatch refusal; PR operations without a Python helper, over loopback HTTP and over HTTPS with a configured root | `InvocationTests`: [invocation](../docs/implementation/invocation.md) |
| TLS root created once with a private key and public certificate; `zeroshot-tls` refusing to start without its provided root; native initialization through the HTTPS origin, the fixed unpublished inner port, refusal before serving, restart and mixed UID preservation | `NativeTargetStartupTests`: actual target and pinned Caddy images with disposable state, [initialization](../deployment/README.md#explicit-initialization-and-guarded-startup) |
| Selected ADR stack configuration, dependency and discovery decisions; a stale Caddy intermediate after incomplete root rotation | `TargetReadinessTests`: controlled inspection and discovery, plus one actual-image stack, [readiness](../docs/implementation/dotnet-target-readiness.md) |
| Shared DirectTarget HTTP/WebSocket bounds, budgets and stock discovery I/O | `DirectTargetExchangeTests`: [transport limits](../docs/implementation/zeroshot-native-integration.md#directtarget-http-transport-limits) |
| HTTPS/WSS DirectTarget trust in exactly the configured private root, refusal of another root, system trust or a mismatched host name, root re-read per TLS connection within one operation, a missing root failing only its operation with no dispatch intent, and HTTPS completion wait | `DirectTargetTrustTests`: the loopback stand-in serving TLS from an in-process private authority, [transport limits](../docs/implementation/zeroshot-native-integration.md#directtarget-http-transport-limits) |
| DirectTarget session setup, JSON-RPC envelope and run status projection validation | `DirectTargetSessionTests`: loopback stock-target stand-in, [status reader](../docs/implementation/zeroshot-native-integration.md#directtarget-run-status-reader) |
| DirectTarget wait polling cadence, per-read deadlines and cancellation; stop precheck, single force and shared deadline | `DirectTargetRunTests`: the same loopback stand-in with a controlled clock, [status reader](../docs/implementation/zeroshot-native-integration.md#directtarget-run-status-reader) |
| Unmodified native HTTP/OECP boundary with the approved asset, through the application: controlled PR delivery from exact B1 without a client checkout, receipt consumption, restart and offline replay, unavailable-B1 failure and same-run replay, and on-demand frozen-reference reads through the installed helper | `StockDirectTargetTests`: [witness](#controlled-stock-directtarget-witness) |
| Native state written by each listed published target image and served by this revision's image on the same mounts and origin: the recorded native version, the retained correlation and its completed result, and exact replay of an unacknowledged submission onto its recorded run | `TargetImageTransitionTests`: [transition check](#native-state-transition-check) |

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

## Native-state transition check

`TargetImageTransitionTests` checks the
[established native-state transitions](../deployment/README.md#native-state-transitions),
one case per source listed in `deployment/native-state-transitions.json`. It first
requires the published image, pulled by digest, to carry the listed native version
and executable SHA-256. Then it runs the stock witness's controlled layer over that
published image, initializes it the same way and serves it through its own
entrypoint. On it, one Attempt's run finishes, with its target's terminal output
read, not consumed. Another Attempt's send with an unavailable B1 leaves the run
recorded but unacknowledged. The target is then stopped and the same state and
home volumes, forge and loopback origin are served by the controlled layer over
this revision's DirectTarget image, through the entrypoint's ordinary startup
without initialization. Through the application alone:

- Wait reconnects by the retained run identity and disposes the Attempt with a
  receipt equal to the terminal output that the published image produced.
- Resume's exact replay of the unacknowledged submission correlates the run the
  published image recorded, and the native ledger gains no run.

The images workflow runs it with `BROODLING_TEST_TARGET_IMAGE` naming the image it
is about to publish. Like the witness, it uses Docker volumes, a literal-loopback
origin rather than `zeroshot-tls`, and controlled provider, forge and receipt. It
does not cover a run active during the swap, a reverse transition or other native
versions.

## Image demonstration

[`images/demonstrate.sh`](images/demonstrate.sh) checks the two built images
together, outside the TUnit suite. The [images workflow](../.github/workflows/images.yml)
runs it before every publication; run it locally after building the images as
in [images](../deployment/README.md#images):

```bash
tests/images/demonstrate.sh BROODLING_IMAGE TARGET_IMAGE [FACTS_JSON]
```

It needs rootful Docker with Compose, curl, jq and a .NET 10 ASP.NET runtime on
the host. It brings up a disposable instance of ADR 0001's single Compose
project ([compose.yaml](images/compose.yaml): `broodling`, `zeroshot` and the
pinned Caddy `zeroshot-tls` with the package Caddyfile, publishing only on host
loopback) over bind mounts under a unique `image-demo-broodling-121-*` child of
the test workspace root. It then follows the documented order: `initialize-tls`,
`zeroshot-tls`, native `initialize` through the origin, `zeroshot`, and
`initialize-store` as the Broodling image user before `broodling` starts. It
checks two kinds of fact separately:

- Network application checks, on the project network only: the image health
  check reports `broodling` healthy; `zeroshot` reads `http://broodling:8080/health`;
  the installed `broodling-reference` helper reaches the reader by service name
  and gets its `404 unknown_record` for the empty store; `broodling` discovers
  the target through `https://zeroshot.dev.faviann.com`, trusting only the
  mounted public root.
- Host-only checks, made by the host's Docker client: no service mounts a Docker
  socket; `broodling` runs as `1654:1654` under an init and mounts only its
  state directory read/write and the public root read-only; the state directory
  and the store it created are owned by that user; and the image's own
  `check-target`, copied out of the Broodling image and run on the host, reports
  the stack ready with its pinned dependency versions.

Those checks run `broodling` as the reader, without processing configuration.
It then demonstrates verified maintenance of a processing-server Attempt (#205):

- Processing, with [processing.yaml](images/processing.yaml) layered over the
  project: `broodling` restarts from the same image and state directory as the
  processing server with `Broodling__RepositoryRoot=/var/lib/broodling/repositories`,
  and `zeroshot` serves the same native state from the
  [controlled stock-target layer](fixtures/README.md#controlled-stock-directtarget)
  over the target image. Only peers are controlled: `gh` and `git` shims,
  mounted ahead of the image's own on `PATH`, answer GitHub's API from files and
  fetch `acme/widget` from a mounted forge; a `gateway` service under the pinned
  gateway's name, signed by the demonstration's root, which `broodling` trusts
  through `SSL_CERT_FILE`, returns a fixed proposal; credentials are fake. The
  server itself captures, proposes, admits and dispatches a posted issue URL to
  a correlated Attempt whose B1 custody is recorded as
  `/var/lib/broodling/repositories/acme/widget.git`. A forge hold keeps the
  target's worker waiting, so the run is active until it is stopped; the
  server's stop route then abandons the Attempt.
- Maintenance, from [compose.yaml](images/compose.yaml) alone, so the unchanged
  image as `1654:1654` with only its state and the public root mounted and no
  credentials or peers: `pause-installation`; `broodling` and `zeroshot` stop,
  and the host builds the stopped-target check from `docker inspect` of the
  stopped target. `retire-attempt` then records the `stopped_target` retirement,
  and `replace-attempt` prepares the successor without dispatching it.

No real credentials, provider, GitHub or existing target are used, and no real
Codex runs, so it proves neither a real agent's network reach nor PR delivery.
It removes its containers, network, volumes, directory and controlled target
image, and the pinned Caddy image if it pulled it. The optional `FACTS_JSON` receives the
store format, schema version and definition SHA-256 and the identities that
`upgrade-store` upgrades, all from the image's `initialize-store` output, the
Caddy reference and the readiness facts for the release record.

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
