# Issue #83: prepared, before the first v6 R01 admission

The [preparation evidence](../runs/2026-09-19-v6-r01/README.md) binds the fresh
repository, original inputs/store and corrected running target. Preparation is
not live authorization. R01-R08 remain `NOT_STARTED` and no Attempt exists.

From `/home/faviann/repos/broodling`, the safe read-only verification is:

```bash
.venv/bin/python evaluation/p5/v6/run_r01.py check
```

Use the supported `.venv` and current ephemeral `GATEWAY_API_KEY`, `GH_TOKEN`, and
exact `GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`. Never place secret
values in arguments, transcripts, evidence or credential files. Clear conflicting
legacy variables (`OPENAI_API_KEY`, `OPENAI_BASE_URL`, `GITHUB_TOKEN`,
`ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `GOOGLE_API_KEY`). Target origin
`http://127.0.0.1:18768` is separate from the gateway endpoint.

Only after fresh owner authorization for **v6 R01 alone**, with the owner actively
supervising and able to stop the target, the action that starts/counts R01 is:

```bash
.venv/bin/python evaluation/p5/v6/run_r01.py start --authorize-r01
```

This repeats preflight, including `/usr/bin/gh` in the selected actual container,
then writes the durable start/counting marker immediately before first Contract
admission. It admits the original Contract, provisions one Attempt and submits the
fixed native PR invocation. No R02 operation exists. Any start marker consumes
R01 even if admission fails or the process crashes. Never remove/reset it, rerun
`start`, replace the Attempt, repair/publish a PR manually or reuse v5 resources.

After dispatch, the start process exits following durable correlation. Observe
the existing run through credential-free SDK inventory (`Client.list_runs()` with
`DirectTarget("http://127.0.0.1:18768")`, `environment={}`) until that exact
correlated run has phase `finished`. Do not poll with `finalize`: each invocation
reserves one of the two permitted caller reattachments. Do not dispatch an
additional probe or reconstruct a run from provider history.

In a fresh process, consume the already-completed result before first disposition:

```bash
env -u GATEWAY_BASE_URL -u GATEWAY_API_KEY -u GH_TOKEN \
  -u OPENAI_API_KEY -u OPENAI_BASE_URL -u GITHUB_TOKEN \
  -u ANTHROPIC_API_KEY -u GEMINI_API_KEY -u GOOGLE_API_KEY \
  .venv/bin/python evaluation/p5/v6/run_r01.py finalize
```

The driver retains the completed-result/detached-caller evidence, native result,
receipt and atomic disposition separately, then closes/reopens the store and
verifies identical retained result/disposition with submission and native waits
forbidden. `replay` with the same credential-free prefix can recover that last
verification if interrupted after disposition. It cannot execute a provider.
Acknowledgment-loss reconciliation is not automated: retain the original
invocation/store and apply only the frozen bounded same-request/key policy.

Use the same credential-free prefix with `stop` for the known run's native
abandonment/stop boundary. External containment is available to the current owner:

```bash
docker stop broodling-p5-v6-r01-target
```

Preserve the original store/source/workspaces and target state/home after every
dispatch, including failure or stop. Neither terminal status nor external stop
authorizes cleanup or replacement.

After a receipt-backed disposition, verify the actual PR repository/base/exact
`headRevision`; fetch and bundle B1 plus that exact head outside target mounts and
execution workspaces. Verify bundle recovery before teardown. Run the frozen
v1 judge with credentials removed against those retained Git objects, then
independently review every scope/behavior criterion. Retain an honest success or
FAIL/BLOCKED settlement and all-started accounting, update #62, and stop. R02-R08
need a successful compatible R01/CO gate and separate authorization.
