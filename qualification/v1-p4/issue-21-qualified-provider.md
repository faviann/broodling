# Issue #21 — approved profile restriction and fresh provider evidence

All seven fresh checks below pass. This is bounded #21 qualification, not an
issue-completion or G4 verdict. Earlier unsafe-source counterexamples and failed
fixture runs remain unchanged in their original records.

The approved profile identifies its restriction as
`sharedGitPolicy=canonical-outside-slash-tmp-v1`. Product first-dispatch validation
resolves actual shared Git metadata and rejects it inside canonical `/tmp`,
which is the pinned mutator's ambient writable scratch root. Positive controls
use a dedicated durable source repository and dedicated durable Attempt
worktrees. The provider auth-only home and diagnostic runtime remain in short
private `/dev/shm` paths as before; these are not Attempt worktree roots.

| Fresh witness | Actual observation |
| --- | --- |
| [Unsafe source](evidence/issue-21-qualified-provider-unsafe.json) | Explicit `/tmp` source is rejected while submission remains prepared; no run id, provider coding invocation or native run directory exists. |
| [Controller loss and live sibling](evidence/issue-21-qualified-provider-loss.json) | Actual detached writer ceases automatically after controller SIGKILL; independent same-repository sibling keeps writing through old Attempt product stop and retirement. |
| [Mutation](evidence/issue-21-qualified-provider-mutation.json) | Actual own-worktree mutation succeeds; sibling/shared-Git writes fail; visible local remote rejects object creation; network fails and remote refs remain empty. |
| [Reviewer](evidence/issue-21-qualified-provider-reviewer.json) | Actual read-only command execution denies own/sibling/shared-Git writes and network; exact product graph completes. |
| [Adjudicator](evidence/issue-21-qualified-provider-adjudicator.json) | Same real read-only boundary controls pass with an actual adjudicator occurrence. |
| [Final assessor](evidence/issue-21-qualified-provider-final.json) | Same real read-only boundary controls pass with an actual final-assessor occurrence. |
| [Uncontained sensitivity control](evidence/issue-21-qualified-provider-negative.json) | Identical detached helper deliberately outside containment survives parent death and keeps changing its marker; exact PID/start-time cleanup follows. |

Actual controller-loss run `01a081e5-b33e-7c30-a967-e846e5c255d4` has a distinct
same-repository sibling run `01a081e5-a116-74a2-a183-e692c5a6e6b4` belonging to
fixture Work Unit #210001. The sibling uses a controlled provider leaf through
actual product admission, graph, runtime and pinned SDK, and remains active in
implementation while the actual loss probe runs. Its increasing heartbeat is
observed before/after old-controller death and again after
`AbandonmentCoordinator.stop()` and `retire()`. The old worktree is absent;
comparison of refs confirms only its local Attempt branch was removed. The
sibling is stopped and its physical cessation confirmed only during final
fixture cleanup. This is independent Work Unit isolation, not an A2 retry.

The actual writer helper double-forks, detaches with `setsid()`, redirects its
standard descriptors, and writes a numbered marker every 200 ms while the real
provider's foreground command waits. Concrete process/start-time/PID-namespace
observations and marker bytes are retained. After controller SIGKILL, host
observations show no surviving writer and stable bytes before product stop or
physical confirmation is requested. `runtime_lost` is only diagnostic. Product
stop records the physical cessation basis and product retirement removes the
old worktree; neither result is treated as semantic success.

Every positive run uses the exact product graph/runtime/evidence leaf. A test
provider dispatcher forwards only its selected role to actual Codex and supplies
controlled valid responses for other model roles. The dispatcher appends a
precise local boundary-probe instruction to the selected role. These controls
qualify launch/sandbox/lifetime behavior, not unmodified prompt reliability or
normal success disposition. Actual commands and host snapshots, rather than
provider assurances, support the checks.

The [profile audit](evidence/issue-21-qualified-provider-profile-audit.json)
retains each record's checksum, policy, durable source/canonical common-Git path,
and a separately labeled post-run hash audit of the frozen `codex_profile.py`
and fixture `tests/support.py`. The first fresh batch did not hash those two
files at invocation; its original invocation hashes are unchanged. The current
harness includes both files in future invocation source hashes. Product launcher
and containment identities are retained directly in every submitted profile.

Two additional fixture mistakes supplied no completed positive witness and are
[recorded explicitly](evidence/issue-21-qualified-provider-fixture-errors.json):
a missing mechanical-evidence declaration prevented sibling submission, and a
later diagnostic query used the old worktree as its cwd after successful
retirement. The fixture now declares evidence and observes the diagnostic result
before retirement. Neither correction changed product code.

Reproduce with the pinned SDK Python and distinct outputs:

```sh
/home/faviann/.cache/broodling-zeroshot-venv/bin/python qualification/v1-p4/issue21_provider.py --case unsafe --output /path/to/unsafe.json
/home/faviann/.cache/broodling-zeroshot-venv/bin/python qualification/v1-p4/issue21_provider.py --case loss --durable-source --output /path/to/loss.json
```

Use `--durable-source` for `mutation`, `reviewer`, `adjudicator`, and `final` as
well. `negative` deliberately bypasses containment for its sensitivity test.
The private copied authentication file is removed during cleanup; credential
contents never enter retained evidence. Existing #21 administration/fault-window
and G1/G2/G3 compatibility suites remain separate obligations.
