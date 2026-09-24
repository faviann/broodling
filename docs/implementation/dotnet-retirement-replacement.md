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
var successor = store.AdmitRetry(attemptId, retryKey, workspaceRoot, profile);
var prepared = store.PrepareRetry(attemptId, retryKey, workspaceRoot, profile);
var submitted = await store.RetryAsync(attemptId, retryKey, workspaceRoot,
    profile, transport, credentials);
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
and, when one exists, abandonment of that fact's exact Attempt before entering
this stop path. A no-Attempt or safely undispatched cancellation returns the
cancelled `IssueSubmission`; a dispatched Attempt may return
`CessationUnconfirmed`, `SubmissionNotReady` when a known run has no stop
transport, `NativeTransportError`, or `OperationCanceledException`. These
outcomes retain the cancellation/abandonment facts and neither means physical
cessation nor grants replacement authority.

The thin host command is `stop <store> <attempt-id> <reason> <python-executable>`.
It returns the exact Attempt, submission, quarantine flag and safe handback even
when stop fails. Cessation refusal returns exit 1; cancellation returns 130 and
retains abandonment. The executable selects the pinned SDK bridge, not dispatch
configuration or credentials. Safe undispatched stop needs no functioning SDK.
Submit still reacquires/proposes its explicit issue, then hands back abandonment;
resume/status/history inspect retained facts without automatic replacement.
`QuarantinedAttemptIds` identifies every dispatched Attempt, even if current.
It describes a permanent cleanup limitation, not native execution status.

## Stop and retirement authority

Abandonment commits before host inspection or native stop; its first reason wins.
Stop checks physical allocation, enclosure marker and acknowledged enclosure
presence, but requires no live checkout, live Git inspection or dispatch
configuration. Known runs use only frozen locator and retained run identity.
Unknown dispatched identity is never discovered by replay. Success,
`force_stopped`, `runtime_lost`, transport failure and cancellation grant no
retirement/replacement authority. Native labels are not cessation receipts.

Safe proof requires submission absent or merely prepared, and either:

- no enclosure and no provisioning acknowledgment (`never_materialized`);
- an owned existing enclosure and provisioning acknowledgment (`never_dispatched`).

A marker alone, an actual checkout without durable acknowledgment, or a missing
acknowledged enclosure cannot establish safe cessation. Proof commits under the
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
There is no execution supervisor or native cleanup mechanism.

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
authority. Provision/prepare/dispatch independently guard currentness. Direct
`PrepareSubmission` enforces the frozen retry target too. Dispatched retry
recovery reuses F's correlation seam without reprovisioning candidate material.

H integrates with G in historical schema **7**, retaining the exact G schema-6
DDL and all v1–v6 definition hashes. Issue-submission persistence extends the
current schema to **8**, retaining those definitions; schema **9** adds
the persisted installation pause gate and current schema **11** adds immutable Issue submission cancellation facts after RequestBundle
capture. Recognized older .NET stores require
deliberate atomic upgrades and ordinary open refuses old schemas. Safe
replacement allocation and preparation remain permitted while paused, but
replacement dispatch still requires explicit release.
Retirement/retry facts resist update, delete and `INSERT OR REPLACE`; SQL refuses
dispatched cleanup authority and missing/changed retry submission targets.
No Python database/import compatibility was added. Authentic G6 upgrade evidence
retains completed and abandoned Attempts alongside all earlier facts. G's
completed-Work-Unit refusal remains in admission/retry/API/SQL, with completed-Attempt
abandonment refusal, factual current-authority-loss guard and Attempt
`INSERT OR REPLACE` protection. H replaces only
the ordinary abandoned-work insertion guard with the safe-retry exception.

## Evidence and limits

`RetirementTests` owns safe/ambiguous proof, stop ordering and all dispatched
outcomes, ownership refusals, dirty deletion, lock inode and lifecycle SQL.
`ReplacementTests` owns original material, atomic allocation, same-key
concurrency, historical replay, target enforcement and SQL binding refusals.
`ReplacementCompletionTests` checks the integrated completed-Work-Unit refusal
at API and SQL boundaries, plus completed retry-key identity handback without
renewed authority. Its historical seed bypasses only ordinary admission while
constructing the fixture; every guard is restored before testing safe retry.
`RetirementProcessTests` drives real SQLite/Git with the test-only caller: SIGKILL
before/after retirement removal, orphan exclusion, premature retry refusal and
retry allocation/preparation transaction deaths. `InvocationTests` adds one
composed stop/quarantine/abandonment handback. `StoreLifecycleTests` compares all
old facts through authentic v1–v8 upgrades, including completion, currentness,
request/run, provisioning, abandonment and source/Contract/B1 facts. G's authentic
F schema-5 prepared fixture remains unchanged.

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
