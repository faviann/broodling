# Issue #5 — Q4/Q5 applicability-handoff and reviewer-context qualification

**Qualification date:** 6 September 2026 (UTC)

**Scope:** Broodling issue #5, Q4 and Q5 only

**Governing inputs:** target v0.3 §§2.3, 3.4, 7.3–7.6, 8, 13–14 and implementation plan
v0.3 §§2 F6, 3 S2, and 4 P1

**Integration:** official async Python SDK `LocalTarget` → matching unpatched Rust sidecar →
local one-run controller

**Verdict:** **Q4 BLOCKED; Q5 BLOCKED; G1-core remains BLOCKED**

Neither missing capability was hidden behind a Broodling validator, dispatcher, node/session
manager, private ledger reader, or asynchronous rerouting step. The blockers are concrete
Zeroshot/profile integration dependencies for G1-core and therefore for P3.

## Exact tested build and evidence

Zeroshot was clean at
[`d0909615d6ba3c179b58bce15a059f40400ec995`](https://github.com/the-open-engine/zeroshot/tree/d0909615d6ba3c179b58bce15a059f40400ec995)
(tree `03b9b027f9a9dd0e03cc827aae0e95dd3e8f2b4d`). The official SDK wheel and matching
sidecar were built unpatched from that checkout.

| Item | Exact value |
|---|---|
| SDK distribution/version | `zeroshot-rust 0.1.0.dev0` |
| wheel SHA-256 | `153d19c2ea1b5f6a58cd06a20d6c4003071ced0669c3155a94b476a3bb2dd9ae` |
| sidecar / SHA-256 | `zeroshot-rust 0.1.0` / `9481e60ddcab0762468f4182e8657570196555010918df5397f2dc20321f9b86` |
| Python / platform | `3.13.5` / Linux x86-64, glibc 2.41 |
| real Q5 provider CLI/model | `codex-cli 0.153.4` / `gpt-5.6-sol`, low effort |
| controlled leaf SHA-256 | `24d59cf73454b3b6842477cb0465ecfd4589764721ab5ba64943c08079e9df1e` |
| issue #5 harness SHA-256 | `63c97db5645dc56bf0ddeb2bba9bbb81db2bb8a09d8bfdef1928ff4bd044e542` |
| full run record SHA-256 | `9292e6659873db89ee091b79715e7f5628865a3741cfa058e1ad6c3aaefc5f64` |

The complete submitted graphs/runtimes, run IDs, source projection, durable terminal results,
authority inputs, workspace observations, timing, build identity, and real-provider Q5 output are
retained in [`evidence/issue-5-run-record.json`](evidence/issue-5-run-record.json).

The Q4 provider leaf was controlled. SDK transport, Rust request validation and admission, Git
source resolution, controller, graph execution/dataflow/routing, workspace access, and durable
result were real. The Q5 context witness used the installed real Codex CLI and configured OpenAI
provider; no provider component was doubled. No secret values were retained.

## Q4 — BLOCKED

The selected local path captures repository, branch, and exact `HEAD` once while preparing the
submission, then gives every node the same mutable user-owned workspace. See
[`native_v2_local.rs` lines 71–99](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_local.rs#L71-L99)
and
[`native_v2_local.rs` lines 198–224](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_local.rs#L198-L224).
The provider prompt receives authored instructions, graph-bound JSON, and the response contract,
but no runtime-sealed current-workspace or raw-evidence identity
([`response.rs` lines 445–465](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_runner/response.rs#L445-L465)).

The issue fixture placed the starting candidate digest, Contract ID, evidence ID, and predecessor
ID in trusted initial state. A processor then supplied claims which the graph bound directly into
the designated authority input. The runtime correctly enforced that typed dataflow; it had no
trusted current-byte/evidence fact against which to establish the claims' eligibility.

| Witness | Actual observation |
|---|---|
| unchanged candidate/evidence control | accepted; claimed and starting identities matched |
| candidate changed `candidate-v1` → `candidate-v2` after submission | accepted with the stale starting digest in authority input |
| forged source/Contract/evidence/predecessor IDs | accepted with all four forged values in authority input |
| required `evidence/raw.txt` deleted before authority | accepted; raw artifact was absent at terminalization |
| external observer deliberately delayed after processor completion | authority/run finished before the observer completed its current-byte digest |

The delay is the required real coordination counterexample: noticing completion through an
external Broodling observer supplies no graph dependency. Adding a Broodling callback that
dispatches or reroutes the next node would be the forbidden second scheduler, so it is not a
qualification solution.

**Q4 dependency:** Zeroshot needs a supported trusted in-run operation that seals the current
candidate and selected raw evidence, binds source/Contract/evidence/predecessor identities into
the authority occurrence's input, and synchronously gates dependent graph progression. It also
needs an explicit custody/availability contract for referenced raw artifacts. The operation may be
a narrow supported host capability; this result does not prescribe an API shape or source-manifest
format.

## Q5 — BLOCKED

There is one positive boundary property: the selected runtime uses `sessionScope: execution`, and
the runner opens a new session for every execution
([`state.rs` lines 165–180](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_runner/state.rs#L165-L180)).
Thus this profile does not intentionally resume a prior node or contaminated reviewer session.
The real reviewer also reported the separately placed worker/private narrative file as
`NOT_VISIBLE`; it was not automatically copied into the graph input.

That is insufficient for the required exhaustive boundary. The local adapter launches Codex in
the shared repository and assigns the current user's `HOME` and `CODEX_HOME` (defaulting to
`$HOME/.codex`), rather than a reviewer-specific controlled home
([`native_v2_local.rs` lines 210–225](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_local.rs#L210-L225)).
The provider command disables web search and selects read-only workspace access for verifiers, but
does not disable user/project config, repository instructions, skills, or the Codex tool surface
([`command.rs` lines 112–140](https://github.com/the-open-engine/zeroshot/blob/d0909615d6ba3c179b58bce15a059f40400ec995/zeroshot-rust/src/native_v2_codex/command.rs#L112-L140)).

The real Codex reviewer was given narrow graph JSON containing only the frozen Contract token,
source token, comparison base, selected raw-evidence token, and governed instruction. Separately,
the untracked workspace contained:

- an `AGENTS.md` repository-instruction canary;
- a `.agents/skills/q5-context-canary/SKILL.md` ambient-skill canary; and
- a file containing worker private reasoning, previous review, adjudication deliberation, and
  repair rationale canaries.

The structured real-provider output reproduced the repository-instruction and ambient-skill
canaries and acknowledged the automatic tool surface, despite none appearing in the graph-bound
input. The narrative canary remained `NOT_VISIBLE`, confirming that fresh execution scope avoids
one contamination path while failing to make the overall automatic context exhaustive. The
starting admitted `HEAD` did not identify any of these untracked context files.

**Q5 dependency:** the selected Zeroshot execution profile needs a supported context-control and
provenance contract that can disable or exhaustively admit project/user instructions, skills,
configuration, session history, and tools for reviewer nodes, while retaining only the governed
review context. A narrow JSON prompt and fresh provider thread alone do not establish that
contract. This is a profile limitation; the witness makes no hostile-agent OS/network/MCP
confinement claim.

## Disposition

Q4 and Q5 each fail required counterexamples, so both are **BLOCKED**, issue #5 must remain open,
and G1-core cannot pass. These blockers are additional to—not replacements or attempted fixes
for—the issue #3 Q1/Q6 completed-occurrence recovery dependency. No later phase, G1-effects work,
or Broodling product implementation was started.

## Reproduction

Build the pinned sidecar/wheel as documented in the shared [P1 harness](README.md), initialize a
short-path disposable Git workspace containing committed `candidate.txt` and
`evidence/raw.txt`, and run:

```bash
P1_ROOT=/path/to/the/P1-build-directory
ISSUE5_ROOT=$(mktemp -d /dev/shm/i5.XXXXXX)
WHEEL=$(find "$P1_ROOT/dist" -maxdepth 1 -name '*.whl' -print -quit)
"$P1_ROOT/venv/bin/python" qualification/p1/issue5_qualify.py \
  --workspace-root "$ISSUE5_ROOT/workspaces" \
  --workspace-template /path/to/the/disposable-git-template \
  --state-dir "$ISSUE5_ROOT/state" \
  --evidence-root "$ISSUE5_ROOT/evidence" \
  --output qualification/p1/evidence/issue-5-run-record.json \
  --zeroshot-source /home/faviann/repos/zeroshot \
  --wheel "$WHEEL"
```

The real Q5 run requires the installed `codex` CLI and an already configured OpenAI provider. The
script records version/path-independent provenance and canary output, never credential values.
