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
