# .NET identity and source custody

[A1 #132](https://github.com/faviann/broodling/issues/132) is the first behavioral
slice of [#130](https://github.com/faviann/broodling/issues/130). It supplies
callable state lifecycle, Work Unit identity and entitled-source custody. It
grants no Contract, Attempt or execution authority by itself. [A2](dotnet-contract-admission.md)
composes these facts into supplied-source Contract admission. See the
[release/cutover guide](../../deployment/README.md) for current operations.

## Application API

`BroodlingApplication` is registered in the ASP.NET host and also works without
HTTP or dependency injection. Its `InitializeStore(path)`, `OpenStore(path)` and
`UpgradeStore(path)` return a disposable `BroodlingStore` session. Use one session
per caller; do not share it across threads. Separate sessions/processes serialize
writes through SQLite. The HTTP host only opens its configured existing store and
opens a separate session per request.

```csharp
var application = new BroodlingApplication();
using (application.InitializeStore("/srv/broodling-dotnet/state.sqlite3")) { }

using var store = application.OpenStore("/srv/broodling-dotnet/state.sqlite3");
var work = store.ResolveWorkUnit(WorkReference.Parse("acme/widget", "#123"));
var source = store.EntitleSource(work.WorkUnitId, new SourceSubmission(
    "caller_statement", "caller://reviewed-request", requestBytes,
    entitlement: new SourceEntitlement("caller", "Reviewed request for issue 123")));
var retained = store.GetEntitledSource(source.SourceId);
```

`WorkReference.Parse` accepts the documented repository/issue spellings in the
[ingress reference](work-reference-ingress.md#work-reference-identity), with
optional opaque repository/issue identity pins. Its issue overloads take a
string or a positive signed 64-bit integer, SQLite's integer range.
`ResolveWorkUnit(reference, expectedWorkUnitId)` optionally checks the asserted
identity. Resolving records every raw submission; canonicalization determines
the Work Unit. Both upstream pins and the submission commit atomically. Omitted
pins retain existing identities; conflicting pins refuse the entire operation.

URL-only callers use `WorkReference.ParseIssueUrl` for the supported HTTPS
`github.com/OWNER/REPOSITORY/issues/NUMBER` form, then `SubmitIssue(issueUrl)`.
The store validates and canonicalizes the Work Unit before any upstream call,
and atomically creates the first ordinary `IssueSubmission` or returns the
latest retained one. Its durable sequence is the ordering authority;
`received_at` is descriptive only. `GetIssueSubmission` and `IssueHistory` are
read-only, and the latter remains usable before a Contract exists.
`AssociateIssueSubmission` binds one exact handle to one Contract revision once;
Attempt IDs are derived from existing Attempt rows for that revision, not copied
into a second execution ledger. A submission with a RequestBundle accepts only a
Contract bound to that exact completed bundle, which
[`AdmitRequestBundle`](dotnet-contract-admission.md#bundle-bound-admission)
records together with the association.
`CancelIssueSubmissionAsync` records one immutable cancellation fact before
entering the existing abandonment/stop path. Its nullable Attempt binding is the
immutable stop/no-stop decision for this exact submission: null means that no
Attempt stop belongs to this cancellation, either because no Attempt existed or
because another retained submission still held shared Contract authority. This
binding is not the full `AttemptIds` lineage, which remains derived from the
shared Contract. Replay uses the original binding even if a later safe
replacement exists. The call returns the refreshed cancelled `IssueSubmission`
when no native stop is needed or safe cessation is retained.
Cancellation and abandonment remain committed when it instead hands back
`CessationUnconfirmed` (`NativeStopRequested` is true only after the native
stop transport was actually called), `SubmissionNotReady` for a known run with
no stop transport, `NativeTransportError`, or caller cancellation. Those
outcomes do not authorize replacement; inspect the exact cancellation and
Attempt history for the durable handback.

`GetWorkUnit(id)`, `FindWorkUnit(reference)`, `ListWorkSubmissions(workUnitId)`,
`GetEntitledSource(id)` and `ListEntitledSources(workUnitId)` inspect retained
facts. `FindWorkUnit` checks supplied pins without adding pins or submissions.
These reads take no writer reservation. Source lists are ordered by source ID.

An accepted Issue submission can begin one `RequestBundle` capture with opaque
acquisition inputs, policy and limits. Register each selected reference as it is
discovered, then persist its first source snapshot or exact local Git blob before
continuing. The reference set may grow while capture is incomplete, including
after reopening the store; replaying a committed capture returns its original
identity and never refreshes it. `CompleteRequestBundleCapture` seals the reached
membership and records a manifest plus SHA-256 digest. A completed bundle cannot
gain references or change its identity, manifest, inputs or captured objects.
Capture requires an unbound submission, so `AssociateIssueSubmission` refuses a
submission whose bundle is still capturing; association and completion are
ordered by the store's writer reservation.
`ReadRequestBundleReference(bundleId, referenceId)` only serves captured members
of a sealed (completed or refused) bundle. It returns the immutable source bytes or reads the exact
Git blob through its Broodling-pinned commit, checking the recorded digest. Git
content remains in the source repository's retained object store; it is not
copied into a second archive. These checkpoint operations record inputs without
defining reference-selection policy or performing remote traversal;
[`CaptureRequestBundleAsync`](dotnet-github-ingress.md#executable-request-capture)
owns that policy. A refused bundle keeps its captured members, seals its
membership and retains its findings in `RequestBundle.Findings`.

For a service-owned GitHub preparation, `PrepareRequestBundleRepositoryAsync`
retains the selected canonical repository, default PR branch, requested branch
ref and exact starting commit in an immutable bundle-scoped row. It reuses the
existing Git retention pin and does not retain credentials. Repository-file
registration uses that starting commit as its Git-blob revision input; it never
re-resolves the default branch. See [GitHub preparation](dotnet-github-ingress.md)
for the credential and existing-bare-repository boundary.

`SourceSubmission` is the trusted low-level presentation boundary from baseline
`entitlement.py`, not the later supplied-source Contract ingress. Trusted
`caller`/`broodling_policy` origins can present material; the Work Unit's exact
canonical primary issue locator receives the existing implicit policy grant.
Other kinds (`referenced_document`, `repository_file`, `caller_statement`) require
an explicit caller/policy grant with a nonempty basis. Unrecognized and model,
candidate or referenced-material origins refuse even if they claim a valid grant.
Payload bytes are never parsed to decide entitlement. A2 applies the
additional ingress restrictions: supplied sources require explicit caller grants,
and only actually acquired/validated primary issue bytes get policy acquisition.

Malformed UTF-16 is refused before source hashing/retention and WorkReference
canonicalization. SQL string parameters are also checked before binding, including
lookup keys, so a lone surrogate cannot alias a legitimate replacement character
or change retained provenance. Source/reference refusals use their existing domain
errors; other malformed SQL text uses `invalid_text`. Payload bytes remain arbitrary.
Well-formed text, including U+FFFD and supplementary characters, keeps its existing
identity bytes and remains readable/replayable in existing .NET state. No schema
change or historical-state rewrite is involved; Contract text already validates
Unicode before canonical serialization.

Snapshot identity includes Work Unit, source kind, exact locator and byte digest.
Identical capture returns the first stored snapshot, including its original
media type, retrieval/recording times, origin and grant. New bytes produce a new
snapshot. Submission and returned source objects copy their byte arrays; callers
cannot mutate retained payloads through a shared array. Database triggers also
refuse source updates/deletes, identity rewrites/unpinning and submission rewrites.

## State lifecycle and persistence decision

Initialization exclusively reserves a new filesystem path and creates a distinct
`broodling.application` schema (version 1). It refuses existing files and orphan SQLite
sidecars. A failed initialization retains its partial new state for inspection.
Store paths inside a marked disposable Attempt enclosure refuse, including paths
through parent symlinks. Caller paths containing malformed UTF-16 refuse with
`invalid_store_path` before physical path resolution or any filesystem access:
a lone surrogate cannot create, open or upgrade a legitimate U+FFFD filename.
Well-formed replacement and supplementary characters remain valid path text.
Open first checks the format/version/definition identity row through an
immutable read of the main file alone, so refusal never replays a journal,
checkpoints a WAL or creates sidecars. That row is committed before WAL is
enabled and never rewritten. Because the read takes no locks, it can overlap a
checkpoint rewriting the main file and see a current store as malformed or
without its row, sometimes for more than 100 ms under load. The probe therefore
reads again after a delay that starts at 10 ms and doubles up to 200 ms, for
about 2 seconds, and refuses only when no read confirms the identity. Foreign or
pre-transition state fails every read, so only a refusal waits that long. Only a matching store is then opened in SQLite
read/write mode without create, where the full identity, retained schema
manifest and installation control are checked before WAL and full
synchronization are configured. An incompatible or
unknown file is not initialized or rewritten. Foreign keys and immediate write
transactions enforce custody.

The HTTP integration requires fresh state. Pre-transition `broodling.dotnet`
stores (schemas 1–12), Python databases and foreign files are unsupported:
ordinary open and `UpgradeStore` refuse them with `incompatible_store` without
changing their files, and there is no migration, import or history reader.
`UpgradeStore` accepts an already-current store unchanged; it upgrades no
earlier identity. `initialize-store` and `upgrade-store` report the exact
identity (format, version and definition SHA-256) for the release record. Until
the first `v*` version freezes version 1 in
[`application-schemas.json`](../../deployment/application-schemas.json), schema
additions edit the version-1 definition in place; a store initialized from an
earlier in-progress definition fails the definition-hash check and is refused,
not upgraded. A frozen definition is never edited: a later change increments the
version, and a released earlier identity that `UpgradeStore` does not upgrade is
refused unchanged with `released_schema_refused` and its documented reason
([application schema compatibility](../../deployment/README.md#application-schema-compatibility)). Existing stores must remain at separate paths and must
never be silently replaced.

Direct `Microsoft.Data.Sqlite` 10.0.12 is used instead of EF Core. The operations
need explicit writer acquisition, short atomic writes, immutable SQL constraints
and simple reads. EF mapping/change tracking would not reduce this transaction
or constraint code. There is one session implementation, with no repository or
unit-of-work wrapper hierarchy. The provider supports the required
[immediate transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
and [non-creating connection mode](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings).

## Operator commands and validation

Run these commands deliberately against a separate .NET path:

```bash
dotnet run --project src/Broodling.Host -- initialize-store /srv/broodling-dotnet/state.sqlite3
dotnet run --project src/Broodling.Host -- upgrade-store /srv/broodling-dotnet/state.sqlite3
dotnet test --solution Broodling.sln
```

The two commands compose the application API, emit JSON on success, and return
nonzero with a stable safe error code on failure. They do not start HTTP or
install/deploy anything. Ordinary host startup serves HTTP over existing state; see the
[release guide](../../deployment/README.md).

TUnit tests use real SQLite for canonical replay/reopen, per-component
non-aliasing, concurrent identity/source convergence, competing pins, atomic
rollback, immutable source bytes/provenance and direct SQL amendment refusal.
Lifecycle cases preserve existing bytes when open/upgrade/initialize refuse
missing, foreign, Python-shaped, corrupt or incompatible state, including
authentic pre-transition schema-1, -10 and -12 stores and a schema-12 store
whose last write exists only in an uncheckpointed WAL. These supplement
the [exact frozen Python baseline](../migration/130-baseline-validation.md);
the [retirement record](../migration/140-retirement.md) describes the current gate.

Validation on 22 September 2026 with .NET SDK 10.0.401: the command above passed
34 tests, with zero failures or skips. A separate compiled-host CLI smoke
initialized new state, reopened it through `upgrade-store` with the same
initialization timestamp, and refused repeated initialization with exit code 1
and a safe `store_exists` response. Python production code is unchanged; the
independent exact-baseline run is recorded in the linked migration evidence.
