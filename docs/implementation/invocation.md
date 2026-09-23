# Caller invocation and recovery

The .NET `Invocation` composes explicit source admission, Attempt allocation,
owned worktree materialization and frozen native dispatch. It is callable without
HTTP. The [current authority](../governing/current.md) governs scope; the
[release guide](../../deployment/README.md) supplies operator configuration and
commands. The retired Python API is preserved at the
[frozen baseline](https://github.com/faviann/broodling/blob/b3f61a96c40401722ec16fc361958d1690982e02/docs/implementation/invocation.md).

| Operation | Current API and owning reference |
| --- | --- |
| Initialize, open or explicitly upgrade a store | `BroodlingApplication.InitializeStore/OpenStore/UpgradeStore`: [state lifecycle](dotnet-identity-custody.md) |
| Accept and inspect a URL-only Issue submission before Contract preparation | `BroodlingStore.SubmitIssue/FindIssueSubmission/GetIssueSubmission/IssueHistory`: [state lifecycle](dotnet-identity-custody.md) |
| Submit an explicit GitHub reference with typed proposer and exact effect authority | `Invocation.SubmitAsync`: [native dispatch](dotnet-native-dispatch.md#callable-application) |
| Resume the exact recorded revision | `Invocation.ResumeAsync`: [native dispatch](dotnet-native-dispatch.md) |
| Inspect retained revision/lineage without external calls | `BroodlingStore.Status/History`: [admission and observation](dotnet-contract-admission.md#persistence-recovery-and-observation) |
| Consume the correlated native result or replay retained completion | `Invocation.WaitAsync` / `BroodlingStore.WaitAsync`: [completion](dotnet-receipt-completion.md) |
| Abandon before requesting native stop | `BroodlingStore.StopAsync`: [lifecycle](dotnet-retirement-replacement.md) |
| Explicit safe retirement/replacement | `RetireAttempt`, `AdmitRetry`, `PrepareRetry`, `RetryAsync`: [lifecycle](dotnet-retirement-replacement.md) |

Use one disposable store session per caller. Keep the database, source Git,
Attempt roots and runtime state at their durable recorded paths. Ordinary open
never creates or upgrades state.

Retain `status.Revision.ContractRevisionId` and the exact Attempt ID from
`status.Attempts`; `status.Submissions` records native correlation. Status/history
use coherent reads without reserving SQLite's writer or refreshing native
progress. Completion belongs to the exact Attempt, not a moving Work Unit tip.

Repeating submit reacquires bytes and reruns the proposer; changed bytes or
proposal meaning can create another immutable revision. Resume continues only
the retained revision. Before initial allocation it needs the selected repository
and B1; afterward stored allocation governs. Interrupted pre-dispatch work can
continue, while acknowledgement-loss recovery reuses only the frozen request/key
and still requires current dispatch credentials and authority.

After durable correlation, resume needs no dispatch configuration or credentials.
Wait/stop reconnect using the frozen locator and run ID. A retained completion
returns without native access. Errors do not undo earlier durable steps: inspect
history after an interrupted submit to recover exact handles. Rejected,
abandoned or completed work is handed back without automatic replacement.

Cancellation/transport loss detaches a waiter. Native failure records abandonment;
invalid receipts or no-effect stable-result gaps refuse completion. Stop records
abandonment before requesting native stop. Every dispatched Attempt remains
quarantined, including unknown-run and terminal cases; stop does not prove
physical cessation. See the owning seams for detailed refusal codes and witnesses.

The [P5 human-review requirement](../../evaluation/p5/README.md) applies to every
accepted revision. No automatic progression, merge, deployment, execution
supervisor or broader effect authority is introduced by the host.
