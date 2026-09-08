# G4-V1 — lifecycle and normal no-effect disposition

**Review status: COMPLETE. Gate verdict: PASS.**
Reviewed product commit: `2d7e666ee405365796a97bcf8d4a1b989e51d094`.
Product/test implementation bytes are unchanged since
`ce03c99d07ccd2ea9c1c8e4ac308e22fa0fa8d7f`.

This is a new, separate gate verdict under target v0.5 and the prospective
[v0.5 G4 evidence addendum](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/docs/governing/v0.5-g4-evidence-addendum.md).
It does not relabel any historical blocked attempt, diagnostic predicate or gate.
No product remediation, provider launch, test execution or P5 work occurred in
this gate. The evidence establishes the finite supported single-host no-effect
lifecycle/disposition integration only.

## Review method and prerequisites

A fresh skeptical read-only context, `/root/issue24_g4_skeptical_gate`, fetched
current #24 and reviewed the target, original plan plus addendum, G1/G2/G3,
current P2/P3/P4 source, raw provider/control evidence and SQLite custody. A
separate bounded read-only context, `issue24_g4_skeptical_gate/lifecycle`, audited
#21/#22 ownership, cessation, retirement, replacement and stale-call behavior.
Both found no blocking acceptance gap. The parent separately verified
publication, source/profile integrity, completed prerequisites and preservation;
see [parent audit](evidence/issue-24-parent-audit.json).

#11/G1, #16/G2, #20/G3 and #21/#22/#23 were independently verified
CLOSED/COMPLETED before this gate concluded. #23 was reviewed, published and
closed before #24 began. Closing #23 was not itself gate evidence.

Immutable reviewed sources and evidence:

- [Target v0.5](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/docs/governing/broodling-target-responsibility-boundary-design-v0.5.md), [original plan v0.5](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/docs/governing/broodling-implementation-dependency-plan-v0.5.md).
- [G1-V1](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p1/issue-11-g1-v1.md), [G2-V1](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p2/issue-16-g2-v1.md), [G3-V1](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p3/issue-20-g3-v1.md).
- [#21 abandonment](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/issue-21-abandonment.md), [#22 replacement](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/issue-22-replacement.md), [#23 completion audit](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/issue-23-completion.md).
- [#22 profile audit](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-22-parent-profile-audit.json), [#22 lifecycle controls](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-22-lifecycle.json).
- [#23 dedicated controls](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-23-controls.json), [verification manifest](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-23-verification.json), [composition audit](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/g4-evidence-correction/composition-audit.json).

## Obligation-by-obligation verdict

| Current #24 obligation | Expected / actual support | Verdict |
| --- | --- | --- |
| Abandonment/currentness | Durable irreversible ineligibility must precede external stop; history stays immutable and an empty slot cannot bypass old safety. `abandonment.py` commits that order, store/schema preserve authority, retry requires acknowledged retirement. #23 adds completed-disposition exclusion. | PASS |
| Supported cessation/containment | Physical provider/descendant cessation must hold through stop, loss and caller interruption; unknown safety blocks retirement/replacement. #21/#22 qualified records and source support that boundary. #22 loss run `01a08255-e27c-74e0-8901-f9e6c6833b88` retains a stable three-line heartbeat after controller death and further delay, no survivors before administration, continuing sibling work and unchanged shared Git. The intentionally uncontained negative grows three to seven lines. Terminal labels alone confer no proof. | PASS |
| Safe owned retirement | Only exact disposable ownership may retire after safety, including interrupted Git children and lost acknowledgments. Exact assignment checks, launch fencing and inherited enclosure lock hold through Git processes. SIGKILL/acknowledgment controls show convergence without deleting sibling/shared state or retained justification. | PASS |
| Fresh B1 replacement | Explicit new Attempt/worktree/branch/submission/run must preserve original immutable Contract/B1. #22 raw lifecycle controls and retry allocation retain those bindings, distinguish ingress replay and converge four concurrent identical retries. Missing B1, live drift and unsupported checkout transformations cannot silently substitute new starting material. | PASS |
| Zero carryover | Actual replacement bytes/bindings/context must exclude A1 semantic/session state while retaining original frozen authority. Fresh-input controls show original B1, empty HOME, auth-only CODEX_HOME and absent A1 canaries. Actual source-only instruction witness A1 `01a08252-14b7-7bb2-88f7-c001a594c682` to A2 `01a08252-a4e7-78a3-8ba8-5446c1de2cd0` shows original instructions reaching and affecting the actual implementer. Other roles/A1 in that witness are controlled. | PASS |
| Late results/stale requests | Old acknowledgments, authority/results/admin calls must not restore A1 or affect A2; ambiguous dispatch remains blocking. Correlation/currentness checks, #22 delayed-final/stale-call/ack controls and #23 delayed observation/custody cases discriminate this. Reconciliation uses existing identity without fresh coding merely to stop old work. | PASS |
| Normal final disposition | Fresh applicable P3 authority, complete current material/evidence/rationale and same admitted Contract with explicit effects `[]` are required. Fresh capture and bound graph/runtime/Contract/B1/instructions/run are preserved; transaction/SQL enforce current non-abandoned exact custody/run and array type/length zero. Missing material/rationale, semantic-gap, forged-identifier and prior P3 controls reject false success. | PASS |
| Crash/race/idempotency | One authority and justified result must survive abandonment, retirement, replacement, dispatch and finalization windows. #21/#22 controls plus 19 #23 scenarios include actual process death and concurrent finalizers. P2 still checks B1 before first dispatch, not after graph-authorized mutation. | PASS |
| Interrupted finalization | Restart must read an already committed complete disposition or abandon, including after complete custody before disposition. Actual SIGKILL `-9` in after-output, during-custody, after-custody and before-commit cases leaves no success; after-commit returns identical disposition without observation. Standalone P3 custody and waiting-finalizer owner death also cannot salvage success. Cleanup readback is exact. | PASS |
| Complementary real-provider evidence | All four prospective addendum legs are supported below: all-real normal completion; actual downstream semantic repair; controlled integrated negative/positive controls; explicit compatible provenance. No historical false predicate becomes an all-real claim. | PASS |
| Compatible reuse/scope | Current lifecycle/profile components match #22; inspected #23 fresh-capture/disposition/schema additions and current regression bridge earlier occurrence evidence. Strengthened W5 containment and shared-Git restriction were explicitly requalified. No effects, candidate sealing, cross-Attempt reuse, recovery, scheduler, session manager, second validator/router/runtime ledger or other deferred machinery appears. | PASS |
| Preservation and verdict | Original governing pair, historical G1/G2/G3/P4 records and false diagnostic predicates are unchanged. This separate record retains current expected/actual findings, identities, controlled/real distinctions and limits. Parent publication checks establish reachability. | PASS |

## Complementary witnesses and their limits

All-real natural runs
`01a082b0-a546-70b0-ae8f-4583ff954e57` and
`01a082b4-0521-7ee3-8be0-57fe476025a5` each contain actual implementation,
independent review, adjudication, designated clean final acceptance and their
own durable SUCCEEDED disposition. They remain clean-only.
[Raw run 1](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-23-natural-1.json),
[raw run 2](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-23-natural-2.json).

The [diagnostic run](https://github.com/faviann/broodling/blob/2d7e666ee405365796a97bcf8d4a1b989e51d094/qualification/v1-p4/evidence/issue-23-repair-diagnostic.json)
`01a082c0-c2c3-7d53-9629-c3c991a8f707` starts with a pre-admission B1 missing
normalized duplicate-header rejection. Source inspection confirms silent
`dict(zip(header, row))` overwrite and acceptance of duplicate header-only input.
Actual review identifies this exact Contract violation; actual adjudication
orders rejection before data processing; actual repair adds the two-line set-size
guard. Renewed required evidence and a distinct fresh review precede explicit
`resolved_d1` and repaired final acceptance. Independent retained-source audit
records three counterexamples before correction and all 12 focused cases passing
afterward. Raw resolution command logs support the additional checks it reports.

The two null repair log messages share one execution:
`nv2-e7546cda3e37d9625260d233b3d484a71dc11493950c5042ba42b58c3c4f5ac1`.
Final custody names that mutation and repaired final execution
`nv2-27648682d687524a6afd0a1d76789bda6a5f7d9d32ece7323a7c9549f42cedc5`.
This is one repair, not two independent repairs inferred from log count.

The diagnostic controls only initial `implement`. Its shim forwards downstream
arguments/prompts to the same actual CLI and inherits cwd/environment within the
existing containment. This extra wrapper is a disclosed test-adapter difference,
not a newly qualified executable/profile. `passed: false` and
`integratedRealRepairWitness: false` remain correct historical statements under
the former checklist. The inherited role-presence predicate proves no provider
provenance by itself. The diagnostic supports only its scoped leg of the new
composition; no fabricated downstream response or candidate tampering is credited.

Ten dedicated tests retain 19 controlled integration scenarios: repaired/forged
paths, gaps, five crash windows, lock contention, stop/commit orders, cleanup and
late A1 results. Controlled semantic roles establish mechanical discrimination;
they do not replace actual semantic judgments in the diagnostic leg.

The three semantic records contain byte-equal graph/runtime and compatible
SDK/provider/launcher/containment settings. Their retained databases pass
read-only integrity checks; each has one independent disposition and custody row.
Recorded post-cleanup readbacks match exactly, and diagnostic database custody
matches its JSON. No runtime semantic authority or history is composed between
Attempts. Different fixture B1, Attempt/run and disposable home identities remain
explicit, as does the implementer shim.

## Exact reviewed identities

| Component | Identity |
| --- | --- |
| Product | Broodling 0.1.0; reviewed `2d7e666ee405365796a97bcf8d4a1b989e51d094`; unchanged product/test baseline `ce03c99d07ccd2ea9c1c8e4ac308e22fa0fa8d7f` |
| Schema | Version 8; stored schema SHA-256 `0f1935c42baada28b56ad626bbd5c416118d98c9911fcf5949c58aa26127abf6` |
| Custody | `broodling.final-assurance/v1` |
| Graph | `a0d7abd7d03fb2a9c2099f1dc5f42315d9e5f76b48fe0bb35429bad305e41d26` |
| Runtime definition | `05fd5af801591caf55464b20f7cc86d98abb20a6b38adf9f45671300342abad0` |
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995` |
| SDK | 0.1.0.dev0; source SHA-256 `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e` |
| Sidecar | `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Provider | Codex CLI 0.153.4, OpenAI gpt-5.6-sol, low effort; `/home/faviann/.local/lib/node_modules/@openai/codex/bin/codex.js` |
| Diagnostic adapter | `qualification/v1-p4/issue23-diagnostic-bin/codex`; SHA-256 `cd73ec2ca6775a75a6a753c19e0094b41398f2d631fbfe527a78beae155e45be` |
| Launcher | `5e65d8e0509fbf55adb2c34b4297efc0afd94784f066cba3e08c4c8ed46d0624` |
| Containment | `linux-pidns-parent-death-v1`; `1cd4507567a66df727d8ddd42e8f947030d09df2edcf5358a4e40f2c776cae7e` |
| Source/store policy | `canonical-outside-slash-tmp-v1`; durable stores outside candidate and ambient /tmp; fail-closed checkout restrictions |
| Host | Linux 6.17.13-2-pve, x86-64; Python 3.13.5; SQLite 3.46.1; Git 2.47.3 |
| Evidence sandbox | Bubblewrap 0.12.0; `573236e5328ac2ebb08f59ae3a9805b4f8d12bdef14be8af4450d5463294985f` |
| Evidence leaf | `c878f7b8eb1e00f115879e78118edc65c323fe4605ffb9743d1b903d4022410c` |

Execution-scoped ephemeral sessions, ignored user configuration/rules, isolated
empty HOME, authentication-only CODEX_HOME, no extra writable roots, forced-off
sandbox network, workspace-write mutators and read-only assurance remain the
supported profile. Authentication required by the provider is not Git/GitHub
effect authority.

## Verification accounting and stop

The fresh reviewer independently verified 87 product/test manifest hashes,
94 dedicated-control source hashes, 32 diagnostic source hashes, 20 prior
evidence hashes and 11 composition evidence hashes. Earlier #22 evidence is not
claimed wholly hash-identical to current product: unchanged lifecycle/profile
components, inspected #23 changes and current regression establish compatibility.

Current SDK evidence is **419 passing regression tests plus a separate 10-test
disposition invocation**, covering all 429 tests. SDK-free evidence retains
373 passes, 56 skips and 2,429 subtests. No single 429-test invocation is claimed.
No new test/provider run was needed or performed in the gate. New record/link
publication passes `git diff --check`; the parent verifies origin/main before
closing #24 and stopping.

This PASS establishes finite single-host, exclusive-worktree, no-effect
lifecycle/disposition integration. It does not establish general semantic
reliability rates, distributed or hostile-host safety, effects readiness or
release qualification. Historical blocked records remain unchanged. No V1-P5 or
later work is created or begun. Stop after publishing the G4-V1 verdict.
