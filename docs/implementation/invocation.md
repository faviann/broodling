# Caller invocation and recovery

The .NET `Invocation` composes explicit source admission, Attempt allocation and
native dispatch for one explicitly configured target: `InvocationTarget.Local`
(worktree materialization and the SDK bridge, for no-effect work) or
`InvocationTarget.Direct` (an HTTP DirectTarget Attempt for authorized PR work,
with no Python, SDK client state, workspace root or launcher). It is a callable
API; the [HTTP service](#http-service) maps submission, resume and stop onto these
operations. The [current authority](../governing/current.md) governs scope; the
[release guide](../../deployment/README.md) supplies operator configuration and
commands. The retired Python API is preserved at the
[frozen baseline](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/docs/implementation/invocation.md).

| Operation | Current API and owning reference |
| --- | --- |
| Initialize or open a store | `BroodlingApplication.InitializeStore/OpenStore/UpgradeStore`: [state lifecycle](dotnet-identity-custody.md) |
| Accept and inspect a URL-only Issue submission before Contract preparation | `BroodlingStore.SubmitIssue/FindIssueSubmission/GetIssueSubmission/IssueHistory`: [state lifecycle](dotnet-identity-custody.md) |
| Retain, resume, complete and read pre-Contract RequestBundle capture | `BeginRequestBundleCapture/RegisterRequestBundleReference/CaptureRequestBundleSource/CaptureRequestBundleGitBlob/CompleteRequestBundleCapture/GetRequestBundle/ReadRequestBundleReference`: [state lifecycle](dotnet-identity-custody.md) |
| Prepare a service-owned GitHub repository and consume its retained B1 | `PrepareRequestBundleRepositoryAsync/RegisterRequestBundleRepositoryFile/AdmitHttpAttempt(submissionId)`: [GitHub preparation](dotnet-github-ingress.md), [B1 custody](dotnet-attempt-allocation.md) |
| Admit a completed RequestBundle under the retained PR authority | `BroodlingStore.AdmitRequestBundle`: [bundle-bound admission](dotnet-contract-admission.md#bundle-bound-admission) |
| Prepare and admit that Contract with the bundled proposer | `BroodlingStore.AdmitRequestBundleAsync`: [bundled proposer](dotnet-contract-admission.md#bundled-proposer) |
| Submit an explicit GitHub reference with typed proposer and exact effect authority | `Invocation.SubmitAsync`: [target selection](zeroshot-native-integration.md#composed-invocation-and-target-selection) |
| Resume the exact recorded revision | `Invocation.ResumeAsync`: [HTTP dispatch](zeroshot-native-integration.md#http-dispatch-and-acknowledgement), [bridge dispatch](dotnet-native-dispatch.md) |
| Request revised work from one named, ended predecessor submission | `BroodlingStore.ReviseIssueSubmission`: [revised work](#revised-work) |
| Resume one Issue submission's admitted Contract from its retained B1 | `Invocation.ResumeSubmissionAsync`: [automatic progression](#automatic-progression) |
| Progress unfinished Issue submissions with no caller connected, for one process lifetime; continue one exact submission at the next scan | `SubmissionProgressor.RunAsync/Resume`: [automatic progression](#automatic-progression) |
| Inspect retained revision/lineage without external calls | `BroodlingStore.Status/History`: [admission and observation](dotnet-contract-admission.md#persistence-recovery-and-observation) |
| Serve those retained reads with bounded native observations over HTTP, and with processing configuration accept, resume and stop exact work while progression and completion observation run | `Broodling.Host` routes: [HTTP service](#http-service), [release guide](../../deployment/README.md#processing-server) |
| Pause, inspect drain status or explicitly release admission/dispatch | `BroodlingStore.PauseInstallation/GetInstallationStatus/ReleaseInstallation`: [installation pause](dotnet-installation-pause.md) |
| Read bounded, unretained native phase/active-node progress for a correlated Attempt, or a dispatched HTTP Attempt by its intended ID | `BroodlingStore.ObserveAsync`: [native integration](zeroshot-native-integration.md#dispatch-recovery-and-completion) |
| Consume the correlated native result or replay retained completion | `Invocation.WaitAsync` / `BroodlingStore.WaitAsync`: [completion](dotnet-receipt-completion.md) |
| Consume correlated HTTP results with no caller waiting, for one process lifetime | `CompletionObserver.RunAsync`: [automatic observation](dotnet-receipt-completion.md#automatic-completion-observation) |
| Abandon before requesting native stop | `BroodlingStore.StopAsync`: [lifecycle](dotnet-retirement-replacement.md) |
| Explicit safe retirement/replacement | `RetireAttempt`, `AdmitRetry`, `PrepareRetry`, `RetryAsync`: [lifecycle](dotnet-retirement-replacement.md) |

Use one disposable store session per caller. Keep the database, source Git,
Attempt roots and runtime state at their durable recorded paths. Ordinary open
never creates or upgrades state.

A new Attempt takes the configured target's kind. An existing Attempt continues
only through the kind its retained resources name; a mismatched target, or a
Direct origin that differs from the retained binding, refuses before allocation,
preparation or target contact. Authorized PR work is always an HTTP Attempt and
no-effect work always a worktree Attempt; neither falls back to the other.

Retain `status.Revision.ContractRevisionId` and the exact Attempt ID from
`status.Attempts`; `status.Submissions` records native correlation. Status/history
use coherent reads without reserving SQLite's writer or refreshing native
progress; `ObserveAsync` reads native progress separately. Completion belongs to the exact Attempt, not a moving Work Unit tip.

Repeating submit reacquires bytes and reruns the proposer; changed bytes or
proposal meaning can create another immutable revision. Resume continues only
the retained revision. Before initial allocation it needs the selected repository
and B1; afterward stored allocation governs. Interrupted pre-dispatch work can
continue, while acknowledgement-loss recovery reuses only the frozen request/key
and still requires current dispatch credentials (HTTP) and authority.

While paused, new ordinary admission, materialization/preparation and dispatch
initiation refuse. The explicit replacement path may still allocate and prepare its
successor (after verified maintenance retirement, only while paused); execution
and correlated/result-capture observation remain available.
Pause status reports two separate facts. `unresolvedDispatches` counts durably
`dispatched` submissions whose run is unknown; that uncertainty persists until
correlation and may persist forever for a quarantined abandoned Attempt.
An HTTP conflict or abandonment does not remove a submission from that count.
`inFlightInitiationDrained` is false while any local dispatch, HTTP send or
submit bridge still holds the installation initiation lock, including after
abandonment commits. A drained reading is local only: a request a target already buffered can
still create a run.

After durable correlation, resume needs no dispatch configuration or credentials.
Wait/stop reconnect using the retained run binding; an HTTP record needs no bridge
transport or Python. An HTTPS target whose certificate chains to a private root
needs a store session opened with `OpenStore(path, directTargetRootCertificate)`.
That root is operator configuration and is never retained, and the retained origin
still decides where each operation connects. A retained completion
returns without native access. Errors do not undo earlier durable steps: inspect
history after an interrupted submit to recover exact handles. Rejected,
abandoned or completed work is handed back without automatic replacement.

Cancellation/transport loss detaches a waiter. Native failure records abandonment;
invalid receipts or no-effect stable-result gaps refuse completion. Stop records
abandonment before requesting native stop. Every dispatched Attempt remains
quarantined, including unknown-run and terminal cases, until verified maintenance
retirement; stop does not prove physical cessation. See the owning seams for detailed refusal codes and witnesses.

## Automatic progression

[#117](https://github.com/faviann/broodling/issues/117) adds
`SubmissionProgressor`, which progresses accepted Issue submissions with no
caller connected:

```csharp
await new SubmissionProgressor(application, databasePath, directTargetRootCertificate,
    new InvocationTarget.Direct(directOrigin), preparer, () => currentCredentials(),
    (progress, failure) => report(progress, failure)).RunAsync(processLifetime);
```

- **Discovery.** At startup and then every 15 seconds it reads the unfinished
  submissions from the store. These are undecided submissions (no Contract, or
  one bound to the submission's RequestBundle, by the same rule that governs
  admission) and admitted ones whose Work Unit has no Attempt, or only its
  current HTTP Attempt for that Contract with no submission record or one that
  is `prepared` or `dispatched` without a replay block. For a
  [revision](#revised-work), ended Attempts of the Contracts preceding it do not
  count. Rejected, cancelled, unchanged,
  completed, abandoned and non-current work, Replacement Attempts, earlier
  unbound associations, and worktree or bridge records are never selected. The store is the only queue. Each submission has
  at most one operation in flight, and different submissions run independently,
  each in its own store session on the thread pool.
- **Operation.** It calls `IssueSubmissionPreparer.PrepareAsync` on the
  process's shared preparer. For an admitted decision it then calls
  `Invocation.ResumeSubmissionAsync` with the Direct target. That call admits the
  first HTTP Attempt from the RequestBundle's retained repository preparation
  (original B1), reopens the retained prepared submission (a different
  configured origin refuses) and sends or exactly replays it through
  `DispatchHttpAsync`. A correlated Attempt returns without credentials or target
  contact; the [completion observer](dotnet-receipt-completion.md#automatic-completion-observation)
  owns it from there. The service never initiates a stop, abandonment or
  replacement. Only that dispatch stops the exact run whose acknowledgement
  arrives after abandonment, as it does for any caller. Zeroshot checks out B1 itself, so no client execution
  workspace is created. The operations' own cancellation, pause, authority and
  replay-block checks apply unchanged, and none holds a SQLite writer during a
  GitHub, gateway or target call.
- **Credentials.** The provider is called at the start of each operation and
  again before continuation, so a rotation applies to the next replay. Nothing
  is retained. A provider that throws stops the submission for attention.
- **Retry.** Retryable preparation failures, `NativeTransportError` other than
  `request_too_large` (the frozen request's size never changes) and a busy or
  locked store are temporary. A retry runs at the first scan at least a delay
  after the failure: 15 seconds, doubling after each consecutive failure up to
  one hour. After 10
  consecutive temporary failures in one stage, about two hours, the submission
  stops. That window outlasts a routine target or gateway maintenance outage,
  while a failure that repeats on the same frozen input (#112) makes at most 10
  model calls. Preparation and continuation count separately, so failed
  proposals never use up the dispatch's retries. The installation pause is a
  wait for release, checked again at the first scan at least 15 seconds after
  the paused attempt, and does not count. Any other
  failure stops at once: a refusal or conflict such as a replay block, a
  retained binding to another origin or missing credentials. A failure caused
  by an end committed meanwhile elsewhere, such as a cancellation or
  abandonment, is not reported; the submission just leaves. Nothing is ever
  replaced.
- **Reasons.** `Progress()` returns each unfinished submission's state
  (`progressing`, `waiting` with its `retryAt`, or `stopped`), the last failure's
  stage (`preparation` or `continuation`), code, message and retryable flag, and
  its consecutive failure count in that stage. The
  required callback runs once when a submission stops, with the exception only
  for an unexpected failure; a callback that throws is ignored. Waits and stops
  live only in the process, so a restart discovers the submission again with a
  fresh limit. `Resume(submissionId)` has the same effect for one exact
  unfinished submission without a restart: it forgets that submission's wait
  or stop and failure count, so the next scan runs it; an operation in flight
  continues. It returns false and changes nothing for a submission that is no
  longer unfinished. A submission that leaves the unfinished set, for example through
  its replay block or a cancellation, leaves `Progress()`; the store retains
  that reason. An unexpected failure of discovery itself ends `RunAsync` with
  that exception.
- **Shutdown.** Cancelling `RunAsync` detaches every operation without stopping,
  abandoning or cleaning up anything. A preparation cancelled because the
  preparer's lifetime ended first, as at host shutdown, detaches the same way. Committed checkpoints remain, and an
  unresolved dispatch stays unresolved for exact replay by the next process;
  local drainage does not resolve it.

## Revised work

[#124](https://github.com/faviann/broodling/issues/124) adds
`ReviseIssueSubmission(predecessorSubmissionId)`. Ordinary submission of an issue
URL keeps returning its latest submission; revised work names its predecessor.

- **Transition.** In one SQLite writer transaction it returns the predecessor's
  existing successor, whatever later history exists. Otherwise it creates a new
  `accepted` submission for the same Work Unit, with the next sequence and an
  immutable link to the predecessor. Concurrent calls converge on that one
  successor. Earlier submissions are never amended; their exact reads gain only
  `successorSubmissionId`.
- **Eligibility.** The predecessor must be the Work Unit's latest submission and
  must have ended: no longer unfinished under the discovery rule above (for
  example rejected, cancelled, unchanged, or admitted with every Attempt ended
  by failure, stop or success). The Work Unit must have no current Attempt, and
  every Attempt with dispatch intent, successful or not, must have its
  `stopped_target` [maintenance retirement](dotnet-retirement-replacement.md#verified-maintenance-retirement).
  Otherwise it refuses with `issue_submission_conflict` and creates nothing.
- **Processing.** Progression captures the successor afresh through the
  ordinary preparer. Before any proposal, its completed bundle's manifest is
  compared byte for byte with that of each earlier submission whose
  bundle-bound Contract was admitted, with only the bundle and submission IDs
  blanked (the manifest records no capture time). Everything else is compared:
  acquisition inputs, policy and limits, the repository, PR target branch,
  starting revision and commit, and each reference's selection, source,
  content digest and pinned blob. On a match the successor ends `unchanged`
  with a retained explanation linking the latest matching submission and its
  Contract, whose Attempts carry the existing outcome; another execution of
  that authority uses explicit replacement. Material that never gained an
  admitted Contract, or any changed input, is proposed and decided as usual.
- **Authority.** The successor's Contract takes an ordinary first Attempt from
  its own retained B1; no Replacement Attempt is allocated. For that Contract
  alone, admission and explicit replacement disregard the completions, and the
  retired or never-dispatched ended Attempts, of the Contracts preceding it.
  SQL enforces the same rule, and one current Attempt per Work Unit still
  holds. Ordinary Work Units keep every completed- and ended-work guard.
- **Stops.** A cancellation stays bound to its first Attempt, and an Attempt
  stop to its exact Attempt, so replaying the predecessor's stops never reaches
  the successor.

## HTTP service

[#120](https://github.com/faviann/broodling/issues/120) attaches these services
to `Broodling.Host`. Configured with a Direct `config.json` (`Broodling:Invocation`)
and an existing service repository root (`Broodling:RepositoryRoot`), and
holding current `GH_TOKEN`, `GATEWAY_BASE_URL` and `GATEWAY_API_KEY`, the server
constructs one `IssueSubmissionPreparer` whose lifetime ends when the host starts
stopping, one `SubmissionProgressor` and one `CompletionObserver`, and runs both
services for the host's lifetime. It reads the credentials from its environment
for each operation, never retaining them. The required callbacks log a stopped
submission and an unexpected observation failure. If either `RunAsync` fails
unexpectedly the server stops and exits unsuccessfully, so it never keeps
accepting work that nothing would process. Without that configuration it serves
reads only; a configuration that cannot process, such as one setting alone, a
LocalTarget configuration, a missing root or missing credentials, refuses startup.

- **Submit** (`POST /submissions`) calls `SubmitIssue` and answers `202` with the
  retained submission and its `Location` once that commits. It contacts no
  GitHub, model or target: the progressor takes the submission at its next scan.
- **Revise** (`POST /submissions/{id}/revisions`) calls `ReviseIssueSubmission`
  and answers `202` with the successor and its `Location`, on creation and on
  replay, before any GitHub, model or target contact.
- **Resume** (`POST /submissions/{id}/resume`) calls `SubmissionProgressor.Resume`,
  so the process's one progression owner continues that exact submission; it
  never creates a Replacement Attempt. Ended or correlated work is handed back.
- **Stop** (`POST /submissions/{id}/stop`, `POST /attempts/{id}/stop`) requires a
  reason and calls `CancelIssueSubmissionAsync` or `StopAsync`, answering with
  the `stop` command's report so committed abandonment stays distinct from
  unconfirmed cessation. The stop runs to its own bounds whatever its caller
  does; only shutdown detaches it. Once the cancellation or abandonment commits,
  any later failure, including that detachment (`caller_detached`), is reported
  with it rather than as an error answer.
- **Reads** add to the submission and Attempt routes one bounded `ObserveAsync`,
  serialized with its availability apart from the retained facts, and the
  submission's in-process `Progress()` entry without its message, which only
  the server log keeps.

An Attempt whose completion observation failed unexpectedly is not observed
again until the server restarts; resume does not re-arm it.

Routes, status codes and configuration are in the
[release guide](../../deployment/README.md#processing-server). Shutdown detaches
both services as described above. The [P5 human-review requirement](../../evaluation/p5/README.md)
applies to every accepted revision. No merge, deployment, execution supervisor
or broader effect authority is introduced.
