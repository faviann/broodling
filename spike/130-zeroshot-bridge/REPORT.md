# Spike report: C# to pinned Zeroshot, issue #130

Bounded technical spike. Evidence branch `spike/130-zeroshot-dotnet-integration`,
commit `11355bc`, not merged and not proposed for merge. Nothing here is a
migration slice or an adopted design.

## Environment and exact versions exercised

| Component | Version |
| --- | --- |
| Host | Linux 6.17.13-2-pve, x86-64 |
| Base commit | `0a36793`, current `main`, which is `b3f61a9` plus the #131 .NET bootstrap |
| .NET SDK | 10.0.401, Microsoft.Testing.Platform runner |
| Test framework | TUnit 1.68.17 |
| Python | 3.13.5, project `.venv` |
| Zeroshot Python SDK | `the-open-engine-zeroshot` 10.3.0.post1, official Linux x86-64 wheel |
| Zeroshot native | `zeroshot 10.3.0`, the executable bundled in that wheel |
| Codex provider | controlled fixture reporting `codex-cli 0.153.4`. No real Codex CLI, no provider account |
| Git | 2.47.3 |
| SQLite | 3.46.1 |

No network, no credentials and no paid provider were used. Twelve of the
fourteen scenarios drive the real released SDK and its real native engine
through the repository's existing controlled-Codex technique. Two use a
controlled stub SDK, for the one case the released engine cannot reach offline.

Supporting runs at this commit. `dotnet test spike/130-zeroshot-bridge/dotnet/Spike.Zeroshot/Spike.Zeroshot.csproj`
reports 14 passed in 33s. `dotnet test` reports 1 passed, the spike project being
outside `Broodling.sln`. `.venv/bin/python -m pytest tests` reports 404 passed
and 299 subtests passed in 132s. That last figure is the current `main`, not an
exact-baseline validation of `b3f61a9`, which #130 still lists as an open
prerequisite.

## What was built

A C# client, a Python bridge of about 110 lines, and one controlled dispatch
fixture. The bridge takes one JSON request on stdin, writes one JSON response on
stdout, and exits. One process per call. Four operations: `version`, `submit`,
`wait`, `stop`. It holds no state, keeps no connection, and translates every
field to the SDK unchanged.

## Scenarios tested and observed results

Each row is an executed test in `dotnet/Spike.Zeroshot/BridgeTests.cs`.

| Scenario | Observed |
| --- | --- |
| Pinned version reachable from C# | Bridge reports `10.3.0.post1`. The pin is enforceable from the C# side |
| No-effect dispatch and wait | `succeeded=true`, `failure=null`, `output=null`. The current stable-result gap, reproduced through the seam |
| Identical replay under the same submission key | Returns the same run identity, no conflict raised |
| Different request under the same key | `SubmissionConflictError`, carrying the existing run identity |
| Identical request, same key, worktree HEAD advanced | Also `SubmissionConflictError`, same identity, same message text |
| Reconnect after the workspace is deleted | Wait succeeds from locator plus run identity alone, with an empty process environment |
| Repeated wait on a finished run | Returns the identical terminal result each time |
| Cancelling a wait mid-run | Caller detaches. The follow-up wait blocks for the remaining execution and then succeeds |
| Killing the bridge process mid-wait | Same as cancellation. No run state is lost |
| Explicit stop on a running run | Returns `succeeded=false`, `failure="force_stopped"`. Later waits return that identical terminal result |
| Unknown run identity | `RunNotFoundError`, typed, no fallback |
| DirectTarget reconnect with no credentials | Reaches transport and fails with `TargetError`. No credential or configuration was demanded first |
| Full pull-request receipt decoded in C# (stub SDK) | The seven-field `v1/pr/opened` receipt arrives unchanged |
| Reconnect and stop after a PR dispatch (stub SDK) | Succeed from locator plus run identity, with no secrets argument |

Process cost, measured separately. A cold bridge process answering `version`
takes about 0.35s, a `wait` on a finished run about 0.20s, and a `submit`
about 0.51s.

Process ownership, measured separately. After the bridge process exits, the
native run controller is alive with parent pid 298, which is `systemd --user`,
and the controlled Codex process is a child of that controller, not of the
bridge. The bridge is never an ancestor of the running work once it has
returned. That is why the C# client detaches with `Kill(entireProcessTree: false)`.

## Proposed minimal C# to Zeroshot interface

Three operations and two documents. Observed to be sufficient for every scenario
above.

```csharp
Task<string>     SubmitAsync(Dispatch dispatch, IReadOnlyDictionary<string,string>? secrets, CancellationToken ct);
Task<RunOutcome> WaitAsync(RunLocator locator, string runId, CancellationToken ct);
Task<RunOutcome> StopAsync(RunLocator locator, string runId, CancellationToken ct);
```

`Dispatch` carries the frozen locator, workspace path, environment map,
submission key, title, task, preset, uniform runtime, and the optional
repository/branch/revision source triple. `RunLocator` is a local state
directory or a direct-target origin, and nothing else. `RunOutcome` is the
SDK's four result fields verbatim, `runId`, `succeeded`, `output`, `failure`.

Two properties matter more than the shape. Secrets are a separate argument, so
they cannot reach the document the caller persists. The locator is a strict
subset of the dispatch, so reconnection provably cannot depend on anything that
was only true at dispatch time.

## Credential and input requirements

**Initial dispatch** needs everything. The frozen document above, plus the
no-effect Codex profile environment, which is `HOME`, `CODEX_HOME`, `PATH`
including the launcher directory, `BROODLING_REAL_CODEX`,
`BROODLING_PROFILE_HOME`, `BROODLING_ISOLATED_CODEX_HOME`, and the blanked
operating variables. Pull-request delivery additionally needs current
`GH_TOKEN`, `GATEWAY_BASE_URL` and `GATEWAY_API_KEY`, supplied only at the call.

**Acknowledgement-loss replay** needs exactly the same inputs again, byte for
byte, under the same submission key. Observed to return the same run identity
without a conflict. Credentials are required again because the call is a genuine
new dispatch attempt.

**Post-correlation reconnect, wait and stop** need the locator and the run
identity. Nothing else. Observed with the dispatch workspace deleted and the
bridge process started with an empty environment. This confirms the current
Python behavior is a property of the pinned API, not a Broodling convention.

## Submission key and conflict behavior

A submission key is idempotent for a byte-identical request and conflicts
otherwise. The conflict always names the existing run.

The important observation is negative. A genuinely different request and an
identical request whose resolved source has drifted produce the same exception
type, the same existing run identity, and the same message. The pinned API does
not separate them. Any caller that wants same-key recovery must decide locally,
from its own record of what it dispatched and what the source is now. The
current Python code already does exactly that, and a C# port must keep that
decision on the C# side.

## Cancellation versus stop

They are different operations with different outcomes, confirmed by observation
rather than by reading. Cancelling or killing a waiter detaches that caller and
nothing else. The run keeps executing, and a later wait blocks for the remainder
and returns the real result. Explicit stop is a separate request that
terminalizes the run as a failure, `succeeded=false` and `failure="force_stopped"`,
and that terminal result is then stable for every later wait.

Two consequences for the port. A stopped run reaches Broodling's disposition
path as a native failure, which abandons the Attempt. And a terminal
`force_stopped` label remains what the current integration document says it is,
not a physical-cessation receipt.

## Result and receipt behavior

`output` is passed through unchanged with no schema applied at the boundary. For
no-effect delivery the released engine returns `succeeded=true` with
`output=null`, which is the existing capability gap, now reproduced across the
C# seam. For pull-request delivery the seven-field receipt decodes unchanged in
C#, verified against the controlled stub only.

One ambiguity the transport does not add but also cannot remove. The SDK models
`output` as a JSON value defaulting to `None`, so a successful run with a JSON
null output and a successful run with no output are the same value at the source.
The current refusals already cover this, by refusing no-effect completion
outright and by requiring a dict-shaped receipt for pull-request completion.

## Bridge process failure and restart

The bridge owns nothing durable. Killing it mid-wait loses the in-flight call and
nothing else, because the run controller has already reparented to the user
session manager. Restart therefore carries no recovery protocol. The only failure
the caller must handle is an empty or malformed response, which must be treated
as an unresolved dispatch and never as a successful no-run, so that durable
dispatch intent survives for an identical replay.

One inherited limit, not introduced by the bridge. The local run controller lives
under `systemd --user`, so a .NET host must not assume it owns or outlives the
run.

## Unsupported or ambiguous behavior to fail closed around

- Same-key conflicts are ambiguous by construction. Treat a conflict as recovery
  only when the persisted request bytes are identical and local source drift
  explains it. Otherwise block.
- No-effect success has no accepted result. Keep the existing refusal.
- `force_stopped` and every other terminal label grant no cleanup or replacement
  authority. Keep the existing quarantine.
- An unanswered or malformed bridge call is an unresolved dispatch, not a
  no-op.
- Pull-request delivery against a real DirectTarget, and a real `v1/pr/opened`
  receipt, were not exercised. This spike used no forge and no gateway. That
  seam stays unproven, exactly as it is unproven in the current Python suite.
- When an explicit environment mapping is supplied, the pinned SDK treats every
  supplied value as a secret for redaction. Keep credentials out of the
  environment used for reconnection, which the locator design already forces.

## Recommendation

**The bridge is transport plumbing. Put it inside the migration's native
dispatch and recovery slice. Do not give it its own slice.**

The evidence, not the preference, is the reason. Its entire contract is four
operations and a field-for-field translation with no branch that is not "pass
through, or report the failure as data". Every judgment the port actually needs
is one the pinned API cannot make and C# must therefore own anyway, including
conflict versus recovery, credential presence, policy validation, locator
freezing, receipt validation and Attempt authority. It holds no durable state,
and the run outlives it, so restart carries no obligation. A separate slice
would place a review boundary around about 110 lines whose only contract is
"reshape nothing", and would create the one place where somebody is tempted to
put a decision.

A different process and a different language are not, on their own, a reason for
a separate slice, and here they are the only reason available.

### Regression tests worth keeping

Move these deliberately into the migration issue rather than merging this
branch. Each has a matching Python test today, so each is a parity witness.

1. Identical replay under the same submission key returns the same run.
2. A different request under the same key conflicts and names the existing run.
3. Source drift makes an identical replay conflict identically, so the
   discrimination test must live in C#.
4. Reconnect, wait and stop succeed from locator plus run identity after the
   dispatch workspace is gone.
5. Cancelling a wait detaches only, and the follow-up wait still blocks and
   succeeds.
6. Explicit stop returns `succeeded=false` with `failure="force_stopped"` and is
   stable afterwards.
7. No-effect success returns `output=null` and refuses disposition.
8. A full pull-request receipt decodes unchanged, against a controlled stub
   until a real one can be observed.
9. An unanswered bridge call leaves durable dispatch intent replayable.

The controlled-Codex technique used here transfers directly. The fixture at
`tests/fixtures/software-change-codex` drives the real engine offline, which is
what made twelve of these fourteen scenarios real rather than mocked.
