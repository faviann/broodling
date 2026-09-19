# First single-host installation

This is the [#77](https://github.com/faviann/broodling/issues/77) deployment of
the [#76 invocation API](../docs/implementation/invocation.md), under the
[#74 first-use decision](https://github.com/faviann/broodling/issues/74).
Use it for **operator-supervised internal PR proposals**. P5 remains **FAIL**:
the demonstrated delivery path accepted a semantically incorrect change.
Every delivered PR needs independent human/operator review of its **exact
accepted revision**, complete frozen request and admitted Contract, considering
appropriate tests/CI, before a separate merge decision. Native review,
automated checks and Broodling `SUCCEEDED` do not authorize merge, deployment,
release, or reliance on semantic correctness. This review is outside Broodling.

## Selected profile

One Linux x86-64 host runs the Broodling CLI as an unprivileged dedicated account
and one rootful Docker container runs Zeroshot DirectTarget. The native target
is unauthenticated; only `127.0.0.1:18770` is published. Local users with access
to that port are trusted. Do not expose it through a public proxy or network
bind. Docker access itself grants substantial host authority.

The target runs as root **inside the container**, matching the demonstrated
profile. Zeroshot allocates its own Linux capsule identities, changes file
ownership and starts provider processes under those identities. Preserve Docker's
default capabilities; running the target with `--user`, rootless Docker, or a
restricted UID/GID mapping is outside this profile. Do not mount the Docker
socket, host credentials, or source/state directories other than the two mounts
created by the installer into the target.

| Component | Pin |
| --- | --- |
| Broodling | Package `0.1.0` plus the full Git commit supplied to `--revision`; recorded in `installation.json` |
| Python / SQLite | Debian 13 Python 3.13, SQLite 3.37+; installed patch versions recorded |
| SDK / native engine | `10.3.0.post1` / `10.3.0`, official wheel URL and SHA-256 in `pyproject.toml`; installer checks bundled native SHA-256 |
| Target base / Node | `node:22-bookworm-slim` at digest `83f487e0a63425e5b4d146fb5e5be574bcbe1b7b843d3ebafdd95eaf7767a7e5`; Node `22.23.2` |
| Codex | npm `@openai/codex@0.153.4` |
| Target GitHub CLI | Official amd64 `2.101.0` package, SHA-256 `f876a3b87bf67c94f773d17becca4dc7340b056dab901473a9260ee2a73e237b` |
| Build tools | pip `25.1.1`, setuptools `80.9.0`, wheel `0.45.1` |
| Execution | Native standard `software-change`, DirectTarget, one `UniformRuntime`, Codex / `gateway` / `gpt-5.6-sol` / medium |
| Gateway | Exactly `https://cliproxy.local.faviann.com/v1`, with current `GATEWAY_API_KEY` |

The product Dockerfile preserves the compatible
[P5 target dependencies](../evaluation/p5/direct-target-gh-compatibility.md).
OS packages receive distribution updates; rebuilds are not claimed to be
bit-identical. Each installation retains and uses its exact resulting image ID.
The actual-container check validates the dependencies used by execution,
including `/usr/bin/gh api graphql --paginate --slurp`.

## Clean host or LXC baseline

Use Debian 13 amd64, durable local storage, working DNS/HTTPS access to GitHub,
the official release/npm/package endpoints and the configured gateway, and
enough disk for retained native runs and quarantined workspaces. In an LXC,
the host operator must enable Docker nesting and the UID/GID operations needed
by rootful Docker. This package does not provision the hypervisor or gateway.
The gateway must offer `gpt-5.6-sol` through its Responses API.

As host administrator:

```bash
apt-get update
apt-get install -y python3 python3-venv python3-pip git gh ca-certificates docker.io
systemctl enable --now docker
useradd --create-home --shell /bin/bash broodling
usermod -aG docker broodling
install -d -o broodling -g broodling -m 0700 /srv/broodling
su - broodling
```

All subsequent host commands run as `broodling` in a fresh login with its Docker
group membership. Keep its home and installation inaccessible to other users.
Do not install into `/tmp`, `/run`, `/dev/shm`, a source checkout, or a symlinked
path. Use one operator at a time; this profile provides no scheduling or global
cross-Work-Unit concurrency policy.

Clone this repository and select the reviewed **full commit ID**, then install:

```bash
git clone https://github.com/faviann/broodling.git ~/broodling-source
cd ~/broodling-source
BROODLING_REVISION=FULL_REVIEWED_40_CHARACTER_COMMIT_ID
git checkout --detach "$BROODLING_REVISION"
python3 deployment/install.py --root /srv/broodling \
  --revision "$BROODLING_REVISION" --container broodling-target --port 18770
docker start broodling-target
/srv/broodling/bin/check-target
```

`install.py` archives only committed package/deployment files, installs a
non-editable release, initializes the store and creates a stopped container.
It submits no work and reads no credentials. Repeating it with the same root,
revision, account, container and port returns the existing inventory; it never
replaces a container, reinitializes a database or upgrades an installation.
A differing or incomplete installation is refused. Preserve a partial
installation for inspection before choosing a new empty root/container.

`check-target` must pass before initial submission. It checks the running
container's image, mounts, local port, dependency versions/hashes and native
discovery response. It does not call a provider or prove credentials, model
availability, repository permissions, or semantic quality. See the retained
[deployment validation](validation.md) for the actual exercised installation.

## Credentials and authority

The operator supplies `GH_TOKEN`, `GATEWAY_API_KEY` and
`GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1` to the **CLI process**
for first dispatch or an ambiguous dispatch replay. Use an existing secret
source or interactive shell input; do not put values in argv, Git, config.json,
Docker environment configuration, or shared shell history. For example:

```bash
read -r -s -p 'GitHub token: ' GH_TOKEN; echo
read -r -s -p 'Gateway key: ' GATEWAY_API_KEY; echo
export GH_TOKEN GATEWAY_API_KEY
export GATEWAY_BASE_URL=https://cliproxy.local.faviann.com/v1
```

The GitHub identity must read the explicit issue/repository and clone/fetch,
push a proposal branch, open/update its PR, and perform native delivery's issue
read/comment and PR/status/check queries. Use repository-scoped credentials
where available; a classic token with `repo` was used in the demonstrated
private-repository profile. Permission to execute the task and the exact target
branch still comes from the operator's admitted effect, independently of broad
credential privileges. There is no merge effect. Disable automatic merge and
downstream release triggered solely by this profile's success.

No node-local Codex OAuth, `auth.json`, alternate provider or runtime is selected.
Secrets are passed through the existing SDK at dispatch and are not part of
Broodling's frozen request. Native execution/session state can contain credentials
or private task content: protect and back it up accordingly. This package is
not a secret broker or a hostile-host security boundary.

## First Work Unit

Choose one self-contained GitHub issue whose entire request is a supported
software change and one explicitly authorized PR. All prerequisites must be
available within this profile. Linked documents and comments are not implicitly
entitled. Use the Python ingress API for separately entitled sources or a typed
proposer that represents additional obligations/prerequisites; do not remove an
unsupported requirement to make admission pass.

Set these values for the chosen issue and exact target branch:

```bash
REPOSITORY=OWNER/REPOSITORY
ISSUE=123
TARGET_BRANCH=main
mkdir -p /srv/broodling/requests
gh repo clone "$REPOSITORY" /srv/broodling/repositories/first
git -C /srv/broodling/repositories/first checkout "$TARGET_BRANCH"
git -C /srv/broodling/repositories/first rev-parse HEAD
gh api --hostname github.com --method GET \
  --header 'Accept: application/vnd.github+json' \
  --header 'X-GitHub-Api-Version: 2022-11-28' \
  "/repos/$REPOSITORY/issues/$ISSUE" > /srv/broodling/requests/first.json
cp /srv/broodling/release/deployment/reviewed_issue.py /srv/broodling/requests/first.py
```

Review `first.json` in full. The example caller-owned proposer preserves its
complete title/body as a criterion and refuses if the acquired issue bytes have
changed. It is only appropriate after the operator confirms that no unsupported
obligations, unresolved prerequisites or additional effects exist. Deterministic
admission cannot prove that natural-language interpretation was complete.
Proposer files are trusted operator Python code.

The source checkout must be clean and committed, have a matching GitHub origin,
and use the supported Git checkout profile (no transformations/filters). B1 must
already be fetchable from GitHub by the target; a local-only commit cannot be
cloned by DirectTarget. Record and pass its full commit ID:

```bash
B1=$(git -C /srv/broodling/repositories/first rev-parse HEAD)
/srv/broodling/bin/check-target
/srv/broodling/bin/broodling submit \
  --repository "$REPOSITORY" --issue "$ISSUE" \
  --checkout /srv/broodling/repositories/first --revision "$B1" \
  --target-branch "$TARGET_BRANCH" --proposer /srv/broodling/requests/first.py \
  > /srv/broodling/first-submission.json
unset GH_TOKEN GATEWAY_API_KEY GATEWAY_BASE_URL
```

Retain `revision.contract_revision_id` and, when admitted, `attempt.attempt_id`
from the JSON response. A rejected admission has no dispatched Attempt; inspect
the recorded decision/findings. On an error, discover retained identifiers with
`history` before deciding on the permitted recovery below. Command JSON is a
view of the existing domain records, not a new result ledger. Source bytes are
base64 encoded and Contract meaning is included for inspection.

```bash
/srv/broodling/bin/broodling history --repository "$REPOSITORY" --issue "$ISSUE"
/srv/broodling/bin/broodling status CONTRACT_REVISION_ID
/srv/broodling/bin/broodling wait ATTEMPT_ID > /srv/broodling/first-disposition.json
```

`wait` consumes the bound native result and commits the existing atomic Broodling
disposition. `SUCCEEDED` includes the exact accepted Git revision and full PR
delivery receipt. Use those retained facts for independent review; do not review
only a moving branch tip. Retain a Git bundle or another repository-side copy
of that exact revision if PR refs may disappear.

## Operations and recovery

There is no Broodling daemon. A CLI process owns its store only while running;
Zeroshot owns native execution. Losing SSH or cancelling `wait` detaches the
caller and does not cancel the Work Unit. Reopen the store with the commands
below; do not reconstruct a new submission from a conversation.

| Operation | Procedure |
| --- | --- |
| Start target after host boot | `docker start broodling-target`, then `/srv/broodling/bin/check-target` |
| Target status/log inspection | `docker inspect --format '{{.State.Status}}' broodling-target`; `docker logs --tail 100 broodling-target` (private output) |
| Retained semantic status | `broodling status CONTRACT_REVISION_ID` or `history --repository OWNER/REPO --issue N`; no native refresh or credentials |
| Native progress | Use the installed SDK `Client`/`DirectTarget` `get_run(RUN_ID).status()`; example below |
| Continue an interrupted caller | `broodling resume CONTRACT_REVISION_ID`; before first Attempt admission also supply `--checkout PATH --revision B1` |
| Consume/reconsume result | `broodling wait ATTEMPT_ID`; after durable run binding, dispatch credentials are unnecessary |
| Stop this Work Unit | `broodling stop ATTEMPT_ID --reason 'operator reason'`; abandonment is durable, native stop requested when possible, dispatched quarantine refusal is expected |
| Stop/restart target process | `docker stop broodling-target` / `docker restart broodling-target`; affects all its native runs; does not grant cleanup authority |

Use `/srv/broodling/bin/broodling` for the abbreviated commands in the table.
For native progress, inspect the already-bound run without submitting work:

```bash
/srv/broodling/venv/bin/python - RUN_ID <<'PY'
import asyncio, sys
from zeroshot import Client, DirectTarget
async def inspect():
    async with Client(target=DirectTarget("http://127.0.0.1:18770"), environment={}) as client:
        run = client.get_run(sys.argv[1])
        print(await run.status())
asyncio.run(inspect())
PY
```

After durable Attempt/run correlation, `resume`/`wait` use the stored target and
run even if caller settings change; terminal disposition replay needs no target.
Before durable acknowledgment, replay can require current dispatch credentials,
but reuses the frozen request/key. Use `resume` on the **same revision**. Repeating
`submit` reacquires issue bytes and may make a different Contract revision.

A target process/host restart preserves completed native results. Native
nonterminal runs interrupted by target loss may become `RuntimeLost`; Zeroshot
does not resume those provider processes. Reconnect to the existing run and
consume its failure/abandonment. Do not silently dispatch another run. A new
container with empty state at the old origin cannot recover the old run. Never
replace mounts or repoint an active installation at another target.

Transport loss alone is not a success or failure disposition: restore access to
the same target and wait again. Native failure abandons authority and returns an
error; inspect retained abandonment. A stop after dispatch normally exits with
`CessationUnconfirmed` even after native stop: physical cessation is not proven.
If native access is unavailable, retain abandonment and use host/Docker control
for emergency containment. No terminal label permits automatic workspace reuse.

## Retention and limitations

| Path under `/srv/broodling` | Role and retention |
| --- | --- |
| `state/broodling.sqlite3` and SQLite sidecars | Authoritative sources, Contract revisions, admission, B1/Attempt lineage, submission/run correlation, receipt/disposition/abandonment; retain |
| `repositories/` | Source Git common directories backing Attempt worktrees and original B1 objects; retain at the same absolute paths |
| `attempts/` | Dedicated Broodling worktrees; **all dispatched Attempts stay quarantined**, including success, failure and stop |
| `runtime/` | SDK/native client state; persistent across caller restarts |
| `target-state/`, `target-home/` | Native runs, results, capsules, sessions and target workspaces; persistent, potentially sensitive, mixed native UID ownership; preserve |
| `installation.json`, `config.json`, `release/`, `venv/`, `bin/` and Docker image/container | Exact deployed release/configuration and operator entrypoints; retain for repeatability; inventory is not semantic authority |
| `requests/`, operator JSON exports/Git bundles | Caller proposal inputs and copies for review; SQLite remains authoritative for admitted facts |
| `build/`, package/download caches | Rebuildable packaging scratch; disposable after successful install, never a source of lifecycle authority |

Plan capacity for indefinite quarantine; there is no automatic retention cleanup.
Do not recursively chown native target storage after use: its different UIDs
belong to Zeroshot's capsule isolation.
Back up the installation, Docker image and target inventory with access restricted
as for credentials. For a consistent ordinary filesystem backup, stop caller
processes, quiesce/stop the native target (with the interruption consequences
above), and preserve ownership, SQLite files/sidecars and absolute paths. Restore
on the same host boundary with the same account IDs, mounts, image and origin;
check before reconnecting. Do not run a restored copy alongside the original.
Live coordinated backups, in-place upgrades, distributed takeover and lost-target
reconstruction are outside this first package.

No-effect stable completion remains unsupported because Zeroshot supplies no
stable accepted local result. This CLI packages only explicitly authorized PR
delivery, including native commit/push/open-or-update; it promises neither passing
CI nor merge/deployment. No automatic deletion/replacement of any dispatched
Attempt, fleet scheduling, additional effects, alternate model/runtime selection,
node-local OAuth, UI, general secret broker, or semantic certification is added.
External stopping/host containment does not manufacture product cleanup authority.
