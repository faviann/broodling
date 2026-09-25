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
build needs access to the pinned image/package sources. Each run owns and removes
its test image, containers and disposable volumes; no port is published and test
containers use `--network none`. No existing target is accessed. No real credentials,
networked provider or opt-in live campaign is required.

Durable Git/SQLite fixtures use unique owned children of
`~/.cache/broodling-tests`; set `BROODLING_TEST_WORKSPACE_ROOT` to another durable
root outside temporary paths if needed. Only disposable native state/sockets use
`/dev/shm`. Preserve other runs' directories and production state.

## Owning boundaries

| Boundary | Tests and detailed reference |
| --- | --- |
| Identity, exact source bytes, immutable admission, coherent observation | `IdentityTests`, `SourceCustodyTests`, `ContractIngressTests`, `ContractPolicyTests`, `AdmissionPersistenceTests`: [admission](../docs/implementation/dotnet-contract-admission.md) |
| Explicit fresh initialization, reopen and unchanged refusal of pre-transition state | `StoreLifecycleTests`, authentic [pre-transition .NET fixtures](Broodling.Tests/Fixtures/README.md): [state](../docs/implementation/dotnet-identity-custody.md) |
| Persisted installation pause, transition ordering and dispatch drain | `InstallationPauseTests`: [installation pause](../docs/implementation/dotnet-installation-pause.md) |
| Interrupted first capture, growing reference checkpoints, immutable RequestBundle completion and scoped source/Git reads | `RequestBundleTests`: [state](../docs/implementation/dotnet-identity-custody.md) |
| Original B1 custody, worktree and no-directory HTTP allocation, owned materialization and surviving Git children | `AttemptAdmissionTests`, `GitCustodyTests`, `WorktreeProvisioningTests`, `ProvisioningProcessTests`: [materialization](../docs/implementation/dotnet-worktree-materialization.md) |
| Controlled GitHub issue and service-owned repository acquisition | `GitHubAdmissionTests`, `RepositoryPreparationTests`, [retained issue fixtures](fixtures/ingress/README.md): [ingress](../docs/implementation/dotnet-github-ingress.md) |
| Frozen dispatch, caller death, launcher policy, released SDK/native transport | `NativeDispatchTests`, `DispatchProcessTests`, `NativePolicyTests`, `NativeTransportTests`: [dispatch](../docs/implementation/dotnet-native-dispatch.md) |
| Offline HTTP preparation, retained asset/request reopen, corrupt-content refusal and HTTP submission SQL guards | `HttpSubmissionTests`: [HTTP preparation](../docs/implementation/zeroshot-native-integration.md#http-submission-preparation) |
| Approved execution asset: build-output inclusion, loader refusals, pinned-tool regeneration and native admission | `ExecutionAssetTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#approved-directtarget-execution-asset) |
| Bounded native progress observation, unavailable/timeout mapping and unchanged retained facts | `NativeObservationTests`: [native integration](../docs/implementation/zeroshot-native-integration.md#dispatch-recovery-and-completion) |
| Receipt validation, atomic exact-Attempt completion, late results | `AttemptCompletionTests`, `CompletionPersistenceTests`: [completion](../docs/implementation/dotnet-receipt-completion.md) |
| Stop/quarantine, safe undispatched retirement (including HTTP Attempts), original-B1 replacement | `RetirementTests`, `RetirementProcessTests`, `ReplacementTests`, `ReplacementCompletionTests`: [lifecycle](../docs/implementation/dotnet-retirement-replacement.md) |
| Composed application/operator recovery and handback | `InvocationTests`: [invocation](../docs/implementation/invocation.md) |
| Native explicit initialization, refusal before serving, restart and mixed UID preservation | `NativeTargetStartupTests`: actual target image with disposable state, [startup](../deployment/README.md#explicit-native-initialization-and-guarded-startup) |
| Selected-target configuration, dependency and discovery decisions | `TargetReadinessTests`: [readiness](../docs/implementation/dotnet-target-readiness.md) |
| Shared DirectTarget HTTP/WebSocket bounds, budgets and stock discovery I/O | `DirectTargetExchangeTests`: [transport limits](../docs/implementation/zeroshot-native-integration.md#directtarget-http-transport-limits) |
| DirectTarget session setup, JSON-RPC envelope and run status projection validation | `DirectTargetSessionTests`: loopback stock-target stand-in, [status reader](../docs/implementation/zeroshot-native-integration.md#directtarget-run-status-reader) |
| DirectTarget wait polling cadence, per-read deadlines and cancellation | `DirectTargetRunTests`: the same loopback stand-in with a controlled clock, [status reader](../docs/implementation/zeroshot-native-integration.md#directtarget-run-status-reader) |

`Broodling.ProcessWitness` is a test-only caller for real process-death and
Git-lock boundaries. Ordinary build/test/publish copies the C administrative
shim. The separate C# Codex launcher builds self-contained for Linux x64 using
the pinned .NET 10.0.12 runtime.

The retained [controlled Codex provider](fixtures/README.md) replaces only the
provider; the released SDK and bundled native engine run. The Python files in
[Fixtures](Broodling.Tests/Fixtures/README.md) control SDK responses, malformed
transport or profile inspection. None implements another Broodling application
or authority store. Stub PR receipts are not real DirectTarget delivery.
Readiness, exchange, session and run tests control Docker/HTTP boundaries or use loopback peers and contact no real target.

## Evidence limits and history

No-effect native success still refuses stable completion. Terminal labels do
not prove physical cessation; every dispatched Attempt remains quarantined.
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
