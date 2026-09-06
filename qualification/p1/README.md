# P1 external SDK/sidecar qualification harness

This directory is the reusable harness delivered by Broodling issue #2. It submits the checked-in
custom `GraphSpec`, `RuntimePlan`, and initial input through the official async Python SDK to its
matching Rust sidecar and a local single-host controller. It does not select Broodling's product
language or architecture, qualify Q1–Q7, pass either G1 gate, evaluate semantic quality, or prove a
Work Unit successful.

## Fixture

The exact submitted values are [graph.json](fixtures/graph.json),
[runtime.json](fixtures/runtime.json), and [initial-input.json](fixtures/initial-input.json). The
[role inventory](fixtures/roles.json) designates `adjudicate_authority` and
`final_assessment_authority` as authority-bearing occurrences for future witnesses; all other
roles are ordinary. That designation is fixture metadata because Zeroshot's graph wire format does
not assign Broodling semantic authority.

The default controlled route is:

```text
implement
  -> review(found) -> adjudicate_authority(repair) -> repair
  -> review(clean) -> adjudicate_authority(advance) -> round_complete
  -> final_assessment_authority(accepted) -> fixture_complete
```

`repair-then-accept`, `clean`, and `gap` provide controlled successful leaf results. A scenario of
`crash:NODE` or `malformed:NODE` provides a controlled unusable provider result. `--interrupt-at
NODE` hangs that leaf after the native execution becomes active, observes the point through the SDK,
and calls the SDK's durable force-stop operation. The loop bound of three exposes repeated
occurrences without claiming any sticky-obligation or routing qualification owned by Q3.

Only [bin/codex](bin/codex) is doubled. It is a test-only Codex JSONL process adapted from the
pinned Zeroshot local CLI fixture. The official SDK's encoding and subprocess transport, Rust
preflight and admission, source resolution, controller, graph execution/reduction, and durable
status/result paths remain real. The driver records prompt digests and controlled provider
invocations, not prompt or credential values.

## Reproduction

The recorded run used Zeroshot source
`d0909615d6ba3c179b58bce15a059f40400ec995`, Rust/Cargo 1.97.0, Python 3.13.5, and `uv` on Linux
x86-64. The source checkout must be clean and at that exact commit. These commands build the real
sidecar and place it into the official SDK's platform wheel; the development version is a build
label, while the source revision and hashes in [run-record.json](evidence/run-record.json) are the
exact build identity.

```bash
cd /home/faviann/repos/zeroshot
test "$(git rev-parse HEAD)" = d0909615d6ba3c179b58bce15a059f40400ec995
/home/faviann/.cargo/bin/cargo build -p zeroshot-rust --bin zeroshot-rust

P1_ROOT=$(mktemp -d /dev/shm/broodling-p1.XXXXXX)
env ZEROSHOT_RUST_BINARY=/home/faviann/repos/zeroshot/target/debug/zeroshot-rust \
  ZEROSHOT_PYTHON_VERSION=0.1.0.dev0 \
  uv build --wheel --out-dir "$P1_ROOT/dist" sdks/python
uv venv "$P1_ROOT/venv"
uv pip install --python "$P1_ROOT/venv/bin/python" "$P1_ROOT"/dist/*.whl

mkdir "$P1_ROOT/workspace"
git -C "$P1_ROOT/workspace" init
git -C "$P1_ROOT/workspace" config user.name "Broodling P1 Fixture"
git -C "$P1_ROOT/workspace" config user.email "broodling-p1@example.invalid"
git -C "$P1_ROOT/workspace" config commit.gpgsign false
touch "$P1_ROOT/workspace/seed"
git -C "$P1_ROOT/workspace" add seed
git -C "$P1_ROOT/workspace" commit -m seed
git -C "$P1_ROOT/workspace" branch -M qualification/p1
git -C "$P1_ROOT/workspace" remote add origin \
  https://github.com/example/broodling-p1-fixture.git

cd /home/faviann/repos/broodling
"$P1_ROOT/venv/bin/python" qualification/p1/run_harness.py \
  --workspace "$P1_ROOT/workspace" \
  --state-dir "$P1_ROOT/native-state" \
  --evidence "$P1_ROOT/evidence/repair-run.json" \
  --submission-key broodling-p1-repair-run-YYYYMMDD

"$P1_ROOT/venv/bin/python" qualification/p1/run_harness.py \
  --workspace "$P1_ROOT/workspace" \
  --state-dir "$P1_ROOT/native-state" \
  --evidence "$P1_ROOT/evidence/stop-run.json" \
  --submission-key broodling-p1-stop-run-YYYYMMDD \
  --interrupt-at final_assessment_authority
```

The SDK receives an explicit environment mapping. `OPENAI_API_KEY` is a literal test-only
placeholder used only to satisfy the admitted provider binding; the controlled executable does not
contact a provider. `PATH`, the scenario, and the driver-state directory are the other relevant
inputs. No secret value is collected. The disposable workspace may be removed after the JSON
evidence needed by a witness has been retained.

## Checks

From the Broodling repository:

```bash
python3 -m unittest discover -s qualification/p1/tests -v
python3 qualification/p1/run_harness.py --help
git diff --check
```

The unit checks validate fixture agreement and the controlled leaf only. A successful acceptance
check requires the external command above; replacing the real sidecar with the Python SDK test fake
does not qualify this harness.

## Issue #3 result

The issue-scoped [Q1/Q2/Q6 report](issue-3-q1-q2-q6.md) and
[retained run record](evidence/issue-3-run-record.json) record the executed external qualification.
Q2 passes. Q1 and Q6 are blocked by the absence of a supported completed-occurrence recovery
surface, so G1-core remains blocked and issue #3 remains open.

## Issue #4 result

The issue-scoped [Q3 report](issue-4-q3.md), [qualification driver](issue4_qualify.py), and
[retained run record](evidence/issue-4-run-record.json) qualify fail-closed routing and sticky
within-run obligations through the real SDK/sidecar path. Q3 passes. G1-core remains blocked only
as already recorded by issue #3 Q1/Q6; issue #4 does not attempt to resolve that dependency.

## Issue #5 result

The issue-scoped [Q4/Q5 report](issue-5-q4-q5.md),
[qualification driver](issue5_qualify.py), and
[retained run record](evidence/issue-5-run-record.json) qualify the selected local profile's
trusted applicability handoff and reviewer automatic-context boundary. Both Q4 and Q5 are blocked
by concrete Zeroshot/profile integration dependencies. The Q5 context witness used the real Codex
CLI/provider; Q4 controlled only the provider leaf. Issue #5 remains open, and this qualification
does not attempt to resolve issue #3 Q1/Q6 or begin a later phase.

## Issue #6 result

The issue-scoped [Q7 report](issue-6-q7.md),
[qualification driver](issue6_qualify.py), and
[retained run record](evidence/issue-6-run-record.json) qualify exact trusted-effect coordination
through the real SDK/sidecar path. Q7 and G1-effects are blocked: the only trusted effect bindings
are bundled PR/merge delivery, and the runtime rejects a generic exact-effect worker. The live
witnesses confirm fail-closed in-run ordering but cannot execute or reconcile either narrower
fixture intent without widening its authority. No production GitHub mutation was attempted with a
valid credential, and this qualification does not begin P6 or resolve the G1-core dependencies.
