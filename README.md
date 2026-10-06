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

Use Linux x86-64, a .NET 10 SDK (tested with 10.0.401), Git, and a C compiler
with libc headers for the store's
[initiation-lock shim](docs/implementation/dotnet-installation-pause.md#ordering-boundary).
The application uses SQLite through `Microsoft.Data.Sqlite`.

Run the suite and Release build as described in [tests/README.md](tests/README.md#run).

The [TUnit suite](tests/README.md) includes controlled stock-native checks and
fails rather than skips when a dependency, such as Docker, is unavailable. It
makes no live-provider or deployment claim. There is no Python Broodling package or pytest acceptance gate.

## Application and operator use

The [release and operations guide](deployment/README.md) gives ordinary
`dotnet publish` commands, the complete host package, the published
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
| Original B1 custody and Attempt admission | [Allocation](docs/implementation/dotnet-attempt-allocation.md) |
| Submit, inspect, resume, wait and stop | [Invocation](docs/implementation/invocation.md) |
| Frozen native dispatch and credential policy | [Native integration](docs/implementation/zeroshot-native-integration.md) |
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

Broodling has one execution target, the HTTP DirectTarget, reached through the
pinned `Zeroshot.Client` SDK. C# owns authority, policy, recovery, persistence
and receipt validation, and there is no production Python source.

Exactly one authorized GitHub `pull_request` effect naming a target branch
selects DirectTarget PR delivery: the stock native 10.10.0
`zeroshot target serve` receives the release-bundled approved asset (one uniform
Codex / `gateway` / `gpt-5.6-sol` / medium-effort runtime) through exactly
`https://cliproxy.local.faviann.com/v1`. The DirectTarget image pins Codex
**0.153.4**.
Current `GH_TOKEN`, `GATEWAY_BASE_URL` and `GATEWAY_API_KEY` are required for
dispatch/replay; their values are not frozen in the invocation. Durable
correlation permits reconnection without those dispatch credentials. Every
target contact also needs the DirectTarget's private control token, read from
the file the configuration names for the retained origin
([private control access](deployment/README.md#private-control-access)); the
target serves only native's private mode. Contract admission
refuses an empty effect set and other, mixed, multiple or underspecified
effects.

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
