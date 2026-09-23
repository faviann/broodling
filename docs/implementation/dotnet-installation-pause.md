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
and `inFlightInitiationDrained`. The last two are independent facts:

- `unresolvedDispatches` counts `native_submissions` in `dispatched` state. It
  is durable uncertainty about whether a native run exists. Pause and status
  never rewrite it; only correlation or a retained conflict (`blocked`)
  settles a submission.
- `inFlightInitiationDrained` is true when no process holds the installation
  initiation lock, so no external submission can still create a native run.

`isPaused: true` together with `inFlightInitiationDrained: true` means that the
paused fact is committed, no new admission, preparation or dispatch
transition can commit before an explicit release, and no Broodling dispatch or
submit bridge is still initiating. A nonzero `unresolvedDispatches` at that
point is retained history for the operator to inspect, not in-flight work.
It does not prove that a native process started by the bridge, a target
request or a container physically stopped; physical cessation remains an
operator/host concern.

## Ordering boundary

Each initiating operation checks `installation_control` in the authoritative
immediate SQLite transaction that commits its effect: the admission decision,
Attempt allocation, provisioning acknowledgment, `prepared` submission, and
the `prepared → dispatched` intent. Provisioning may do its existing
pre-commit filesystem work, but its acknowledgment is guarded by that final
transaction. The pause transaction serializes with those writers. If the
effect commits first, it is complete; if the pause commits first, the effect is
refused with `installation_paused`. An operation already in flight when the
pause commits is refused at its next such transition, leaving only its earlier
durable records, which ordinary recovery resumes after release. Ordinary entry
points also check the pause before slow work, so a fresh admission never
reaches the proposer while paused.

The only initiation that can continue after a pause commits is an external
`SubmitAsync` whose intent is already durably `dispatched`. The SQLite writer
is never held across that call. Instead, `DispatchAsync` takes a shared
`flock` on `<store>-initiation.lock` before its dispatch transaction and holds
it until the transport returns. `ZeroshotTransport` spawns the submit bridge
with that lock description inherited, so the lock stays held while the bridge
runs even if its caller dies. The bridge's own children do not inherit it.
Status probes the lock with a non-blocking exclusive `flock`; any shared holder
makes it undrained. Concurrent dispatches share the lock and never block each
other. The lock file must not be removed while a store is in use. A child that
a Broodling process forks shares the description until its `exec` closes it,
so a single undrained reading can be transient; query again before acting on it.
A drained reading is never premature.

Abandonment does not release the lock. `StopAsync` can commit abandonment
while a `SubmitAsync` for that Attempt is still running, and that call can
still create the run, so status stays undrained until it returns. Replaying a
`dispatched` submission can also create its run, so replay is a dispatch and
refuses while paused.

## Restart and storage failure

The status has no separate initiation record to clean up and no PID,
start-time or in-memory ownership. The kernel releases the lock when the last
process holding it exits, so caller loss, transport loss or a failed
correlation write cannot leave the installation permanently undrained. They
leave the submission `dispatched`, because the target may have accepted it.
For a current Attempt, release and replay converge it through submission-key
correlation. An abandoned Attempt with no known run is quarantined:
`DispatchAsync` refuses it and `StopAsync` never replays it to discover a
run, so its submission stays `dispatched` and counted in
`unresolvedDispatches` while status reports drained. Explicit release changes
only the pause fact.

Pause and release writes are short SQLite transactions. A failed release leaves
the already-persisted paused fact unchanged.

## Replacement exception

The existing explicitly safe replacement path may allocate and prepare its
successor while paused, because it does not dispatch and its predecessor has
already completed the existing abandonment/retirement/current-authority
checks. Replacement execution/dispatch still enters the ordinary dispatch gate
and requires release. Correlated reads, startup observation and result capture
do not check the pause.

Schema 9 adds `installation_control` to the unchanged schema-8 definitions.
Ordinary open refuses schemas 7 and 8; explicit upgrade adds the table and the
default unpaused row atomically after applying any earlier recognized
migrations. The authentic schema-8 fixture is
`tests/Broodling.Tests/Fixtures/dotnet-v8.sql`.
