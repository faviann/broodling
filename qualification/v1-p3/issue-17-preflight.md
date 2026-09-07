# Issue #17 — qualified graph integration preflight

**Status: BLOCKED pending an explicit boundary decision.**
This is an issue-#17 investigation record, not completion of #17 or a G3-V1
review. No product implementation or successor issue was started.

## Reviewed authority and baseline

The current v0.5 [target](../../docs/governing/broodling-target-responsibility-boundary-design-v0.5.md)
and [plan](../../docs/governing/broodling-implementation-dependency-plan-v0.5.md)
govern, followed by the [G1-V1 PASS](../v1-p1/issue-11-g1-v1.md), current product,
[G2-V1 PASS from #16](../v1-p2/issue-16-g2-v1.md), and active
[issue #17](https://github.com/faviann/broodling/issues/17).

Reviewed product head: `88894861a0ae30f42c8e5295ec7995e16cbcc0f3`, reachable from
`origin/main`. Prior qualification records are unchanged. The parent independently
checked the installed integration using `assert_qualified_integration()` and ran
the existing product suite:

- Qualified SDK environment: **211 passed, 1775 subtests passed** in 83.38 seconds.
- Environment without SDK: **194 passed, 17 explicitly skipped, 1775 subtests
  passed** in 17.79 seconds.

Interpreter: Python 3.13.5. SDK: `zeroshot-rust 0.1.0.dev0`. Installed SDK source
SHA-256: `0263b63cb6c6991703f699919ea974ba502da23e3a14ab7d5ab8c5d5ac3b256e`.
Sidecar SHA-256: `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86`.
The inspected Zeroshot source is `d0909615d6ba3c179b58bce15a059f40400ec995`.

## Discriminating observations

[Machine evidence](evidence/issue-17-preflight.json) was produced by
[the evidence-only reproducer](issue17_preflight.py) through the official SDK
and matching sidecar. Every case uses the exact qualified W3 graph SHA-256
`f3ffcfced5bab598bc818db65ed985637afa0696a4ff551ee96ed5807788cd2b`.
The reproducer reads the unchanged qualification fixture as reference evidence;
it is not a production import or implementation of the product protocol.

| Case | Actual result | Implication |
|---|---|---|
| Clean positive control | Final clean assessor reached; runtime success at `c1` | The probe can execute the valid route. |
| Every repair returns `c2`; authority explicitly resolves the third repair | Three repairs change the fixture candidate to C2/C3/C4; final repaired assessor and result still receive `c2`; runtime success | The sole ordinal state does not enforce advancement. |
| `round_complete` crashes after eligible resolution | Final repaired assessor reached; runtime success | An unusable control occurrence does not fail closed. |
| `round_complete` omits required output | Final repaired assessor reached; runtime success | Missing required control output does not fail closed. |

The repeated-generation case changes only two controlled-leaf expressions,
recorded exactly in the JSON: the repair label and the authority's resolution
timing. Required evidence, fresh review and eligible resolution still execute
after **each** repair. This demonstrates an unenforced ordinal, **not** reuse of
stale assurance or a demonstrated stale-candidate acceptance bypass.

The control-failure cases use the existing fixture fault injection without graph
or leaf changes. The graph's `resolution_route` checks the resolution authority's
error before `round_complete`, but has no subsequent guard for the control's own
error. The loop's exit condition depends on the resolution signal.

The parent independently reran all four cases and reproduced the same outcomes:
clean `01a07e15-f5e7-78a3-9bad-ca52bc94bf94`, repeated-generation
`01a07e16-0be7-7430-8528-6702351a5605`, control-crash
`01a07e16-3da4-7fe2-ae04-b6d806bef7f8`, and control-missing
`01a07e16-583d-7323-a93f-6f97cf5cf83d`. The committed machine record is the
testing subagent's final rerun with the build assertions and source metadata.

These are deterministic graph-mechanics probes. They do not independently
requalify real-provider containment, no-effect enforcement, reviewer independence
or model judgment. Controlled leaves are not a real-provider product profile.

## Questions that the governing sources do not settle

1. **Canonical generation.** The qualified repair response permits any of
   `c2|c3|c4`, directly writing worker output to graph state. #17 requires every
   completed mutation to advance the canonical structural generation. The pinned
   public graph schema exposes state/item input selectors, node-channel writes
   and signal/error guards, but no increment or loop-index binding. Options
   include making graph position/runtime occurrence canonical and demoting worker
   labels, or changing the bounded encoding to enforce advancement. Those choices
   affect the qualified representation or topology and require an explicit
   decision; a Broodling-side counter/router is not an admissible substitute.
2. **Control failure.** #17 and target section 7.2 require unusable required
   occurrences to fail closed. Keeping the exact graph retains the observed
   bypass; adding explicit failure routing changes its qualified encoding.
3. **Semantic payloads.** The exact graph carries only `clean|found` review and
   `none|open_d1` adjudication signals, with null outputs and unbound diagnostics.
   Repair receives `open_d1` without actual directive content. Target section 7.1
   requires actual findings and applicable directives as role inputs. Narrow
   typed payload bindings could preserve control signals and authority roles,
   but would change the qualified types/bindings. The permitted extent of that
   substitution must be explicit rather than silently inferred.

The orchestrator surfaced these questions to the user before selecting an
interpretation. Product graph construction is blocked; #18, #19 and #20 remain
behind their strict dependency boundaries. No historical PASS is rewritten, no
new qualification PASS is claimed, and no G3 review is performed by this record.

## Reproduction

From the repository root, using the installed qualified SDK environment:

```sh
/home/faviann/.cache/broodling-zeroshot-venv/bin/python \
  qualification/v1-p3/issue17_preflight.py \
  --output /home/faviann/.cache/broodling-evidence/issue-17-preflight-rerun.json
```

The probe creates dedicated fixture repositories under the user's durable cache
and a short controller directory under `/dev/shm`. It retains public terminal
results and controlled-leaf inputs. It reads no private runtime history and
introduces no product observer, recovery, sealing, effects or P4 behavior.
