# Issue #79: ready for separate R01 live authorization

**Prepared through the last safe point before admission. R01–R08 remain
NOT_STARTED. No live authorization, admission, Attempt, dispatch, provider task,
native PR effect, disposition or trial judgment occurred.**

The [pre-admission verification](pre-admission-verification.json) records current
readiness. The original #81 setup package remains immutable; this new directory
holds the execution handoff and a byte-identical copy of its input records and
zero-start allocation. The original prepared durable store is reused.

| Prerequisite | Verified result |
| --- | --- |
| Corrected v5 freeze | `52eb3569b3671baa37426792a67b50058e2d223f`; protocol bytes unchanged |
| Compatible baseline | `9c799de1b5cfff16082e65531933e7a249544282`; product/dependency/test/config identities unchanged; retained 351 tests and 267 subtests remain applicable |
| Dependency | SDK `10.3.0.post1`, all 15 installed package files and native `10.3.0` match the pinned wheel |
| Disposable repository | Private `faviann/broodling-p5-v3-20260917`, exact repository node identity, no PRs |
| Issue and authority | Open issue #1, exact frozen R01 body; entitled source and criteria-only Contract authorize one native PR to `p5-eval` |
| B1 | `884bd64264df1515bee76a63f548db9cabe25a35`, both remote `p5-eval` and clean local two-file fixture |
| DirectTarget | Fresh `broodling-p5-v5-r01-target` running at `http://127.0.0.1:18767`; pinned image/native binary, Codex `0.153.4`, native discovery and empty inventory verified |
| Gateway | Exact `https://cliproxy.local.faviann.com/v1`; current key authenticates read-only `/models` metadata and selected `gpt-5.6-sol` is listed; target DNS/TLS verified |
| GitHub delivery | Current `GH_TOKEN` authenticates as `faviann`, has `repo` scope and push/admin permission; target branch is unprotected; GitHub HTTPS reachable from target |
| Retention and stop | Fresh target state/home separated from historical state and judge material; approximately 75 GiB available for state/Docker and 831 GiB for repository evidence; Docker stop access available |
| Counting boundary | Zero admission/Attempt/submission/result/disposition rows; `P=8`, all other counts zero |

The rulesets endpoint reports that the current private-repository plan does not
enable that feature. The branch readback reports no protection. No GitHub write
was used to test delivery; actual native delivery remains the purpose of R01.

Evidence details: [authority and baseline](authority-verification.json),
[target](target-verification.json), [target network](target-network-verification.json),
[gateway credentials](gateway-credential-verification.json),
[GitHub credentials](github-credential-verification.json),
[copied-input provenance](input-provenance.json). Credential values are absent
from these records. Gateway access here was metadata only, with no inference or
provider task.

The [final readback](final-checks.json) still has zero admissions, Attempts and
target runs. The [driver validation](tool-validation.json) records six passing
offline safety checks, independent API/boundary review and the passing
[read-only preflight](read-only-preflight.json). `PREPARATION-SHA256SUMS` seals this
pre-admission snapshot; the new directory's slot accounting changes only after
separate authorization. The original #81 package remains unchanged throughout.

## Exact handoff

From `/home/faviann/repos/broodling`, the following only repeats read-only checks:

```bash
.venv/bin/python evaluation/p5/v5/run_r01.py check
```

After **separate owner authorization for the one R01 trial and active
supervision**, with the current three dispatch variables supplied ephemerally in
the shell environment, the exact live action is:

```bash
.venv/bin/python evaluation/p5/v5/run_r01.py start --authorize-r01
```

This command has **not** been run. It rechecks prerequisites, durably records
R01's start immediately before first Contract admission, then admits/provisions
one Attempt and dispatches the fixed v5 invocation. A recorded start cannot be
rerun or replaced. It never starts R02–R08. The historical v4 driver is obsolete.

External containment, if directed during live supervision:

```bash
docker stop broodling-p5-v5-r01-target
```

The target is deliberately left running with no runs or injected dispatch
credentials, ready for the separate authorization. Its fresh state and home are
under `/home/faviann/.local/share/broodling-p5-v5/`; the stopped historical v3
container and state remain preserved.

The [v5 handoff](../../v5/handoff.md) describes credential-free reconnect/stop
and the remaining exact-receipt judging and evidence obligations. Preparation
does not demonstrate native task quality, establish a live-boundary pass, or
unblock #68. No issue/comment was posted and no remote repository was changed.
