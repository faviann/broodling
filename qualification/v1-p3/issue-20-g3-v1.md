# Issue #20 — G3-V1 integrated assurance gate

**Review status: COMPLETE.**

**G3-V1 gate verdict: PASS.**

**Reviewed product:** `d94eaa6aed176c49aeea0b441c31e44c2136013b` on `origin/main`.

Every current G3 obligation is supported as one compatible V1-P3 product
configuration. No unresolved blocker was found. This gate establishes the bounded
assurance integration, not Work Unit disposition, semantic reliability or release
readiness. Review performed no product remediation and creates no V1-P4 work.

## Authority, dependency and review method

The review followed the [v0.5 target](https://github.com/faviann/broodling/blob/6a866c5304415e2c03b09f442312adfbccac3c68/docs/governing/broodling-target-responsibility-boundary-design-v0.5.md),
then [v0.5 plan](https://github.com/faviann/broodling/blob/6a866c5304415e2c03b09f442312adfbccac3c68/docs/governing/broodling-implementation-dependency-plan-v0.5.md),
[G1-V1](https://github.com/faviann/broodling/blob/eebbff192d99797638799f1a624e681dc89bb686/qualification/v1-p1/issue-11-g1-v1.md),
current implementation, [G2-V1 from #16](https://github.com/faviann/broodling/blob/88894861a0ae30f42c8e5295ec7995e16cbcc0f3/qualification/v1-p2/issue-16-g2-v1.md),
and the active [#20 obligations](https://github.com/faviann/broodling/issues/20).
The qualified basis includes [W3](https://github.com/faviann/broodling/blob/6c5ab629d5f45a38e3913d706257cfc79644f3cc/qualification/v1-p1/issue-9-w3.md),
[W4/W6](https://github.com/faviann/broodling/blob/259a64aa7c06602b6f0fd49fe6af064097211046/qualification/v1-p1/issue-10-w4-w6.md)
and the unchanged issue-#8 host/no-effect evidence consumed by G1.

The strictly ordered implementation chain is complete and reachable:

- [#17](https://github.com/faviann/broodling/blob/25c496df9ed33715d3cbbff18d77f07a581ba565/qualification/v1-p3/issue-17-assurance-graph.md): product graph and authorized bounded corrections.
- [#18](https://github.com/faviann/broodling/blob/b29701f1d19152d403e900939cd654339fe8198d/qualification/v1-p3/issue-18-evidence-review.md): Contract-declared deterministic evidence and real reviewer controls.
- [#19](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/qualification/v1-p3/issue-19-final-assurance.md): current final authority and complete declared custody, including its previously committed observation/storage foundation.

A fresh read-only snapshot of the reviewed commit was used for three independent,
issue-scoped skeptical reviews: graph/authority mechanics, evidence/reviewer/profile
compatibility, and final custody/P2/stop boundaries. Parent Astra independently
read the governing obligations, inspected implementation and evidence, reran bounded
custody/observer/storage/material checks, and owns this verdict. Subagent judgments
were supporting review inputs rather than delegated completion authority.

The [integrity audit](evidence/issue-20-integrity.json) verifies all 180 snapshot files
against the reviewed Git tree, all nine capture-source identities, and all 13
capture requests against the current graph/runtime. Governing and historical G1/G2
records are unchanged. #17–#19 are closed; their completion comments accurately
identify landed behavior, evidence and limits. The repository was clean at entry.

## Compatible configuration and bounded deviations

Historical qualification graphs remain evidence. The user explicitly authorized
#17 to remove worker-supplied generation ordinals from routing state, add the
small native error/required-output correction for `round_complete`, and bind actual
review findings/adjudicated directive content. Those corrections preserve the short
W3 role topology, graph order and repair bound; historical limitations are recorded
without changing historical W3/G1 verdicts.

The user selected #18's small trusted graph-local deterministic check: existing
Zeroshot evidence agent occurrences dispatch the Broodling leaf. Explicit literal
argv/cwd/material declarations are frozen with a newly admitted Contract revision;
free-form validation/population text is never executable syntax. Actual read-only,
no-network observations replace model evidence-availability judgment, without
changing native lifecycle, routes or semantic authority. Affected leaf containment
and real reviewer controls were rerun and retained by #18.

The [#19 graph delta](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/qualification/v1-p3/evidence/issue-19-graph-deviation.json)
adds only typed final rationale state/initialization/export and designated final
leaf instructions/output. Direct comparison of both retained #18 real-reviewer
requests against the current graph finds all nine other executable nodes identical,
including both evidence nodes and both reviewers. Runtime, reviewer instructions,
launcher, profile and deterministic leaf are unchanged. Therefore the actual #18
reviewer/containment evidence supports the current configuration; no new real-model
run on #19 is claimed.

The user's #19 selection decision is custody-only: exact final/B1 paths belong to
a new admitted Contract revision and fresh Attempt. Existing Attempt/Contract
bindings remain immutable. No automatic source discovery, candidate identity,
provenance authority, whole-worktree archive or P4 replacement was introduced.

## Obligation-by-obligation verdict

All rows are **PASS** for the configuration below. Implementation/test links point
to the reviewed immutable commit; issue records above link retained raw controls.

| G3 obligation | Current implementation and discriminating evidence |
| --- | --- |
| Qualified protocol reuse | [Product graph](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/broodling/assurance_graph.py) preserves W3's short sequence and finite three-repair loop. Authorized deviations and exact #18/#19 graph deltas are explicit; relevant role/runtime/profile equality is verified above. |
| Graph-authorized mutation only | Only implement and repair use mutating step mode; all evidence, review, authority and control nodes are read-only. G1 host containment transfers on the unchanged qualified model profile; [leaf containment tests](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/tests/test_mechanical_evidence.py) cover its explicit sandbox substitution. |
| Structural candidate freshness | Every successful mutation necessarily routes through renewed evidence and fresh review; repaired acceptance additionally requires eligible resolution. [Current graph controls](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/tests/assurance_support.py) exercise repeated worker “c2” claims across three repairs, with no ordinal authority. [Current final controls](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/tests/test_final_assurance_public.py) verify changed C2 material and renewed evidence. |
| Sticky adjudicated obligations | Only designated authority writes the canonical obligation/directive. Repair and clean/empty review cannot discharge it. Current graph and evidence controls retain the same directive through three clean repair reviews until explicit resolution or exhaustion. |
| Bounded repair | Native `maxIterations=3`, one attempt per leaf, explicit unresolved exhaustion and post-control guard; persistent obligation performs exactly three repairs and never starts final assessment. |
| Graph-local evidence | [Evidence integration controls](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/tests/evidence_support.py) use actual frozen check declarations/leaf. Missing initial and renewed material fail before dependent review; valid material proceeds. |
| Evidence sufficiency | The same controls keep availability valid while independently varying population, host, mode, artifact, contradiction and criterion sufficiency; all six reach `semantic_gap`. Controlled semantic roles inspect actual bound observations. Availability is not acceptance or applicability. |
| Raw-finding/repair isolation | Repair has exactly Contract, obligation signal and adjudicated directive/correction content. Integrated repair excludes raw evidence/findings/private narrative. A deliberately widened binding exposes the canary, proving sensitivity of the firewall check. |
| Authority isolation | Ordinary diagnostics have no trusted state binding. Lookalike outputs, worker resolution claims and forged Contract/source/evidence/predecessor/generation/occurrence identifiers cannot select authority or retarget frozen inputs. Actual findings reach adjudication, while only designated authority signals route. |
| Fail-closed unusable execution | Current native graph controls cover crashes at required roles, timeout/refusal, missing/malformed/default outputs and payload gaps. Error guards precede continuation; failed `round_complete` with open obligation stops after one repair. Missing/default final output cannot return current final authority. |
| Controlled independent reviewer | [Actual #18 reviewer record](https://github.com/faviann/broodling/blob/b29701f1d19152d403e900939cd654339fe8198d/qualification/v1-p3/evidence/issue-18-integrated-reviewer.json): two actual SDK/Codex occurrences, ten checks. Clean sees all four legitimate source/B1/raw/governing canaries and no forbidden narrative; deliberate contamination sees six forbidden canaries. Runtime/bindings/profile transfer to current graph was checked directly. |
| Final semantic assessment distinct | Clean review/adjudication only enables its separate designated final assessor. `gap`/`refused` and inadequate criterion evidence fail. Typed rationale comes only from final leaves; native success with empty rationale cannot produce completed custody. |
| Structurally current final authority | [Public observer](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/broodling/zeroshot_sdk.py) requires exact admitted graph/runtime, current nonterminal status, live final node/execution after the last observed mutator, and no later mutation. It uses only public current status and forward watch/result; no model ID or digest establishes currentness. Clean/repaired captures select their respective designated final occurrence. |
| Minimal complete custody | [Coordinator](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/broodling/assurance.py) atomically retains frozen Contract reference, Attempt/run, final/latest-mutation references, exact declared final/B1 bytes or absence, raw evidence/context and criterion rationale. [Capture controls](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/tests/test_final_assurance_capture.py) prove binary/deleted/unchanged material, incomplete-material refusal, immutable row and durable reread after test-only cleanup. Completeness checks require material presence/coverage; they do not re-judge semantic truth. |
| P2/G2 compatibility | Coordinator rechecks immutable current Attempt/request/source/run bindings before observation and commit. Graph-authorized post-dispatch mutation is allowed; first dispatch still requires B1. All 13 capture controls retain exactly one Attempt/submission; full current suite includes P2 identity/replay/crash tests. New declarations never rebind existing Attempts. |
| No deferred machinery | [Observer rejection tests](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/tests/test_current_run_observation.py) refuse terminal-only/missed/foreign/ambiguous provenance and stream loss. Loss/cancellation after actual final observation but before commit leaves zero custody; a late call refuses the completed run. Schema has one immutable final row, no per-node/status/log/usage/cursor mirror; no private runtime storage, seal, effects/GitDelivery, scheduler/router/session manager or generic response validator. |
| P4 boundary preserved | No Work Unit success/disposition flag or abandonment, stop/retirement/restart/replacement API is added. Rereading already-retained custody uses no runtime recovery. Test-only fixture deletion proves durability and is not product worktree retirement. |
| Separate gate record | This file records review status separately from verdict, immutable references, exact versions/profile, limitations and the absence of blockers. The gate commit changes only review records and README status. |
| PASS only as one compatible configuration | Current full suite regenerates current product graph/runtime for #17 mechanics, #18 evidence and #19 final controls. All 13 retained #19 capture requests match it. Unchanged reviewer/evidence nodes/runtime justify bounded real-reviewer evidence transfer. No incompatible alternate product configuration is used to manufacture PASS. |

## Integrated scenarios and checks

The exact [13-case SDK custody record](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/qualification/v1-p3/evidence/issue-19-final-custody.json)
traces the clean route through implement/evidence/review/adjudication/clean-final to
one complete row, and the repaired route through C1 finding/directive, C2 repair,
renewed evidence/review, explicit resolution and repaired final to one complete row.
Retained final evidence equals the actual final input and generation references the
last runtime mutation execution. Forged diagnostics do not alter these bindings.

Blocking routes are exercised by the current full suite and retained issue controls:
missing initial/renewed evidence, six evidence-context/sufficiency failures, open
obligation at bound, unusable required outputs, ordinary authority lookalikes,
missing final rationale/material and lost normal observation. Native success with
empty rationale, unreadable/nonregular selected material or precommit observer loss
still leaves no complete custody. Clean/contaminated/valid-material real-reviewer
controls distinguish isolation from blindness on the compatible profile.

Validation supporting this review:

- [Full current pinned-SDK suite](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/qualification/v1-p3/evidence/issue-19-suite.txt): **293 tests, 2045 subtests passed, no skips**. The gate did not repeat the full suite.
- [SDK-free suite](https://github.com/faviann/broodling/blob/d94eaa6aed176c49aeea0b441c31e44c2136013b/qualification/v1-p3/evidence/issue-19-without-sdk.txt): 260 tests, 1964 subtests passed; 33 SDK-dependent skips explicit.
- Fresh [read-only gate probes](evidence/issue-20-custody-probes.txt): **42 tests, 1848 subtests passed**, covering current observation, storage, completeness, literal material and package/storage boundaries. Ran from the immutable snapshot with bytecode/cache writes disabled; fixture state was external.
- Current #19 qualification: seven tests/13 controls PASS. #18 retains 12 SDK evidence cases/14 checks and two real reviewer occurrences/10 checks. Historical #17 extra adversarial probes remain historical controls, not falsely claimed fresh gate runs.
- [Integrity audit](evidence/issue-20-integrity.json) independently verifies snapshot/source/build identities. Pre-gate changed-file Ruff and wheel/source/mode checks passed; review made no product changes requiring a remediation rerun.

## Exact reviewed software and profile

| Component | Identity |
| --- | --- |
| Product | `broodling 0.1.0`, commit `d94eaa6aed176c49aeea0b441c31e44c2136013b`, schema 4 |
| Graph SHA-256 | `6619b045f12637e82f0127033c71851eb92718df530e99e99cb16ad0accbb034` |
| Runtime SHA-256 | `05fd5af801591caf55464b20f7cc86d98abb20a6b38adf9f45671300342abad0` |
| Encoding | UTF-8 `canonical_request()`: sorted compact JSON, no NaN |
| Graph bounds | Eleven executable nodes; timeout 300,000 ms each; one attempt; three repairs maximum |
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995` |
| SDK | `0.1.0.dev0`; source SHA-256 `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e` |
| Sidecar SHA-256 | `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Provider | Codex `0.153.4`, `gpt-5.6-sol`, low effort; profile `g1-v1-codex-w4` |
| Reviewer/session profile | Fresh execution-scoped, read-only, ephemeral, ignored user config/rules, empty isolated HOME, authentication-only initial CODEX_HOME, network off, no extra writable candidate roots |
| Product launcher SHA-256 | `a9ba410c2ae51d1fc7a0483254753749a70991ae919648278d80631dfece643e` |
| Deterministic leaf SHA-256 | `c878f7b8eb1e00f115879e78118edc65c323fe4605ffb9743d1b903d4022410c` |
| Evidence sandbox | Bubblewrap `0.12.0`, SHA-256 `573236e5328ac2ebb08f59ae3a9805b4f8d12bdef14be8af4450d5463294985f`; read-only root/worktree, isolated PID/network, private scratch, cleared environment; trusted collector outside check PID namespace |
| Host tools | Linux `6.17.13-2-pve` x86-64; Git `2.47.3`; Python `3.13.5`; SQLite `3.46.1` |

## Limits and stop boundary

The qualification is single-host, exclusive Attempt worktree/exclusive authorized
writer, no-effect V1. It is not hostile-host/distributed confinement or a general
confidentiality boundary. Software/evidence hashes identify reviewed artifacts;
they are not product candidate seals or applicability checks.

Controlled model-role substitutes prove graph mechanics and evidence discrimination,
not broad model judgment reliability. Actual reviewer evidence is two #18 occurrences
on the verified unchanged reviewer configuration, not a new real-provider run here.
Negative sensitivity controls deliberately shorten selected timeouts or widen a
binding and are labeled test substitutions, not alternate passing product profiles.

Mechanical raw material supports exact UTF-8 text and ordinary exit codes 0–127;
unsupported binary raw check material/abnormal 128+ execution fails closed. Final
custody separately supports selected binary file/symlink bytes and absence. No
selection semantics are guessed for older revisions or unsupported file types.
Observer loss before durable custody remains incomplete; there is no completed-run
salvage, semantic catch-up, failed/stopped occurrence scan or cross-Attempt reuse.

**G3-V1 PASS clears this gate only.** Review stops here. No V1-P4 or later issue was
created or begun, and no abandonment/restart, safe cessation, worktree retirement,
replacement or Work Unit disposition behavior was implemented in this gate.
