# Issue #19 — current final assessment and complete custody

**Implementation review: COMPLETE. Issue acceptance: PASS.** This successor resolves
#19's material-selection blocker. It is not G3 PASS or Work Unit disposition.

## Authority and authorized decision

The current [v0.5 target](../../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md)
and [plan](../../docs/governing/broodling-implementation-dependency-plan-v0.5.md),
[G1](../v1-p1/issue-11-g1-v1.md), current product, [G2](../v1-p2/issue-16-g2-v1.md)
and completed [#17](issue-17-assurance-graph.md)/[#18](issue-18-evidence-review.md)
govern this implementation. #18 landed at
`b29701f1d19152d403e900939cd654339fe8198d`; the independent #19 observation/storage
foundation landed at `a56bcd4c0e900a33bc5b1abfcbf40808f3513b10`.

The user authorized the [previously unresolved selection](issue-19-material-selection.md):
small explicit final-assurance material declarations in a **new admitted Contract
revision**, naming exact repository-relative final-candidate/B1 selections, exercised
through a **fresh Attempt**. Existing Attempt bindings must not be amended. Selection
must remain custody-only and retain exact applicable bytes or explicit absence,
without automatic discovery, model authority, whole-worktree archiving or candidate
identity/provenance. This implementation follows that decision. The historical
blocker, W3/W4/W6/G1/G2 and prior issue evidence remain unchanged.

## What landed

The [implementation guide](../../docs/implementation/v1-p3-final-assurance.md)
describes `FinalAssuranceMaterial`, literal final/B1 collection,
`FinalAssuranceCoordinator.capture`, the live public adapter seam and immutable
Attempt-scoped schema-4 row. Existing canonical Contracts omit the new optional
field. A changed declaration creates a new revision requiring admission; a current
Attempt cannot be rebound or superseded by this feature.

The [exact #19 graph deviation](evidence/issue-19-graph-deviation.json) is only typed criterion-level `finalRationale` initialization,
required output/binding on the two designated final leaves, their instructions and
root export. Graph topology, route guards, authority roles, repair bound, runtime,
reviewer/session/sandbox profile and the #18 deterministic evidence leaf are
unchanged. The authorized custody declaration/collector adds no graph topology.
The graph's stable final interval and live runtime node/execution references establish
structural currentness; collected bytes never do.

Capture verifies the immutable admitted request and current correlated Attempt,
observes only the public current run, requires complete raw evidence/context and
criterion rationale, and atomically retains declared final/B1 bytes/absence.
It allows graph-authorized source changes after dispatch. A missed normal result
cannot be salvaged from completed history. There is no partial-custody lifecycle,
second execution ledger, disposition flag or worktree lifecycle operation.

## Acceptance evidence

[Actual SDK capture evidence](evidence/issue-19-final-custody.json) retains **13 controls
from seven tests**, all PASS, using the exact current product graph/runtime and
actual deterministic evidence leaf. All controls keep exactly one Attempt and one
submission. The [test log](evidence/issue-19-final-custody-tests.txt) and
[reproduction harness](issue19_capture.py) identify the exact source bytes.

| Obligation | Discriminating evidence |
| --- | --- |
| Designated current final authority on clean route | `clean`: implement → evidence → review/adjudication → exact clean final occurrence → one complete row. |
| Repaired structural generation and renewed assurance | `repair`: directive → repair → fresh evidence/review → eligible resolution → exact repaired final occurrence. Retained evidence equals the actual final input; generation references the repair execution. |
| Forged identifiers do not retarget authority or custody | `forged-identifiers`: admitted Contract/B1/run and graph-selected occurrence remain authoritative; forged diagnostics and raw rejected-finding canary do not enter custody. |
| Exact complete material survives disposable state | Positive cases retain binary final source, B1 deletion bytes, final/B1 explicit absence and unchanged governing source with exact CRLF, in addition to candidate/check material and criterion rationale. `cleanup-reread` proves immutable reread after test-only worktree/native-state deletion. Product performs no deletion. |
| New meaning requires new revision and fresh Attempt | `new-declared-revision-fresh-attempt` admits the declaration before provisioning its first fresh Attempt; old revision bytes stay unchanged. `old-undeclared-attempt-refused` leaves the existing Attempt/revision intact and no custody. `test_new_declaration_cannot_rebind_an_existing_current_attempt` rejects competing provisioning. No retirement/replacement is implemented. |
| Required custody missing despite native success | `empty-rationale`, `nonregular-material`, `unreadable-material` have native success but zero custody. SDK-free multicriterion tests reject missing/duplicate/foreign/blank rationale and absent raw/context/artifact material. Empty raw text remains valid. |
| Missing initial or renewed raw material | `missing-initial`, `missing-renewed` leave zero custody. Existing public controls also reject ordinary authority lookalikes, missing/default final output, missing rationale, semantic gap and persistent open obligation. |
| Lost normal observation cannot catch up | `cancellation`, `observer-loss` interrupt after actual current observation before commit: native success, zero custody, subsequent terminal observation refused. Public tests reject initially completed runs; adapter unit tests reject terminal-only IDs, wrong run, missing/mismatched final/latest mutation, postfinal mutation and loss. |
| Current binding, P2 identity and atomic minimal storage | Completeness tests reject foreign run/nonproduct request before storage; P2 regression and schema tests cover immutable correlation, exact migration, and insert/update/delete/replace protection. Only one `final_assurance` row is added; no history/cursor/status tables or second run. |
| Exact declared collection without inferred selection | `test_final_material.py`: binary/empty/absence, state selection, modes/symlinks, pinned B1 despite HEAD change and path validation. Independent adversarial probes cover literal Git-magic/leading-dash/newline/Unicode/backslash/space names and ancestor symlink swap refusal. |

Full validation is retained in [SDK regression suite](evidence/issue-19-suite.txt)
(293 tests and 2045 subtests passed; no skips),
[SDK-free suite](evidence/issue-19-without-sdk.txt) and
[wheel verification](evidence/issue-19-packaging.txt). The SDK-free suite has
260 passes, 33 SDK-dependent skips and 1964 subtests. Changed Python files pass
Ruff; `git diff --check` passes. The wheel contains byte-identical custody modules
and executable launcher and excludes tests/qualification data.

Parent independently inspected the coordinator, literal collection, old revision
compatibility, evidence bindings and source identities. A fresh issue-scoped
adversarial reviewer found no material issue. This implementation acceptance is
separate from the required subsequent fresh skeptical #20 gate.

## Exact configuration and limits

- Product graph SHA-256: `6619b045f12637e82f0127033c71851eb92718df530e99e99cb16ad0accbb034`.
- Runtime SHA-256: `05fd5af801591caf55464b20f7cc86d98abb20a6b38adf9f45671300342abad0`.
- Hash encoding: UTF-8 `canonical_request()` (sorted keys, compact JSON, no NaN).
- Zeroshot source: `d0909615d6ba3c179b58bce15a059f40400ec995`; SDK `0.1.0.dev0` source SHA-256 `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e`; sidecar SHA-256 `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.
- Profile: `g1-v1-codex-w4`, Codex `0.153.4`, `gpt-5.6-sol`/low, execution-scoped, read-only assurance, controlled isolated/ephemeral reviewer settings, network off.
- Launcher SHA-256: `a9ba410c2ae51d1fc7a0483254753749a70991ae919648278d80631dfece643e`.
- Evidence leaf SHA-256: `c878f7b8eb1e00f115879e78118edc65c323fe4605ffb9743d1b903d4022410c`.
- Bubblewrap SHA-256: `573236e5328ac2ebb08f59ae3a9805b4f8d12bdef14be8af4450d5463294985f`.
- Controlled model fixture SHA-256: `ccbb21db61be7263589aa47f06f756ccf6855b033ab8cb356446a72128a5e830`.

The model-role executable is a controlled fixture; its version response matches
the profile but is not an actual model-quality witness. SDK/sidecar, admission,
worktree, public stream, graph routing and deterministic evidence execution are
real. Fixture synchronization releases implementation after the adapter reads its
actual initial public status; no history catch-up supplies the observation.
#18's real reviewer controls remain the bounded model/profile witness.

Final selected material supports binary regular files and symlink target bytes;
raw mechanical check material remains #18's exact UTF-8 profile. Unsupported file
types or missing selection declarations fail closed. Collection relies on the
already-qualified stable final interval, not a general hostile-host race protocol.
This work does not implement completed-run recovery, candidate sealing/provenance,
effects/GitDelivery, a scheduler/router/response validator/session manager,
Work Unit disposition or V1-P4 abandonment/restart/retirement/replacement.
