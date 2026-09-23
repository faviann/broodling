# .NET persisted installation pause

Issue #110 adds one persisted installation control to the existing SQLite store:
`PauseInstallation`, `GetInstallationStatus` and `ReleaseInstallation`. The
control gates the current admission, preparation and dispatch entry points. A
pause is not native stop, cancellation, cleanup authority or a maintenance
service. Execution and result capture remain observation/completion paths and
can drain while admission and dispatch are paused.

## Ordering boundary

Each initiating operation first records an `installation_initiations` row in a
short immediate SQLite transaction that checks `installation_control`. The
dispatch row remains until the durable `prepared → dispatched` intent, the
external `SubmitAsync` call and factual correlation have all completed. The
SQLite writer is never held across transport, and late correlation still
retains its existing facts after authority loss.

The pause transaction serializes with initiation-row insertion. If insertion
commits first, the pause status includes that outstanding row; if the pause
commits first, a new ordinary initiation is refused. `InFlightInitiationDrained`
means only that no durable initiation rows remain outstanding. The status counts
are not process liveness observations and do not prove that an unobserved
external bridge or target request stopped. `UnresolvedDispatches` identifies
outstanding dispatch rows whose native intent is already `dispatched`.

## Restart and storage failure

Initiation rows are deliberately not reaped from caller PID, process start time,
or in-memory state. Those observations are not valid across shared installation
PID namespaces, and caller loss does not prove that a Python bridge or submitted
target request ceased initiating. A row surviving restart or a failed cleanup
therefore keeps the status conservatively undrained. This scope has no safe
adoption/clearing bypass; later preparation/recovery/replay must inspect the
retained state and an operator must reconcile an orphaned row. Actual container
cessation remains a downstream host concern.

Pause/release writes and initiation cleanup are short SQLite transactions. A
failed release leaves the already-persisted paused fact unchanged. A failed
cleanup leaves its outstanding row, so the store never reports a false drained
state. Explicit release changes only the pause fact; it does not erase evidence.

## Replacement exception

The existing explicitly safe replacement path may allocate and prepare its
successor while paused, because it does not dispatch and its predecessor has
already completed the existing abandonment/retirement/current-authority
checks. Replacement execution/dispatch still enters the ordinary dispatch gate
and requires release. Correlated reads, startup observation and result capture
do not create initiation rows.

Schema 8 adds `installation_control` and `installation_initiations` to the
unchanged schema-7 definitions. Ordinary open refuses schema 7; explicit
upgrade adds both tables and the default unpaused row atomically. The authentic
schema-7 fixture is `tests/Broodling.Tests/Fixtures/dotnet-v7.sql`.
