# P5 v1 preflight and freeze review — 17 September 2026

**Protocol complete; LIVE EXECUTION BLOCKED. #64 is not ready to dispatch.**
This is the #63 preflight disposition, not a #64 execution report. No live
provider call, native PR evaluation, task issue creation, target setup or
credential probe was performed. #65 remains unstarted behind #64.

Protocol: [p5-native-pr-v1](protocol.md). Corpus/B1 and evaluator are frozen in
[corpus.md](corpus.md) and [judge.py](judge.py). The completion comment on #63
records their commit identity. Approval to complete #63 is not approval to spend,
publish task PRs, or operate disposable infrastructure.

## Inspected source and dependency baseline

Current `main` resolved through GitHub to
`b901367b339c3ca715fa51ad91646b499a8fa068`; the decomposition used the same commit.
Authority was read first, followed by #62/#63, current code/tests, then supporting
native-integration/source/handoff records. #64 and #65 were read to align the
handoff; their bodies established no supplied target or run authorization.

| Component | Selected / observed identity |
| --- | --- |
| Broodling product source | `b901367b339c3ca715fa51ad91646b499a8fa068`, version 0.1.0 |
| Governing file | `docs/governing/current.md` at that commit; no architecture change |
| SDK wheel | `the_open_engine_zeroshot-10.3.0.post1-py3-none-manylinux_2_17_x86_64.whl` from the pinned official GitHub release in `pyproject.toml` |
| Wheel SHA-256 | `f3629459837a27b7496f98fe0034e7b47a00079d93d3374922c2960952b8ace9` |
| Native engine | Bundled Zeroshot 10.3.0; no `ZEROSHOT_PYTHON_NATIVE_BINARY` override permitted |
| Runtime from production `runtime_for("pull_request")` | `harness=codex`, `provider=openai`, `model=gpt-5.6-sol`, `effort=low`, `size=small`, `session_scope=execution`; no agent credential connections |
| Production delivery | `Preset("software-change", delivery="pull_request")`; configured DirectTarget and explicit repository / target branch / original B1 |
| Selected local no-effect CLI baseline | Codex 0.153.4; it does not establish the DirectTarget's installation or sandbox |
| Actual live DirectTarget/native/provider build/profile | **UNASSIGNED / NOT VERIFIED**. Record endpoint, operator, native build/version and sanitized provider executable/version/config identity before live work. No newer dependency/model may be substituted silently. |

Source references: [dependency pin](../../../pyproject.toml),
[SDK selection](../../../broodling/zeroshot_sdk.py),
[immutable submission](../../../broodling/submission.py),
[receipt/disposition](../../../broodling/disposition.py),
[criteria/effect admission](../../../broodling/closability.py), and the
[native integration boundary](../../../docs/implementation/zeroshot-native-integration.md).
The source/handoff audits' upstream release assertions remain dated research;
no new "latest release" or target compatibility claim is made here.

## Actual validation in this session

The local administrative sandbox reports:

```text
OS: Linux; architecture: x86_64
Python: 3.13.5
SQLite: 3.46.1
Git: 2.47.3
pytest: 9.0.2
the-open-engine-zeroshot: NOT INSTALLED
hypothesis: NOT INSTALLED
pytest-timeout: NOT INSTALLED
codex executable on PATH: NOT FOUND
```

These values were obtained with `platform.system()/machine()`, `sys.version`,
`sqlite3.sqlite_version`, `importlib.metadata.version`, `shutil.which("codex")`
and `git --version`. No credential values or ambient secret/config files were read.
Repository inspection used the authenticated GitHub connector, not a mounted
complete runnable checkout. A local `git ls-remote` for this repository failed
with `Could not resolve host: github.com`. This session's network/profile is not
an established evaluation host; the connector's repository access is not a
DirectTarget delivery credential or provider authentication.

**Default suite: NOT RUN / BLOCKED, no pass or exit code claimed.** The required
SDK, Hypothesis and pytest-timeout are absent and no complete checkout is mounted.
No SDK substitution, partial import pass, old run, installation workaround or
controlled fixture is used to label missing integration coverage successful.
The supported-host result must be obtained and retained before #64 dispatch:

```bash
# On the approved supported administrative host, at the frozen product baseline
# plus this evaluation-only commit; do not expose credentials in the log.
python -m pip install -e '.[test]'
# Choose a durable directory outside temporary roots before executing the suite.
export BROODLING_TEST_WORKSPACE_ROOT=/approved/durable/test-workspaces
python -m pytest tests
```

The path is an example placeholder, not an allocated host path. Preserve the real
path, exact product/protocol commit, full install/suite stdout/stderr and exit,
collected/pass/fail/skip counts, Python/SQLite/Git/SDK/native versions and test
package versions (`pip freeze` with secret-bearing URLs redacted). Missing SDK or
an unexecuted native check blocks the baseline regardless of other passing tests.
No historical G1–G4 campaign or optional mutation run is required.

**Offline evaluation-material checks performed here:** deterministic fixture Git
B1/tree and both SHA-256 values reproduced; `python -I
evaluation/p5/v1/judge.py --self-test` exited 0 with `calibration_pass=true`:

| Task | Defective B1, expected/observed | Reference function, expected/observed |
| --- | --- | --- |
| T1 | FAIL / FAIL | PASS / PASS |
| T2 | FAIL / FAIL | PASS / PASS |
| T3 | FAIL / FAIL | PASS / PASS |
| T4 | FAIL / FAIL | PASS / PASS |

Additional local CLI smoke checks used synthetic, offline-only receipt locators:
all four reference commits were read successfully from exact Git objects despite
a deliberately wrong mutable worktree and a branch still at B1; a changed README
was rejected (exit 1), and B1-as-result was refused (exit 2). Python syntax parsing
and `git diff --cached --check` also passed for the proposed additions. None of
these synthetic locators is retained or counted as an actual native receipt.

These eight local controls validate the evaluator's examples only. No Broodling
Attempt, provider session, native workflow or real receipt was involved; live
counts remain P=8, S=D=U=A=J_A=0. All R01–R08 are NOT_STARTED.

## Reuse existing boundary tests, not new native campaigns

Paths below are existing suite coverage at the inspected product commit. This is
a claim-to-test map, **not an assertion that these tests ran in this session**.

| Required boundary | Existing validation |
| --- | --- |
| Entitled frozen sources, immutable Contract and Work Unit identity | `tests/test_entitlement.py`, `test_contract_revisions.py`, `test_frozen_instructions.py`, `test_work_unit_identity.py`, `test_work_reference_properties.py` |
| Criteria-only admission and exact authorized PR; unsupported/multiple effects and effect-dependent evidence refused | `test_admission.py`: `test_acceptance_criterion_needs_no_predeclared_validation_plan`, `test_one_exact_pull_request_delivery_is_admitted`, `test_unsupported_or_underspecified_required_effect_is_rejected`, `test_multiple_pull_request_effects_do_not_widen_authority`, `test_effect_dependent_evidence_is_rejected` |
| Original B1, exclusive workspace and one current Attempt | `test_starting_state.py`, `test_attempt_admission.py`, `test_worktree_provisioning.py`, `test_attempt_crash_recovery.py`, `test_store_state_machine.py` |
| Immutable invocation/SDK target and current credentials; DirectTarget acknowledgement loss/conflicts | `test_submission.py`: `PullRequestSubmissionControls.test_acknowledgement_loss_recovers_without_local_worktree_drift`, `test_dispatched_replay_requires_a_current_delivery_credential`, `test_direct_target_true_conflict_is_not_mistaken_for_ack_recovery`; also `test_submission_crashes.py`, `test_sdk_policy.py` |
| Receipt identity, foreign/invalid results, durable reopen, native failure, currentness and late success | `test_workflow_result.py`: `ResultTests.test_native_pr_receipt_is_the_stable_successful_result`, `test_malformed_or_mismatched_delivery_receipt_fails_closed`, `test_foreign_run_result_cannot_complete`, `test_native_failure_abandons_without_claiming_cleanup`, `test_abandonment_while_waiting_refuses_late_success` |
| Reconnect without current execution profile, repeatable detached waits, atomic result/disposition and concurrent finalizers | Same module: `test_correlated_pr_completes_after_current_execution_config_is_unavailable`, `test_cancelled_or_unavailable_wait_can_be_repeated_for_the_same_attempt`, `test_result_and_disposition_are_atomic_and_retryable_after_write_failure`, `test_concurrent_finalizers_converge_without_execution_observation_state`; correlated SDK locator tests in `test_sdk_policy.py` |
| Real-native controlled-provider no-effect stable-result refusal | `test_workflow_result.py::NativeWorkflowTests::test_no_effect_success_exposes_the_stable_result_capability_gap`; expected native success/null, Broodling refusal, not local completion |
| Dispatched quarantine despite terminal/stop labels; safe never-dispatched retirement/replacement | `test_workflow_result.py::ResultTests::test_native_stop_never_promotes_terminal_labels_to_cleanup_authority`; `test_retirement.py`, `test_retry_admission.py`, `test_retry_crashes.py`, `test_retry_races.py` |
| Historical storage preserved without reviving old authority | `test_schema.py`, `test_submission_migration.py`, `test_assurance_custody_storage.py`; current suite guidance in `tests/README.md` |

The PR result tests use synthetic receipts; the no-effect native test substitutes
only the provider. Neither is live GitHub delivery or real-provider outcome quality.
No-effect stable-result refusal is an expected controlled CG, and refusal of
unsupported authority an expected controlled LR, not a successful live task.

## Live preflight decision and required operator bindings

All missing items below are blockers. "Not supplied/verified" means exactly that,
not proof that the user has no such infrastructure somewhere else. No infrastructure
was provisioned or inferred from earlier qualification hosts or example URLs.

| ID | Required before live admission/dispatch | Current status / evidence needed to unblock |
| --- | --- | --- |
| B01 | Supported default-suite baseline | BLOCKED as above; retain a genuine complete suite result on the chosen compatible host, with SDK/native coverage |
| B02 | Authorized disposable GitHub repository and branch | No repository designated or approved. Proposed branch `p5-eval`, B1 frozen in corpus. Named owner must authorize setup and native commit/push/open-or-update there; preserve the branch at B1, no merge or other effects. Do not use `faviann/broodling` as a trial target by default. |
| B03 | Entitled task sources and independent trial allocation | Eight primary issue locators, source snapshots/entitlement decisions, Work Unit/Contract IDs, source checkout and distinct durable local workspace allocations absent. Bind them to R01–R08 and exact B1 before dispatch; reserve target workspaces/quarantine capacity too. |
| B04 | Compatible configured DirectTarget and provider | No endpoint, operator, native/provider installation/version/config evidence or compatibility attestation supplied. Verify the unchanged requested runtime/model is actually supported there. Pin a compatible target/operator-owned profile; unsupported selection is a blocker, not permission to substitute. |
| B05 | Target sandbox and trust assumptions | No target policy attestation. Record filesystem/network/extension/MCP/config policy, permitted checkout/delivery access, isolation from judging material/other trials, and protection of credentials; local Codex launcher restrictions do not constrain DirectTarget. |
| B06 | Provider authentication and delivery credentials | Not verified on any designated target. Operator must attest provider authentication and currently available correctly scoped delivery credentials for the approved repository/branch, with time/expiry/reference. Keep tokens out of records, task text and agent runtime connections. No secret or effectful probe was performed. |
| B07 | Explicit protocol/effect/run/spend approval | No approval reference or operator signature. Authorized runs/spend are **zero**. Frozen requested maxima are 8 serial trials/native runs, USD 20/run and USD 160 total, 30 minutes/run and 240 total. Name approver, exact caps, scope, expiry, stop margin and accounting/enforcement arrangements; protocol acceptance is not supplied infrastructure. |
| B08 | Stop, containment, teardown and retained capacity | No named operator, independently effective host/target teardown procedure, storage reserve/free-space stops, access or approval supplied. Document containment if stop is unavailable/ambiguous, evidence export before teardown, and capacity for all quarantined assignments. Never translate external teardown into Broodling cleanup/replacement authority. |
| B09 | Independent judging and durable evidence retention | No credential-free/network-disabled judging sandbox, independent reviewer/session assignment, backup location/owner/access/retention or verified exact-Git-object export supplied. Keep judge/reference code unavailable to workers; any leakage is a retained deviation. Preserve B1/head and records outside target teardown. |

An operator preflight record must bind these identities to the freeze commit with
approval references/status and timestamps, not credentials. Verify validity and
remaining budget before **each** dispatch/reconciliation. Evidence-only reads of
an already-correlated run use its persisted locator, not recreated execution
profiles or renewed dispatch credentials. Target access controls still apply.

Until all blockers are resolved and current explicit authorization exists,
**#64 is NOT READY**; neither a proposed budget nor closing #63 changes that.
The next action is supplying/verifying this named operational preflight, not
running providers to discover what infrastructure might work. #65 cannot bypass
#64. No new cleanup, assurance architecture or product implementation is needed
to explain this blocked state.

## Review and freeze decision

The completing assistant performed an author self-review against #63's four
completion criteria and current source: criteria-only task admission; original B1
and exact receipt judging; distinct semantic/lifecycle outcomes; fixed sample,
thresholds, replay/stopping/counting rules; target/operator trust and zero current
approval; no-effect refusal/dispatched quarantine; existing-suite mapping and
honest missing coverage. Positive/negative oracle controls were checked offline.

**Decision: freeze v1 for reviewable execution planning with BLOCKED live preflight.**
No independent peer review or operator approval is claimed. A pre-run operator must
acknowledge the exact protocol commit when authorizing the named resources/caps;
any requested measurement change requires a new prospective version before outputs.
#66's independent evidence/readiness review remains separate and unstarted. No
historical evidence/verdict, production source, schema, dependency, test invariant,
workflow/provider selection or governing architecture was changed by this issue.
