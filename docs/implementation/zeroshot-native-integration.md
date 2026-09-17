# Thin native Zeroshot integration

**Current implementation reference.** The
[current architecture and P5 plan](../governing/current.md) governs scope and next
work; this document supplies the detailed native boundary. The source and
result-handoff audits are dated supporting research, not competing plans.

Decision date: 16 September 2026. This is the current implementation boundary.
It supersedes the custom execution/proof requirements and recovery capability
claims in the v0.5 governing pair and V1-P3/V1-P4 implementation notes, without
changing their original bodies or qualification evidence. Immutable
admission, source entitlement, current-Attempt authority, and effect authorization
remain product requirements.

## Decision

Broodling prepares one immutable Work Unit/Attempt invocation, selects native
delivery from that Work Unit's frozen effect authority, submits it to Zeroshot's
standard `software-change` workflow, consumes the eventual native result, and
makes its own lifecycle decision.

An empty effect set selects `delivery="none"`; it never acquires PR authority by
configuration or fallback. Exactly one `pull_request` effect naming a target
branch selects `delivery="pull_request"`. Every other, mixed, underspecified or
multiple effect declaration is refused. Because LocalTarget binds delivery to
the checked-out branch while Broodling uses a synthetic Attempt branch, authorized
PR work uses a configured DirectTarget and passes the frozen repository, target
branch and B1 revision through Zeroshot's supported source selectors. PR delivery
is refused unless the Work Unit's frozen forge host is `github.com`.

The product owner explicitly selected the standard workflow over Broodling's
extra mechanical-evidence, separate-adjudication, and criterion-by-criterion
final-rationale record. The owner also selected blocked cleanup/retry when
cessation is uncertain over another Broodling execution supervisor.

The owner also removed admission preconditions requiring a finite evidence
population, validation seam, validation action, and falsifying observation for
every criterion. Acceptance criteria are enough; any already-available validation
guidance is passed through immutably. Choosing and performing validation belongs
to the standard workflow, not to an upstream Broodling proof-plan gate.

These are capability decisions, not just renamings. A successful supported
workflow is the acceptance authority for the frozen task. Broodling no longer
requires an independently collected execution proof record. It no longer claims
automatic safe recovery from a dispatched local run.

## Dependency and supported model

The adopted stable release is [Zeroshot 10.3.0](https://github.com/the-open-engine/zeroshot/releases/tag/v10.3.0),
with the matching [Python SDK 10.3.0.post1](https://github.com/the-open-engine/zeroshot/releases/tag/zeroshot-python-v10.3.0_1).
The Python package is `the-open-engine-zeroshot`, imported as `zeroshot`.
The official platform wheel bundles the matching Rust executable. Python exposes
typed requests/results and transport; native Zeroshot owns graph admission,
workflow expansion, execution, state, and provider behavior. The project pins the
official Linux x86-64 wheel URL and SHA-256 in [pyproject.toml](../../pyproject.toml).
It does not rebuild or source-hash the old development SDK.

The [current source audit](zeroshot-current-source-audit.md) records release
verification, supported APIs, and the source basis for the limitations below.
The material capabilities are:

- `Preset("software-change", delivery="none"|"pull_request")` supplies implementation,
  independent acceptance/code review, and bounded repair. Broodling chooses the
  preset; it neither authors nor interprets its graph.
- `UniformRuntime` supplies the provider/model configuration and
  `session_scope="execution"`. Zeroshot owns occurrence roles and fresh execution
  sessions; Broodling supplies the explicit local policy described below.
- `submission_key` provides native idempotency and a typed conflict carrying the
  existing run identity.
- `get_run(run_id).wait()` returns the eventual result, including one already
  completed before Broodling reconnects. Cancellation or a transport failure
  detaches a waiter; it is not abandonment or an execution restart.
- `force_stop()` requests native stop. Its terminal result is not a public
  physical-cessation receipt for a local provider process tree.

Execution-scoped freshness separates provider sessions between occurrences.
Within an execution, Zeroshot may resume its own session for response correction
or provider retries. Broodling no longer forces CLI-session disposal, which would
break that supported behavior. This is one concrete case where the native
workflow replaces a historical Broodling policy rather than merely translating
its configuration.

## Responsibilities that remain

No retained responsibility below interprets execution history or replaces native
control flow. Each protects a Broodling outcome that the supported native model
does not represent or configure.

| Broodling responsibility | Concrete outcome | Why native behavior alone is insufficient |
| --- | --- | --- |
| Entitled source snapshots, immutable Contract revision, and exact delivery authorization | Preserve the exact authorized task and permit only its one declared result effect. | Zeroshot accepts a selected preset; it does not know whether this Work Unit authorized that delivery. |
| One current Attempt, original B1, and exclusive worktree ownership | A result cannot complete another Attempt; host setup/deletion cannot target somebody else's checkout. | A `LocalTarget` uses a supplied workspace but does not own Broodling's Attempt authority, B1 policy, or disposal entitlement. |
| Persisted invocation and Attempt-to-run correlation | A repeated call or lost acknowledgement refers to the same admitted work, never a newly invented invocation. | Native idempotency does not persist the caller's Work Unit/Attempt relationship. Local source resolution also includes current HEAD, which can change after accepted work starts. |
| Explicit no-effect/user-configuration policy | Restrict supported tool/network settings and avoid inheriting ambient Codex user configuration or exec-policy escalation on the declared trusted-host profile. | The local worker defaults enable shell networking and resolve user configuration; the public runtime has no fields for these network/configuration policies. This does not exclude repository guidance or replace the managed-configuration host precondition below. |
| Current-authority check and atomic stable-result/disposition retention | Only the still-authoritative Attempt can complete, and only from the delivery it authorized. | Zeroshot's run result does not mutate Broodling's lifecycle database or decide its effect policy. |
| Fail closed when no stable result exists | A null no-effect result cannot be mislabeled as an immutable accepted candidate. | Zeroshot 10.3 has no local commit/artifact delivery; Broodling reports the gap instead of snapshotting or supervising. |
| Refuse cleanup/retry without safety authority | Do not delete a workspace or start a replacement while an old writer might survive. | `LocalTarget` has no general public physical-cessation receipt, including after controller loss. Broodling declines the operation instead of adding a supervisor. |

Broodling still uses locks and durable transitions for its own SQLite/Git
administrative operations. Those serialize admission, provisioning, correlation,
and safe undispatched retirement writes; they do not serialize the external
Zeroshot submission call or supervise execution.

## Invocation and result

Preparation freezes the canonical Contract, exact entitled instruction snapshots,
original comparison base B1, workspace/source identity, authorized delivery,
native preset/runtime, target configuration, and submission key. Callers cannot supply a replacement
graph or a different per-call task. A criterion may supply only its identity and
acceptance statement. An evidence population, validation seam/action, and
falsifying observation are optional task guidance, not Broodling-run evidence
commands or admission requirements. Historical supplied guidance remains part of
its immutable Contract. Unsupported effects and external obligations,
effect-dependent evidence, and unsatisfied prerequisites still fail closed;
removing the proof-plan gates does not waive those domain restrictions.

Broodling durably marks dispatch before the external call, releases its SQLite
writer, then stores the returned run ID in a second short transaction.
Independent Work Units and lifecycle writes therefore remain concurrent with a
slow native submission. Concurrent callers for one Attempt may both cross the
SDK seam with the exact persisted request; Zeroshot's submission-key idempotency
is the duplicate-prevention boundary, and Broodling requires every response to
converge on one run ID. Reconciliation repeats only the identical invocation. A
typed native conflict can recover the existing run ID only for the narrow case
of an already-dispatched request whose owned source assignment still matches and
whose HEAD has changed from B1. Other source/configuration conflicts fail closed.
DirectTarget replay uses the same explicit source triple, so an exact retry
normally returns the existing run without local worktree drift. Because delivery
credentials are intentionally absent from persisted requests, Broodling checks
the current `GH_TOKEN` again before every initial or replayed PR dispatch, outside
the SQLite writer transaction.
There is no lease, ledger scan, execution discovery, or runtime replay algorithm.

Abandonment may commit while submission is in flight. If Zeroshot subsequently
acknowledges a run, Broodling retains that factual Attempt-to-run correlation but
reports that current authority was lost; abandonment still prevents disposition.
If acknowledgement was lost before abandonment, Broodling does not replay after
authority is gone. The unresolved dispatched Attempt remains quarantined.

Finalization waits through the public SDK. A successful result must name the
correlated run, and the Attempt must remain current and bound to the unchanged
admitted invocation. For PR delivery, the exact native v1/pr/opened receipt must
match the frozen repository and target branch; its non-B1 `headRevision` is the
stable accepted result. Broodling retains the whole receipt without re-reading
the mutable worktree. Native PR delivery owns commit, push, PR creation/update,
repair and receipt validation.

After durable correlation, Broodling reconnects from the locator already frozen
in that invocation: LocalTarget uses the canonical native state directory and
DirectTarget uses the persisted target origin. Waiting and force-stop do not
reconstruct or revalidate the dispatch-time workspace, Codex profile, runtime,
provider environment, target configuration, or `GH_TOKEN`. Those remain strict
requirements for new dispatch and acknowledgement-loss replay only. Completion
still rechecks current Attempt authority, admitted Contract and delivery
authority, the immutable Attempt/run/invocation binding, and the native delivery
receipt.

For no-effect delivery, Zeroshot 10.3 succeeds with `output=None`. Broodling does
not turn that mutable workspace into a completed result: final disposition fails
closed with the explicit local-result capability gap. A native failure abandons
the Attempt but does not grant cleanup authority. Explicit abandonment also
prevents a later successful result from completing the Work Unit.

Successful result retention and disposition commit together in one SQLite
transaction. A failed write grants no partial completion; another call may
consume the same native result. A cancelled/unavailable wait likewise leaves the
Attempt available for another wait. There is no live observer, completion-window
marker, or finalization recovery protocol.

The result record retains the Attempt, Contract revision, run identity, preset,
accepted revision and entire Zeroshot delivery receipt. The old selected-material
collector is removed: new Contracts that request it are refused rather than
silently ignored, while historical records remain readable.

## Local policy and cleanup limitation

The launcher policy below applies to `delivery="none"` LocalTarget execution.
Authorized PR delivery uses the configured Zeroshot DirectTarget, whose operator
owns its provider installation, authentication and sandbox profile. Broodling
supplies `GH_TOKEN` only to the SDK process for the native delivery binding; the
token is not persisted in the invocation and the agent runtime declares no token
connection. The frozen Contract authorizes the PR effect, not general provider
access or any additional effect. Configuring that endpoint is therefore a hard
operator trust boundary: Broodling freezes its origin into the invocation and
refuses a changed endpoint on replay, but it cannot qualify or constrain the
remote target's installation from the local SDK.

The small [Codex launcher](../../broodling/codex_bin/codex) applies explicit
workspace-write worker/read-only verifier sandbox modes, strips sandbox/approval
bypass flags, disables shell networking and web search, and selects explicit
isolated homes. `--ignore-user-config` excludes the Codex-home `config.toml`;
`--ignore-rules` excludes exec-policy `.rules` files. Approval is always `never`.
Supported CLI overrides also set `features.apps=false`,
`features.plugins=false`, `features.hooks=false`, and `notify=[]` to disable those
effect-capable extension paths outside the shell sandbox.
The launcher refuses the native app-server
configuration probe so Zeroshot uses its supported unavailable-config fallback
rather than consulting ambient configuration. Execution then `exec`s the
configured Codex CLI.
It does not fork a supervisor, track PIDs, interpret prompts/responses, dispatch
evidence commands, or perform cleanup. Native Zeroshot owns the launched process.

These switches do not disable `AGENTS.md` or ordinary project documentation.
Zeroshot's standard workflow may use current repository guidance as execution
context. The frozen Contract and entitled snapshots remain Broodling's admission
authority; repository edits cannot amend those stored facts. Broodling trusts the
supported workflow to carry out that task rather than independently proving that
an agent never follows erroneous repository guidance.

The selected CLI is `codex-cli 0.153.4`. Host-provisioned HOME starts empty;
CODEX_HOME starts auth-only. Explicit SDK environment values suppress ambient
homes/configuration/scratch variables; the native engine still selects its own
exact execution TMPDIR. These fresh homes carry no project-trust grants;
repository `.codex` configuration cannot grant itself trust. This is distinct
from the standard workflow reading repository guidance.
Codex's supported
[`exclude_slash_tmp=true` setting](https://learn.chatgpt.com/docs/config-file/config-reference) removes
the broad writable `/tmp` root, so the old shared-Git-outside-`/tmp` prohibition
is unnecessary. Profiles/executables remain outside the candidate, and native
control state has a canonical directory separate from candidate/shared Git data.
This is a configured local policy, not a new qualification claim against
arbitrary host compromise or malicious sandbox escapes.

There is also a managed-configuration limit: operator-managed/system Codex
configuration can declare MCP servers. The selected CLI has no universal
unknown-server-name MCP-off override; `mcp_servers={}` merges configuration and
does not reliably remove inherited server entries. **This local profile requires
a trusted host with no operator-managed effect-capable MCP or extension
configuration.** Administrators can enforce an empty `[mcp_servers]` allowlist in
managed `requirements.toml`. An environment where that precondition is not known
to hold is outside this no-effect profile. Broodling does not reproduce Codex's
configuration interpreter, scan arbitrary managed settings, or independently
prove the dependency's enforcement. The source audit records this supported
local-policy limitation; shell-network restrictions alone are not a universal
no-effect guarantee.

Native local process-group cleanup does not establish the stronger escaped-child
cessation guarantee previously supplied by Broodling's own containment machinery.
Because the public result has no receipt for that guarantee, the current policy
is conservative: **every Attempt dispatched under this integration is ineligible
for automatic cleanup or replacement**, even if a terminal result reports success
or force stop.
`AbandonmentCoordinator.stop` records irreversible abandonment and requests native
stop when the run is known. Even a returned terminal result yields
`CessationUnconfirmed`; an unavailable stop also grants no cleanup authority. An
unknown run ID is not recovered by replaying execution from the stop path.

An Attempt proven never materialized or never dispatched can still be retired
under Broodling's exact ownership checks. Explicit replacement then uses its
original admitted B1 and the same empty/auth-only pre-dispatch profile policy;
it never reuses an abandoned candidate. Fresh execution-scoped provider sessions
are Zeroshot's responsibility. There is no separate retry-home reservation
registry or scan of historical provider context. Dispatched recovery requires a
stronger supported execution target/receipt or a separate operator-established
safe host boundary.
This version provides no bypass that promotes a terminal label or unfinished
historical cessation proof into new cleanup authority. Already-completed
historical retirements remain recorded facts; the new policy does not undo them.

## Removed machinery and historical compatibility

Removed: the authored assurance graph and its routing/bookkeeping policy,
deterministic evidence worker and prompt-dispatch workaround, separate reviewer
projection, criterion-by-criterion final proof capture, live run observation,
candidate-generation/provenance bookkeeping, process containment supervisor,
SDK source-hash qualification gate, retry-home reservation/context-scanning
machinery, and finalization markers. The associated custom execution fixtures
and campaigns were deleted. Native capability gaps
are stated above rather than recreated behind new abstractions.
The four historical proof-plan admission gates and their refusal tests were also
removed; criteria-only admission now exercises the simpler supported boundary.

The `final_assurance` storage name remains so historical records stay readable;
new stable delivery records are tagged `broodling.final-assurance/v3`. Schema v10
migration preserves v9 stores created by earlier PR revisions as well as
old admission/custody/disposition facts and completed retirements but removes the
obsolete `attempt_finalizations` mechanism. An old proof record cannot authorize a new
standard-workflow disposition. Old invocation configurations are not mechanically
rewritten to target the new release or preset.

The preserved [qualification reports](../../qualification/README.md), baseline,
and versioned governing/implementation records describe their original versions.
Their passes do not transfer to this release. The new acceptance model deliberately
does not promise the extra proof record or automatic dispatched-run recovery.

## Validation boundary

The [test suite](../../tests/README.md) concentrates on Broodling's admission,
identity, lifecycle, custody, migration, policy, and public SDK seams. Native
workflow tests use the published SDK/bundled executable with a controlled Codex
provider and assert resulting candidate/run binding, not internal graph traces.
They do not qualify paid-provider reasoning, sandbox escape resistance, or a
physical-cessation capability. The PR description records the exact validation
commands and results for this change.
