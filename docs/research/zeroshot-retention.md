# Zeroshot 10.3.0 retention and reclamation boundary

Scope: the official Zeroshot source pinned by Broodling, commit
[`054ad3f`](https://github.com/the-open-engine/zeroshot/tree/054ad3fd6c763b98d12f5b2e90830b97116561ad), paired with Python SDK `10.3.0.post1`.
This is source and documentation review only; no provider work or tests were run.

## Findings

There is no public per-run delete, target prune, or garbage-collection operation in
the pinned CLI/SDK surface. Run observation offers list/status/watch/logs/attach
and force-stop, and the native run-ledger interface has create/read/list/append/
stop/history methods but no deletion method ([CLI](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cli/parser.rs#L74-L96), [Python handle](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L308-L330), [ledger interface](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger.rs#L344-L373)).

However, DirectTarget already reclaims each run's execution capsule as part of
native terminalization. Its target root opens `runs.sqlite3`; a separate
`runs/<hash-of-run-id>/` tree holds that run's GitHub checkout under `workspace/`
and provider-private files under `runtime/` ([target composition](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting.rs#L96-L124), [capsule layout](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting/allocator.rs#L92-L133), [run path](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting/allocator.rs#L382-L405)). On normal completion the supervisor closes the run, removes its capsule, and only then appends the terminal result; force-stop follows the same cleanup-before-terminal pattern ([normal completion](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_supervisor.rs#L289-L300), [force-stop](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_supervisor/controller.rs#L207-L226), [directory deletion](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting/allocator.rs#L352-L405)). This removes the native checkout and the whole provider runtime home, including files a provider kept there. It does not remove the corresponding ledger row or its idempotency key.

The same-state restart path also performs native orphan cleanup. Before serving,
the new controller enumerates persisted nonterminal runs, claims each run's
controller lock, removes its capsule directory, and appends a `runtime_lost`
terminal failure. It does not allocate a replacement runtime or resume provider
work ([startup reconciliation](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cloud.rs#L148-L167)). A pinned unit test checks that a reconstructed nonterminal run becomes `runtime_lost` and is not reallocated, using fake ledger/allocator components ([test](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cloud/tests/cases_1.rs#L21-L50)). If native cleanup itself fails, startup reconciliation returns an error; there is no public API to select and manually clean that orphan.

Completed history is addressable after a restart when the target reopens the same
ledger. `Client.get_run(run_id)` creates a durable handle, and `wait()` returns
an already-present terminal result; `status()` and `logs()` also read retained
history ([SDK reconnect and wait](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L308-L322), [wait](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/client.py#L656-L758)). The DirectTarget image stores its configured target root on `/var/lib/zeroshot` ([image volume and command](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/docker/zeroshot-target/Dockerfile#L77-L86)); Broodling's installer mounts its durable `target-state/` at `/state` and starts Zeroshot with `--storage /state` ([installer](../../deployment/install.py#L116-L123)). Reusing that complete, consistent state is the same-ledger case. Zeroshot does not document a backup/restore API or cross-version storage-migration guarantee here, so restore and upgrade compatibility beyond same-version reopen remain operational assumptions to verify.

Submission-key deduplication belongs to that ledger. SQLite stores one unique
`submission_key` and digest per run; `create_or_get` returns the existing run,
and sourceful replay checks the digest ([schema](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger/sqlite.rs#L18-L34), [replay handling](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_cloud.rs#L214-L267)). By inference, a fresh empty state at the same HTTP origin has neither the old run ID nor its key/digest row: `get_run(old_id)` is not found, and replaying the old submission key can create a new run. An archived copy of the complete old state should retain results and deduplication when reopened, but that is an inference from the configured storage path and ledger code, not a documented native restore contract. Never replay a frozen old invocation against an empty replacement state.

## What remains in native storage

After normal native terminalization, the potentially material remainder is the
target ledger, not the per-run checkout or provider home. The SQLite schema
retains `stored_json` for each run and ordered `event_json` records. Stored run
state includes the admitted submission, snapshot and identity; events include
node inputs/completions, terminal results, token usage and safe provider/runtime
log lines ([stored run and events](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger.rs#L65-L71), [event types](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger.rs#L185-L216), [durable output bridge](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_supervisor/runtime.rs#L203-L242)). Admitted data is bounded at 2 MiB, each event at 1 MiB, and each safe log line at 16 KiB; these are per-object bounds. Replay is paged to 256 events/1 MiB at a time, not a limit on total retained history ([bounds](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/v2_run_ledger.rs#L32-L38)). No total run/event cap or retention pruning exists in the inspected interface, so cumulative ledger growth is unbounded by this implementation. The allocator also creates a per-run controller lock file at the target root; capsule cleanup does not remove it ([lock creation/path](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting/allocator.rs#L252-L268), [lock path](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting/allocator.rs#L389-L405)).

## Broodling boundary and recommendation

DirectTarget executes from the target's own clone, not Broodling's candidate
worktree. The SDK describes `DirectTarget.workspace` as a source-selection and
dirty-state context; the target then initializes a workspace and fetches the
specified GitHub branch/revision itself ([SDK target contract](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/sdks/python/src/zeroshot/runtime.py#L29-L40), [target checkout](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_hosting/repository.rs#L162-L215)). Broodling's local Attempt worktree remains a separate resource with Broodling's own ownership and retirement rules.

For this first profile, rely on native automatic capsule cleanup and retain
native run/event history indefinitely. After an interrupted run, restart the
same target with the same state and let startup reconciliation clean the orphan
and record failure. Offline backup, upgrade and restore remain whole-target
operator maintenance: keep the exact target state path, layout and ownership,
and keep an archived old installation inactive. This is an operator policy,
not a selective native retention API. Do not edit native SQLite or delete hashed
run directories manually. If a later profile needs native history pruning while
preserving results and deduplication, that would require an upstream-supported
first-party retention boundary; none exists in this release.

The local Attempt worktree is independent of the native capsule and can be
considered under Broodling's own ownership/retirement policy. Native execution
clones the selected GitHub revision into its own capsule; removing the Broodling
worktree does not remove native run history or result access.

These sources do not prove that a terminal label is a kernel-level process-tree
cessation receipt, nor do they test Docker-container fencing, live backups, or
cross-version restore. A drained whole-container maintenance fence is an
operator boundary external to the Zeroshot API; preserve the exact target state
path and keep an old installation inactive when restoring its archived state.
