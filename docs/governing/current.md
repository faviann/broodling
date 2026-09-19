# Current architecture and next work

**Status: current governing architecture and plan.** Start here for new work,
including P5. This records the native-integration decisions already implemented
on main, checked at
[525902050c256f10232bd74ff717cfb59224e985](https://github.com/faviann/broodling/tree/525902050c256f10232bd74ff717cfb59224e985)
for issue #60. It does not redesign the product or grant a qualification verdict.

## Documentation authority

This document governs current scope and next work. The
[thin native integration design](../implementation/zeroshot-native-integration.md)
is its detailed implementation reference. Production code and the
[current tests](../../tests/README.md) establish implemented behavior; investigate
a discrepancy rather than silently restoring an older requirement.

The [source audit](../implementation/zeroshot-current-source-audit.md) and
[result-handoff audit](../implementation/zeroshot-result-handoff-audit.md) are dated
supporting research, not competing plans. The investigation progressed from a
no-delivery native workflow to an explicitly authorized PR receipt as the only
currently supported stable handoff. The integration design records the adopted
decision. Upstream capabilities in an audit are not automatically Broodling support.

All other documents in this governing directory are historical, including
v0.4/v0.5, the G0-v0.4 review and G4 addendum. The V1-P2/P3/P4 implementation
notes and PR #48 ownership audit describe earlier revisions. Their requirements,
phase dependencies, passes and blockers do not govern this integration. Status
notices preserve the original bodies; exact original file bytes remain in Git.

The [baseline](../baseline/p0-g0-inventory.md) and
[qualification archive](../../qualification/README.md) retain their recorded
source/profile/evidence/verdict meaning. Neither an old G1–G4 pass nor an old
blocked gate is relabeled for the native release. The qualification README is
mutable navigation, not evidence. Root and test READMEs are current usage guidance.

## Ownership today

Broodling handles one software-change Work Unit, identified by one repository
and primary issue. It is not a backlog selector, scheduler, dependency waiter
or multi-project orchestrator.

| Broodling owns | Zeroshot owns |
| --- | --- |
| Entitled source snapshots, immutable Contract admission, criteria and exact effect authorization | Implementing and validating the frozen task through the standard software-change workflow |
| One current Attempt, original B1 and exclusive ownership of its dedicated local worktree | Native graph expansion/routing, independent acceptance/code review, repair and provider sessions |
| Frozen invocation, durable dispatch intent, Attempt/run correlation and current-authority checks | Submission-key idempotency, execution state, reconnectable terminal result and native stop |
| Receipt validation against authorized delivery and atomic stable-result/disposition retention | Authorized native checkout, commit, push and PR creation/update, including delivery repair and receipt production |
| Explicit local execution policy and refusal of unsafe cleanup/retry | Provider execution and process cleanup; Broodling does not add a supervisor |

B1 is the original admitted Git commit plus entitled instruction snapshots, not
today's branch tip. A source-attributed Contract can be admitted with acceptance
criteria alone: evidence population, validation seam/action and falsifying
observation are optional guidance. Other admission checks still apply.
Unsupported effects/obligations, effect-dependent evidence, unsatisfied
prerequisites and legacy selected-final-material requests are refused, not waived.
Supplied Contract material stays immutable. Repository guidance may be execution
context but cannot amend stored admission authority.

Source: [admission](../../broodling/closability.py),
[delivery selection](../../broodling/delivery.py),
[submission](../../broodling/submission.py),
[SDK integration](../../broodling/zeroshot_sdk.py),
[disposition](../../broodling/disposition.py) and
[abandonment](../../broodling/abandonment.py).

## Supported outcomes and limits

The administrative profile is single-host Linux x86-64, Python 3.13+, SQLite
3.37+ and Git, with Zeroshot 10.3.0 / Python SDK 10.3.0.post1 pinned in
[pyproject.toml](../../pyproject.toml). Local execution uses the documented
Codex 0.153.4 profile. This is a selected baseline, not a floating latest-release
claim or a transfer of old host qualification.

The deliberately simple V1 authorized-PR execution profile is Zeroshot's standard
`software-change` workflow with native `pull_request` delivery through the
already-supported DirectTarget path. One `UniformRuntime` applies across the
workflow: Codex harness, `gateway` provider through CLIProxyAPI at exactly
`https://cliproxy.local.faviann.com/`, `gpt-5.6-sol`, medium reasoning effort,
small size, and execution-scoped sessions. The no-effect LocalTarget retains its
existing Codex/OpenAI profile. V1 has no caller-selectable harness,
provider, model, effort, per-node runtime, skill/tool capability profile, or fleet
placement. Node-local OAuth authentication and those configuration/orchestration
features are post-V1 concerns.

| Frozen delivery authority | Current execution and Broodling outcome |
| --- | --- |
| Empty required-effect set | Standard workflow, delivery=none, LocalTarget. Native success returns null. Broodling refuses successful disposition without a stable accepted local result; no PR or post-run snapshot is inferred. |
| Exactly one pull_request effect with a target branch, for a GitHub Work Unit | Standard workflow, delivery=pull_request, configured DirectTarget with frozen repository/branch/B1. A matching successful v1/pr/opened receipt supplies the non-B1 headRevision; the whole receipt and disposition commit atomically for the still-current Attempt. |
| Other, mixed, multiple or underspecified effect authority | Refusal. No merge, standalone push, issue mutation, deployment or generic effect executor is supported. |

PR delivery includes native commit/push/open-or-update, not a branch-only effect.
An opened PR does **not** promise passing CI or a merge. Its commit OID is a stable
identity, not guaranteed permanent remote object retention. The DirectTarget
operator owns its execution checkout, provider installation, and sandbox policy;
Broodling passes the current provider and delivery credentials ephemerally at PR
dispatch because the supported DirectTarget has no connection store. The local
launcher does not constrain that target. This explicit trust boundary is not
Broodling multi-host orchestration. The local no-effect
profile requires the documented trusted-host precondition excluding
operator-managed effect-capable MCP/extensions.

PR dispatch requires explicit current `GATEWAY_BASE_URL`, `GATEWAY_API_KEY` and
`GH_TOKEN` inputs, passed only in the SDK environment for a new or idempotently
replayed dispatch. The gateway URL must match the selected endpoint exactly;
missing/empty gateway inputs, a different URL, or conflicting legacy provider
credentials in the dispatch environment fail closed. No `OPENAI_API_KEY` is sent.
Credential values and gateway inputs are absent from frozen requests/runtime
plans and credential values remain absent from logs and evidence. The gateway
endpoint is distinct from the persisted Zeroshot DirectTarget origin.

After lost submission acknowledgement, only the identical frozen invocation may
reconcile while authority remains current. After correlation, waiting again uses
the persisted run locator, including for an already-completed result, without
reconstructing execution or requiring today's dispatch credentials/profile.
Detached waits are not abandonment. Native failure abandons; explicit abandonment
prevents late success. Result retention and disposition are one atomic write.

**Every dispatched Attempt remains ineligible for automatic deletion or
replacement**, even after native success or stop. Terminal labels are not physical
cessation. A safely retired never-dispatched/never-materialized Attempt can be
explicitly replaced from original B1; interrupted provisioning can still block
retirement. There is no operator override granting cleanup authority. Completed
historical retirements remain facts; unfinished historical proofs cannot authorize
new cleanup. Legacy storage/type names do not restore the old assurance protocol.

## Next phase: P5 native-workflow product evaluation

**P5 protocol/setup work is in progress; no live provider trial has started and P5
has not passed.** Establish how well the existing thin integration delivers an
authorized task and assess native-workflow outcome quality. Do not inherit v0.5's
adjudicator/final-assessor checklist or mandatory new API.

The current prospective live profile is
[`p5-native-pr-v5`](../../evaluation/p5/v5/protocol.md). It preserves the frozen
v1 corpus/judge, v2 claim-level decision rules, and v3 operational policy while
changing only v4's provider/credential binding to the gateway profile above.
Protocols v1-v4 and all prior evidence remain byte-for-byte unchanged provenance;
none is rescored or reinterpreted as gateway evidence. R01-R08 remain NOT_STARTED.

The [issue #72 source audit](../implementation/zeroshot-node-local-authorized-pr-audit.md)
remains the source-backed dependency finding for a future node-local OAuth
profile. Its pinned Zeroshot limitation is not a blocker for V1's supported
DirectTarget/API-key path.

### Prerequisites

Use this authority, one recorded compatible product/dependency baseline and a
supported full-suite result. Record v5's first-addition commit as its freeze
identity. The selected profile is V1: configured DirectTarget, exact
`GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/`, current `GATEWAY_API_KEY`,
current GitHub delivery credential, and the fixed uniform runtime above.
A live trial still needs a designated disposable GitHub
repository/target branch and the exact authorized PR effect.
Each trial still needs the entitled source/primary issue/Work Unit/Contract/B1/
Attempt/workspace facts required by the current Broodling APIs.

Operational setup should be automated where practical. Evaluation tooling may
create or verify the disposable fixture, task records, target configuration and
evidence locations; that automation does not create new Broodling product
requirements. A setup failure before a slot's first admission may be corrected and
retried without consuming the preregistered trial. Once admission starts, the
frozen campaign accounting and no-rerun rules apply.

These first real-provider P5 runs have **no P5-specific dollar or wall-time
ceiling**. The corpus itself remains bounded at eight serial trials. The owner
actively supervises execution and can stop/contain the target externally. Record
observed cost and latency where available, but do not require budget enforcement,
billing-lag accounting, stop margins, operator signatures or campaign deadlines as
conditions for the current MVP evaluation.

Preserve the target/operator trust boundary without turning it into a product
feature. Record the actual target/runtime facts needed to identify the evaluated
profile, keep frozen judge/reference material out of worker inputs, independently
judge exact accepted revisions, retain enough evidence outside disposable
workspaces to reproduce those judgments, and preserve enough capacity for the
known quarantined Attempts. A formal sandbox inventory, network-disabled judging
host, separately assigned reviewer, archive SLA or detailed teardown runbook is
not intrinsically required unless a concrete evaluation/safety concern makes it
material.

Choose a small task set, independent judging checks and claim/acceptance criteria
before evaluation runs. For P5 this choice is already frozen as the bounded v1/v2
corpus and v2 claim-level gate. These are evaluation controls, not mandatory
admission proof plans. No historical G1–G4 rerun or #3/#5/#6 pass is a prerequisite.

### Work and evidence

1. **Establish the supported vertical slice.** Use existing Python entrypoints
   from criteria-only admission through native submission, durable correlation,
   reconnectable result and receipt-backed PR disposition. Include an actual
   authorized PR handoff on the configured target. Reuse current deterministic
   tests for conflicts/acknowledgement loss, invalid receipts, currentness,
   detached waits, atomicity, abandonment and undispatched replacement rather
   than reimplementing native execution tests.
2. **Assess task outcomes independently.** Include valid changes and meaningful
   failure opportunities such as incomplete/wrong-scope implementations, missed
   acceptance behavior or attempts to reinterpret frozen authority. Compare the
   exact accepted headRevision with original criteria using independent checks
   and review. Report correct outcomes, false acceptance and unjustified
   rejection/noncompletion separately from infrastructure/capability gaps.
   Observe correction effectiveness, churn and cost/latency where available;
   do not force an internal repair trace or add a live semantic-blessing hop.
3. **Retain a bounded, honest result.** Record source/B1/Contract, dependency and
   target/provider configuration, run IDs, receipts, Broodling outcomes, judging
   evidence and fixture substitutions, without credentials. State observed counts
   and limitations, not broad reliability claims from a tiny sample. Separate
   controlled seam evidence from live delivery and real-provider quality.

The exit deliverable is a reproducible evaluation record and evidence-backed
readiness recommendation for the **authorized-PR native profile**, explicit
failures/blocked claims and narrowly justified follow-up. A green unit suite
alone is not P5 success; defining this plan grants no release verdict.

### Capability gaps and non-goals

The no-effect stable-local-result gap blocks successful local disposition, not
P5 definition. Zeroshot 10.3.0's separate node-local authorized-PR gap remains a
post-V1 dependency finding, not a blocker for the selected DirectTarget profile.
Preserve both limitations; do not call either successful local completion.
Dispatched cessation/cleanup is a live operational limitation for both delivery
modes under current Broodling policy. Preserve quarantine and assess its
operational consequences; a supervisor or automatic retry is not a hidden P5
prerequisite. Closing any gap requires a separately justified supported
capability and explicit scope change.

Zeroshot owns workflow internals, reviewer sessions, validation choices and repair
routing. P5 does not restore custom graphs, deterministic evidence leaves, sticky
adjudicated directives, separate final assessors, occurrence-history catch-up,
candidate seals, supervision or a second execution ledger. Broader effects, merge,
long-term Git-object custody, new CLI/service packaging and model/reviewer tuning
are not mandatory P5 deliverables. Findings may motivate separate work; phase
numbering alone does not authorize it.

### P5 planning provenance

Version 1 remains frozen at `717e94b3548d1dc029bfb54e2758e9929f1d93bd`.
Version 2 remains frozen at `1d581717509de182410c3ab2f54d3f65768d8d7f`.
The blocked #64/#65 preflight/not-run record at
`b9cacd99beb9a297966e68e6047c7663aca9b4c6` remains evidence that the supported
suite passed there but R01-R08 were all NOT_STARTED and no live provider/PR
P5 observation occurred. V3 remains frozen and unexecuted. The profile-selection
pause recorded by #71 and the source-backed #72 finding remain preserved. V4's
OpenAI/`OPENAI_API_KEY` selection is historical prospective authority, superseded
only for future dispatch by v5's gateway binding. This provider amendment leaves
every earlier protocol and evidence package unchanged and consumes no slot.

The current order is **#81 → record the v5 freeze and compatible baseline →
separately authorize #79's one R01 gateway execution → #68 → #66**. The current
#62/#79 issue dependency text follows this order; #79's former OpenAI/v4 wording
and comments cannot authorize gateway execution. #68 remains gated
on compatible successful R01 boundary evidence, and #66 may review a settled
incomplete package. Implementation, controlled tests, documentation, setup and
endpoint preflight do not consume R01. Issue #81 performs none of the live P5
provider calls, admissions, dispatches, PRs, dispositions or judgments and grants
no CLIProxyAPI quality, live-boundary or P5-readiness verdict.

## Backlog disposition from issue #60

The pre-alignment open-issue audit found only #3, #5, #6 and #60. These old
requirements are superseded, not newly qualified:

| Issue | Why it is no longer a current prerequisite | Preserved historical verdict |
| --- | --- | --- |
| #3: exact authority-occurrence recovery / semantic catch-up | Current immutable submission/run correlation and terminal-result consumption do not replay historical semantic decisions. | Q1/Q6 BLOCKED; Q2 PASS on the recorded profile |
| #5: trusted candidate/evidence handoff, delayed observer and exhaustive custom reviewer-input qualification | Native review/repair replaces that protocol; admission authority and supported-profile limitations remain explicit. | Q4/Q5 BLOCKED |
| #6: generic exact-effect coordination before/after separate assessment | The authorized native PR bundle is not those narrower fixture intents or a generic effect system. | Q7/G1-effects BLOCKED |

Issue bodies and qualification records remain provenance. No other open issue
was found directing work toward removed graph/evidence/supervision machinery.
Future work follows the P5 scope above, not the historical phase chain.
