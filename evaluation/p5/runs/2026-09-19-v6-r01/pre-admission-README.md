# Issue #83: last safe point before v6 R01 admission

**Prepared for separate owner authorization; no live execution authorized.**
All v6 slots remain `NOT_STARTED`, with `P=8; S=0; D=0; U=0; A=0; J_A=0`.
No start marker, admission, Attempt, submission, provider task or PR exists.

[Pre-admission verification](pre-admission-verification.json) records readiness
and references the [fresh input/baseline package](../2026-09-19-v6-setup/README.md).
[Read-only preflight](read-only-preflight.json) is the selected v6 driver's actual
check result. [Final checks](final-checks.json) retain the zero-start boundary.

The actual running container is
`6dcb51921a9b6a71e3275429c2459a735457860745aa1d1164330364c2953d6d`, named
`broodling-p5-v6-r01-target`, at `http://127.0.0.1:18768`. It uses exact corrected
image `sha256:3511d1b7134167b6a845bdc0536532a2265cba580e77b475e26d25d0382f4e5a`.
Its only mounts are fresh v6 `target-state` and `target-home`; no credentials,
prior outputs, host checkout or judge material were mounted/injected.

[Target verification](target-verification.json) records `/usr/bin/gh 2.101.0`
and `api_paginate_slurp=true` using the shared verifier inside that container,
native `10.3.0`, Codex `0.153.4`, Node `v22.23.2`, discovery,
standard PR preset and empty inventory. SDK `10.3.0.post1` is verified in the
setup dependency record. [Network checks](target-network-verification.json)
establish container DNS/TLS and gateway/GitHub reachability without credentials.
[Gateway metadata](gateway-credential-verification.json) and
[GitHub authorization](github-credential-verification.json) verify current
ephemeral credentials without retaining values or running inference. Actual
native PR delivery and task quality remain untested.

The owner has current Docker access and can contain the target with
`docker stop broodling-p5-v6-r01-target`. Active supervision must accompany the
later separate authorization; preparation cannot attest future owner presence.
Quarantine capacity and exact-revision judging/retention arrangements are in the
setup operational record. The future driver enforces completed-result consumption
before first disposition and verifies reopened-store replay without execution.

The [handoff](../../v6/handoff.md) contains the safe `check` command and the exact
action that would start/count R01, **only after separate owner authorization**:

```bash
.venv/bin/python evaluation/p5/v6/run_r01.py start --authorize-r01
```

Run from `/home/faviann/repos/broodling` with current ephemeral credentials and
active supervision. Start repeats preflight before the counting/admission boundary.
Do not use `finalize` as a poll or rerun `start` after a start marker exists.

Historical before/after fingerprints match for tracked v1-v5 files, every regular
v5 state file, the stopped v5 container and partial PR #2. V5 remains consumed
`NOT_DELIVERED / IF`, `P=8; S=1; D=1; U=1; A=0; J_A=0`; it is not pooled with v6.
R02-R08 remain gated. P5 readiness is still NOT REVIEWED.
