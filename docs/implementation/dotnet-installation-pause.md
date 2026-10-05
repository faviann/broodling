# .NET persisted installation pause

Issue #110 adds one persisted installation control to the existing SQLite store:
`PauseInstallation`, `GetInstallationStatus` and `ReleaseInstallation`. The
control gates the current admission, preparation and dispatch entry points. A
pause is not native stop, cancellation, cleanup authority or a maintenance
service. Execution and result capture remain observation/completion paths and
can drain while admission and dispatch are paused.

## Release-host commands

Run the persisted control commands against the deliberate existing .NET store:

```bash
dotnet /RELEASE/host/Broodling.Host.dll pause-installation /EXISTING/DOTNET/state.sqlite3
dotnet /RELEASE/host/Broodling.Host.dll installation-status /EXISTING/DOTNET/state.sqlite3
dotnet /RELEASE/host/Broodling.Host.dll release-installation /EXISTING/DOTNET/state.sqlite3
```

The commands emit JSON with `isPaused`, `changedAt`, `unresolvedDispatches`
and `inFlightInitiationDrained`. `changedAt` is refreshed by every pause call,
even while already paused, and by a release that unpauses; it starts the
maintenance epoch that
[verified maintenance retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement)
binds its host check to. The last two are independent facts:

- `unresolvedDispatches` counts `native_submissions` in `dispatched` state:
  committed dispatch intent without retained correlation. It is durable
  uncertainty about whether a native run exists. Pause and status never rewrite
  it; only exact correlation settles a submission. An HTTP submission's retained
  conflict does not settle it, and neither do abandonment, stop, a terminal or
  unknown-run observation, local drainage or verified maintenance retirement.
  A counted entry whose Attempt has a `stopped_target` retirement is one the
  host procedure recorded as stopped with its target; restart over the same
  ledger ends any such run.
- `inFlightInitiationDrained` is true when no local process holds the
  installation initiation lock at the moment of the reading. It is a local fact
  only; it does not prove that no external submission can still create a run.

`isPaused: true` together with `inFlightInitiationDrained: true` means that the
paused fact is committed, no new admission, preparation or dispatch
transition can commit before an explicit release, and no local Broodling
dispatch is still initiating. It does not fence requests already sent. A
caller holds the lock only in its own process, so the
lock is released when the caller returns or dies, while a request the target
already buffered can still be accepted and create a run. A nonzero
`unresolvedDispatches` at that point therefore still names possibly executing
work, not merely history. Pause does not stop native execution; correlation,
observation, completion and explicit stop remain available under their own
guards. None of these facts proves that a native process, target request or
container physically stopped; physical cessation remains an operator/host
concern.

## Ordering boundary

Each initiating operation checks `installation_control` in the authoritative
immediate SQLite transaction that commits its effect: the admission decision,
Attempt admission, `prepared` submission, and the `prepared → dispatched`
intent. The pause transaction serializes with those writers. If the
effect commits first, it is complete; if the pause commits first, the effect is
refused with `installation_paused`. An operation already in flight when the
pause commits is refused at its next such transition, leaving only its earlier
durable records, which ordinary recovery resumes after release. Ordinary entry
points also check the pause before slow work, so a fresh admission never
reaches the proposer while paused.

The only initiation that can continue after a pause commits is an external
submission whose intent is already durably `dispatched`. The SQLite writer
is never held across that call. `DispatchHttpAsync` takes a shared `flock` on
the already existing SQLite store file before its dispatch transaction and
releases it when its own send returns, fails or is cancelled; nothing inherits
it, so caller death releases it too. Bytes that already reached the target are
not recalled. Status probes the store file with a non-blocking exclusive
`flock`; any shared holder makes it undrained. Concurrent dispatches share the
lock and never block each other.
A child that a Broodling process forks shares the description until its `exec`
closes it, so a single undrained reading can be transient; query again before
acting on it.

`InitiationLock` owns this lock: `AcquireExisting(store, shared: true)` holds
it across a dispatch, and `IsFree(store)` is the status probe that drain status
and [stopped-target retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement)
read. It opens the existing store file read-only and close-on-exec, because
`flock` needs no write access, and it never creates the file. The opener,
`broodling_open_lock`, is the only export of the small C library
`libbroodling_git.so`, built from `native/git_spawn.c`. Ordinary
`dotnet build`, `dotnet test` and host `dotnet publish` compile it with `cc` and
copy it beside the managed assemblies, so a build needs a C compiler and libc
headers, and no `LD_LIBRARY_PATH` is required. A failure to open or lock the
store file raises `initiation_lock_error`.

Abandonment does not release the lock. `StopAsync` can commit abandonment
while a send for that Attempt is still running, and that send can
still create the run, so status stays undrained until it returns. Replaying a
`dispatched` submission can also create its run, so replay is a dispatch and
refuses while paused. Every replay carries the same persisted `submission_key`,
so native key idempotency prevents a concurrent identical caller from creating
a second run after another caller has correlated it.

## Restart and storage failure

The status has no separate initiation record to clean up and no PID,
start-time or in-memory ownership. The kernel releases the lock when the last
process holding it exits, so caller loss, transport loss or a failed
correlation write cannot leave the installation permanently undrained. They
leave the submission `dispatched`, because the target may have accepted it.
For a current Attempt, release and replay converge it through submission-key
correlation. An abandoned Attempt with no known run is quarantined:
`DispatchHttpAsync` refuses it and `StopAsync` never replays it to discover a
run, so its submission stays `dispatched` and counted in
`unresolvedDispatches` while status reports drained. Explicit release changes
only the pause fact.

Pause and release writes are short SQLite transactions. A failed release leaves
the already-persisted paused fact unchanged.

## Replacement exception

The existing explicitly safe replacement path may allocate and prepare its
successor while paused, because it does not dispatch and its predecessor has
already completed the existing abandonment/retirement/current-authority
checks. Replacement of work retired under verified `stopped_target` maintenance
goes further: its admission and first preparation require the pause. Replacement
execution/dispatch still enters the ordinary dispatch gate and requires release.
Correlated reads, startup observation and result capture do not check the pause.

Initialization creates `installation_control` with the default unpaused row.
A pre-transition store is not opened (see the
[state lifecycle](dotnet-identity-custody.md)), so an existing persisted pause is
neither released nor carried over.
