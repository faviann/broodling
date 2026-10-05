# .NET stop, safe retirement and explicit replacement

[H #139](https://github.com/faviann/broodling/issues/139) adds lifecycle operations
over B's abandonment and native stop. The
frozen Python reference is `b3f61a96c40401722ec16fc361958d1690982e02`. The
[release guide](../../deployment/README.md) separates current .NET source support
from the future owner-approved operational switch.

## Application and operator operations

```csharp
using var store = new BroodlingApplication().OpenStore(storePath);
await store.StopAsync(attemptId, "Operator ended this Attempt");
var retirement = store.RetireAttempt(attemptId);
// Dispatched work, only during verified stopped-target maintenance.
var maintained = store.RetireStoppedTargetAttempt(attemptId, stoppedTargetCheck);
// The successor is admitted from original B1 and owns no local directory.
var successor = store.AdmitRetry(attemptId, retryKey);
// Admit and prepare it at the predecessor's retained origin; never dispatches.
var prepared = store.PrepareRetry(attemptId, retryKey);
```

These are explicit operations, not an automatically executed sequence. Each
store session belongs to one caller; concurrent callers open separate sessions.
`StopAsync` returns an `AttemptRetirement` only for retained safe cessation.
Dispatched work throws `CessationUnconfirmed` after native stop, or propagates
native transport/cancellation failure. Abandonment has already committed and
cannot be reversed. `FindRetirement` and `FindRetry` inspect durable facts without
Git/native access. `AttemptRecord.Retirement` and `.Retry` expose them through
status/history.

`CancelIssueSubmissionAsync(submissionId, reason, token)` is the
callable submission-level handback. It commits the immutable cancellation fact
and, when this exact cancellation owns the last relevant shared Contract
authority, abandonment of that fact's exact Attempt before entering this stop
path. Its nullable Attempt binding is the immutable stop/no-stop decision for
the submission, not a replacement for the shared Contract's derived Attempt
lineage; a sibling cancellation therefore leaves the current Attempt alone. A
no-stop or safely undispatched cancellation returns the cancelled
`IssueSubmission`; a dispatched Attempt may return
`CessationUnconfirmed`, `NativeTransportError` or `OperationCanceledException`. These
outcomes retain the cancellation/abandonment facts and neither means physical
cessation nor grants replacement authority.

The thin host command is `stop <store> <attempt-id> <reason> [config.json]`.
It returns the exact Attempt, a compact submission summary, the quarantine flag
and safe handback even when stop fails. Cessation refusal returns exit 1;
cancellation returns 130 and retains abandonment. The configuration is the
invocation `config.json`; stop uses only its root certificate, never
credentials, and stops the record through its retained binding. Safe
undispatched stop makes no target contact.
Submit still reacquires/proposes its explicit issue, then hands back abandonment;
resume/status/history inspect retained facts without automatic replacement.
`QuarantinedAttemptIds` identifies every dispatched Attempt, even if current,
until a verified maintenance retirement below lifts that cleanup limitation. It
is not native execution status. The stop command's `quarantined` flag follows it.

## Stop and retirement authority

Abandonment commits before native stop; its first reason wins. Stop requires
no Git inspection, credentials or dispatch configuration. A known run is
addressed only through its retained binding and run identity. Unknown dispatched
identity is never discovered by replay. Success,
`force_stopped`, `runtime_lost`, transport failure and cancellation grant no
retirement/replacement authority. Native labels are not cessation receipts.

A prepared record makes no target contact and keeps the `no_dispatch_intent`
path below.
Dispatch intent uses one 30-second
[DirectTarget stop](zeroshot-native-integration.md#directtarget-run-reader-and-stopper).
A correlated record forces its confirmed run. A dispatched but unacknowledged
record first reads its intended run ID. Force is sent only when the projection
matches the retained ID, title, repository, branch, B1 and size. Neither the
read nor the force reply establishes correlation. Outcomes map to the existing
surface:

| DirectTarget outcome | Result after committed abandonment |
| --- | --- |
| Terminal result from force or the wait that follows it | `CessationUnconfirmed` with `NativeStopRequested = true`; still no physical cessation proof |
| No force sent (unknown, foreign, malformed or unavailable precheck; setup failure) | `CessationUnconfirmed` with `NativeStopRequested = false` and the fixed kind in its message |
| Force possibly sent, outcome uncertain (timeout, transport loss, malformed reply) | `NativeTransportError` with the fixed kind, such as `TimeoutError` |
| Caller cancellation | `OperationCanceledException` |

Force is never reissued automatically. A later explicit Stop may address an
intended run that was unknown earlier, for example after delayed acceptance.
Every outcome leaves dispatch intent quarantined; only the verified maintenance
retirement below can end that.
`StopAsync(attemptId, reason)` is also the late-acknowledgement stop path for
abandoned work: abandonment is idempotent, and routing follows the retained
record.

Safe proof (`no_dispatch_intent`) requires abandonment and a submission that is
absent or merely prepared. An Attempt owns no local resource to inspect. The
proof rests on abandonment plus the absence of any committed dispatch intent,
checked under the SQLite writer after abandonment commits, so no new dispatch
can start; a missing directory, unknown run or failed request never supplies it.
SQL accepts only the `no_dispatch_intent` and `stopped_target` bases.

Retirement rechecks the retained proof under the writer and acknowledges it. It
mutates no file or Git state: B1 and accepted pins, source and history remain.
There is no execution supervisor or native cleanup mechanism.

## Verified maintenance retirement

[#122](https://github.com/faviann/broodling/issues/122) adds the only path that
retires dispatched work, for HTTP DirectTarget Attempts during host maintenance.
The host procedure ([homelab-iac#356](https://github.com/faviann/homelab-iac/issues/356))
pauses the installation, drains and quiesces Broodling, stops the target, verifies
the correct target and its state mounts are stopped and keeps them stopped. It then
runs the command once per Attempt with a check it made during this pause. A CLI
Attempt uses the release artifact's command
(`dotnet /RELEASE/host/Broodling.Host.dll retire-attempt …`), which reaches the
caller checkout's common Git directory. For an Attempt that the processing server
created (#205) it is the Broodling image's own command against the stopped
application's store:

```bash
docker compose run --rm --no-deps broodling retire-attempt /var/lib/broodling/state.sqlite3 ATTEMPT_ID \
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
goes through the normal checks, which refuse it.

An abandoned Attempt retired this way can then be replaced (#123), still within
the maintenance pause, with the same image or release command:

```bash
docker compose run --rm --no-deps broodling replace-attempt /var/lib/broodling/state.sqlite3 PREDECESSOR_ATTEMPT_ID RETRY_KEY
```

It calls `PrepareRetry(predecessorId, retryKey)`, which admits the successor and
prepares its submission at the predecessor's retained origin, here the one the
check named, and prints that prepared submission (`attemptId`, `intendedRunId`,
`state`). Repeating it with the same key prints the successor's existing
submission. A predecessor without a retained origin is refused before anything is
admitted. The command also replaces an HTTP predecessor retired with
`no_dispatch_intent`, which needs no pause. For a `stopped_target` predecessor,
both the successor's admission and its first preparation require the
persisted pause, checked under the writer (`maintenance_unverified` otherwise), so
a successor admitted before a release cannot be prepared, by any caller, until the
installation is paused again. The command never dispatches. After
`release-installation`, Resume dispatches the successor through the ordinary
gate: for a CLI Attempt the release artifact's `resume`, and for a
processing-server Attempt the image's `resume`, run as the processing service
beside the restarted server:

```bash
docker compose run --rm --no-deps broodling resume /var/lib/broodling/state.sqlite3 CONTRACT_REVISION_ID /etc/broodling/invocation.json
```

Resume takes the Contract revision's latest Attempt, the successor, returns its
retained preparation at the configured origin (refusing a different one), checks
its retained B1 custody at the recorded container path and sends the prepared request with the service's current credentials,
under the same pause check and initiation lock as any dispatch. Nothing else
dispatches it: automatic progression never selects a Replacement Attempt or its
submission, and the server's resume route never continues one, so running the
command while the server runs cannot double-dispatch. Once the successor is
correlated, the server's completion observation consumes it like any correlated
current HTTP Attempt. Dispatch the successor rather than stopping it while
prepared: its `no_dispatch_intent` proof would never be acknowledged (nothing
calls `RetireAttempt`), so it could not be replaced; the predecessor already has
its one successor; and a revision with unchanged inputs ends `unchanged`.
A completed Attempt cannot be replaced.

This is safe because the pinned Zeroshot ends every non-terminal run as
`runtime_lost` before serving anything when restarted over the same ledger,
never reallocating it, and has no queue of accepted-but-unstarted runs
(`zeroshot/src/native_v2_cloud.rs:146-162`, `:173-193` at
[`3ee1192c`](https://github.com/the-open-engine/zeroshot/tree/3ee1192cec359a0b997f464e703a936e8b67d63c)).
A run still preparing its environment after acknowledgement is non-terminal and
ends the same way.
That holds only if the restarted target mounts the same ledger, which #356 checks.
The drainage condition covers local senders only; the single-host loopback
sender assumption depends on the unresolved topology in #155.

## Explicit replacement and schema

New retry requires abandonment, completed retirement and no competing
current Attempt. The retirement is either a safe never-dispatched proof
(`no_dispatch_intent`) or [verified maintenance](#verified-maintenance-retirement)
with basis `stopped_target` (#123). The predecessor keeps its dispatched history,
including an unresolved dispatch still counted in `unresolvedDispatches`; its
successor gets its own Attempt identity, submission key and intended run ID. It
validates original object custody and source bytes, never resolving today's HEAD
or original requested spelling anew, and never salvaging candidate edits. The
successor preserves Work Unit, Contract and original B1/source bindings and,
like every Attempt, owns no local directory. One SQLite transaction retains its
key and lineage. A deferred FK prevents lineage-only commits; triggers check the
safe predecessor and original bindings. The same key converges across callers
and reopen. A changed predecessor or a second key for one predecessor refuses.
An old key returns its historical successor after replacement without reviving
authority. The retry records no root or target; the successor's own
`PrepareHttpSubmission` freezes its target binding and a new intended run ID.
A prepared-only record keeps the `no_dispatch_intent` basis; any later phase
quarantines. Preparation and dispatch independently guard currentness.

H integrates with G in historical schema **7**, retaining the exact G schema-6
DDL and all v1–v6 definition hashes. Issue-submission persistence extends the
current schema to **8**, retaining those definitions; schema **9** adds
the persisted installation pause gate, schema **10** adds RequestBundle capture,
schema **11** adds immutable Issue submission cancellation facts, and schema
**12** adds service-owned repository preparation. The fresh
`broodling.application` schema retains these definitions
([state lifecycle](dotnet-identity-custody.md)). Replacement
of never-dispatched work may be allocated and prepared while paused or not;
replacement after `stopped_target` retirement requires the pause for both.
Replacement dispatch always requires explicit release.
Retirement/retry facts resist update, delete and `INSERT OR REPLACE`; SQL refuses
dispatched cleanup authority outside the `stopped_target` conditions and retry
of a dispatched predecessor with any other retirement basis.
No Python database/import compatibility was added. G's
completed-Work-Unit refusal remains in admission/retry/API/SQL, with completed-Attempt
abandonment refusal, factual current-authority-loss guard and Attempt
`INSERT OR REPLACE` protection. H replaces only
the ordinary abandoned-work insertion guard with the safe-retry exception.
For a [revision's](invocation.md#revised-work) Contract (#124), admission and
replacement disregard the completions, and the retired or never-dispatched ended
Attempts, of the Contracts preceding it, in the application and in SQL. A failed
Attempt of a later revision can therefore be replaced after an earlier revision
succeeded; every other Work Unit keeps these guards unchanged. Conversely, the
application and `retry_requires_retirement` refuse to replace an Attempt whose
Contract the Work Unit's latest submission supersedes: that submission is neither
bound to the Contract nor ended `unchanged` with a link to it.

## Evidence and limits

`RetirementTests` owns safe never-dispatched proof, stop ordering, all
dispatched outcomes and SQL refusal of manufactured cleanup authority, plus
verified maintenance retirement: abandoned unresolved/correlated and successful
Attempts, each pause/check/drainage/currentness/retention refusal and the
`retire-attempt` command.
`ReplacementTests` owns replacement lineage, same-key convergence, missing
original material, historical replay and SQL lineage guards, plus
replacement of an unresolved DirectTarget predecessor after `stopped_target`
retirement: admission and first preparation refused outside the pause, the same
bundle-bound task, origin and B1, same-key handback, dispatch refused until release
and then through Resume, unchanged predecessor history, and the `replace-attempt`
command.
The [image demonstration](../../tests/README.md#image-demonstration) runs both
commands from the Broodling image on an abandoned, correlated Attempt that the
image's own processing server created, with a check the host made of the
stopped target, then dispatches the successor after release with the image's
`resume`.
`ReplacementCompletionTests` checks the integrated completed-Work-Unit refusal
at API and SQL boundaries, plus completed retry-key identity handback without
renewed authority. Its historical seed bypasses only ordinary admission while
constructing the fixture; every guard is restored before testing safe retry.
`RetirementProcessTests` drives real SQLite/Git with the test-only caller,
SIGKILLed during and after a replacement's admission and preparation writes; a
later `PrepareRetry` converges on one successor from original B1.
`InvocationTests` adds one
composed stop/quarantine/abandonment handback.

Before G integration, on 22 September 2026, `dotnet test --solution Broodling.sln` passed **238 tests,
0 failed, 0 skipped**, using SDK 10.0.401, runtime 10.0.12 and TUnit 1.68.17.
Build-node reuse/shared compilation were disabled to avoid reusing
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
production-state operation occurred. Dispatched quarantine remains. The [P5 human-review limit](../governing/current.md#first-use-limitation)
still governs every proposed PR; green checks certify neither semantic quality
nor permission to merge.
