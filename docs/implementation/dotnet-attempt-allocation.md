# .NET original B1 and Attempt allocation

[B #134](https://github.com/faviann/broodling/issues/134) extends the
[Contract admission API](dotnet-contract-admission.md) with local Git custody,
one current Attempt and basic abandonment. Every Attempt is an HTTP DirectTarget
Attempt that retains B1 but owns no local directory.
See the [release/cutover guide](../../deployment/README.md) for current operations.

## Callable API

```csharp
using var store = new BroodlingApplication().OpenStore("/srv/broodling-dotnet/state.sqlite3");
var attempt = store.AdmitHttpAttempt(revisionId,
    repository: "/srv/broodling-dotnet/repositories/widget",
    revision: "HEAD");
// A bundle-bound submission supplies the retained repository and B1.
var prepared = store.AdmitHttpAttempt(submissionId);

var current = store.RequireCurrentAttempt(attempt.AttemptId);
var status = store.Status(revisionId); // exact revision, including Attempts
var history = store.History(WorkReference.Parse("acme/widget", 123));
var abandoned = store.AbandonAttempt(attempt.AttemptId, "Operator ended authority");
```

`AdmitHttpAttempt` is the public admission path; callers cannot supply an unchecked
starting-state object to bypass Git custody. It requires a committed admitted
Contract decision. B1 records the physical common Git directory, exact SHA-1
commit, original requested revision spelling and a fingerprint of the Contract's
sorted source ID/digest pairs. Existing Contract/source identities and canonical
bytes are unchanged. Entitled bytes remain in their existing immutable store.

The prepared-submission overload consumes the completed RequestBundle's immutable
repository preparation. It requires the submission's Contract to be bound to that
bundle, whose [admission](dotnet-contract-admission.md#bundle-bound-admission)
already fixed the retained PR target. It uses the retained service-owned bare
repository as the Git common directory and records the retained exact starting
commit and requested branch ref as B1. Every Attempt route, including the
revision-based ones and replacement, applies the store's
[bundle authority rule](dotnet-contract-admission.md#bundle-bound-admission). A
later repository default, caller input or moving branch therefore cannot retarget
a bound Contract. An unbound association with a completed bundle cannot start an
Attempt. A submission without a RequestBundle is refused with `UnknownRecord`. The explicit-repository
overload resolves its caller checkout and revision before the shared admission
core.

The supported checkout profile is checked before status can invoke conversion
drivers. Configured external filters, unsupported byte conversions, sparse
checkout, fsmonitor, conditional includes and effective transformation attributes
refuse. Committed attributes come from the selected tree, even if the live index
differs. Tracked, staged and untracked material refuses with up to twenty named
entries. Bare repositories and linked source worktrees are supported.

Every custody command removes inherited `GIT_*` variables, disables prompting,
optional locks, replacement objects and lazy fetch, and suppresses administrative
hooks. Selected commit/tree/blob availability is checked with `rev-list --objects
--no-walk --missing=error`; ancestor-only loss is permitted. A direct
`refs/broodling/starting/<commit>` pin is created with compare-and-swap before
the Attempt can be acknowledged. Existing identical pins converge; symbolic or
conflicting pins refuse. The source repository and retained refs remain durable
operating state; this operation neither fetches nor copies them elsewhere.

## No local directory

[#173](https://github.com/faviann/broodling/issues/173) added the HTTP
DirectTarget Attempt, which owns no local directory, and #233 made it the only
kind. `AdmitHttpAttempt` has an explicit-repository overload and a
prepared-submission overload. Both share the admission core above: committed
admitted Contract, cancellation, pause, completed-work and one-current-Attempt
checks, supported checkout profile and the create-only
`refs/broodling/starting/<commit>` pin in the canonical common Git directory
before the SQLite acknowledgement. The prepared overload keeps the retained
default-branch check. The admitted Contract must authorize exactly one supported
`pull_request` effect; Contract admission already refuses an empty
required-effect set. There is no workspace root, enclosure, marker, lock,
branch, worktree or runtime directory, and none is created. Zeroshot checks out
B1 in its own execution workspace.

## Atomicity, replay and observation

Direct `Microsoft.Data.Sqlite` remains sufficient. One Attempt row contains all
B1 fields, so an admission cannot be partially committed.
SQLite serializes admissions and enforces one current Attempt per Work Unit. Triggers
protect immutable bindings, admitted Contract ownership and irreversible
authority loss. Git retention precedes the SQLite transaction: interruption can
leave an extra retention pin, but cannot acknowledge an unretained Attempt.

Identical requests return the original Attempt and timestamps. An equivalent
revision spelling does not rename it. Different Contract/source, repository or commit bindings conflict.
Reopen uses the retained IDs; a later `HEAD` never changes stored B1. Re-admission
still validates the supplied starting state and its Git custody; status and
`GetAttempt` observe retained facts without accessing Git.

`RequireCurrentAttempt` is a point-in-time check. Future dependent writes must
call its transaction-taking overload within their own SQLite transaction.
`AbandonAttempt` atomically records the first nonempty reason and timestamp and
ends current authority. Repetition keeps the first fact. Ordinary admission
cannot resurrect it. Abandonment makes no claim about native cessation and
grants no cleanup or replacement authority.

`Status(revisionId).Attempts` contains only Attempts bound to that exact revision,
including B1 and nullable abandonment. `History` and existing JSON
status/history commands expose the same facts. Observation uses a coherent
deferred snapshot without reserving the writer; an admitted later revision does
not inherit another revision's Attempt.

B introduced .NET schema 3; the fresh `broodling.application` schema retains H's
definitions, Issue submission persistence, installation pause, RequestBundle
custody, cancellation facts and repository preparation. See the
[state lifecycle](dotnet-identity-custody.md).

## Evidence and next slices

On 22 September 2026, SDK 10.0.401 and Git 2.47.3 ran
`dotnet test --solution Broodling.sln`: **102 passed, 0 failed, 0 skipped**,
including the already-merged D acquisition checks.
`dotnet build Broodling.sln --configuration Release` also passed with zero
warnings or errors.
`AttemptAdmissionTests` and `GitCustodyTests` use real SQLite and local Git for
binding, concurrency, reopen, allocation/abandonment rollback, dirty material,
selected-object loss, permitted ancestor loss, retained B1 after GC, hook
suppression and environment/path refusals. SQLite failure-injection tests
interrupt actual writes; they do not claim process-kill or power-loss coverage.
Tests create and remove only their own directories under
`~/.cache/broodling-tests` (or `BROODLING_TEST_WORKSPACE_ROOT`). No live provider, native or evaluation run
was performed by this slice. Python production/tests are unchanged. Separately,
the supported Python suite on merged main `cc300e4` passed **404 tests in 117.88s**;
this is distinct from the independently recorded
[exact-baseline run](../migration/130-baseline-validation.md).

- The schema-3 and 102-test evidence above describes B's original landing.
- [Dispatch](zeroshot-native-integration.md#http-dispatch-and-acknowledgement)
  checks current authority inside its dispatch-intent write; observing current
  authority here does not authorize a later unguarded dispatch.
- [G completion](dotnet-receipt-completion.md) adds the baseline's Work-Unit-wide
  completed-result admission refusal in both the store and schema. Exact-Attempt
  result retention never allows completed work to acquire fresh authority.
- [H lifecycle](dotnet-retirement-replacement.md) implements native stop,
  retirement and explicit replacement from original B1;
  ordinary admission and abandonment here grant none of those operations.

The supported profile excludes concurrent hostile mutation of repository
configuration, attributes, paths or retained refs. These witnesses establish
Broodling's local allocation/custody boundary, not execution or delivery parity.
