# Broodling tests

The [current architecture and operating status](../docs/governing/current.md)
governs scope. This README describes current validation, not a continuation of
removed custom-assurance gates or a real-provider quality verdict.

The default suite tests Broodling's own decisions and its public Zeroshot seam.
It does not maintain a second execution model or prove Zeroshot's graph routing,
review/repair loops, dependency handling, or process supervision.

## Run

Install the project's pinned release SDK and test dependencies:

```bash
python -m pip install -e '.[test]'
python -m pytest tests
```

The .NET migration has a separate TUnit suite, including real SQLite identity,
source custody, immutable Contract admission and deliberate state lifecycle/upgrade
checks. See the [A1 application API](../docs/implementation/dotnet-identity-custody.md)
and [A2 admission/observation evidence](../docs/implementation/dotnet-contract-admission.md).
It does not yet replace or reduce the Python parity suite:

```bash
dotnet test --solution Broodling.sln
```

Python 3.13+, SQLite 3.37+, Git, and the Linux x86-64 release wheel are required.
The controlled provider fixture needs no credentials or network. There is no
separate opt-in Zeroshot campaign: the small real-native integration checks run
in the default suite. Missing SDK dependencies must not be interpreted as a
successful integration check.

Worktree tests use a durable root, by default
`~/.cache/broodling-tests`. Set `BROODLING_TEST_WORKSPACE_ROOT` to choose another
durable directory outside temporary roots. Only disposable native runtime
state/sockets use `/dev/shm`; production runtime state must survive reconnects.

`pytest-timeout` supplies a generous per-phase accidental-hang safety net.
It reports a stuck boundary; it is not a product deadline or evidence of
physical cleanup. The `signal` method allows normal fixture cleanup to run.
`python -m unittest discover -s tests` also runs the suite, without that plugin.

## What remains

- The installed single-host CLI reuses invocation authority and retains exact
  source/Contract bytes in JSON inspection. Deployment tests cover immutable
  installation replay/refusal, operator-reviewed issue byte pins, and actual-target
  configuration/dependency checks. They do not substitute for the separate
  [installation validation](../deployment/validation.md) or prove provider quality.
- Caller-facing invocation from explicit GitHub reference through receipt-backed
  disposition, with pinned lineage/status, repeated and reopened submission,
  recovery after lost dispatch acknowledgment or interrupted provisioning,
  admission/abandonment handback, authority conflicts and observation during a
  lifecycle write. These integration tests compose real Broodling services/local
  Git and control only the GitHub/SDK boundaries; they do not dispatch live
  provider work.
- Explicit GitHub work-reference ingress, exact issue snapshots, explicit caller
  source grants and effect authority, complete proposal/source attribution,
  ordering-independent ingress revisions, immutable replay, and deterministic
  refusal of unsupported capabilities. The
  [retained issue fixtures](fixtures/ingress/README.md) exercise current structured
  issue prose with controlled proposers and no network/provider execution.
- Work Unit identity, entitled source snapshots, immutable Contract/Attempt
  bindings, criteria-only admission, exact PR/no-effect delivery selection, and one current Attempt. A complete
  validation plan is not an admission precondition; effect-dependent evidence and
  unsatisfied prerequisites remain refusals.
- Exact original B1 provisioning and exclusive ownership of disposable
  worktrees, including Broodling's own concurrent-call/crash boundaries and
  retention of the selected commit and its tree/blob objects through
  Broodling refs after source/worktree refs, reflogs and history disappear and
  Git garbage collection runs. Missing objects and conflicting/symbolic pins
  refuse admission before an Attempt is acknowledged.
- Immutable invocation preparation, durable dispatch/correlation, acknowledgement
  loss (including unchanged-worktree DirectTarget replay), current PR credentials
  checks, and refusal of conflicting target, forge, or source identities.
- Fixed Codex `gateway` PR selection, exact
  `GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1`, ephemeral
  `GATEWAY_API_KEY`/`GH_TOKEN` handoff, suppressed ambient credentials, and
  rejection of legacy credentials in persisted target configuration. Synthetic
  credential rotation and process-death replay preserve the same request/key;
  credential-free wait/stop also cover historical OpenAI requests.
- Native PR receipt binding and stable `headRevision`; refusal to disposition a
  null-output no-effect run; reopened completed runs; current-Attempt authority;
  atomic result/disposition writes; repeatable detached waits; native failure;
  and refusal of late success after abandonment.
- Explicit local no-effect/user-configuration policy, direct-target PR source and
  credential seams, and refusal of legacy selected-material requests.
- Quarantine after dispatch: native terminal/stop labels never authorize worktree
  deletion or replacement. Never-dispatched retirement/retry still checks exact
  Broodling ownership.
- Explicit SQLite schema upgrades preserve historical result, disposition and
  Attempt bindings, with successful results retained by exact Attempt; ordinary
  open refuses missing, unrecognized and historical stores.

`test_workflow_result.py` exercises no-effect execution against the released SDK
and bundled native executable, and PR receipt handling through the same public
result interface. The
[controlled Codex fixture](fixtures/README.md) substitutes only the provider.
Tests assert the Broodling outcome and native receipt/run binding, not
Zeroshot's internal history.

`test_invocation.py` covers facade wiring, lineage, recovery handles and handback.
Exact SDK request fields and replay contents, detailed closability findings, and
Git/B1 materialization stay in the submission, admission and provisioning suites.
HEAD drift in invocation recovery tests distinguishes reusing the recorded
Attempt from incorrectly admitting one again; those tests do not inspect Git
materialization.

Native failure, cancelled waits, foreign-run rejection, the no-effect result gap
and dispatched cleanup refusals stay at the coordinator seams in
`test_workflow_result.py`. The invocation stop scenario checks only stop wiring
and the facade's subsequent abandonment handback from `resume`/`submit`.
The invocation contention regression reads
`history`/`status` while real Attempt provisioning holds a lifecycle write
transaction, so acquiring the writer slot during observation fails immediately.
History also checks caller-supplied upstream identities through the existing
store identity rule; its regression covers conflicting/matching/omitted pins
and unknown references while SQLite is read-only and external calls are refused.

Gateway dispatch tests replace the public SDK client and never connect to
CLIProxyAPI or GitHub. These checks require no real gateway credentials and
neither admit nor execute a live evaluation run.
`test_gateway_runtime.py` materializes the pinned native gateway profile locally
without connecting to a target, verifying uniform runtime expansion and its
declared gateway environment names.

The two bounded Hypothesis modules remain narrowly focused:
`test_work_reference_properties.py` checks canonical identity/non-aliasing;
`test_store_state_machine.py` checks immutable admission, repeatable operations,
irreversible abandonment, and current-Attempt authority. Their deterministic
settings live in `property_support.py`; Hypothesis is a required test dependency,
not a product runtime dependency.

## What was removed

Custom assurance-graph, deterministic evidence-leaf, criterion-rationale,
live-observation, and containment-supervisor campaigns tested responsibilities
that no longer belong to Broodling. Those tests and their private execution
fixtures were deleted rather than ported onto the new engine. We do not retain
an opt-in historical lane as a current acceptance gate.

The current suite does not establish real-provider semantic quality, resistance
to malicious escaped descendants, or a general physical-cessation guarantee.
Those are not implied by a green controlled-provider run.
Nor does it independently verify the supported local profile's trusted-host
precondition excluding operator-managed effect-capable MCP/extensions.

## Optional test-suite review

The `mutation` extra remains an on-demand tool, not a CI gate or a score target:

```bash
python -m pip install -e '.[test,mutation]'
mutmut run
```

Its default configuration in `pyproject.toml` limits mutation to identity
canonicalization and its two direct test modules. Widening that scope is a
deliberate review action, not a hidden part of the normal suite.

## Archived history

Prior qualification outcomes, campaign tooling and old test notes apply only to
their recorded implementations. They remain available in the
[pre-cleanup Git tree](https://github.com/faviann/broodling/tree/348e1f469c04fecbc24f4088e6eb438a3934e872)
and do not define current coverage or transfer a pass to this release.
