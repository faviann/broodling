# Issue #13 — concurrent identical Attempt admission

**Date:** 7 September 2026 (America/Toronto)
**Answers:** [G2-V1 review](issue-15-g2-v1.md) §5.3, blocking failure owned by
[#13](https://github.com/faviann/broodling/issues/13)
**Reviewed tree:** `00db4a2e403622bcbf9e8fe5a7a0727737ca29a6` (product unchanged
from #13's `f42f00d89e662f110254f8779fd3d953b9a125ce`)
**Remediation:** `3806128` — see §5
**Scope:** #13 only. No #14 work, no V1-P3, no abandon/restart, no effects, no
recovery/catch-up.

The gate recorded one isolated failure of
`test_two_identical_concurrent_admissions_resolve_one_attempt` across repeated
full-suite runs and concluded, correctly, that the retained evidence did not say
what had failed. This is that diagnosis.

**Result.** The observed failure was not a currentness defect, and currentness
was never at risk: no run of this witness, before or after remediation, ever
produced two Attempts, two current Attempts, two worktree assignments or two
branches. It was a defect in *provisioning*, whose only symptom is the result
handed back to a caller. The witness raced admission and provisioning together,
which is why its failure could not say which of the two had gone wrong.

The defect is real, is fixed, and had four symptoms — including one more serious
than the observed flake, which the suite's fixture could not see. §1 states the
invariant, §2 the mechanism, §3 what actually went wrong, §4 why the durable
state was nonetheless always sound, §5 the fix, §6 the retained evidence.

## 1. The invariant under review

From #13's acceptance criteria:

> Enforce at most one current Attempt for a Work Unit transactionally. A
> concurrent or repeated admission request either returns the same current
> Attempt where semantically identical or conflicts; it cannot create a second
> current authority.

Two obligations, not one:

* **A — currentness.** At most one current Attempt per Work Unit, whatever races.
* **B — the returned result.** A semantically identical concurrent request
  returns the same current Attempt. Where that Attempt's worktree is
  materialized, the caller is handed that worktree, at B1.

A was always met. B was not, and B is what the gate's "inconsistent returned
result" names.

## 2. Mechanism: `git worktree add` publishes in stages

Provisioning decides what to do by reading host state — `git worktree list`, and
the directory. That is sound against an *interrupted* run, whose leftovers have
stopped changing. It is not sound against a *concurrent* one, because
`git worktree add` is not atomic. It publishes its result in stages, and a
reader can land between any two of them.

Measured over a 3000-file B1, five trials
([record](evidence/issue-13-stage-order.json)):

| Stage | What a reader now sees | Trial timings |
|---|---|---|
| registration written, directory created | `worktree list` reports the path, **no branch**; the directory holds `.git` | 6.4 – 29.1 ms |
| branch attached | `worktree list` reports the path *and* the branch | +0.6 – 1.5 ms |
| checkout complete | the tree is finally B1 | +741 – 1070 ms |

Two orderings hold in `5/5` trials, and both matter. The registration is never
later than the directory becoming non-empty, so *"contains files but is not
registered"* is never a true statement about a settled worktree — only about one
being built. And the registration always precedes the branch.

The gap between registration and branch is small. The gap between the attached
branch and the finished tree is not: on this B1 it is roughly three orders of
magnitude larger, and it grows with the repository.

**The suite's fixture B1 is one file.** All three windows are microseconds wide
there, which is exactly why this reached the gate as a rare unexplained flake
instead of a reproducible failure.

## 3. What went wrong, four ways

`provision` read that staged state and misread it four ways. All four are the
same error: an intermediate state of *this Attempt's own* worktree is not
distinguishable, by reading, from foreign or half-built state.

Counts below are from two runs of 300 rounds
([1](evidence/issue-13-race-reviewed-flake-1.json),
[2](evidence/issue-13-race-reviewed-flake-2.json)), 2 racers, against `00db4a2`
at the suite's one-file B1 — the conditions under which the gate saw the flake.
**13 rounds of 600 violated.** Every violation was caller-visible, and every
failing round still ended with exactly one Attempt row and one worktree
assignment.

### 3.1 Stale listing, fresh directory — false refusal

The caller's `git worktree list` snapshot is taken before the winner registers;
by the time it looks at the directory, the winner has created it. Not registered,
but not empty, so `_assert_path_is_free` refuses:

```
WorktreeOwnershipConflict: .../worktree already contains files but is not a
registered worktree of this repository; provisioning will not overwrite it
```

The premise is false by the time it is stated. A one-off diagnostic build that
re-read the registration at the instant of refusal — not retained, since the
remediation makes the path unreachable — reported the path as already registered,
on this Attempt's own branch, with the winner's checkout in flight:

```
contents=['.git']  recheck_registered=True
recheck_branch=broodling/faviann-broodling-12/4908b71a43cdfc072dec3766
```

The retained equivalent is §2's `registered_before_directory_populated` ordering:
a populated directory is always an already-registered one, so this refusal can
only ever fire on a stale reading.

**6 of 600 rounds.**

### 3.2 Registered, branch not yet attached — false refusal

The caller reads the registration after the winner writes it but before the
branch attaches, so `_assert_entry_is_ours` sees no branch:

```
WorktreeOwnershipConflict: .../worktree is already a registered worktree on
branch None, not this Attempt's branch 'broodling/faviann-broodling-12/e3ae...'
```

**4 of 600 rounds.** Together with §3.1 these are the refusals the gate saw.

### 3.3 Branch attached, tree not yet checked out — **false success**

The worst one, and the one the suite could not see. The caller reads after the
branch attaches but before the checkout finishes. Every check passes — registered,
right branch, `.git` present — so `provision` acknowledges, durably marks the
assignment provisioned, and returns a `ProvisionedWorktree` over a tree that is
not yet B1.

Against the reviewed tree, with a 3000-file B1, this is not a flake. It is every
round ([record](evidence/issue-13-race-reviewed.json)): 12 of 12 rounds, 36
violations, three of the four callers in each round handed the same worktree with

```json
{"provisioned": true, "head": "<B1>", "tracked_files": 0}
```

while the fourth — the winner — saw all 3000. `head` is right because the ref is
written early; the tree is empty because the checkout is still running.

A caller that acted on that worktree would read an empty repository as B1.

At the suite's one-file B1 the window is microseconds and this appeared in **2 of
600 rounds** — indistinguishable, without asking the caller what it was handed,
from a passing round.

### 3.4 Branch created between the check and the create — false refusal

Both callers find no branch, so both enter creation with `reuse_branch=False`,
and the loser's `git worktree add -b` fails:

```
fatal: cannot lock ref 'refs/heads/broodling/...': reference already exists
```

`provision` already had a convergence path for exactly this — catch
`GitCommandError`, and if the worktree is already materialized, converge on it.
It did not fire, because at that instant the winner had created the ref but the
worktree was not yet live, so `_already_materialized` was still false.

**1 of 600 rounds.** This one matters beyond its count: the existing fallback was
itself a read of concurrently-changing state, and therefore had the same defect
it was meant to absorb. It is why the answer in §5 is not another re-read.

## 4. Why the durable state was always sound

The harness classifies the durable state of *every* round, not just the ones it
retains, and reports `competing_authority` or `duplicate_worktree` separately
from `inconsistent_result`. Across all 624 rounds recorded here — before and
after the fix — **`violation_kinds` contains `inconsistent_result` and nothing
else**. Each round ended with exactly:

* one `attempts` row, `is_current = 1`;
* one `worktree_assignments` row;
* one registered worktree and one branch beyond the source checkout;
* that worktree at B1, complete.

The 28 rounds the records retain in full — every failing round, plus one passing
sample per run — can be re-checked against that list directly.

That is not luck, and it is not the provisioning code:

* every caller *derives* the same Attempt id, from the Contract revision, the
  repository, the commit and the frozen material digest — so identical requests
  are not competitors, they are the same request;
* `BEGIN IMMEDIATE` serializes admission;
* `attempts_one_current_per_work_unit`, a partial unique index, permits one
  current Attempt per Work Unit, and it is the database that enforces it;
* `worktree_path` is globally unique and `(repository, branch)` is unique.

The failures of §3 are all downstream of those facts. They are reports, not
records. **Obligation A was never violated, and the gate's ambiguity is
resolved: the observed failure was harmless to currentness.**

To keep it that way, currentness now has a witness of its own.
`test_identical_concurrent_admissions_alone_resolve_one_attempt` races four
identical admissions and stops at admission, so no provisioning outcome stands in
front of the result. It passes against the reviewed tree and the remediated one
alike — that is the point of it. A future provisioning defect can no longer make
this fact ambiguous.

## 5. Remediation

Reordering the checks cannot fix §3: any read can land inside a stage, and no
amount of re-reading makes a partially built worktree announce itself.

Materializing one Attempt is therefore single-writer on this host. `provision`
holds `flock` on `.broodling-provisioning.lock` in that Attempt's enclosure for
the whole operation, so every observation it makes is of state some other process
has finished writing.

What it deliberately is not:

* **not durable authority.** Ownership stays with the store's constraints. The
  lock decides nothing; it only keeps a reader out of a half-written answer.
  `tests/test_store_boundary.py` lets only `provisioning.py` import `fcntl`.
* **not crash state.** The kernel drops it when a process dies, so the crash
  windows in `tests/test_attempt_crash_recovery.py` recover by repeating the
  operation exactly as before.
* **not a lease or fencing system.** It is scoped to one enclosure, which is one
  Attempt's scaffolding, so two Work Units never queue on each other, and it says
  nothing about any other host. V1 is single-host by qualification and this does
  not change that.

No schema change, no new durable state, no new record.

## 6. Retained evidence

| Record | Tree | Result |
|---|---|---|
| [`issue-13-stage-order.json`](evidence/issue-13-stage-order.json) | n/a — Git only | 5/5 trials register before populating the directory and before attaching the branch; incomplete-tree window 741–1070 ms |
| [`issue-13-race-reviewed.json`](evidence/issue-13-race-reviewed.json) | `00db4a2` | **12/12 rounds violate**, 36 `inconsistent_result` |
| [`issue-13-race-reviewed-flake-1.json`](evidence/issue-13-race-reviewed-flake-1.json), [`-2`](evidence/issue-13-race-reviewed-flake-2.json) | `00db4a2` | the original flake at the suite's one-file B1: **13/600 rounds**, all four symptoms between them, no round with competing authority |
| [`issue-13-race-remediated.json`](evidence/issue-13-race-remediated.json) | `3806128` | **0/12 rounds violate**; all four callers converge |

Each round's record holds what every caller was handed *and* the durable state
afterwards, because those disagree — that disagreement is the whole finding.
`README.md` has the commands. The `reviewed`/`remediated` pair ran 4 racers over
a 3000-file B1; the `flake` records ran 2 racers over the suite's one-file B1 to
reproduce the gate's own conditions. Two flake runs are retained because §3.4 is
rare enough to appear in only one of them; every other symptom appears in both.

In the product suite:

| Fact | Test | Against `00db4a2` |
|---|---|---|
| Identical concurrent admissions resolve one Attempt, with no provisioning in front of it | `test_identical_concurrent_admissions_alone_resolve_one_attempt` | passes |
| Differing concurrent admissions leave one authority | `test_two_differing_concurrent_admissions_leave_one_authority` | passes |
| Identical concurrent admission and provisioning converge | `test_two_identical_concurrent_admissions_resolve_one_attempt` | flakes (§3.1, §3.2) |
| Every concurrent caller is handed the finished worktree | `test_every_concurrent_caller_is_handed_the_finished_worktree` | **fails, 3 of 4 callers** |
| A second provisioner waits rather than reading a half-built worktree | `test_a_second_provisioner_waits_rather_than_reading_a_half_built_worktree` | **fails** |

## 7. Limitations

* The stage boundaries of §2 are Git's, observed on `git version 2.47.3`,
  Linux 6.17.13, CPython 3.13.5. The remediation does not depend on their order —
  it removes the need to reason about them — but §3's specific symptoms do.
* The window widths are host- and repository-dependent. `--files` sets them; the
  ordering does not change.
* This addresses G2-V1 §5.3 only. §5.1 and §5.2 are #14's and remain open.
