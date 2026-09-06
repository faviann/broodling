# Issue #7 — G1-core evidence review

**Review date:** 6 September 2026 (UTC)  
**Scope:** [Issue #7](https://github.com/faviann/broodling/issues/7), gate/evidence review only  
**Review status:** COMPLETE  
**G1-core verdict:** **BLOCKED**  
**Independent G1-effects verdict:** **BLOCKED**, as recorded by [issue #6](https://github.com/faviann/broodling/issues/6); not a prerequisite for this core verdict

| Question | Evidence owner | Reviewed verdict |
|---|---|---|
| Q1 — Recoverable authority occurrences | [#3][I3] | **BLOCKED** |
| Q2 — Admission/run correlation | [#3][I3] | **PASS**, within the recorded witness scope |
| Q3 — Fail-closed protocol expressiveness | [#4][I4] | **PASS**, for the recorded canonical-signal fixture |
| Q4 — Trusted in-run source/evidence handoff | [#5][I5] | **BLOCKED** |
| Q5 — Automatic context and session isolation | [#5][I5] | **BLOCKED** |
| Q6 — Stop/loss and semantic catch-up | [#3][I3] | **BLOCKED** |

All six questions must pass for G1-core to pass. Two bounded passes and four blocked questions do not authorize P2 or the no-effect product slice. The gate review is complete even though the integration is not qualified. Closing #7 records completion of this review, not resolution of #3/#5, qualification of effects, or permission to start later phases.

## 1. Governing basis and evidence boundary

Authority order is the supplied [target v0.3][T], then the supplied [implementation plan v0.3][P]. In particular, target §§2.3, 4.3, 6.4, 7.3–7.6 and 13–14 govern designated-occurrence authority, trusted applicability, terminalization/catch-up, and the information boundary. Plan §4 P1 defines Q1–Q6 and the independent G1-core/G1-effects gates. v0.2 and historical experiment passes do not substitute for this qualification.

The supplied input SHA-256 values were checked locally and match the values recorded in the [G0 baseline][G0]:

| Input | SHA-256 |
|---|---|
| Target v0.3 | `58599595020680880c334e85274160421943ca884deb76595ff193aa1af1c1d6` |
| Implementation plan v0.3 | `31b35558dc0576d133dc623ed0b5eece3f45ec6ba1b98459d9d13c591f928a04` |

The reviewed Broodling `main` snapshot was `3a486f270f5fdc1f029d6f255c1ede62b9378f03`. Issue #1 is complete with G0 PASS for specification completeness only. Issue #2 is complete with an executed external harness, not a G1 pass. Its [build record][E2] and [reproduction instructions][H2] are the common starting point.

This review read the issue requirements and completion comments, the full #3–#5 reports, relevant retained machine-record witnesses, and the Q3 graph-construction code. It compared claims and limitations against v0.3. It did **not** rebuild Zeroshot, rerun qualification/provider experiments, inspect private SQLite, or attempt to resolve a dependency. Runtime observations below are retained P1 evidence, not new executions by this review. Source explanations are identified in the original reports at their pinned revision, not represented as a new exhaustive upstream audit.

## 2. Chosen integration, exact versions, and reproducibility

The evidence concerns one external integration family: official async Python SDK `LocalTarget` → bundled matching Rust sidecar → local single-host, one-run controller, with custom GraphSpec/RuntimePlan inputs. SDK transport, Rust admission, source resolution, graph execution/routing, observation, and stop mechanics were real. #3, #4, and Q4 of #5 controlled only the Codex-compatible provider leaf. Q5 of #5 used the real installed Codex CLI/provider. A controlled leaf cannot qualify the real provider's context boundary.

| Shared item | Recorded identity |
|---|---|
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995`, clean and unpatched in the reports |
| Zeroshot tree | `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d` |
| SDK distribution/version | `zeroshot-rust 0.1.0.dev0` |
| Sidecar version | `zeroshot-rust 0.1.0` |
| Sidecar SHA-256, #2–#5 | `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Python/platform | Python `3.13.5`; `Linux-6.17.13-2-pve-x86_64-with-glibc2.41` |
| #2–#4 recorded Rust/Cargo | `rustc 1.97.0 (2d8144b78 2026-07-07)`; `cargo 1.97.0 (c980f4866 2026-06-30)` |
| Controlled provider profile | Codex harness, OpenAI binding, `broodling-p1-controlled-leaf`; test-only credential placeholder |
| Q5 real provider profile | `codex-cli 0.153.4`; OpenAI `gpt-5.6-sol`, low effort, `sessionScope: execution` |

The wheel filename is `zeroshot_rust-0.1.0.dev0-py3-none-linux_x86_64.whl`, but two distinct wheel artifacts were recorded:

| Evidence | Recorded wheel SHA-256 |
|---|---|
| #3 and #4 | `16bc7919f913ccc00853b5a917bc164800c5b44d3b4c4c99f2131d09f9ebeebb` |
| #2 and #5 | `153d19c2ea1b5f6a58cd06a20d6c4003071ced0669c3155a94b476a3bb2dd9ae` |

The reports identify the same clean source, SDK version, sidecar bytes, platform and external seam. They do not establish why the wheel hashes differ or independently prove wheel-byte equivalence; this review does not invent that explanation. The results remain attached to their exact artifacts. No unsupported embedded path, private reader, or passing mock transport is combined into a product capability. In particular, Q2/Q3 positives do not cure Q1/Q4/Q5/Q6, and the real-context negative is not replaced by controlled-leaf input captures. No adapter or dependency patch has been qualified by this evidence set.

### Immutable reproduction and evidence register

Use each **originating Broodling snapshot**, including its `bin/codex`, when following its reproduction. Later commits changed that controlled leaf. A reproduction from current `main` must not be called the original tested fixture merely because its filename is unchanged.

| Owner | Originating Broodling commit | Reproduction and fixture | Retained machine evidence |
|---|---|---|---|
| #2 | `500fdb401edd83b36d791d02074920e607a5c98d` | [Shared instructions][H2] | [Harness record][E2] |
| #3: Q1/Q2/Q6 | `f144e15b15bd85cd788aa21a1c17d973914cb426` | [Report/reproduction][R3]; [driver][D3] | [Issue #3 record][E3] |
| #4: Q3 | `0b1a3b24d25d1467057593ca614b09a0cbce9766` | [Report/reproduction][R4]; [driver][D4] | [Issue #4 record][E4] |
| #5: Q4/Q5 | `03bf963eb7d3c59ffa52c4aee388f1470f444f73` | [Report/reproduction][R5]; [driver][D5] | [Issue #5 record][E5] |

The original reports/build records supply these checksums; they are recorded provenance, not a claim that this review rebuilt binaries or recomputed every remote file digest:

| Owner | Controlled leaf SHA-256 | Qualification driver SHA-256 | Machine evidence SHA-256 |
|---|---|---|---|
| #3 | `4e7bf555695b4ac1c0635b62f6177c1065478da680c1d448813cfab4e39dd241` | `7b4ef407ffce503a3f98155dd0491b256cf05b8cdbf15268df7e7b10e0d564db` | `1776dc4b82759450ff7eed8dc54f87d4fb87d276d20cbb6e9c5ddce230c925f5` |
| #4 | `5634d646fce2f77c8ff5820b649cd21af0424f2e643d8ee487289771d3febab5` | `843233e430a92077e00b1415211804df42623b4f070946f48ce608ae9ced5f55` | `e17485caeee5322932a5508b1d26d468ffcec238abae39aea8342a1171bf3275` |
| #5 | `24d59cf73454b3b6842477cb0465ecfd4589764721ab5ba64943c08079e9df1e` | `63c97db5645dc56bf0ddeb2bba9bbb81db2bb8a09d8bfdef1928ff4bd044e542` | `9292e6659873db89ee091b79715e7f5628865a3741cfa058e1ad6c3aaefc5f64` |

## 3. Q1–Q6 evidence decisions

### Q1 — Recoverable authority occurrences: BLOCKED

**Expected:** after client restart and successful, failed and stopped runs, recover the admitted graph/runtime relationship and exact settled occurrences: identity, trusted input, successful/error/voided outcome and completion order. Distinguish repeated visits and provide a stable terminal boundary with usable retention guarantees. [T §§2.3, 6.4, 14.1; P P1 Q1]

**Reproduction/evidence:** [#3 reproduction][R3], [driver][D3], [machine record][E3], especially `Q1` and the `Q6.controllerKilled` witness it references; exact profile/build in §2.

**Actual:** restarted SDK clients replayed public status/watch/log data. Successful repeated-visit run `01a077aa-8edb-7063-9561-91ebd768ed06` finished at `v2:50`; distinct active selectors exposed two adjudicator visits. Stopped run `01a077aa-9318-7151-891f-2073d9ce4340` finished `force_stopped` at `v2:36`. Killed-controller run `01a077aa-968b-7d73-b49f-b579a270aac8` finished `runtime_lost` at `v2:46`.

These observations do not expose trusted settled input/outcome, success versus error/voiding, completion cursors/order, or the retained admitted graph/runtime relationship. An opaque active selector is not the full recovery contract; a terminal graph result or log transcript is not an authority record. Internal RunLedger facts identified by the report were not accessed as a substitute.

**Missing dependency:** the supported external settled-occurrence read/recovery contract, with version and retention guarantees. Owner #3; consumes G1-core and P4/G4 catch-up/replacement. The runtime node-attempt field is not a Broodling Attempt.

### Q2 — Admission/run correlation: PASS within the recorded witness

**Expected:** a caller-visible lost submission acknowledgement can be reconciled to the original run, including after source/HEAD changes. A conflicting request cannot create competing run/Attempt authority. [T §§4.1, 4.3; P P1 Q2]

**Reproduction/evidence:** [#3 reproduction][R3], [driver][D3], [machine record][E3] `Q2`; same exact #3 integration/build as Q1.

**Actual:** the child client exited with code 87 after SDK submission returned but before communicating the handle/run ID to its parent. A new client replaying key `broodling-issue3-q2-ambiguous-v1` recovered run `01a077aa-8c7f-75d0-acc3-60a79bf64a4a`. After HEAD changed from `8356fc0614a201b5e3b16af72eb1807848a1dc73` to `5011b804a04ac9685d779622d74eb2444302b17f`, replay raised `SubmissionConflictError` identifying that same run. Before/after inventories contained only the original new run; `noSecondRun` is true.

**Boundary:** this is the recorded caller-process acknowledgement-loss simulation, not proof of every transport fault or a production Attempt-admission store. The same-source replay is the valid control; the changed-HEAD conflict is explicit reconciliation, not permission for another Attempt. No unresolved Q2-specific dependency is demonstrated within this scope. Q1's missing authority history remains independently blocked.

### Q3 — Fail-closed protocol expressiveness: PASS for the recorded fixture

**Expected:** unusable execution, contradictory decisions, and unresolved obligations cannot reach clean/accepting progression; valid empty-review and observed-RED controls remain usable. Only designated authority can drive correction, without a duplicate blessing; repair is bounded and final assessment remains separate. [T §§2.3, 7.4–7.5; P P1 Q3]

**Reproduction/evidence:** [#4 reproduction][R4], [graph/driver][D4], [machine record][E4] `cases` and `acceptanceChecks`; exact #4 profile/build in §2.

**Mechanism actually tested:** one canonical enum signal is also written to typed state. Initial authority can emit `none` or `open_d1`; resolution authority can emit only `open_d1` or explicit `resolve_d1`. Review/repair cannot write that obligation state. The adjudication payload must be `null`, so an attempted second contradictory payload is malformed. This is a finite one-directive fixture with three repair rounds, not a selected production directive schema.

| Witness group | Actual retained observation |
|---|---|
| Completed empty-review control | Success with obligation `none`; designated adjudication then separate final assessment |
| Successfully observed RED/finding control | Repair, clean re-review, explicit resolution authority, then final assessment; success with `resolve_d1` |
| Crash, timeout, malformed output, missing output | `execution_unusable`; no clean/accepting progression |
| Refusal witness | Force-stop while review was active; `force_stopped`, no later dispatch |
| Injected failed applicability | `identity_or_applicability_failed` before review |
| Contradictory payload/signal; ordinary reviewer authority-shaped output | `execution_unusable`; no unauthorized authority/clean route |
| Fresh empty reviews with omitted resolution | Exactly three repair/review/resolution rounds, then `obligations_exhausted`; no final assessment |
| Gap after clean adjudication | Separate final assessor returned `semantic_gap` |

Recorded repair input is `{"directive":"open_d1"}`. Resolution input is `{"findings":"clean","outstanding":"open_d1"}`. The graph retains the obligation despite fresh empty findings; an explicit designated resolution routes directly without a Broodling copy/admit hop. The driver records/asserts results rather than validating or routing live node outcomes.

**Limits preserved:** the refusal evidence is runtime forced-refusal/force-stop terminalization, not an independently observed model refusal or recovered settled-refusal record. The applicability case injects a failure result; it proves routing of that result, **not** trusted current-source/evidence detection. That remains Q4. Controlled provider input is not Q5 context qualification. No semantic judgment reliability or arbitrary production obligation encoding is certified. Within the recorded mechanism, no Q3-specific unresolved dependency was demonstrated; the reviewed Q3 verdict remains PASS.

### Q4 — Trusted in-run source/evidence handoff: BLOCKED

**Expected:** trusted current source, Contract/evidence/predecessor context and required artifact availability bind authority inputs; stale/forged/unavailable context fails closed. The assessed source remains stable and dependent progression waits for the required trusted operation, even when the external observer is delayed. [T §§2.3, 7.5, 8; P P1 Q4]

**Reproduction/evidence:** [#5 reproduction][R5], [driver][D5], [machine record][E5] `q4.cases`; exact #5 controlled-leaf profile/build in §2.

**Actual:** the unchanged-context control was accepted. Changing candidate bytes after submission also accepted the stale starting digest: run `01a077de-8441-7ca3-905a-ce68ed23713c` retained starting digest `e7100a0d...` in authority input while observed candidate bytes hashed to `27d279a2...`. Forged source/Contract/evidence/predecessor values reached authority input and the accepting fixture route in run `01a077de-86b5-71f1-8e7d-c65354baa88c`. Run `01a077de-894a-7422-a981-1881bb985c7c` accepted although required `evidence/raw.txt` was absent. In the delayed-observer case, authority/run completion preceded the observer's completed current-byte digest.

These accepting fixture results are counterexamples, not valid Broodling SemanticAcceptance or Work Unit success. This diagnoses the selected handoff, not a universal claim that every possible Zeroshot graph accepts forged context. The fixture's typed claim propagation is not trusted applicability. A starting HEAD, processor-supplied identifiers, or an asynchronous observation callback does not supply the missing synchronous trusted dependency. The required stable assessed-source interval and artifact custody are not qualified.

**Missing dependency:** a supported trusted in-run source/evidence/predecessor handoff that synchronously gates dependent progression, together with explicit raw-artifact availability/custody. Owner #5; consumes G1-core and P3/G3. No API shape, manifest format, external scheduler, or workaround is selected here.

### Q5 — Automatic context and session isolation: BLOCKED

**Expected:** the actual reviewer profile automatically receives only the frozen Contract, exact source/comparison base, selected raw evidence and governed review instructions, with relevant session/instruction/skill/configuration/tool provenance controlled or explicitly admitted. Narrow JSON alone cannot establish this. [T §§7.3, 7.6; P P1 Q5]

**Reproduction/evidence:** [#5 reproduction][R5], [driver][D5], [machine record][E5] `q5`; real `codex-cli 0.153.4` / `gpt-5.6-sol` profile in §2. This reproduction requires the actual configured provider, not the controlled leaf.

**Actual:** real reviewer run `01a077de-8f17-76a1-90d1-56284264ceff` reproduced `Q5_REPOSITORY_INSTRUCTION_CANARY` and `Q5_AMBIENT_SKILL_CANARY` and reported `Q5_TOOL_SURFACE_CANARY`, despite their absence from the narrow graph-bound JSON. The untracked repository instructions and project skill were not identified by the starting HEAD. Its output reported the separately placed private-narrative canary as `NOT_VISIBLE`.

Execution-scoped session freshness is a positive property; the private-narrative result is a limited control, not proof of exhaustive isolation. The reported local composition uses current-user HOME/CODEX_HOME and does not establish exhaustive control/provenance over automatic project/user instructions, skills, configuration, sessions and tools. Read-only workspace access does not itself establish the semantic-input firewall. The witness does not claim a complete provider-context transcript or hostile-agent confinement.

**Missing dependency:** a supported selected-profile context-control/provenance contract that disables or exhaustively admits the relevant automatic context while preserving the governed reviewer boundary. Owner #5; consumes G1-core and P3/G3. No product session manager or new isolation architecture is introduced.

### Q6 — Stop/loss and semantic catch-up: BLOCKED

**Expected:** after stop or controller loss, recover all successful designated authority committed by durable terminalization, including after a Broodling-side crash before catch-up; incorporate the same occurrence exactly once before replacement/current-state-dependent use. No uncommitted post-terminal outcome becomes authority. The stop request itself is not the cutoff. [T §§4.3, 6.4; P P1 Q6]

**Reproduction/evidence:** [#3 reproduction][R3], [driver][D3], [machine record][E3] `Q1.stoppedRun` and `Q6`; exact #3 profile/build in §2.

**Actual:** force-stop while the second adjudicator was active produced `force_stopped` at `v2:36`. Killing the one-run controller while final assessment was active produced durable `runtime_lost` at `v2:46` after a new observer reopened the run. No later dispatch was observed. A separate child harness exited with code 86 after first-adjudicator routing reached repair in run `01a077aa-9bce-7fa1-b623-4d4f08c647b8`, before semantic catch-up; restarted observation/stop recovered terminal `force_stopped` at `v2:24`.

These are real terminalization and crash-window witnesses, not a passing catch-up contract. Successor routing corroborates earlier completed adjudication, but the public surface cannot return its exact trusted input, successful/error/voided outcome and completion identity/order. Consequently the directive cannot be recovered exactly once under that canonical occurrence into fixture current/replacement state. Post-terminal authority exclusion is not promoted from “no later dispatch observed” to an externally recovered complete authority inventory.

**Missing dependency:** the same Q1 external completed-occurrence recovery/retention contract, consumed here by terminal semantic catch-up. Owner #3; consumes G1-core and P4/G4. A log scraper, replacement result store, same-run takeover or second decision identity is not accepted.

## 4. Retention/custody and consolidated dependencies

The #3 report conditions public replay on the explicit `LocalTarget.state_dir` remaining preserved and readable by the pinned build. It identifies no broader retention SLA or cross-version migration guarantee. Preserving that directory is necessary for those observed replays, but does not make private settled data externally supported.

Committed reports and JSON witness records are review evidence. They are **not** a production canonical occurrence API, a second runtime-result ledger, or proof that every original runtime directory, wheel, sidecar binary and raw artifact remains available now. The review does not claim current possession of the original runtime state. Q1/Q6 still lack a supported exact-history/custody contract; Q4 still lacks the necessary raw-artifact handoff/custody contract.

| Unresolved integration contract | Questions / existing evidence owner | Consuming boundary |
|---|---|---|
| Supported exact settled-occurrence recovery, admitted graph/runtime relationship, completion ordering and retention/version guarantees, including failed/stopped runs | Q1 and Q6 / #3 | G1-core; therefore P2 is not cleared; P4/G4 catch-up/replacement cannot rely on a substitute |
| Trusted current source/evidence/predecessor input, stable applicability and synchronous handoff, with required artifact availability/custody | Q4 / #5 | G1-core; P3/G3 and dependent authority/effect applicability |
| Qualified actual reviewer automatic-context controls and provenance | Q5 / #5 | G1-core; P3/G3 and subsequent reviewer-context claims |

These are the discovered dependencies, not newly created implementation issues or a chosen remediation design. No production language/framework/storage, embedding decision, generic Broodling validator/router, runtime-result store, session manager, source manifest or effect executor is selected. Later semantic evaluation remains necessary even if these integration dependencies are eventually qualified.

## 5. Formal gate disposition and independent effects visibility

| Issue #7 gate criterion | Review result |
|---|---|
| All Q1–Q6 demonstrated through the chosen external integration, with controls and rejecting cases | **NOT MET:** Q1/Q4/Q5/Q6 BLOCKED; bounded Q2/Q3 PASS only |
| Supported version and retention/custody make completed-occurrence catch-up available, including failed/stopped runs and the crash window | **NOT MET:** Q1/Q6 history contract missing; preserving state or logs is insufficient |
| Trusted applicability, sticky obligations, routing, actual context, correlation and terminalization jointly preserve the target without concealed duplicate machinery | **NOT MET as a whole:** Q3 routing and Q2 correlation positives do not establish Q4/Q5 or complete Q6; the investigations did not replace the missing capability with Broodling execution machinery |
| PASS only when every witness passes; otherwise record verdict, missing capability, evidence owner and consumer | **MET by this record:** G1-core **BLOCKED**; owners and consumers above |
| Keep G1-effects independent and create no later-phase implementation issues | **MET by this review:** independent status below; no later-phase work or issue creation |

**G1-effects remains BLOCKED independently.** Issue #6's [report][R6], [retained evidence][E6], and [completion comment][I6] at Broodling `3a486f270f5fdc1f029d6f255c1ede62b9378f03` record Q7 BLOCKED. The reported gap is exact trusted-intent execution and same-run verified-result coordination without the stock commit/push/PR/merge bundle, plus post-run exact readback without a coding Attempt. This review carries that status forward; it does not requalify or resolve Q7. Issue #6 remains open and blocks P6 and every effect/publication-dependent Contract even if core later passes. Conversely, an effects pass could not override the present core blockers.

**Disposition of this task:** #7's evidence-review work is complete and may close with reason `completed`. The first three gate conditions remain unsatisfied; closing the review must not check them off as passed. Issues #3 and #5 remain open with their recorded blockers; #4's closed Q3 investigation does not pass G1-core; #6 remains open independently. No P2 or later phase is authorized or started, including the no-effect product slice. The documentation commit and issue closeout are user-authorized repository housekeeping, not evidence that Broodling's trusted-effect integration has been qualified.

## References

[T]: https://github.com/faviann/broodling/blob/5f017e39dfa6a84a4c4f27da90e70174e0817cd6/docs/baseline/inputs/broodling-target-responsibility-boundary-design-v0.3.md
[P]: https://github.com/faviann/broodling/blob/5f017e39dfa6a84a4c4f27da90e70174e0817cd6/docs/baseline/inputs/broodling-implementation-dependency-plan-v0.3.md
[G0]: https://github.com/faviann/broodling/blob/5f017e39dfa6a84a4c4f27da90e70174e0817cd6/docs/baseline/p0-g0-inventory.md
[H2]: https://github.com/faviann/broodling/blob/500fdb401edd83b36d791d02074920e607a5c98d/qualification/p1/README.md#reproduction
[E2]: https://github.com/faviann/broodling/blob/500fdb401edd83b36d791d02074920e607a5c98d/qualification/p1/evidence/run-record.json
[I3]: https://github.com/faviann/broodling/issues/3#issuecomment-5560772395
[I4]: https://github.com/faviann/broodling/issues/4#issuecomment-5560973993
[I5]: https://github.com/faviann/broodling/issues/5#issuecomment-5561099120
[R3]: https://github.com/faviann/broodling/blob/f144e15b15bd85cd788aa21a1c17d973914cb426/qualification/p1/issue-3-q1-q2-q6.md#reproduction
[D3]: https://github.com/faviann/broodling/blob/f144e15b15bd85cd788aa21a1c17d973914cb426/qualification/p1/issue3_qualify.py
[E3]: https://github.com/faviann/broodling/blob/f144e15b15bd85cd788aa21a1c17d973914cb426/qualification/p1/evidence/issue-3-run-record.json
[R4]: https://github.com/faviann/broodling/blob/0b1a3b24d25d1467057593ca614b09a0cbce9766/qualification/p1/issue-4-q3.md#reproduction
[D4]: https://github.com/faviann/broodling/blob/0b1a3b24d25d1467057593ca614b09a0cbce9766/qualification/p1/issue4_qualify.py
[E4]: https://github.com/faviann/broodling/blob/0b1a3b24d25d1467057593ca614b09a0cbce9766/qualification/p1/evidence/issue-4-run-record.json
[R5]: https://github.com/faviann/broodling/blob/03bf963eb7d3c59ffa52c4aee388f1470f444f73/qualification/p1/issue-5-q4-q5.md#reproduction
[D5]: https://github.com/faviann/broodling/blob/03bf963eb7d3c59ffa52c4aee388f1470f444f73/qualification/p1/issue5_qualify.py
[E5]: https://github.com/faviann/broodling/blob/03bf963eb7d3c59ffa52c4aee388f1470f444f73/qualification/p1/evidence/issue-5-run-record.json
[R6]: https://github.com/faviann/broodling/blob/3a486f270f5fdc1f029d6f255c1ede62b9378f03/qualification/p1/issue-6-q7.md
[E6]: https://github.com/faviann/broodling/blob/3a486f270f5fdc1f029d6f255c1ede62b9378f03/qualification/p1/evidence/issue-6-run-record.json
[I6]: https://github.com/faviann/broodling/issues/6#issuecomment-5561209556
