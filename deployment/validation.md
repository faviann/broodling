# Issue #77 deployment validation

Validated on 19 September 2026 against implementation commit
`f87bb335ce816098b3ff797d5c4cc970e23cce9e`. Subsequent changes in this issue are
documentation/evidence only. This is one authorized deployment smoke check,
separate from all P5 cohorts. It does not change P5's scoped **FAIL** or establish
general outcome quality.

## Installation and prerequisites

Used the existing Debian 13.6 amd64 host with Python 3.13.5, SQLite 3.46.1,
Git 2.47.3 and Docker 29.8.0. Installed the documented host `gh` package
(`2.46.0`), created fresh unprivileged account `broodling77` (UID/GID 30033,
Docker group access), and an empty durable `/srv/broodling-issue77` directory.
No previous Broodling store, workspace, native target or user home was reused.
This validates the package on that host; it is not a separately provisioned
pristine OS or an arbitrary LXC configuration claim.

Transferred the committed source using a Git bundle, checked out the exact
commit as the dedicated account, and ran the documented installer with:

```bash
python3.13 deployment/install.py --root /srv/broodling-issue77 \
  --revision f87bb335ce816098b3ff797d5c4cc970e23cce9e \
  --container broodling-issue77-target --port 18770
docker start broodling-issue77-target
/srv/broodling-issue77/bin/check-target
```

The installer downloaded and verified the pinned SDK wheel, installed the
non-editable release, built the product Dockerfile, initialized persistent state
and created the target. Repeating the same installation returned the same
inventory without rebuilding/replacing anything. The initial accidental short
revision argument was refused before installation; the full revision above was
then used. Package code was imported in isolated Python mode.

Actual target identity:

| Fact | Observed value |
| --- | --- |
| Container | `60a37f041a6cb856aac5cb16c5ea8a5e24a2de6fba6d583f97498ce8cd2b39b1` |
| Image | `sha256:cb6d96d228e5718d97da3c189b4e95c6b8036448ec07357d5f5c7c3500fbbc64` |
| Origin | `http://127.0.0.1:18770` |
| Target native / SDK | `10.3.0` / `10.3.0.post1` |
| Node / Codex / target gh | `22.23.2` / `0.153.4` / `2.101.0` |

The actual-container checker passed versions and executable hashes, native
discovery, image/config/mount/loopback identity, the required GitHub CLI grammar,
and an offline UID/GID transition. Before provider execution, authenticated
`/models` requests from both host and actual target succeeded against exactly
`https://cliproxy.local.faviann.com/v1`, advertising `gpt-5.6-sol`. Actual-target
GitHub API and `graphql --paginate --slurp` succeeded; repository push authority
and the exact `main` B1 through `git ls-remote` were confirmed. These probes
allocated no native run and performed no provider inference.

The bearer key was read at runtime from the user-supplied temporary file;
GitHub authentication came from the existing `gh auth token` login. Values were
passed through process environment/stdin only, with no fallback provider or
authentication path and no persisted credential configuration.

## Real invocation and recovery

The user explicitly authorized a fresh private repository with one small
documentation issue and an unmerged PR. Created
[`faviann/broodling-first-use-77-20260919`](https://github.com/faviann/broodling-first-use-77-20260919)
and [issue #1](https://github.com/faviann/broodling-first-use-77-20260919/issues/1),
asking for an operator-review document and README link. The complete request
was captured and used with the installed example proposer; the explicit effect
was one PR targeting `main`.

| Retained identity | Value |
| --- | --- |
| Original B1 | `78f1956d944f9cd86c500e1ee201931980830bea` |
| Work Unit | `wu-b2f08dc1f4a0235c1e4ca81a7979858d91c7418fdb3c967c410a979108cea843` |
| Contract revision | `cr-165d806a294b6c6540f04d155402a756c3eca732774cc3ba2072a65055a28821` |
| Attempt | `at-97525680c82d209529cf54d6541872b82d44e9d35b9e6066905722e0d3def018` |
| Native run | `01a0bbfb-9159-7813-80ac-7609f5b855da` |
| Accepted revision | `17cc56664291fe7e712ed1a07f722f2a34674d79` |
| Delivery | [PR #2](https://github.com/faviann/broodling-first-use-77-20260919/pull/2), opened, targeting `main`, left unmerged |

The installed CLI performed source capture, deterministic admission, B1/worktree
provisioning and dispatch. Its submit process exited, leaving native execution
detached. A reporting-only validation snippet initially used the API property
name `run_id` instead of the serialized `zeroshot_run_id`; `history` recovered
the successful submission without redispatch. Credential-free `resume` in a new
process returned the same Attempt and native run. Native status was observed as
running. A later credential-free CLI `wait` consumed the completed native PR
receipt and recorded Broodling **SUCCEEDED** at `2026-09-19T23:25:40.405690+00:00`.

Stopped the target with ordinary `docker stop`. With the target offline, another
CLI process returned the byte-equivalent parsed disposition. Restarted the same
container and mounts, passed the actual-target check again, and used native
`Run.wait()` to read the same successful receipt. Native listing contained
**exactly one run**. No competing Attempt or replacement run was created.
Active-work target-loss recovery was not deliberately induced in this smoke
check; the guide preserves pinned native `RuntimeLost` behavior as a limitation.

All commands ran under the dedicated account with a readable working directory.
An initial auxiliary SDK status probe inherited the other user's inaccessible
working directory through `sudo`; rerunning from the installation directory
resolved it without touching lifecycle authority. The documented fresh login
avoids that problem.

An independent agent reviewed the exact accepted commit against all six frozen
criteria and original B1, including the complete Git tree. No findings: only
`README.md` (+2 lines) and `docs/operator-review.md` (+7 lines) changed, preserving
the original README text. The documentation-only repository has no test/CI
infrastructure. This external check did not gate or change disposition and does
not replace the owner's separate merge decision. The PR remains open/unmerged.

During validation, the accepted Git history was retained in
`/srv/broodling-issue77/accepted-result.bundle`; source bytes, Contract, B1,
receipt/disposition and run correlation remained in its SQLite store. Native
state, home and the dispatched Broodling workspace were kept through completion
and evidence review. No dispatched workspace was deleted, replaced or reused
during validation. Historical P5 targets and evidence were untouched.

## Tests and secret handling

`python -m pytest tests -q`: **422 passed, 383 subtests passed** in 86.62 seconds.
Focused deployment/CLI tests: **22 passed, 31 subtests passed**. These cover CLI
authority/recovery/stop handback, immutable installation replay and refusals,
reviewed-source byte pins and target configuration/dependency failures. Native
stop/quarantine behavior is covered by controlled tests; this live smoke check
stopped the target only after completion.

After validation, exact credential values and their base64 forms were checked
in repository working files, newly committed Git blobs, retained installation
configuration, Broodling SQLite/exports, accepted repository data and target
logs. No supplied secret was found in repository/evidence output or generated
configuration. The native retained files inspected also contained neither value.
Only allowlisted nonsecret facts are recorded here; no credential value, digest,
raw environment, or provider transcript is included.

Remaining limits are the [first-use guide's limits](README.md#retention-and-limitations):
supervised PR proposals with external review, persistent single-host ownership,
fixed gateway/runtime, no no-effect stable completion, no dispatched cleanup or
replacement, and no automatic merge/deployment or semantic reliability claim.

## Environment cleanup

On 20 September 2026, after this evidence was committed, the disposable local
validation environment was intentionally retired. The exact container and
untagged image, `/srv/broodling-issue77` installation/state/workspaces, and
dedicated `broodling77` account/home were removed. The separate P5 containers,
images, state, run evidence and working files were not cleanup targets and were
left unchanged.

The private `faviann/broodling-first-use-77-20260919` repository remains the
only live validation resource while deletion waits for GitHub's required
`delete_repo` authorization scope.
