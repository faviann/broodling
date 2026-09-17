# Provisioning-race witness investigation (#55)

17 September 2026. Recommendation: **retain the current pair unchanged** for this
maintenance round. The prototype controls a meaningful incomplete-worktree state
and detects an actual bad return under a bounded test-local fault, but it does not
remove all timing assumptions or provide a simpler equivalent replacement.
No implementation change or experimental merge is recommended.

## Scope and pinned evidence

- Governing question and boundaries: [issue #55](https://github.com/faviann/broodling/issues/55), under [#49](https://github.com/faviann/broodling/issues/49).
- Baseline: `fffaf80455e4cf4088f04a5c631c90349913a44b`, after [#53 merged in PR #58](https://github.com/faviann/broodling/pull/58).
- Reproducible prototype: [`tests/prototype_provisioning_race.py` at `1cf280038b163c87fdaec711f49f4fde08086d41`](https://github.com/faviann/broodling/blob/1cf280038b163c87fdaec711f49f4fde08086d41/tests/prototype_provisioning_race.py).
- Branch: `investigation/issue-55-provisioning-race`. It adds this report and the
  explicitly selected prototype; production files and existing acceptance tests
  are unchanged. The prototype filename is outside normal pytest test discovery.

This investigation reads the actual repository source and runs real SQLite, Git,
and subprocesses. It introduces no production hook, dependency, generic race
framework, or native execution simulation.

## Guarantees and proposed-removal mapping

No test removal is proposed. The table records why neither current witness should
be replaced by this particular experiment.

| Existing witness | What remains protected | Prototype comparison |
| --- | --- | --- |
| [`test_every_concurrent_caller_is_handed_the_finished_worktree`](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/tests/test_attempt_crash_recovery.py#L250) | Four whole-operation callers agree on Attempt/path/branch/B1/provisioned state and expected tracked-file count; one Attempt, disposable worktree, and branch remain. | Two callers receive the same owned B1 and actual README bytes, but the Attempt is admitted beforehand. It does not exercise the full concurrent admit-plus-provision path. The separate admission-only race remains useful, but is not claimed to make these whole-operation scenarios identical. |
| [`test_a_second_provisioner_waits_rather_than_reading_a_half_built_worktree`](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/tests/test_attempt_crash_recovery.py#L282) | An externally held enclosure lock excludes an ordinary provisioner, then release permits convergence at B1. The holder does not also hold SQLite's writer, so this directly witnesses host exclusion. | The prototype's live leader holds both locks. Disabling only the follower's flock still passes; therefore it cannot replace this independent host-exclusion witness. The existing explicit-lock test itself uses a bounded observation window, not a scheduler proof. |
| [`test_orphan_git_child_retains_exclusion_until_materialization_finishes`](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/tests/test_retry_crashes.py#L76) and [`test_orphan_git_retains_exclusion_until_it_finishes`](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/tests/test_retirement.py#L156) | Git retains the inherited host lock after its caller dies and releases SQLite; a follower cannot race that live child during materialization or removal. | Neither process death nor a surviving Git child is exercised by the prototype. These tests remain untouched under every considered option. |

The explicit-lock and orphan tests overlap in follower exclusion, but the latter
add descriptor inheritance and process death. The former isolates an ordinary
caller's enclosure exclusion without relying on another SQLite writer. The
finished-worktree test adds the caller-visible completed outcome. The prototype
does not subsume the combination.

Source detail: [`provision()`](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/broodling/provisioning.py#L139)
first claims the enclosure inside `store._write()`, then holds the enclosure lock
and another writer transaction across `_materialize`. A live competitor can
therefore wait on SQLite before reaching the host lock. The
[`_sole_provisioner` implementation](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/broodling/provisioning.py#L311)
and the orphan tests establish why the host lock still matters after caller death.

## Experiment and discrimination

The baseline outcome test creates 600 additional files and races four processes;
its child [reports `git ls-files` count](https://github.com/faviann/broodling/blob/fffaf80455e4cf4088f04a5c631c90349913a44b/tests/attempt_crash_child.py#L117),
which describes the index rather than reading physical file contents. That is a
limitation of the observation, not evidence of a demonstrated product defect.

The prototype uses the existing one-file repository and two live provisioners:

1. A leader's test-local Git wrapper executes real `git worktree add --no-checkout`.
   The parent verifies the registered/attached worktree's branch and original B1,
   absence of physical README bytes, and still-unacknowledged allocation.
2. The leader waits at a file gate before real `git reset --hard B1` completes the
   checkout. This guarantees the intermediate host state; no Git or SQLite state
   is faked.
3. A second process announces it is about to provision. The parent gives it a
   200 ms observation window, then releases the leader. Each child's returned
   snapshot includes actual README bytes, identity, path, branch, B1, and
   provisioning acknowledgement. Final checks require only one Attempt/worktree/
   branch.

The two-stage Git operation is a controlled test construction, not an observation
of every intermediate step inside one native `git worktree add` invocation.
The `started` event precedes `provision()` and does **not** prove that the follower
reached its lock or dangerous read before the gate releases. A slow faulty
follower could therefore evade detection. The 200 ms budget matches the existing
explicit-lock observation budget; it was not increased to manufacture reliability.

Two test-local fault modes clarify what the experiment detects:

- `flock`: disable only the follower's `fcntl.flock`. The prototype still passes
  because the leader retains SQLite exclusion. This is not evidence that the host
  lock is unnecessary.
- `both`: additionally release the leader's writer transaction around
  `_materialize`, restoring transaction shape on exit. With both exclusion layers
  bypassed, the follower returns an acknowledged worktree before its README
  exists. Both observed fault runs failed exactly at the physical-result assertion:
  `AssertionError: None != 'original admitted state\n'` (prototype line 131).
  This is a caller-visible bad result, not a lock-call-count or serialization error.

The combined fault deliberately models loss of both live-caller exclusion layers;
it is not a claim to cover every possible provisioning regression. Existing
production code was not modified for these faults. An independent review identified
possible buffered-stdout prefetch as a confound before measurement; the committed
prototype uses unbuffered binary pipes so event reading cannot consume a result
that later `communicate()` would miss. Fault failures were the intended missing-byte
assertion, not JSON parsing errors.

## Reproducible commands and measured results

All commands ran in `/home/faviann/scratch/broodling-maintenance-49.BGkEiS/issue55`
using Python 3.13.5, pytest 9.1.1, pytest-timeout 2.4.0, and Hypothesis 6.168.0.
Every run took the shared suite lock; shell `time` is inside `flock`, so waiting
for another suite is excluded from the measured wall time.

Baseline pair: three repeats, source exactly at `fffaf80455e4cf4088f04a5c631c90349913a44b`:

```bash
flock /dev/shm/broodling-maintenance-suite.lock bash -c 'for run in 1 2 3; do time /home/faviann/repos/broodling/.venv/bin/python -m pytest -q tests/test_attempt_crash_recovery.py::ConcurrentAdmissionTests::test_every_concurrent_caller_is_handed_the_finished_worktree tests/test_attempt_crash_recovery.py::ConcurrentAdmissionTests::test_a_second_provisioner_waits_rather_than_reading_a_half_built_worktree; done'
```

Candidate: three clean repeats at `1cf280038b163c87fdaec711f49f4fde08086d41`, run as one initial selection followed by two repeats:

```bash
flock /dev/shm/broodling-maintenance-suite.lock bash -c 'time /home/faviann/repos/broodling/.venv/bin/python -m pytest -q tests/prototype_provisioning_race.py'
flock /dev/shm/broodling-maintenance-suite.lock bash -c 'for run in 1 2; do time /home/faviann/repos/broodling/.venv/bin/python -m pytest -q tests/prototype_provisioning_race.py; done'
```

Faults at the same candidate commit: one flock-only run and two combined-fault runs:

```bash
flock /dev/shm/broodling-maintenance-suite.lock bash -c 'time env BROODLING_RACE_PROTOTYPE_FAULT=flock /home/faviann/repos/broodling/.venv/bin/python -m pytest -q tests/prototype_provisioning_race.py'
flock /dev/shm/broodling-maintenance-suite.lock bash -c 'for run in 1 2; do time env BROODLING_RACE_PROTOTYPE_FAULT=both /home/faviann/repos/broodling/.venv/bin/python -m pytest -q tests/prototype_provisioning_race.py; done'
```

| Selection | Runs and result | Pytest seconds, in run order | Shell wall seconds, in run order |
| --- | --- | --- | --- |
| Baseline pair | 3/3 passed, 2 tests each | 2.19, 2.41, 2.70 | 2.917, 2.814, 3.056 |
| Candidate, unchanged product | 3/3 passed, 1 test each | 2.15, 1.70, 1.33 | 2.905, 2.625, 1.674 |
| Candidate, follower flock bypass | 1/1 passed | 1.45 | 2.164 |
| Candidate, both exclusions bypassed | 2/2 failed at missing README assertion, as intended | 0.92, 1.18 | 1.243, 1.509 |

These are bounded checks on one host, not a stress campaign or performance study.
The median observed shell wall cost is 2.917s for the pair and 2.625s for the
candidate; the small sample and variation do not support a stable speedup claim.
All clean runs passed, but three runs cannot establish race freedom or a flake rate.

## Maintenance cost and decision

The current pair occupies 84 lines (baseline test module lines 250–333), using
existing shared child/race helpers. The standalone prototype adds 152 lines,
including subprocess event handling, staged Git operations, gate release/cleanup,
physical result capture, and two private fault seams. This is a conservative
comparison of local code, not a claim that shared baseline helpers cost nothing.
Some prototype code is only needed for fault discrimination, but the gate/protocol
and scheduling caveat remain even without those modes.

The experiment improves physical-byte observation and makes the incomplete host
state explicit. It also requires more test-specific machinery, omits concurrent
admission, cannot independently discriminate host-lock removal, and leaves a
follower-scheduling assumption. On this evidence, replacing the timing-amplified
witness or consolidating the pair is not a proportionate maintenance improvement.
Retain both existing tests and both orphan-Git witnesses. The branch/report preserve
the investigation for any later explicitly authorized implementation decision;
no such decision is needed to complete the retain/no-change recommendation.
