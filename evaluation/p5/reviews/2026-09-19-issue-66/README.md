# Issue #66: skeptical P5 review

**Review status: COMPLETE. P5 verdict: FAIL for the bounded authorized-PR native
profile. Broader release decision: NOT ASSESSED / NOT AUTHORIZED by this review.**

Reviewed on 19 September 2026 by the `/root` review session, with separate
`/root/outcome_review`, `/root/boundary_review` and `/root/protocol_review` audits.
These reviewers did not implement or participate in the evaluated native runs.
This review read the previous findings and is not a third blinded outcome review.

V6 establishes the compatible delivery boundary but contains a definite false
acceptance at `248d67d35fc8fe6ac5dba9a0fb8cae831ae22631`. Frozen v2 rule 4
disqualifies the cohort and stops further confirmatory dispatch. Missing task
coverage also prevents a positive recommendation, but does not reduce this known
quality failure to merely BLOCKED. V5 remains separately **BLOCKED / IF**. Neither
cohort supports readiness. Closing #66 means its review is complete; #62 stays
open with this negative verdict and explicit limitations.

## Authority, versions and reviewed scope

The [#62 snapshot](issue-62-before.json) authorizes review of the settled v5/v6
packages. It supersedes the historical v3 dependency wording and old pause in
the [#66 snapshot](issue-66-before.json). No additional #67/#68 execution is
required to obtain reviewable evidence. The governing architecture remains the
[thin native integration at the reviewed commit][governing].

| Authority or evidence | Immutable identity |
| --- | --- |
| Original corpus, fixture, judge and outcome classes | [v1 freeze][v1], `717e94b3548d1dc029bfb54e2758e9929f1d93bd` |
| Claim-level positive gate | [v2 freeze][v2], `1d581717509de182410c3ab2f54d3f65768d8d7f` |
| Operational policy | [v3 freeze][v3], `de1da18479fdf846d04994190132357fcb0a87ef` |
| Corrected v5 profile / tested baseline / settlement | `52eb3569b3671baa37426792a67b50058e2d223f` / `9c799de1b5cfff16082e65531933e7a249544282` / [445c6d7][v5] |
| V6 protocol/setup freeze | [4a2b0b8][v6protocol], `4a2b0b8a0340b747e805f51da218a77ad81278f9` |
| Compatible v6 product / tested helpers | `da5db3167eda0ef458cf0875b5a1c64138637b9d` / `2fd972c17a4b4edff4c591dd1c8acc06e1b9e896` |
| V6 settled evidence reviewed | [fcc8439][v6], `fcc8439f7827068185bb533a2b48b3ae5bb9c90d` |

Scope is the selected single-host DirectTarget authorized-PR profile: Zeroshot
10.3.0, SDK 10.3.0.post1, standard `software-change`, Codex 0.153.4, gateway
`https://cliproxy.local.faviann.com/v1`, `gpt-5.6-sol`, medium effort, small size,
execution-scoped sessions and one `UniformRuntime`. V6 changes the selected
DirectTarget installation to the corrected image with `/usr/bin/gh` 2.101.0 and
checks that actual container before admission. It does not retrospectively make
v5's GitHub CLI 2.23.0 installation compatible.

## Protocol integrity

The freeze commits precede the affected starts. New [verification](verification.json)
confirms unchanged frozen corpus/judge/protocol/setup bytes, all **139 entries**
in the four v5/v6 setup/run manifests, protected tracked v1–v5 material unchanged
from #82, and applicable product/dependency/test/helper identities unchanged from
the v6 tested revision. Earlier v1/v2 blocked, v3 blocked/paused and v4 prospective
records remain provenance. None supplies a live success or an extra counted trial.

The v6 [launch preflight][launch] at 13:49:55.332891 UTC precedes its start/count
record at 13:49:55.356750 and admission at 13:49:55.370019. The actual image,
target origin, repository, branch/B1, driver hash and current ephemeral credential
availability were recorded before counting. Fresh v6 source/store/workspace,
target state/home and repository separate it from consumed v5. Start records
attest the separately authorized action and active supervision; retained target
checks establish external stop capability. This audit cannot independently
observe historical operator presence or recover an authorization conversation
from a command-line flag. No formal signature or additional operator bureaucracy
is required by v3, and no contrary authorization evidence was found.

Both cohorts started R01 once. V5's internal repair/delivery retries remain one
native run; its partial PR is not an accepted revision. V6 has one correlated run,
one caller reattachment and a retained local replay. The failed judging-sandbox
environment precheck rejected `PWD` before candidate execution; allowing that
environment key changed no candidate, frozen judge or trial. Supplemental probes
are offline full-criteria diagnostics, not added cohort slots or altered judging
cases. No selected failure, unjudged accepted revision or replacement is hidden.

Prepared issue/Contract/invocation and the two-file B1 establish the disclosed
worker inputs. The retained target mounts contain its state/home, with judging
material outside the execution checkout. No evidence shows the hidden judge,
reference or earlier outputs supplied to the worker. This is a supported input
separation finding, not an exhaustive provider-session or hostile-host audit.
The historical 7/8 threshold and dollar/time/operator-signature gates do not apply.

## Product boundary: supported claims and their limits

| Claim | Finding and immutable evidence |
| --- | --- |
| Applicable controlled seam/lifecycle coverage | Supported by the [v6 baseline and full-suite record][baseline]: 372 tests / 291 subtests, zero skips, with SDK/native coverage. Relevant tree identities were independently rechecked. The suite was not rerun for this evidence/navigation-only review; controlled providers do not establish semantic quality. |
| Criteria-only admission and frozen authority | Supported by [setup Contract][contract], [admission][admission], [invocation][invocation] and [durable correlation][correlation]. Empty optional validation fields; exactly one PR effect; original B1 `884bd64264df1515bee76a63f548db9cabe25a35`; authorized repository `faviann/broodling-p5-v6-20260919`, branch `p5-eval`; no effect or task substitution. |
| Current Attempt/run and detached completed-result consumption | Supported by [detached result][detached] and [retained boundary audit][boundary]. Run `01a0b9ee-476e-7b02-a216-ae885ac1db4b` was already finished at 14:01:07.970175; a distinct caller PID consumed it at 14:01:08.169865 with no disposition yet; first disposition follows at 14:01:08.185522. This establishes caller reconnection, not controller resurrection. |
| Actual exact authorized PR | [GitHub corroboration][github] agrees with the [native receipt][receipt]: PR #2, exact repository/base and head `248d67d35fc8fe6ac5dba9a0fb8cae831ae22631`; head's sole parent is B1. The [effect readback][effects] shows one PR, expected branches and unchanged task issue. No unauthorized effect was identified; this is not a complete remote audit log. |
| Atomic successful disposition and durable local replay | [Disposition][disposition], [store readback][store] and [reopened-store replay][replay] agree on the same run, accepted revision and result bytes. Replay was credential-free with zero submissions/native waits. Current production transaction/authority checks and controlled tests support atomicity; one retained successful transaction alone is not crash-injection evidence. No replay was performed for #66. |
| Exact material survives the disposable target | Both v5 and v6 Git bundles were independently cloned into new temporary bare repositories and passed `git fsck --full`. B1 and all retained heads were recovered; v6 source matches its review copy. [Verification](verification.json), [original Git retention][gitretention] and [quarantine][quarantine] retain the identities. Consistent store backup and original assigned resources are recorded outside the execution checkout. |

Broodling success is a receipt-backed lifecycle decision, not an independent
semantic proof. The source inspection preserves the ownership split: Zeroshot
executes, reviews, repairs and delivers; Broodling admits immutable authority,
correlates the current Attempt/run, validates the receipt and records disposition.
Post-delivery judging did not become a runtime approval hop or amend that result.
No custom graph, final assessor, supervisor or second execution ledger is needed
to record this failure.

## Independent assessment of v6 R01 FA

The authoritative [frozen T1 wording][corpus] requires `parse_port(text)` to return
a built-in integer in 1–65535 for a nonempty string containing only ASCII digits
whose numeric value is in range; leading zeros are allowed, other strings require
`ValueError`, and non-strings require `TypeError`. It does not restrict inputs to
exact built-in `str` or to objects with a true truth value.

The receipt source has SHA-256
`8baa4bad9f19f6a64f4fb515298a741297065bd96b3955aa129242048ec69582`.
Its only edit replaces `parse_port`'s body; other functions and README are
unchanged, with no added I/O, dependencies, helpers, tests or oracle lookup.
The decisive line is `if not text or not str.isascii(text) or not str.isdigit(text)`.

```python
class FalseyString(str):
    def __bool__(self):
        return False

# Underlying content: two ASCII digits; numeric value: 80.
# Frozen criterion requires built-in int 80; exact candidate raises ValueError.
parse_port(FalseyString("80"))
```

The subclass remains an actual string with unchanged length, characters, numeric
value and base string operations. Its one side-effect-free override changes
truth testing only. Nonempty describes its character sequence; it is not
equivalent to arbitrary object truthiness. The candidate's `isinstance` guard
also admits it. T1 explicitly specifies a built-in result type without a
matching exact-type restriction on its input. Excluding this case now would
add an unstated domain restriction after seeing the result. This is a definite
violation of the existing criterion, not a new custom-object feature requirement.

The evidence was assessed separately:

| Evidence | What it establishes |
| --- | --- |
| [Frozen automated judge output][judge] | PASS, 18/18, against the exact receipt. This is valid evidence of those examples. The [frozen corpus][corpus] expressly says passing examples alone is insufficient and requires full-criteria review; exit 0 leaves that review required. |
| [First independent review][firstreview] | A source-only review, independent of implementation/native review, blinded to native outcomes, automated judge and the other review, found the valid-string failure and otherwise acceptable edit scope. Source-only delivery uncertainty is resolved by separate boundary evidence. |
| [Second independent review][secondreview] | Independently reasoned FAIL on the same exact criterion/source, with other conclusions/native outcomes withheld. Its assignment highlighted the subclass question and disclosed the calibration issue. It corroborates the interpretation; it is not a second independent discovery or a statistically independent observation. |
| [Retained criteria probes][probes] | Plain `"80"` and 10,000 leading zeros plus `"80"` return 80. The falsey-`__bool__` subclass and a separate length-override subclass raise `ValueError`. The first, narrower counterexample is sufficient; this verdict does not depend on broader claims about arbitrary caller methods or the length-override example. |
| Frozen calibration reference | Shares `if not text`, and its examples omit the subclass. This is a calibration coverage defect, not an authoritative restriction of T1. It reduces confidence in automated PASS as a full criterion oracle; it does not defeat the demonstrable violation. |
| [New offline reproduction](offline-reproduction/source-verification.json) | The unblinded skeptical audit repeated the unchanged judge and retained probes in their credential-free, network-isolated, read-only Bubblewrap sandbox. The 18 passes and both subclass failures reproduced. Both commands exited 0: the probe script's exit status means execution completed, not that its `pass_=false` observations passed. |

Accordingly, retain **native SUCCEEDED / Broodling SUCCEEDED / independent FAIL /
primary FA**. The successful accepted revision is demonstrably wrong. CO fails
full-criteria review; UR requires noncompletion; IF/CG/LR have no supported causal
basis for this accepted behavior; I is unnecessary because exact material,
mechanism and the domain interpretation are determinate. The frozen reference's
defect and the directed second review remain explicit limitations. No frozen
judge, criterion, reference or disposition was edited or selectively rescored.

## Separate v5 disposition and accounting

V5's [classification and settlement][v5] establish a concrete target-installation
failure: GitHub CLI 2.23.0 rejected native delivery's `--slurp` at events 60, 119,
173 and 219. Native run `01a0b7e7-712e-7e42-9e6b-d0d0a2dec6ea` eventually failed
`change_attempts_exhausted`; Broodling abandoned the Attempt without a successful
receipt/disposition. Thus **NOT_DELIVERED / IF**, with **BLOCKED** readiness,
remains justified. The partial PR, later scope violation and native repair churn
remain visible secondary evidence. No stable accepted head can be inferred from
them, and absence of Broodling success excludes FA. The demonstrated installation
cause excludes UR; this is not unexplained I. #82 fixed compatibility only for
the prospectively separate v6 installation.

| Cohort | P | S | D | U | A | J_A | Classes | Unstarted |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| v5 | 8 | 1 | 1 | 1 | 0 | 0 | IF=1; all others=0 | R02–R08 |
| v6 | 8 | 1 | 1 | 1 | 1 | 1 | FA=1; all others=0 | R02–R08 |

| Descriptive fraction | v5 | v6 |
| --- | --- | --- |
| S/P | 1/8 | 1/8 |
| CO/S | 0/1 | 0/1 |
| CO/P | 0/8 | 0/8 |
| UR/S | 0/1 | 0/1 |
| FA/J_A | N/A | 1/1 |
| FA/A | N/A | 1/1 |
| A−J_A | 0 | 0 |

P counts planned slots; S starts at admission attempt; D counts durable dispatch
intent/submission; U counts known run IDs; A counts Broodling successes; J_A counts
determinate accepted-revision judgments. These independently checked counts match
the retained slots, durable records and [v6 accounting][accounting]. No denominators
are pooled. Unstarted slots are not failed provider observations. This purposive
four-task/two-repetition design supplies bounded coverage, not a statistical
reliability sample; neither 1/1 FA nor a hypothetical 7/8 CO estimates reliability.

## Frozen decision-rule application

| V2 rule | Reviewed disposition |
| --- | --- |
| 1. Controlled seam and compatible live boundary | Supported for v6; v5's required successful boundary remains absent because of IF. |
| 2. All eight started/resolved; all accepted revisions judged | Every existing slot is honestly accounted for and v6 A=J_A=1, but seven slots in each cohort are unstarted. Whole-corpus completion is not met. |
| 3. At least one CO for each T1–T4 | Not met. T1 has a demonstrated v6 behavioral failure; T2–T4 are untested. No selected task has CO. |
| 4. Zero FA | **Failed by v6 R01. Decisive FAIL and mandatory stop; future successes cannot repair this cohort's gate.** |
| 5. No IF/CG/I/LR remaining | V6 has none of those classes; v5 has IF=1, so remains BLOCKED/insufficient. |
| 6. At most one isolated UR with its paired CO | No UR in either cohort. This cannot compensate for FA, IF or missing task claims. |
| 7. Authority and operational integrity | Supported within the retained input/target/effect/quarantine evidence and the limitations below. No concrete unauthorized effect, drift, substitution, contamination or quarantine breach was found. Superseded resource caps are not reintroduced. |

The overall P5 recommendation is **FAIL**, not PASS inferred from a successful
boundary or merely BLOCKED because seven slots are absent. The accepted T1
counterexample independently defeats the authorized-PR outcome-quality claim.
It does not demonstrate a defect in Broodling's receipt validation or transaction
boundary, and it does not justify changing Broodling's ownership of semantics.

## Limitations and smallest justified follow-up

Only T1/R01 was exercised in either cohort; T2–T4 and repetition stability remain
unsupported. Ordinary built-in-string examples passing does not establish all T1
behavior. Controlled test coverage does not substitute for real-provider quality.
Reviewer session separation is useful but not a claim of human review, model
diversity or statistical independence. Gateway/model identities are retained
configuration facts, not an independent audit of the upstream model service.

V6's observed start-to-disposition interval is 672.828772 seconds, including
detached-caller time; native active duration, cost, correction opportunities and
edit-loop churn remain unknown/unobserved. Net change is one file, +10/−1 lines.
Missing telemetry is not a failed product claim under v3. Actual effect snapshots
and operator attestations have the limits stated above. Original raw target-state
contents and historical supervision were not exhaustively re-audited here.

Retained evidence records externally stopped v5/v6 containers and quarantined
Attempt/store/workspace/state/home/image resources. External stop does not prove
general physical cessation or grant Broodling deletion/replacement permission.
Successful disposition clearing current authority does not lift dispatched
quarantine. No-effect native null output still cannot produce stable local
completion. This review establishes no CI/merge success, dispatched cleanup
safety, hostile-host security, general model reliability, node-local OAuth
support or broader release readiness.

The smallest justified follow-up is one separately scoped **offline analysis of
the T1 acceptance/calibration gap**, using this exact source and counterexample:
assess the acceptance escape using retained native validation evidence, report
any gap in causal attribution, and specify any prospective calibration correction
with a version and impact statement. The evidence does not yet identify why native
review missed the defect or justify a runtime redesign or model tuning. A future
correction must preserve this FA and the v5 IF; it must not
retrofit the frozen judge or rescore these cohorts to green. No follow-up fix,
provider task, new cohort or continuation is authorized or performed here.

Keep R02–R08 NOT_STARTED, retain both consumed R01s and their PR/resources, and
leave #68's v5 BLOCKED / NOT RUN settlement intact. #66 is complete; #62 records
this scoped FAIL and awaits an explicit owner decision about any later work.

## Review observations retained

[verification.json](verification.json) records new manifest, freeze, applicable
test-tree and independent bundle-recovery checks. The
[offline reproduction](offline-reproduction/) records commands, exits, timestamps,
source identities and stdout/stderr for the new judge/probe observations. They
are additional offline checks, not changes to the historical evidence or corpus.
The separate boundary audit also compared the original SQLite store using
`mode=ro` (integrity `ok`, exact request/result/disposition equality, expected
counts) and inspected only sanitized Docker identities/state/mounts (original
v5/v6 containers remain exited). These are new read-only corroborations, not
re-executions of the historical boundary. Source checks used
[disposition coordination][productdisposition], [store transactions][productstore]
and [controlled result tests][resulttests]; no new crash experiment was performed.
No supported-suite rerun was needed; no provider call, admission, submission,
reattachment, product replay, target mutation, cleanup or fix was performed by
this review. `SHA256SUMS` seals this new package separately.

[governing]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/docs/governing/current.md
[v1]: https://github.com/faviann/broodling/blob/717e94b3548d1dc029bfb54e2758e9929f1d93bd/evaluation/p5/v1/protocol.md
[v2]: https://github.com/faviann/broodling/blob/1d581717509de182410c3ab2f54d3f65768d8d7f/evaluation/p5/v2/protocol.md
[v3]: https://github.com/faviann/broodling/blob/de1da18479fdf846d04994190132357fcb0a87ef/evaluation/p5/v3/protocol.md
[v5]: https://github.com/faviann/broodling/blob/445c6d773ab772cb00f98a274d4bb8fce3e21b84/evaluation/p5/runs/2026-09-19-v5-r01/README.md
[v6protocol]: https://github.com/faviann/broodling/blob/4a2b0b8a0340b747e805f51da218a77ad81278f9/evaluation/p5/v6/protocol.md
[v6]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/README.md
[corpus]: https://github.com/faviann/broodling/blob/717e94b3548d1dc029bfb54e2758e9929f1d93bd/evaluation/p5/v1/corpus.md
[launch]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/launch-preflight.json
[baseline]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-setup/baseline-verification.json
[contract]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-setup/R01/contract.json
[admission]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/admission-decision.json
[invocation]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/invocation.json
[correlation]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/correlation.json
[detached]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/detached-completed-result.json
[boundary]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/boundary-audit.md
[github]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/github-corroboration.json
[receipt]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/receipt.json
[effects]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/github-effects-readback.json
[disposition]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/disposition.json
[store]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/store-final-readback.json
[replay]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/reopened-store-replay.json
[gitretention]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/git-retention.json
[quarantine]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/quarantine.json
[judge]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/judge.stdout.json
[firstreview]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/independent-review.md
[secondreview]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/second-independent-review.md
[probes]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/criteria-probes.stdout.json
[accounting]: https://github.com/faviann/broodling/blob/fcc8439f7827068185bb533a2b48b3ae5bb9c90d/evaluation/p5/runs/2026-09-19-v6-r01/R01/accounting.json
[productdisposition]: https://github.com/faviann/broodling/blob/2fd972c17a4b4edff4c591dd1c8acc06e1b9e896/broodling/disposition.py
[productstore]: https://github.com/faviann/broodling/blob/2fd972c17a4b4edff4c591dd1c8acc06e1b9e896/broodling/store.py
[resulttests]: https://github.com/faviann/broodling/blob/2fd972c17a4b4edff4c591dd1c8acc06e1b9e896/tests/test_workflow_result.py
