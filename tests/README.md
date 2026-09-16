# Broodling tests

The default suite tests Broodling's own decisions and its public Zeroshot seam.
It does not maintain a second execution model or prove Zeroshot's graph routing,
review/repair loops, dependency handling, or process supervision.

## Run

Install the project's pinned release SDK and test dependencies:

```bash
python -m pip install -e '.[test]'
python -m pytest tests
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

- Work Unit identity, entitled source snapshots, immutable Contract/Attempt
  bindings, criteria-only/no-effect admission, and one current Attempt. A complete
  validation plan is not an admission precondition; effect-dependent evidence and
  unsatisfied prerequisites remain refusals.
- Exact original B1 provisioning and exclusive ownership of disposable
  worktrees, including Broodling's own concurrent-call/crash boundaries.
- Immutable invocation preparation, durable dispatch/correlation, acknowledgement
  loss, and refusal of conflicting target or source identities.
- Successful, null-output native results; reopened completed runs; current-Attempt
  authority; atomic result/disposition writes; repeatable detached waits; native
  failure; and refusal of late success after abandonment.
- Explicit local no-effect/user-configuration policy, optional selected-material
  retention, and refusal of unsupported material reads.
- Quarantine after dispatch: native terminal/stop labels never authorize worktree
  deletion or replacement. Never-dispatched retirement/retry still checks exact
  Broodling ownership.
- Known SQLite schema migrations and preservation of historical records without
  treating old proof rows as new workflow results.

`test_workflow_result.py` exercises the public standard-workflow seam against the
released SDK and bundled native executable. The
[controlled Codex fixture](fixtures/README.md) substitutes only the provider.
Tests assert the Broodling outcome and retained candidate/run binding, not
Zeroshot's internal history.

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

## Historical evidence

Previous timing measurements, mutation demonstrations, opt-in lane definitions,
and qualification outcomes apply only to the old implementation. The
[previous test notes at the starting main commit](https://github.com/faviann/broodling/blob/87f228652117557de95a679d4f22bca76cb86b83/tests/README.md)
retain that context.
[Qualification reports](../qualification/README.md) and
[the earlier ownership audit](../docs/implementation/pr48-ownership-audit.md)
remain unchanged; no old pass is relabeled for the current dependency.
Historical-tooling tests retain only inert-import and record-overwrite
protections; they do not re-run an old execution campaign against this release.
