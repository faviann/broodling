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
and `inFlightInitiationDrained`. The last field is derived from the third:
`isPaused: true` together with `inFlightInitiationDrained: true` means that the
paused fact is committed and no new admission, preparation or dispatch
transition can commit before an explicit release, and no durably dispatched
submission is unresolved in the store.
It does not prove that a Python bridge, target request, process or container
physically stopped; physical cessation remains an operator/host concern.

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
is never held across that call. `unresolvedDispatches` counts
`native_submissions` in `dispatched` state, and
`inFlightInitiationDrained` is true when that count is zero. Correlation or a
retained conflict (`blocked`) settles a submission. Replaying a `dispatched`
submission can still create its run, so replay is a dispatch and refuses while
paused. A pause may therefore observe a still-running external call as
undrained until late correlation commits.

## Restart and storage failure

The status is derived from durable admission and submission state, with no
separate initiation evidence to clean up and no PID, start-time or in-memory
ownership. Caller loss or a storage failure before an authoritative transition
commits leaves no new initiation fact to reap; existing recovery can retry any
pre-commit filesystem work. Caller loss, transport loss or a failed correlation
write after the `dispatched` commit leaves the submission `dispatched`: the
target may have accepted it, so the status stays undrained. That state
converges through the existing recovery path: release, replay the dispatch so
the submission-key correlation settles it, then pause again. Explicit release
changes only the pause fact.

Pause and release writes are short SQLite transactions. A failed release leaves
the already-persisted paused fact unchanged.

## Replacement exception

The existing explicitly safe replacement path may allocate and prepare its
successor while paused, because it does not dispatch and its predecessor has
already completed the existing abandonment/retirement/current-authority
checks. Replacement execution/dispatch still enters the ordinary dispatch gate
and requires release. Correlated reads, startup observation and result capture
do not check the pause.

Schema 8 adds `installation_control` to the unchanged schema-7 definitions.
Ordinary open refuses schema 7; explicit upgrade adds the table and the default
unpaused row atomically. The authentic
schema-7 fixture is `tests/Broodling.Tests/Fixtures/dotnet-v7.sql`.
