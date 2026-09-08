# V1-P4 abandonment and physical cessation

Issue #21 remains **in progress**. The real-provider temporary-source shared-Git
counterexample is unresolved; this implementation is not a G4 or no-effect
compatibility pass. See the [actual-provider record](../../qualification/v1-p4/issue-21-provider-containment.md).

`BroodlingStore.abandon_attempt()` permanently removes currentness before any
stop or retirement operation. `AbandonmentCoordinator.stop()` then closes the
Attempt's physical launch boundary, uses the public SDK to stop its already-known
run, and checks physical cessation. It never replays submission to discover an
ambiguous old run. A runtime terminal result is diagnostic only.

## Bounded W5 containment adaptation

The launcher retains the existing real Codex command flags, isolated homes,
execution-scoped sessions, and inner command sandbox. An outer Bubblewrap PID
namespace adds descendant lifetime containment. It preserves provider API
network access and the prior host `/dev`/`/dev/shm` view; Codex command network
access and read-only roles remain constrained by their existing inner sandbox.
The deterministic evidence leaf is enclosed too and retains its inner read-only,
no-network sandbox.

The launcher validates and arms parent death against the pinned sidecar
controller. A tiny trusted namespace-init entry starts exactly one command,
waits for that command and exits. There is no restart policy or generic process
supervision. Kernel PID-namespace teardown kills descendants, including a
double-forked process that created a new session.

Before the command can start, the launcher durably records the namespace-init
PID and process start time in its owned enclosure and releases an explicit byte
token. EOF is refusal. The entry also checks a launcher pidfd, closing startup
orphan races. The entry avoids stock Bubblewrap init's fork-before-parent-death
arming window. It does not interpret any model response.

The enclosure retains one latest physical receipt, a launch lock and an
irreversible closed marker. The receipt is overwritten only after the preceding
namespace has fully exited. It contains no runtime occurrence history, semantic
material, candidate identifier or authority decision.

Retirement checks the closed marker, obtains the launch lock and verifies the
exact namespace-init identity is gone or its pidfd reports completion. Lock
release alone is insufficient: kernel file-descriptor cleanup can precede
namespace teardown. A missing/unreadable/invalid safety prerequisite fails
closed. Old persisted launch profiles cannot acquire the new containment proof.

## Owned retirement and interruption

Schema 6 adds one Attempt retirement-administration row: immutable cessation
proof/time and a single subsequent retirement acknowledgment. Schema 2–5
migrations retain existing bindings, correlations, abandonment and P3 custody.

`AbandonmentCoordinator.retire()` requires that proof and rechecks physical
cessation and owned host state. It removes only the exact Git worktree and
recorded attached local branch. The enclosure, ownership marker, locks and
physical receipt remain; the durable store and P3 custody are elsewhere.
Another tree, changed branch/common directory or unregistered nonempty path is
not adopted for deletion.

Retirement shares the existing provisioning lock, then obtains the store write
transaction. Its local Git child inherits the lock descriptor, so killing the
caller cannot expose in-progress Git deletion to another retirement. Hooks are
disabled for those exact local administrative commands. A crash after removal
but before acknowledgment converges on the same record without recreating the
tree. A completed acknowledgment is readable without repeating deletion.

Never-dispatched Attempts do not invoke the SDK. A never-materialized allocation
or an acknowledged provisioned worktree has no dispatched provider to stop.
An allocated enclosure left by interrupted provisioning remains blocked: its
unacknowledged Git child lifetime is unknown. Missing dispatched run identity,
inaccessible runtime state, an unqualified old profile or a live namespace also
remains blocking. None restores Attempt eligibility.

No replacement admission, Work Unit disposition, semantic recovery/catch-up,
effects, candidate sealing or later-phase machinery is implemented here.
