# Issue #21 — abandonment, cessation and owned retirement

Status: **COMPLETE — #21 acceptance audit satisfied**. This record covers #21
only; no replacement, disposition or G4 verdict is included.

The phase entered at `8838d98696822d31f8bbf96633ac925c15541124` after G3-V1 PASS.
The current v0.5 target and plan govern, followed by G1 qualification, current
P2/P3 implementation, G2/G3 evidence and #21. The foundation landed at `b228b14`;
bounded containment/retirement and the shared-Git blocker landed at `68f9ad7`.
The two earlier #21 blocker records remain historical, as do all G1/G2/G3 records.

## Approved source-placement restriction

The actual provider changed shared Git config/refs when the source common Git
directory was under `/tmp`. The user approved rejecting that unsupported source
placement, rather than changing outer mounts. `QualifiedCodexProfile.validate`
now resolves Git's actual common directory strictly from the worktree and
compares it component-wise against canonical `/tmp`. Git/OS resolution failure
rejects. The check precedes provider version probing and the prepared-to-dispatched
transition. Symlinks and separate Git directories do not hide unsafe placement.
The request identity records `sharedGitPolicy=canonical-outside-slash-tmp-v1`.

The narrow root set follows the selected profile, not a generalized filesystem
policy. [Codex 0.153.4's sandbox policy](https://github.com/openai/codex/blob/rust-v0.153.4/codex-rs/protocol/src/protocol.rs#L1189)
includes `/tmp` and a supplied `TMPDIR`. This product supplies no `TMPDIR`.
At pinned Zeroshot revision `d0909615d6ba3c179b58bce15a059f40400ec995`, independently
inspected source establishes the environment boundary:

| Source | Fact | SHA-256 |
| --- | --- | --- |
| `zeroshot-rust/src/native_v2_runner/workspace.rs:54` | Runtime values must exactly match declared connections. | `1316e456f5d5f2a89bbae8b1bd648872ac2efe75bc9273180904f48971f93e6e` |
| `zeroshot-rust/src/native_v2_codex/command.rs:17` | Provider environment adds only HOME, CODEX_HOME and PATH. | `d6863b61ec62329c5e319d0969f80842595a9cbf36ba9805725052fa005c1ff3` |
| `zeroshot-rust/src/execution/process/spawn_recovery.rs:109` | Child launch clears inherited environment before setting exact values. | `8b02301a045df70bc06a87134477d3344f3838ee06a89834ef00e7f72c081878` |

Product `assurance_runtime()` declares only the three profile path variables
and the deterministic evidence selector. An ambient controller `TMPDIR` thus
does not expand provider writable roots. No `/var/tmp`, `/run` or other broad
root blacklist is inferred. Existing durable-worktree policy remains separate.
Test source repositories now use an explicit durable fixture root; negative
controls explicitly create the unsafe temporary source.

## Acceptance audit

| #21 obligation | Implementation and discriminating support |
| --- | --- |
| Durable facts; preserving migration; one current Attempt | Schema 6, immutable abandonment/cessation records and monotonic retirement acknowledgment. `test_abandonment_foundation.py` compares populated v4 identity/Contract/B1/correlation/P3 rows across migration; `test_retirement.py` preserves v5 abandonment. Empty currentness cannot authorize another ordinary admission. |
| Ineligible before stopping; irreversible | `abandon_attempt` commits before runtime calls or deletion. SQL update/delete/replace guards and real caller-death windows prevent restoration. |
| Narrow public stop adapter | `stop_known` uses public known-run force-stop/status, retaining diagnosis only. `test_stop_adapter.py` covers repeated stop and inconsistent/inaccessible observations. |
| Actual physical cessation and no interference | W5-style parent-death PID containment, gated durable PID1 receipt and pidfd teardown; real detached writer stops automatically after controller loss. Uncontained negative keeps writing. Same-repository sibling remains active through product retirement. |
| Never-dispatched versus ambiguous | Never-dispatched stop invokes no SDK. Missing dispatched identity, legacy profile, unknown/live containment and interrupted allocated provisioning block retirement. No submission replay discovers old runs. |
| Currentness and late external results | Admission/provisioning and submit acknowledgment races share currentness exclusion; final capture rechecks after observation. Abandoned custody remains readable as historical material; stale capture/replay/provisioning cannot restore A1. |
| Exact owned retirement | Marker, canonical ownership, Git registration/common-directory and branch checks; only owned dirty worktree/local branch removed. Store/custody/enclosure/sibling survive. Git children inherit the existing provisioning lock across caller death. |
| Repetition, concurrency and fault windows | Overlapping public stop callers explicitly repeat after any understood lost acknowledgment and converge. Concurrent retirement waits for inherited Git lock; process death after removal before acknowledgment converges. Before/after abandonment, stop and terminal observation are covered. |
| Separate product witnesses | Fresh lifecycle controls retain stop during repair mutation and adjudication, controller death, caller death after directive and after final custody, pre-stop/post-terminal interruption, late-result refusal and concurrent stop. Controlled semantic timing is distinct from actual provider qualification. |

The [implementation boundary](../../docs/implementation/v1-p4-abandonment.md)
describes physical proof and intentionally blocking cases in detail.
No completed-history recovery, semantic carryover, effects, sealing/provenance,
scheduler/session manager, second runtime ledger/router/validator, replacement
or disposition is introduced. P3 custody does not become Work Unit success.

## Fresh evidence and current checks

- [Actual-provider requalification](issue-21-qualified-provider.md): all seven
  controls PASS on the restricted source profile, including unsafe pre-dispatch
  refusal, admitted mutation/read-only/no-effect boundaries and controller-loss
  cessation with live sibling continuity through retirement.
- [SDK-free regression](evidence/issue-21-restricted-without-sdk.txt): **315 tests
  and 2,160 subtests passed; 39 expected SDK-dependent skips**.
- [Fresh pinned-SDK lifecycle](evidence/issue-21-restricted-lifecycle.json) and
  [execution log](evidence/issue-21-restricted-lifecycle-tests.txt): **6 tests /
  9 controls passed in 212.320 seconds**, with no skips. Parent verified every
  retained source hash against the frozen current files. Concurrent stop retained
  one understood transport error and explicit repeated-stop convergence.
- [Parent profile audit](evidence/issue-21-parent-profile-audit.json) independently
  checks exact current graph/runtime/profile identities and actual completed
  command-execution output for all four sandbox-boundary roles. A read-only
  reviewer separately checked record hashes, raw output and heartbeat progression.
- [Complete pinned-SDK regression](evidence/issue-21-restricted-sdk-full.txt):
  **354 tests and 2,246 subtests passed in 1,581.52 seconds**, with no skips and
  one failed timeout-descendant subcontrol. That run used the old one-second
  fixture; the diagnosis and correction below are retained explicitly. It is
  not represented as an all-green invocation.
- [Complete affected evidence-suite rerun](evidence/issue-21-requalified-evidence-suite.txt):
  **1 test and all 14 subcontrols passed in 275.96 seconds**, with no skips.
  Together with the complete run, this verifies every current test assertion:
  the only subsequent change is the explicit timeout fixture correction, and
  its complete consumer suite was rerun. No product code changed between runs.
- Changed Python files pass Ruff lint/format checks; `git diff --check` passes.
  Parent and independent read-only review found no remaining #21 acceptance gap.

The current product launcher, containment, graph/runtime, evidence leaf and
SDK/sidecar remain unchanged from `68f9ad7`; the only subsequent product change
is the source-placement check and its profile identity. Tests and qualification
fixtures reflect that supported layout. Exact per-run profile identities,
host/integration versions, occurrence/run references, observations and limitations
are retained in the linked machine evidence.

## Explained test failures retained

The first concurrent-stop test incorrectly required every overlapping SDK call
to receive a successful reply. The [focused reproduction](evidence/issue-21-concurrent-acknowledgment-reproduction.txt)
and [initial lifecycle log](evidence/issue-21-concurrent-acknowledgment-initial.txt)
retain the observed `TargetError: Zeroshot Rust observation transport disconnected`.
Pinned `native_v2_portable_controller/process.rs:220–255` exits after terminal
publication without draining detached connection handlers; `native_v2_cli/local.rs:120–149`
reconnects on connection establishment failure, not an RPC lost mid-call. Parent
and read-only review independently identified this acknowledgment-loss window.

The corrected witness collects both caller outcomes, accepts only this specific
understood transport error, and explicitly repeats administration. It requires
permanent abandonment, no retirement without proof, matching immutable safety
records, and identical repeated retirement. Product errors still propagate;
there is no automatic retry or new product serialization. The initial failed
lifecycle JSON is not positive qualification and its end-of-run source hashes
are not asserted to identify the already-loaded failed test body.

The first SDK-free run overlapped construction of the new negative fixture and
observed a temporary ownership mismatch; the frozen rerun above passes. The
provider report separately retains explained fixture setup/diagnostic mistakes.
Original actual `/tmp` shared-Git failure evidence is preserved, not relabeled.

The [initial complete pinned-SDK run](evidence/issue-21-restricted-sdk-initial.txt)
reported 353 passed tests, the old concurrent-reply assertion failure, and a
timeout-descendant subcontrol failure. The latter was investigated separately;
it was not erased by rerunning the suite. Two unchanged one-second timeout cases
([first](evidence/issue-21-timeout-initial.json),
[second with logs and timing](evidence/issue-21-timeout-diagnosis.json)) returned
`execution_unusable`, no graph progress beyond implementation, and no descendants
after terminalization, but neither observed the required live child beforehand.

The new launcher hashes the exact pinned 311,856,272-byte controller executable
before namespace/check startup. The retained identical hash operation took
1.467924 seconds on this host, longer than the test-only one-second node budget.
That budget no longer reliably reaches the intended live-child fault window.
The product's normal node budget remains 300 seconds. Only the current fixture's
declared timeout and expected deviation now use five seconds, while its child
still runs for 60 seconds. Required observed-live-child and absent-after-terminal
assertions remain unchanged. Parent and read-only review approved this bounded
timing correction; no product identity or containment mechanism was weakened.

The [focused requalification](evidence/issue-21-timeout-requalified.json), run
`01a08200-f624-7c91-b5fd-af50d9493deb`, observes child PID `3376627`, then
`execution_unusable` with zero descendants after terminalization and no graph
progress beyond implementation. The complete affected evidence suite passed
against the corrected fixture. Historical G3 timeout evidence remains unchanged;
this is its explicit affected-control requalification for #21.

## Completion boundary

The parent independently checked the acceptance map, exact current profile and
retained observations, explained both regression failures, verified the affected
reruns, and checked that governing/G1/G2/G3 files have no changes from `8838d98`.
Implementation and evidence are committed together before the GitHub completion
comment; that comment identifies the landed commit after remote reachability is
verified. No #22 work is included.

Unknown cessation, missing dispatched identity, legacy/unqualified bindings and
unacknowledged interrupted provisioning remain deliberately blocking. No terminal
label or retained P3 custody authorizes retirement or Work Unit success by itself.
The later actual-provider repair-to-disposition slice remains a #24/G4 obligation,
not a claim made by these #21 confinement and administrative witnesses.
