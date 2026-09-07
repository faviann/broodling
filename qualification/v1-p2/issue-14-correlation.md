# Issue #14 — durable submission correlation, G2-V1 remediation

**Date:** 7 September 2026. **Scope:** implementation/evidence for #14 only.
**Base:** `7b334bfedc8bc3bd25a4d61e105670c844310b51`, current `origin/main` including
#13's provisioning remediation and retained evidence.

Authority order: [target v0.5](../../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md)
and [plan v0.5](../../docs/governing/broodling-implementation-dependency-plan-v0.5.md),
then [G2-V1 BLOCKED](issue-15-g2-v1.md) §5.1/§5.2, the current #12/#13 product and
evidence, and [issue #14](https://github.com/faviann/broodling/issues/14).
The local WIP branch was inspected only as reference. No commit was cherry-picked
or published from it. This record supersedes #14's earlier unreachable
`012c32f` implementation claim and its B1-only replay limitation.

## Behavior and safety argument

The defect was a correlation gap: after acceptance but before Broodling stored
R, R could advance its worktree. Requiring replay to remain at B1 prevented even
calling the qualified public contract, and treating every returned conflict as
terminal would discard the already-existing public run identity.

The implementation keeps the smallest owned administrative facts in
`attempt_submissions`: Attempt, stable key, immutable canonical request,
`prepared`/`dispatched`/`correlated`/`blocked`, optional unique run ID and error.

1. Currentness comes from Broodling's durable Attempt rows. The Contract still
   reads back as admitted and immutable; frozen source material still matches B1.
2. Preparation requires the exclusively owned provisioned worktree, attached
   assigned branch, expected common Git directory and clean B1. It commits
   `broodling:v1:<attempt-id>` and the exact request before any SDK call.
3. Dispatch intent commits separately. A crash with only `prepared` still
   requires clean B1. A crash with `dispatched` repeats the same persisted call,
   including the same runtime state directory and complete PATH-only environment.
4. Dispatch and correlation hold Broodling's SQLite write transaction. Concurrent
   callers reread the committed winner; process death releases the lock and
   leaves the request/dispatch intent durable. The database prevents replacing
   a key/request/run ID, deleting the row, or assigning one run to two Attempts.
5. Replay checks repository/worktree/branch/enclosure ownership and origin URL.
   It allows changes to the owned candidate after dispatch. When unchanged replay
   raises the qualified public conflict, changed HEAD explains the resolved-source
   digest difference and its nonempty `existing_run_id` supplies the correlation.
   Dirty files alone do not change that digest and cannot excuse a conflict.
6. A changed caller request, target/environment or source ownership/configuration
   is refused. An unexplained public conflict (including a conflicting request
   or branch submitted directly under the key) becomes terminal `blocked`, with
   no run correlation. An empty public ID cannot correlate. No refusal mints a
   new key/run/Attempt or overwrites a correlation.

**Profile assumptions are load-bearing.** The key is Broodling-owned and only the
persisted request is dispatched under it. After dispatch, only that Attempt's
accepted run may mutate its dedicated worktree. Zeroshot retains its original
state directory and key binding. A persisted dispatch intent is not itself proof
of acceptance; the public receipt/conflict supplies the run identity. The public
conflict does not expose a request comparison, so this is not a mechanism to
recognize arbitrary external writers, foreign key injection or deleted runtime
storage. Those are outside the qualified single-host/exclusive-writer profile.
The implementation neither resets the worktree nor reconstructs missing runtime
state to manufacture a match.

Local request/source refusals reject the competing operation without poisoning
the original immutable request. A definitive unexplained SDK conflict is stored
as `blocked`. Neither path authorizes further execution under different identity.
A correlated row says only “this run belongs to this Attempt”; it says nothing
about runtime success, completion, candidate acceptance or Work Unit disposition.

## Discriminating evidence

| Obligation | Retained witness |
|---|---|
| Normal submit, repeated issue ingress, admission/provisioning and restart resolve one lineage | `PublicSubmissionTests.test_normal_and_repeated_ingress_preserve_entire_lineage` |
| Key/request commit before external call | `SubmissionControls.test_prepared_identity_commits_before_dispatch` reads from an independent store connection inside the call |
| Lost acknowledgement before mutation | `PublicSubmissionTests.test_acknowledgement_loss_before_worktree_mutation` |
| HEAD advanced: actual public conflict exposes the original R | `PublicSubmissionTests.test_acknowledgement_loss_after_worktree_mutation_uses_public_conflict` explicitly calls the adapter and checks the public ID before product reconciliation |
| Uncommitted mutation: ordinary same-ID replay | `PublicSubmissionTests.test_acknowledgement_loss_with_uncommitted_mutation_replays_normally` |
| **Accepted graph mutates after Broodling process death** | `SubmissionCrashTests.test_accepted_graph_mutates_after_broodling_process_dies`: real SDK/sidecar, gated deterministic graph step, hard caller exit before correlation, then graph-owned commit and public-conflict reconciliation |
| True request/source conflicts block | `test_true_conflicting_request_at_public_boundary_blocks`, `test_true_conflicting_source_at_public_boundary_blocks`; direct SDK competitors under the same key |
| Changed request, origin, repository, branch, enclosure, target or environment never excuses conflict | `SubmissionControls`, `AdditionalSubmissionControls` |
| Dirty files cannot excuse a true conflict; missing ID cannot correlate | `test_dirty_files_do_not_excuse_true_conflict`, `test_conflict_without_public_id_blocks_after_mutation` |
| Every persistence crash window | `SubmissionCrashTests`: death during prepare, after preparation, after dispatch intent/before call, after acceptance, after acceptance plus mutation, during correlation write, after correlation commit, and a second death after receiving the public conflict |
| Concurrent first submit and mutated replay | Four real child processes race behind a gate in `SubmissionCrashTests`; one durable mapping and one ID for all callers |
| Stale Attempt refuses every ingress/replay path | `test_stale_attempt_cannot_prepare_dispatch_or_read_back_correlation`, across absent/prepared/dispatched/correlated states; no SDK call |
| Immutable correlation and cross-Attempt uniqueness | SQL mutation/deletion controls and `test_a_second_attempt_cannot_claim_the_same_run_id` |
| Preserve #13 and upgrade real v2 facts | Full existing suite plus `SubmissionMigrationTests`; #13 provisioning/locking code and its witnesses are unchanged |
| No runtime mirror/scanner/effect wiring | `SubmissionBoundaryTests` plus existing schema/import/process boundaries |

[Full product-test output](evidence/issue-14-tests.txt): **211 passed**, including
**17 real SDK/sidecar cases**, with 1775 additional subtest checks.
[Without the SDK](evidence/issue-14-without-sdk.txt): **194 passed, 17 explicitly
skipped**. The unchanged #13 provisioning/concurrency tests run in both suites.
[Red regression output](evidence/issue-14-red.txt) records the same mutation case
with the rejected B1-on-replay policy injected by `issue14_regression.py`:
`SubmissionNotReady` at restart. This is a behavioral policy injection, not a
claim that the unpublished branch was the tested baseline. The corresponding
uninjected run is retained in [green output](evidence/issue-14-green.txt).

The [machine lineage record](evidence/issue-14-graph-crash.json), generated by
`issue14_evidence.py`, identifies the implementation commit, exact versions,
B1 and graph-mutated HEAD, Work Unit/Contract/Attempt/key/request identity,
public run ID and one-row counts. Its fixture asserts the acceptance/crash/
mutation ordering and unchanged replay bytes before writing that record.
See [reproduction commands](README.md#reproducing-the-14-correlation-evidence)
and [fixture scope](../../tests/fixtures/README.md).

## Build, persistence and retained boundaries

- Python 3.13 and Broodling-owned SQLite v3; actual interpreter/SQLite versions
  and schema digest are in the machine record.
- Official `zeroshot-rust 0.1.0.dev0`, source
  `d0909615d6ba3c179b58bce15a059f40400ec995`, qualified wheel SHA-256
  `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`.
- Sidecar SHA-256
  `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.
- Installed SDK Python source SHA-256
  `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e`.
  Computation: sorted package-relative `*.py` paths, each UTF-8 path + NUL + exact
  bytes + NUL. All 11 installed source files were compared byte-for-byte to the
  qualified Git revision. The adapter checks this digest, version and binary
  hash on submission; a same-version but different SDK also fails closed.
- The additive v2→v3 migration recognizes only the exact published v2 fingerprint,
  updates metadata and creates the new table/triggers atomically, preserving all
  Contract/Attempt/worktree facts and #13's host-local provisioning lock.
- Product uses only `Client.submit` and the qualified public conflict ID. It
  never opens Zeroshot SQLite/RunLedger, scans status/history/completed runs,
  waits for results or stores runtime occurrences. Package-source/binary reads
  serve build verification only.
- No W3 product graph, assurance/final-result semantics, P4 abandon/restart,
  effects/GitDelivery, candidate sealing, cross-Attempt reuse or scheduler.

**Stop:** #14 implementation/evidence only. The historical G2-V1 BLOCKED record
is unmodified. This is not a G2 rerun or PASS verdict; V1-P3 remains blocked until
its separately authorized gate review.
