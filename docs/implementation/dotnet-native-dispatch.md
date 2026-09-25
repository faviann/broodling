# .NET frozen native dispatch

[F #137](https://github.com/faviann/broodling/issues/137) composes the existing
acquisition, admission, Attempt allocation and
[owned materialization](dotnet-worktree-materialization.md) with the pinned
Zeroshot SDK. Since #180 this SDK bridge serves only no-effect LocalTarget work;
authorized PR work uses the [HTTP DirectTarget](zeroshot-native-integration.md#composed-invocation-and-target-selection). The [release guide](../../deployment/README.md) describes current
packaging and the separate operational cutover gate. The frozen parity baseline is
`b3f61a96c40401722ec16fc361958d1690982e02`. The #130 spike is protocol evidence,
not an adopted API or a claim of real PR delivery.

## Callable application

```csharp
using var store = new BroodlingApplication().OpenStore(databasePath);
// Authorized PR work: an HTTP DirectTarget Attempt. No Python, workspace or launcher.
var direct = new Invocation(store, new InvocationTarget.Direct(directTargetOrigin));
var credentials = new DispatchCredentials(githubToken, gatewayBaseUrl, gatewayApiKey);
var status = await direct.SubmitAsync(reference, propose,
    [new RequiredEffect("pr", "Deliver a PR to main.", "pull_request", "main")],
    repositoryPath, revision: originalCommit, credentials: credentials);
// Retain status.Revision.ContractRevisionId and status.Attempts.Single().AttemptId.
var resumed = await direct.ResumeAsync(status.Revision.ContractRevisionId,
    credentials: credentials);

// No-effect work: a worktree Attempt through the pinned SDK bridge.
var local = new Invocation(store, new InvocationTarget.Local(workspaceRoot,
    new NativeProfile(nativeStateDirectory, codexProfile), new ZeroshotTransport(pythonExecutable)));
```

The caller owns the store lifetime and uses a separate session per caller.
`SubmitAsync` composes `AdmitGitHubAsync`; its proposer, exact required effects,
additional caller-entitled sources, source boundary and producer are the existing
D inputs. Repetition reacquires source bytes and may create a new revision.
`ResumeAsync` uses only the specified stored revision. Rejected admission or
ended Attempt authority returns retained status. Before first allocation it
requires a repository; afterward the original B1/allocation govern. It continues
interrupted materialization before a submission exists. Once any invocation is
prepared it reconciles the frozen request without restoring its workspace.
Direct `ProvisionAttempt` also refuses after dispatch intent.

`Status` and `History` now include `Submissions` (with an HTTP record's
`Format`, `IntendedRunId` and `ReplayBlockedReason`; `RunId` stays confirmed
correlation only), alongside the existing exact
revision's sources, Contract, decision and Attempts. They remain coherent,
deferred SQLite reads without native access. Exceptions do not undo earlier
commits: use history to find the exact revision after a failed submit.

`PrepareSubmission(attemptId, profile)` freezes a bridge request independently of
dispatch. `DispatchAsync(attemptId, profile, transport, token)` prepares if
necessary and reconciles that same invocation. `FindSubmission`
returns `NativeSubmission`: Attempt ID, submission key, request JSON, state,
optional run ID and the derived frozen `NativeLocator`. A correlated resume
does not reconstruct a dispatch profile, select current HEAD, invoke the SDK,
or require the old worktree or credentials.

## Authority, dispatch and schema

The request contains the complete Contract, all exact entitled source bytes and
original B1 inside the native task. Valid UTF-8 remains exact text, including
CRLF; otherwise `encoding=base64` preserves every byte. Construction is
deterministic, and the stored request string and
`broodling:dotnet:v1:<attempt-id>` key remain immutable across reopen/replay.
Python serialization/IDs/database compatibility are not required.

The existing enclosure marker, provision acknowledgment, canonical paths,
registered linked worktree, branch and common Git directory establish ownership.
Preparation and first dispatch require clean B1 and the supported checkout
profile. Dispatch rechecks the original source origin, immutable admitted
material and current Attempt authority. It acquires C's stable enclosure lock
before the short SQLite writer and releases both before the external SDK call.
No subprocess owns that lock during native execution.

F introduced schema **5**, retained within the fresh `broodling.application` schema, with one
`native_submissions` row per Attempt. Its immutable `format` is `bridge` for a
provisioned worktree Attempt or `http.v1` for an HTTP Attempt; see
[HTTP submission preparation](zeroshot-native-integration.md#http-submission-preparation).
For every format, any state after `prepared` is committed dispatch intent, the
one fact that retirement, replacement, quarantine and unresolved-dispatch
counting read. SQL constraints/triggers protect the request/key and permit only
`prepared → dispatched → correlated|blocked` for bridge rows. An `http.v1` row
permits only `prepared → dispatched → correlated`, with `run_id` equal to its
immutable `intended_run_id`. Its `replay_blocked_reason = submission_conflict`
may be recorded once while dispatched or correlated and never cleared; it blocks
further sends without settling dispatch (see
[HTTP dispatch](zeroshot-native-integration.md#http-dispatch-and-acknowledgement)). A
cross-row guard refuses an intended ID that another Attempt's confirmed `run_id`
names, and the reverse. The completion trigger accepts either format's exact
source binding. Preparation and dispatch require
current authority. Correlation deliberately does not: an acknowledgment arriving
after abandonment retains factual run identity, commits it, then raises
`StaleAttempt`. It never restores authority. Once authority is lost, an
uncorrelated invocation cannot replay to discover a run.

The dispatch-intent transaction commits before crossing the transport. No SQLite
writer spans version probing or the external submission. Concurrent callers can
submit identical requests; native submission-key idempotency owns duplicate
prevention. Correlation requires their acknowledged run identities to converge.
The frozen key is passed unchanged on every replay, so a caller returning after
correlation cannot create distinct native work; a different acknowledged identity
is rejected as `SubmissionConflict`.
`CancelIssueSubmissionAsync` uses the same authority, not a parallel execution
ledger. Its immediate transaction records the exact submission's immutable
stop/no-stop binding; only a cancellation that owns the last relevant shared
Contract authority also commits abandonment against its exact Attempt before
any native stop. A sibling cancellation records a null binding and leaves the
shared current Attempt available. If an owned cancellation's dispatch callback
later returns a run identity, correlation stores that identity on the abandoned
Attempt and the existing stop handoff uses its retained locator/run pair. No
replay discovers or selects a later replacement. The callable cancellation
result is either the durable cancelled submission or the documented
stop/transport exception; all such failures leave the cancellation and any
abandonment facts inspectable.
`StaleAttempt.NativeStopRequested` is true only when `StopAsync` reached the
native `StopAsync` transport. A missing or ambiguous enclosure, or an unresolved
run, produces `CessationUnconfirmed` before transport and therefore false. A
native transport failure or caller cancellation propagates; physical cessation
remains unconfirmed in every dispatched case. The HTTP late-acknowledgement
handoff is described in
[HTTP dispatch](zeroshot-native-integration.md#http-dispatch-and-acknowledgement).
An empty/blank identity, empty stdout, malformed JSON/envelope, transport loss,
cancellation or caller death leaves durable unresolved dispatch. A genuine
typed conflict with a nonblank run identity becomes `blocked`. A conflict
envelope missing that identity, or carrying a nonstring/blank identity, is
malformed transport and remains replayable. Recovery of a conflict's existing identity
requires unchanged persisted invocation, validated owned source/branch/origin,
and HEAD drift from original B1, checked again after the call. Dirty files alone,
an error message, or a run ID alone cannot establish recovery.

Ordinary open never creates a store; see the
[state lifecycle](dotnet-identity-custody.md) for fresh-state requirements. The fresh schema retains durable
Issue submission persistence, the
[persisted installation pause](dotnet-installation-pause.md),
[RequestBundle capture](dotnet-identity-custody.md#application-api), immutable
Issue submission cancellation facts and retained repository preparation.

## Fixed policy and transport

`NativeProfile` owns the bridge's runtime/profile selection and validation. It
supports only empty effects: LocalTarget/`delivery=none` with Codex/OpenAI, the
standard `software-change` workflow, `gpt-5.6-sol`, medium effort, small size and
execution-scoped sessions. A worktree Attempt refuses an authorized-PR Contract
at admission, and the bridge refuses PR delivery and any `direct` locator. PR
work, its gateway runtime and its frozen source selectors belong to the
[HTTP DirectTarget](zeroshot-native-integration.md#http-submission-preparation).

The SDK is **10.3.0.post1**, its bundled native is **10.3.0**, and Codex is
**0.153.4**. The wheel URL/digest are in
[bridge/requirements.txt](../../src/Broodling/bridge/requirements.txt). C# refuses the
native-binary override and checks both reported SDK/native pins before each SDK
operation. The bridge starts Python with `-I` and a minimal process environment.
Dispatch explicitly blanks the baseline operating/home/config/scratch variables,
then supplies frozen PATH, isolated homes and the three profile connection
variables. It carries no dispatch credentials and never forwards ambient provider
or forge credentials. There is no arbitrary persisted environment/runtime
override. Profile comparison rejects injected credential keys even when their
values are empty. Raw SDK exception text and bridge stderr are excluded from
operator diagnostics.

The local `CodexProfile` validates canonical executable/launcher/homes outside
the candidate, separate empty HOME and auth-only CODEX_HOME, regular nonsymlink
`auth.json`, executable permission and exact CLI version. Like the executable
baseline, ambiguous local replay repeats the initial-home validation; this does
not claim qualification of a real populated post-Codex home. Native state stays
canonical and separate from candidate/shared Git.

`Broodling.Codex` is the C# `codex` launcher. Validation requires that exact
filename and a directory without the PATH separator: native resolves `codex`
through that prepended directory, so a renamed launcher or split search-path
component cannot silently fall through to an ambient executable.
Both launcher and real executable require execute access for the dispatch
identity, checked with Linux libc `faccessat(AT_EACCESS)`, not the presence of
an unrelated owner's/group's execute bit. See the
[Linux access contract](https://man7.org/linux/man-pages/man2/access.2.html).
This is trusted-host preflight, not a guarantee against later host mutation.
It applies the baseline sandbox,
approval, networking, user-config/rules, search, apps/plugins/hooks and notify
policy, refuses app-server probing, preserves native same-execution resume, then
calls libc `execve`. The provider retains that same PID and stdin. There is no
execution supervisor. Frozen launcher identity hashes the apphost, managed
entry point, runtime/dependency manifests and Broodling policy assembly, not
authentication bytes. Local execution still requires a trusted host without
operator-managed effect-capable MCP/extensions.

The launcher builds as self-contained Linux x64 with runtime **10.0.12**, so
native's explicit environment does not depend on ambient `DOTNET_ROOT`. Use its
complete output/publish directory; copying only `codex` is insufficient. The
normal host and application remain .NET 10. Build/publish the launcher separately
with `dotnet publish src/Broodling.Codex --configuration Release`; the
[release guide](../../deployment/README.md#build-a-release-artifact) packages
both complete outputs without installing or deploying them.

The bridge boundary has two narrow roles. `INativeReader` provides `WaitAsync(run)` and
`StatusAsync(run, bound)` for completion and progress. `INativeStopper.StopAsync(run)`
serves stop and cancellation. Read and stop operations depend only on their role.
`INativeTransport` adds `SubmitAsync(requestJson)` to both. Bridge dispatch takes it
because it also stops a run whose acknowledgement arrives after abandonment;
`InvocationTarget.Local` uses it too. HTTP records never use these roles. Read
and stop receive a `NativeRunBinding`: the retained locator, correlated run ID,
frozen title, runtime size and, for HTTP delivery, the frozen repository,
authorized branch and B1 selectors. It carries no credentials, workspace or
adapter settings such as the Python executable, bridge script or Codex launcher.
One internal reader of the retained request supplies that binding and the
delivery, source and custody facts used by dispatch and receipt validation; it
never rewrites the saved bytes. The Python bridge only translates supplied fields
to `Client`, targets, `Preset` and `UniformRuntime`, calls the SDK, and returns its public fields/errors. It owns no
Broodling policy or durable state. Reconnect uses only the frozen SDK version,
canonical local state directory and run identity, with empty explicit SDK
environment. It needs no usable old workspace/profile/credentials.
Unknown runs fail closed. `NativeResult` carries run ID, success, arbitrary JSON
output (including null) and failure unchanged; foreign run identities refuse.
`NativeProgress` carries only the SDK's current phase and the node names of its
active executions.

Cancellation/killing detaches only the bridge process, never the whole process
tree or native work. After the submit request is handed to it, cancellation
detaches the caller while the bridge remains alive awaiting its native submit
child, retaining the initiation lock until that command finishes. The submit
bridge is spawned by `libbroodling_git.so` and inherits the installation
initiation lock description, a read-only store descriptor; its child does not (see
[installation pause](dotnet-installation-pause.md)). The status observation bound
covers its version preflight and SDK read. Cancellation or timeout during preflight
stops before status starts. Once status starts, caller cancellation detaches at
once without killing its bridge; the remaining bound is passed to the SDK so it
can stop its own native command. Explicit native stop is a separate transport
operation.
Neither terminal success nor force-stop grants cleanup authority.

## Thin operator commands

The host accepts these commands without starting HTTP:

```text
submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch|-> <reviewed-issue.json> <producer>
resume <store> <contract-revision-id> [config.json [checkout [revision]]]
wait <store> <attempt-id> [config.json]
stop <store> <attempt-id> <reason> [config.json]
```

`-` explicitly authorizes no effect. The producer normally is `caller`.
`ReviewedIssueProposal` requires the complete acquired issue to match the
operator-reviewed file exactly. The configuration names exactly one target kind:
`{"target": "direct", "directOrigin": ...}`, with an optional absolute
`directRootCertificate`, for authorized PR work, or
`{"target": "local", "pythonExecutable", "stateDirectory", "workspaceRoot"}` plus
all four of `realCodex`, `profileHome`, `codexHome` and `launcher` for
no-effect work. It contains no secrets. A missing or unknown kind, a field of the
other kind, an unknown field or a credential field is refused. The DirectTarget
origin must pass the same rule as `InvocationTarget.Direct`: canonical HTTPS or
literal-loopback HTTP, without user information, path, query or fragment, and
never port 0. The `<checkout>` names the source
repository whose exact revision becomes B1; it is not an execution checkout.
The operator command obtains current PR credentials from its environment and
passes them explicitly to the application. `wait` and `stop` take the same
optional configuration: a LocalTarget record uses its pinned SDK Python, and an
HTTP record uses a Direct configuration's root certificate. The submit, resume and stop handbacks
report each submission only as its status facts (Attempt, format, phase, intended
and confirmed run IDs, replay block); `status` and `history` remain the full
retained-fact inspection surface, including the frozen request. Correlated/rejected/ended resume
requires no configuration file. Status/history keep the existing command shape.
Safe failures point to retained history/status; Ctrl+C returns detached handback.
The callable proposer API remains available for richer source/Contract inputs.

## Validation and next ownership

`NativeDispatchTests` owns real SQLite/Git authority, immutable task, writer
release, late acknowledgment, concurrent convergence/refusal, source-drift
discrimination and correlation rollback. `DispatchProcessTests` SIGKILLs callers
during preparation, after preparation, before the SDK call, after real native
acceptance, during a correlation transaction and after correlation commit.
`NativeTransportTests` owns dedicated corrupt submit responses plus real
released-SDK/native replay/conflict/reconnect/detach/stop/unknown-run/null-output
witnesses, and refusal of a `direct` locator before any contact.

`NativePolicyTests` owns launcher exec/PID/prompt/policy, profile refusal,
ambient suppression, persisted credential-injection refusal and managed launcher
identity. `InvocationTests` keeps composed submit/resume, target selection and
operator checks small. HTTP credential validation and rotation belong to
`HttpDispatchTests`.
All native runs use disposable local controlled providers, no live gateway,
forge, provider account, deployment or evaluation.

[G completion](dotnet-receipt-completion.md) implements completed-Work-Unit
guards, receipt validation, result/disposition retention and application wait.
[H lifecycle](dotnet-retirement-replacement.md) adds abandonment/stop composition,
retirement and explicit replacement in historical schema 7, preserving the prior
definitions; schema 8 adds Issue submission persistence, schema 9 adds the
installation pause boundary, schema 10 adds interruption-safe RequestBundle
capture and immutable bundle-scoped reads, schema 11 adds immutable Issue
submission cancellation facts that bind replay to the original Attempt,
including a durable no-Attempt result, and schema 12 adds service-owned
repository preparation.
The local null-output stable-result gap, dispatched quarantine and independent
operator review requirements remain.

## File and validation handoff

| Owning files | Responsibility |
| --- | --- |
| `src/Broodling/NativeDispatch.cs` | Frozen request, exact task bytes, durable intent, conflict recovery, current authority and correlation |
| `src/Broodling/NativeProfile.cs` | Fixed LocalTarget runtime/target, local profile identity, PR credential policy and bridge locator validation |
| `src/Broodling/NativeTransport.cs`, `src/Broodling/bridge/zeroshot_bridge.py` | Native read/stop roles and combined transport, pinned SDK bridge and locator/run-only result transport for G/H |
| `src/Broodling/NativeBinding.cs` | Retained run binding and the single interpretation of frozen request facts, per format |
| `src/Broodling/HttpSubmission.cs` | HTTP preparation, retained-content validation and the stock request/binding shape |
| `src/Broodling/CodexLauncher.cs`, `src/Broodling.Codex/{Program.cs,Broodling.Codex.csproj}` | C# launcher policy, same-PID exec and self-contained packaging |
| `src/Broodling/Invocation.cs`, `src/Broodling.Host/{InvocationCommands.cs,InvocationConfiguration.cs,Program.cs}` | Callable and thin operator submit/resume/wait/stop and explicit target selection |
| `src/Broodling/{StoreSchema.cs,BroodlingStore.cs,ContractAdmission.cs,WorktreeProvisioning.cs,Errors.cs}` | Schema, retained inspection, materialization guard and typed errors |
| `tests/Broodling.Tests/{NativeDispatchTests.cs,NativeTransportTests.cs,NativePolicyTests.cs,DispatchProcessTests.cs,InvocationTests.cs,NativeFixture.cs,StoreLifecycleTests.cs}` | Owning-boundary and small composed witnesses |
| `tests/Broodling.ProcessWitness/Program.cs` | SIGKILL preparation/dispatch/correlation boundaries |
| `tests/Broodling.Tests/Fixtures/{corrupt-submit.py,slow-codex,inspect-codex,README.md}` | Controlled provider/SDK fixtures and provenance |
| `Broodling.sln`, `src/Broodling/Broodling.csproj`, `tests/Broodling.Tests/Broodling.Tests.csproj` | Launcher build, bridge packaging and test assets |
| `README.md`, `docs/implementation/{dotnet-native-dispatch.md,dotnet-worktree-materialization.md,invocation.md,zeroshot-native-integration.md}`, `tests/README.md` | Current migration API, materialization handoff and evidence/limits |

Validation on 22 September 2026 used SDK 10.0.401, runtime 10.0.12 and TUnit
1.68.17. Commands run with an owned NuGet HTTP cache, and
`BROODLING_TEST_PYTHON=/home/faviann/repos/broodling/.venv/bin/python` selects the
pinned shared Python SDK for this worktree:

- The implementation-agent `dotnet test --solution Broodling.sln` run passed
  **184 tests, 0 failed, 0 skipped** before the operator-boundary repair below.
- The root post-repair run passed **192 tests, 0 failed, 0 skipped** in
  62.984 seconds; the focused invocation run passed all 12 tests.
- `dotnet build Broodling.sln --configuration Release`: **0 warnings, 0 errors**
  in the post-repair run (12.84 seconds).
- `PATH=/home/faviann/repos/broodling/.venv/bin:$PATH python -m pytest tests`:
  **404 passed** in 107.00 seconds; the unchanged Python executable reference.
- `git diff --check`: clean.
- Separate Release publishes of the host and launcher succeeded. Their
  `Broodling.dll` hashes match, and both carry the SDK bridge and native Git
  shim. The published self-contained launcher executed a controlled provider
  under an empty environment without `DOTNET_ROOT`, preserving CRLF stdin.
  This is a packaging check, not a live-provider or installation validation.

This evidence covers controlled local execution and transport, not real
DirectTarget delivery, provider semantic quality or physical cessation.

Root review added the baseline operator refusals for unknown/credential
configuration fields and noncanonical/nonloopback origins, plus safe ordinary
process-error handback. The focused witnesses first reproduced eight accepted
invalid configurations and an escaping synthetic `Win32Exception`; the repair
rejects them before allocation and never emits exception text.

Independent review identified launcher/PATH binding and malformed-conflict
classification defects. Six added cases first reproduced both failures with
the existing 192 tests still passing. Repairs require the exact native-resolved
launcher and treat missing/nonstring/blank conflict run identities as unresolved
transport. A further case covers a launcher directory containing the PATH
separator. The shared application handback predicate also removes duplicated
operator resume policy.
The first repaired candidate passed **199 tests, 0 failed, 0 skipped** in
62.907 seconds and its Release build passed with **0 warnings, 0 errors** in
7.38 seconds.

Fresh repair review then found that an owner-inexecutable `0641` launcher could
pass the any-execute-bit check. Initial dispatch and replay witnesses reproduced
the defect, including benign ambient-PATH fallback. The effective-access repair
passed **201 tests, 0 failed, 0 skipped** in 59.911 seconds. Privileged test runners
use a no-execute-bit case when their identity can legitimately execute `0641`;
the recorded run used ordinary UID 1000 and exercised the owner-permission case.
