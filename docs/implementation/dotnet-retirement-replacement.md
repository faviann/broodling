# .NET stop, safe retirement and explicit replacement

[H #139](https://github.com/faviann/broodling/issues/139) adds lifecycle operations
over B's abandonment, C's owned materialization and F's native transport. The
frozen Python reference is `b3f61a96c40401722ec16fc361958d1690982e02`. The
[release guide](../../deployment/README.md) separates current .NET source support
from the future owner-approved operational switch.

## Application and operator operations

```csharp
using var store = new BroodlingApplication().OpenStore(storePath);
await store.StopAsync(attemptId, "Operator ended this Attempt", transport);
var retirement = store.RetireAttempt(attemptId);
// Dispatched DirectTarget work, only during verified stopped-target maintenance.
var maintained = store.RetireStoppedTargetAttempt(attemptId, stoppedTargetCheck);
// A no-effect worktree predecessor keeps its kind and the SDK bridge.
var successor = store.AdmitRetry(attemptId, retryKey, workspaceRoot, profile);
var prepared = store.PrepareRetry(attemptId, retryKey, workspaceRoot, profile);
var submitted = await store.RetryAsync(attemptId, retryKey, workspaceRoot,
    profile, transport);
// An HTTP predecessor's successor is prepared and dispatched as an HTTP Attempt.
var httpSuccessor = store.AdmitRetry(httpAttemptId, retryKey);
```

These are explicit operations, not an automatically executed sequence. Each
store session belongs to one caller; concurrent callers open separate sessions.
`StopAsync` returns an `AttemptRetirement` only for retained safe cessation.
Dispatched work throws `CessationUnconfirmed` after native stop, or propagates
native transport/cancellation failure. Abandonment has already committed and
cannot be reversed. `FindRetirement` and `FindRetry` inspect durable facts without
Git/native access. `AttemptRecord.Retirement` and `.Retry` expose them through
status/history.

`CancelIssueSubmissionAsync(submissionId, reason, transport, token)` is the
callable submission-level handback. It commits the immutable cancellation fact
and, when this exact cancellation owns the last relevant shared Contract
authority, abandonment of that fact's exact Attempt before entering this stop
path. Its nullable Attempt binding is the immutable stop/no-stop decision for
the submission, not a replacement for the shared Contract's derived Attempt
lineage; a sibling cancellation therefore leaves the current Attempt alone. A
no-stop or safely undispatched cancellation returns the cancelled
`IssueSubmission`; a dispatched Attempt may return
`CessationUnconfirmed`, `SubmissionNotReady` when a known run has no stop
transport, `NativeTransportError`, or `OperationCanceledException`. These
outcomes retain the cancellation/abandonment facts and neither means physical
cessation nor grants replacement authority.

The thin host command is `stop <store> <attempt-id> <reason> [config.json]`.
It returns the exact Attempt, a compact submission summary, the quarantine flag
and safe handback even when stop fails. Cessation refusal returns exit 1;
cancellation returns 130 and retains abandonment. The configuration is the
invocation `config.json`, but stop uses neither its dispatch settings nor
credentials. A LocalTarget configuration selects the pinned SDK bridge, and only
a LocalTarget record needs it. An HTTP record stops through its retained binding,
using a Direct configuration only for its root certificate. Safe undispatched
stop needs no functioning SDK. Without a LocalTarget configuration, a dispatched
LocalTarget record is refused before abandonment with `python_required`, because
its native stop could not be requested.
Submit still reacquires/proposes its explicit issue, then hands back abandonment;
resume/status/history inspect retained facts without automatic replacement.
`QuarantinedAttemptIds` identifies every dispatched Attempt, even if current,
until a verified maintenance retirement below lifts that cleanup limitation. It
is not native execution status. The stop command's `quarantined` flag follows it.

## Stop and retirement authority

Abandonment commits before host inspection or native stop; its first reason wins.
Stop checks physical allocation, enclosure marker and acknowledged enclosure
presence, but requires no live checkout, live Git inspection or dispatch
configuration. Known runs use only frozen locator and retained run identity.
Unknown dispatched identity is never discovered by replay. Success,
`force_stopped`, `runtime_lost`, transport failure and cancellation grant no
retirement/replacement authority. Native labels are not cessation receipts.

An HTTP (`http.v1`) record routes on its retained format, ignores any bridge
transport and needs no enclosure, credentials or source custody. A prepared
record makes no target contact and keeps the `no_dispatch_intent` path below.
Dispatch intent uses one 30-second
[DirectTarget stop](zeroshot-native-integration.md#directtarget-run-status-reader).
A correlated record forces its confirmed run. A dispatched but unacknowledged
record first reads its intended run ID. Force is sent only when the projection
matches the retained ID, title, repository, branch, B1 and size. Neither the
read nor the force reply establishes correlation. Outcomes map to the existing
surface:

| DirectTarget outcome | Result after committed abandonment |
| --- | --- |
| Terminal result from force or its polling | `CessationUnconfirmed` with `NativeStopRequested = true`; still no physical cessation proof |
| No force sent (unknown, foreign, malformed or unavailable precheck; setup failure) | `CessationUnconfirmed` with `NativeStopRequested = false` and the fixed kind in its message |
| Force possibly sent, outcome uncertain (timeout, transport loss, malformed reply) | `NativeTransportError` with the fixed kind, such as `TimeoutError` |
| Caller cancellation | `OperationCanceledException` |

Force is never reissued automatically. A later explicit Stop may address an
intended run that was unknown earlier, for example after delayed acceptance.
Every outcome leaves dispatch intent quarantined; only the verified maintenance
retirement below can end that.
`StopAsync(attemptId, reason, transport: null)` is also the late-acknowledgement
stop path for abandoned HTTP work: abandonment is idempotent, and routing follows
the retained record. The thin host `stop` command still selects the bridge;
HTTP operator routing follows separately.

Safe proof requires submission absent or merely prepared, and one of:

- no enclosure and no provisioning acknowledgment (`never_materialized`);
- an owned existing enclosure and provisioning acknowledgment (`never_dispatched`);
- an HTTP resource-kind Attempt (`no_dispatch_intent`), which owns no local
  resource to inspect.

A marker alone, an actual checkout without durable acknowledgment, or a missing
acknowledged enclosure cannot establish safe cessation. The HTTP proof rests on
abandonment plus the absence of any committed dispatch intent, checked under the
same writer; a missing directory, unknown run or failed request never supplies
it. SQL ties each basis to its resource kind. Proof commits under the
SQLite writer after abandonment excludes queued provisioners/new dispatch.
An orphan provisioner's unacknowledged host state remains ambiguous and refused.

Retirement rechecks retained proof under the writer. It validates physical paths,
original common Git, marker, registration and assigned branch, refusing branches
attached elsewhere, symbolic aliases, foreign Git and unregistered extant paths.
All ownership checks precede destructive Git. The exact safe dirty checkout may
be forcibly removed, followed by its assigned branch. Source/siblings, enclosure,
marker, stable lock inode and store remain. An absent never-materialized enclosure
needs no mutation; unexpected Git state there refuses acknowledgment.

C's stable enclosure lock precedes the SQLite writer. The exact Git child
inherits that same lock description; parent disposal never unlocks it. Retirement
acknowledgment follows both administrative commands. Caller death after removal
loses acknowledgment; replay recognizes missing owned path/branch and finishes.
A surviving Git child blocks followers before settled inspection/acknowledgment.
There is no execution supervisor or native cleanup mechanism. Retiring an HTTP
Attempt only acknowledges its retained proof under the writer: no filesystem or
Git mutation, and B1/accepted pins, source and history remain.

## Verified maintenance retirement

[#122](https://github.com/faviann/broodling/issues/122) adds the only path that
retires dispatched work, for HTTP DirectTarget Attempts during host maintenance.
The host procedure ([homelab-iac#356](https://github.com/faviann/homelab-iac/issues/356))
pauses the installation, drains and quiesces Broodling, stops the target, verifies
the correct target and its state mounts are stopped and keeps them stopped. It then
runs the image command once per Attempt with a check it made during this pause:

```bash
dotnet /RELEASE/host/Broodling.Host.dll retire-attempt /EXISTING/DOTNET/state.sqlite3 ATTEMPT_ID \
  '{"directOrigin":"https://zeroshot.dev.faviann.com","containerName":"broodling-zeroshot-1","stateMount":"/NEW/target-state","homeMount":"/NEW/target-home","verifiedAt":"2026-09-25T12:00:00Z"}'
```

`RetireStoppedTargetAttempt(attemptId, StoppedTargetCheck)` records every member
as supplied and refuses a blank one; like `check-target`, the command parser
requires every member, refuses nulls and accepts nothing else. Under one SQLite writer it
requires the persisted pause, `verifiedAt` no earlier than the latest pause call
and no later than now (host and application share a clock), the check's origin
equal to the Attempt's retained binding origin, a drained initiation lock, a
non-current HTTP Attempt (abandoned or completed) with dispatch intent, and its
B1 pin and any accepted pin, read from Git while the writer is held. Drainage is an additional condition, never authorization: a sender that
committed intent before the stop holds that lock until its send returns.
Every `PauseInstallation` call, even while already paused, refreshes the pause
time that `verifiedAt` is compared with. The host contract is therefore: each maintenance invocation starts by calling
pause, then verifies the target, then retires. A check from an earlier invocation,
interrupted or not, or from before a release and re-pause, is then refused.
This epoch rests on the host clock, which the host and application share, not
stepping backwards across maintenance invocations.
Pause/check/drainage refusals are `maintenance_unverified`; ineligible Attempts
are `cessation_unconfirmed`. A native terminal label, a stop result, local
drainage or a missing directory grants nothing on its own.

Success inserts basis `stopped_target`, `ceased_at` (`verifiedAt` in UTC), the check JSON
and `retired_at` in one transaction. SQL ties that basis to a non-current HTTP
Attempt with dispatch intent while paused. Nothing is deleted: an HTTP Attempt
owns no Broodling worktree, and Zeroshot's checkout and ledger are native state.
Frozen request/asset, B1 and accepted pins, receipt and completion stay; a
completed Attempt is retired without abandonment; a later `stop` of any retired
Attempt returns its retirement without abandoning it or contacting the target. An uncorrelated submission
stays `dispatched` and counted in `unresolvedDispatches`. An already
acknowledged retirement (`retired_at` set) is returned unchanged on repeat,
whatever its basis, so the host procedure must check the returned `basis` rather
than treat exit 0 as its own verified retirement. An unacknowledged safe proof
goes through the normal checks, which refuse it. Replacement still refuses dispatched
predecessors; widening it belongs to #123. LocalTarget worktree Attempts keep the
policy above.

This is safe because the pinned Zeroshot ends every non-terminal run as
`runtime_lost` before serving anything when restarted over the same ledger,
never reallocating it, and has no queue of accepted-but-unstarted runs
(`zeroshot/src/native_v2_cloud.rs:136`, `:148-170`, `:206-245` at
[`054ad3f`](https://github.com/the-open-engine/zeroshot/tree/054ad3fd6c763b98d12f5b2e90830b97116561ad)).
That holds only if the restarted target mounts the same ledger, which #356 checks.
The drainage condition covers local senders only; the single-host loopback
sender assumption depends on the unresolved topology in #155.

## Explicit replacement and schema

New retry requires abandonment, completed safe retirement and no competing
current Attempt. It validates original object custody and source bytes, never
resolving today's HEAD or original requested spelling anew, and never salvaging
candidate edits. The successor preserves Work Unit, Contract and original B1/
source bindings with a new branch/enclosure/worktree. One SQLite transaction
retains its key, lineage, chosen root/full target and allocation before host setup.
A deferred FK prevents lineage-only commits; triggers check safe predecessor,
original bindings and allocation. Same key/parameters converge across callers
and reopen. Changed predecessor/root/target or a second predecessor key refuses.
An old key returns its historical successor after replacement without reviving
authority. A successor keeps its predecessor's resource kind: an HTTP
predecessor takes `AdmitRetry(predecessorId, retryKey)` and its successor also
has no local directory. Its retry records no root or target; the successor's own
`PrepareHttpSubmission` freezes its target binding and a new intended run ID,
also while paused. A prepared-only HTTP record keeps the `no_dispatch_intent`
basis; any later phase quarantines. Provision/prepare/dispatch independently guard currentness. Direct
`PrepareSubmission` enforces the frozen retry target too. Dispatched retry
recovery reuses F's correlation seam without reprovisioning candidate material.

H integrates with G in historical schema **7**, retaining the exact G schema-6
DDL and all v1–v6 definition hashes. Issue-submission persistence extends the
current schema to **8**, retaining those definitions; schema **9** adds
the persisted installation pause gate, schema **10** adds RequestBundle capture,
schema **11** adds immutable Issue submission cancellation facts, and schema
**12** adds service-owned repository preparation. The fresh
`broodling.application` schema retains these definitions
([state lifecycle](dotnet-identity-custody.md)). Safe
replacement allocation and preparation remain permitted while paused, but
replacement dispatch still requires explicit release.
Retirement/retry facts resist update, delete and `INSERT OR REPLACE`; SQL refuses
dispatched cleanup authority outside the `stopped_target` conditions and
missing/changed retry submission targets.
No Python database/import compatibility was added. G's
completed-Work-Unit refusal remains in admission/retry/API/SQL, with completed-Attempt
abandonment refusal, factual current-authority-loss guard and Attempt
`INSERT OR REPLACE` protection. H replaces only
the ordinary abandoned-work insertion guard with the safe-retry exception.

## Evidence and limits

`RetirementTests` owns safe/ambiguous proof, stop ordering and all dispatched
outcomes, ownership refusals, dirty deletion, lock inode and lifecycle SQL, plus
verified maintenance retirement: abandoned unresolved/correlated and successful
HTTP Attempts, each pause/check/drainage/currentness/retention refusal, the
LocalTarget refusal and the `retire-attempt` command.
`ReplacementTests` owns original material, atomic allocation, same-key
concurrency, historical replay, target enforcement and SQL binding refusals.
`ReplacementCompletionTests` checks the integrated completed-Work-Unit refusal
at API and SQL boundaries, plus completed retry-key identity handback without
renewed authority. Its historical seed bypasses only ordinary admission while
constructing the fixture; every guard is restored before testing safe retry.
`RetirementProcessTests` drives real SQLite/Git with the test-only caller: SIGKILL
before/after retirement removal, orphan exclusion, premature retry refusal and
retry allocation/preparation transaction deaths. `InvocationTests` adds one
composed stop/quarantine/abandonment handback.

Before G integration, on 22 September 2026, `dotnet test --solution Broodling.sln` passed **238 tests,
0 failed, 0 skipped**, using SDK 10.0.401, runtime 10.0.12 and TUnit 1.68.17.
`BROODLING_TEST_PYTHON=/home/faviann/repos/broodling/.venv/bin/python` selects the
pinned SDK. Build-node reuse/shared compilation were disabled to avoid reusing
another sandbox's build processes. Test roots are owned disposable subdirectories
of `/home/faviann/.cache/broodling-tests`.

`dotnet build Broodling.sln --configuration Release` passed with **0 warnings,
0 errors**. The unchanged executable reference also passed:
`PATH=/home/faviann/repos/broodling/.venv/bin:$PATH python -m pytest tests` —
**404 passed in 127.51s**. The .NET invocation used
`MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false`;
an earlier invocation failed on sandbox-incompatible reused build nodes before
the successful complete run.

Integration onto G's exact merge `cacc268708d2785bb9344c3bf2793447ccfe0138`
preserved every v1–v6 SQL definition, G's authentic prepared v5 fixture, both
host wait/stop branches, completion handback and quarantine inspection. H's
ended-work SQL guard retains its safe-retry exception; G's completed-work guard
remains unconditional. G's two-result historical fixture now temporarily
bypasses both ordinary-admission guards during seeding, then restores them.

The first two-test integration run reproduced one failure: SQL correctly
refused completed-work retry, but the API exposed `SqliteException` instead of
`StaleAttempt`. The application now shares G's completed-work check for new
retry requests, after historical-key handback and before material validation or
allocation. Both integration tests then passed in 3.992 seconds. That focused
filter ran only those two tests; upgrade coverage comes from the full suite.

Final integration validation on the rebased H plus uncommitted repairs, using
the same environment above:

- `dotnet test --solution Broodling.sln`: **257 passed, 0 failed, 0 skipped**,
  30.241 seconds, including authentic v1–v6 upgrades and both integration tests.
- `dotnet build Broodling.sln --configuration Release`: **0 warnings, 0 errors**,
  22.47 seconds.
- Unchanged Python reference: **404 passed**, 120.48 seconds.
- `git diff --check`: clean. G5/G6 SQL fixture hashes match their provenance;
  every G v1–v6 SQL definition is unchanged.

The conflict-only candidate had passed 254 tests in 26.147 seconds and Release
in 26.34 seconds. A pre-final integrated run passed 257 tests in 26.974 seconds
with one test assertion style warning, corrected before the final clean run.
G's earlier unexplained Git/provider failures and bounded nonrecurrence evidence
remain in its [completion record](dotnet-receipt-completion.md); these integration
passes establish neither their cause nor a repair.

Fresh full integrated review of `4039f41ffbf19ec1ba8400c14160be8dd869ad41`
passed both axes: Standards reported three nonblocking P3 duplication
observations; Spec reported zero actionable findings. A bounded cleanup shares
only the admitted-material fingerprint, temporary-root predicate and existing
direct-branch inspection. Caller errors, transactions, digest bytes and Git
ordering remain unchanged. Short source-byte checks stay local because dispatch
also builds instructions and has a distinct exception classification.
After that cleanup, the full suite passed **257 tests, 0 failed/skipped**, in
25.426 seconds; Release passed with **0 warnings/errors** in 26.05 seconds.
Schema, tests and Python were unchanged; Python was not rerun for this cleanup.
Fresh separate repair reviews of `6ac0c4f1cdedde06f44696119a0e41a887acdf93`
both passed: **Standards 0 findings; Spec 0 findings**. Their bounded inspection
covered all changed production paths and callers, not a rerun of the suites.
The final evidence-only edit consolidates duplicate H map prose and records
these outcomes; production remains identical to that reviewed candidate.

Controlled transport/PR receipt stubs do not establish real DirectTarget PR
delivery. No live gateway/GitHub mutation/provider/deployment/evaluation or
production-state operation occurred. The native no-effect stable-result gap and
dispatched quarantine remain. The [P5 human-review limit](../governing/current.md#first-use-limitation)
still governs every proposed PR; green checks certify neither semantic quality
nor permission to merge.
