# V6 compatible baseline and setup requirements

This accompanies the frozen [v6 protocol](protocol.md). It records selected
provenance and requirements, **not completed v6 setup or live authorization**.
No v6 repository, task issue, Contract, Attempt, container or run was created by
this preparation. V6 R01-R08 remain NOT_STARTED; counts are
`P=8; S=0; D=0; U=0; A=0; J_A=0`.

## Selected baseline and evidence reuse

Select #82 commit **`da5db3167eda0ef458cf0875b5a1c64138637b9d`** as the
compatible product/dependency/test and evaluation-helper baseline. Its root Git
tree is `ee6c2e151f407a5a7155f818b49cf8a161b5bcc4`.

| Relevant identity | Git object ID at the selected baseline |
| --- | --- |
| `broodling/` tree | `e8fe4e2a25272d240a3406b51d5c77fa904da562` |
| `tests/` tree | `445fd3f04a01ad2e7667ad8733f4d77f11f095da` |
| `pyproject.toml` blob | `3269f76db05f2789b7fe89f27563607150b01882` |
| `conftest.py` blob | `6dd7826c7d2773ac2058887218b50499f8ea6600` |
| `evaluation/p5/verify_direct_target_gh.py` blob | `00eb7f2a2a56bbe4107ad8085a65cc038a1110ed` |

The compare from v5's `9c799de1b5cfff16082e65531933e7a249544282` baseline
contains no changes to `broodling/`, `pyproject.toml` or `conftest.py`.
The test tree does change: #82 adds the actual-start-boundary compatibility
regression. Therefore do not present the older 351-test run as the complete v6
suite. Reuse #82's [retained validation report](https://github.com/faviann/broodling/blob/da5db3167eda0ef458cf0875b5a1c64138637b9d/evaluation/p5/direct-target-gh-compatibility.md):
`.venv/bin/python -m pytest tests -q` reported **356 passed, 270 subtests**,
and the focused compatibility tests reported **5 passed, 3 subtests**, with no
live provider inference. This preparation inspected the source/report; it did
not independently rerun that suite or rebuild the image.

Before admission, record the actual checkout/helper revision and prove relevant
product/dependency/test/helper compatibility. Reuse the retained result only
while applicable; a changed relevant tree or unusable evidence requires a fresh
supported full-suite result with no missing SDK/native coverage. A small v6
helper/test adaptation must be validated and recorded before admission, not
silently labeled as the old tested tree. Documentation-only changes do not
require repeating historical campaigns. No unreviewed product/runtime change is
included in this successor.

## Corrected target selection

Use #82's validated Linux amd64 image:

```text
image ID: sha256:3511d1b7134167b6a845bdc0536532a2265cba580e77b475e26d25d0382f4e5a
label: broodling-p5-direct-target:zeroshot-10.3.0-codex-0.153.4-gh-2.101.0
```

The tag is a convenience, not the immutable identity. The image definition is
`evaluation/p5/v3/DirectTarget.Dockerfile` at the selected #82 commit. Its original
Node base digest is preserved; Node remains `v22.23.2`, Codex `0.153.4`, native
Zeroshot `10.3.0`. It installs official GitHub CLI `2.101.0` at `/usr/bin/gh`.
Package, executable and base-image hashes and offline positive/negative probes
are retained in the #82 report; do not edit that report or historical setup files.

Keep SDK `10.3.0.post1`, official wheel SHA-256
`f3629459837a27b7496f98fe0034e7b47a00079d93d3374922c2960952b8ace9`,
and native executable SHA-256
`afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06`.
Record the actual container ID, image ID, persisted origin and these runtime
identities. If the validated image is unavailable or rebuilt, retain a new
non-provider compatibility record before admission, demonstrating the same
pinned dependencies and explaining the changed image identity; do not silently
accept dependency/runtime drift.

Run the shared check against the **actual selected running v6 container** before
any execution-start marker, slot count, Contract admission or submission:

```bash
.venv/bin/python evaluation/p5/verify_direct_target_gh.py \
  --container "$V6_CONTAINER_ID"
```

Retain the installed version and `api_paginate_slurp=true`. The check invokes
`/usr/bin/gh --version` and
`/usr/bin/gh api graphql --paginate --slurp --help`; it sends no GitHub request
and performs no provider inference. A missing/failed capability check refuses
before the trial starts. Require the selected pinned CLI version as part of image
identity, not merely a newer workstation CLI or a cached successful image probe.
The future v6 start path must call this check itself. Preserve the existing
incompatible-CLI-before-counting/admission regression and validate that ordering
for the selected v6 driver.

## Fresh bindings, minimal setup, no v5 reuse

Use a **new private disposable GitHub repository**, with `p5-eval` at exact B1
`884bd64264df1515bee76a63f548db9cabe25a35`. Its locator is deliberately unbound
until setup; do not invent a created repository or current effect authorization.
A fresh repository avoids exposing v5's partial PR and history to the new worker.
Only the frozen v1 fixture enters it. Do not merge PRs or advance the base.

Allocate a new primary issue for each slot as needed. Preserve v1's exact body
construction, including the `P5 v1 / <task> / <slot>` heading; v6 is the campaign
metadata, not a task-text rewrite. Capture/entitle the new issue snapshot and
record new Work Unit, source and canonical criteria-only Contract identities.
Fresh state is not a clone/reset of the consumed v5 database. Each started slot
has its own Attempt, dedicated workspace, submission key and native run.

Use new v6 state/home/container resources and new evidence directories outside
execution workspaces, for example `evaluation/p5/runs/2026-09-19-v6-setup/` and
`evaluation/p5/runs/2026-09-19-v6-r01/`. These are intended paths, not existing
setup evidence. Record actual absolute state/store/source/workspace paths and
DirectTarget origin. Do not reuse v5 target mounts, provider sessions, run state,
source checkout, task issue, start marker, credentials file or partial PR refs.
Retain capacity for v5 quarantine as well as the new bounded cohort.

The existing `v5/setup_r01.py` and `v5/run_r01.py` are historical/reference tooling,
not runnable v6 entrypoints. The driver pins the old protocol, baseline, issue,
repository, paths and consumed slot. Under the single next R01 issue, add only the
minimal evaluation-only adaptation needed to bind v6 inputs and preserve its
actual-target pre-admission check, start guard and reconnection/evidence behavior.
Keep old files unchanged. Do not bypass guards by resetting v5 state or treating
a constants-only edit as validated setup. No generic campaign framework or product
refactor is required. This preparation adds no execution driver.

Before the first v6 admission, the one R01 issue must retain:

1. The immutable v6 freeze, compatible tested baseline/helper identity, eight-slot
   zero-start allocation, new exact repository/issue/Contract/B1 authority and
   pristine store with no prior admission/Attempt/submission.
2. Actual new container/image/origin and in-container CLI compatibility, native
   discovery and initially empty run inventory, selected uniform runtime,
   target-to-gateway DNS/TLS/reachability, and current ephemeral gateway/delivery
   credential availability with authorized repository access. Keep secrets out of
   records. No paid inference or extra provider task is a setup probe.
3. Active owner supervision/external stop capability, isolation from prior outputs
   and hidden judge/reference material, sufficient quarantine capacity, and
   independent exact-revision judging/Git retention arrangements.

Use v3's minimal operational policy: no P5-specific dollar/wall-time ceiling,
formal signature, exhaustive sandbox inventory or archive bureaucracy. Record
unavailable telemetry honestly rather than introducing a new product gate.

**Next gate:** finish and record those non-provider setup facts, obtain fresh
owner authorization for **v6 R01 only**, then follow the frozen protocol once.
Until then, do not admit, provision an Attempt or dispatch. On R01 completion,
retain success or an honest FAIL/BLOCKED settlement, update #62, and stop before
R02. A failure never authorizes another attempt or an automatic new cohort.
