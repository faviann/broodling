# P5 native authorized-PR evaluation protocol v4

**ID: `p5-native-pr-v4` — prospective V1 execution-profile amendment, 18 September 2026.**

**No live P5 provider trial has started. R01-R08 remain NOT_STARTED.**

This version supersedes `p5-native-pr-v3` only for future live dispatch. It
incorporates v3's operational policy unchanged and preserves the v1 corpus,
tasks, B1, judge, outcome classes and counting rules together with v2's
claim-level decision rules. The v1-v3 protocols and all blocked/not-run records,
including #64, #65, #67 and #71, remain immutable provenance.

The immutable freeze identity for a future run is the commit that first adds this
file. Record that commit and a compatible product/dependency/test baseline before
R01's first admission. This amendment performs no admission, provider call, run,
GitHub delivery, disposition, or judgment.

## Selected V1 execution profile

Every counted trial uses exactly this product profile:

- Zeroshot's standard `software-change` workflow;
- native `pull_request` delivery through the already-supported `DirectTarget`
  path, with the Contract-authorized repository, target branch and exact B1;
- Codex harness, OpenAI provider, current Sol model `gpt-5.6-sol`, and medium
  reasoning effort;
- one `UniformRuntime` across all executable workflow nodes, with size `small`
  and `session_scope="execution"`; and
- current `OPENAI_API_KEY` and GitHub delivery credentials supplied ephemerally
  at initial or replayed dispatch as established by #69, never persisted in the
  frozen invocation. Correlated wait/finalization remains credential-free.

This is a fixed V1 selection, not a configuration surface. Variable harness,
provider, model, or effort selection; per-node `RuntimePlan` overrides; skill/tool
capability profiles; fleet scheduling; and node-local OAuth authentication are
post-V1 concerns. Issue #72 remains the source-backed dependency finding for a
future node-local OAuth authorized-PR profile. Its result does not block this
supported DirectTarget profile.

## Preserved evaluation and operational rules

Follow v3 for setup prerequisites, monitored operation, evidence retention,
stop/safety behavior and the distinction between setup and a started trial. In
particular, retain the designated disposable GitHub target, real Broodling
inputs, exact PR authority, compatible DirectTarget, active operator stop
capability, independent exact-revision judging, durable-enough evidence, serial
execution and dispatched-Attempt quarantine.

Follow the frozen v1/v2 material for corpus order, judging and decision rules.
R01 is still the single live-boundary trial and counts once at its first admission
attempt. Setup failures before that boundary may be corrected without consuming
the slot. Do not substitute a provider, manually publish a PR, replace or rerun a
dispatched Attempt, run R02-R08 before a compatible successful R01 boundary, or
change task/measurement rules in response to an observed result.

The next task is issue #79: one R01 live-boundary run against this exact profile.
It must retain either compatible successful boundary evidence or an honest
terminal FAIL/BLOCKED record, then stop before R02. Preparing this protocol does
not start that task.
