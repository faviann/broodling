# R01 setup helper

`setup_r01.py` is evaluation tooling. It reconstructs the frozen two-file B1,
initializes eight `NOT_STARTED` slots, and prepares only R01's primary issue and
immutable source/Work Unit/Contract records. It never admits an Attempt, runs a
provider, or opens a PR. The separate execution step must account for the first
admission before calling the product API.

From the repository root, using the supported Python environment:

```bash
.venv/bin/python evaluation/p5/v3/setup_r01.py \
  --evidence "$PWD/evaluation/p5/runs/2026-09-17-v3-r01" \
  --state-dir /home/faviann/.local/share/broodling-p5-v3/r01-inputs \
  --repository faviann/broodling-p5-v3-20260917 \
  --provision-github
```

Omit `--provision-github` for local fixture/evidence initialization only. The
explicit flag authorizes this invocation to create the named private disposable
repository, publish B1 to `p5-eval`, and create the exact frozen R01 issue. Git
obtains authentication from the configured `gh` credential helper; this script
does not read or retain credentials. Existing resources must match their recorded
identities and bytes. An existing repository must have the helper's disposable
fixture description. Existing Git history is never reset, and duplicate matching
R01 issues cause refusal.

`setup.json` records source, branch, protocol and storage identities.
`github-repository.json` and `R01/issue-readback.json` retain GitHub readbacks.
`R01/input-records.json` points to the durable database, source and workspace root;
the adjacent files retain source entitlement, Work Unit identity and canonical
Contract bytes. The source checkout has only the frozen task files; no judge,
reference implementations or campaign records enter it.

Setup may be rerun before admission to verify the same material. It refuses to
amend an evidence directory once any slot has left `NOT_STARTED`. A failed or
interrupted preparation does not consume a slot, but conflicting partial material
must be investigated rather than overwritten. The live driver must preserve
first-admission accounting and the frozen no-rerun rule independently.

## Target preparation used for #67

These are operator setup commands, not Broodling features. The build context
contains only the Dockerfile and pinned native executable. The image installs
Codex 0.153.4; the retained run record identifies the actual image digest and
package versions. No worker checkout or judging material is mounted.

```bash
mkdir -p /home/faviann/.local/share/broodling-p5-v3/target-build
mkdir -p /home/faviann/.local/share/broodling-p5-v3/target-state
mkdir -p /home/faviann/.local/share/broodling-p5-v3/target-home/.codex
chmod 700 /home/faviann/.local/share/broodling-p5-v3/target-home/.codex
cp .venv/lib/python3.13/site-packages/zeroshot/_bin/zeroshot \
  /home/faviann/.local/share/broodling-p5-v3/target-build/zeroshot
cp evaluation/p5/v3/DirectTarget.Dockerfile \
  /home/faviann/.local/share/broodling-p5-v3/target-build/Dockerfile
docker build -t broodling-p5-v3-target:codex-0.153.4 \
  /home/faviann/.local/share/broodling-p5-v3/target-build
docker run -d --name broodling-p5-v3-r01-target \
  --publish 127.0.0.1:18767:18767 \
  --mount type=bind,src=/home/faviann/.local/share/broodling-p5-v3/target-state,dst=/p5-state \
  --mount type=bind,src=/home/faviann/.local/share/broodling-p5-v3/target-home,dst=/home/node \
  broodling-p5-v3-target:codex-0.153.4 \
  --listen 0.0.0.0:18767 --public-origin http://127.0.0.1:18767 --storage /p5-state
```

The supervised setup also copied the available `auth.json` into the isolated
home with mode 0600, without reading/logging its values, and checked
`docker exec broodling-p5-v3-r01-target codex login status`. This confirmed CLI
ChatGPT authentication only. It does **not** satisfy the native hosted provider
connection. Do not use a successful login check as permission to dispatch.

Use `verify_provider_auth.py` for the reproducible, non-secret credential-path
check. Target discovery, SDK `Client(DirectTarget(origin), environment={}).list_runs()`
and version checks are read-only; they do not start a provider task. The retained
target verification contains the exact commands and outputs. Direct-mode native
connection listing was also checked through temporary client configuration and
refused because this target does not advertise connection management.

After the empty inventory was retained, the operator ran
`docker stop broodling-p5-v3-r01-target` and removed the unused isolated auth copy.
The stopped container, image and state remain available for inspection. Do not
rerun the `docker run` command over that retained container; inspect it first.
Any future use still requires resolving #69 and satisfying current prerequisites.
