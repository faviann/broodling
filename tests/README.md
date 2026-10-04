# Broodling tests

The supported suite is TUnit on .NET 10. The [current authority](../docs/governing/current.md)
governs scope; these checks establish Broodling-owned behavior and its public
SDK seam, not provider semantic quality or a validated live deployment.

## Run

Use Linux x86-64, a .NET 10 SDK (tested with 10.0.401), Git, `cc` and libc headers.
`global.json` selects the Microsoft.Testing.Platform runner, not an SDK version.
Restore needs a GitHub token with `read:packages` for the pinned `Zeroshot.Client`
from GitHub Packages ([nuget.config](../nuget.config)). Supply it outside the
repository, in the environment or the user-level NuGet configuration:

```bash
export NuGetPackageSourceCredentials_github="Username=YOUR_GITHUB_USER;Password=$(gh auth token)"
```

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
The asset check downloads the DirectTarget binding's pinned native 10.10.0 Linux
x86-64 release archive from GitHub into `native-releases/` under the test workspace root (below)
when it is absent there, and fails without that access. A cached archive is
verified by checksum and needs no network.
The image startup tests also require rootful Docker access. They build the actual
`deployment/DirectTarget.Dockerfile`, which fetches the pinned native release
archive, so an uncached build needs access to the pinned image/package sources.
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
| LocalTarget bridge: frozen dispatch, caller death, launcher policy, released SDK/native transport, the bridge dependency file pinned to the handshake SDK apart from the DirectTarget binding, and refusal of a direct locator | `NativeDispatchTests`, `DispatchProcessTests`, `NativePolicyTests`, `NativeTransportTests`: [dispatch](../docs/implementation/dotnet-native-dispatch.md) |
| Offline HTTP preparation, retained asset/request reopen, a bundle-bound task's compact manifest without reference bodies, corrupt-content refusal and HTTP submission SQL guards | `HttpSubmissionTests`: [HTTP preparation](../docs/implementation/zeroshot-native-integration.md#http-submission-preparation) |
| HTTP send gates, intent before contact, one SDK attempt of the exact retained bytes, exact acknowledgement with a foreign one reported, retained conflict, concurrent replies, late acknowledgement stop, killed HTTP callers and a buffered request accepted after caller death | `HttpDispatchTests`, `DispatchProcessTests`: loopback stock-target stand-in, [HTTP dispatch](../docs/implementation/zeroshot-native-integration.md#http-dispatch-and-acknowledgement) |
| Approved execution asset: build-output inclusion; loader refusal of a missing, changed or differently hashed asset, any manifest other than the reviewed one (a changed native, policy or recipe binding, the superseded 10.3.0 approval); the manifest policy read from the asset's own runtime; reproduction, structure and native admission from the binding's pinned release archive | `ExecutionAssetTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset), [recipe](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset) |
| Bounded native progress observation, unavailable/timeout mapping and unchanged retained facts | `NativeObservationTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#dispatch-recovery-and-completion) |
| Receipt validation, with one table of refused receipts (non-object, missing, extra or non-string fields, `v1`/`opened`, non-ready outcomes, wrong repository or branch, unchanged or malformed head, malformed PR ID) driven through both the application and SQL's `completion_bound`; atomic exact-Attempt completion, offline reads and late results (correlated HTTP Attempts over the loopback stand-in) | `AttemptCompletionTests`, `CompletionPersistenceTests`: [completion](../docs/implementation/dotnet-receipt-completion.md) |
| Automatic completion without a reader: startup/running discovery that never dispatches, per-scan retry of temporary failures, retained receipt and accepted-pin refusal, detachment after authority loss, shutdown and restart, no rediscovery of retained results | `CompletionObserverTests`: [automatic observation](../docs/implementation/dotnet-receipt-completion.md#automatic-completion-observation) |
| Stop/quarantine, safe undispatched retirement (including HTTP Attempts), verified stopped-target maintenance retirement, original-B1 replacement, including after that maintenance retirement | `RetirementTests`, `RetirementProcessTests`, `ReplacementTests`, `ReplacementCompletionTests`: [lifecycle](../docs/implementation/dotnet-retirement-replacement.md) |
| Predecessor-linked revisions over real capture, Git and SQLite with controlled GitHub and gateway peers and the stock-target stand-in: refusal of active work, a current Attempt and unretired dispatched work; concurrent requests converging on one successor, exact replay after later history and ordinary submission creating nothing; material without a committed Contract, or whose admitted Contract was cancelled before any Attempt, seeking admission; an unchanged successor ending with its retained link without proposal or execution although the issue, a referenced issue and a referenced comment gained GitHub bookkeeping, while a moved starting commit, or a changed issue body with the same Executable Request, proceeds; an ordinary first Attempt after maintenance retirement with predecessor stop replay unable to reach it; and two revisions retaining each result, with the later revision's failed Attempt still replaceable | `RevisionTests`: [revised work](../docs/implementation/invocation.md#revised-work) |
| HTTP reader: existing-state startup refusal and session release, retained reads mapped to application operations without external services or writes, reads while another session holds the writer | `HttpReadTests` |
| Processing server over real capture, Git and SQLite with controlled GitHub and gateway peers and the stock-target stand-in, through the host's own composition: acknowledgement with handle and `Location` after durable acceptance and before acquisition, refusal of an unsupported reference before any write, processing and result retention with no client connected, a stopped submission's visible reason and exact resume, retained facts apart from available and unavailable native observations, shutdown detaching in-flight dispatch; stops with a required reason that outlive a disconnected caller and report abandonment apart from cessation, submission cancellation and hand-back; a revision accepted once with its `Location` and replay, refused for active work, and its unchanged end readable; startup refusal of configurations that cannot process and a reader refusing submission; a failed service stopping the server | `HttpProcessingTests`: [HTTP service](../docs/implementation/invocation.md#http-service) |
| Composed application/operator recovery and handback; explicit Local/Direct target configuration, retained-kind routing and mismatch refusal; PR operations without a Python helper, over loopback HTTP and over HTTPS with a configured root | `InvocationTests`: [invocation](../docs/implementation/invocation.md) |
| TLS root created once with a private key and public certificate; `zeroshot-tls` refusing to start without its provided root; native initialization through the HTTPS origin, the fixed unpublished inner port, refusal before serving, restart and mixed UID preservation | `NativeTargetStartupTests`: actual target and pinned Caddy images with disposable state, [initialization](../deployment/README.md#explicit-initialization-and-guarded-startup) |
| Selected ADR stack configuration, dependency and discovery decisions; a stale Caddy intermediate after incomplete root rotation | `TargetReadinessTests`: controlled inspection and discovery, plus one actual-image stack, [readiness](../docs/implementation/dotnet-target-readiness.md) |
| Readiness discovery's bounded read and budget | `DirectTargetExchangeTests`: [transport](../docs/implementation/zeroshot-native-integration.md#directtarget-transport-and-budgets) |
| The configured root reaching the SDK's HTTPS and WSS connections: exactly that root, refusal of another root or system trust, a missing root failing only its operation with no dispatch intent, a new root read by each dispatch, and an HTTPS completion wait through the SDK's watch | `DirectTargetTrustTests`: the loopback stand-in serving TLS from an in-process private authority, [transport](../docs/implementation/zeroshot-native-integration.md#directtarget-transport-and-budgets) |
| SDK run reader and stopper: reconnection by the retained binding, unsupported bindings refused before contact, identity (`foreign_run`) and fixed failure kinds, read budgets, a finished first status as the result, wait through the SDK's watch without polling, a failed watch detaching with a fixed kind, cancellation detaching without stop; stop precheck, single force, a force that cannot connect as not sent, a foreign force reply as uncertain, waiting after a nonterminal force and the shared deadline | `DirectTargetRunTests`: the loopback stock-target stand-in with a controlled clock, [run reader and stopper](../docs/implementation/zeroshot-native-integration.md#directtarget-run-reader-and-stopper) |
| Unmodified native HTTP/OECP boundary with the approved asset, through the application: controlled PR delivery from exact B1 without a client checkout, receipt consumption, restart and offline replay, unavailable-B1 failure and same-run replay, and on-demand frozen-reference reads through the installed helper | `StockDirectTargetTests`: [witness](#controlled-stock-directtarget-witness) |
| The approved asset's stock PR readiness, repair and feedback contract on the same boundary: `ready` and accepted pinning despite a failing optional check or a missing approval native may hand off; a required check in progress or missing, or an approval it may not hand off, pending across polls until an explicit stop while a bounded Wait invents no receipt; behind-head advancement; CI-failure and conflict repair; ten-iteration repair exhaustion; new and edited versus unchanged feedback in one live run; exact PR identity refusal; no merge request in native's recorded forge requests, with the native and asset identities | `StockPullRequestDeliveryTests`: [witness](#pr-readiness-repair-and-feedback) |
| Native state written by this revision's target image, or by each listed published image of the same native, and served by this revision's image on the same mounts and origin: the recorded native version, the retained correlation and its completed result, and exact replay of an unacknowledged submission onto its recorded run | `TargetImageTransitionTests`: [transition check](#native-state-transition-check) |

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

`StockDirectTargetTests` runs the selected unmodified native: `zeroshot 10.10.0`
(source `3ee1192cec359a0b997f464e703a936e8b67d63c`, Linux x86-64 executable
SHA-256 `d0c84ffbafa731ef7fa6b61f87af9c000cc4e5b4d2e0d3b7df461fd239bb923e`) from
the pinned `v10.10.0` release archive, as `zeroshot target serve` inside the
actual DirectTarget image, with the approved asset
`sha256:258dc0ab46f30f05d6c95f7be493ede2ad0963160b9247f5ccdb699e4dcc20fc`. Only
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
  The candidate commit's parent is exact B1. Before publishing, native merges
  the moved target branch into it and routes that integration to delivery
  repair as `repair_required`, whose commit follows the merge. Native reports the PR `ready`
  (the controlled forge has no checks, protection or feedback), and Wait
  validates its `v2`/`pr`/`ready` receipt,
  fetches and pins the accepted commit from the forge and disposes atomically.
  The consumed receipt equals the target's terminal output after a target
  restart, and the retained completion replays with the target gone.
- A send of an exact B1 missing from the forge reaches the target, which records
  the run, but its acknowledgement is lost: the test commits the dispatch intent
  and sends the retained request itself, discarding the reply. An exact replay
  with rotated credentials correlates the same single run, whose checkout
  failure abandons the Attempt with no delivery branch.
- A bundle-bound Attempt's controlled agent reads every reference listed in its
  task through the image's `broodling-reference` helper, using only the bundle
  and reference IDs. A real application reader over the retained store serves
  them. It binds only to the host's default-bridge gateway, which the target
  resolves as `broodling`; a mounted file replaces only the image's reader
  origin. The delivered commit carries the exact retained bytes. This proves the
  native execution environment's read, not Compose service-name networking
  (homelab-iac#353).

Native 10.10.0 acknowledges a submission once it has recorded the run, then
prepares the execution environment, including the checkout, in the background.
A missing B1 therefore fails the correlated run rather than the send, and a
slow real fetch no longer holds the 60-second submit budget.

The provider, forge and PR receipt are controlled. The run is not a real GitHub
PR, semantic-quality result, image publication or production topology check.

### PR readiness, repair and feedback

`StockPullRequestDeliveryTests` runs the same native, image and approved asset,
one fresh target per case. Each case scripts the
[controlled forge](fixtures/README.md#controlled-stock-directtarget) through its
scenario file, and the application alone dispatches, waits, observes and stops.
The asset's delivery node is `builtin.git-delivery.pr@2` with native's default
`Consider` feedback policy. `ci_failed`, `conflict` and `repair_required` route
to its delivery repair node inside the ten-iteration change loop. Delivery polls
every 20 seconds with no deadline. Evidence comes from three places: native's
forge requests as the forge traced them, the delivery repair inputs that the
controlled provider appends to `delivery-repairs.jsonl` in the next candidate
commit, and, where the application reduces a failure to `native_failed`,
native's own ledger read inside the target:

- Ready: GitHub reports `UNSTABLE` because an optional check failed while the
  required one passed. Or `BLOCKED`, `REVIEW_REQUIRED` under a rule requiring
  only an approving review, which native's approval handoff permits. Wait
  accepts the `v2`/`pr`/`ready` receipt and pins exactly the PR head native last
  assessed.
- Pending: a required check in progress, a required check missing, or an
  approval missing where the rule also requires conversation resolution. Native
  reports `deliver` active and keeps polling at its interval. A Wait cancelled
  after 5 seconds leaves no completion and no abandonment. The explicit stop
  abandons the Attempt, native records `force_stopped`, and nothing completes.
- Behind: main moves without conflict once the PR opens, and the scenario's
  branch rule requires up-to-date branches, the only case in which GitHub
  reports `BEHIND`. Native requests
  GitHub's branch update from the published head, adopts the forge's merge
  commit and hands off that head on the next poll.
- CI failure: the first head's required check fails. The repair receives the
  check and its job-log excerpt, and the repaired head is ready.
- Conflict: main changes the candidate's file once the PR opens. Native
  materializes the conflict, the repair receives `conflictedPaths`, and the
  repaired head integrates main.
- Exhaustion: the required check always fails. Ten deliveries of ten heads, each
  followed by a repair, end the run as `change_attempts_exhausted`. The Attempt
  is abandoned without completion.
- Feedback: two pages of discussion comments, a bodiless changes-requested
  review and an inline comment exist when the PR opens. The first delivery sends
  all four to repair. The second reads them unchanged and goes on to readiness.
  Once it has, the test edits one comment. Only that comment returns to repair,
  and the third delivery is ready. This is native's in-memory checkpoint within
  one live run. It proves nothing about deduplication after a target restart.
- Identity: the forge reports the created PR's head in another repository.
  Native refuses it as `delivery_failed` without assessing readiness, reading
  feedback or repairing.

Every case also requires the binding's native version and executable SHA-256,
as read in the running target, and the approved asset SHA-256 retained by the
Attempt. It requires no merge request (`gh pr merge`, a REST merge route or a
merge/auto-merge/queue mutation), no forge request outside the fixture, and
nothing on main but B1 and the forge's own scripted change. Each case prints
those identities, its result and native's forge requests to its test output,
which `--report-trx` retains. The cases run in parallel. A case takes 15 to 60
seconds, most of it native's poll interval.

The scenarios are controlled GitHub responses, not a live forge. Future human
reviews, resolution of every comment, merge and PR quality are outside what the
stock `ready` contract promises and what these cases show.

## Native-state transition check

`TargetImageTransitionTests` checks the
[established native-state transitions](../deployment/README.md#native-state-transitions):
one case restarts this revision's target image over native state it wrote
itself, and one case per source listed in `deployment/native-state-transitions.json`
(currently none) updates from it. Every source must carry the DirectTarget
binding's native version and executable SHA-256, which the case first checks in
the image, pulling a listed image by digest. Then it runs the stock witness's
controlled layer over that source image, initializes it the same way and serves
it through its own entrypoint. On it, one Attempt's run finishes, with its
target's terminal output read, not consumed. Another Attempt's send with an
acknowledgement is lost as in the witness, leaving the run recorded but
unacknowledged. The target is then
stopped and the same state and home volumes, forge and loopback origin are
served by the controlled layer over this revision's DirectTarget image, through
the entrypoint's ordinary startup without initialization. Through the
application alone:

- Wait reconnects by the retained run identity and disposes the Attempt with a
  receipt equal to the terminal output that the source image produced.
- Resume's exact replay of the unacknowledged submission correlates the run the
  source image recorded, and the native ledger gains no run.

The images workflow runs it with `BROODLING_TEST_TARGET_IMAGE` naming the image it
is about to publish. Like the witness, it uses Docker volumes, a literal-loopback
origin rather than `zeroshot-tls`, and controlled provider, forge and receipt. It
does not cover a run active during the swap, a reverse transition or another
native version.

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
It then demonstrates verified maintenance and replacement of a processing-server
Attempt (#205, #210):

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
- Dispatch of the Replacement Attempt (#210): `release-installation`, then
  `broodling`, `zeroshot` and `gateway` restart with the processing overlay, and
  the image's `resume`, run beside the server as the processing service with its
  invocation configuration, fake credentials and network, dispatches the
  successor, which the restarted server left `prepared`. Resume passes the
  retained B1 custody check at the recorded container path inside the one-off
  container, and the server then reads the successor as `correlated` with the
  intended run ID that `replace-attempt` printed, its run available at the
  target; the predecessor stays retired.

No real credentials, provider, GitHub or existing target are used, and no real
Codex runs, so it proves neither a real agent's network reach nor PR delivery.
It removes its containers, network, volumes, directory and controlled target
image, and the pinned Caddy image if it pulled it. The optional `FACTS_JSON` receives the
store format, schema version and definition SHA-256 and the identities that
`upgrade-store` upgrades, all from the image's `initialize-store` output, the
Caddy reference and the readiness facts for the release record.

## SDK adoption

Broodling adopts one exact `Zeroshot.Client` package and owns the evidence that
its integration works with it. The SDK's own release checks accept the package
for publication. This lane accepts it for Broodling. A failure here blocks
Broodling's adoption of that package. It is not a prerequisite for SDK
publication and does not make a Broodling revision part of the SDK's release.

[`Broodling.csproj`](../src/Broodling/Broodling.csproj) references one exact
version, written `[VERSION]`, and its [`packages.lock.json`](../src/Broodling/packages.lock.json)
records its NuGet content hash (SHA-512). Every restore refuses other bytes under
that version with `NU1403`. The Broodling image build is a CI build
(`ContinuousIntegrationBuild`), so its restore runs in locked mode and also
refuses a lock file that no longer matches the references (`NU1004`); a local
restore rewrites a stale lock file instead.

### Adoption lane

The lane runs the compact suites that prove the adapter's consumer contract,
the stock-native witnesses and the [native-state transition check](#native-state-transition-check)
as the restart/replay baseline, plus the asset, schema-freeze and fresh-store
checks that identify the recorded contract. It needs the full [run](#run)
environment, including Docker. Run it, then the full suite and the Release build:

```bash
dotnet test --project tests/Broodling.Tests/Broodling.Tests.csproj --treenode-filter '/*/*/(HttpSubmissionTests)|(HttpDispatchTests)|(DispatchProcessTests)|(NativeObservationTests)|(AttemptCompletionTests)|(CompletionPersistenceTests)|(CompletionObserverTests)|(RetirementTests)|(RetirementProcessTests)|(DirectTargetRunTests)|(DirectTargetTrustTests)|(InvocationTests)|(StockDirectTargetTests)|(StockPullRequestDeliveryTests)|(TargetImageTransitionTests)|(ExecutionAssetTests)|(ApplicationSchemaFreezeTests)|(StoreLifecycleTests)/*'
dotnet test --solution Broodling.sln
dotnet build Broodling.sln --configuration Release
```

| Required behavior | Tests |
| --- | --- |
| Exact-B1 source in the frozen request; a B1 missing from the forge fails the run without fallback | `HttpSubmissionTests`, `StockDirectTargetTests` |
| Intent before contact; one SDK attempt of the retained bytes; only the exact acknowledgement correlates and a foreign one is reported, not adopted | `HttpDispatchTests`, `InvocationTests` |
| Lost, late or concurrent replies and caller death leave intent unresolved; authorized exact replay, with rotated credentials kept apart, converges on the same run without regenerating content | `HttpDispatchTests`, `DispatchProcessTests`, `HttpSubmissionTests`, `StockDirectTargetTests` |
| Retained run identity across client restart, target restart and a same-native image restart | `CompletionObserverTests`, `DispatchProcessTests`, `StockDirectTargetTests`, `TargetImageTransitionTests` |
| Observation and wait cancellation or shutdown neither stop nor complete a run; a restarted observer retains the same run | `NativeObservationTests`, `DirectTargetRunTests`, `CompletionObserverTests` |
| Atomic receipt acceptance, Git pinning and disposition; retained completion and offline reads | `AttemptCompletionTests`, `CompletionPersistenceTests`, `InvocationTests`, `StockDirectTargetTests` |
| C# and SQL agree on every refused receipt | `AttemptCompletionTests`, `CompletionPersistenceTests` |
| Stop: intended-ID precheck, single force, uncertain effects, one total stop budget | `DirectTargetRunTests`, `RetirementTests` |
| Dispatched work stays quarantined; retirement and replacement keep custody across process death | `RetirementTests`, `RetirementProcessTests` |
| Stock-native readiness, repair and feedback cases | `StockPullRequestDeliveryTests` |
| One composed application invocation over the unmodified native through the SDK | `StockDirectTargetTests`, with `InvocationTests` and `DirectTargetTrustTests` over the loopback stand-in |
| Approved asset and native identity; fresh-store definition | `ExecutionAssetTests`, `ApplicationSchemaFreezeTests`, `StoreLifecycleTests` |

Every record in these tests is created fresh under the current contract.
For HTTP DirectTarget Attempts, submission, status, wait and stop go only through
[`DirectTargetClient`](../src/Broodling/DirectTargetClient.cs); readiness
discovery alone is a plain HTTP read, over the SDK's handler. The LocalTarget
cases in these classes, such as `NativeObservationTests`' `ZeroshotTransport`
cases and the `ControlledTransport` cases in `DispatchProcessTests`,
`RetirementProcessTests` and `InvocationTests`, run through the bridge and are
not adoption evidence.

### Upgrading the SDK

To adopt another `Zeroshot.Client` version, in one change: set the exact
version in `Broodling.csproj`, regenerate `packages.lock.json` by restoring,
confirm that the restored `.nupkg` SHA-256 equals `expectedSha256` and
`servedSha256` in the SDK's publication record for that version, adapt Broodling to it, run the lane, the full suite and
the Release build, and replace the record below. A new native release or asset
also changes the binding and its approval; see the
[native integration](../docs/implementation/zeroshot-native-integration.md#pinned-dependencies-and-bridge).
Never refresh the lock file alone to make a restore pass. The publication
record is `publication.json` in the `publication` artifact of the
qualification run for the SDK's version tag in `faviann/zeroshot-dotnet-sdk`:

```bash
gh run download RUN_ID -R faviann/zeroshot-dotnet-sdk -n publication
```

### Current adoption record

- Tested code revision: `0283c4d667200e85286149f7be093d4f47a9d583`. Later
  commits that only edit this record or other documentation leave it valid; a
  code change requires a new run.
- SDK: `Zeroshot.Client` `10.10.0.1-preview.1` from
  `https://nuget.pkg.github.com/faviann/index.json`; `.nupkg` SHA-256
  `b6edd08c0054a057460a69b245d11e242e34737245b33ee51d7f2cd893fc7d10`, NuGet
  content hash `m2+uSkQbhLCDDEdQ3Y397BFgwM1IKNYO3BSnMkMsje0Jr0GSTNAxXW0/HEdHTj5EkXYhxNOI5OmcbfWsmGvxXg==`.
  The SHA-256 equals `expectedSha256` and `servedSha256` in the SDK's verified
  publication record (`faviann/zeroshot-dotnet-sdk` tag `v10.10.0.1-preview.1`,
  source `0518360411d252539ff85137eec81a795c45e0ab`, qualification run
  `37235647508`); the SDK publishes no GitHub release for it.
- Approved asset: SHA-256
  `258dc0ab46f30f05d6c95f7be493ede2ad0963160b9247f5ccdb699e4dcc20fc`.
  Native: `zeroshot 10.10.0`, source `3ee1192cec359a0b997f464e703a936e8b67d63c`,
  Linux x86-64 executable SHA-256
  `d0c84ffbafa731ef7fa6b61f87af9c000cc4e5b4d2e0d3b7df461fd239bb923e`, from the
  `v10.10.0` musl archive SHA-256
  `fbc13b2385a088ff0f8fa03fdf72d4aa7ae6202d4289204e57ba1617628d6f16`.
- Fresh store, from `initialize-store` at that revision: format
  `broodling.application`, schema version 1, definition SHA-256
  `2ef2d8752c19220da9dfaa8800520032ce0982a76de5c59b599884a5dc0d1d60`, upgrading
  from nothing. The version is not yet frozen.
- Results, on Linux x86-64 with .NET SDK 10.0.401 and Docker 29.8.1, the
  transition check building this revision's target image: the lane passed 273 of
  273 on two consecutive runs; the full suite passed 719 of 719; the Release
  build succeeded with no warnings. The host was heavily loaded by unrelated
  work (load average 10 to 13). Under that load, earlier runs at this revision
  failed bounded-wait cases that pass on rerun: `CompletionObserverTests`
  timeouts and, once, `TimeoutError` budget expiries in `DirectTargetRunTests`,
  `DirectTargetTrustTests` and `AttemptCompletionTests` in the lane, and
  `ReplacementTests`' `A local dispatch is still initiating` in the full suite.
  Those three classes then passed five consecutive runs. The
  `CompletionObserverTests` and `ReplacementTests` cases fail the same way at
  the previous revision `708aec6` on the same host.
- Baseline: the transition check ran with its recorded scope unchanged, as a
  restart of this revision's native 10.10.0 target image over its own state
  (#215 introduced it for 10.9.0; #226 moved the binding). No transition source
  is listed. The native binding, approved asset and fresh-store definition are
  unchanged from the `0.2.0-preview.1` adoption.

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
