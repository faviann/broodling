# Issue #82: DirectTarget GitHub CLI compatibility

Completed dependency validation on 2026-09-19. This is an image/preflight fix;
it defines no prospective protocol or cohort and authorizes no P5 execution.
The [retained v5 R01](runs/2026-09-19-v5-r01/README.md) remains `IF` and cannot
be rerun or replaced.

## Dependency and installation

The selected image definition remains [v3/DirectTarget.Dockerfile](v3/DirectTarget.Dockerfile).
Its build context contains only that Dockerfile and the native executable from
the pinned SDK wheel. Debian bookworm supplied `gh 2.23.0`, which rejects
`api --slurp`. The fix installs the official `gh 2.101.0` amd64 Debian package,
verified by SHA-256
`f876a3b87bf67c94f773d17becca4dc7340b056dab901473a9260ee2a73e237b`.
The [official checksum manifest](https://github.com/cli/cli/releases/download/v2.101.0/gh_2.101.0_checksums.txt)
and release API digest agree. The Debian package installs `/usr/bin/gh`, the
[absolute executable used by pinned Zeroshot](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/github.rs#L41-L45).

Source audit of Zeroshot revision `054ad3fd6c763b98d12f5b2e90830b97116561ad`
found these native `pull_request` CLI requirements:

| Path | Required CLI features |
| --- | --- |
| PR discovery/create/update, ref confirmation, source-issue read/comment | `api`, `--method GET/POST/PATCH`, `-f/--raw-field`, `-F/--field` |
| [GraphQL status/policy](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/github/policy.rs#L190-L217) | `api graphql --paginate --slurp`, `-f`, `-F` |
| [Failed-job logs](https://github.com/the-open-engine/zeroshot/blob/054ad3fd6c763b98d12f5b2e90830b97116561ad/zeroshot/src/native_v2_delivery/github/api.rs#L52-L74) | `api --method GET --allow-escape-sequences`; native fallback omits the escape flag if unsupported |

[GitHub CLI 2.101.0's API implementation](https://github.com/cli/cli/blob/0cf1092493af067646fc5f3db9421c6a6ec9c938/pkg/cmd/api/api.go#L292-L307)
supports all these flags. Native PR delivery uses REST API calls to create and
update PRs; the separately implemented `gh pr merge` path is outside the selected
`pull_request` delivery. No delivery implementation or provider/model selection changed.

The Node base is pinned to the original build's digest
`sha256:83f487e0a63425e5b4d146fb5e5be574bcbe1b7b843d3ebafdd95eaf7767a7e5`.
The floating tag had moved, so preserving this digest avoids a Node upgrade.
The image still contains Node `v22.23.2`, Codex `0.153.4`, and Zeroshot `10.3.0`.

## Build and preflight

Use a fresh build context and tag, separate from all retained target state:

```bash
target_build_dir=$(mktemp -d)
cp evaluation/p5/v3/DirectTarget.Dockerfile "$target_build_dir/Dockerfile"
cp .venv/lib/python3.13/site-packages/zeroshot/_bin/zeroshot "$target_build_dir/zeroshot"
docker build --platform linux/amd64 \
  -t broodling-p5-direct-target:zeroshot-10.3.0-codex-0.153.4-gh-2.101.0 "$target_build_dir"
.venv/bin/python evaluation/p5/verify_direct_target_gh.py \
  --image broodling-p5-direct-target:zeroshot-10.3.0-codex-0.153.4-gh-2.101.0
```

The Dockerfile checks the native command grammar during the build.
[verify_direct_target_gh.py](verify_direct_target_gh.py) records the installed
version and checks `/usr/bin/gh api graphql --paginate --slurp --help`, including
the advertised flag. `--image` uses disposable containers with networking
disabled, a read-only filesystem, no credentials or mounts, and an overridden
entrypoint; it never starts the target server or a provider. Exit 1 retains
the observed version and refusal facts without subprocess stderr.

For an already-running selected target, use `--container CONTAINER_ID`.
[The driver](v5/run_r01.py) invokes the same verifier before credentials/network
preflight, the start marker, slot counting, or Contract admission. It includes
the result in `direct_target_github_cli` and includes those facts in refusal
output. Existing consumed-slot and baseline guards still refuse v5 reuse.
Every separately reviewed future driver must retain this actual-container check
at its pre-admission boundary; an earlier image check alone is insufficient.

## Result and validation

Built local tag:
`broodling-p5-direct-target:zeroshot-10.3.0-codex-0.153.4-gh-2.101.0`

Image identity:
`sha256:3511d1b7134167b6a845bdc0536532a2265cba580e77b475e26d25d0382f4e5a`

Platform: `linux/amd64`. No persistent target was started or deployed.
The installed `/usr/bin/gh` reports `gh version 2.101.0 (2026-09-15)`;
its executable SHA-256 is
`ea857a3f0f7d4276cf5848b236542c5048e2eaa7bdd1b6ddec238f8793e74bff`.
The native executable hash remains
`afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06`.
The installed SDK remains `10.3.0.post1`; its cached official wheel still matches
the `pyproject.toml` digest
`f3629459837a27b7496f98fe0034e7b47a00079d93d3374922c2960952b8ace9`.

| Validation | Result |
| --- | --- |
| Same verifier against retained image `sha256:bfed075c6443f63127dc30f73376483148d78bf3023d093bd90ce76353344dad` in disposable offline containers | Exit 1; records `gh 2.23.0`, `api_paginate_slurp=false` |
| Verifier against rebuilt image above | Exit 0; records `gh 2.101.0`, `api_paginate_slurp=true` |
| Rebuilt `/usr/bin/gh api --help` and offline help invocation with all audited flags | All required flags exposed and accepted |
| `.venv/bin/python -m pytest tests/test_direct_target_gh.py -q` | 5 passed, 3 subtests; includes real `start()` boundary refusing the incompatible fixture before counting/admission or evidence/store writes |
| `.venv/bin/python -m pytest tests -q` | 356 passed, 270 subtests; no live provider inference |

Retained v5 evidence, the quarantined Attempt/store/workspace, target state/home,
stopped target container and original image remain unchanged. PR #2 remains open
and unmerged at `64213ae12edcd571bc313bbe7920b66dcd6c17d8`.
Before/after file-content checks matched for both tracked v5 evidence packages
and all regular files under `/home/faviann/.local/share/broodling-p5-v5`.
Container state/image and PR state/head/update timestamp also matched.
The read-only v5 driver `check` still exits 1 with
`R01 start was already recorded; no rerun permitted`.

Before another prospective P5 run: separately review a new protocol/cohort and
compatible baseline, bind the new target identity and fresh run inputs/state,
retain the actual-target compatibility and other required preflight results,
and obtain fresh live authorization. This issue neither defines that cohort
nor resumes v5 or repairs its partial PR.
