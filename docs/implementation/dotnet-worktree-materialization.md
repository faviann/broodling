# .NET owned worktree materialization

[C #135](https://github.com/faviann/broodling/issues/135) makes an existing
[B allocation](dotnet-attempt-allocation.md) match local Git state. This seam adds no
dispatch, retirement, replacement, execution supervision or operator command.

## Application API and retained facts

```csharp
using var store = new BroodlingApplication().OpenStore("/srv/broodling-dotnet/state.sqlite3");
var attempt = store.ProvisionAttempt(attemptId);
var path = attempt.Allocation.WorktreePath;
var firstAcknowledgment = attempt.Provision!.ProvisionedAt;
var status = store.Status(attempt.ContractRevisionId);
var history = store.History(WorkReference.Parse("acme/widget", 123));
```

`ProvisionAttempt` is synchronous. The store session remains confined to one
caller; independent callers open independent sessions. The method takes only
the existing Attempt ID. It cannot select a new repository, revision, root or
Attempt. `AttemptRecord.Allocation` always records the reservation;
`AttemptRecord.Provision` is null until materialization is acknowledged. Its
first timestamp is immutable and survives reopen, replay and abandonment.
Status/history, including the existing JSON commands, expose those same exact
revision facts without touching Git. A provision record is historical
acknowledgment, not a live filesystem check or execution/cleanup authority.
F now [composes this seam](dotnet-native-dispatch.md) before dispatch and guards
`ProvisionAttempt` against every dispatched candidate. Its external SDK call
releases the stable enclosure lock and SQLite writer after durable intent.

C introduced .NET schema 4; [H lifecycle](dotnet-retirement-replacement.md) uses
current schema 10 (historical H schema 7, schema-8 Issue submission persistence,
and schema-9 installation pause). Explicit upgrade recognizes unchanged v1–v9 definition hashes and
applies missing migrations transactionally; ordinary open still
refuses old schemas. The actual pre-C schema-3 fixture retains its B1,
allocation and abandonment, alongside all earlier Contract/source facts.
Upgrade neither fabricates past provisioning nor imports Python state.

## Ownership and convergence

The enclosure and its `worktree` child come exclusively from the allocation.
The marker and stable lock sit outside candidate material. Marker publication
uses a flushed staging file and atomic rename; a recognized incomplete staging
file can be retried. Foreign enclosure contents, marker bytes, nonphysical
paths/symlinks, lock symlinks/hard links, branch assignments, detached checkouts
and common-Git mismatches refuse. An already-open store cannot be enclosed by
the candidate scaffolding or live inside the source common Git directory.
Source checkouts, candidate and common Git remain separate. No foreign checkout
is adopted and no branch is moved to manufacture ownership.

Provisioning repeats `GitCustody.Retain` from recorded B1 and repeats the shared
checkout-profile check before checkout. It never resolves current HEAD. Git
commands retain the existing sanitized environment, no-lazy-fetch policy,
replacement-object exclusion and suppressed administrative hooks. New worktrees
attach to the assigned local branch at exactly B1. A branch left by an
interrupted creation can be reused only at B1 and only when it is not attached
elsewhere. A missing, exactly registered owned path can be recreated using
Git's worktree-add force option; a nonempty unrecognized path is refused.

As in the frozen Python implementation, an already owned, registered live
worktree is recognized and left alone. Its actual common Git directory,
top-level path and attached branch must match. Replaying provisioning does
**not** reset HEAD, clean files or discard later candidate progress. New
creation validates B1; live recognition does not redefine B1 from candidate
HEAD. F owns dispatch/replay policy for that candidate. Unrecognizable partial
Git state remains a refusal rather than permission to delete or reset it.

## Exclusion and authority

1. A short SQLite writer checks current authority and claims the enclosure.
2. After releasing SQLite, the caller acquires the enclosure's stable `flock`.
3. Under that lock, a SQLite writer rechecks current authority, observes settled
   Git state, materializes as necessary, and commits the provisioning fact.

Holding the writer through administrative Git serializes abandonment with
mutation and acknowledgment. It does not describe F's external submission
boundary, which must release the SQLite writer. A caller killed in this section
loses its uncommitted acknowledgment. An orphan Git still owns the lock; another
provisioner waits before observing its result. Abandonment can commit after the
dead caller loses its writer, and the waiting replay then fails its authority
check. Status/history use a coherent deferred read snapshot throughout a real
provisioning write.

`AdministrativeGitProcess` owns a close-only safe lock descriptor opened with
`O_CLOEXEC`. It stays CLOEXEC in the parent for its entire lifetime. A small
in-process C library uses `posix_spawn` child file actions to duplicate that
same description into the selected Git only. It never temporarily enables
parent inheritance, launches a worker executable, or changes host signals.
There is no `LOCK_UN`: closing a parent must not release its child's lock.

C# owns the positive child PID, drains stdout/stderr concurrently and consumes
only `waitpid(thatPid, ...)`, retrying EINTR. Signal termination and failed wait
ownership, including ECHILD, are failures. The synchronous operation has no
cancel/detach path; while the caller lives it retains its direct-child waiter.
This is administrative resource ownership, not native execution supervision.
If caller death closes output readers, ordinary pipe failure can also terminate
Git; safety does not depend on Git surviving. The orphan witness deliberately
keeps Git output available to exercise the harder surviving-writer case.

## Supported host and packaging

The seam supports an ordinary Linux x86-64 .NET 10 host with local filesystem
`flock`, waitable direct children and no competing reaper for those PIDs. It
refuses PID 1, explicit ignored SIGCHLD and SA_NOCLDWAIT before enclosure
mutation. The supported operator profile runs the CLI as an unprivileged host process;
the separately managed DirectTarget container is not this application host.
Arbitrary host signal changes/reapers, fork-only descendants retaining parent
descriptors, detached helpers discarding the lock, hostile concurrent path or
Git configuration mutation, and network-filesystem locking are outside this
ownership profile. The lock pathname/inode must never be replaced or unlinked
while holders or waiters may exist. A terminal native label grants none of
these administrative ownership conditions.

Ordinary `dotnet build`/`dotnet test` compile `native/git_spawn.c` with `cc` and
copy `libbroodling_git.so` transitively beside the managed assemblies. Host
`dotnet publish` includes it as well; no `LD_LIBRARY_PATH` is required. Build
requires a C compiler and libc development headers on the supported Linux
host. The binary links against that compiler's libc baseline: build/publish for
the destination host's compatible libc, not a different architecture or an
unqualified glibc/musl combination. Local validation uses SDK 10.0.401, runtime
10.0.12, glibc 2.41 and Git 2.47.3; it does not establish a portable binary
release across other libc versions.

## Evidence and integration handoff

On 22 September 2026, `dotnet test --solution Broodling.sln` passed **129 tests,
0 failures, 0 skipped**. `dotnet build Broodling.sln --configuration Release`
passed with **0 warnings and 0 errors**. Host publish to an owned local build
directory passed; the test caller loaded the published application/shim using
the host's published runtimeconfig/deps with `LD_LIBRARY_PATH` unset. This was
a packaging check, not deployment or native workflow execution. NuGet's HTTP
cache was directed to an owned `/tmp` directory for the sandboxed commands.
Python production/tests were unchanged and were not rerun for C.

`WorktreeProvisioningTests` exercises real SQLite/Git original B1, retention
repetition, owned candidate progress, branch-only and lost-acknowledgment
replay, missing-worktree reconstruction, ownership/path refusals, checkout
policy, hook suppression and abandonment. `ProvisioningProcessTests` uses a
small test caller and a gated real Git command for independent callers,
read-only observation during a writer, caller SIGKILL before/after checkout,
orphan exclusion and abandonment while a replay waits. It also checks ordinary
parent lock disposal, absence of lock retention by an unrelated live
`Process.Start` child, selected-child signal/failure cleanup, ignored-SIGCHLD
refusal and output larger than both pipe capacities. It makes no provider,
Zeroshot, deployment, power-loss or hostile-process claim.

- F composes `ProvisionAttempt` for permitted pre-dispatch recovery. Preserve
  live candidate progress; do not automatically reprovision a correlated or
  quarantined execution. Recheck checkout policy and current authority inside
  the appropriate dispatch-intent transaction. Provisioning acknowledgment
  alone is neither dispatch authority nor native cessation.
- H reuses the stable enclosure lock and selected-child Git handoff for any
  authorized administrative removal. Keep enclosure-lock-before-SQLite
  ordering, exact ownership checks and close-only release. C authorizes no
  retirement/replacement, and every dispatched Attempt remains quarantined.
