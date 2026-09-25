# .NET source-attributed Contract admission

[A2 #133](https://github.com/faviann/broodling/issues/133) adds supplied-source
admission and retained revision inspection to the [A1 store session](dotnet-identity-custody.md).
It ends at an immutable admission decision. [B adds Attempt allocation and Git
custody](dotnet-attempt-allocation.md). GitHub acquisition, Git materialization
and execution belong to their owning seams. See the
[release/cutover guide](../../deployment/README.md) for current operations.

## Callable application path

Use an explicitly initialized .NET store, independently of HTTP:

```csharp
var application = new BroodlingApplication();
using var store = application.OpenStore("/srv/broodling-dotnet/state.sqlite3");
var reference = WorkReference.Parse("acme/widget", 123);
var status = store.AdmitSources(reference,
    [new SourceSubmission("primary_issue", reference.IssueLocator, requestBytes,
        entitlement: new("caller", "Reviewed supplied request bytes"))],
    input => new Contract(input.WorkUnit.WorkUnitId, input.SourceAttribution,
        [new Criterion("request", "Implement the complete reviewed request.")],
        requiredEffects: input.RequiredEffects, constructedBy: input.ConstructedBy),
    requiredEffects: [new RequiredEffect("pr", "Open a PR targeting main.", "pull_request", "main")],
    constructedBy: "caller");

var revisionId = status.Revision.ContractRevisionId;
Console.WriteLine(status.Decision!.Outcome);
```

`requiredEffects` is mandatory, including an explicit empty collection for no
effect. Every supplied source requires caller origin and an explicit caller
grant with a nonempty basis. Exactly one primary issue must name the Work Unit's
canonical issue locator. Supplied bytes are not upstream-verified; there is no
implicit fetching of links, comments or repository guidance.

The caller callback receives a `ContractProposalInput` containing immutable
Work Unit/source records, all exact source pins, copied effects and the declared
producer. A proposal must preserve the complete source pin set, Work Unit,
producer, and every effect field and its sequence. Older entitled snapshots,
foreign sources, duplicate pins and omitted material refuse. Sources captured
before a malformed or throwing proposer remain durable; there is no revision
or admission for that proposal. Collections are defensively copied and read-only,
including nested guidance, so the callback cannot amend frozen caller grants.

Criteria alone can be admitted. Optional evidence population, validation seam,
action, falsifying observation and mechanical guidance remain exact task context;
Broodling neither executes nor requires that guidance. Unsupported obligations
and effects, every effect-dependent evidence declaration, unsatisfied
prerequisites, unsupported/PR-contradictory host assumptions and any selected-final-
material request (including an empty selection) produce preserved rejection
findings. Delivery admission supports no effect or one GitHub `pull_request`
effect naming a nonempty target branch. This grants no implicit merge, deployment
or wider effect authority and does not resolve the no-effect stable-result gap.

## Bundle-bound admission

`AdmitRequestBundle(submissionId, propose, constructedBy)` admits one Issue
submission's completed [RequestBundle](dotnet-github-ingress.md#executable-request-capture)
under the trusted URL-to-PR profile. It uses the same proposal checks and `Admit`
as supplied sources; there is no second Contract pipeline.

- It takes its authority from the digest-verified manifest. The bundle must be
  complete, and its manifest must match the retained digest and name this
  bundle, submission and Work Unit. The attributed Executable Request is the
  manifest's `request` member, whose retained source must be this Work Unit's
  `executable_request` at the recorded digest. The PR target branch is the
  manifest's repository selection, which the retained preparation row must
  equal.
- The proposer receives that Executable Request as its only source, plus
  `RequestBundle` for the manifest and members. The primary issue and available
  references are supporting material read through the bundle. A proposal that
  attributes them refuses, so their capture cannot add requested work.
- Broodling supplies one `pull_request` effect to that retained PR target
  branch. The proposal must preserve it and carry `BundleBinding`
  unchanged. A changed branch, binding, pin, Work Unit or producer refuses and
  records nothing.
- Unsupported obligations, prerequisites and other refusals remain rejection
  findings, exactly as for supplied sources.

The revision is recorded and associated with the submission in one transaction.
`Admit` then decides it, applying the submission cancellation and installation
pause checks. The decision also moves the submission from `capturing` to
`admitted` or `rejected`. An interruption before the decision leaves the bound,
undecided revision. A later call decides it without calling the proposer again,
and it returns an existing decision even while paused, as `Admit` does. A new
proposal refuses under pause, and a cancelled submission is refused before the
proposer runs. When concurrent callers propose for one submission, the first
association wins. The other caller discards its uncommitted proposal and returns
the winning revision's status.

A bound Contract's canonical JSON carries `requestBundle` with `bundleId` and
`manifestSha256`, so its revision identity covers the exact bundle. The field is
omitted when absent: Contracts recorded without a bundle keep their exact bytes
and identities. Public `RecordContractRevision` refuses a bound Contract, and
`AssociateIssueSubmission` gives a bundled submission only a Contract bound to
its own bundle. Each bound revision therefore belongs to one submission, and its
admission is always submission-guarded.

One store rule governs progression. Once an associated Issue submission's
RequestBundle has completed, the revision must carry exactly that bundle's
identity and manifest digest. `Admit`, every Attempt admission and replacement
check this rule in their committing transaction, before any replay. So do
`AdmitRequestBundle` replay, because it decides through `Admit`, and
`AssociateIssueSubmission`, including its idempotent replay. A bound Contract's
Attempt must also start from exactly the bundle's retained preparation. A
caller-selected repository or revision is refused.

State written before #111 can associate a completed bundle with an unbound
Contract. That association stays readable through `Status`, `History` and
submission reads. Every route that would decide it, confirm it or start or
replace an Attempt from it refuses with `IssueSubmissionConflict`, and nothing is
converted into bundle authority. Revisions with no bundled submission, including
supplied-source Contracts, use the revision-based APIs unchanged.

## Persistence, recovery and observation

`RecordContractRevision(contract)` atomically records the canonical Contract and
all entitled-source bindings. `Admit(revisionId)` separately records or replays
the immutable decision. An interruption between these steps leaves an undecided
revision; `IsAdmitted` is false for both undecided and rejected revisions. Calling
`Admit` after reopening can decide that exact revision. Admission creates no
Attempt or execution.

Equivalent meaning reuses the original revision, decision, timestamps and lineage,
even after newer revisions exist. Meaning changes append a numbered revision
with its predecessor ID. Source pins are an unordered set normalized by ordinal
source ID/digest; all other sequences retain their order. Canonical UTF-8 JSON
and its SHA-256 are a new .NET representation, with no Python serialization or
database compatibility requirement. Reading checks the retained canonical bytes,
identity and exact binding set. Database guards prevent revision, binding and
decision amendments, including adding an unlisted source.

`Status(revisionId)` returns `AdmissionStatus`: Work Unit, exact pinned source
bytes/provenance, canonical revision/Contract and nullable decision/findings.
It reads one coherent deferred SQLite snapshot without reserving the writer.
`History(reference)` returns recorded statuses oldest first, each in its own
coherent snapshot. History checks supplied upstream IDs only against already
known pins; it creates no Work Unit, submission or previously unknown pin.
Neither operation reacquires sources, reruns the proposer or calls native code.

The existing thin operator entry point exposes the same observations:

```bash
dotnet run --project src/Broodling.Host -- status /srv/broodling-dotnet/state.sqlite3 cr-...
dotnet run --project src/Broodling.Host -- history /srv/broodling-dotnet/state.sqlite3 acme/widget '#123'
```

JSON `sources[].content` and `revision.canonicalBytes` are base64, retaining exact
binary material alongside the decoded `revision.contract` and findings. Unknown
records and incompatible state produce nonzero safe error responses. These
commands add no HTTP endpoints or deployment workflow.

## Schema and parity evidence

The A2 slice introduced .NET schema version 2;
[the fresh `broodling.application` schema](dotnet-identity-custody.md#state-lifecycle-and-persistence-decision)
retains its definitions.

On 22 September 2026, .NET SDK 10.0.401 ran
`dotnet test --solution Broodling.sln --no-restore`: **60 passed, 0 failed,
0 skipped**. `ContractIngressTests` composes admitted/rejected supplied sources,
reopen and exact operator inspection. `ContractPolicyTests` carries guidance
and supported-profile refusal witnesses. `AdmissionPersistenceTests` uses real
SQLite for write rollback, undecided recovery, immutable decisions, concurrent
convergence, corruption refusal and observation during an active writer.
Rollback tests inject
SQLite failures inside the actual transactions; they do not claim process-kill
coverage or reproduce the Python crash harness.

These witnesses map to the frozen Python Contract/admission/ingress/crash and
invocation-observation behavior in the [migration map](../migration/130-parity-map.md).
Python production/tests remain unchanged from the independently validated
[404-test baseline](../migration/130-baseline-validation.md).
Deterministic admission cannot certify natural-language extraction completeness
or semantic correctness; the governing operator-review limitation remains.
