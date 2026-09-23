# .NET frozen native dispatch

[F #137](https://github.com/faviann/broodling/issues/137) composes the existing
acquisition, admission, Attempt allocation and
[owned materialization](dotnet-worktree-materialization.md) with the pinned
Zeroshot SDK. The [release guide](../../deployment/README.md) describes current
packaging and the separate operational cutover gate. The frozen parity baseline is
`b3f61a96c40401722ec16fc361958d1690982e02`. The #130 spike is protocol evidence,
not an adopted API or a claim of real PR delivery.

## Callable application

```csharp
using var store = new BroodlingApplication().OpenStore(databasePath);
var profile = new NativeProfile(nativeStateDirectory, directOrigin: directTargetOrigin);
var transport = new ZeroshotTransport(pythonExecutable);
var invocation = new Invocation(store, workspaceRoot, profile, transport);
var credentials = new DispatchCredentials(githubToken, gatewayBaseUrl, gatewayApiKey);
var status = await invocation.SubmitAsync(reference, propose,
    [new RequiredEffect("pr", "Deliver a PR to main.", "pull_request", "main")],
    repositoryPath, revision: originalCommit, credentials: credentials);
// Retain status.Revision.ContractRevisionId and status.Attempts.Single().AttemptId.
var resumed = await invocation.ResumeAsync(status.Revision.ContractRevisionId,
    credentials: credentials);
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

`Status` and `History` now include `Submissions`, alongside the existing exact
revision's sources, Contract, decision and Attempts. They remain coherent,
deferred SQLite reads without native access. Exceptions do not undo earlier
commits: use history to find the exact revision after a failed submit.

`PrepareSubmission(attemptId, profile)` freezes the request independently of
dispatch. `DispatchAsync(attemptId, profile, transport, credentials, token)`
prepares if necessary and reconciles that same invocation. `FindSubmission`
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

F introduced schema **5**, retained within the current schema **8**, with one
`native_submissions` row per provisioned Attempt. SQL
constraints/triggers protect the request/key and permit only
`prepared → dispatched → correlated|blocked`. Preparation and dispatch require
current authority. Correlation deliberately does not: an acknowledgment arriving
after abandonment retains factual run identity, commits it, then raises
`StaleAttempt`. It never restores authority. Once authority is lost, an
uncorrelated invocation cannot replay to discover a run.

The dispatch-intent transaction commits before crossing the transport. No SQLite
writer spans version probing or the external submission. Concurrent callers can
submit identical requests; native submission-key idempotency owns duplicate
prevention. Correlation requires their acknowledged run identities to converge.
An empty/blank identity, empty stdout, malformed JSON/envelope, transport loss,
cancellation or caller death leaves durable unresolved dispatch. A genuine
typed conflict with a nonblank run identity becomes `blocked`. A conflict
envelope missing that identity, or carrying a nonstring/blank identity, is
malformed transport and remains replayable. Recovery of a conflict's existing identity
requires unchanged persisted invocation, validated owned source/branch/origin,
and HEAD drift from original B1, checked again after the call. Dirty files alone,
an error message, or a run ID alone cannot establish recovery.

Ordinary open never creates or upgrades a store. Explicit upgrade recognizes
the unchanged definition hashes for schemas 1–6 and applies missing migrations in one
transaction. The authentic pre-F schema-4 fixture retains all prior records,
including first provisioning acknowledgment and abandonment. Upgrade invents no
past dispatch. Its provenance and exact hashes are in the
[fixture record](../../tests/Broodling.Tests/Fixtures/README.md).

## Fixed policy and transport

`NativeProfile` owns runtime/profile selection and validation. Empty effects
select LocalTarget/`delivery=none`, with Codex/OpenAI. One authorized GitHub PR
selects DirectTarget/`delivery=pull_request`, with Codex/gateway. Both use the
standard `software-change` workflow, `gpt-5.6-sol`, medium effort, small size,
execution-scoped sessions. PR source selectors are the frozen owner/repository,
authorized target branch and original B1, never the synthetic Attempt branch.
The DirectTarget origin is distinct from the gateway URL.

The SDK is **10.3.0.post1**, its bundled native is **10.3.0**, and Codex is
**0.153.4**. The wheel URL/digest are in
[bridge/requirements.txt](../../src/Broodling/bridge/requirements.txt). C# refuses the
native-binary override and checks both reported SDK/native pins before each SDK
operation. The bridge starts Python with `-I` and a minimal process environment.
Dispatch explicitly blanks the baseline operating/home/config/scratch variables,
then supplies frozen PATH and, locally, isolated homes and the three profile
connection variables. It never forwards ambient provider or forge credentials.

Current nonblank `GH_TOKEN` and `GATEWAY_API_KEY` (maximum 4096 characters) and
exactly `https://cliproxy.local.faviann.com/v1` are required for PR dispatch and
ambiguous replay. They enter the transport separately, never the stored request.
Rotation changes only ephemeral inputs. There is no arbitrary persisted
environment/runtime override. Profile comparison rejects injected credential
keys even when their values are empty. Raw SDK exception text and bridge stderr
are excluded from operator diagnostics.

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

`INativeTransport` exposes `SubmitAsync(requestJson, credentials)`,
`WaitAsync(locator, runId)` and `StopAsync(locator, runId)` for G/H. The Python
bridge only translates supplied fields to `Client`, targets, `Preset` and
`UniformRuntime`, calls the SDK, and returns its public fields/errors. It owns no
Broodling policy or durable state. Reconnect uses only the frozen SDK version,
canonical local state directory or direct origin, and run identity, with empty
explicit SDK environment. It needs no usable old workspace/profile/credentials.
Unknown runs fail closed. `NativeResult` carries run ID, success, arbitrary JSON
output (including null) and failure unchanged; foreign run identities refuse.

Cancellation/killing detaches only the bridge process, never the whole process
tree or native work. Explicit native stop is a separate transport operation.
Neither terminal success nor force-stop grants cleanup authority.

## Thin operator commands

The host accepts these commands without starting HTTP:

```text
submit <store> <config.json> <repository> <issue> <checkout> <revision> <target-branch|-> <reviewed-issue.json> <producer>
resume <store> <contract-revision-id> [config.json [checkout [revision]]]
```

`-` explicitly authorizes no effect. The producer normally is `caller`.
`ReviewedIssueProposal` requires the complete acquired issue to match the
operator-reviewed file exactly. Configuration contains `pythonExecutable`,
`stateDirectory`, `workspaceRoot`, and either `directOrigin` or the local
`realCodex`, `profileHome`, `codexHome`, `launcher` paths. It contains no secrets.
Unknown configuration fields, including credential fields, are refused. The
operator's DirectTarget origin must be exactly `http://127.0.0.1:<port>` with an
explicit port from 1 through 65535, without user information, path, query or
fragment. This preserves the installed loopback profile; the callable API is
not restricted to that installation configuration.
The operator command obtains current PR credentials from its environment and
passes them explicitly to the application. Correlated/rejected/ended resume
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
witnesses. Its seven-field PR receipt and other JSON output assertions use a
precise stub SDK: **they do not establish real DirectTarget PR delivery**.

`NativePolicyTests` owns launcher exec/PID/prompt/policy, profile refusal,
ambient suppression, credential rotation, managed launcher identity and local
native gateway profile expansion. The expansion uses no target or provider.
`InvocationTests` keeps composed submit/resume and operator checks small.
Schema lifecycle tests preserve every retained old fact and ordinary-open
refusal. All native runs use disposable local controlled providers, no live
gateway, forge, provider account, deployment or evaluation.

[G completion](dotnet-receipt-completion.md) implements completed-Work-Unit
guards, receipt validation, result/disposition retention and application wait.
[H lifecycle](dotnet-retirement-replacement.md) adds abandonment/stop composition,
retirement and explicit replacement in schema 7, preserving the prior definitions.
The local null-output stable-result gap, dispatched quarantine and independent
operator review requirements remain.

## File and validation handoff

| Owning files | Responsibility |
| --- | --- |
| `src/Broodling/NativeDispatch.cs` | Frozen request, exact task bytes, durable intent, conflict recovery, current authority and correlation |
| `src/Broodling/NativeProfile.cs` | Fixed runtime/target, local profile identity, credential policy and locator validation |
| `src/Broodling/NativeTransport.cs`, `src/Broodling/bridge/zeroshot_bridge.py` | Pinned SDK bridge and locator/run-only result transport for G/H |
| `src/Broodling/CodexLauncher.cs`, `src/Broodling.Codex/{Program.cs,Broodling.Codex.csproj}` | C# launcher policy, same-PID exec and self-contained packaging |
| `src/Broodling/Invocation.cs`, `src/Broodling.Host/{InvocationCommands.cs,Program.cs}` | Callable and thin operator submit/resume |
| `src/Broodling/{StoreSchema.cs,BroodlingStore.cs,ContractAdmission.cs,WorktreeProvisioning.cs,Errors.cs}` | Schema 5/upgrades, retained inspection, materialization guard and typed errors |
| `tests/Broodling.Tests/{NativeDispatchTests.cs,NativeTransportTests.cs,NativePolicyTests.cs,DispatchProcessTests.cs,InvocationTests.cs,NativeFixture.cs,StoreLifecycleTests.cs}` | Owning-boundary and small composed witnesses |
| `tests/Broodling.ProcessWitness/Program.cs` | SIGKILL preparation/dispatch/correlation boundaries |
| `tests/Broodling.Tests/Fixtures/{dotnet-v4.sql,corrupt-submit.py,receipt-sdk.py,gateway-profile.py,slow-codex,inspect-codex,README.md}` | Authentic schema 4, controlled provider/SDK fixtures and provenance |
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
