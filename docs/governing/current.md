# Current architecture and operating status

**Status: current authority for product scope and responsibility.** Broodling is
post-MVP, with a .NET 10 application/operator path for limited,
operator-supervised internal PR proposals. Source/release support does not claim
a validated live .NET deployment or authorize an existing-state switch.
Completed phase plans, qualification campaigns and P5 execution protocols are
not current requirements or a backlog. Git history is their archive.

## Documentation authority

This document governs architecture and supported scope. The
[native integration](../implementation/zeroshot-native-integration.md) is the
detailed implementation reference. The [invocation](../implementation/invocation.md)
and [work-reference ingress](../implementation/work-reference-ingress.md) documents
describe the two public application seams. Production code and the
[current tests](../../tests/README.md) establish implemented behavior; investigate
a discrepancy rather than restoring an older design.

The root README is current developer/user guidance. The
[release and operations guide](../../deployment/README.md) describes packaging,
the supported host/target profile and the future owner-approved cutover gate.
The [#77 validation record](../../deployment/validation.md) is historical Python
deployment evidence only. The concise [P5 outcome](../../evaluation/p5/README.md) explains the
quality limitation that still constrains use.

Historical governing plans, implementation designs, qualification harnesses and
evaluation campaigns were removed from the working tree to prevent accidental
reuse. They remain available in Git, including the complete pre-cleanup tree at
[`348e1f4`](https://github.com/faviann/broodling/tree/348e1f469c04fecbc24f4088e6eb438a3934e872).
Their requirements, gates, verdicts and commands apply only to their recorded
revisions and profiles.

## Product boundary

Broodling handles one software-change Work Unit, identified by one repository
and primary issue. It is not a backlog selector, scheduler, dependency waiter or
multi-project orchestrator.

| Broodling owns | Zeroshot owns |
| --- | --- |
| Entitled source snapshots, immutable Contract admission, criteria and exact effect authorization | Implementing and validating the frozen task through the standard `software-change` workflow |
| One current Attempt, original B1 and exclusive ownership of its dedicated local worktree | Native graph expansion/routing, acceptance/code review, repair and provider sessions |
| Frozen invocation, durable dispatch intent, Attempt/run correlation and current-authority checks | Submission-key idempotency, execution state, reconnectable terminal result and native stop |
| Receipt validation against authorized delivery and atomic result/disposition retention | Authorized checkout, commit, push and PR creation/update, including delivery repair and receipt production |
| Explicit local execution policy and refusal of unsafe cleanup/retry | Provider execution; operator/host procedure owns process/container containment and physical-cessation proof |

The persisted pause controls Broodling admission and dispatch initiation only;
its status never proves native execution or container/process cessation. Zeroshot
owns native execution and its stop interface, while actual host/container
containment and cessation checks remain with the operator/host procedure.

B1 is the original admitted Git commit plus entitled instruction snapshots, not
today's branch tip. A source-attributed Contract can be admitted with acceptance
criteria alone: evidence population, validation seam/action and falsifying
observation are optional guidance. Unsupported effects or obligations,
effect-dependent evidence, unsatisfied prerequisites and legacy selected-final-
material requests are refused. Repository guidance may be execution context but
cannot amend stored authority.

Source: [admission/delivery policy](../../src/Broodling/Closability.cs),
[dispatch](../../src/Broodling/NativeDispatch.cs),
[SDK transport](../../src/Broodling/NativeTransport.cs),
[completion](../../src/Broodling/AttemptCompletion.cs), and
[stop/retirement](../../src/Broodling/AttemptRetirement.cs).

## Supported profile and outcomes

The supported source/release profile is single-host Linux x86-64, .NET 10 /
ASP.NET Core, SQLite through Microsoft.Data.Sqlite, Git, Python 3.13+ solely
for the Zeroshot 10.3.0 / SDK 10.3.0.post1 bridge, and Codex 0.153.4. The
authorized-PR path uses an operator-managed DirectTarget, Zeroshot's standard
`software-change` workflow, one uniform Codex / `gateway` / `gpt-5.6-sol` /
medium-effort runtime, and exactly
`https://cliproxy.local.faviann.com/v1`. Callable `TargetReadiness.CheckAsync`
and the thin `check-target` command check the selected actual target's image,
configuration and pinned dependencies, including GitHub CLI. See
[readiness](../implementation/dotnet-target-readiness.md).

| Frozen effect authority | Supported behavior |
| --- | --- |
| Empty required-effect set | LocalTarget execution is permitted, but native success returns no stable accepted result; Broodling therefore refuses successful disposition. |
| Exactly one `pull_request` effect with a target branch for a GitHub Work Unit | DirectTarget native PR delivery. A matching successful `v1/pr/opened` receipt supplies the stable non-B1 `headRevision`; after that exact commit is fetched and pinned locally, the disposition for that exact Attempt commits atomically with the receipt. |
| Other, mixed, multiple or underspecified effects | Refusal. Merge, standalone push, issue mutation, deployment and generic effect execution are unsupported. |

PR delivery includes native commit, push and open-or-update. It promises neither
passing CI nor merge. Initial or acknowledgement-replay dispatch requires current
`GH_TOKEN`, `GATEWAY_BASE_URL` and `GATEWAY_API_KEY`; their values are not frozen
or persisted by Broodling. Once run correlation is durable, status, wait, stop and
terminal replay use retained identity without dispatch credentials. Lost
acknowledgement may replay only the identical frozen invocation while authority
remains current.

The no-effect LocalTarget profile is restricted to the documented trusted-host
policy and remains unable to produce successful stable disposition. Node-local
OAuth authorized-PR delivery, caller-selectable harness/model/runtime, per-node
runtimes, fleet placement and broader effects are unsupported rather than hidden
configuration options.

## First-use limitation

P5 is complete with a scoped **FAIL**. One compatible v6 run proved the
authorized-PR delivery, disposition and reconnection boundary but delivered a
semantically incorrect change that native reviewers accepted. This is why the
supported use is limited to operator-supervised internal PR proposals.

Every delivered PR requires independent human/operator review of the exact
accepted revision against the frozen work request and admitted Contract, with
appropriate repository tests and CI considered before a separate merge decision.
Broodling `SUCCEEDED`, native acceptance and automated checks certify neither
semantic correctness nor authority to merge, deploy or release. See the
[retained outcome summary](../../evaluation/p5/README.md).

The .NET application preserves the #75–#77 callable/operator behavior. The
[passed migration review](../migration/130-migration-review.md) and
[parity map](../migration/130-parity-map.md) retain its evidence. Current
development follows explicitly scoped open issues. #105 adds durable
pre-Contract Issue submission identity and inspection through the callable
SQLite store. #157 adds persisted admission/dispatch pause controls and drain
status; host containment, process supervision and physical-cessation maintenance
remain operator/host responsibilities. #106 adds interruption-safe RequestBundle
capture checkpoints, immutable completion and bundle-scoped reads. #115 fetches
and pins each exact accepted commit under a Broodling-owned ref before successful
disposition. #119 adds bounded, unretained native phase/active-node observation
for correlated Attempts, reported separately from retained facts and as
unavailable on transport loss. Remaining #100 intent includes remote reference selection/acquisition,
public HTTP intake, a bundled proposer, automatic progression/completion, Compose,
maintenance and backup/restore; those remain unimplemented. The ASP.NET host is not authority
to add them.

## Lifecycle and retention limits

Every dispatched Attempt remains ineligible for automatic deletion or
replacement, including after native success or stop. Terminal labels are not
physical-cessation receipts. Broodling records abandonment and requests native
stop, while operators retain host/container containment responsibility. A safely
retired never-dispatched Attempt can still be explicitly replaced from original
B1. There is no override that converts incomplete historical proof into cleanup
authority.

The Broodling SQLite store, source Git common directories, Attempt worktrees,
runtime state and DirectTarget state/home are durable operating state. Preserve
their identities and absolute paths as described by the operations guide. .NET
uses deliberate separate fresh state; the owner must choose drain or explicit
abandon-and-retain for existing Python work before any operational switch.
There is no import, in-flight takeover or implicit deletion authority. The
separate #77 smoke environment was disposable and has been removed; its committed
validation record remains.
