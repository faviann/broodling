# Issue #67: monitored v3 setup, blocked before R01 admission

**Disposition: BLOCKED before admission. R01 remains NOT_STARTED.** The prepared
host, disposable GitHub fixture, source and Contract exist, but prerequisite 4 of
`p5-native-pr-v3` cannot be satisfied through the unchanged product's provider
credential path. No live trial, provider task, native delivery, receipt or
Broodling disposition occurred. This is a concrete setup finding, not an observed
live task failure or a P5 readiness verdict.

Authority: [current governing document](../../../../docs/governing/current.md),
[parent #62](https://github.com/faviann/broodling/issues/62),
[issue #67](https://github.com/faviann/broodling/issues/67), and protocol
`p5-native-pr-v3`, frozen at `de1da18479fdf846d04994190132357fcb0a87ef`.
The execution checkout was updated from `b9cacd99beb9a297966e68e6047c7663aca9b4c6`
to `b8474c4220a6b0a289d954578b9d7c8550de0e64` before setup. The product baseline
remains `b901367b339c3ca715fa51ad91646b499a8fa068`.

## Concrete blocker and evidence

The installed Zeroshot 10.3.0 materializer expands Broodling's selected
`codex/openai/gpt-5.6-sol`, effort `low`, size `small`, execution-scoped PR runtime
to require `openai: [OPENAI_API_KEY]` for every agent. The current
`ZeroshotSubmitter._submission_client` passes an explicit SDK environment with
cleared operating variables, PATH and the delivery credential GH_TOKEN. It does
not supply OPENAI_API_KEY or CODEX_API_KEY. An ambient non-secret sentinel confirms
that the SDK suppresses an ambient provider key on this path.

The pinned native source forwards exact supplied connections without a resolver;
hosted Codex has no local-user authentication fallback. The installed binary
also refuses `connection list` on this DirectTarget with `direct target does not
advertise connection management`. An available host/isolated Codex ChatGPT login
therefore cannot satisfy this invocation. Merely supplying an operator API key
would not fix the absent product credential route. Injecting secrets into the
public target mapping would persist them in the frozen invocation and is not an
acceptable setup workaround.

Reproducer: [verify_provider_auth.py](../../v3/verify_provider_auth.py).
[Provider verification](provider-auth-verification.json) retains the installed
native expansion, non-secret environment probe and immutable upstream source
links. [Baseline verification](baseline-verification.json) contains an independent
source crosscheck. `--validate-only` returned `valid: true`; that validates the
runtime declaration, not authentication. No actual target submission or rejection
was induced. Full binary/source equivalence was not established; the known serve
help difference is disclosed in the probe. The critical connection declaration
and DirectTarget connection-management refusal were verified in the installed
binary itself.

[Follow-up #69](https://github.com/faviann/broodling/issues/69) records the narrowly
scoped credential-routing defect and reproducer. #67 authorizes evaluation-only
tooling, not a production repair, provider substitution or alternate execution
path. No first admission was attempted to rediscover this known missing prerequisite.

## Setup actually completed

| v3 prerequisite | Observation |
| --- | --- |
| 1: compatible baseline | PASS, reused B01: 330 tests passed, zero skipped, exit 0. Production/test/dependency trees, installed package versions and all 15 SDK/native wheel files match. No suite rerun was needed. |
| 2: exact authorized disposable target | PASS: private `faviann/broodling-p5-v3-20260917`, `p5-eval` at original B1 `884bd64264df1515bee76a63f548db9cabe25a35`; final GitHub readback still matches and there are no PRs. |
| 3: real product inputs | Source/issue/Work Unit/immutable criteria-only Contract prepared. No Attempt/worktree was admitted because prerequisite 4 failed. Dedicated durable workspace root reserved. |
| 4: DirectTarget/current credentials | BLOCKED on provider route above. Real pinned native DirectTarget started and discovery/SDK inventory succeeded with zero runs. GitHub setup credential exercised real repository creation, B1 push and issue creation; `admin`/`push` readback retained. This does not prove native delivery. |
| 5: active stop capability | Owner explicitly authorized setup and supervised this session with stop capability. Root container bound only to host loopback, controlled through Docker; external stop completed. No P5 time/dollar ceiling imposed. |
| 6: independent judging | Frozen judge/reference excluded from source and target mounts. No accepted revision exists, so no live judgment was performed. Existing frozen evaluator and retained calibration remain available. |
| 7: evidence | Canonical inputs, exact relevant database readbacks, B1 bundle and verified restoration retained here; consistent SQLite backup retained outside the target. No credential values retained. |

The fixture issue is
[R01 / T1 / repetition 1](https://github.com/faviann/broodling-p5-v3-20260917/issues/1).
[Setup identities](setup.json), [GitHub identity](github-repository.json),
[input IDs](R01/input-records.json), [exact issue bytes](R01/issue-body.md),
[entitlement](R01/source.json), [Work Unit](R01/work-unit.json), and
[canonical Contract](R01/contract.json) retain the real records. Its only effect is
`RequiredEffect("deliver-pr", <frozen delivery text>, "pull_request", "p5-eval")`.
No hidden checks or reference implementation entered the issue or Contract.

Automated setup used [setup_r01.py](../../v3/setup_r01.py). The first invocation
created the repository, then hit GitHub's HTTP 409 for a new empty repository's
missing branch. The helper was corrected to recognize that specific empty-repo
response; [initial failure](setup-first.txt) is preserved. The
[second invocation](setup-second.txt) completed setup, and the
[repeat invocation](setup-repeat.txt) verified identical records. These were
setup retries before admission and consumed no slot.

The execution host is the existing Linux x86-64 host. Its administrative Python
3.13.5/SQLite 3.46.1/SDK 10.3.0.post1 profile matches B01. The isolated target used
the bundled native 10.3.0 binary, Codex 0.153.4, Git and gh in the
[Dockerfile](../../v3/DirectTarget.Dockerfile). Actual build/image/container
identities, versions, mounts, loopback discovery and empty SDK inventory are in
[build output](target-build.txt) and [target verification](target-verification.json).
The readable build log normalizes line endings/trailing whitespace; exact original
bytes remain in [the compressed raw log](target-build.raw.txt.gz).
Only dedicated target state and a fresh target home were mounted; no Broodling
checkout, judge, reference, other trial or Docker socket was mounted.

The copied authentication file was handled without reading/logging its values.
After verifying no run existed, `docker stop broodling-p5-v3-r01-target` stopped
the unused target (retained container exit 137); the isolated auth copy was
removed. The original host auth was untouched. Container and target state remain
retained. [Completion checks](completion-checks.json) preserve this readback.
This external containment grants no Broodling cleanup authority.

## Counts, unobserved claims and disposition

`P=8; S=0; D=0; U=0; A=0; J_A=0; A−J_A=0`. All R01–R08 are NOT_STARTED in
[slots.json](slots.json). All primary live class counts CO/FA/UR/LR/IF/CG/I are
zero because no slot started; the setup blocker is not assigned a live CG/IF.

Descriptive sample observations: S/P = 0/8, CO/P = 0/8; CO/S, UR/S, FA/J_A and
FA/A are N/A. These are not quality or reliability estimates. Real-provider
quality, correction effectiveness, edit churn, live latency and spend are
unobserved. No evaluation provider task was invoked; billing was not measured.
Setup timestamps are retained, not presented as trial latency.

There is no frozen submission, key, run ID, result, receipt, PR head, stable
accepted revision, disposition or judgment to retain. Detached/completed-result
reconnection and atomic receipt-backed disposition remain unobserved live claims.
[Store readback](store-readback.json) confirms one Work Unit/source/Contract and
zero admissions, Attempts, workspace assignments, submissions and dispositions.
No Attempt is quarantined because none was dispatched. Every future dispatched
Attempt still remains ineligible for automatic deletion or replacement.

Settle #67 as **BLOCKED before admission**, with its requested concrete evidence
package completed. #68 remains gated: no R02–R08 dispatch is justified by this
record. #66 can review this settled incomplete v3 package. #62 remains open,
P5 **NOT REVIEWED**; no positive authorized-PR readiness claim is supported.
After separately authorized repair and compatible verification, a successor may
consider the still-unstarted R01. Preserve this blocked result as provenance.

## Reproduction and provenance

Run the authentication probe with the supported environment:

```bash
.venv/bin/python evaluation/p5/v3/verify_provider_auth.py
```

The command uses no real credentials, no admission and no run submission. The
setup command and its effects are documented in [setup notes](../../v3/setup.md).
It verifies existing frozen identities rather than resetting them. Do not
interpret successful setup or `--validate-only` as dispatch readiness.

[b1.bundle](b1.bundle) retains the original objects; [archive verification](archive-verification.json)
records its SHA-256, successful bare clone and exact B1 `tiny.py` hash, plus the
consistent external SQLite backup. `store-readback.json` supplies the exact
relevant rows without requiring access to that host archive. There is no result
revision to reconstruct or judge.

The entire v1/v2 corpus, protocols, judge and
[prior blocked package](../2026-09-17-v2-preflight/README.md) remain byte-for-byte
unchanged. Historical #63/#64/#65 are neither reopened nor relabeled. The original
#62 and #67 snapshots are retained beside this report. No production, test,
dependency, model, task criterion or frozen protocol change was made.
