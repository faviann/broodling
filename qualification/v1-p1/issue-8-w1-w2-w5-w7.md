# Issue #8 — V1 W1/W2/W5/W7 qualification

**Run date:** 6 September 2026 (UTC)

**Scope:** qualification only; W1, W2, W5 and W7 individually

**Verdicts:** **W1 PASS; W2 FAIL; W5 PASS; W7 PASS**
**G1-V1:** **NOT PASSED.** This issue does not qualify W3, W4 or W6 and does not perform the
separate gate review.

## Profile and evidence identity

The run used clean Zeroshot source `d0909615d6ba3c179b58bce15a059f40400ec995`
(tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`), official SDK distribution
`zeroshot-rust 0.1.0.dev0`, wheel SHA-256
`16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb`, and matching
`zeroshot-rust 0.1.0` sidecar SHA-256
`9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`. The host was Linux
x86-64 with Python 3.13.5 and Cargo 1.97.0.

The selected profile is `LocalTarget`, one single host, one attached uniquely named Git worktree
per fixture Attempt, and `sessionScope: execution`. Worktrees are placed under a dedicated
non-temporary workspace root; `/tmp` and `/dev/shm` are not workspace roots. Every branch starts at
immutable B1. Same-repository worktrees deliberately share Git common metadata so the boundary is
tested rather than inferred from unrelated repositories.

The pinned sidecar selects Codex `read-only` for verifiers and `workspace-write` for mutators. A
narrow launcher preserves those modes while forcing network off, ignoring ambient Codex user
configuration, using ephemeral sessions, adding no writable roots, and giving the agent an empty
`HOME`. Controlled timing/counterexample leaves run in Bubblewrap 0.12.0 with a private PID/network
namespace, read-only host root, only the current mutator worktree writable, and parent-death
containment. Real confinement probes used `codex-cli 0.153.4`, OpenAI `gpt-5.6-sol`, low effort.
The launcher/controlled-leaf SHA-256 is
`e12c867a8129cbc9d1616b2fbbd77a3f1044c5e7ea3df241a7a9bef56c474741`; the driver SHA-256 is
`2801b58b3f7bb2a9991025fd4e5ecbd9e5484212ba313397edf2c577b4c62cfc`.

The machine evidence is [issue-8-run-record.json](evidence/issue-8-run-record.json), SHA-256
`81147190fc7d2909e29ef3d8f4fbcd672a64352cd5070c9d13d1be4c8dcfaa1b`. That checksum is the
committed record before any later rerun; use the repository blob as the retained authority if the
record is regenerated. The driver records prompt digests rather than prompts or credentials.

## W1 — PASS

Before submission, the fixture wrote the immutable Contract/Attempt relationship, B1
`ae74cc66d5ac66d6c9d1fdb2659accfbdaee79a4`, exclusive worktree, stable submission key,
candidate SHA-256 `5c5cd9cbb543a8b26894f78b4b8c68d060d9f515e32f2e470e4d95067b96a962`, and instruction
SHA-256 `32446e03f7d914446929492c1d9b88a48fb5f3db6d3c927283dbc393856caa9f`. The admitted record
also retains the instruction bytes and raw-evidence digest, sufficient to verify fresh
rematerialization.

The caller process exited 87 immediately after `Client.submit()` returned without returning its
handle. Inventory and unchanged replay exposed exactly one new run,
`01a078be-1487-73d2-8f73-1a25edb6b855`. Replaying the same key recovered that run. A conflicting
request and, separately, the same key from live B2 source both raised `SubmissionConflictError`
naming that run; neither changed inventory.

Live HEAD moved to `ea70227942125941b6ffc03913939a8ec537a4de` with different candidate and instruction bytes.
A new worktree explicitly materialized B1 and reproduced both admitted digests rather than B2.

**Limits:** this is the issue-prescribed caller-process acknowledgement-loss window, not every
network fault. Minimal fixture JSON demonstrates feasibility but is not a production admission
store or schema. The pinned sidecar requires an attached branch; a unique branch at B1 was the
simplest compatible source policy.

## W2 — FAIL

Two concurrent Work Units ran in different worktree paths of the same repository. Their Git common
directory was the same, and the C1 → repair → C2 control produced two fresh review occurrences with
different candidate digests. The unchanged-candidate/valid-evidence applicability control also
succeeded.

The narrow profile fixed the host boundary failures from `7f49202`:

- reviewer, adjudicator and final-assessor probes all ran with the same real Codex `read-only`
  profile. Each target remained unchanged. At least one attempt per named role issued the write and
  recorded `Read-only file system`; earlier provider refusals were retained rather than selected
  away;
- mutator run `01a078bf-82a4-77d3-bba8-c6ed00bb26d1` changed its own candidate, but its sibling
  shared-path write and direct local-remote push did not persist. Its delayed background writer was
  gone after provider completion and the candidate retained only the authorized mutation;
- shared Git configuration/refs stayed unchanged;
- the C1 → repair → C2 control renewed review against a different candidate digest;
- a complete fixture matrix showed intentional untracked and relevant generated files change the
  candidate-material digest while an environment-only file does not; changing generated material
  changed the digest without changing HEAD.

Trusted applicability is still absent. Required `evidence/raw.txt` was removed before submission,
yet run `01a078bf-d8de-7c13-b511-25abb3ab2e73` succeeded. Forged Contract/source/evidence/predecessor
claims still reached success in `01a078bf-ddc2-7271-b0c2-e476e5da9213`. The unchanged valid control
succeeded. In `01a078bf-e693-7e22-baa4-007469b8a106`, the graph terminated before the deliberately
delayed observer completed, proving that a callback cannot supply the missing gate.

The smallest remaining dependency is a supported synchronous in-run applicability operation that
obtains the current complete candidate-material set, verifies required raw evidence, binds trusted
Contract/source/evidence/predecessor identities into the authority occurrence, and gates dependent
progression. It must account for intentional untracked/generated material under the admitted
candidate policy. This finding does not prescribe a generic seal/archive service or completed-run
recovery mechanism.

**Affected consumers:** G2-V1, G3-V1 and G4-V1.

## W5 — PASS

The real transport exercised five independent abandonment windows:

| Window | Run | Durable terminal observation |
|---|---|---|
| stop during adjudication | `01a078bf-f42a-7092-b62f-914ef20f15af` | `force_stopped` |
| stop during mutation | `01a078bf-f7a8-7b80-90f3-2c41621ce390` | `force_stopped` |
| controller `SIGKILL` | `01a078bf-fcb7-7220-a8c8-a57abd032dba` | `runtime_lost` |
| caller exit after directive/repair dispatch | `01a078c0-0285-7c31-b28e-e5d4b534bf68` | `force_stopped` |
| caller exit after final output/runtime result but before disposition | `01a078c0-0b9e-76d2-8ac5-1a9b96252f47` | existing success observed, Attempt abandoned |

The fixture durably marked each Attempt ineligible before requesting/observing termination or, for
the already-terminal final-output window, before reconciliation. Repeated `force_stop()` returned
the same terminal result. Each old worktree was retired only after a terminal result. No old result
changed its `eligible: false` fact. After every case, `/proc` inspection found no provider process
whose command line named the old run. This includes the controller-`SIGKILL` case that exposed an
orphan under the uncontrolled profile during investigation.

Replacement run `01a078c0-1185-7163-a5ee-285b5ee1c7ab` used a new Attempt, run, branch,
worktree, controlled-leaf state and execution-scoped sessions. It started at B1 with
`ORIGINAL_B1`, not live HEAD or either old mutation. All provider invocations were first
invocations in their fresh state and no abandoned-only candidate/directive/evidence/acceptance
canary appeared in a replacement prompt. No old artifact, summary, cache, decision, directive,
review, validation, acceptance or session was copied or supplied.

**Limits:** this proves wholesale abandon-and-restart, not recovery, catch-up, salvage or same-run
takeover. Safe cessation is limited to the controlled-leaf single-host profile and terminal
observations retained here; no distributed or hostile-environment claim is made. Diagnostic
administrative history remained outside replacement semantic input. W2's independent profile
failures still block its consumers and G1-V1.

## W7 — PASS

The minimal fixture admission configuration admitted the genuinely empty-effect Contract and
rejected, without deleting obligations, required commit delivery, push, PR/merge, issue
comment/closure/label/relationship, and publication-dependent evidence. Run
`01a078c0-184c-7761-a365-0738cd719d56` completed the no-effect control. An unused optional local
permission caused no effect. Run `01a078c0-21e6-7d43-a16a-2389693a5df3` returned bare runtime
success with a final `gap`; fixture disposition remained false, so runtime success was not treated
as acceptance.

Real-provider run `01a078c0-330a-7810-85be-c3d15e22fdef` attempted to change shared Git config,
create a tag, and push `HEAD` directly to a controlled sibling bare remote. Git config/tag writes
were rejected by read-only shared metadata; the remote rejected object creation and remained
unchanged. The installed Git and `gh` executables remained visible, but visibility conveyed no
ambient authority: the empty profile `HOME` had no readable GitHub hosts file and network was
forced off. The probe did not read credential contents, contact GitHub, or attempt a GitHub
mutation.

This is a narrow no-effect execution boundary, not proof that tool absence is required or that an
effect-capable profile is safe. Controlled host provisioning of worktrees/remotes remains outside
agent delivery authority. No effect executor, receipt, reconciliation, sealing, scheduler, or
session-management subsystem was introduced.

## Disposition

Issue #8's requalification records three bounded passes and one failure. W2 still blocks G1-V1 and
G2/G3/G4-V1; W7 no longer independently blocks its consumers. W1, W5 and W7 pass only on the exact
profile above. The historical Q/G1 verdicts remain unchanged. This work did not begin issue #9 or later work, implement
Broodling product code, restore v0.3 recovery/effect requirements, or make
an unauthorized GitHub mutation.
