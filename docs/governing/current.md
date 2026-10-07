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
| One current Attempt and original B1, with no local execution checkout | Native graph expansion/routing, acceptance/code review, repair and provider sessions |
| Frozen invocation, durable dispatch intent, Attempt/run correlation and current-authority checks | Submission-key idempotency, execution state, reconnectable terminal result and native stop |
| Receipt validation against authorized delivery and atomic result/disposition retention | Authorized checkout, commit, push and PR creation/update, including delivery repair and receipt production |
| The approved execution asset and refusal of unsafe cleanup/retry | Provider execution; operator/host procedure owns process/container containment and physical-cessation proof |

The persisted pause controls Broodling admission and dispatch initiation only;
its status never proves native execution or container/process cessation. Zeroshot
owns native execution and its stop interface, while actual host/container
containment and cessation checks remain with the operator/host procedure.

B1 is the original admitted Git commit plus entitled instruction snapshots, not
today's branch tip. A source-attributed Contract that grants exactly one
`pull_request` effect can be admitted with acceptance criteria alone: evidence
population, validation seam/action and falsifying observation are optional
guidance. An empty required-effect set, other unsupported effects or
obligations, effect-dependent evidence, unsatisfied prerequisites and legacy
selected-final-material requests are refused. Repository guidance may be
execution context but cannot amend stored authority.

Source: [admission/delivery policy](../../src/Broodling/Closability.cs),
[invocation](../../src/Broodling/Invocation.cs),
[HTTP submission](../../src/Broodling/HttpSubmission.cs) through the
[SDK client](../../src/Broodling/DirectTargetClient.cs),
[run reader and stopper](../../src/Broodling/DirectTargetRun.cs),
[completion](../../src/Broodling/AttemptCompletion.cs), and
[stop/retirement](../../src/Broodling/AttemptRetirement.cs).

## Supported profile and outcomes

The supported source/release profile is single-host Linux x86-64, .NET 10 /
ASP.NET Core, SQLite through Microsoft.Data.Sqlite, Git and the GitHub CLI.
Broodling has one execution target. It talks HTTP/OECP, through the pinned
`Zeroshot.Client` SDK, to the stock `zeroshot target serve` of an
operator-managed DirectTarget running native Zeroshot 10.10.0 (#215, #226). The
DirectTarget image pins native 10.10.0 and Codex 0.153.4. Broodling submits the
release-bundled approved execution asset (native 10.10.0's standard
`software-change` PR workflow with one uniform Codex / `gateway` /
`gpt-5.6-sol` / medium-effort runtime and native's default `consider` PR
feedback) and uses exactly `https://cliproxy.local.faviann.com/v1`. Native
materializes the run from that asset's graph and runtime; Broodling passes both
through unchanged and builds no preset or runtime of its own. Broodling needs no
Python, SDK client state, workspace root, Codex installation or launcher. The
operator configuration names the DirectTarget origin and the file holding its
private control token. The target serves only native's private mode (#187):
every control and OECP request needs that token, which the operator's
`bootstrap-target` installs in each new target process and execution agents
cannot obtain. Agents can still reach Broodling's own unauthenticated
processing routes on the shared project network, so strict agent isolation
is not established until #240 authorizes those routes after the MVP
([private control access](../../deployment/README.md#private-control-access)). Callable
`TargetReadiness.CheckAsync` and the thin `check-target` command check the
actual target's image, configuration, pinned dependencies, including GitHub
CLI, and authenticated private control. See
[readiness](../implementation/dotnet-target-readiness.md) and
[private control access](../../deployment/README.md#private-control-access).

| Frozen effect authority | Supported behavior |
| --- | --- |
| Exactly one `pull_request` effect with a target branch for a GitHub Work Unit | DirectTarget native PR delivery from exact B1, with no client execution checkout. A matching successful `v2/pr/ready` receipt supplies the stable non-B1 `headRevision`; after that exact commit is fetched and pinned locally, the disposition for that exact Attempt commits atomically with the receipt. |
| Empty required-effect set | Refused at Contract admission with a retained finding. No-effect work has no supported execution target or stable result; the [capability umbrella](https://github.com/faviann/broodling/issues/78) keeps it as future work. |
| Other, mixed, multiple or underspecified effects | Refused at Contract admission. Merge, standalone push, issue mutation, deployment and generic effect execution are unsupported. |

PR delivery includes native commit, push, open-or-update and native's `ready`
assessment: its required checks and policy gates passed, with PR feedback
considered and non-ready outcomes routed to native repair. It promises neither
human approval, semantic correctness nor merge. Initial or acknowledgement-replay dispatch requires current
`GH_TOKEN`, `GATEWAY_BASE_URL` and `GATEWAY_API_KEY`; their values are not frozen
or persisted by Broodling. Once run correlation is durable, status, wait, stop and
terminal replay use retained identity without dispatch credentials. Lost
acknowledgement may replay only the identical frozen request while authority
remains current; only the exact acknowledgement establishes correlation.

Node-local OAuth authorized-PR delivery, caller-selectable harness/model/runtime,
per-node runtimes, fleet placement and broader effects are unsupported rather
than hidden configuration options.

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
disposition. #107 adds
service-owned GitHub repository preparation: the retained default PR branch and
exact starting commit are separate facts, and prepared Attempt admission uses
those retained facts rather than caller checkout or later repository state.
Repository-file capture continues through the retained commit. #108 captures the
marked v1 Executable Request and its declared, bounded GitHub reference closure,
or retains deterministic refusal findings before any Contract. #111 admits a
completed bundle as a Contract bound to its identity and manifest digest,
attributing only the Executable Request and granting one PR to the retained
target branch. #119 adds bounded, unretained
native phase/active-node observation for correlated Attempts, reported separately
from retained facts and as unavailable on transport loss. #163 replaces the
DirectTarget client bridge with the HTTP/OECP integration over fresh state: the
approved execution asset, offline preparation, exact durable acknowledgement,
retained-binding progress, wait and stop, and (#180) routing of callable and
operator PR invocation through it, with the bridge DirectTarget path removed. A
controlled witness runs the unmodified stock native boundary. #186 packages
[ADR 0001](../adr/0001-directtarget-https-origin-and-compose-topology.md)'s
target side: a once-created TLS root, `zeroshot-tls`'s Caddyfile, native
initialization through the HTTPS origin and readiness of that stack. #121 builds
the non-root Broodling image and the DirectTarget image, demonstrates them
together in a disposable instance of that topology and publishes them to GHCR
with a release record of digests, pins and the supported store schema
([images](../../deployment/README.md#images)). #125 checks before each publication
that the target image serves native state written by each listed published
target image, and records those
[established transitions](../../deployment/README.md#native-state-transitions).
#215 replaced the initial DirectTarget native, asset, receipt and store
definition with native 10.9.0's and removed the 10.3.0 sources; the check now
restarts the candidate image over its own native state, and transitions are
established only within one native release. #216 submits and explicitly replays
the retained request through the pinned `Zeroshot.Client` 0.1.0-preview.1 SDK, one
attempt with current credentials and no automatic resend; Broodling keeps the
durable facts. #226 moved the binding to native 10.10.0 and `Zeroshot.Client`
0.2.0-preview.1; an Attempt retained with the 10.9.0 binding refuses as
differing. #217 moves the run reader and stopper onto the same SDK client:
inspection is one bounded status read, completion waits through the SDK's
`Run.WaitAsync` instead of client polling, and the configured root reaches every
connection through the SDK's `TrustedRootCertificatePath`. #220 records
Broodling's [adoption](../../tests/README.md#sdk-adoption) of that exact package:
a lock file fixes its bytes, and Broodling's own lane, rerun for every SDK
upgrade, gates adoption, not SDK publication. No ambient proxy is a
deployment assumption: Broodling's container must not define proxy variables.
#219 witnesses that asset's stock PR readiness, repair and feedback contract
against controlled forge scenarios
([witness](../../tests/README.md#pr-readiness-repair-and-feedback)), and the
status reader now drops native's workspace-recovery facts on failed runs.
The Compose installation
(homelab-iac#353) remains open. #113 serves retained
work and frozen references over read-only HTTP from the ASP.NET host, opening
existing state only. #118 adds a callable completion observer that retains
correlated HTTP results with no caller waiting. #241 makes each native submission carry its RequestBundle
references in place of #114's agent reads from Broodling; see
[frozen references](../implementation/zeroshot-native-integration.md#frozen-references). #112 adds the callable bundled proposer: the supported model
behind the pinned gateway prepares that bundle-bound Contract from the Executable
Request, a compact manifest and on-demand frozen-reference reads. Malformed or
authority-changing proposals retain findings and reject the submission; gateway
failures retain nothing and may be retried. #116 composes capture and that
proposer into one callable operation that prepares an exact submission to its
admission decision or retained finding, or reports a failure as retryable or
needing attention. It continues from committed checkpoints and never proposes a
committed Contract again. Within one process each submission has one
preparation owner, and different submissions prepare independently. #117 adds a
callable progression service that discovers unfinished Issue submissions at
startup and on a cadence and, with no caller connected, prepares each through
that operation and continues an admitted Contract from retained B1 to native
correlation, replaying an unresolved dispatch exactly. It retries temporary
failures with a doubling delay up to a limit, stops on refusals and conflicts,
never waits for an Attempt and never initiates a stop, abandonment or
replacement. #120 makes the ASP.NET host, when configured with a DirectTarget,
a service repository root and current credentials, accept URL-only Issue
submissions over HTTP once durably committed, map exact resume and stop onto
the progression service and the existing cancellation and stop operations, and
run one preparer, the progression service and the completion observer for its
lifetime. Its submission and Attempt reads add one bounded native observation
beside the retained facts. Disconnects and ordinary shutdown detach without
stopping or abandoning work; the server stops if either service fails.
Unconfigured, it stays a reader. #124 adds an explicit revision: a new Issue
submission that names the Work Unit's latest, ended submission as its
predecessor, created at most once and returned exactly on replay. Progression
captures and prepares it afresh; if its work-defining request identity (#207:
primary and referenced issue titles and bodies, referenced comment bodies,
other references' content, starting commit and PR target, ignoring GitHub
bookkeeping) equals that of an already-admitted submission whose Contract has
an Attempt, it ends with a retained explanation linking that authority instead
of being proposed or executed. #205 runs `retire-attempt` and
`replace-attempt` from the Broodling image for Attempts that the processing
server created, with the stopped-target check supplied by the host; CLI Attempts
keep them in the release artifact. After release, the image's `resume`, run as
the processing service, dispatches a processing-server Replacement Attempt
(#210). #233 removed the no-effect LocalTarget with its Python SDK bridge,
worktree Attempts and C# Codex launcher. Contract admission now refuses an empty
required-effect set, and the unreleased store schema 1 was redefined in place.
#187 replaced the unauthenticated DirectTarget with native 10.10.0's private
mode: a per-target random, non-expiring control token, distinct from a
root-only bootstrap key; initialization without native's client; and explicit
rotation by restart and bootstrap. Broodling reads the token for each operation
from the file configured for the exact retained origin and never retains it, so
a token change amends no Contract, Prepared submission, correlation or binding.
Homelab provisioning remains homelab-iac#353 and #354.
Remaining #100 intent includes
Compose, maintenance and backup/restore; those remain unimplemented. The
ASP.NET host is not authority to add them.

## Lifecycle and retention limits

Every dispatched Attempt remains ineligible for automatic deletion or
replacement, including after native success or stop. Terminal labels are not
physical-cessation receipts. An Attempt owns no local directory, so no
retirement deletes anything. Its ordinary safe retirement
requires abandonment with no committed dispatch intent. A dispatched DirectTarget
Attempt, abandoned or completed, can be retired only on the narrow
`stopped_target` maintenance path (#122): under the persisted pause, with drained
local initiation and a current host check that the correct target and its state
mounts are stopped. It records that check and leaves a completed Attempt
unabandoned. An abandoned Attempt retired this way can then be explicitly replaced
(#123) from the same Contract, RequestBundle binding and original B1; the
successor is admitted and prepared only under the pause and dispatches only after
release, through Resume (for a processing-server successor, the image's `resume`,
#210). A revision (#124) likewise requires every dispatched Attempt of its Work
Unit, successful or not, to be retired this way and none to be current; its
Contract's own Attempts then disregard the earlier Contracts' ended and completed
work, and replacement refuses an Attempt of any Contract the latest submission
supersedes (a latest unchanged revision keeps its linked Contract replaceable). One
current execution authority, that of the latest revision, therefore remains, and
earlier results stay intact.
An HTTP Attempt's prepared submission retains the complete request,
approved asset bytes and an intended run identity; that is neither dispatch intent
nor native acceptance. Broodling records abandonment and requests native
stop, while operators retain host/container containment responsibility. A safely
retired never-dispatched Attempt can still be explicitly replaced from original
B1. There is no override that converts incomplete historical proof into cleanup
authority.

The Broodling SQLite store, source Git common directories and DirectTarget
state/home are durable operating state. Preserve their identities and absolute
paths as described by the operations guide. .NET uses deliberate separate fresh
state; the owner must choose drain or explicit abandon-and-retain for existing
Python work before any operational switch. There is no import, in-flight
takeover or implicit deletion authority. The DirectTarget integration likewise
requires an explicitly initialized fresh store; see the
[state lifecycle](../implementation/dotnet-identity-custody.md). No store schema
has been released, so schema 1 still changes in place, and a store that an
earlier build initialized is refused unchanged. The first `v*` release freezes
it, and later changes need an explicit upgrade or a documented refusal. The
separate #77 smoke environment was disposable and has been removed; its committed
validation record remains.
