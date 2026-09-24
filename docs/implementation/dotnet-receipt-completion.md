# .NET receipt-backed completion by exact Attempt

[G #138](https://github.com/faviann/broodling/issues/138) consumes the correlated
result from [F's native dispatch](dotnet-native-dispatch.md). Its parity source
is `b3f61a96c40401722ec16fc361958d1690982e02`, especially `disposition.py`,
`test_workflow_result.py`, binding/cardinality cases in
`test_disposition_foundation.py`, and composed completion in `test_invocation.py`.
Current source/release support is .NET; the
[release guide](../../deployment/README.md) distinguishes that from live deployment.

## Callable and operator behavior

```csharp
using var store = new BroodlingApplication().OpenStore(databasePath);
var retained = store.FindCompletion(exactAttemptId);
var completed = await store.WaitAsync(exactAttemptId,
    new ZeroshotTransport(pinnedPythonExecutable), cancellationToken);
```

`FindCompletion` reads only the supplied Attempt ID, including historical
Attempts. It returns null for an unretained identity and never substitutes the
current or latest Attempt. `WaitAsync` first returns that exact retained record
without reserving SQLite's writer or contacting native execution. Otherwise it
requires a current, nonabandoned Attempt, an admitted immutable Contract, and an
already-correlated run with the matching reconstructed frozen invocation. This
uses F's frozen source bytes, original B1, execution settings and delivery
selectors; it never revalidates the old workspace or today's dispatch profile.
Only the pinned transport, locator and run identity are needed for the wait.

The SQLite writer is released before native contact. The returned run must
match; transport failure or cancellation leaves authority unchanged so the
caller can wait again. Native failure records abandonment and raises
`SubmissionNotReady`, without cleanup proof. Malformed success raises
`SubmissionConflict` without abandonment. No-effect success raises the explicit
stable-result capability refusal even when output is null; mutable workspace
contents never supply a substitute accepted revision.

For PR success the receipt is an object with exactly seven string fields:
`version`, `mode`, `outcome`, `repository`, `targetBranch`, `headRevision`,
`pullRequestId`. Require `v1`/`pr`/`opened`, the exact authorized repository and
target branch, and a 40-character lowercase hexadecimal revision different from
original B1. PR ID is a nonempty ASCII digit **string**, not a number: `"0"`,
leading zeros and arbitrarily long strings are valid. There is no positivity or
numeric range rule.

[#115](https://github.com/faviann/broodling/issues/115) then retains the exact
accepted commit before any successful disposition, outside any SQLite writer.
DirectTarget delivers from its own clone, so `headRevision` is normally absent
from the source repository. When `git rev-list --objects --no-walk
--missing=error <oid>` fails in the frozen request's `repository` (the original
B1 common Git directory), Broodling runs `git fetch --no-tags
--no-write-fetch-head --no-recurse-submodules <originUrl> <oid>` against the
frozen `originUrl`. An already-local result needs no network. It then creates
the direct ref `refs/broodling/accepted/<oid>` with the same pin operation as
B1's `refs/broodling/starting/<oid>`: create-only, convergent under concurrent
creation, and never repointing or accepting a symbolic or conflicting ref. A
moving PR branch tip never substitutes for the receipt's commit. The pin keeps
the result readable after the origin branch or repository, the Attempt worktree
and branch are gone and `git gc --prune=now` has run.

A failed fetch, a still-missing object or a conflicting pin raises
`ResultRetentionError` (`result_retention_error`) without a completion row. The
Attempt stays current and the next wait consumes the same native result again.
A pin alone is not success; a later retry converges on the unchanged pin.
Cancelling the wait during the fetch kills and reaps that Git process tree and
detaches the caller without disposition; a pin created before cancellation
remains. The fetch has no product timeout. The
Broodling host therefore needs Git fetch access to the frozen origin URL, with
the host user's own Git credential configuration and no terminal prompt, until
completion is retained.

Inside the final write transaction the application first checks for a competing
retained completion, then repeats currentness, admission and frozen invocation
binding checks. Receipt retention, successful disposition and currentness loss
commit together. Independent finalizers converge on the same record and pin. If
abandonment wins first, late success is refused; if completion wins, abandonment
is refused. A failed write rolls back the entire transition and leaves only the
accepted pin.

`AttemptCompletion` exposes exact Attempt, Work Unit, Contract revision and run
IDs, the complete receipt, accepted revision, completion time,
`Workflow == "software-change"`, and `Outcome == "SUCCEEDED"`. There is no
historical Python assurance format. `AdmissionStatus.Completions` includes the
exact results for that revision's retained Attempts in the existing coherent
read snapshot. Submit/resume/status/history hand back these facts. Repeated
submit still acquires and proposes; resume and inspection do not.
`Invocation.WaitAsync` delegates to the same store operation.

The thin host command does not start HTTP:

```text
wait <store> <attempt-id> [python-executable]
```

Supply the pinned SDK Python executable for an unretained result. Retained
completion needs no executable, dispatch configuration, credentials, origin
access or working native target. Existing `resume`, `status` and `history`
commands include completion facts; Ctrl+C from wait returns caller-detached
handback.

## Durable authority and upgrades

G introduced schema **6**, retained unchanged within the current schema **12**. It uses
one immutable `attempt_completions` row for the receipt and
successful disposition. A single row avoids intermediate receipt-only custody;
an insertion trigger removes current authority in the same transaction. The
Attempt primary key and unique run ID prevent duplicate result identity. The
Work Unit index is deliberately **nonunique** so historical results remain
independently representable and readable.

SQL validates current/admitted/correlated Attempt, Work Unit, Contract, submission
key and delivery selectors against the receipt. The application additionally
reconstructs the entire invocation. SQL independently refuses unjustified
currentness loss, abandonment of a completed Attempt, and admission of any new
Attempt for a completed Work Unit. Ordinary admission has the same Work Unit
guard. H's explicit replacement preserves these guards; result
cardinality is not permission to reopen completed work.

The schema-6 upgrade also repairs a baseline parity gap found by an independent
SQL audit: `INSERT OR REPLACE` must not rebind an Attempt or evict its allocated
identity through a current-Work-Unit, enclosure, checkout or repository/branch
collision. A targeted insert guard preserves these baseline protections without
depending on connection-specific recursive-delete triggers. Original schema
definitions remain frozen. Replacement hardening for provisioning/submission
rows is not included: the same mutations were permitted by the Python baseline.

Ordinary open now refuses schemas 1–6. Deliberate upgrade retains their original
definition hashes and applies missing schema changes atomically without
reinterpreting old facts. The authentic pre-G schema-5 fixture was captured
from F's public API before these edits; see its
[provenance](../../tests/Broodling.Tests/Fixtures/README.md). Upgrade witnesses
compare every prior fact, including frozen native submission, and cover reopen
and repeated upgrade. Python state/import compatibility is excluded.

## Evidence and limits

`AttemptCompletionTests` owns receipt field/type/authority refusals, PR-ID edge
strings, vanished-workspace reconnect, foreign run, uncorrelated/stale authority,
transport/cancel versus failure, late success, rollback after insertion,
independent finalizers, frozen invocation rechecks and no-effect refusal. It
also owns #115's accepted-object retention: survival of origin, workspace and
branch removal plus `gc --prune=now`, an unpublished commit that completes once
pushed, cancellation during a stalled fetch, a conflicting pin, and the pin
surviving a rolled-back write.
`CompletionPersistenceTests` owns direct-SQL receipt/binding refusals,
immutability, justified currentness loss and completed-work admission guards.
Its two-result historical fixture bypasses only new-admission prohibition while
seeding the second Attempt; it demonstrates nonunique Work Unit cardinality and
exact reads, not a supported way to create a new Attempt after completion.
`StoreLifecycleTests` owns authentic prior-schema upgrades. One composed
`InvocationTests` path covers wait, repeated submit, reopen and offline operator
handback, without duplicating boundary failure matrices.

PR receipts here come from controlled native-result boundaries, not real
DirectTarget PR delivery. Receipts that reach `WaitAsync` name a real commit
made in a separate clone and pushed to a local bare stand-in origin, which the
source repository reaches through `url.<bare>.insteadOf`; no GitHub fetch occurs. F's released-SDK transport evidence remains distinct.
No live provider, gateway, GitHub mutation, deployment or evaluation is involved.
Python production code and tests are unchanged. [H implements stop/retirement/
replacement](dotnet-retirement-replacement.md). Every dispatched Attempt remains quarantined after native terminal
success or failure; completion supplies no physical-cessation proof.

The [P5 scoped FAIL](../../evaluation/p5/README.md) and supervised-use limit
remain: every delivered PR requires independent human/operator review of the
exact accepted revision against frozen work and Contract, with relevant tests
and CI before a separate merge decision. `SUCCEEDED` certifies neither semantic
correctness nor authority to merge, deploy or release.

Validation on 22 September 2026 uses .NET SDK 10.0.401, runtime 10.0.12 and
TUnit 1.68.17, with
`BROODLING_TEST_PYTHON=/home/faviann/repos/broodling/.venv/bin/python` for .NET
native witnesses. The unchanged Python suite,
`PATH=/home/faviann/repos/broodling/.venv/bin:$PATH python -m pytest tests`,
passed **404 tests in 107.25 seconds**.

- Implementation-agent `dotnet test --solution Broodling.sln`: **217 passed, 0 failed,
  0 skipped**, 26.146 seconds, including retained replay under an unrelated
  SQLite writer.
- `dotnet build Broodling.sln --configuration Release`: **0 warnings,
  0 errors**, 41.53 seconds.
- `git diff --check`: clean.

Root's subsequent Attempt-replacement regression run reproduced **2 failures
of 11** before the schema guard. After that repair, the full suite ran **218
tests: 217 passed, 1 failed**, 57.823 seconds. The replacement checks passed;
`NoEffectSuccessCannotManufactureStableLocalResult` failed starting its isolated
provider with `Win32Exception: Exec format error` at `CodexProfile.Validate`.
An immediate focused rerun passed 1/1 in 1.461 seconds. That rerun does not explain
the failure. The repaired Release build passed with 0 warnings/errors in 22.94
seconds. A fresh independent bounded diagnosis subsequently passed 101 focused
checks (initial plus 100 repetitions) and all 218 tests in 25.223 seconds with
unchanged test DLL/provider hashes. Neither reported error recurred; no cause,
relationship between the errors, or repair was established. No speculative
production change was made for either observation.

Fresh independent Spec review passed with zero findings. Standards review
identified one nonblocking duplicated fixture-restoration helper; consolidation
preserved all per-version assertions, and fresh repair review passed with zero
findings. Production code was unchanged by that consolidation.

Final standard validation on the reviewed implementation and consolidated tests:
**218 passed, 0 failed, 0 skipped**, 55.741 seconds. Final Release build:
**0 warnings, 0 errors**, 20.79 seconds. Build-node reuse/shared compilation were
disabled with `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0` and
`UseSharedCompilation=false`. `git diff --check` is clean. These final passes
remain distinct from the earlier unexplained failures.

An initial .NET run during implementation passed 211 of 212 tests and failed
the unchanged `PreparedResumeReconcilesFrozenInvocationWithoutRestoringMissingWorkspace`
test with this exact reported Git error:

```text
UnsupportedStartingState: Cannot establish local Git custody: fatal: bad config line 1 in file ./config
```

The stack went through `GitCustody.Checked` → `Text` → `RetentionOid` → `Retain`
→ `BroodlingStore.AdmitAttempt` → `AttemptFixture.Admit` →
`NativeFixture.Provision`. Subsequent full runs passed 216/216 and 217/217.
The initial failure's cause has not been diagnosed; those passes do not establish
a cause or repair. No unrelated Git/provisioning code was changed.

#115 validation on 24 September 2026 used the same SDK, runtime, TUnit version,
`BROODLING_TEST_PYTHON` and build settings. `dotnet test --solution
Broodling.sln` passed **373 of 373, 0 skipped**, 61.403 seconds. The Release
build had **0 warnings, 0 errors**, 8.45 seconds. `git diff --check` is clean.
With the accepted pin removed by hand, the garbage-collection regression failed
because the fetched objects had been pruned. Without cancellable fetch the
cancellation regression blocked until the stalled transport exited; without the
process-tree kill its transport survived the detached wait.
