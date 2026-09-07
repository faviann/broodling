# Issue #17 — product assurance graph and corrected W3 mechanics

**Issue #17 implementation verification: PASS.**
This record covers #17 only. It does not record G3-V1 or complete #18/#19.
The governing v0.5 target/plan, historical G1 evidence and #16 G2-V1 PASS remain
unchanged. The [preflight record](issue-17-preflight.md) preserves the discovered
limitations of the historical W3 graph.

## Boundary decision and implementation

The user explicitly resolved the three preflight questions before implementation:
generation is structural, the required control must fail closed, and narrow typed
finding/directive bindings are authorized. Historical W3's exact hash remains
evidence rather than an artifact that overrides governing requirements.

The [implementation description](../../docs/implementation/v1-p3-assurance-graph.md)
documents the complete deviation. The product graph removes all worker ordinal
bindings; successful mutation occurrences establish candidate intervals. It
retains the same executable roles and three-repair loop, adding a loop error exit
and post-loop error guard for `round_complete`. Review content reaches designated
adjudication; the resulting directive/correction content survives mutation and
clean reviews without any worker/reviewer write to authoritative obligation state.
Resolution explicitly changes the canonical obligation signal and leaves the
directive content intact.

`prepare_assurance`/`submit_assurance` derive the frozen Contract and protocol
internally, and use the existing P2 request, ownership and run-correlation
boundary. Caller graph/runtime/initial-state replacement is unavailable on this
path. No schema change or runtime-history projection is introduced.

The launcher materializes the existing qualified profile settings rather than
changing them. All agent bindings remain execution-scoped; only implement/repair
are workspace-write, and all assurance/control occurrences are read-only.
Provider timeouts are finite 300,000 ms, one attempt per node. The test timeout
substitutions are recorded separately.

## Exact configuration

| Component | Identity |
|---|---|
| Historical W3 graph | `f3ffcfced5bab598bc818db65ed985637afa0696a4ff551ee96ed5807788cd2b` |
| Product graph | `e42e4f1c71d2c1fe2053acbeabf5df2f3fc314ae56ea58f91a379b7af8747fb2` |
| Product runtime | `2372abf66db4aa1704b73b723db9c56047e74f79e6901ed480338b4853f6b6d0` |
| Zeroshot source | `d0909615d6ba3c179b58bce15a059f40400ec995` |
| SDK | `zeroshot-rust 0.1.0.dev0` |
| SDK source SHA-256 | `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e` |
| Sidecar SHA-256 | `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Product provider selection | Codex CLI 0.153.4, OpenAI `gpt-5.6-sol`, low effort |
| Host | Linux single host, Python 3.13.5, one current Attempt/exclusive durable worktree |
| Effects | Empty required-effect set; no GitDelivery |

Graph/runtime hashes use canonical sorted compact JSON. They identify the
submitted software definition, not candidate applicability.

## Product controls

The [retained machine record](evidence/issue-17-controls.json) contains 38
mechanical SDK cases, two admitted product runs, 15 passing checks, exact
definitions, runtime IDs, controlled-leaf inputs and code hashes. Reproduce with
[issue17_controls.py](issue17_controls.py) using the qualified SDK environment.

| #17 obligation | Discriminating evidence |
|---|---|
| Product-owned definition and immutable submission | Admitted clean and repair runs use exact product graph/runtime hashes; override/collision tests reject arbitrary requests and initialized state. |
| Structural freshness | Repeated `c2` narrative claims accompany C1 through C4 fixture material; every repair traverses renewed evidence/review/resolution before final. No ordinal exists in state or bindings. |
| Sticky semantic obligation | Three clean renewed reviews cannot remove the original directive/correction content or `open_d1`; only designated resolution changes the signal. |
| Repair firewall | Exact repair input is Contract + open signal + adjudicated content; the deliberately widened binding exposes the raw-finding canary and proves detector sensitivity. |
| Finite bound | Persistent obligation fails as `obligations_exhausted` after exactly three repairs, before final assessment. |
| Required-control correction | Crash, missing, malformed, default and timeout control cases fail before final; an error with the obligation still open stops after one repair. |
| Other unusable required executions | Crashes at all roles, missing typed semantic payloads, refusal and malformed/default cases route non-success. |
| Evidence/semantic gap | Missing initial/renewed material fails before dependent review; final gap/refusal cannot become acceptance. Real Contract evidence integration remains #18. |
| Distinct final authority | Clean adjudication only reaches a separate final assessor; ordinary implement/review/repair authority-lookalikes fail validation. |
| Non-authoritative diagnostics | Forged identifiers/contradictory narrative do not retarget the frozen Contract, clear the obligation or bypass repair. |
| Qualified profile | Actual admitted product runs execute the bundled launcher and observe read-only assurance, execution freshness flags, isolated homes and forced-off network. |
| P2 compatibility | Admission/currentness tests remain; product replay after candidate mutation and provider runtime-file creation preserves the same request/run. Invalid initial profiles and changed launcher identity fail before dispatch. |

The direct mechanical controls substitute controlled model/connection bindings;
their executable records sandbox selections but does not itself enforce host
containment. Two timeout cases also shorten only the selected timeout; the
widened-binding sensitivity case is deliberately not the product graph. Those
differences are explicitly recorded, not presented as a single exact-byte run.
The two admitted product cases use the **unchanged product graph, runtime and
launcher** with a controlled underlying provider executable. They demonstrate
that the mechanics and profile wiring compose on the actual P2 admission path.

These tests establish mechanics and configuration, not a fresh real-model
semantic-quality or W2 containment qualification. The role kinds, sandbox/session
settings and no-effect controls remain the qualified ones. #18 retains its own
required real-reviewer context and legitimate-material controls.

## Verification and stop boundary

The parent independently reviewed the actual code and evidence, verified retained
source and graph/runtime hashes, and ran the final full regression suite:

- [Qualified SDK suite](evidence/issue-17-tests.txt): **235 passed, 1843 subtests
  passed** in 294.89 seconds.
- [Without SDK](evidence/issue-17-without-sdk.txt): **214 passed, 21 explicitly
  skipped, 1796 subtests passed** in 15.91 seconds.
- Ruff checks passed for changed product modules, product tests, launcher and
  implementation-control runner.

A fresh independent reviewer reported **no correctness or scope findings** and
retained [45 additional public SDK probes](evidence/issue-17-adversarial.json),
all passing on the exact product graph. These cover missing/malformed/default
responses at all eleven nodes, genuinely absent response envelopes and absent
agent messages at mutators/final/control, plus explicit typed-null mutator
positive controls. Every fault was observed at its intended node and stopped
there, preventing an earlier fixture failure from masquerading as a pass.
The [review reproducer](issue17_adversarial.py) is retained byte-for-byte with its
source hash; run it from the repository root using the qualified interpreter.
Its output goes to `/dev/shm/issue17_adversarial_results.json`.

The wheel build was checked to retain the exact launcher bytes and executable
permission. The store schema, governing documents, G1 and G2 evidence remain
unchanged. The new product code contains no completed-run recovery/catch-up,
candidate seal/provenance service, observer/router/validator/session manager,
effects, abandonment/restart, Work Unit disposition or #18/#19 data machinery.
