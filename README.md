# Broodling

Broodling admits one software-change Work Unit, gives its frozen Contract to
Zeroshot's standard `software-change` workflow, and retains the receipt-backed
lifecycle decision. The application and operator commands are C# on .NET 10.
Zeroshot owns execution, review, repair and provider sessions.

Start with the [current architecture and operating status](docs/governing/current.md).
Use is limited to operator-supervised internal PR proposals: [P5 remains scoped
**FAIL**](evaluation/p5/README.md). Every delivered PR needs independent human
review of the **exact accepted revision** against the complete frozen request
and Contract, considering repository tests/CI, before a separate merge decision.
`SUCCEEDED` certifies neither semantic correctness nor permission to merge,
deploy or release.

## Develop and validate

Use Linux x86-64, a .NET 10 SDK (tested with 10.0.401), Git,
a C compiler with libc headers, and Python 3.13+ for the pinned SDK bridge
(no-effect LocalTarget work and its tests).
The application uses SQLite through `Microsoft.Data.Sqlite`. Native Git
administration requires a non-PID-1 host with waitable children and no competing
reaper; see [materialization](docs/implementation/dotnet-worktree-materialization.md).

Create the pinned bridge environment and run the suite and Release build as
described in [tests/README.md](tests/README.md#run).

The [TUnit suite](tests/README.md) includes controlled released-SDK/native checks
and fails when the dependency is unavailable. It makes no live-provider or
deployment claim. There is no Python Broodling package or pytest acceptance gate.

## Application and operator use

The [release and operations guide](deployment/README.md) gives ordinary
`dotnet publish` commands, the complete host/launcher package, the published
Broodling and DirectTarget images, explicit store
initialization, invocation configuration and existing-target readiness checks.
Source/release support is distinct from a validated live .NET deployment.
The [#77 deployment record](deployment/validation.md) describes historical Python
validation only.

Application behavior is callable without HTTP. Follow the seam you need:

| Operation | Reference |
| --- | --- |
| Explicit fresh store initialization, Work Unit identity and source custody | [State API](docs/implementation/dotnet-identity-custody.md) |
| Supplied-source or explicit GitHub issue admission, typed caller proposer | [Ingress](docs/implementation/work-reference-ingress.md) |
| Original B1 and owned worktree | [Allocation](docs/implementation/dotnet-attempt-allocation.md), [materialization](docs/implementation/dotnet-worktree-materialization.md) |
| Submit, inspect, resume, wait and stop | [Invocation](docs/implementation/invocation.md) |
| Frozen native dispatch, launcher and credential policy | [Native integration](docs/implementation/zeroshot-native-integration.md) |
| Receipt-backed completion by exact Attempt | [Completion](docs/implementation/dotnet-receipt-completion.md) |
| Safe never-dispatched retirement and explicit replacement | [Lifecycle](docs/implementation/dotnet-retirement-replacement.md) |
| Inspect an existing selected DirectTarget | [Target readiness](docs/implementation/dotnet-target-readiness.md) |

The ASP.NET host serves retained work, frozen references and bounded native
progress over HTTP from existing state. With its
[processing configuration](deployment/README.md#processing-server) it also
accepts URL-only Issue submissions and resumes and stops exact work, while
automatic progression through
[preparation](docs/implementation/dotnet-contract-admission.md#issue-submission-preparation)
and the bundled proposer to dispatch, and automatic completion capture, run for
its lifetime ([HTTP service](docs/implementation/invocation.md#http-service)).
Scheduling, backlog selection, Compose deployment, maintenance and retention
automation remain separately scoped work.

## Native boundary and limitations

The only production Python source file is the [one-call SDK bridge](src/Broodling/bridge/zeroshot_bridge.py)
for no-effect LocalTarget work: it translates version/submit/wait/stop calls to
the pinned official SDK and returns public results. C# owns authority, policy, recovery, persistence and
receipt validation. The [bridge dependency file](src/Broodling/bridge/requirements.txt)
retains the exact official wheel URL and SHA-256: SDK **10.3.0.post1**, bundled
Zeroshot **10.3.0**. Codex remains **0.153.4**.

Exactly one authorized GitHub `pull_request` effect naming a target branch
selects HTTP DirectTarget PR delivery, with no Python helper: the stock
`zeroshot target serve` receives the release-bundled approved asset (one uniform
Codex / `gateway` / `gpt-5.6-sol` / medium-effort runtime) through exactly
`https://cliproxy.local.faviann.com/v1`.
Current `GH_TOKEN`, `GATEWAY_BASE_URL` and `GATEWAY_API_KEY` are required for
dispatch/replay; their values are not frozen in the invocation. Durable
correlation permits credential-independent reconnection. Other, mixed,
multiple or underspecified effects refuse.

Empty effects select LocalTarget. Its C# Codex launcher enforces explicit local
policy and isolated homes. The profile requires a trusted host without
operator-managed effect-capable MCP/extensions; it is no universal no-effect
proof. Native success supplies no stable local result, so successful Broodling
disposition remains refused.

Every dispatched Attempt is permanently ineligible for automatic deletion or
replacement, even after success, failure or stop. Terminal status is not physical
cessation. Broodling adds no execution supervisor or automatic merger.

## Migration and existing state

The [parity map](docs/migration/130-parity-map.md),
[exact-baseline validation](docs/migration/130-baseline-validation.md) and
[passed independent migration review](docs/migration/130-migration-review.md)
retain traceable evidence. Retired Python source and tests remain in the
[frozen baseline tree](https://github.com/faviann/broodling/tree/b3f61a96c40401722ec16fc361958d1690982e02);
later evidence records identify their own revisions.

Before any operational switch, the owner must explicitly choose to drain
existing Python work or explicitly abandon and retain it. Follow the
[fresh-state cutover gate](deployment/README.md#existing-python-work-and-the-operational-switch).
.NET uses separate fresh state; there is no import or in-flight takeover.
Source retirement does not authorize replacement, deletion or deployment.
