# V1-P3 assurance graph — issue #17

The product now authors the short assurance protocol in
[`broodling/assurance_graph.py`](../../broodling/assurance_graph.py). Zeroshot
admits and executes the definition, validates typed responses, routes failures,
and owns occurrences and provider sessions.

```text
IMPLEMENT → evidence → REVIEW → ADJUDICATE
  no directive → FINAL SEMANTIC ASSESSOR
  open directive → REPAIR → renewed evidence → fresh REVIEW → RESOLVE
    resolved → FINAL SEMANTIC ASSESSOR
    open → repeat, at most three repairs → obligations_exhausted
```

Every required execution error routes to non-success. In particular, an unusable
`round_complete` exits the loop and reaches an error guard before the final
assessor. Missing/malformed/default responses remain Zeroshot validation errors;
Broodling does not validate or bless processor responses.

## Authorized deviations from historical W3

The user's #17 boundary decision followed the retained
[preflight counterexamples](../../qualification/v1-p3/issue-17-preflight.md).
It authorizes these bounded corrections without rewriting historical W3/G1:

1. **Structural generation:** remove `candidateGeneration` from state and every
   binding. B1 is C0; each successful mutating occurrence creates the next
   candidate interval by graph/runtime order. Mutators return typed null
   completion and cannot assign a generation. No product counter, digest,
   observer or candidate identity service replaces the ordinal.
2. **Required-control failure:** stop the loop on a `round_complete` error and
   fail before the post-loop final route. This adds no worker, authority role,
   retry or scheduler.
3. **Semantic content:** reviewers return required `findingContent`, bound only
   as observations for designated authority. The initial adjudicator returns
   required `directiveContent` with directive and correction text. Only its
   designated signal opens the obligation. Repair receives the frozen Contract,
   open-obligation signal and adjudicated content, with access to the current
   candidate in its worktree. It receives no raw findings or private rationale.
   Resolution reads the sticky content and can explicitly resolve the obligation;
   it does not overwrite the content. Mutations and later clean reviews cannot
   clear either the directive or the open signal.

The original executable roles, their step/verifier sandbox selections, single
execution sessions and finite repair bound of three are preserved. Signal enums
remain canonical routing authority. Payload text and forged identifiers cannot
retarget graph bindings. Semantic truth of an authority's content remains a
judgment concern, not a second response validator in Broodling.

The product uses a finite 300,000 ms per-provider timeout and one attempt per
node. This replaces the deterministic W3 fixture's 250 ms timing with W4's
real-provider bound. It changes no topology, authority, session or sandbox
assumption. The controlled test timeout cases explicitly shorten only the
selected node to 250 ms and record that deviation.

## Product submission and provider profile

`SubmissionCoordinator.prepare_assurance(attempt_id)` derives the Contract from
the Attempt's immutable admitted revision, constructs the graph/runtime and
initial empty assurance state internally, then freezes them through the existing
P2 request boundary. `submit_assurance(attempt_id)` dispatches that request and
uses the same correlation/replay path. Neither method accepts graph, runtime or
initial-state overrides. A prior arbitrary P2 request conflicts rather than
silently becoming the product protocol.

The explicit P2 `prepare`/`submit` methods remain available for their original
opaque integration scope. They cannot replace a frozen P3 request. First
dispatch still requires clean B1 and exclusive current worktree ownership;
post-dispatch owned mutation does not reimpose B1 or create another run.

The product path requires `QualifiedCodexProfile(real_codex, profile_home,
isolated_codex_home)` on `ZeroshotSubmitter`. The host supplies the pinned Codex
0.153.4 executable, an empty HOME, and a separate authentication-only CODEX_HOME
containing `auth.json`, all outside the candidate. No authentication bytes are
copied into the request. The first dispatch validates this initial profile.
Subsequent replay permits provider-owned runtime files created after dispatch.

The bundled executable applies the already-qualified W4 settings: retain the
sidecar's sandbox selection, ignore user config/rules, execute ephemerally,
force sandbox network off, and select the isolated homes. It forwards stdin and
the runtime's response-schema/model arguments unchanged. It does not inspect
prompts, route work, manage sessions or read runtime history. Nonsecret profile
paths and the launcher's code-integrity hash are part of frozen submission
identity; this is runtime configuration identity, not a candidate seal.

## Issue boundary

Issue #17 integrates the graph mechanics and profile foundation. The evidence
node still exposes the qualified availability signal; #18 owns Contract-derived
evidence production/selection/bindings and the product reviewer contamination
controls. #19 owns public final-assurance observation and custody. No P3 custody
table, Work Unit disposition, abandonment/restart, effects or later-phase API
is added here.

Product tests execute the real pinned SDK/sidecar with controlled provider
leaves. They establish structural mechanics, typed bindings and faithful launch
arguments. They are not fresh real-model judgment or host-containment evidence;
those qualified profile limits remain explicit.
