# Issue #8 — V1 W1/W2/W5/W7 qualification

**Run date:** 6 September 2026 (UTC)

**Scope:** qualification only; W1, W2, W5 and W7 individually

**Verdicts:** **W1 PASS; W2 FAIL; W5 PASS; W7 FAIL**
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
per fixture Attempt, and `sessionScope: execution`. Every worktree branch starts at immutable B1.
Worktrees of the same fixture repository deliberately share Git common metadata so that the claimed
boundary is tested rather than inferred from different paths. The graph, runtime and initial input
are retained verbatim in the machine record. No `GitDelivery` is present. Only the Codex provider
leaf is controlled; its SHA-256 is
`2eb6c6326180ac104896f2e4f2f70e285e68839209d0e9d855662d5ba97a46fe`.

The machine evidence is [issue-8-run-record.json](evidence/issue-8-run-record.json), SHA-256
`7449b6ab2b6768d1e73d6f7d9df80769fd0008de970336411d481c9d95845dcd`. That checksum is the
committed record before any later rerun; use the repository blob as the retained authority if the
record is regenerated. The driver records prompt digests rather than prompts or credentials.

## W1 — PASS

Before submission, the fixture wrote the immutable Contract/Attempt relationship, B1
`d771662ccecccd3762ff7a3116c585577b49780b`, exclusive worktree, stable submission key,
candidate SHA-256 `5c5cd9cbb543a8b26894f78b4b8c68d060d9f515e32f2e470e4d95067b96a962`, and instruction
SHA-256 `32446e03f7d914446929492c1d9b88a48fb5f3db6d3c927283dbc393856caa9f`. The admitted record
also retains the instruction bytes and raw-evidence digest, sufficient to verify fresh
rematerialization.

The caller process exited 87 immediately after `Client.submit()` returned without returning its
handle. Inventory and unchanged replay exposed exactly one new run,
`01a07896-23c0-7fe3-b9c0-b63698cfe32d`. Replaying the same key recovered that run. A conflicting
request and, separately, the same key from live B2 source both raised `SubmissionConflictError`
naming that run; neither changed inventory.

Live HEAD moved to `8cb2ca2bb5130a3bfb2331c3c51706efbe9f3e1e` with different candidate and instruction bytes.
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

The selected profile did not enforce the required boundary:

- reviewer, adjudicator and final-assessor candidate writes all succeeded in run
  `01a07896-334e-75a0-b8b0-c026cee6cc6a`;
- a detached lingering writer outlived its implementing leaf and changed the candidate to
  `LINGERING_WRITER` in run `01a07896-38d2-7f62-a62a-545f157d3790`;
- a graph-authorized worker wrote a shared path outside its candidate worktree in run
  `01a07896-4371-7272-a209-5e3c5159d523`;
- removal of required `evidence/raw.txt` still yielded runtime success in run
  `01a07896-47e1-71c2-9832-1148355d350b`;
- forged Contract/source/evidence/predecessor claims reached the accepting fixture route in run
  `01a07896-4cc4-7631-817b-5bc5ef568dd8`;
- the W7 controlled probe additionally changed the same repository's shared Git config and refs.

These are counterexamples, not Broodling acceptance. Separate paths and graph role names do not
provide read-only assurance, writer lifetime containment, shared-path protection, or trusted
applicability.

**Not executed after the decisive failures:** an additional delayed-observer race and a complete
intentional-untracked/generated-content matrix. Historical #5 already establishes that an external
observer can lose its race, but it is not promoted to a new-profile positive. These omitted cases
cannot improve the verdict and must be rerun with the other W2 cases if a narrower enforcing profile
is proposed. No generic sealing or completed-occurrence mechanism was introduced.

**Affected consumers:** G2-V1, G3-V1 and G4-V1.

## W5 — PASS

The real transport exercised five independent abandonment windows:

| Window | Run | Durable terminal observation |
|---|---|---|
| stop during adjudication | `01a07896-5230-77d0-83cb-a37b3b5d8a7a` | `force_stopped` |
| stop during mutation | `01a07896-55ce-7740-869d-91131df5cc57` | `force_stopped` |
| controller `SIGKILL` | `01a07896-59fa-7211-b3a4-dd47a313786e` | `runtime_lost` |
| caller exit after directive/repair dispatch | `01a07896-6002-7a23-b166-85acf8d1ab36` | `force_stopped` |
| caller exit after final output/runtime result but before disposition | `01a07896-65ad-7c40-b541-7f80e4d2c242` | existing success observed, Attempt abandoned |

The fixture durably marked each Attempt ineligible before requesting/observing termination or, for
the already-terminal final-output window, before reconciliation. Repeated `force_stop()` returned
the same terminal result. Each old worktree was retired only after a terminal result. No old result
changed its `eligible: false` fact.

Replacement run `01a07896-6ad6-7543-80a2-928e3e8263cc` used a new Attempt, run, branch,
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

## W7 — FAIL

The minimal fixture admission configuration admitted the genuinely empty-effect Contract and
rejected, without deleting obligations, required commit delivery, push, PR/merge, issue
comment/closure/label/relationship, and publication-dependent evidence. Run
`01a07896-6e80-7050-9bc2-07c38ec187ef` completed the no-effect control. An unused optional local
permission caused no effect. Run `01a07896-73c5-7320-bfce-851ba4838bf7` returned bare runtime
success with a final `gap`; fixture disposition remained false, so runtime success was not treated
as acceptance.

The execution boundary nevertheless failed. In run
`01a07896-7db5-7062-a6ca-1c950c08aa7f`, the controlled agent process successfully:

1. changed shared Git configuration;
2. created a tag in shared repository metadata; and
3. pushed that tag to the controlled local bare remote.

The process could resolve `/usr/bin/git` and the installed `gh`, and could read the configured
GitHub hosts file. The probe recorded only accessibility, never file contents or credentials. No
GitHub command or remote mutation was attempted. All authoritative-mutation targets were
host-local controlled fixtures.

Excluding `GitDelivery`, declaring no effect, and omitting a credential from the explicit runtime
connection are therefore insufficient: ordinary agent execution can bypass the boundary through
ambient tools, readable credential configuration and shared Git state.

**Affected consumers:** G2-V1, G4-V1 and G5-V1. The smallest missing dependency is a supported
execution/profile configuration that removes those credential/tool/mutation paths while retaining
the real V1 graph. This is not a request for an effect executor, receipt system or reconciliation
suite.

## Disposition

Issue #8's qualification work is complete even though two witnesses fail. W2 and W7 block G1-V1;
their consumers cannot proceed. W1 and W5 remain bounded passes on the exact profile above. The
historical Q/G1 verdicts remain unchanged. This work did not begin issue #9 or later work, implement
Broodling product code, restore v0.3 recovery/effect requirements, invoke a real provider, or make
an unauthorized GitHub mutation.
