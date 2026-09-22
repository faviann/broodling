# #140 source retirement and release evidence

Scope: [#140](https://github.com/faviann/broodling/issues/140), based on PR150 merge
`da7e1560dc1eccedd32874124783d897e98cad5e`, on 22 September 2026. This is source
retirement and release/guidance adaptation, not operational cutover or another
behavioral slice.

## Start gate and retained history

All eight behavioral slices #132–#139 were closed. The fresh independent whole
migration review passed both axes at
`5a1f01cd15ad32d5eec028be19c5b9b97d9af903`: Standards COMPLETE/PASS with two
dispositioned nonblocking P3 preferences; Spec COMPLETE/PASS with zero actionable
findings. The evidence-only commit was
`51e35ebe15996fcbfb53f3f4b6279b9ffa3b0860`, merged by PR150. See the
[actual combined gate comment](https://github.com/faviann/broodling/issues/130#issuecomment-5783095900)
and [review record](130-migration-review.md). Retirement does not reopen that
passed review or use it to authorize new behavior.

The [parity map](130-parity-map.md) still resolves Python responsibilities and
witnesses against frozen `b3f61a96c40401722ec16fc361958d1690982e02` (tree
`7e061c9314d16785c070483799d0e8057a42ba31`). The
[baseline record](130-baseline-validation.md) distinguishes exact-baseline runs
from spike/later runs. The unchanged Python 404-pass run in 113.37s before
retirement belongs to the integrated PR150 candidate, not a new baseline claim.
The separate executable baseline checkout was untouched. Retired source/tests
remain in Git history; no duplicate application is kept as a current test lane.

## Retired and retained assets

Retired 82 tracked paths: the Python `broodling/` application/domain/store/
lifecycle and launcher, `pyproject.toml` packaging/entrypoint, root pytest setup,
Python tests/crash/property helpers and Python schema fixtures, and superseded
`deployment/install.py`, `check_target.py` and `reviewed_issue.py`.
The installer's host checks (x86-64, non-root account, Python 3.13, SQLite
3.37+, local rootful Docker socket) were dropped deliberately rather than
rebuilt: SQLite now ships with `Microsoft.Data.Sqlite`, and the remaining host
prerequisites are operator-owned in the [release guide](../../deployment/README.md#build-a-release-artifact).
`TargetReadiness` keeps the target-side checks.
Each deletion targeted an enumerated tracked file after checking .NET references.
Virtual environments, caches, other worktrees, untracked user files and operational
state were not retirement targets.

The only production Python file is
[`src/Broodling/bridge/zeroshot_bridge.py`](../../src/Broodling/bridge/zeroshot_bridge.py):
one-call version/submit/wait/stop translation to the official SDK. It owns no
Broodling policy, application store, lifecycle or recovery. The new
[`requirements.txt`](../../src/Broodling/bridge/requirements.txt) preserves the exact
official Linux x86-64 wheel URL and SHA-256 from the retired package metadata.
It is copied with the bridge on ordinary build/publish. Zeroshot **10.3.0**,
SDK **10.3.0.post1** and Codex **0.153.4** remain unchanged.

Retained controlled Python SDK/provider fixtures are used by the .NET tests,
including `tests/fixtures/software-change-codex`, three SDK/transport `.py`
fixtures and the inspect/slow provider executables. The retained issue snapshots
are still linked by the .NET test project. All six authentic .NET schema fixtures
and their provenance are unchanged. The C# readiness operation retains its short
existing-target Python UID-transition probe. None is a second Python Broodling
application. The pinned DirectTarget Dockerfile remains the target dependency
recipe; no image was built or deployed.

Current README, governing/agent/test/operator guidance and seam references now
use .NET APIs/commands. Old Python API examples are replaced by current seam
pointers; historical links are pinned to their revisions. Earlier slice results,
#77 deployment validation and P5 remain dated evidence. Current .NET schema is 7.
Implementation guidance in the bodies of #100, #104, #105, #110, #113, #117,
#119 and #126 was updated for .NET without changing product intent or dependency
relationships; [#100](https://github.com/faviann/broodling/issues/100) remains the
specification for its deferred behavior.

## Validation of the retirement candidate

The C# application and test sources are identical to `da7e156`. The only project
change copies `bridge/requirements.txt` to build and publish output. Commands used
SDK 10.0.401 with the environment in [tests/README.md](../../tests/README.md). The
bridge Python was a fresh virtual environment installed only from
`src/Broodling/bridge/requirements.txt`, which resolved SDK 10.3.0.post1.

| Check | Observed result |
| --- | --- |
| `dotnet test --solution Broodling.sln`, first candidate run | 338 passed, 3 failed (below) |
| Same command, final candidate with fresh bridge environment | 339 passed, 2 failed (the lock cases below), 0 skipped |
| `dotnet build Broodling.sln --configuration Release` | 0 warnings, 0 errors |
| Host and launcher Release publish, archive, extracted-package smoke | Passed |

The intermittent failures are
`ProvisioningProcessTests.GitNulRefusalPrecedesSpawnAndPreservesEnclosureLock`
(both variants, line 186 `LockIsFree`) and
`GitHubAdmissionTests.RetainedRealIssueFixturesAdmitOrPreserveTheirUnsatisfiedPrerequisiteWithoutExecution`
(`GitHubSourceError` at `GitHubIssueSource.cs:65`, underlying `ETXTBSY`). Other
candidate runs passed 341/341 three times and failed one or two lock cases.

**They are not caused by this retirement.** Small harnesses loaded the
unchanged Debug test assembly built from clean `da7e156` and ran the real test
method or acquisition seam while 16 threads repeatedly launched `/bin/true`. The
lock test failed at line 186 in 3 of 3 runs, and acquisition failed with
`ETXTBSY` then line 65 in 3 of 3 runs. The same acquisition without concurrent
launches passed 100 of 100 calls. Nothing in the test suite reads the retired
Python paths at run time. The failure and its likely descriptor-inheritance
mechanism are tracked in
[#151](https://github.com/faviann/broodling/issues/151); this issue does not
repair them. The earlier unexplained acquisition and Git observations in the
[G record](../implementation/dotnet-receipt-completion.md) and
[migration review](130-migration-review.md) may share that cause.

The framework-dependent apphost could not discover this host's Nix .NET
installation (exit 131, before state creation). The documented command therefore
uses `dotnet /RELEASE/host/Broodling.Host.dll`; direct apphost use requires a
registered runtime or an appropriate `DOTNET_ROOT`.

## Release package and operational boundary

The [release guide](../../deployment/README.md#build-a-release-artifact) publishes
both complete directories and archives them with the source revision. The local
smoke artifact was built locally from the retirement candidate; it is not a
released build. An extracted copy verified:

- Host/launcher assets, bridge plus dependency pin, native Git shim, SQLite
  native assets, executable modes and the complete self-contained launcher runtime.
- Matching host/launcher `Broodling.dll` SHA-256
  `4912e90d92679a00b0c86ec5c9409473836f9f997c2eaf08e6677de881a1a967`.
- CLI fresh schema-7 initialization, same-identity reopen/explicit current upgrade,
  repeated-initialization refusal, empty retained history and command usage routing.
- Published shim loading without `LD_LIBRARY_PATH`, using the existing test-only
  process caller added only to the extracted smoke copy, not the package.
- Published bridge SDK/native version response through isolated Python.
- The self-contained launcher executing the controlled provider under a minimal
  environment without `DOTNET_ROOT`, preserving PID/stdin and applying local policy.

No real Docker, target, forge, gateway or provider operation, native dispatch,
deployment or operational cutover occurred in this packaging smoke.
Source/release support is not a validated live .NET deployment. Before any
operational switch, the owner must choose drain or explicit abandon-and-retain
for existing Python work, preserving paths/source Git/native/target/receipts;
see the [cutover gate](../../deployment/README.md#existing-python-work-and-the-operational-switch).
Abandonment grants no cleanup authority. There is no import, takeover, silent
replacement or state deletion.

P5 remains scoped FAIL with independent human review of each exact accepted
revision. No-effect stable success still refuses. Dispatched Attempts remain
permanently quarantined. The trusted-host local policy remains required; no
automatic merger, execution supervisor, HTTP intake, automatic progression or
other remaining #100 behavior was added.
