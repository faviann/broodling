# P5 native authorized-PR evaluation protocol v5

**ID: `p5-native-pr-v5` — prospective gateway provider/credential amendment, 19 September 2026.**

**No live P5 provider trial has started. R01-R08 remain NOT_STARTED.**

This version supersedes [`p5-native-pr-v4`](../v4/protocol.md) only for future
dispatch and changes only its provider/credential binding. Preserve every v1-v4
protocol and prior evidence package byte-for-byte, including all BLOCKED/NOT RUN
provenance. The frozen v1 corpus/judge, v2 claim-level rules and v3 operational
policy remain unchanged. No prior evidence is rescored or relabeled for v5.

The immutable freeze identity is the commit that first adds this file. Record
that commit and a compatible product/dependency/test baseline with a supported
full-suite result before R01's first admission. This amendment performs no live
provider call, admission, dispatch, PR, disposition or judgment; it claims no
CLIProxyAPI task quality, successful native PR boundary or P5 readiness.

## Provider/credential delta

Replace v4's `openai` provider with Zeroshot's supported `codex/gateway` lane
through CLIProxyAPI at exactly `https://cliproxy.local.faviann.com/`. Supply explicit
current `GATEWAY_BASE_URL`, nonempty `GATEWAY_API_KEY` and GitHub delivery credential
`GH_TOKEN` only in the SDK environment for initial or idempotently replayed
dispatch. No `OPENAI_API_KEY` is sent. Missing/empty gateway fields, a different
base URL or conflicting legacy provider credentials in the dispatch environment
fail closed. Credential values stay out of Contracts, persisted requests/runtime
plans, logs and evidence. Correlated reconnect/wait/stop remains credential-free
from the persisted DirectTarget origin.

Keep Zeroshot 10.3.0 / Python SDK 10.3.0.post1, the standard `software-change`
workflow, native `pull_request` delivery through the configured DirectTarget,
Contract-authorized repository/target branch/exact B1, Codex harness,
`gpt-5.6-sol`, medium reasoning effort, size `small`, execution-scoped sessions and
one `UniformRuntime` across every executable workflow node. The CLIProxyAPI base
URL does not replace the Zeroshot DirectTarget origin. Node-local OAuth remains a
post-V1 concern documented by #72. No-effect LocalTarget behavior is unchanged.

## Ordering before the one R01 boundary

The current dependency order is **#81 → record v5 freeze and compatible baseline
→ separately authorize #79's one R01 gateway execution → #68 → #66**. The current
#62/#79 issue dependency text follows this order. Do not execute #79 using its
former OpenAI/v4 wording or comments; those do not authorize this profile.

Retain v3's configured compatible DirectTarget/current credentials, disposable
GitHub target, exact PR authority, real per-trial Broodling inputs, active operator
stop capability, independent exact-revision judging, evidence retention and
dispatched-Attempt quarantine. R01 counts once at its first admission attempt.
Implementation, controlled tests, documentation, setup and endpoint preflight do
not consume its slot or authorize a provider task run. Once admission starts,
the frozen all-started accounting and no-rerun rules apply.

Only compatible successful R01 boundary evidence unlocks #68's R02-R08 corpus.
#79 must retain either that evidence or an honest FAIL/BLOCKED record and stop
before R02. #66 may review a settled incomplete package. No provider substitution,
manual PR publication, replacement dispatched Attempt, extra boundary trial or
change to frozen measurement rules is authorized by this amendment.
