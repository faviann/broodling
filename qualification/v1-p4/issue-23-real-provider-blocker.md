# Issue #23 — real-provider repair witness remains blocked

Status: **BLOCKED — required integration evidence is missing**.
This is not G4-V1 review or a G4 verdict. Issue #24 has not begun.

## Established facts

Two finite actual-provider executions used the same staged immutable Contract
`cr-3f0cef9c15b3a259a204651bc2c33d3836fea7e4e96fe327e19d9c4f8a1a7eeb`.
The second execution changed no Contract, entitled instructions, candidate fixture,
graph, runtime, role prompt or model setting. Separate fixture commits have
different Git identities but the same admitted material digest.
Each execution independently admitted its fixture in a separate store; these
qualification runs do not exercise `RetryCoordinator` or cross-Attempt reuse.

| Observation | First execution | Unchanged repeat |
| --- | --- | --- |
| Public run | `01a0827c-fcd3-7db0-8deb-593344fb9e97` | `01a08282-626b-7f13-ac40-ff5b124c8aed` |
| Initial candidate change | Added wrapper and immediately changed `used <= limit` to `used < limit` | Same complete initial correction |
| Initial evidence | All three below/equal/above cases passed; checker unchanged | Same |
| Review/adjudication | Clean / no directive | Clean / no directive |
| Final authority | `final_assessment_authority_clean` after `implement` | Same |
| Durable disposition | `SUCCEEDED`, with P3 custody | `SUCCEEDED`, with P3 custody |
| Repair occurrence | None | None |
| Required repair witness | **Not satisfied** | **Not satisfied** |

Every model role that ran used actual Codex through the official pinned SDK and
sidecar. The deterministic evidence leaf remained the product leaf. No controlled
model output, synthetic directive or post-admission candidate write was inserted.
Both invocation/final source hash inventories were stable. Exact requests,
Contract/B1, occurrence identities, public observations and retained SQLite stores
are in [first execution](evidence/issue-23-real-provider.json) and
[repeat](evidence/issue-23-real-provider-repeat.json).
The [initial harness](evidence/issue-23-provider-harness-initial.py) and
[repeat harness](evidence/issue-23-provider-harness-repeat.py) snapshots were
reconstructed from the known edits and verified against each execution's exact
invocation/final SHA-256. They preserve the historical source, including its
documented deficiencies; they are not the corrected current harness.

The frozen Contract/source explicitly asked the initial phase to preserve the
legacy predicate pending adjudicated correction while requiring correct final
behavior. The implementer instead corrected it immediately. Public logs contain
the actual edit and passing check, but no explanation of that choice. Prioritizing
final correctness over the temporary staging instruction is an inference, not an
observed provider rationale. Clean completion cannot substitute for the issue's
required substantive directive, real repair and renewed assurance.

## Harness corrections and limits

The first fixture attempted `confirm_ceased` without first closing its launch
fence. That API correctly refused to establish cessation. A late product stop
also correctly refused to abandon a completed disposition. The harness now uses
fixture-only launch fencing followed by physical cessation confirmation before
controlled cleanup; it adds no product successful-worktree retirement policy.
The [separate cleanup record](evidence/issue-23-real-provider-fixture-cleanup.json)
proves safe cleanup and durable readback, preserving the original JSON/SQLite
bytes. The repeat completed this cleanup and readback normally.

Parent audit found that a harness check compared initial source to an absent
renewed source and mislabeled that inequality as a correction. The predicate now
requires a real repair occurrence and material on both sides. The [separate
read-only audit](evidence/issue-23-real-provider-repeat-audit.json) records the
correct negative result without rewriting the original observations. Neither
execution was ever an overall passing repair witness.

The inherited cheap store fixture also placed its SQLite file and adjacent
finalization lock under `/tmp`, a known writable root of the selected mutator
profile. No provider modification of those files was observed, but these runs
cannot establish their exclusion from provider-writable locations. The future
actual-provider harness now allocates its control store in a separate durable
directory outside the candidate and `/tmp`, consistent with the default durable
product-store placement. This is a fixture configuration correction; arbitrary
store-location protection is not newly implemented or claimed. No actual-provider
run has yet qualified that corrected fixture configuration.
The [placement preflight](evidence/issue-23-provider-placement-preflight.json)
checks only fixture initialization outside the candidate and ambient `/tmp`;
it launches no provider and supplies no repair or containment qualification.

## Unresolved work and bounded options

The outstanding question is how to construct a finite, Contract-consistent
actual-provider witness that demonstrably takes the real repair path on the
unchanged product. Another clean-only result cannot close this obligation.

One option is a different explicitly bounded task/fixture, retaining all real
model roles and the corrected durable control-store placement. The alternative
is to preserve this blocker. Neither option authorizes changed graph authority,
forced model responses, external candidate tampering, a prompt/model optimization
campaign, broad semantic evaluation or a waived repair requirement.

Independent deterministic disposition, crash/race, migration and current
regression verification can finish and be retained. They cannot fill this
actual-provider evidence gap. Issue #23 remains open; #24 and all later work
remain blocked.
