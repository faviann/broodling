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
  unchanged.
- A proposal that is not a valid Contract, or that changes the branch, binding,
  pin, Work Unit or producer, records no revision. Its finding (the refusal's
  safe code and detail) is retained in `contract_proposal_refusals`, the
  submission moves to `rejected` and the call throws `ContractProposalRefused`.
  `IssueSubmission.ProposalRefusal` exposes it, including through the
  read-only HTTP submission reads. The refusal is final: later calls report it
  without proposing again, and the submission can never bind a Contract. Like a
  capture refusal, it grants nothing, so one reached during a pause is retained.
- Unsupported obligations, prerequisites and other refusals remain rejection
  findings, exactly as for supplied sources.

The revision is recorded and associated with the submission in one transaction.
`Admit` then decides it, applying the submission cancellation and installation
pause checks. The decision also moves the submission from `capturing` to
`admitted` or `rejected`. An interruption before the decision leaves the bound,
undecided revision. A later call decides it without calling the proposer again,
and it returns an existing decision even while paused, as `Admit` does. A new
proposal refuses under pause, and a cancelled submission is refused before the
proposer runs. The pause is checked before the proposer runs, not again when the
proposal commits: a proposal that finishes during a pause is still bound, and
only its decision waits for release. When concurrent callers propose for one submission, the first
association wins. The other caller discards its uncommitted proposal and returns
the winning revision's status. The first committed refusal or association
likewise stands over a later refusal or association. No transaction is open while the proposer
runs.

### Bundled proposer

`AdmitRequestBundleAsync(submissionId, credentials, cancellationToken)` runs the
same admission with the one built-in proposer
([#112](https://github.com/faviann/broodling/issues/112)). It is not
caller-selectable and has producer `model_extraction`. It calls the supported
model `gpt-5.6-sol` through the OpenAI-compatible Chat Completions API
(`POST /chat/completions`, JSON-object response format) at exactly
`https://cliproxy.local.faviann.com/v1`. The
[operations guide](../../deployment/README.md#bundled-contract-proposer)
describes its configuration.

- The initial context is the Executable Request text, the fixed authority
  (Work Unit, request pin, PR effect, producer and bundle binding) and the same
  compact manifest native agents receive (#114), projected from the
  digest-verified retained manifest: each member's reference identity, capture
  kind, selector, digest and any Git commit and path, without its content. Fixed instructions describe the output
  shape. They tell the model to preserve every requirement, expressing other
  requested actions as obligations and unmet conditions as prerequisites, and
  never to change the authority.
- The model reads members on demand with one `read_reference` tool. Each read
  goes through `ReadRequestBundleReference` for this bundle, so it returns only
  frozen captured bytes: a non-member is an error reply, and nothing is fetched
  upstream or read from a working tree. A read returns at most 64 KiB, with an
  offset to continue; one proposal reads at most 512 KiB and makes at most 8
  model calls. The last call forbids tools (`tool_choice: none`), so the model
  must answer.
- The final reply is parsed into a `Contract` with its canonical field names;
  unrecognized fields and repeated properties are refused. An omitted authority field takes its fixed
  value. A supplied one is kept, so any change refuses rather than being
  corrected. A reply that is not such a JSON object is a malformed proposal. Both
  are retained refusals, as above.
- Operational failures retain nothing and throw `ContractProposerError`:
  missing or invalid credentials and other gateway refusals (`Retryable` false),
  and transport failure, a 3-minute call timeout, HTTP 408/429/5xx, a reply cut
  off at the output limit (`finish_reason: length`), any other finish reason
  than `stop` (absent counts as `stop`; `tool_calls` only with tool calls),
  non-text content, tool calls on the final call, or another unusable gateway
  response or tool call (`Retryable` true). A text-free `stop` reply remains a
  retained refusal. The submission stays
  `capturing`, and a later call proposes again from the same frozen inputs. A
  bound or refused submission is never proposed again. Neither the key nor the
  gateway's response text appears in an error or a retained record.

A frozen-state integrity or custody failure during a proposal, such as a failed
compact-manifest check or an on-demand read that fails for a reason other than an
unknown member, throws a conflict that needs attention. It is neither a retained
refusal nor a `ContractProposerError`, and it retains nothing.

The proposer runs only after capture completes and cannot add bundle members.
Controlled tests do not establish model interpretation quality; the supervised
review limitation applies to every admitted Contract.

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

## Submission preparation

`SubmissionPreparer.PrepareAsync(submissionId, githubCredentials, gatewayCredentials, cancellationToken)`
([#116](https://github.com/faviann/broodling/issues/116)) takes one exact accepted
Issue submission through [capture](dotnet-github-ingress.md#executable-request-capture),
which includes repository selection, and then `AdmitRequestBundleAsync`. It adds
no record or schema: progress and findings stay in the capture, refusal and
admission records above. The preparer is constructed with the application, the
store path, the service repository root and a lifetime token that is cancelled at
shutdown.

- It returns a `SubmissionPreparation`: `Decided` with the admission status
  (admitted, or rejected with its findings), `CaptureRefused` with the refused
  bundle, `ProposalRefused` with the retained refusal, `Cancelled`, or `Failed`
  with a safe code and message and `Retryable`. Retryable failures are
  retryable GitHub, repository and gateway failures, the installation pause, and
  a busy or locked SQLite store (`store_busy`). Other failures need attention:
  identity or bundle conflicts, missing credentials, an earlier unbound
  association, a store that is missing or incompatible, and any other SQLite
  error, such as a guard abort (`store_error`, with SQLite's error code and
  message). The preparer has no retry cadence; its caller decides when to call
  again.
- Every result except `Failed` is retained, so a later call returns it again
  without acquisition or a model call. A cancellation or refusal that commits
  while preparation runs is returned instead of the failure it causes.
- A later call continues from committed checkpoints. Capture resumes without
  refetching committed members. A proposal interrupted before its Contract
  commits repeats against the frozen bundle. Once a Contract is associated, the
  submission is never captured or proposed again, and a later call only decides
  it.
- The existing checks apply unchanged. Capture stops at a committed
  cancellation. Proposal and decision refuse a cancelled submission. While the
  installation is paused, a new proposal and an undecided Contract's decision
  return the retryable `installation_paused` failure. Capture itself does not
  check the pause.
- One process holds one preparer. Concurrent callers for the same submission
  share its single in-flight preparation, and different submissions prepare
  independently. The caller that starts a preparation supplies its credentials.
  Only the lifetime token stops a preparation. A caller's token ends only its
  own wait, so a disconnecting caller neither cancels the shared work nor wastes
  its model call. Each preparation runs on the thread pool in its own store
  session. As in capture and admission, no SQLite writer transaction is open
  during acquisition or the model call.
- There is no cross-process lease. If another process prepares the same
  submission, the store's guards keep the first committed result, but the other
  preparation can report a conflict that needs attention even though a later
  call continues from what was committed.

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
